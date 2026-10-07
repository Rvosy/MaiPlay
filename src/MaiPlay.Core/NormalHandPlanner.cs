using PointFrame = MaiPlay.Core.ContactPoint;
using static MaiPlay.Core.ContactTrajectoryCompiler;

namespace MaiPlay.Core;

/// <summary>常规击打与滑动先分配双手，再通过真实输入判定核验整首谱面。</summary>
public static class NormalHandPlanner
{
    private const int BeamWidth = 96;
    private const double Pulse = .025;
    private const double MaxTravelSpeed = 18;
    private const double Epsilon = 1e-8;
    public const double FastRepeatInterval = .125;
    public const double FastSweepInterval = .1;
    private sealed record Stroke(int Index, ChartNote Note, InputChannel Channel, PointFrame[] Points, bool TwoHandChord = false)
    {
        public int? RepeatPreviousId { get; init; }
        public bool FastRepeat { get; init; }
        public int RotationDirection { get; init; }
        public int? RotationPreviousId { get; init; }
        public int RotationGroup { get; init; } = -1;
        public double RotationStart { get; init; }
        public bool FastSweep { get; init; }
        public bool KeepRotationHand { get; init; }
        public double[] Shifts { get; init; } = Note.Kind == NoteKind.Touch ? [0, -.075, .075, -.125, .125] : [0];
        public double Start => Points[0].Time;
        public double End => Points[^1].Time;
        public ChartPoint First => Points[0].Position;
        public ChartPoint Last => Points[^1].Position;
    }
    private sealed record Choice(Stroke Stroke, HandSide Hand, double Shift)
    {
        public double Start => Stroke.Start + Shift;
        public double End => Stroke.End + Shift;
    }
    private sealed record SearchState(Choice? Left, Choice? Right, double Cost, SearchState? Parent, Choice? Added);

    public static HandPlan Plan(Chart chart, CancellationToken cancellationToken = default)
    {
        var strokes = CreateStrokes(chart, cancellationToken).OrderBy(s => s.Start)
            .ThenBy(s => s.Note.Kind == NoteKind.Touch ? s.First.X : 0).ThenBy(s => s.Note.Id)
            .Select((s, i) => s with { Index = i }).ToArray();
        MarkRepeats(strokes);
        var plan = Optimize(chart, strokes, cancellationToken, out var selectedStrokes);
        var calibratedStrokes = selectedStrokes;
        if (CreateRotations(calibratedStrokes, includeShortSweeps: false) is { } rotating)
        {
            var rotationPlan = Optimize(chart, rotating, cancellationToken, out var optimized);
            if (Better(rotationPlan, plan) || rotationPlan.Errors.Count == plan.Errors.Count &&
                rotationPlan.Achievement == plan.Achievement && rotationPlan.Gaps.Count <= plan.Gaps.Count)
            {
                plan = rotationPlan;
                selectedStrokes = optimized;
            }
        }
        if (CreateRotations(calibratedStrokes, includeShortSweeps: true) is { } sweeping && sweeping.Any(s => s.FastSweep))
        {
            var singleHand = sweeping.Select(s => s.FastSweep ? s with { KeepRotationHand = true } : s).ToArray();
            var singlePlan = Optimize(chart, singleHand, cancellationToken, out var optimized);
            if (Better(singlePlan, plan) || singlePlan.Errors.Count == plan.Errors.Count &&
                singlePlan.Achievement == plan.Achievement && singlePlan.Gaps.Count <= plan.Gaps.Count)
            {
                plan = singlePlan;
                selectedStrokes = optimized;
            }
        }
        if (CreateRotations(calibratedStrokes, includeShortSweeps: true, allowSkippedKeys: true) is { } continuous)
        {
            var continuousHand = continuous.Select(s => s.FastSweep ? s with { KeepRotationHand = true } : s).ToArray();
            var continuousPlan = Optimize(chart, continuousHand, cancellationToken, out var optimized);
            if (Better(continuousPlan, plan) || continuousPlan.Errors.Count == plan.Errors.Count &&
                continuousPlan.Achievement == plan.Achievement && continuousPlan.Gaps.Count <= plan.Gaps.Count)
            {
                plan = continuousPlan;
                selectedStrokes = optimized;
            }
        }
        if (CreatePairedSweeps(selectedStrokes) is { } paired)
        {
            var pairedPlan = Optimize(chart, paired, cancellationToken, out _);
            if (Better(pairedPlan, plan) || pairedPlan.Errors.Count == plan.Errors.Count &&
                pairedPlan.Achievement == plan.Achievement && pairedPlan.Gaps.Count <= plan.Gaps.Count)
                plan = pairedPlan;
        }
        return plan;
    }

