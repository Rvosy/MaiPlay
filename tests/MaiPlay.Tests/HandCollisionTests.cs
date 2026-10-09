using MaiPlay.Core;
using static MaiPlay.Core.ContactTrajectoryCompiler;

namespace MaiPlay.Tests;

public sealed class HandCollisionTests
{
    private static readonly HandContact[] Touch = [new("palm", InputChannel.Touch, default, .19)];
    private static readonly HandContact[] Button = [new("finger", InputChannel.Button, default, .035)];

    private static HumanMotionModel Model(double radius = 10) =>
        new(new CabinetGeometry(100), 1000, 500, handBodyRadiusMillimeters: radius);

    [Fact]
    public void FindsCollisionBetweenKeyframesEvenWhenEveryEndpointIsSeparated()
    {
        HandFrame[] left = [new(0, new(-1, 0), Touch), new(1, new(1, 0), [])];
        HandFrame[] right = [new(0, new(1, 0), Touch), new(1, new(-1, 0), [])];

        var result = Model().AnalyzeCollisions(left, right);

        var collision = Assert.Single(result.Intervals);
        Assert.Equal(.45, collision.StartTime, 10);
        Assert.Equal(.55, collision.EndTime, 10);
        Assert.Equal(.5, collision.ClosestTime, 10);
        Assert.Equal(0, collision.MinimumDistanceMillimeters, 10);
        Assert.Equal(.1, result.Seconds, 10);
        Assert.Equal(20, result.RequiredSeparationMillimeters);
        Assert.True(Model().HasCollision(left, right));
    }

    [Fact]
    public void MergesAdjacentCollisionPiecesAcrossAsynchronousKeyframes()
    {
        HandFrame[] left =
        [new(0, new(-1, 0), Touch), new(.48, new(-.04, 0), Touch), new(1, new(1, 0), [])];
        HandFrame[] right =
        [new(0, new(1, 0), Touch), new(.53, new(-.06, 0), Touch), new(1, new(-1, 0), [])];

        var result = Model().AnalyzeCollisions(left, right);

        Assert.Single(result.Intervals);
        Assert.Equal(.1, result.Seconds, 10);
        Assert.Equal(.5, result.ClosestTime!.Value, 10);
        Assert.True(Model().HasCollision(left, right));
    }

    [Fact]
    public void IgnoresAirborneProjectionOverlapAndUsesHalfOpenContactIntervals()
    {
        HandFrame[] left =
        [new(0, default, Touch), new(.5, default, []), new(1, default, [])];
        HandFrame[] right =
        [new(0, default, []), new(.5, default, Touch), new(1, default, [])];

        var result = Model().AnalyzeCollisions(left, right);

        Assert.Equal(0, result.Count);
        Assert.Null(result.MinimumDistanceMillimeters);
        Assert.Null(result.ClosestTime);
        Assert.False(Model().HasCollision(left, right));
    }

    [Fact]
    public void ReleaseGapSeparatesTwoStationaryCollisionIntervals()
    {
        HandFrame[] left = [new(0, default, Touch), new(2, default, [])];
        HandFrame[] right =
        [new(0, default, Touch), new(.5, default, []), new(1, default, Touch), new(2, default, [])];

        var result = Model().AnalyzeCollisions(left, right);

        Assert.Equal(2, result.Count);
        Assert.Equal(1.5, result.Seconds);
    }

    [Fact]
    public void ReplacedContactTailIsExcludedWithoutLosingEarlierCollisionTime()
    {
        HandFrame[] left = [new(1.02, default, Touch), new(1.05, default, [])];
        HandFrame[] previousRight = [new(1, default, Touch), new(1.025, default, [])];
        HandFrame[] nextRight =
        [new(1, default, Touch), new(1.1, new(1, 0), Touch), new(1.125, new(1, 0), [])];

        Assert.Single(Model().AnalyzeCollisions(left, previousRight).Intervals);
        Assert.Empty(Model().AnalyzeCollisions(left, previousRight, endTime: nextRight[0].Time).Intervals);
        Assert.Empty(Model().AnalyzeCollisions(left, nextRight).Intervals);
        Assert.False(Model().HasCollision(left, previousRight, endTime: nextRight[0].Time));
        Assert.False(Model().HasCollision(left, nextRight));
        var clipped = Model().AnalyzeCollisions(left, previousRight, endTime: 1.023);
        Assert.Equal(.003, clipped.Seconds, 10);
        Assert.True(Model().HasCollision(left, previousRight, endTime: 1.023));
    }

