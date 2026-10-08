using System.Globalization;
using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class SweepRelayTailTests
{
    public static IEnumerable<object[]> RotatedSweeps()
    {
        foreach (var start in Enumerable.Range(1, 8))
        foreach (var direction in new[] { -1, 1 })
        foreach (var count in new[] { 9, 10 })
            yield return [start, direction, count, 1d / 40.8];
        foreach (var start in Enumerable.Range(1, 8))
        foreach (var direction in new[] { -1, 1 })
            yield return [start, direction, 17, .04];
    }

    [Theory]
    [MemberData(nameof(RotatedSweeps))]
    public void RotationCanKeepItsTailWithTheHandThatFinishesTheSweep(int start, int direction, int count, double interval)
    {
        var buttons = Enumerable.Range(0, count).Select(i =>
            ((start - 1 + direction * i % 8 + 8) % 8 + 1).ToString(CultureInfo.InvariantCulture) + (i == count - 1 ? "b" : ""));
        var chart = SimaiParser.ParseChart("(102){#" + interval.ToString("R", CultureInfo.InvariantCulture) + "}" +
            string.Join(',', buttons) + ",E", first: 23.921568627461006);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Id}/{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(count, plan.Results.Count);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
    }

    [Fact]
    public void FastTailStillRespectsAConfiguredMovementLimit()
    {
        var chart = SimaiParser.ParseChart("(102){96}1,2,3,4,5,6,7,8,1b,E", first: 23.921568627461006);
        var model = new HumanMotionModel(maxSpeedMillimetersPerSecond: 1500, preferredSpeedMillimetersPerSecond: 1000);
        var plan = HumanPlanner.Plan(chart, new HumanPlannerOptions { MotionModel = model });
        Assert.False(plan.AllCriticalPerfect);
        Assert.NotEmpty(plan.Gaps);
        Assert.Equal(0, model.Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
    }
}