    private static HandPlan Optimize(Chart chart, Stroke[] strokes, CancellationToken cancellationToken, out Stroke[] selectedStrokes)
    {
        selectedStrokes = strokes;
        var plan = Solve(chart, strokes, cancellationToken);
        if (plan.AllCriticalPerfect) return plan;
        var problemTimes = plan.Gaps.Select(g => g.Time).Concat(plan.Errors.SelectMany(e =>
            new[] { e.Time, e.Note.Kind == NoteKind.Slide ? e.Note.SlideStartTime : e.Note.Time })).ToArray();
        var failedSlides = plan.Errors.Where(e => e.Note.Kind == NoteKind.Slide).Select(e => e.Note.Id).ToHashSet();
        var expanded = strokes.Select(s => problemTimes.Any(t => t >= s.Start - .5 && t <= s.End + .5)
            ? s with { Points = s.Note.Kind is NoteKind.Hold or NoteKind.TouchHold
                    ? [s.Points[0], s.Points[^1] with { Time = Math.Max(s.Start + Pulse, s.End - .04) }]
                    : failedSlides.Contains(s.Note.Id) && s.Note.SlidePath is { IsWifi: false } path && s.Note.Duration < path.Length / 30
                        ? SlidePoints(s.Note, path.Length / 30) : s.Points,
                Shifts = s.Note.Kind switch
                {
                    NoteKind.Tap or NoteKind.Hold => [0, -.0125, .0125],
                    NoteKind.Slide => [0, -.06, .06, -.12, .12],
                    NoteKind.TouchHold => [0, -.075, .075, -.125, .125],
                    _ => s.Shifts
                } }
            : s).ToArray();
        var alternative = Solve(chart, expanded, cancellationToken);
        if (Better(alternative, plan))
        {
            plan = alternative;
            selectedStrokes = expanded;
        }
        if (plan.AllCriticalPerfect) return plan;
        var repaired = RepairEarlyContacts(expanded, plan);
        if (repaired is not null)
        {
            var adjusted = Solve(chart, repaired, cancellationToken);
            if (Better(adjusted, plan))
            {
                plan = adjusted;
                selectedStrokes = repaired;
            }
        }
        return plan;
    }

    private static bool Better(HandPlan candidate, HandPlan before) => candidate.Errors.Count < before.Errors.Count ||
        candidate.Errors.Count == before.Errors.Count && (candidate.Achievement > before.Achievement ||
            candidate.Achievement == before.Achievement && candidate.Gaps.Count < before.Gaps.Count);

    private static Stroke[]? RepairEarlyContacts(Stroke[] strokes, HandPlan plan)
    {
        var repaired = strokes.ToArray();
        var changed = false;
        foreach (var error in plan.Errors.Where(e => e.DifferenceSeconds < 0 && e.Note.Kind is NoteKind.Slide or NoteKind.Tap or NoteKind.Hold))
        {
            foreach (var action in plan.Strokes.Where(s => s.Channel == InputChannel.Touch &&
                         (error.Note.Kind == NoteKind.Slide ? s.NoteId == error.Note.Id : s.Start <= error.Time && s.End >= error.Time - PlaybackSession.LogicStep)))
                foreach (var stroke in strokes.Where(s => s.Note.Id == action.NoteId && s.Channel == action.Channel))
                {
                    var shift = action.Start - stroke.Start;
                    if (error.Note.Kind != NoteKind.Slide)
                    {
                        var point = stroke.Points.MinBy(p => Math.Abs(p.Time + shift - error.Time))!;
                        var area = "A" + error.Note.StartPosition;
                        if (!Fingers(point.Position, InputChannel.Touch).Any(f => ContactMapper.AreasAt(InputChannel.Touch,
                                new(point.Position.X + f.Offset.X, point.Position.Y + f.Offset.Y), f.Radius).Contains(area))) continue;
                    }
                    var delay = -error.DifferenceSeconds!.Value;
                    repaired[stroke.Index] = stroke with { Shifts = [shift + delay] };
                    changed = true;
                }
        }
        return changed ? repaired : null;
    }

