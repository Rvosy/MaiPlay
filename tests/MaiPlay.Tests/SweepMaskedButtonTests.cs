using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class SweepMaskedButtonTests
{
    public static IEnumerable<object[]> Cases => Enumerable.Range(0, 8).SelectMany(rotation =>
        new[] { 1d, 102.1518987342308 }.SelectMany(first => new[] { false, true }
            .Select(repeat => new object[] { rotation, first, repeat })));

    [Theory]
    [MemberData(nameof(Cases))]
    public void DoubleSweepReleasesTheButtonBeforeTheNextHandsPress(int rotation, double first, bool repeat)
    {
        int Key(int key) => (key - 1 + rotation) % 8 + 1;
        string Pair(int key) => $"{Key(key)}x/{Key(key + 1)}x";
        var text = repeat
            ? $"(155){{16}}{Pair(5)},{{8}}{Pair(5)},{Pair(7)},{Pair(1)},{{16}}{Pair(3)},{{4}}{Key(3)}x/{Key(4)}x-{Key(7)}[8:1],E"
            : $"(158){{8}}{Key(1)}{Key(2)},{Pair(3)},{Pair(5)},{Pair(7)},E";
        var chart = SimaiParser.ParseChart(text, first: first);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}/{e.Reason}")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);

        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }
}
