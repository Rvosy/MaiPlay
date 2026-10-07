using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class SweepWindowContinuityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(121.2)]
    public void ConvergingDoubleSweepKeepsItsSingleEnding(double first)
    {
        var chart = SimaiParser.ParseChart("(200){#0.025}1b,2b/8b,3b/7b,4b/6b,5b,E", first: first);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}/{e.DifferenceSeconds}")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
    }

    [Fact]
    public void InterleavedDoubleSweepDoesNotHitTheOtherLanesFutureButtonsEarly()
    {
        var chart = SimaiParser.ParseChart("(100){#0.15}6,6,7/8,1/2,3/4,{#0.075}6,5,6,E", first: 103.05);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}/{e.DifferenceSeconds}")));
        Assert.Empty(plan.Gaps);
    }

    [Fact]
    public void DoubleSweepAfterWifiCanMergeIntoOneSlideHead()
    {
        var chart = SimaiParser.ParseChart("(200){#0.6}1b$w5[0.3##0.15],{#0.3}8b$w4[0.3##0.15],Cf,{#0.025}1b,2b/8b,3b/7b,4b/6b,5b$>5[0.3##3.2]*<5[0.3##3.2],E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}/{e.DifferenceSeconds}")));
        Assert.Empty(plan.Gaps);
    }
}