    private static HandPlan Solve(Chart chart, Stroke[] strokes, CancellationToken cancellationToken)
    {
        var gaps = new List<HandPlanGap>();
        var beam = new List<SearchState> { new(null, null, 0, null, null) };
        foreach (var stroke in strokes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new List<SearchState>();
            foreach (var state in beam)
                foreach (var hand in new[] { HandSide.Left, HandSide.Right })
                    foreach (var shift in stroke.Shifts)
                        if (Assign(state, new(stroke, hand, shift)) is { } candidate) next.Add(candidate);
            if (next.Count == 0)
            {
                gaps.Add(new(stroke.Note.Id, stroke.Start, stroke.End, "双手占用或移动时间不足，暂未找到可衔接的手顺。"));
                continue;
            }
            beam = next.OrderBy(s => s.Cost)
                .DistinctBy(s => (s.Left?.Stroke.Index, s.Left?.Shift, s.Right?.Stroke.Index, s.Right?.Shift))
                .Take(BeamWidth).ToList();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var choices = new List<Choice>();
        for (var state = beam[0]; state.Added is not null; state = state.Parent!) choices.Add(state.Added);
        var tracks = BuildTracks(choices);
        var replay = new HandReplay(tracks);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, replay.Events);
        var results = new List<PlaybackEvent>();
        for (var time = Math.Min(0, strokes.FirstOrDefault()?.Start ?? 0); time < session.FinishTime; time += 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.AddRange(session.Advance(time).Where(e => e.IsFinal));
        }
        results.AddRange(session.Advance(session.FinishTime + .1).Where(e => e.IsFinal));
        return new(chart, tracks, replay, choices.OrderBy(c => c.Start).Select(c =>
            new PlannedStroke(c.Stroke.Note.Id, c.Hand, c.Start, c.End, c.Stroke.Channel,
                c.Stroke.RotationDirection != 0 ? HandTechnique.Rotation : c.Stroke.Note.Kind == NoteKind.Slide ? HandTechnique.Slide : HandTechnique.Strike)).ToArray(),
            results, session.Achievement.Percentage, session.DXScore,
            gaps.Where(g => results.Any(e => e.Note.Id == g.NoteId && e.Grade != AchievementGrade.Perfect)).DistinctBy(g => g.NoteId).ToArray());
    }

