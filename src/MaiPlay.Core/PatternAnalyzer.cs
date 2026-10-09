namespace MaiPlay.Core;

public enum PatternKind { SingleTap, Chord, ChordStream, Jack, Trill, Anchor, Stair, Rotation, Stream, HoldLock, SimpleSlide, Wifi, Touch, HoldSweep }

public sealed record PatternSegment(PatternKind Kind, IReadOnlyList<GameplayDemand> Demands, int Direction = 0, int SecondDirection = 0)
{
    public double Start => Demands[0].Start;
    public double End => Demands.Max(d => d.End);
}

/// <summary>按时序切分配置；每个需求只属于一段，长音符的占手仍由搜索状态处理。</summary>
public static class PatternAnalyzer
{
    private const double MaximumSequenceGap = .5;
    private const double Epsilon = 1e-8;

    public static IReadOnlyList<PatternSegment> Analyze(IReadOnlyList<GameplayDemand> demands,
        Func<GameplayDemand, double>? startTime = null)
    {
        var time = startTime ?? (d => d.Start);
        var ordered = demands.OrderBy(time).ThenBy(d => d.Id).ToArray();
        var segments = new List<PatternSegment>();
        for (var index = 0; index < ordered.Length;)
        {
            if (IdentifyHoldSweep(ordered, index, time) is { } holdSweep)
            {
                segments.Add(holdSweep);
                index += holdSweep.Demands.Count;
                continue;
            }
            if (IdentifyChordStream(ordered, index, time) is { } chordStream)
            {
                segments.Add(chordStream);
                index += chordStream.Demands.Count;
                continue;
            }
            var end = index + 1;
            while (end < ordered.Length && Math.Abs(time(ordered[end]) - time(ordered[index])) < Epsilon) end++;
            if (end > index + 1)
            {
                // Slide 的起滑时刻可以与头部击打相同，但两者不是双押约束。
                var heads = ordered[index..end].Where(d => d.Note.Kind != NoteKind.Slide).ToArray();
                var slides = ordered[index..end].Where(d => d.Note.Kind == NoteKind.Slide).ToArray();
                if (slides.Any(d => d.Note.SlidePath!.IsWifi))
                {
                    // 外键先收尾，Touch 再检查滑动能否顺带覆盖，避免双方互相抢占双手。
                    var buttons = heads.Where(d => d.Channel == InputChannel.Button).ToArray();
                    var touches = heads.Where(d => d.Channel == InputChannel.Touch).ToArray();
                    if (buttons.Length > 0) segments.Add(buttons.Length > 1 ? new(PatternKind.Chord, buttons) : Single(buttons[0]));
                    // 先分配需要双手的 Wifi，让同刻普通分支检查这次接触能否顺带完成。
                    foreach (var slide in slides.OrderByDescending(d => d.Note.SlidePath!.IsWifi)) segments.Add(Single(slide));
                    if (touches.Length > 0) segments.Add(touches.Length > 1 ? new(PatternKind.Chord, touches) : Single(touches[0]));
                }
                else
                {
                    if (heads.Length > 0) segments.Add(heads.Length > 1 ? new(PatternKind.Chord, heads) : Single(heads[0]));
                    foreach (var slide in slides) segments.Add(Single(slide));
                }
                index = end;
                continue;
            }
            if (ordered[index].Note.Kind != NoteKind.Tap)
            {
                segments.Add(Single(ordered[index++]));
                continue;
            }
            while (end < ordered.Length && ordered[end].Note.Kind == NoteKind.Tap &&
                   time(ordered[end]) - time(ordered[end - 1]) <= MaximumSequenceGap + Epsilon &&
                   time(ordered[end]) > time(ordered[end - 1]) + Epsilon &&
                   (end == ordered.Length - 1 || time(ordered[end + 1]) > time(ordered[end]) + Epsilon) &&
                   IdentifyHoldSweep(ordered, end, time) is null) end++;
            SplitTapRun(ordered[index..end], segments, time);
            index = end;
        }
        return segments;
    }

