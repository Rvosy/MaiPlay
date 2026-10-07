namespace MaiPlay.Core;

public sealed record TechniqueCandidate(string Name, IReadOnlyList<HandSide?> Hands, int SweepDirection = 0,
    double PreferenceCost = 0, IReadOnlyList<int>? SweepDirections = null, IReadOnlyList<int>? DemandOrder = null);

/// <summary>手序与连续扫键候选。偏好只用于搜索排序，可行性由占手、轨迹和判定器决定。</summary>
public static class TechniqueCandidates
{
    public static IReadOnlyList<TechniqueCandidate> For(PatternSegment segment)
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
            case PatternKind.ChordStream:
                foreach (var first in new[] { HandSide.Left, HandSide.Right })
                    candidates.Add(new(first == HandSide.Left ? "paired-sweep-left" : "paired-sweep-right",
                        Enumerable.Range(0, count).Select(i => (HandSide?)(i % 2 == 0 ? first : Other(first))).ToArray(),
                        SweepDirections: Enumerable.Range(0, count).Select(i => i % 2 == 0 ? segment.Direction : segment.SecondDirection).ToArray()));
                break;
            case PatternKind.Chord when count == 2:
                Alternating();
                break;
            case PatternKind.Jack:
                var fastRepeat = segment.Demands.Zip(segment.Demands.Skip(1), (a, b) => b.Start - a.Start)
                    .All(interval => interval < NormalHandPlanner.FastRepeatInterval - 1e-8);
                SameHand("repeat", fastRepeat ? .3 : 0);
                Alternating(fastRepeat ? 0 : .15);
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
                break;
        }
        var freeHands = Enumerable.Repeat<HandSide?>(null, count).ToArray();
        var freeCost = candidates.Count == 0 ? 0 : .25;
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
        }
        else candidates.Add(new("free", freeHands, PreferenceCost: freeCost));
        return candidates;
    }

    private static HandSide Other(HandSide hand) => hand == HandSide.Left ? HandSide.Right : HandSide.Left;
}