    private static Stroke[]? CreateRotations(Stroke[] strokes, bool includeShortSweeps, bool allowSkippedKeys = false)
    {
        var heads = strokes.Where(s => s.Note.Kind != NoteKind.Slide).ToArray();
        var changed = false;
        var skipped = false;
        var result = strokes.ToArray();
        for (var start = 0; start < heads.Length - 2; start++)
        {
            if (heads[start].Note.Kind != NoteKind.Tap || heads[start].TwoHandChord) continue;
            var step = ButtonStep(heads[start].Note.StartPosition, heads[start + 1].Note.StartPosition);
            if (Math.Abs(step) is < 1 || Math.Abs(step) > (allowSkippedKeys ? 2 : 1)) continue;
            var direction = Math.Sign(step);
            var end = start + 1;
            while (end < heads.Length && heads[end].Note.Kind == NoteKind.Tap && !heads[end].TwoHandChord &&
                   heads[end].Start > heads[end - 1].Start &&
                   Math.Sign(ButtonStep(heads[end - 1].Note.StartPosition, heads[end].Note.StartPosition)) == direction &&
                   Math.Abs(ButtonStep(heads[end - 1].Note.StartPosition, heads[end].Note.StartPosition)) <= (allowSkippedKeys ? 2 : 1) &&
                   heads[end].Start - heads[end - 1].Start <= .2 * Math.Abs(ButtonStep(heads[end - 1].Note.StartPosition, heads[end].Note.StartPosition)) + Epsilon) end++;
            var fast = Enumerable.Range(start + 1, end - start - 1)
                .All(i => heads[i].Start - heads[i - 1].Start <= FastSweepInterval * Math.Abs(ButtonStep(heads[i - 1].Note.StartPosition, heads[i].Note.StartPosition)) + Epsilon);
            if (end - start < (fast && includeShortSweeps ? 3 : 4)) continue;
            skipped |= Enumerable.Range(start + 1, end - start - 1)
                .Any(i => Math.Abs(ButtonStep(heads[i - 1].Note.StartPosition, heads[i].Note.StartPosition)) == 2);
            MarkRotation(result, heads[start..end], direction, fast);
            changed = true;
            start = end - 1;
        }
        return changed && (!allowSkippedKeys || skipped) ? result : null;
    }

    private static int ButtonStep(int from, int to)
    {
        var step = (to - from + 8) % 8;
        return step > 4 ? step - 8 : step;
    }

    private static Stroke[]? CreatePairedSweeps(Stroke[] strokes)
    {
        var groups = strokes.Where(s => s.Note.Kind != NoteKind.Slide).GroupBy(s => s.Start)
            .Select(g => g.OrderBy(s => s.Note.StartPosition).ToArray()).ToArray();
        static bool IsPair(Stroke[] group) => group.Length == 2 && group.All(s => s.Note.Kind == NoteKind.Tap) &&
            group[0].Note.StartPosition != group[1].Note.StartPosition;
        var result = strokes.ToArray();
        var changed = false;
        var joinedSingles = new HashSet<int>();
        for (var start = 0; start < groups.Length - 2; start++)
        {
            if (!IsPair(groups[start]) || !IsPair(groups[start + 1])) continue;
            var first = groups[start];
            var next = groups[start + 1];
            var directions = Array.Empty<int>();
            foreach (var order in new[] { new[] { 0, 1 }, new[] { 1, 0 } })
            {
                var candidate = order.Select((index, lane) =>
                    (next[index].Note.StartPosition - first[lane].Note.StartPosition + 8) % 8).ToArray();
                if (!candidate.All(d => d is 1 or 7)) continue;
                directions = candidate.Select(d => d == 1 ? 1 : -1).ToArray();
                break;
            }
            if (directions.Length == 0) continue;
            var sequences = first.Select(s => new List<Stroke> { s }).ToArray();
            var end = start + 1;
            while (end < groups.Length && IsPair(groups[end]) &&
                   groups[end][0].Start > groups[end - 1][0].Start &&
                   groups[end][0].Start - groups[end - 1][0].Start <= FastSweepInterval + Epsilon)
            {
                // 反向扫动不能在同一段外圈上迎面穿过另一只手。
                var separation = ((sequences[1][^1].Note.StartPosition - sequences[0][^1].Note.StartPosition) * directions[0] + 8) % 8;
                if (directions[0] != directions[1] && separation <= 2) break;
                var following = sequences.Select((sequence, lane) => groups[end].SingleOrDefault(s =>
                    s.Note.StartPosition == (sequence[^1].Note.StartPosition - 1 + directions[lane] + 8) % 8 + 1)).ToArray();
                if (following.Any(s => s is null) || following[0] == following[1]) break;
                for (var lane = 0; lane < 2; lane++) sequences[lane].Add(following[lane]!);
                end++;
            }
            if (end - start < 3) continue;
            foreach (var before in new[] { true, false })
            {
                var index = before ? start - 1 : end;
                if (index < 0 || index >= groups.Length || groups[index] is not [var single] ||
                    single.Note.Kind != NoteKind.Tap || joinedSingles.Contains(single.Note.Id)) continue;
                var interval = before ? first[0].Start - single.Start : single.Start - groups[end - 1][0].Start;
                if (interval <= 0 || interval > FastSweepInterval + Epsilon) continue;
                var lane = Enumerable.Range(0, 2).Where(i => before
                        ? ButtonStep(single.Note.StartPosition, sequences[i][0].Note.StartPosition) == directions[i]
                        : ButtonStep(sequences[i][^1].Note.StartPosition, single.Note.StartPosition) == directions[i])
                    .OrderByDescending(i => sequences[i].Sum(s => SlidePath.ButtonPosition(s.Note.StartPosition).X) * SlidePath.ButtonPosition(single.Note.StartPosition).X)
                    .Select(i => (int?)i).FirstOrDefault();
                if (lane is not { } chosen) continue;
                if (before) sequences[chosen].Insert(0, single); else sequences[chosen].Add(single);
                joinedSingles.Add(single.Note.Id);
            }
            // 双押分别延伸成两条连续轨迹，每条保持同手，不能合成单手掌盖。
            for (var lane = 0; lane < 2; lane++) MarkRotation(result, sequences[lane].ToArray(), directions[lane], true, keepHand: true);
            changed = true;
            start = end - 1;
        }
        return changed ? result : null;
    }

