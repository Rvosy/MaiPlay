using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanTouchCoverageTests
{
    [Fact]
    public void DenseTouchGroupCanReachMajorityWithoutExceedingSpeedLimit()
    {
        var chart = SimaiParser.ParseChart(
            "(120){#1}B1/Cf/E1/A1/D1/B2/E2/A2/D2/B3/E3/D3/B4/B5/B6/B7/E7/A7/D7/B8/E8/A8/D8,E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();

        Assert.Equal(23, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Empty(plan.Gaps);
        Assert.True(results.Count(e => e.Reason != "touch-group") * 2 > chart.Notes.Count);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks).SpeedLimitViolations);
    }
}
