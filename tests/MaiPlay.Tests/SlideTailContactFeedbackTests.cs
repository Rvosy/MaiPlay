using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class SlideTailContactFeedbackTests
{
    public static IEnumerable<object[]> Cases => Enumerable.Range(0, 8)
        .SelectMany(rotation => new[] { 0d, 1d / 480 }.SelectMany(phase => new[] { false, true }
            .SelectMany(hold => new[] { false, true }.Select(human => new object[] { rotation, phase, hold, human }))));

    [Theory]
    [MemberData(nameof(Cases))]
    public void SlideAfterChordClearsItsTailBeforeTheFollowingHeadWindow(int rotation, double phase, bool hold, bool human)
    {
        int Position(int button) => (button + rotation - 1) % 8 + 1;
        var following = hold ? $"{Position(4)}h[#0.15]" : Position(4).ToString();
        var chart = SimaiParser.ParseChart(
            $"(200){{#0.3}}{Position(8)}$-{Position(4)}[0.3##0.15],{Position(1)}/{Position(8)}," +
            $"{{#0.075}}{Position(6)},{following},{Position(6)},{Position(3)},{Position(6)},{Position(2)},{Position(6)},E",
            first: 1.2 + phase);

        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);

        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e =>
            $"{e.Note.Time:F6} {e.Note.Source}: {e.Grade}/{e.DifferenceSeconds}")));
        Assert.Empty(plan.Gaps);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }
}
