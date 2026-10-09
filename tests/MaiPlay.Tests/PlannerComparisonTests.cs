using System.Text.Json;
using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class PlannerComparisonTests
{
    [Theory]
    [InlineData("11.5", "11")]
    [InlineData("11.6", "11+")]
    [InlineData("11.9", "11+")]
    [InlineData("12.0", "12")]
    [InlineData("12+", "12+")]
    [InlineData("12", "12")]
    [InlineData("手顺求解示例", null)]
    [InlineData("", null)]
    public void DisplayLevelUsesPointSixBoundary(string constant, string? expected) =>
        Assert.Equal(expected, PlannerComparison.DisplayLevel(constant));

    [Fact]
    public void ImprovementCannotHideLowerAchievementOrMoreGaps()
    {
        var baseline = new PlannerComparison.Metrics("partial", 99, 100m, 299, 1, 1, new Dictionary<NoteKind, int>(), null);
        Assert.Equal("improved", PlannerComparison.Change(baseline, baseline with { CP = 100, Gaps = 0 }));
        Assert.Equal("regressed", PlannerComparison.Change(baseline, baseline with { Achievement = 99.99m }));
        Assert.Equal("mixed", PlannerComparison.Change(baseline, baseline with { CP = 100, Gaps = 2 }));
        Assert.Equal("unchanged", PlannerComparison.Change(baseline, baseline with { Seconds = 20 }));
    }

    [Fact]
    public void EqualScoresStillExposeCollisionAndContinuityRegressions()
    {
        var movement = new HumanMotionMetrics(100, 100, 1000, 0, 0, 0, 0, 1, 1);
        var link = new SlideConnectionOutcome(1, 3, 1, HandSide.Left, HandSide.Left, 0, 0, 0);
        var baseline = new PlannerComparison.Metrics("all-cp", 4, 100, 12, 0, 1, new Dictionary<NoteKind, int>(), null,
            movement, IsExecutable: true, IsSolved: true, SlideConnections: [link]);
        var colliding = baseline with
        {
            Movement = movement with { Collisions = new([new(.4, .6, .5, 0)], 0, .5, 90) },
            IsExecutable = false, IsSolved = false
        };
        Assert.Equal("unchanged", PlannerComparison.Change(baseline, colliding));
        Assert.Equal("regressed", PlannerComparison.ActionChange(baseline, colliding));
        var broken = baseline with { SlideConnections = [link with { ContactGapSeconds = .04 }] };
        Assert.Equal("unchanged", PlannerComparison.Change(baseline, broken));
        Assert.Equal("regressed", PlannerComparison.ActionChange(baseline, broken));
        Assert.Equal("improved", PlannerComparison.ActionChange(broken, baseline));
    }

    [Fact]
    public void RelayIsAReportedAlternativeRatherThanAnAutomaticRegression()
    {
        var movement = new HumanMotionMetrics(100, 100, 1000, 0, 0, 0, 0, 1, 1);
        var link = new SlideConnectionOutcome(1, 3, 1, HandSide.Left, HandSide.Left, 0, 0, 0);
        var baseline = new PlannerComparison.Metrics("all-cp", 4, 100, 12, 0, 1, new Dictionary<NoteKind, int>(), null,
            movement, IsExecutable: true, IsSolved: true, SlideConnections: [link]);
        var relay = baseline with { SlideConnections = [link with { NextHand = HandSide.Right, ContactGapSeconds = .02 }] };
        Assert.Equal("changed", PlannerComparison.ActionChange(baseline, relay));
        Assert.Equal("changed", PlannerComparison.ActionChange(relay, baseline));
        Assert.Equal("unchanged", PlannerComparison.ActionChange(baseline, baseline with { Seconds = 10 }));
    }

    [Fact]
    public void MotionTradeoffsRemainVisibleAndMissingDiagnosticsAreNotUnchanged()
    {
        var movement = new HumanMotionMetrics(100, 100, 1000, 0, 0, 0, 0, 1, 1)
        {
            Collisions = new([new(.4, .6, .5, 0)], 0, .5, 90)
        };
        var baseline = new PlannerComparison.Metrics("all-cp", 4, 100, 12, 0, 1, new Dictionary<NoteKind, int>(), null,
            movement, SlideConnections: []);
        var faster = baseline with { Movement = movement with { Collisions = HandCollisionAnalysis.Empty, SpeedLimitViolations = 1 } };
        Assert.Equal("mixed", PlannerComparison.ActionChange(baseline, faster));
        Assert.Equal("unavailable", PlannerComparison.ActionChange(baseline, baseline with { Movement = null }));
    }

    [Fact]
    public void BaselineReportSeparatesFullScoreFromExecutableMotion()
    {
        var plan = NormalHandPlanner.Plan(SimaiParser.ParseChart("(120)1-5[0.5##1]/5-1[0.5##1],E", first: 1));
        var inspected = PlannerComparison.WithMotionDiagnostics(plan);

        Assert.True(inspected.AllCriticalPerfect);
        Assert.False(inspected.IsExecutable);
        Assert.False(inspected.IsSolved);
        Assert.Single(inspected.Diagnostics!.Movement.Collisions.Intervals);
        Assert.Equal(plan.Tracks, inspected.Tracks);
        Assert.Equal(plan.Results, inspected.Results);
    }

    [Fact]
    public void ComparisonReturnsRegressionForNewCollisionWithIdenticalGrades()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"MaiPlay-motion-comparison-{Guid.NewGuid():N}");
        var root = Path.Combine(directory, "charts");
        var output = Path.Combine(directory, "report");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "maidata.txt"), "&title=Collision\n&first=1\n&lv_5=11.6\n&inote_5=(120)1,E\n");
            HandPlan Colliding(Chart chart)
            {
                var plan = NormalHandPlanner.Plan(chart);
                var original = Assert.Single(plan.Tracks);
                HandTrack[] tracks = [original, original with { Hand = original.Hand == HandSide.Left ? HandSide.Right : HandSide.Left }];
                var replay = new HandReplay(tracks);
                var session = new PlaybackSession(chart, PlayMode.Algorithm, replay.Events);
                var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
                return plan with { Tracks = tracks, Replay = replay, Results = results, Achievement = session.Achievement.Percentage, DXScore = session.DXScore };
            }
            var exit = PlannerComparison.Run(root, output, 5, "11+", chart => NormalHandPlanner.Plan(chart), Colliding,
                "baseline", "candidate");

            Assert.Equal(2, exit);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "comparison.json")));
            var item = Assert.Single(report.RootElement.GetProperty("charts").EnumerateArray());
            Assert.Equal("unchanged", item.GetProperty("change").GetString());
            Assert.Equal("regressed", item.GetProperty("actionChange").GetString());
            Assert.True(item.GetProperty("candidate").GetProperty("allCriticalPerfect").GetBoolean());
            Assert.False(item.GetProperty("candidate").GetProperty("isSolved").GetBoolean());
            Assert.Contains("regressed", File.ReadAllText(Path.Combine(output, "comparison.md")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ComparisonFiltersDifficultyAndLevelAndReportsCandidateFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"MaiPlay-comparison-{Guid.NewGuid():N}");
        var root = Path.Combine(directory, "charts");
        var output = Path.Combine(directory, "report");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "maidata.txt"), "&title=Comparison\n&first=1\n&lv_4=12.5\n&inote_4=(120){4}1,E\n&lv_5=11.6\n&inote_5=(120){4}2,E\n");
            var excluded = Path.Combine(root, "excluded");
            Directory.CreateDirectory(excluded);
            File.WriteAllText(Path.Combine(excluded, "maidata.txt"), "&title=Excluded\n&first=1\n&lv_5=11.5\n&inote_5=(120){4}3,E\n");
            var calls = new List<int>();
            HandPlan Baseline(Chart chart)
            {
                calls.Add(chart.Difficulty);
                return NormalHandPlanner.Plan(chart);
            }
            var exitCode = PlannerComparison.Run(root, output, 5, "11+", Baseline,
                _ => throw new InvalidOperationException("candidate failed"), "baseline", "candidate");
            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { 5 }, calls);
            var json = File.ReadAllText(Path.Combine(output, "comparison.json"));
            using var report = JsonDocument.Parse(json);
            Assert.Equal(1, report.RootElement.GetProperty("excludedLevel").GetInt32());
            var chart = Assert.Single(report.RootElement.GetProperty("charts").EnumerateArray());
            Assert.Equal("maidata.txt", chart.GetProperty("path").GetString());
            Assert.Equal("all-cp", chart.GetProperty("baseline").GetProperty("status").GetString());
            Assert.True(chart.GetProperty("baseline").GetProperty("allCriticalPerfect").GetBoolean());
            Assert.True(chart.GetProperty("baseline").GetProperty("isExecutable").GetBoolean());
            Assert.True(chart.GetProperty("baseline").GetProperty("isSolved").GetBoolean());
            Assert.Equal("error", chart.GetProperty("candidate").GetProperty("status").GetString());
            Assert.Equal("error", chart.GetProperty("actionChange").GetString());
            Assert.Contains("candidate failed", chart.GetProperty("candidate").GetProperty("error").GetString());
            Assert.Equal(64, chart.GetProperty("chartSha256").GetString()!.Length);
            Assert.DoesNotContain("inote", json);
            Assert.DoesNotContain("tracks", json);
            Assert.DoesNotContain(root, json);
            Assert.True(File.Exists(Path.Combine(output, "comparison.md")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