    private static PatternSegment? IdentifyHoldSweep(GameplayDemand[] demands, int start, Func<GameplayDemand, double> time)
    {
        if (demands[start].Note.Kind != NoteKind.Tap) return null;
        var paired = IsPair(demands, start, time);
        var next = start + (paired ? 2 : 1);
        if (!paired && next < demands.Length && time(demands[next]) <= time(demands[start]) + Epsilon) return null;
        PatternSegment? best = null;
        var bestDistance = int.MaxValue;
        for (var seed = start; seed < start + (paired ? 2 : 1); seed++)
        {
            var sequence = new List<GameplayDemand> { demands[seed] };
            if (paired) sequence.Add(demands[start + 1 - (seed - start)]);
            var previous = demands[seed];
            var direction = 0;
            var distance = 0;
            for (var index = next; index < demands.Length; index++)
            {
                var current = demands[index];
                if (current.Note.Kind is not (NoteKind.Tap or NoteKind.Hold) ||
                    (index + 1 < demands.Length && time(demands[index + 1]) <= time(current) + Epsilon)) break;
                var step = ButtonStep(previous.Note.StartPosition, current.Note.StartPosition);
                var interval = time(current) - time(previous);
                if (Math.Abs(step) is not (1 or 2) || (direction != 0 && Math.Sign(step) != direction) ||
                    interval <= Epsilon || interval > .2 * Math.Abs(step) + Epsilon) break;
                direction = Math.Sign(step);
                distance += Math.Abs(step);
                sequence.Add(current);
                if (current.Note.Kind == NoteKind.Hold)
                {
                    if (sequence.Count >= (paired ? 4 : 3) && distance < bestDistance)
                    {
                        best = new(PatternKind.HoldSweep, sequence.ToArray(), direction);
                        bestDistance = distance;
                    }
                    break;
                }
                previous = current;
            }
        }
        return best;
    }

    private static PatternSegment? IdentifyChordStream(GameplayDemand[] demands, int start, Func<GameplayDemand, double> time)
    {
        if (!IsPair(demands, start, time) && demands[start].Note.Kind == NoteKind.Tap &&
            IsPair(demands, start + 1, time))
        {
            var following = IdentifyChordStream(demands, start + 1, time);
            if (following is null || Math.Abs(time(following.Demands[0]) - time(following.Demands[1])) >= Epsilon) return null;
            var interval = time(following.Demands[0]) - time(demands[start]);
            for (var lane = 0; lane < 2; lane++)
            {
                var step = ButtonStep(demands[start].Note.StartPosition, following.Demands[lane].Note.StartPosition);
                var direction = lane == 0 ? following.Direction : following.SecondDirection;
                if (Math.Sign(step) == direction && Math.Abs(step) is 1 or 2 &&
                    interval <= NormalHandPlanner.FastSweepInterval * Math.Abs(step) + Epsilon)
                    return following with { Demands = new[] { demands[start] }.Concat(following.Demands).ToArray() };
            }
            return null;
        }
        if (!IsPair(demands, start, time) || !IsPair(demands, start + 2, time)) return null;
        var initial = demands[start..(start + 2)];
        PatternSegment? best = null;
        foreach (var swap in new[] { false, true })
        {
            var next = swap ? new[] { demands[start + 3], demands[start + 2] } : demands[(start + 2)..(start + 4)];
            var directions = Enumerable.Range(0, 2).Select(lane => Math.Sign(ButtonStep(initial[lane].Note.StartPosition, next[lane].Note.StartPosition))).ToArray();
            if (!CanFollow(initial, next, directions, time)) continue;
            var sequence = new List<GameplayDemand>(initial);
            sequence.AddRange(next);
            var previous = next;
            for (var index = start + 4; IsPair(demands, index, time, allowHolds: true); index += 2)
            {
                next = demands[index..(index + 2)];
                if (!CanFollow(previous, next, directions, time)) Array.Reverse(next);
                if (!CanFollow(previous, next, directions, time)) break;
                sequence.AddRange(next);
                previous = next;
                if (next.Any(d => d.Note.Kind == NoteKind.Hold)) break;
            }
            if (sequence.Count >= 6 && (best is null || sequence.Count > best.Demands.Count))
                best = new(PatternKind.ChordStream, sequence.ToArray(), directions[0], directions[1]);
        }
        return best;
    }

    private static bool IsPair(GameplayDemand[] demands, int start, Func<GameplayDemand, double> time, bool allowHolds = false) => start + 1 < demands.Length &&
        (demands[start].Note.Kind == NoteKind.Tap || allowHolds && demands[start].Note.Kind == NoteKind.Hold) &&
        (demands[start + 1].Note.Kind == NoteKind.Tap || allowHolds && demands[start + 1].Note.Kind == NoteKind.Hold) &&
        demands[start].Note.StartPosition != demands[start + 1].Note.StartPosition &&
        Math.Abs(time(demands[start]) - time(demands[start + 1])) < Epsilon &&
        (start == 0 || time(demands[start]) > time(demands[start - 1]) + Epsilon) &&
        (start + 2 == demands.Length || time(demands[start + 2]) > time(demands[start]) + Epsilon);