    private static void MarkRotation(Stroke[] result, Stroke[] sequence, int direction, bool fast, bool keepHand = false)
    {
        for (var i = 0; i < sequence.Length; i++)
        {
            var stroke = sequence[i];
            var centrePoint = SlidePath.ButtonPosition(stroke.Note.StartPosition);
            var centre = Math.Atan2(centrePoint.Y, centrePoint.X);
            // 按四指实际覆盖计算进入外键的角度，让按下边沿落在音符时刻。
            var low = 0d;
            var high = Math.PI / 4;
            for (var step = 0; step < 24; step++)
            {
                var offset = (low + high) / 2;
                var p = Ring(centre - direction * offset);
                var touches = Fingers(p, InputChannel.Button).Any(f => ContactMapper.AreasAt(InputChannel.Button,
                    new(p.X + f.Offset.X, p.Y + f.Offset.Y), f.Radius).Contains(stroke.Note.StartPosition.ToString()));
                if (touches) low = offset; else high = offset;
            }
            var position = Ring(centre - direction * (low - .001));
            if (i == 0)
            {
                var touched = Fingers(position, InputChannel.Button).SelectMany(f => ContactMapper.AreasAt(InputChannel.Button,
                    new(position.X + f.Offset.X, position.Y + f.Offset.Y), f.Radius)).ToHashSet();
                if (result.Any(s => s.Channel == InputChannel.Button && s.Note.StartPosition != stroke.Note.StartPosition &&
                        s.Note.Time >= stroke.Start - Epsilon && s.Note.Time <= stroke.Start + HeadJudgements.EarlyInputWindow + Epsilon &&
                        touched.Contains(s.Note.StartPosition.ToString()))) position = Ring(centre);
            }
            result[stroke.Index] = stroke with
            {
                RotationDirection = direction, RotationPreviousId = i > 0 ? sequence[i - 1].Note.Id : null,
                RotationGroup = sequence[0].Note.Id, RotationStart = sequence[0].Start, FastSweep = fast,
                KeepRotationHand = keepHand,
                Points = [new(stroke.Start, position), new(stroke.End, position)]
            };
        }
    }

    private static ChartPoint Ring(double angle) => new(1.12 * Math.Cos(angle), 1.12 * Math.Sin(angle));
    private static double RotationAngle(ChartPoint from, ChartPoint to, int direction) =>
        ((Math.Atan2(to.Y, to.X) - Math.Atan2(from.Y, from.X)) * direction + 2 * Math.PI) % (2 * Math.PI);
    private static bool ContinuesRotation(Choice? before, Choice next) => before is not null &&
        next.Stroke.RotationPreviousId == before.Stroke.Note.Id && next.Stroke.RotationDirection == before.Stroke.RotationDirection;

