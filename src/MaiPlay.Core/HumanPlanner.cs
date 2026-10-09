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
        HandFrame[] Contact, ChartPoint ApproachPosition, double ApproachTime, bool Continues,
        double Start, double End, string Technique, int Pattern, int Direction, int Branch, ContactProfile Profile)
    {
        public bool ContinuesSlide { get; init; }
        public bool FullSlide { get; init; }
        private HandFrame[]? _frames;
        public ChartPoint Last => Contact[^1].Position;
        // 剪枝只需要时位与成本；后续衔接或实际回放用到时才展开轨迹。
        public HandFrame[] Frames => _frames ??= BuildFrames();
        public HandFrame[] CollisionFrames => Continues ? Frames : Contact;

        public ChartPoint PositionAt(double time)
        {
            if (time >= Start) return HumanMotionModel.PositionAt(Contact, time);
            if (time <= ApproachTime) return ApproachPosition;
            return Continues ? HumanMotionModel.PositionAt(Frames, time) :
                ChartPoint.Lerp(ApproachPosition, Contact[0].Position, (time - ApproachTime) / (Start - ApproachTime));
        }

        private HandFrame[] BuildFrames()
        {
            if (ContinuesSlide) return Contact;
            var count = Continues ? Math.Max(2, (int)Math.Ceiling((Start - ApproachTime) * PlaybackSession.LogicHz)) : 1;
            var frames = new HandFrame[count + Contact.Length];
            if (Continues)
            {
                var angle = Math.Atan2(ApproachPosition.Y, ApproachPosition.X);
                var sweep = RotationAngle(ApproachPosition, Contact[0].Position, Direction);
                for (var i = 0; i < count; i++)
                {
                    var point = Ring(angle + Direction * sweep * i / count);
                    frames[i] = new(ApproachTime + (Start - ApproachTime) * i / count, point, Fingers(point, InputChannel.Button));
                }
            }
            else frames[0] = new(ApproachTime, ApproachPosition, []);
            Contact.CopyTo(frames, count);
            return frames;
        }
    }
    private sealed record State(Action? Left, Action? Right, bool Crossed, int Skipped, double Cost,
        State? Parent, Action? Added, GameplayDemand? Gap)
    {
        public CoveredDemand? Covered { get; init; }
    }
    private sealed record Motion(HandFrame[] Frames, double Cost, bool Feasible, HandFrame[]? EntryFrames = null,
        ContactProfile Profile = ContactProfile.Fingertips, bool FullSlide = false);
    private readonly record struct ActionKey(int? DemandId, double? Start, double? End, int? Direction,
        int? Pattern, int? Branch, double? ApproachTime, ChartPoint? ApproachPosition, ContactProfile? Profile,
        bool FullSlide, bool ContinuesSlide, HandFrame[]? Contact);
    private sealed record PreparedSearch(Dictionary<int, Motion[]> Motions, IReadOnlyList<PatternSegment> Patterns,
        HashSet<double> DoubleTimes)
    {
        public Dictionary<(int Id, int Direction), Motion> SweepMotions { get; } = new();
        public Dictionary<(int Id, int Direction), HandFrame[]> LiftedSweepEntries { get; } = new();
        public Dictionary<(HandFrame[] Frames, double Shift), HandFrame[]> ShiftedContacts { get; } = new();
        public Dictionary<int, Motion> FullSlides { get; init; } = new();
        public Dictionary<int, int> PreviousSlide { get; init; } = new();
        public Dictionary<int, ChartNote> ChainTails { get; init; } = new();
        public Dictionary<int, GameplayDemand[][]> TailInputs { get; init; } = new();
        public Dictionary<int, Motion[]> AvoidanceSlides { get; init; } = new();
    }

    public static HandPlan Plan(Chart chart, CancellationToken cancellationToken = default) =>
        Plan(chart, new HumanPlannerOptions(), cancellationToken);

    public static HandPlan Plan(Chart chart, HumanPlannerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BeamWidth < 1 || options.VerificationCandidates < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "搜索宽度和核验候选数必须为正数。");
        ArgumentNullException.ThrowIfNull(options.MotionModel);
        cancellationToken.ThrowIfCancellationRequested();
        var demands = GameplayDemandCompiler.Compile(chart, cancellationToken);
        var connections = SlideConnections.Analyze(chart);
        var previousSlides = connections.ToDictionary(c => c.NextId, c => c.PreviousId);
        var nextSlides = connections.ToDictionary(c => c.PreviousId, c => c.NextId);
        var chainIds = connections.SelectMany(c => new[] { c.PreviousId, c.NextId }).ToHashSet();
        var notesById = chart.Notes.ToDictionary(n => n.Id);
        var chainTails = chainIds.ToDictionary(id => id, id =>
        {
            while (nextSlides.TryGetValue(id, out var next)) id = next;
            return notesById[id];
        });
        var tailInputs = chainTails.Values.DistinctBy(n => n.Id).ToDictionary(n => n.Id, n => demands
            .Where(d => d.Note.Kind != NoteKind.Slide && d.Start >= n.EndTime - Epsilon)
            .GroupBy(d => Math.Round(d.Start, 7)).Take(2).Select(g => g.ToArray()).ToArray());
        var doubleTimes = chart.Notes.Where(n => n.Kind != NoteKind.Slide).GroupBy(n => n.Time)
            .Where(g => g.Count() == 2).Select(g => g.Key).ToHashSet();
        var avoidanceNotes = new HashSet<int>();
        PreparedSearch PrepareContacts(IReadOnlySet<int> small)
        {
            var motions = CreateMotions(chart, cancellationToken, small).GroupBy(m => m.Note.Id).ToDictionary(g => g.Key,
                g => g.Select(m => PrepareMotion(m, options.MotionModel)).ToArray());
            var fullSlides = chainIds.Count == 0 ? new Dictionary<int, Motion>() :
                CreateMotions(chart with { Notes = chart.Notes.Where(n => chainIds.Contains(n.Id)).ToArray() },
                    cancellationToken, small, chainIds).ToDictionary(m => m.Note.Id,
                    m => Prepare(ContactFrames(m.Points, m.Channel, 0, m.Profile), options.MotionModel)
                        with { Profile = m.Profile, FullSlide = true });
            var avoidanceSlides = new Dictionary<int, Motion[]>();
            foreach (var id in avoidanceNotes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var profile = small.Contains(id) ? ContactProfile.Fingertips : ContactProfile.Palm;
                var alternatives = new List<Motion>();
                if (CreateInsetSlideMotion(chart, notesById[id], profile) is { } inset)
                    alternatives.Add(PrepareMotion(inset, options.MotionModel));
                foreach (var bend in new ChartPoint[] { new(.2, 0), new(-.2, 0), new(0, .2), new(0, -.2) })
                    if (CreateBentSlideMotion(chart, notesById[id], profile, bend, .8) is { } bent)
                        alternatives.Add(PrepareMotion(bent, options.MotionModel));
                if (alternatives.Count != 0) avoidanceSlides[id] = alternatives.ToArray();
            }
            return new(motions, PatternAnalyzer.Analyze(demands, d => d.Note.SlidePath is { IsWifi: true }
                ? Math.Max(d.Start, motions[d.Id][0].Frames[0].Time) : motions[d.Id][0].Frames[0].Time), doubleTimes)
            {
                FullSlides = fullSlides, PreviousSlide = previousSlides, ChainTails = chainTails, TailInputs = tailInputs,
                AvoidanceSlides = avoidanceSlides
            };
        }
        var prepared = PrepareContacts(new HashSet<int>());
        var initial = PlanPass(chart, options, prepared, null, cancellationToken);
        var diagnosed = Recover(chart, options, prepared, initial, PrepareContacts, true, cancellationToken);
        if (diagnosed.IsSolved) return diagnosed;

        // 局部改善可能改变下一轮误触来源；基础路线从初始方案独立走完，保留原本可完成的音符。
        var basis = Recover(chart, options, prepared, initial, PrepareContacts, false, cancellationToken);
        var previousErrors = basis.Errors.ToDictionary(e => e.Note.Id);
        static bool IsMiss(PlaybackEvent result) => result.Kind == PlaybackEventKind.Miss ||
            result.Grade is AchievementGrade.Miss or AchievementGrade.TooFast;
        var best = !Better(diagnosed, basis) ? basis :
            diagnosed.Diagnostics!.Movement.SatisfiesMotionConstraints != basis.Diagnostics!.Movement.SatisfiesMotionConstraints
                ? diagnosed : diagnosed.Errors.Any(e => !previousErrors.TryGetValue(e.Note.Id, out var previous) ||
                    IsMiss(e) && !IsMiss(previous)) ? basis : diagnosed;
        if (best.IsSolved) return best;
        var trouble = best.Errors.Select(e => e.Note.Kind == NoteKind.Slide ? e.Note.SlideStartTime : e.Note.Time)
            .Concat(best.Gaps.Select(g => g.Time)).ToArray();
        avoidanceNotes.UnionWith(chart.Notes.Where(n => n.Kind == NoteKind.Slide && n.SlidePath is { IsWifi: false } &&
            trouble.Any(t => t >= n.SlideStartTime - .3 && t <= n.EndTime + .3)).Select(n => n.Id));
        if (avoidanceNotes.Count == 0) return best;
        var avoiding = PrepareContacts(new HashSet<int>());
        var alternate = PlanPass(chart, options, avoiding, null, cancellationToken);
        alternate = Recover(chart, options, avoiding, alternate, PrepareContacts, true, cancellationToken);
        return Better(alternate, best) ? alternate : best;
    }

    private static HandPlan Recover(Chart chart, HumanPlannerOptions options, PreparedSearch initialPrepared,
        HandPlan initial, Func<IReadOnlySet<int>, PreparedSearch> prepareContacts, bool diagnosticRecovery,
        CancellationToken cancellationToken)
    {
        var fingertips = new HashSet<int>();
        var prepared = initialPrepared;
        var best = initial;
        var shifts = new Dictionary<int, double?>();
        for (var attempt = 0; attempt < 2 && !best.IsSolved; attempt++)
        {
            var before = best;
            var smaller = new HashSet<int>(fingertips);
            smaller.UnionWith(PlannerTimingFeedback.FingertipCandidates(best));
            if (smaller.Count > fingertips.Count)
            {
                var smallerPrepared = prepareContacts(smaller);
                var adjusted = PlanPass(chart, options, smallerPrepared, shifts, cancellationToken);
                if (Better(adjusted, best) && (adjusted.Errors.Count < best.Errors.Count ||
                    adjusted.Achievement > best.Achievement || adjusted.Gaps.Count < best.Gaps.Count))
                {
                    best = adjusted;
                    fingertips = smaller;
                    prepared = smallerPrepared;
                }
                if (best.IsSolved) break;
            }
            var feedback = PlannerTimingFeedback.DelayedContacts(best);
            var earlier = PlannerTimingFeedback.EarlierContacts(best, includeHeadFeedback: diagnosticRecovery);
            if (feedback.Count == 0 && (!diagnosticRecovery || earlier.Count == 0))
            {
                if (ReferenceEquals(before, best)) break;
                continue;
            }
            var accepted = shifts;
            foreach (var timing in new[]
                     {
                         feedback.ToDictionary(kv => kv.Key, kv => (double?)null),
                         earlier.ToDictionary(kv => kv.Key, kv => (double?)kv.Value),
                         feedback.ToDictionary(kv => kv.Key, kv => (double?)kv.Value)
                     })
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timing.Count == 0) continue;
                var hints = new Dictionary<int, double?>(shifts);
                foreach (var (id, start) in timing) hints[id] = start;
                // 当前最优方案已经核验过这组时机，不再重复搜索整首谱面。
                if (hints.Count == shifts.Count && hints.All(pair =>
                        shifts.TryGetValue(pair.Key, out var previous) && previous == pair.Value)) continue;
                var adjusted = PlanPass(chart, options, prepared, hints, cancellationToken);
                if (!Better(adjusted, best)) continue;
                best = adjusted;
                accepted = hints;
                if (best.IsSolved) break;
            }
            if (ReferenceEquals(before, best)) break;
            shifts = accepted;
        }
        var sweepBase = best;
        var earlySweeps = PlannerTimingFeedback.InterferingSweeps(sweepBase, includeMaskedContacts: false);
        var allInterference = PlannerTimingFeedback.InterferingSweeps(sweepBase);
        var sweepCandidates = earlySweeps.SetEquals(allInterference) ? new[] { earlySweeps } : [earlySweeps, allInterference];
        foreach (var strikeOnly in sweepCandidates)
        {
            // 持续占键约束可能让其他段失去可行解，两组反馈从同一方案分别核验。
            var candidate = sweepBase;
            if (strikeOnly.Count > 0)
            {
                var adjusted = PlanPass(chart, options, prepared, shifts, cancellationToken, strikeOnly);
                if (Better(adjusted, candidate)) candidate = adjusted;
            }
            if (!candidate.IsSolved && candidate.Gaps.Any(g => chart.Notes[g.NoteId].Kind is NoteKind.Tap or NoteKind.Hold &&
                    candidate.Strokes.Any(s => s.Technique == HandTechnique.Rotation &&
                        (diagnosticRecovery || g.Time > s.Start) &&
                        Math.Abs(g.Time - s.Start) <= 2 * NormalHandPlanner.FastSweepInterval)))
            {
                // 密集扫键的可行时序可能落在原候选之间，保留原方案后再核验更细的采样。
                var adjusted = PlanPass(chart, options, prepared, shifts, cancellationToken, strikeOnly,
                    refineSweepTiming: true, includeRecoveryTechniques: diagnosticRecovery);
                if (Better(adjusted, candidate)) candidate = adjusted;
            }
            if (Better(candidate, best)) best = candidate;
            if (best.IsSolved) break;
        }
        return best;
    }

    private static HandPlan PlanPass(Chart chart, HumanPlannerOptions options, PreparedSearch prepared,
        IReadOnlyDictionary<int, double?>? shiftHints, CancellationToken cancellationToken,
        IReadOnlySet<int>? strikeOnly = null, bool refineSweepTiming = false, bool includeRecoveryTechniques = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var doubleTimes = prepared.DoubleTimes;
        var model = options.MotionModel;
        var coverageProbe = new DemandCoverageProbe(chart);
        var coverageCache = new Dictionary<(ActionKey, ActionKey), (IReadOnlyList<InputEvent> Inputs, Dictionary<int, bool> Results)>();
        State SkipDemand(State state, GameplayDemand demand, int actions, bool checkCoverage = true)
        {
            if (!checkCoverage || demand.Note.Kind is not (NoteKind.Tap or NoteKind.Touch or NoteKind.Slide)) return Skip(state, demand, actions);
            Action? Relevant(Action? action) => action is { Channel: InputChannel.Touch } &&
                action.Start <= demand.End + .3 && action.End >= demand.Start - .3 ? action : null;
            var left = Relevant(state.Left);
            var right = Relevant(state.Right);
            if (left is null && right is null) return Skip(state, demand, actions);
            var key = (Key(left), Key(right));
            if (!coverageCache.TryGetValue(key, out var coverage))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tracks = new List<HandTrack>();
                if (left is not null) tracks.Add(new(HandSide.Left, left.Frames));
                if (right is not null) tracks.Add(new(HandSide.Right, right.Frames));
                coverage = (new HandReplay(tracks).Events, new());
                coverageCache.Add(key, coverage);
            }
            if (!coverage.Results.TryGetValue(demand.Id, out var covered))
            {
                cancellationToken.ThrowIfCancellationRequested();
                covered = coverageProbe.IsCoveredByInputs(demand.Note, coverage.Inputs);
                coverage.Results.Add(demand.Id, covered);
            }
            // 已有轨迹顺带完成的需求不增加搜索缺口，最终仍以整曲回放为准。
            return covered ? Skip(state, demand, 0) with
            {
                Covered = new(demand.Id, new[] { left?.DemandId, right?.DemandId }.OfType<int>().ToArray())
            } : Skip(state, demand, actions);
        }
        var motions = prepared.Motions;
        var patterns = prepared.Patterns;
        var sweepMotions = prepared.SweepMotions;
        var shiftedContacts = prepared.ShiftedContacts;
        var beam = new List<State> { new(null, null, false, 0, 0, null, null, null) };
        for (var patternIndex = 0; patternIndex < patterns.Count; patternIndex++)
        {
            var pattern = patterns[patternIndex];
            var alternatives = new List<State>();
            foreach (var technique in TechniqueCandidates.For(pattern, model.PreferredStrikeIntervalSeconds,
                         includeRecoveryTechniques))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (strikeOnly is not null && pattern.Demands.Where((d, i) => strikeOnly.Contains(d.Id) &&
                        (technique.SweepDirections?[i] ?? technique.SweepDirection) != 0).Any()) continue;
                var local = beam.Where(s => !technique.RequiresSweepContinuation || CanContinueTechnique(s, pattern, technique, shiftHints))
                    .Select(s => s with { Cost = s.Cost + technique.PreferenceCost * pattern.Demands.Count }).ToList();
                if (local.Count == 0) continue;
                foreach (var index in technique.DemandOrder ?? Enumerable.Range(0, pattern.Demands.Count).ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var demand = pattern.Demands[index];
                    var direction = technique.SweepDirections?[index] ?? technique.SweepDirection;
                    var requiresContinuation = technique.RequiresSweepContinuation && direction != 0;
                    var motion = motions[demand.Id];
                    if (direction != 0)
                    {
                        var key = (demand.Id, direction);
                        if (!sweepMotions.TryGetValue(key, out var sweep))
                        {
                            sweep = Prepare(SweepFrames(demand, direction, chart, entering: false), model) with
                            {
                                EntryFrames = SweepFrames(demand, direction, chart, entering: true)
                            };
                            sweepMotions.Add(key, sweep);
                        }
                        motion = [sweep];
                    }
                    var motionVariants = MotionVariants(demand, motion, model);
                    if (prepared.FullSlides.TryGetValue(demand.Id, out var fullSlide))
                        motionVariants = new[] { new[] { fullSlide } }.Concat(motionVariants).ToArray();
                    if (prepared.AvoidanceSlides.TryGetValue(demand.Id, out var avoidingSlides))
                        motionVariants = motionVariants.Concat(avoidingSlides.Select(m => new[] { m })).ToArray();
                    var next = new List<State>();
                    foreach (var state in local)
                    {
                        var assigned = false;
                        var hands = technique.Hands[index] is { } required ? new[] { required } : [HandSide.Left, HandSide.Right];
                        foreach (var hand in hands)
                            foreach (var variant in motionVariants)
                            foreach (var shift in CandidateShifts(demand, variant[0], state, prepared, shiftHints,
                                         refineSweepTiming && direction != 0))
                            {
                                var candidate = state;
                                for (var branch = 0; branch < variant.Length; branch++)
                                {
                                    var branchHand = branch == 0 ? hand : Other(hand);
                                    candidate = Assign(candidate, demand, variant[branch], branchHand, shift,
                                        technique.Name, patternIndex, direction, branch,
                                        doubleTimes.Contains(demand.Note.Time), technique.RequiresSweepContinuation, model, shiftedContacts, prepared);
                                    if (candidate is null) break;
                                }
                                if (candidate is not null)
                                {
                                    next.Add(candidate);
                                    assigned = true;
                                }
                            }
                        // 时机反馈先保留被修正的接触，避免用丢弃 Slide 代替修正其误触。
                        if (!requiresContinuation && (shiftHints is null || !shiftHints.ContainsKey(demand.Id)) &&
                            (!assigned || demand.Note.Kind is not (NoteKind.Hold or NoteKind.TouchHold)))
                            next.Add(SkipDemand(state, demand, motion.Length, checkCoverage: !assigned));
                    }
                    if (next.Count == 0 && requiresContinuation)
                    {
                        local.Clear();
                        break;
                    }
                    if (next.Count == 0) next.AddRange(local.Select(s => SkipDemand(s, demand, motion.Length)));
                    local = Prune(next, options.BeamWidth, s => TailLookahead(s, prepared, model));
                }
                alternatives.AddRange(local);
            }
            beam = Prune(alternatives, options.BeamWidth, s => TailLookahead(s, prepared, model));
            coverageCache.Clear();
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

    private static IEnumerable<double> Shifts(GameplayDemand demand, bool refineSweepTiming)
    {
        if (refineSweepTiming) return [0, -.00625, .00625, -.0125, .0125];
        return demand.Note.Kind switch
        {
            NoteKind.Touch or NoteKind.TouchHold => [0, -.025, .025, -.075, .075, -.125, .125],
            NoteKind.Slide => [0, -.06, .06, -.12, .12],
            _ => [0, -.0125, .0125]
        };
    }

    private static IEnumerable<double> CandidateShifts(GameplayDemand demand, Motion motion, State state,
        PreparedSearch prepared, IReadOnlyDictionary<int, double?>? hints, bool refineSweepTiming)
    {
        var shifts = hints is not null && hints.TryGetValue(demand.Id, out var requested)
            ? new[] { requested is { } start ? start - motion.Frames[0].Time : 0 }
            : Shifts(demand, refineSweepTiming);
        foreach (var shift in shifts) yield return shift;
        if (!prepared.PreviousSlide.TryGetValue(demand.Id, out var previousId)) yield break;
        var before = state.Left?.DemandId == previousId ? state.Left : state.Right?.DemandId == previousId ? state.Right : null;
        if (before is not { FullSlide: true }) yield break;
        // 判定反馈可产生采样档位之外的时移，后轨仍可继承当前划手的真实出口时刻。
        var inherited = before.End - motion.Frames[0].Time;
        if (!shifts.Any(shift => Math.Abs(shift - inherited) < Epsilon)) yield return inherited;
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
        var frames = ContactFrames(motion.Points, motion.Channel, 0, motion.Profile);
        var prepared = Prepare(frames, model) with { Profile = motion.Profile };
        if (prepared.Feasible || motion.Note.Kind != NoteKind.Slide || motion.Note.SlidePath!.IsWifi) return prepared;
        var peak = frames.Zip(frames.Skip(1), (a, b) =>
            model.Geometry.DistanceMillimeters(a.Position, b.Position) / (b.Time - a.Time)).Max();
        var scale = peak / model.MaxSpeedMillimetersPerSecond * 1.001;
        // 短轨道可以在已有的时机搜索范围内延长，整曲判定仍须实际通过。
        if ((frames[^1].Time - frames[0].Time) * (scale - 1) > .12) return prepared;
        return Prepare(frames.Select(f => f with { Time = frames[0].Time + (f.Time - frames[0].Time) * scale }).ToArray(), model)
            with { Profile = motion.Profile };
    }

    private static Motion[][] MotionVariants(GameplayDemand demand, Motion[] original, HumanMotionModel model)
    {
        if (demand.Note.SlidePath is { IsWifi: true })
        {
            // 延后双手入屏时仍保留末区时刻，避免为等待扫尾而把整条轨道拖晚。
            return new[] { 0d, .06, .12, .18, .24 }.Select(delay => original.Select(m =>
            {
                var start = m.Frames[0].Time;
                var end = m.Frames[^1].Time;
                if (end - start <= delay + PlaybackSession.LogicStep) return m with { Feasible = false };
                return Prepare(m.Frames.Select(f => f with { Time = f.Time + delay * (end - f.Time) / (end - start) }).ToArray(), model)
                    with { Profile = m.Profile };
            }).ToArray()).ToArray();
        }
        if (demand.Note.Kind is not (NoteKind.Hold or NoteKind.TouchHold)) return [original];
        var frames = original[0].Frames;
        var end = Math.Max(frames[0].Time + .025, frames[^1].Time - .04);
        if (end >= frames[^1].Time - Epsilon) return [original];
        var shortened = Prepare(frames.Where(f => f.Time < end - Epsilon)
            .Append(new(end, HumanMotionModel.PositionAt(frames, end), [])).ToArray(), model);
        // 提前释放只是候选，头尾宽限和持续比例仍由原判定器核验。
        return [original, [shortened with { Cost = shortened.Cost + .1, Profile = original[0].Profile }]];
    }

    private static State? Assign(State state, GameplayDemand demand, Motion motion, HandSide hand, double shift,
        string technique, int pattern, int direction, int branch, bool twoHandChord, bool requiresSweepContinuation, HumanMotionModel model,
        Dictionary<(HandFrame[] Frames, double Shift), HandFrame[]> shiftedContacts, PreparedSearch prepared, bool liftSweep = false)
    {
        if (!motion.Feasible) return null;
        var before = hand == HandSide.Left ? state.Left : state.Right;
        var other = hand == HandSide.Left ? state.Right : state.Left;
        var start = motion.Frames[0].Time + shift;
        var end = motion.Frames[^1].Time + shift;
        var inChain = prepared.ChainTails.ContainsKey(demand.Id);
        var hasPrevious = prepared.PreviousSlide.TryGetValue(demand.Id, out var previousId);
        var predecessor = hasPrevious ? before?.DemandId == previousId ? before : other?.DemandId == previousId ? other : null : null;
        var continuesSlide = predecessor == before && before is { FullSlide: true } &&
            Math.Abs(start - before.End) < Epsilon && before.Last.DistanceTo(motion.Frames[0].Position) < Epsilon;
        var continues = !liftSweep && direction != 0 && before is not null && before.Direction == direction &&
            (before.Pattern == pattern || CanContinueSweep(before, demand, direction, start));
        if (requiresSweepContinuation && direction != 0 && !continues) return null;
        var contactFrames = !continues && motion.EntryFrames is { } entryFrames ? entryFrames : motion.Frames;
        if (liftSweep)
        {
            var key = (demand.Id, direction);
            if (!prepared.LiftedSweepEntries.TryGetValue(key, out var landing))
            {
                var entry = contactFrames[0].Position;
                var target = SlidePath.ButtonPosition(demand.Note.StartPosition, 1.12);
                var remaining = RotationAngle(entry, target, direction);
                var point = Ring(Math.Atan2(entry.Y, entry.X) + direction * Math.Min(.06, remaining));
                landing = [new(contactFrames[0].Time, point, Fingers(point, InputChannel.Button)),
                    new(contactFrames[^1].Time, point, [])];
                prepared.LiftedSweepEntries.Add(key, landing);
            }
            contactFrames = landing;
        }
        var first = contactFrames[0].Position;
        var from = before?.Last ?? Rest(hand);
        var departure = before?.End ?? Math.Min(0, start - 1);
        if (liftSweep && before is { Note.Kind: NoteKind.Tap })
        {
            // 扫过上一键后抬手越过占用区，再接回同向扫动；实际释放帧替换原来的停留尾部。
            departure = before.Start + PlaybackSession.LogicStep;
            from = before.PositionAt(departure);
        }
        var distance = model.Geometry.DistanceMillimeters(from, first);
        if (continuesSlide)
        {
            departure = start;
            distance = 0;
        }
        else if (continues)
        {
            from = before!.Contact[0].Position;
            departure = before.Start;
            var sweep = RotationAngle(from, first, direction);
            distance = 1.12 * model.Geometry.JudgementRadiusMillimeters * sweep;
        }
        else
        {
            if (start - departure < PlaybackSession.LogicStep - Epsilon) return null;
            // 搜索和轨迹生成使用同一段移动时间，空闲时按舒适速度提前出发。
            var travel = Math.Max(PlaybackSession.LogicStep, distance / model.PreferredSpeedMillimetersPerSecond);
            departure = Math.Max(departure, start - travel);
        }
        if (!continuesSlide && (start <= departure || distance > (start - departure) * model.MaxSpeedMillimetersPerSecond + Epsilon)) return null;
        var otherPosition = other is null ? Rest(Other(hand)) : other.PositionAt(start);
        var transition = continuesSlide
            ? new HumanMotionTransition(true, 0, 0, 0, hand == HandSide.Left ? first.X > otherPosition.X : first.X < otherPosition.X)
            : model.EvaluateTransition(hand, from, departure, first, start, otherPosition, distance, inChain ? .1 : 1);
        if (!transition.Feasible) return null;
        // 同刻普通双押保留双手；被持续占用时，Touch 可以在窗口内依次击打。
        if (twoHandChord && before is not null && before.Note.Kind != NoteKind.Slide && demand.Note.Kind != NoteKind.Slide &&
            Math.Abs(before.Note.Time - demand.Note.Time) < Epsilon &&
            !(before.Note.Kind == NoteKind.Touch && demand.Note.Kind == NoteKind.Touch && other is not null &&
              other.Start < demand.Start - .15 && other.End > demand.Start + .15)) return null;
        // 同一模板的时移与分手、前驱状态无关，整轮搜索只生成一次。
        if (!shiftedContacts.TryGetValue((contactFrames, shift), out var contact))
        {
            contact = contactFrames.Select(f => f with { Time = f.Time + shift }).ToArray();
            shiftedContacts.Add((contactFrames, shift), contact);
        }
        var action = new Action(demand.Id, demand.Note, hand, demand.Channel, contact, from, departure, continues,
            start, end, inChain ? continuesSlide ? "one-stroke-continue" : predecessor is not null && predecessor.Hand != hand
                ? "one-stroke-relay" : "one-stroke-entry" : technique, pattern, direction, branch, motion.Profile)
        { ContinuesSlide = continuesSlide, FullSlide = motion.FullSlide };
        // 同屏接触的实体冲突先于成本比较。检查已分配的重叠动作，包括被后续动作替换的历史。
        var coveredFrom = double.PositiveInfinity;
        for (var node = state; node is not null; node = node.Parent)
        {
            if (node.Added is not { } occupied || occupied.Hand == hand) continue;
            if (occupied.End <= action.ApproachTime + Epsilon) break;
            if (occupied.ApproachTime < end - Epsilon &&
                model.HasCollision(action.CollisionFrames, occupied.CollisionFrames, coveredFrom))
                return continues && demand.Note.Kind == NoteKind.Tap
                    ? Assign(state, demand, motion, hand, shift, technique, pattern, direction, branch, twoHandChord,
                        false, model, shiftedContacts, prepared, liftSweep: true) : null;
            coveredFrom = Math.Min(coveredFrom, occupied.ApproachTime);
        }
        var crossingChange = transition.Crossed != state.Crossed ? (inChain ? .015 : .15) : 0;
        var repeatCost = before is not null && before.Note.Kind == NoteKind.Tap && demand.Note.Kind == NoteKind.Tap &&
            demand.Note.Time > before.Note.Time + Epsilon &&
            before.Note.StartPosition == demand.Note.StartPosition
            ? model.RepetitionCost(Math.Min(start - before.Start, demand.Note.Time - before.Note.Time)) : 0;
        // 接力是初次搜索中的正常候选。有限代价允许后续接键的收益抵消交接操作。
        var seamCost = predecessor is null ? 0 : continuesSlide ? 0 : predecessor.Hand != hand ? .65 : 1.3;
        var turnCost = before is null || continues || continuesSlide ? 0 : ExitTurnCost(before, first, start, model);
        var cost = transition.Cost + motion.Cost + Math.Abs(shift) * 3 + crossingChange + repeatCost + seamCost + turnCost + (liftSweep ? .25 : 0);
        return new(hand == HandSide.Left ? action : state.Left, hand == HandSide.Right ? action : state.Right,
            transition.Crossed, state.Skipped, state.Cost + cost, state, action, null);
    }

    private static double ExitTurnCost(Action before, ChartPoint next, double arrival, HumanMotionModel model)
    {
        var contact = before.Contact;
        if (contact.Length < 2) return 0;
        var a = contact[^2];
        var b = contact[^1];
        var vx = (b.Position.X - a.Position.X) / (b.Time - a.Time);
        var vy = (b.Position.Y - a.Position.Y) / (b.Time - a.Time);
        var dx = next.X - b.Position.X;
        var dy = next.Y - b.Position.Y;
        var speed = Math.Sqrt(vx * vx + vy * vy);
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (speed < Epsilon || distance < Epsilon) return 0;
        var reversal = Math.Max(0, -(vx * dx + vy * dy) / (speed * distance));
        return .35 * reversal * speed * model.Geometry.JudgementRadiusMillimeters /
            model.PreferredSpeedMillimetersPerSecond / Math.Max(.1, arrival - before.End);
    }

    // 只用于剪枝，不累加到动作成本；后续动作到来后仍按实际轨迹计算。
    private static double TailLookahead(State state, PreparedSearch prepared, HumanMotionModel model)
    {
        var active = new[] { state.Left, state.Right }.Where(a => a is not null && prepared.ChainTails.ContainsKey(a.DemandId))
            .OrderByDescending(a => a!.Start).FirstOrDefault();
        if (active is null) return 0;
        var tail = prepared.ChainTails[active.DemandId];
        var groups = prepared.TailInputs[tail.Id];
        if (groups.Length == 0) return 0;
        var progress = Math.Max(state.Left?.Start ?? double.NegativeInfinity, state.Right?.Start ?? double.NegativeInfinity);
        var upcoming = groups.Where(g => g[0].Start > progress + Epsilon).ToArray();
        if (upcoming.Length == 0) return 0;
        (ChartPoint Position, double Time) Exit(HandSide hand)
        {
            var action = hand == HandSide.Left ? state.Left : state.Right;
            if (action == active && active.DemandId != tail.Id)
                return (tail.SlidePath!.PointAt(1), tail.EndTime);
            return (action?.Last ?? Rest(hand), action?.End ?? 0);
        }
        var left = Exit(HandSide.Left);
        var right = Exit(HandSide.Right);
        double Cost((ChartPoint Position, double Time) from, GameplayDemand demand)
        {
            var to = demand.Channel == InputChannel.Button ? SlidePath.ButtonPosition(demand.Note.StartPosition, 1.12)
                : TouchCentre(demand.Note.TouchArea);
            var distance = model.Geometry.DistanceMillimeters(from.Position, to);
            var duration = Math.Max(PlaybackSession.LogicStep, demand.Start - from.Time);
            var excess = Math.Max(0, distance / duration / model.PreferredSpeedMillimetersPerSecond - 1);
            return .15 * Math.Pow(distance / model.Geometry.JudgementRadiusMillimeters, 2) / Math.Min(duration, 1) + .15 * excess * excess;
        }
        var total = 0d;
        foreach (var group in upcoming)
        {
            var first = group[0];
            if (group.Length >= 2)
                total += Math.Min(Cost(left, first) + Cost(right, group[1]), Cost(right, first) + Cost(left, group[1]));
            else total += Math.Min(Cost(left, first), Cost(right, first));
        }
        return total;
    }

    private static bool CanContinueTechnique(State state, PatternSegment pattern, TechniqueCandidate technique,
        IReadOnlyDictionary<int, double?>? shiftHints)
    {
        for (var i = 0; i < pattern.Demands.Count; i++)
        {
            var direction = technique.SweepDirections?[i] ?? technique.SweepDirection;
            if (direction == 0) continue;
            var demand = pattern.Demands[i];
            var start = shiftHints?.GetValueOrDefault(demand.Id) ?? demand.Start;
            bool Continues(Action? before) => before is not null && before.Direction == direction &&
                CanContinueSweep(before, demand, direction, start);
            if (!Continues(state.Left) && !Continues(state.Right)) return false;
        }
        return true;
    }

    private static bool CanContinueSweep(Action before, GameplayDemand demand, int direction, double start)
    {
        if (before.Note.Kind != NoteKind.Tap || demand.Note.Kind is not (NoteKind.Tap or NoteKind.Hold)) return false;
        var steps = ((demand.Note.StartPosition - before.Note.StartPosition) * direction + 8) % 8;
        return steps is 1 or 2 && start > before.Start &&
            start - before.Start <= NormalHandPlanner.FastSweepInterval * steps + Epsilon;
    }

    private static List<State> Prune(List<State> states, int width, Func<State, double>? lookahead = null)
    {
        double Rank(State state) => state.Cost + (lookahead?.Invoke(state) ?? 0);
        var seen = new HashSet<(ActionKey Left, ActionKey Right, bool Crossed)>(states.Count);
        var ranked = new List<State>(states.Count);
        foreach (var state in states.OrderBy(s => s.Skipped).ThenBy(Rank))
            if (seen.Add((Key(state.Left), Key(state.Right), state.Crossed))) ranked.Add(state);
        // 每种占手布局保留最低成本，以及左右手各自最早释放的方案。
        // 长按尾部的少量时差可能决定下一组 Touch 是否来得及完成。
        var layouts = ranked.Where(s => s.Skipped == ranked[0].Skipped)
            .GroupBy(s => (s.Left?.DemandId, s.Left?.Direction, s.Left?.Branch, s.Left?.FullSlide,
                s.Right?.DemandId, s.Right?.Direction, s.Right?.Branch, s.Right?.FullSlide)).ToArray();
        var selected = layouts.Select(g => g.First()).Take(width).ToList();
        var included = new HashSet<State>(selected, ReferenceEqualityComparer.Instance);
        foreach (var state in layouts.SelectMany(g => new[]
                 {
                     g.OrderBy(s => s.Left?.End ?? double.NegativeInfinity).First(),
                     g.OrderBy(s => s.Right?.End ?? double.NegativeInfinity).First()
                 }).OrderBy(Rank))
        {
            if (selected.Count == width) break;
            if (included.Add(state)) selected.Add(state);
        }
        selected.AddRange(ranked.Where(s => !included.Contains(s)).Take(width - selected.Count));
        return selected.OrderBy(s => s.Skipped).ThenBy(Rank).ToList();
    }

    private static ActionKey Key(Action? action) =>
        new(action?.DemandId, action?.Start, action?.End, action?.Direction, action?.Pattern, action?.Branch,
            action?.ApproachTime, action is { Continues: true }
                ? Ring(Math.Atan2(action.ApproachPosition.Y, action.ApproachPosition.X)) : action?.ApproachPosition, action?.Profile,
            action?.FullSlide ?? false, action?.ContinuesSlide ?? false, action?.Contact);

    private static HandPlan Verify(Chart chart, State state, IReadOnlyList<PatternSegment> patterns,
        HumanMotionModel model, CancellationToken token)
    {
        var actions = new List<Action>();
        var skipped = new List<GameplayDemand>();
        var covered = new List<CoveredDemand>();
        for (var node = state; node is not null; node = node.Parent)
        {
            if (node.Added is { } added) actions.Add(added);
            if (node.Gap is { } gap) skipped.Add(gap);
            if (node.Covered is { } coverage) covered.Add(coverage);
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
            {
                SlideConnections = MaiPlay.Core.SlideConnections.Measure(chart, tracks, strokes, model.Geometry),
                CoveredDemands = covered.Where(c => !failed.Contains(c.DemandId)).OrderBy(c => c.DemandId).ToArray()
            }
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
        if (a.Movement.Collisions.Count != b.Movement.Collisions.Count) return a.Movement.Collisions.Count < b.Movement.Collisions.Count;
        if (a.Movement.SpeedLimitViolations != b.Movement.SpeedLimitViolations) return a.Movement.SpeedLimitViolations < b.Movement.SpeedLimitViolations;
        if (candidate.Errors.Count != before.Errors.Count) return candidate.Errors.Count < before.Errors.Count;
        if (candidate.Achievement != before.Achievement) return candidate.Achievement > before.Achievement;
        if (candidate.Gaps.Count != before.Gaps.Count) return candidate.Gaps.Count < before.Gaps.Count;
        return a.MovementCost < b.MovementCost;
    }

    private static HandFrame[] SweepFrames(GameplayDemand demand, int direction, Chart chart, bool entering)
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
        if (entering && chart.Notes.Any(n => n.Kind is NoteKind.Tap or NoteKind.Hold && n.StartPosition != demand.Note.StartPosition &&
            n.Time >= demand.Start - Epsilon && n.Time <= demand.Start + HeadJudgements.EarlyInputWindow + Epsilon &&
            covered.Contains(n.StartPosition.ToString()))) point = Ring(centre);
        var end = demand.Note.Kind == NoteKind.Hold
            ? Math.Max(demand.Start + .025, demand.End - Math.Min(.04, demand.Note.Duration * .1)) : demand.Start + .025;
        return demand.Note.Kind == NoteKind.Hold
            ? ContactFrames(HoldSweepPoints(demand.Start, end, point, demand.Note.StartPosition, direction),
                InputChannel.Button, 0, ContactProfile.Fingertips)
            : [new(demand.Start, point, Fingers(point, InputChannel.Button)), new(end, point, [])];
    }

    private static HandSide Other(HandSide hand) => hand == HandSide.Left ? HandSide.Right : HandSide.Left;
    private static ChartPoint Rest(HandSide hand) => new(hand == HandSide.Left ? -.5 : .5, .5);
    private static ChartPoint Ring(double angle) => new(1.12 * Math.Cos(angle), 1.12 * Math.Sin(angle));
    private static double RotationAngle(ChartPoint from, ChartPoint to, int direction) =>
        ((Math.Atan2(to.Y, to.X) - Math.Atan2(from.Y, from.X)) * direction + 2 * Math.PI) % (2 * Math.PI);
}