    [Fact]
    public void BodySizeIsIndependentOfFingertipRadiusAndContactChannel()
    {
        HandFrame[] left = [new(0, default, Button), new(1, default, [])];
        HandFrame[] right = [new(0, new(.15, 0), Button), new(1, new(.15, 0), [])];

        var result = Model().AnalyzeCollisions(left, right);

        Assert.Single(result.Intervals);
        Assert.Equal(15, result.MinimumDistanceMillimeters);
        Assert.Equal(0, Model(5).AnalyzeCollisions(left, right).Count);
    }

    [Theory]
    [InlineData(.2)]
    [InlineData(.200001)]
    public void AdjacentBodiesAndTangentMotionAreAllowed(double distance)
    {
        HandFrame[] left = [new(0, default, Touch), new(1, default, [])];
        HandFrame[] right =
        [new(0, new(-1, distance), Touch), new(1, new(1, distance), [])];

        var result = Model().AnalyzeCollisions(left, right);

        Assert.Equal(0, result.Count);
        Assert.Equal(distance * 100, result.MinimumDistanceMillimeters!.Value, 10);
        Assert.Equal(.5, result.ClosestTime);
        Assert.False(Model().HasCollision(left, right));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void WifiSideBySidePalmsDoNotCountAsCollision(int start)
    {
        var chart = SimaiParser.ParseChart($"(120){start}w{(start + 3) % 8 + 1}[0.5##0.5],E", first: 1);
        var motions = CreateMotions(chart, CancellationToken.None).Where(m => m.Note.Kind == NoteKind.Slide).ToArray();
        var tracks = motions.Select((m, i) => new HandTrack((HandSide)i,
            ContactFrames(m.Points, m.Channel, 0, m.Profile))).ToArray();

        var result = new HumanMotionModel().AnalyzeCollisions(tracks);

        Assert.Equal(0, result.Count);
        Assert.True(result.MinimumDistanceMillimeters > result.RequiredSeparationMillimeters);
        Assert.False(new HumanMotionModel().HasCollision(tracks[0].Frames, tracks[1].Frames));
    }

    [Fact]
    public void FastCollisionQueryMatchesDiagnosticsAcrossDeterministicIrregularTracksAndCutoffs()
    {
        var random = new Random(478413);
        HandFrame[] Track()
        {
            var count = random.Next(2, 65);
            var time = random.NextDouble() * 6 - 3;
            var frames = new HandFrame[count];
            var position = new ChartPoint(random.NextDouble() * 3 - 1.5, random.NextDouble() * 3 - 1.5);
            for (var i = 0; i < count; i++)
            {
                time += .001 + random.NextDouble() * .3;
                if (random.Next(4) != 0)
                    position = new(random.NextDouble() * 3 - 1.5, random.NextDouble() * 3 - 1.5);
                frames[i] = new(time, position, i == count - 1 || random.Next(3) == 0 ? [] : random.Next(2) == 0 ? Touch : Button);
            }
            return frames;
        }
        var hits = 0;
        var misses = 0;
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var left = Track();
            var right = Track();
            var model = new HumanMotionModel(new CabinetGeometry(75 + random.NextDouble() * 250), 1000, 500,
                handBodyRadiusMillimeters: 5 + random.NextDouble() * 60);
            double[] cutoffs =
            [
                double.PositiveInfinity, Math.Min(left[0].Time, right[0].Time) - .01,
                Math.Max(left[0].Time, right[0].Time), left[random.Next(left.Length)].Time,
                right[random.Next(right.Length)].Time, Math.Min(left[^1].Time, right[^1].Time),
                (left[0].Time + right[^1].Time) / 2, random.NextDouble() * 15 - 3
            ];
            foreach (var cutoff in cutoffs)
            {
                var expected = model.AnalyzeCollisions(left, right, cutoff).Count > 0;
                if (expected) hits++; else misses++;
                Assert.Equal(expected, model.HasCollision(left, right, cutoff));
                Assert.Equal(expected, model.HasCollision(right, left, cutoff));
            }
        }
        Assert.True(hits > 500);
        Assert.True(misses > 500);
    }

    [Fact]
    public void FastQueryHandlesBriefContactNearTheEndOfALongTrackWithoutMissingAnInteriorCollision()
    {
        var left = Enumerable.Range(0, 10001)
            .Select(i => new HandFrame(i * .01, default, i == 10000 ? [] : Touch)).ToArray();
        HandFrame[] right =
        [new(99.5, new(-1, 0), Touch), new(99.504, new(1, 0), []), new(100, new(1, 0), [])];

        Assert.True(Model().HasCollision(left, right));
        Assert.False(Model().HasCollision(left, right, 99.501));
        Assert.True(Model().HasCollision(left, right, 99.502));
        Assert.Single(Model().AnalyzeCollisions(left, right).Intervals);
    }

