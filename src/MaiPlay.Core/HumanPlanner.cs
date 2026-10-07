using static MaiPlay.Core.ContactTrajectoryCompiler;

namespace MaiPlay.Core;

public sealed record HumanPlannerOptions
{
    public int BeamWidth { get; init; } = 64;
    public int VerificationCandidates { get; init; } = 4;
    public HumanMotionModel MotionModel { get; init; } = new();
}

/// <summary>在同一次搜索中组合各段打法，最终成绩由完整输入回放决定。</summary>
public static class HumanPlanner
{
    private const double Epsilon = 1e-8;
    private sealed record Action(int DemandId, ChartNote Note, HandSide Hand, InputChannel Channel,
        HandFrame[] Frames, double Start, double End, string Technique, int Pattern, int Direction, int Branch)
    {
        public ChartPoint Last => Frames[^1].Position;
    }
    private sealed record State(Action? Left, Action? Right, bool Crossed, int Skipped, double Cost,
        State? Parent, Action? Added, GameplayDemand? Gap);
    private sealed record Motion(HandFrame[] Frames, double Cost, bool Feasible);

    public static HandPlan Plan(Chart chart, CancellationToken cancellationToken = default) =>
        Plan(chart, new HumanPlannerOptions(), cancellationToken);

    public static HandPlan Plan(Chart chart, HumanPlannerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BeamWidth < 1 || options.VerificationCandidates < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "搜索宽度和核验候选数必须为正数。");
        ArgumentNullException.ThrowIfNull(options.MotionModel);
        var best = PlanPass(chart, options, null, cancellationToken);
        var shifts = new Dictionary<int, double?>();
        for (var attempt = 0; attempt < 2 && !best.AllCriticalPerfect; attempt++)
        {
            var feedback = PlannerTimingFeedback.DelayedContacts(best);
            if (feedback.Count == 0) break;
            var before = best;
            var accepted = shifts;
            foreach (var restoreOriginalTiming in new[] { true, false })
            {
                var hints = new Dictionary<int, double?>(shifts);
                foreach (var (id, start) in feedback) hints[id] = restoreOriginalTiming ? null : start;
                var adjusted = PlanPass(chart, options, hints, cancellationToken);
                if (!Better(adjusted, best)) continue;
                best = adjusted;
                accepted = hints;
            }
            if (ReferenceEquals(before, best)) break;
            shifts = accepted;
        }
        return best;
    }

    private static HandPlan PlanPass(Chart chart, HumanPlannerOptions options,
        IReadOnlyDictionary<int, double?>? shiftHints, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var demands = GameplayDemandCompiler.Compile(chart, cancellationToken);
        var doubleTimes = chart.Notes.Where(n => n.Kind != NoteKind.Slide).GroupBy(n => n.Time)
            .Where(g => g.Count() == 2).Select(g => g.Key).ToHashSet();
        var model = options.MotionModel;
        var motions = CreateMotions(chart, cancellationToken).GroupBy(m => m.Note.Id).ToDictionary(g => g.Key,
            g => g.Select(m => PrepareMotion(m, model)).ToArray());
        var patterns = PatternAnalyzer.Analyze(demands, d => motions[d.Id][0].Frames[0].Time);
        var sweepMotions = new Dictionary<(int Id, int Direction), Motion>();
        var beam = new List<State> { new(null, null, false, 0, 0, null, null, null) };
        for (var patternIndex = 0; patternIndex < patterns.Count; patternIndex++)
        {
            var pattern = patterns[patternIndex];
            var alternatives = new List<State>();
            foreach (var technique in TechniqueCandidates.For(pattern))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var local = beam.Select(s => s with { Cost = s.Cost + technique.PreferenceCost }).ToList();
                foreach (var index in technique.DemandOrder ?? Enumerable.Range(0, pattern.Demands.Count).ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var demand = pattern.Demands[index];
                    var direction = technique.SweepDirections?[index] ?? technique.SweepDirection;
                    var motion = motions[demand.Id];
                    if (direction != 0)
                    {
                        var key = (demand.Id, direction);
                        if (!sweepMotions.TryGetValue(key, out var sweep))
                        {
                            sweep = Prepare(SweepFrames(demand, direction, chart), model);
                            sweepMotions.Add(key, sweep);
                        }
                        motion = [sweep];
                    }
                    var motionVariants = MotionVariants(demand, motion, model);
                    var next = new List<State>();
                    foreach (var state in local)
                    {
                        var hands = technique.Hands[index] is { } required ? new[] { required } : [HandSide.Left, HandSide.Right];
                        foreach (var hand in hands)
                            foreach (var variant in motionVariants)
                            foreach (var shift in shiftHints is not null && shiftHints.TryGetValue(demand.Id, out var contactStart)
                                         ? new[] { contactStart is { } requestedStart ? requestedStart - variant[0].Frames[0].Time : 0 }
                                         : Shifts(demand, direction))
                            {
                                var candidate = state;
                                for (var branch = 0; branch < variant.Length; branch++)
                                {
                                    var branchHand = branch == 0 ? hand : Other(hand);
                                    candidate = Assign(candidate, demand, variant[branch], branchHand, shift,
                                        technique.Name, patternIndex, direction, branch,
                                        doubleTimes.Contains(demand.Note.Time), model);
                                    if (candidate is null) break;
                                }
                                if (candidate is not null) next.Add(candidate);
                            }
                        // 时机反馈先保留被修正的接触，避免用丢弃 Slide 代替修正其误触。
                        if (shiftHints is null || !shiftHints.ContainsKey(demand.Id))
                            next.Add(Skip(state, demand, motion.Length));
                    }
                    if (next.Count == 0) next.AddRange(local.Select(s => Skip(s, demand, motion.Length)));
                    local = Prune(next, options.BeamWidth);
                }
                alternatives.AddRange(local);
            }
            beam = Prune(alternatives, options.BeamWidth);
        }

        HandPlan? best = null;
        foreach (var state in beam.Take(options.VerificationCandidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = Verify(chart, state, patterns, model, cancellationToken);
            if (best is null || Better(plan, best)) best = plan;
        }
        return best!;
    }

    private static IEnumerable<double> Shifts(GameplayDemand demand, int direction)
    {
        if (direction != 0) return [0];
        return demand.Note.Kind switch
        {
            NoteKind.Touch or NoteKind.TouchHold => [0, -.075, .075, -.125, .125],
            NoteKind.Slide => [0, -.06, .06, -.12, .12],
            _ => [0, -.0125, .0125]
        };
    }

    private static State Skip(State state, GameplayDemand demand, int actions) =>
        new(state.Left, state.Right, state.Crossed, state.Skipped + actions, state.Cost, state, null, demand);

    private static Motion Prepare(HandFrame[] frames, HumanMotionModel model)
    {
        double cost = 0;
        for (var i = 1; i < frames.Length; i++)
        {
            var distance = model.Geometry.DistanceMillimeters(frames[i - 1].Position, frames[i].Position);
            var time = frames[i].Time - frames[i - 1].Time;
            if (time <= 0 || distance > time * model.MaxSpeedMillimetersPerSecond + Epsilon)
                return new(frames, 0, false);
            cost += distance / 1000 + distance * distance / (time * model.PreferredSpeedMillimetersPerSecond * model.PreferredSpeedMillimetersPerSecond);
        }
        return new(frames, cost, true);
    }

    private static Motion PrepareMotion(ContactMotion motion, HumanMotionModel model)
    {
        var frames = ContactFrames(motion.Points, motion.Channel, 0);
        var prepared = Prepare(frames, model);
        if (prepared.Feasible || motion.Note.Kind != NoteKind.Slide || motion.Note.SlidePath!.IsWifi) return prepared;
        var peak = frames.Zip(frames.Skip(1), (a, b) =>
            model.Geometry.DistanceMillimeters(a.Position, b.Position) / (b.Time - a.Time)).Max();
        var scale = peak / model.MaxSpeedMillimetersPerSecond * 1.001;
        // 短轨道可以在已有的时机搜索范围内延长，整曲判定仍须实际通过。
        if ((frames[^1].Time - frames[0].Time) * (scale - 1) > .12) return prepared;
        return Prepare(frames.Select(f => f with { Time = frames[0].Time + (f.Time - frames[0].Time) * scale }).ToArray(), model);
    }

    private static Motion[][] MotionVariants(GameplayDemand demand, Motion[] original, HumanMotionModel model)
    {
        if (demand.Note.Kind is not (NoteKind.Hold or NoteKind.TouchHold)) return [original];
        var frames = original[0].Frames;
        var end = Math.Max(frames[0].Time + .025, frames[^1].Time - .04);
        if (end >= frames[^1].Time - Epsilon) return [original];
        var shortened = Prepare([frames[0], frames[^1] with { Time = end }], model);
        // 提前释放只是候选，头尾宽限和持续比例仍由原判定器核验。
        return [original, [shortened with { Cost = shortened.Cost + .1 }]];
    }

    private static State? Assign(State state, GameplayDemand demand, Motion motion, HandSide hand, double shift,
        string technique, int pattern, int direction, int branch, bool twoHandChord, HumanMotionModel model)
    {
        if (!motion.Feasible) return null;
        var before = hand == HandSide.Left ? state.Left : state.Right;
        var other = hand == HandSide.Left ? state.Right : state.Left;
        var start = motion.Frames[0].Time + shift;
        var end = motion.Frames[^1].Time + shift;
        var first = motion.Frames[0].Position;
        var continues = direction != 0 && before is not null && before.Pattern == pattern && before.Direction == direction;
        var from = before?.Last ?? Rest(hand);
        var departure = before?.End ?? Math.Min(0, start - 1);
        var distance = model.Geometry.DistanceMillimeters(from, first);
        HandFrame[] approach;
        if (continues)
        {
            from = before!.Frames.First(f => Math.Abs(f.Time - before.Start) < Epsilon).Position;
            departure = before.Start;
            var angle = Math.Atan2(from.Y, from.X);
            var sweep = RotationAngle(from, first, direction);
            distance = 1.12 * model.Geometry.JudgementRadiusMillimeters * sweep;
            var count = Math.Max(2, (int)Math.Ceiling((start - departure) * PlaybackSession.LogicHz));
            approach = Enumerable.Range(0, count).Select(i =>
            {
                var point = Ring(angle + direction * sweep * i / count);
                return new HandFrame(departure + (start - departure) * i / count, point, Fingers(point, InputChannel.Button));
            }).ToArray();
        }
        else
        {
            if (start - departure < PlaybackSession.LogicStep - Epsilon) return null;
            // 搜索和轨迹生成使用同一段移动时间，空闲时按舒适速度提前出发。
            var travel = Math.Max(PlaybackSession.LogicStep, distance / model.PreferredSpeedMillimetersPerSecond);
            departure = Math.Max(departure, start - travel);
            approach = [new(departure, from, [])];
        }
        if (start <= departure || distance > (start - departure) * model.MaxSpeedMillimetersPerSecond + Epsilon) return null;
        var otherPosition = other is null ? Rest(Other(hand)) : HumanMotionModel.PositionAt(other.Frames, start);
        var transition = model.EvaluateTransition(hand, from, departure, first, start, otherPosition, distance);
        if (!transition.Feasible) return null;
        // 同刻普通双押保留双手；被持续占用时，Touch 可以在窗口内依次击打。
        if (twoHandChord && before is not null && before.Note.Kind != NoteKind.Slide && demand.Note.Kind != NoteKind.Slide &&
            Math.Abs(before.Note.Time - demand.Note.Time) < Epsilon &&
            !(before.Note.Kind == NoteKind.Touch && demand.Note.Kind == NoteKind.Touch && other is not null &&
              other.Start < demand.Start - .15 && other.End > demand.Start + .15)) return null;
        var contact = motion.Frames.Select(f => f with { Time = f.Time + shift }).ToArray();
        var frames = approach.Concat(contact).ToArray();
        var action = new Action(demand.Id, demand.Note, hand, demand.Channel, frames, start, end, technique, pattern, direction, branch);
        var crossingChange = transition.Crossed != state.Crossed ? .15 : 0;
        var cost = transition.Cost + motion.Cost + Math.Abs(shift) * 3 + crossingChange;
        return new(hand == HandSide.Left ? action : state.Left, hand == HandSide.Right ? action : state.Right,
            transition.Crossed, state.Skipped, state.Cost + cost, state, action, null);
    }

    private static List<State> Prune(IEnumerable<State> states, int width)
    {
        var ranked = states.OrderBy(s => s.Skipped).ThenBy(s => s.Cost)
            .DistinctBy(s => (Key(s.Left), Key(s.Right), s.Crossed)).ToArray();
        // 同一种分手的大量微小时机差异不能挤掉另一种占手布局。
        // 后续同刻的 Slide 或长按仍可能需要当前较远的那只手。
        var selected = ranked.Where(s => s.Skipped == ranked[0].Skipped)
            .DistinctBy(s => (s.Left?.DemandId, s.Right?.DemandId)).Take(width).ToList();
        var included = new HashSet<State>(selected, ReferenceEqualityComparer.Instance);
        selected.AddRange(ranked.Where(s => !included.Contains(s)).Take(width - selected.Count));
        return selected.OrderBy(s => s.Skipped).ThenBy(s => s.Cost).ToList();
    }

    private static (int?, double?, double?, int?, int?, int?, double?, ChartPoint?) Key(Action? action) =>
        (action?.DemandId, action?.Start, action?.End, action?.Direction, action?.Pattern, action?.Branch,
            action?.Frames[0].Time, action?.Frames[0].Position);

    private static HandPlan Verify(Chart chart, State state, IReadOnlyList<PatternSegment> patterns,
        HumanMotionModel model, CancellationToken token)
    {
        var actions = new List<Action>();
        var skipped = new List<GameplayDemand>();
        for (var node = state; node is not null; node = node.Parent)
        {
            if (node.Added is { } added) actions.Add(added);
            if (node.Gap is { } gap) skipped.Add(gap);
        }
        actions.Reverse();
        var tracks = BuildTracks(actions);
        var replay = new HandReplay(tracks);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, replay.Events);
        var results = new List<PlaybackEvent>();
        for (var time = Math.Min(0, chart.Notes.Select(n => n.Time).DefaultIfEmpty(0).Min() - 1); time < session.FinishTime; time += 1)
        {
            token.ThrowIfCancellationRequested();
            results.AddRange(session.Advance(time).Where(e => e.IsFinal));
        }
        token.ThrowIfCancellationRequested();
        results.AddRange(session.Advance(session.FinishTime + .1).Where(e => e.IsFinal));
        var failed = results.Where(e => e.Grade != AchievementGrade.Perfect).Select(e => e.Note.Id).ToHashSet();
        var strokes = actions.Select(a => new PlannedStroke(a.DemandId, a.Hand, a.Start, a.End, a.Channel,
            a.Direction != 0 ? HandTechnique.Rotation : a.Note.Kind == NoteKind.Slide ? HandTechnique.Slide : HandTechnique.Strike)).OrderBy(a => a.Start).ToArray();
        return new(chart, tracks, replay, strokes, results, session.Achievement.Percentage, session.DXScore,
            skipped.Where(d => failed.Contains(d.Id)).DistinctBy(d => d.Id).OrderBy(d => d.Start)
                .Select(d => new HandPlanGap(d.Id, d.Start, d.End, "占手或移动约束下未找到可衔接的动作。" )).ToArray())
        {
            Diagnostics = new(model.Analyze(tracks, strokes), state.Cost,
                patterns.GroupBy(p => p.Kind).Select(g => new PatternOutcome(g.Key, g.Count(),
                    g.Count(p => p.Demands.All(d => !failed.Contains(d.Id))))).ToArray(),
                actions.GroupBy(a => a.Technique).ToDictionary(g => g.Key, g => g.Count()))
        };
    }

    private static HandTrack[] BuildTracks(IReadOnlyList<Action> actions)
    {
        var tracks = new List<HandTrack>();
        foreach (var hand in new[] { HandSide.Left, HandSide.Right })
        {
            var ordered = actions.Where(a => a.Hand == hand).ToArray();
            if (ordered.Length == 0) continue;
            var frames = new List<HandFrame>();
            foreach (var action in ordered)
            {
                // 连续扫键从上一键的接触帧开始，替换那一键原定的抬手。
                while (frames.Count > 0 && frames[^1].Time >= action.Frames[0].Time - Epsilon) frames.RemoveAt(frames.Count - 1);
                frames.AddRange(action.Frames);
            }
            frames.Add(new(frames[^1].Time + .25, frames[^1].Position, []));
            tracks.Add(new(hand, frames));
        }
        return tracks.ToArray();
    }

    private static bool Better(HandPlan candidate, HandPlan before)
    {
        var a = candidate.Diagnostics!;
        var b = before.Diagnostics!;
        if (a.Movement.SpeedLimitViolations != b.Movement.SpeedLimitViolations) return a.Movement.SpeedLimitViolations < b.Movement.SpeedLimitViolations;
        if (candidate.Errors.Count != before.Errors.Count) return candidate.Errors.Count < before.Errors.Count;
        if (candidate.Achievement != before.Achievement) return candidate.Achievement > before.Achievement;
        if (candidate.Gaps.Count != before.Gaps.Count) return candidate.Gaps.Count < before.Gaps.Count;
        return a.MovementCost < b.MovementCost;
    }

    private static HandFrame[] SweepFrames(GameplayDemand demand, int direction, Chart chart)
    {
        var target = SlidePath.ButtonPosition(demand.Note.StartPosition);
        var centre = Math.Atan2(target.Y, target.X);
        var low = 0d;
        var high = Math.PI / 4;
        for (var i = 0; i < 24; i++)
        {
            var offset = (low + high) / 2;
            var p = Ring(centre - direction * offset);
            if (Fingers(p, InputChannel.Button).Any(f => ContactMapper.AreasAt(InputChannel.Button,
                    new(p.X + f.Offset.X, p.Y + f.Offset.Y), f.Radius).Contains(demand.Target))) low = offset;
            else high = offset;
        }
        var point = Ring(centre - direction * (low - .001));
        var covered = Fingers(point, InputChannel.Button).SelectMany(f => ContactMapper.AreasAt(InputChannel.Button,
            new(point.X + f.Offset.X, point.Y + f.Offset.Y), f.Radius)).ToHashSet();
        if (chart.Notes.Any(n => n.Kind is NoteKind.Tap or NoteKind.Hold && n.StartPosition != demand.Note.StartPosition &&
            n.Time >= demand.Start - Epsilon && n.Time <= demand.Start + HeadJudgements.EarlyInputWindow + Epsilon &&
            covered.Contains(n.StartPosition.ToString()))) point = Ring(centre);
        return [new(demand.Start, point, Fingers(point, InputChannel.Button)), new(demand.Start + .025, point, [])];
    }

    private static HandSide Other(HandSide hand) => hand == HandSide.Left ? HandSide.Right : HandSide.Left;
    private static ChartPoint Rest(HandSide hand) => new(hand == HandSide.Left ? -.5 : .5, .5);
    private static ChartPoint Ring(double angle) => new(1.12 * Math.Cos(angle), 1.12 * Math.Sin(angle));
    private static double RotationAngle(ChartPoint from, ChartPoint to, int direction) =>
        ((Math.Atan2(to.Y, to.X) - Math.Atan2(from.Y, from.X)) * direction + 2 * Math.PI) % (2 * Math.PI);
}