    private static bool CanFollow(GameplayDemand[] previous, GameplayDemand[] next, int[] directions, Func<GameplayDemand, double> time)
    {
        var interval = time(next[0]) - time(previous[0]);
        if (interval <= Epsilon) return false;
        var steps = Enumerable.Range(0, 2).Select(lane => ButtonStep(previous[lane].Note.StartPosition, next[lane].Note.StartPosition)).ToArray();
        if (steps.Where((step, lane) => Math.Sign(step) != directions[lane] || Math.Abs(step) is not (1 or 2) ||
                interval > NormalHandPlanner.FastSweepInterval * Math.Abs(step) + Epsilon).Any()) return false;
        if (directions[0] == directions[1]) return true;
        var separation = ((previous[1].Note.StartPosition - previous[0].Note.StartPosition) * directions[0] + 8) % 8;
        return separation > Math.Abs(steps[0]) + Math.Abs(steps[1]);
    }

    private static PatternSegment Single(GameplayDemand demand) => new(demand.Note.Kind switch
    {
        NoteKind.Hold or NoteKind.TouchHold => PatternKind.HoldLock,
        NoteKind.Slide when demand.Note.SlidePath!.IsWifi => PatternKind.Wifi,
        NoteKind.Slide => PatternKind.SimpleSlide,
        NoteKind.Touch => PatternKind.Touch,
        _ => PatternKind.SingleTap
    }, [demand]);

    private static void SplitTapRun(GameplayDemand[] run, List<PatternSegment> segments, Func<GameplayDemand, double> time)
    {
        for (var index = 0; index < run.Length;)
        {
            if (Identify(run, index, time) is { } pattern)
            {
                segments.Add(pattern);
                index += pattern.Demands.Count;
                continue;
            }
            var end = index + 1;
            while (end < run.Length && time(run[end]) - time(run[end - 1]) <= .2 + Epsilon && Identify(run, end, time) is null) end++;
            if (end - index >= 3) segments.Add(new(PatternKind.Stream, run[index..end]));
            else for (var i = index; i < end; i++) segments.Add(Single(run[i]));
            index = end;
        }
    }

    private static PatternSegment? Identify(GameplayDemand[] run, int start, Func<GameplayDemand, double> time)
    {
        if (run.Length - start < 3) return null;
        var first = run[start].Note.StartPosition;
        var end = start + 1;
        while (end < run.Length && run[end].Note.StartPosition == first) end++;
        if (end - start >= 3) return new(PatternKind.Jack, run[start..end]);

        var second = run[start + 1].Note.StartPosition;
        if (first != second)
        {
            end = start + 2;
            while (end < run.Length && run[end].Note.StartPosition == ((end - start) % 2 == 0 ? first : second)) end++;
            if (end - start >= 4) return new(PatternKind.Trill, run[start..end]);

            for (var anchorOffset = 0; anchorOffset < 2; anchorOffset++)
            {
                var anchor = run[start + anchorOffset].Note.StartPosition;
                end = start + 2;
                while (end < run.Length && ((end - start) % 2 == anchorOffset
                           ? run[end].Note.StartPosition == anchor : run[end].Note.StartPosition != anchor)) end++;
                if (end - start >= 5 && run[start..end].Where((_, i) => i % 2 != anchorOffset)
                        .Select(d => d.Note.StartPosition).Distinct().Count() > 1)
                    return new(PatternKind.Anchor, run[start..end]);
            }
        }

        var step = ButtonStep(first, second);
        if (Math.Abs(step) is not (1 or 2)) return null;
        var direction = Math.Sign(step);
        var distance = 0;
        end = start + 1;
        while (end < run.Length)
        {
            step = ButtonStep(run[end - 1].Note.StartPosition, run[end].Note.StartPosition);
            if (Math.Sign(step) != direction || Math.Abs(step) is not (1 or 2) ||
                time(run[end]) - time(run[end - 1]) > .2 * Math.Abs(step) + Epsilon) break;
            distance += Math.Abs(step);
            end++;
        }
        return end - start >= 3 ? new(distance >= 8 ? PatternKind.Rotation : PatternKind.Stair, run[start..end], direction) : null;
    }

    internal static int ButtonStep(int from, int to)
    {
        var step = (to - from + 8) % 8;
        return step > 4 ? step - 8 : step;
    }
}
