using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanHoldSweepTests
{
    [Theory]
    [MemberData(nameof(ChordToHoldCases))]
    [MemberData(nameof(OccupiedHandCases))]
    public void SweepReachesAndKeepsItsFinalHold(string source)
    {
        Verify(HumanPlanner.Plan(SimaiParser.ParseChart(source, first: 1)));
    }

    public static IEnumerable<object[]> ChordToHoldCases()
    {
        foreach (var bpm in new[] { 110, 120 })
            foreach (var direction in new[] { -1, 1 })
                for (var first = 1; first <= 8; first++)
                    foreach (var reverseChord in new[] { false, true })
                    {
                        int Key(int offset) => (first - 1 + offset * direction + 16) % 8 + 1;
                        var chord = reverseChord ? $"{Key(-1)}/{first}" : $"{first}/{Key(-1)}";
                        yield return [FormattableString.Invariant($"({bpm}){{64}}{chord},{Key(1)},{Key(2)}h[#{6 * 60.0 / bpm / 16}],E")];
                    }
    }

    public static IEnumerable<object[]> OccupiedHandCases()
    {
        foreach (var bpm in new[] { 200, 240 })
            foreach (var direction in new[] { -1, 1 })
                for (var first = 1; first <= 8; first++)
                {
                    int Key(int offset) => (first - 1 + offset * direction + 16) % 8 + 1;
                    yield return [$"({bpm}){{32}}Ch[#1]/{first},{Key(1)},{Key(2)},{Key(3)},{Key(4)}h[#0.2],E"];
                }
    }

    internal static void Verify(HandPlan plan)
    {
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        if (plan.Chart.Notes.Any(n => n.Kind == NoteKind.TouchHold))
        {
            var hold = Assert.Single(plan.Strokes, s => plan.Chart.Notes[s.NoteId].Kind == NoteKind.Hold);
            var previous = plan.Strokes.Where(s => plan.Chart.Notes[s.NoteId].Kind == NoteKind.Tap)
                .MaxBy(s => plan.Chart.Notes[s.NoteId].Time)!;
            Assert.Equal(previous.Hand, hold.Hand);
            for (var i = 0; i <= 10; i++)
            {
                var time = previous.Start + (hold.Start - previous.Start) * i / 10;
                var pose = plan.Replay.PoseAt(hold.Hand, time)!;
                Assert.True(pose.Position.DistanceTo(default) >= 1.1, $"Sweep left the outer ring at {time}.");
                Assert.Contains(pose.Contacts, c => c.Channel == InputChannel.Button);
            }
        }
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var session = new PlaybackSession(plan.Chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(plan.Chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(plan.DXScore, session.DXScore);
    }
}
