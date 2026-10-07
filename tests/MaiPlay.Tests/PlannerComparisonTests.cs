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
            Assert.Equal("error", chart.GetProperty("candidate").GetProperty("status").GetString());
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