    private static void MarkRepeats(Stroke[] strokes)
    {
        // 连续单键段保持用手；达到速度和长度阈值后才优先交互。
        for (var start = 0; start < strokes.Length; start++)
        {
            if (strokes[start].Note.Kind != NoteKind.Tap || strokes[start].TwoHandChord) continue;
            var end = start + 1;
            while (end < strokes.Length && strokes[end].Note.Kind == NoteKind.Tap && !strokes[end].TwoHandChord &&
                strokes[end].Note.StartPosition == strokes[start].Note.StartPosition &&
                strokes[end].Start - strokes[end - 1].Start is > 0 and <= .5) end++;
            for (var i = start + 1; i < end; i++)
            {
                var interval = strokes[i].Start - strokes[i - 1].Start;
                var fast = interval < FastRepeatInterval - Epsilon &&
                    (i > start + 1 && strokes[i - 1].Start - strokes[i - 2].Start < FastRepeatInterval - Epsilon ||
                     i + 1 < end && strokes[i + 1].Start - strokes[i].Start < FastRepeatInterval - Epsilon);
                strokes[i] = strokes[i] with { RepeatPreviousId = strokes[i - 1].Note.Id, FastRepeat = fast };
            }
            start = end - 1;
        }
    }

    private static SearchState? Assign(SearchState state, Choice choice)
    {
        if (choice.Stroke.KeepRotationHand)
            for (var previous = state; previous.Added is { } added && added.Stroke.Start >= choice.Stroke.RotationStart - Epsilon; previous = previous.Parent!)
                if (added.Stroke.RotationGroup == choice.Stroke.RotationGroup)
                {
                    if (added.Hand != choice.Hand) return null;
                    break;
                }
        var before = choice.Hand == HandSide.Left ? state.Left : state.Right;
        var other = choice.Hand == HandSide.Left ? state.Right : state.Left;
        var singleHandTouches = false;
        if (choice.Stroke.TwoHandChord)
            for (var previous = state; previous.Added is { } added && added.Stroke.Start >= choice.Stroke.Start - Epsilon; previous = previous.Parent!)
                if (added.Stroke.TwoHandChord && added.Stroke.Note.Time == choice.Stroke.Note.Time && added.Hand == choice.Hand)
                {
                    // 另一手在整个 Touch CP 窗内持续占用时，由空闲手依次击打；不合并接触面。
                    singleHandTouches = choice.Stroke.Note.Kind == NoteKind.Touch && added.Stroke.Note.Kind == NoteKind.Touch &&
                        other is not null && other.Stroke.Note.Kind is NoteKind.Hold or NoteKind.TouchHold or NoteKind.Slide &&
                        other.Start < choice.Stroke.Note.Time - .15 && other.End > choice.Stroke.Note.Time + .15;
                    if (!singleHandTouches) return null;
                }
        var position = before?.Stroke.Last ?? new ChartPoint(choice.Hand == HandSide.Left ? -.5 : .5, .5);
        var distance = position.DistanceTo(choice.Stroke.First);
        var gap = before is null ? 1 : choice.Start - before.End;
        var rotation = ContinuesRotation(before, choice);
        if (rotation)
        {
            gap = choice.Start - before!.Start;
            distance = 1.12 * RotationAngle(before.Stroke.First, choice.Stroke.First, choice.Stroke.RotationDirection);
        }
        // 连续扫键的移动包含前一键的接触阶段，不需要逐键停下并抬手。
        if (gap < PlaybackSession.LogicStep - Epsilon || distance > gap * (rotation ? 30 : MaxTravelSpeed) + Epsilon) return null;
        var cost = distance * distance / Math.Min(gap, 1) * (rotation ? .045 : .15) + (singleHandTouches ? 10 : 0);
        var targetX = choice.Stroke.RotationDirection == 0 ? choice.Stroke.First.X : SlidePath.ButtonPosition(choice.Stroke.Note.StartPosition).X;
        var wrongSide = choice.Hand == HandSide.Left ? targetX : -targetX;
        cost += Math.Max(0, wrongSide) * (choice.Stroke.RotationDirection != 0 ? 4 : .8) + Math.Abs(choice.Shift) * 3;
        if (rotation && wrongSide <= 0) cost -= .5;
        if (choice.Stroke.RepeatPreviousId is { } previousId)
        {
            var sameHand = before?.Stroke.Note.Id == previousId;
            var otherHand = other?.Stroke.Note.Id == previousId;
            if (choice.Stroke.FastRepeat && sameHand) cost += 3;
            if (!choice.Stroke.FastRepeat && otherHand) cost += 2;
        }
        if (other is not null)
        {
            var otherX = other.Stroke.Last.X;
            var crossing = choice.Hand == HandSide.Left ? choice.Stroke.First.X - otherX : otherX - choice.Stroke.First.X;
            cost += Math.Max(0, crossing - .1) * 2;
        }
        return new(choice.Hand == HandSide.Left ? choice : state.Left,
            choice.Hand == HandSide.Right ? choice : state.Right, state.Cost + cost, state, choice);
    }

