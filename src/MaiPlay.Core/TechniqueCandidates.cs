namespace MaiPlay.Core;

public sealed record TechniqueCandidate(string Name, IReadOnlyList<HandSide?> Hands, int SweepDirection = 0,
    double PreferenceCost = 0, IReadOnlyList<int>? SweepDirections = null, IReadOnlyList<int>? DemandOrder = null,
    bool RequiresSweepContinuation = false);

/// <summary>手序与连续扫键候选。偏好只用于搜索排序，可行性由占手、轨迹和判定器决定。</summary>
public static class TechniqueCandidates
{
    public static IReadOnlyList<TechniqueCandidate> For(PatternSegment segment,
        double preferredStrikeInterval = NormalHandPlanner.FastRepeatInterval, bool refineSweepTiming = false)
    {
        var count = segment.Demands.Count;
        var candidates = new List<TechniqueCandidate>();
        void Alternating(double cost = 0)
        {
            foreach (var first in new[] { HandSide.Left, HandSide.Right })
                candidates.Add(new(first == HandSide.Left ? "alternate-left" : "alternate-right",
                    Enumerable.Range(0, count).Select(i => (HandSide?)(i % 2 == 0 ? first : Other(first))).ToArray(), PreferenceCost: cost));
        }
        void SameHand(string name, double cost, int direction = 0)
        {
            foreach (var hand in new[] { HandSide.Left, HandSide.Right })
                candidates.Add(new($"{name}-{hand.ToString().ToLowerInvariant()}",
                    Enumerable.Repeat<HandSide?>(hand, count).ToArray(), direction, cost));
        }

        switch (segment.Kind)
        {
            case PatternKind.SingleTap:
            case PatternKind.HoldLock when segment.Demands[0].Note.Kind == NoteKind.Hold:
                foreach (var direction in new[] { -1, 1 })
                    candidates.Add(new(direction < 0 ? "continue-sweep-backward" : "continue-sweep-forward",
                        [null], direction, RequiresSweepContinuation: true));
                break;
            case PatternKind.HoldSweep:
                var beginsWithChord = Math.Abs(segment.Demands[1].Start - segment.Demands[0].Start) < 1e-8;
                Alternating(.1);
                foreach (var hand in new[] { HandSide.Left, HandSide.Right })
                    candidates.Add(new($"hold-sweep-{hand.ToString().ToLowerInvariant()}",
                        Enumerable.Range(0, count).Select(i => (HandSide?)(beginsWithChord && i == 1 ? Other(hand) : hand)).ToArray(),
                        SweepDirections: Enumerable.Range(0, count).Select(i => beginsWithChord && i == 1 ? 0 : segment.Direction).ToArray()));
                break;
            case PatternKind.ChordStream:
                var prefix = Math.Abs(segment.Demands[0].Start - segment.Demands[1].Start) < 1e-8 ? 0 : 1;
                foreach (var first in new[] { HandSide.Left, HandSide.Right })
                    foreach (var initialLane in prefix == 0 ? new[] { 0 } : [0, 1])
                    {
                        if (prefix != 0)
                        {
                            var step = PatternAnalyzer.ButtonStep(segment.Demands[0].Note.StartPosition,
                                segment.Demands[prefix + initialLane].Note.StartPosition);
                            var direction = initialLane == 0 ? segment.Direction : segment.SecondDirection;
                            if (Math.Sign(step) != direction || Math.Abs(step) is not (1 or 2) ||
                                segment.Demands[1].Start - segment.Demands[0].Start >
                                NormalHandPlanner.FastSweepInterval * Math.Abs(step) + 1e-8) continue;
                        }
                        int Lane(int index) => index < prefix ? initialLane : (index - prefix) % 2;
                        candidates.Add(new(first == HandSide.Left ? "paired-sweep-left" : "paired-sweep-right",
                            Enumerable.Range(0, count).Select(i => (HandSide?)(Lane(i) == 0 ? first : Other(first))).ToArray(),
                            SweepDirections: Enumerable.Range(0, count).Select(i => Lane(i) == 0 ? segment.Direction : segment.SecondDirection).ToArray()));
                    }
                break;
            case PatternKind.Chord when count == 2:
                Alternating();
                foreach (var firstDirection in segment.Demands[0].Note.Kind is NoteKind.Tap or NoteKind.Hold ? new[] { -1, 0, 1 } : [0])
                    foreach (var secondDirection in segment.Demands[1].Note.Kind is NoteKind.Tap or NoteKind.Hold ? new[] { -1, 0, 1 } : [0])
                    {
                        if (firstDirection == 0 && secondDirection == 0) continue;
                        int[] directions = [firstDirection, secondDirection];
                        candidates.Add(new("continue-sweep-chord", [null, null], SweepDirections: directions,
                            DemandOrder: Enumerable.Range(0, count).OrderBy(i => directions[i] == 0).ToArray(),
                            RequiresSweepContinuation: true));
                    }
                break;
            case PatternKind.Jack:
                SameHand("repeat", 0);
                Alternating();
                foreach (var first in new[] { HandSide.Left, HandSide.Right })
                {
                    var hands = new HandSide?[count];
                    var hand = first;
                    hands[0] = hand;
                    for (var i = 1; i < count; i++)
                    {
                        if (segment.Demands[i].Start - segment.Demands[i - 1].Start < preferredStrikeInterval - 1e-8)
                            hand = Other(hand);
                        hands[i] = hand;
                    }
                    if (!candidates.Any(c => c.Hands.SequenceEqual(hands)))
                        candidates.Add(new(first == HandSide.Left ? "rhythmic-repeat-left" : "rhythmic-repeat-right", hands));
                }
                break;
            case PatternKind.Trill:
            case PatternKind.Anchor:
            case PatternKind.Stream:
                Alternating();
                break;
            case PatternKind.Stair:
            case PatternKind.Rotation:
                var fastSweep = segment.Demands.Zip(segment.Demands.Skip(1), (a, b) =>
                    (b.Start - a.Start) / Math.Abs(PatternAnalyzer.ButtonStep(a.Note.StartPosition, b.Note.StartPosition)))
                    .All(interval => interval <= NormalHandPlanner.FastSweepInterval + 1e-8);
                Alternating(.1);
                SameHand("strike", .2);
                SameHand("sweep", fastSweep ? 0 : .15, segment.Direction);
                foreach (var swap in new[] { false, true })
                    candidates.Add(new(swap ? "relay-crossed" : "relay", segment.Demands.Select(d =>
                    {
                        var hand = d.Note.StartPosition <= 4 ? HandSide.Right : HandSide.Left;
                        return (HandSide?)(swap ? Other(hand) : hand);
                    }).ToArray(), segment.Direction, fastSweep ? .1 : 0));
                // 细化恢复时在段内交接一次，让扫尾越过半圈分界后仍可由当前手完成。
                if (fastSweep && refineSweepTiming)
                    foreach (var split in new[] { count / 2, (count + 1) / 2 }.Distinct())
                        foreach (var first in new[] { HandSide.Left, HandSide.Right })
                        {
                            var hands = Enumerable.Range(0, count)
                                .Select(i => (HandSide?)(i < split ? first : Other(first))).ToArray();
                            if (candidates.Any(c => c.SweepDirection == segment.Direction && c.Hands.SequenceEqual(hands))) continue;
                            candidates.Add(new($"relay-split-{split}-{first.ToString().ToLowerInvariant()}",
                                hands, segment.Direction, .1));
                        }
                break;
        }
        var freeHands = Enumerable.Repeat<HandSide?>(null, count).ToArray();
        var freeCost = candidates.Count == 0 || segment.Kind is PatternKind.SingleTap or PatternKind.HoldLock ? 0 : .25;
        if (segment.Kind == PatternKind.Chord && segment.Demands.All(d => d.Note.Kind is NoteKind.Touch or NoteKind.TouchHold))
        {
            var indices = Enumerable.Range(0, count).ToArray();
            bool Holds(int index) => segment.Demands[index].Note.Kind == NoteKind.TouchHold;
            double X(int index) => ContactTrajectoryCompiler.TouchCentre(segment.Demands[index].Target).X;
            candidates.Add(new("free", freeHands, PreferenceCost: freeCost, DemandOrder: indices.OrderBy(Holds).ToArray()));
            candidates.Add(new("touch-left-to-right", freeHands, PreferenceCost: freeCost,
                DemandOrder: indices.OrderBy(Holds).ThenBy(X).ToArray()));
            candidates.Add(new("touch-right-to-left", freeHands, PreferenceCost: freeCost,
                DemandOrder: indices.OrderBy(Holds).ThenByDescending(X).ToArray()));
            if (count > 2)
            {
                var centres = segment.Demands.Select(d => ContactTrajectoryCompiler.TouchCentre(d.Target)).ToArray();
                // 同刻 Touch 没有固定手序，近邻遍历为短距离连续点按保留候选。
                foreach (var reverse in new[] { false, true })
                {
                    var remaining = indices.Where(i => !Holds(i)).ToList();
                    var order = new List<int>();
                    var position = new ChartPoint(reverse ? 1 : -1, 0);
                    while (remaining.Count > 0)
                    {
                        var next = remaining.MinBy(i => centres[i].DistanceTo(position));
                        order.Add(next);
                        remaining.Remove(next);
                        position = centres[next];
                    }
                    order.AddRange(indices.Where(Holds));
                    candidates.Add(new(reverse ? "touch-nearest-right" : "touch-nearest-left", freeHands,
                        PreferenceCost: freeCost, DemandOrder: order));
                }
            }
        }
        else candidates.Add(new("free", freeHands, PreferenceCost: freeCost));
        return candidates;
    }

    private static HandSide Other(HandSide hand) => hand == HandSide.Left ? HandSide.Right : HandSide.Left;
}
