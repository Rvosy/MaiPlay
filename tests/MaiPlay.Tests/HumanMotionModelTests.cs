using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanMotionModelTests
{
    [Fact]
    public void FasterRepeatedStrikesCostMoreWithoutBecomingInfeasible()
    {
        var model = new HumanMotionModel();
        var rapid = model.RepetitionCost(.05);
        var moderate = model.RepetitionCost(.1);

        Assert.Equal(0, model.RepetitionCost(.25));
        Assert.Equal(0, model.RepetitionCost(model.PreferredStrikeIntervalSeconds));
        Assert.True(moderate > 0);
        Assert.True(rapid > moderate);
        Assert.True(double.IsFinite(rapid));
    }

    [Fact]
    public void RepetitionPreferenceCanBeConfiguredIndependentlyOfMovement()
    {
        var relaxed = new HumanMotionModel(preferredStrikeIntervalSeconds: .08);
        var deliberate = new HumanMotionModel(preferredStrikeIntervalSeconds: .16);

        Assert.Equal(0, relaxed.RepetitionCost(.1));
        Assert.True(deliberate.RepetitionCost(.1) > 0);
        Assert.Equal(relaxed.MaxSpeedMillimetersPerSecond, deliberate.MaxSpeedMillimetersPerSecond);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RepetitionPreferenceRequiresAFinitePositiveInterval(double interval)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HumanMotionModel(preferredStrikeIntervalSeconds: interval));
    }

    [Fact]
    public void PhysicalScaleControlsTravelFeasibility()
    {
        var small = new HumanMotionModel(new CabinetGeometry(100), 1000, 500);
        var large = new HumanMotionModel(new CabinetGeometry(200), 1000, 500);
        var first = small.EvaluateTransition(HandSide.Left, new(-1, 0), 0, new(1, 0), .3);
        var second = large.EvaluateTransition(HandSide.Left, new(-1, 0), 0, new(1, 0), .3);
        Assert.Equal(200, first.DistanceMillimeters);
        Assert.True(first.Feasible);
        Assert.Equal(400, second.DistanceMillimeters);
        Assert.False(second.Feasible);
    }

    [Fact]
    public void MoreTimeReducesMovementCostAndMaximumSpeedIsInclusive()
    {
        var model = new HumanMotionModel(new CabinetGeometry(100), 1000, 500);
        var quick = model.EvaluateTransition(HandSide.Right, new(0, 0), 0, new(1, 0), .1);
        var slower = model.EvaluateTransition(HandSide.Right, new(0, 0), 0, new(1, 0), .5);
        Assert.True(quick.Feasible);
        Assert.True(slower.Cost < quick.Cost);
        Assert.False(model.EvaluateTransition(HandSide.Right, new(0, 0), 0, new(1, 0), .099).Feasible);
    }

    [Fact]
    public void SweepCostAndSpeedUseTheTraveledArcInsteadOfItsChord()
    {
        var model = new HumanMotionModel(new CabinetGeometry(100), 2000, 1000);
        var from = new ChartPoint(1, 0);
        var to = new ChartPoint(0, 1);
        var direct = model.EvaluateTransition(HandSide.Right, from, 0, to, .1);
        var sweep = model.EvaluateTransition(HandSide.Right, from, 0, to, .1,
            pathDistanceMillimeters: 100 * Math.PI / 2);
        Assert.True(sweep.Feasible);
        Assert.True(sweep.Cost > direct.Cost);
        Assert.True(sweep.SpeedMillimetersPerSecond > direct.SpeedMillimetersPerSecond);

        var limit = new HumanMotionModel(new CabinetGeometry(100), 1500, 1000);
        Assert.True(limit.EvaluateTransition(HandSide.Right, from, 0, to, .1).Feasible);
        Assert.False(limit.EvaluateTransition(HandSide.Right, from, 0, to, .1,
            pathDistanceMillimeters: 100 * Math.PI / 2).Feasible);
        Assert.True(limit.EvaluateTransition(HandSide.Right, from, 0, to, .1,
            pathDistanceMillimeters: 150).Feasible);
        Assert.False(limit.EvaluateTransition(HandSide.Right, from, 0, to, .1,
            pathDistanceMillimeters: 150.1).Feasible);
    }

    [Fact]
    public void CrossingUsesOtherHandPositionAtArrivalInsteadOfItsFutureEndpoint()
    {
        var model = new HumanMotionModel();
        HandFrame[] other = [new(0, new(.8, 0), []), new(2, new(-.8, 0), [])];
        var atArrival = HumanMotionModel.PositionAt(other, .5);
        var transition = model.EvaluateTransition(HandSide.Left, new(-.8, 0), 0, new(0, 0), .5, atArrival);
        var futureEndpoint = model.EvaluateTransition(HandSide.Left, new(-.8, 0), 0, new(0, 0), .5, other[^1].Position);
        Assert.Equal(.4, atArrival.X, 10);
        Assert.False(transition.Crossed);
        Assert.True(futureEndpoint.Crossed);
        Assert.True(transition.Cost < futureEndpoint.Cost);
        Assert.Equal(other[^1].Position, HumanMotionModel.PositionAt(other, 10));
    }

    [Fact]
    public void MetricsMeasureEachHandAndFindCrossingBetweenDifferentKeyframeTimes()
    {
        var model = new HumanMotionModel(new CabinetGeometry(100), 100, 100);
        HandTrack[] tracks =
        [
            new(HandSide.Left, [new(0, new(-1, 0), []), new(2, new(1, 0), []), new(4, new(-1, 0), [])]),
            new(HandSide.Right, [new(0, new(0, 0), []), new(.5, new(0, 0), []), new(4, new(0, 0), [])])
        ];
        var result = model.Analyze(tracks);
        Assert.Equal(400, result.TotalDistanceMillimeters);
        Assert.Equal(400, result.LeftDistanceMillimeters);
        Assert.Equal(0, result.RightDistanceMillimeters);
        Assert.Equal(100, result.PeakSpeedMillimetersPerSecond);
        Assert.Equal(0, result.SpeedLimitViolations);
        Assert.Equal(1, result.ProjectedCrossingCount);
        Assert.Equal(2, result.ProjectedCrossingSeconds, 10);
        Assert.Equal(1, result.DirectionReversalCount);
        Assert.Null(result.PeakLeftActionsPerSecond);
    }

    [Fact]
    public void CrossingIncludesAReleasedHandWaitingWhileTheOtherHandContinues()
    {
        var model = new HumanMotionModel(new CabinetGeometry(100), 100, 100);
        HandTrack[] tracks =
        [
            new(HandSide.Left, [new(0, new(0, 0), []), new(1, new(0, 0), [])]),
            new(HandSide.Right, [new(0, new(1, 0), []), new(3, new(-1, 0), [])])
        ];

        var result = model.Analyze(tracks);

        Assert.Equal(1, result.ProjectedCrossingCount);
        Assert.Equal(1.5, result.ProjectedCrossingSeconds, 10);
        Assert.Equal(0, result.LeftDistanceMillimeters);
        Assert.Equal(200, result.RightDistanceMillimeters);
    }

    [Fact]
    public void MetricsCountSpeedViolationsAndActionsInHalfOpenWindows()
    {
        var model = new HumanMotionModel(new CabinetGeometry(100), 100, 100);
        HandTrack[] tracks = [new(HandSide.Left,
            [new(0, new(0, 0), []), new(.1, new(1, 0), []), new(1, new(1, 0), [])])];
        PlannedStroke[] strokes =
        [
            new(0, HandSide.Left, 0, .01, InputChannel.Button),
            new(1, HandSide.Left, 0, .01, InputChannel.Touch),
            new(2, HandSide.Left, .5, .51, InputChannel.Button),
            new(3, HandSide.Left, 1, 1.01, InputChannel.Button),
            new(4, HandSide.Right, .2, .21, InputChannel.Button)
        ];
        var result = model.Analyze(tracks, strokes);
        Assert.Equal(1, result.SpeedLimitViolations);
        Assert.Equal(1000, result.PeakSpeedMillimetersPerSecond);
        Assert.Equal(2, result.PeakLeftActionsPerSecond);
        Assert.Equal(1, result.PeakRightActionsPerSecond);
        Assert.Equal(0, result.ProjectedCrossingCount);
    }
}