    private static IEnumerable<Stroke> CreateStrokes(Chart chart, CancellationToken token)
    {
        var doubleTimes = chart.Notes.Where(n => n.Kind != NoteKind.Slide).GroupBy(n => n.Time)
            .Where(group => group.Count() == 2).Select(group => group.Key).ToHashSet();
        return CreateMotions(chart, token).Select(m => new Stroke(0, m.Note, m.Channel, m.Points,
            m.Note.Kind != NoteKind.Slide && doubleTimes.Contains(m.Note.Time)));
    }
    private static HandTrack[] BuildTracks(IEnumerable<Choice> choices)
    {
        var tracks = new List<HandTrack>();
        foreach (var hand in new[] { HandSide.Left, HandSide.Right })
        {
            var ordered = choices.Where(c => c.Hand == hand).OrderBy(c => c.Start).ToArray();
            if (ordered.Length == 0) continue;
            var frames = new List<HandFrame> { new(Math.Min(0, ordered[0].Start - 1), new(hand == HandSide.Left ? -.5 : .5, .5), []) };
            Choice? previous = null;
            foreach (var choice in ordered)
            {
                if (ContinuesRotation(previous, choice))
                {
                    frames.RemoveAt(frames.Count - 1);
                    var angle = Math.Atan2(previous!.Stroke.First.Y, previous.Stroke.First.X);
                    var sweep = RotationAngle(previous.Stroke.First, choice.Stroke.First, choice.Stroke.RotationDirection);
                    var count = Math.Max(2, (int)Math.Ceiling((choice.Start - previous.Start) * PlaybackSession.LogicHz));
                    for (var i = 1; i < count; i++)
                    {
                        var p = Ring(angle + choice.Stroke.RotationDirection * sweep * i / count);
                        frames.Add(new(previous.Start + (choice.Start - previous.Start) * i / count, p, Fingers(p, InputChannel.Button)));
                    }
                    frames.AddRange(ContactFrames(choice.Stroke.Points, choice.Stroke.Channel, choice.Shift));
                    previous = choice;
                    continue;
                }
                var last = frames[^1];
                var travel = Math.Max(.06, last.Position.DistanceTo(choice.Stroke.First) / 5);
                var depart = Math.Max(last.Time, choice.Start - travel);
                if (depart > last.Time + Epsilon) frames.Add(new(depart, last.Position, []));
                frames.AddRange(ContactFrames(choice.Stroke.Points, choice.Stroke.Channel, choice.Shift));
                previous = choice;
            }
            frames.Add(new(frames[^1].Time + .25, frames[^1].Position, []));
            tracks.Add(new(hand, frames));
        }
        return tracks.ToArray();
    }

}