    [Fact]
    public void MovementMetricsReportBodyCollisionSeparatelyFromProjectionCrossing()
    {
        HandTrack[] tracks =
        [
            new(HandSide.Left, [new(0, new(1, 0), Touch), new(1, new(1, 0), [])]),
            new(HandSide.Right, [new(0, new(-1, 0), Touch), new(1, new(-1, 0), [])])
        ];

        var metrics = Model().Analyze(tracks);

        Assert.Equal(1, metrics.ProjectedCrossingCount);
        Assert.Equal(0, metrics.Collisions.Count);
        Assert.True(metrics.SatisfiesMotionConstraints);
        var collisionTracks = tracks.Select(t => t with
        {
            Frames = t.Frames.Select(f => f with { Position = default }).ToArray()
        }).ToArray();
        Assert.False(Model().Analyze(collisionTracks).SatisfiesMotionConstraints);
    }

    [Fact]
    public void ExecutabilityNeedsMotionValidationAndIsSeparateFromScore()
    {
        var chart = SimaiParser.ParseChart("(120)1,E");
        HandTrack[] tracks = [new(HandSide.Left, [new(0, default, Button), new(1, default, [])])];
        PlaybackEvent[] results = [new(PlaybackEventKind.Perfect, chart.Notes[0], 0)];
        var plan = new HandPlan(chart, tracks, new HandReplay(tracks), [], results, 101, 3, []);
        Assert.True(plan.AllCriticalPerfect);
        Assert.False(plan.IsExecutable);

        var metrics = Model().Analyze(tracks);
        plan = plan with { Diagnostics = new(metrics, 0, [], new Dictionary<string, int>()) };
        Assert.True(plan.IsExecutable);
        Assert.True(plan.IsSolved);
        var lowScore = plan with { Results = [results[0] with { Grade = AchievementGrade.LateGood }] };
        Assert.True(lowScore.IsExecutable);
        Assert.False(lowScore.IsSolved);
        var tooFast = plan with { Diagnostics = plan.Diagnostics with { Movement = metrics with { SpeedLimitViolations = 1 } } };
        Assert.False(tooFast.IsExecutable);
        Assert.False((plan with { Gaps = [new(0, 0, 1, "unassigned")] }).IsExecutable);
        Assert.False((plan with { Results = [] }).IsExecutable);
    }

    [Fact]
    public void PostureWeightChangesOnlySoftPositionPreference()
    {
        var model = Model();
        var usual = model.EvaluateTransition(HandSide.Left, new(-1, 0), 0, new(1, 0), 1, new(0, 0));
        var relaxed = model.EvaluateTransition(HandSide.Left, new(-1, 0), 0, new(1, 0), 1, new(0, 0), postureWeight: .1);
        Assert.True(relaxed.Feasible);
        Assert.True(relaxed.Crossed);
        Assert.Equal(usual.DistanceMillimeters, relaxed.DistanceMillimeters);
        Assert.Equal(usual.SpeedMillimetersPerSecond, relaxed.SpeedMillimetersPerSecond);
        Assert.Equal(2.52, usual.Cost - relaxed.Cost, 10);
    }

    [Fact]
    public void OpposingSlidesCannotBeDeclaredSolvedByJudgementAlone()
    {
        var chart = SimaiParser.ParseChart("(120)1-5[0.5##1]/5-1[0.5##1],E", first: 1);
        var baseline = NormalHandPlanner.Plan(chart);
        var baselineMotion = new HumanMotionModel().Analyze(baseline.Tracks, baseline.Strokes);
        Assert.True(baseline.AllCriticalPerfect);
        Assert.Single(baselineMotion.Collisions.Intervals);
        Assert.False(baselineMotion.SatisfiesMotionConstraints);
        var plan = HumanPlanner.Plan(chart);

        Assert.Equal(0, plan.Diagnostics!.Movement.Collisions.Count);
        Assert.False(plan.IsSolved);
        var gap = Assert.Single(plan.Gaps);
        Assert.Equal(NoteKind.Slide, chart.Notes.Single(n => n.Id == gap.NoteId).Kind);
        Assert.Equal(3, plan.Results.Count(e => e.Grade == AchievementGrade.Perfect));
    }
}
