using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HoldSweepLandingTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void SettlesOnTheHeldButtonAndLeavesTheEntryNeighbourPlayable(bool human, string source)
    {
        var chart = SimaiParser.ParseChart(source, first: 1);
        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var hold = chart.Notes.Single(n => n.Kind == NoteKind.Hold);
        var holding = Assert.Single(plan.Strokes, s => s.NoteId == hold.Id);
        var neighbour = chart.Notes.Last().StartPosition.ToString();
        for (var time = hold.Time + .06; time < hold.EndTime - .1; time += .02)
        {
            var pose = plan.Replay.PoseAt(holding.Hand, time)!;
            var buttons = pose.Contacts.Where(c => c.Channel == InputChannel.Button)
                .SelectMany(c => ContactMapper.AreasAt(c.Channel,
                    new(pose.Position.X + c.Offset.X, pose.Position.Y + c.Offset.Y), c.Radius)).ToHashSet();
            Assert.Contains(hold.StartPosition.ToString(), buttons);
            Assert.DoesNotContain(neighbour, buttons);
        }
        var session = new PlaybackSession(chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var human in new[] { false, true })
            foreach (var direction in new[] { -1, 1 })
                for (var first = 1; first <= 8; first++)
                {
                    int Key(int offset) => (first - 1 + offset * direction + 16) % 8 + 1;
                    yield return [human, $"(110){{64}}{first}/{Key(-1)},{Key(1)},{Key(2)}h[#0.5],,,{Key(1)},,,{Key(1)},E"];
                }
    }
}
