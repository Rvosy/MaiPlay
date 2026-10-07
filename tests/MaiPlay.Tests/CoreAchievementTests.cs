using MaiPlay.Core;

namespace MaiPlay.Tests;

public class CoreAchievementTests
{
    [Fact]
    public void StartsAtChartMaximumIncludingEmptyAndNoBreakCharts()
    {
        Assert.Equal("100.0000%", new AchievementState(MakeChart()).DisplayText);
        Assert.Equal("100.0000%", new AchievementState(MakeChart(new ChartNote { Kind = NoteKind.Slide })).DisplayText);
        Assert.Equal("101.0000%", new AchievementState(MakeChart(new ChartNote { IsBreak = true })).DisplayText);
        Assert.Equal("101.0000%", new AchievementState(MakeChart(new() { IsBreak = true }, new() { IsBreak = true })).DisplayText);
    }

    [Fact]
    public void MissesUseNoteWeightsAndBreakOverridesItsPhysicalKind()
    {
        var chart = MakeChart(
            new() { Kind = NoteKind.Tap }, new() { Kind = NoteKind.Touch },
            new() { Kind = NoteKind.Hold }, new() { Kind = NoteKind.TouchHold }, new() { Kind = NoteKind.Slide },
            new() { Kind = NoteKind.Tap, IsBreak = true }, new() { Kind = NoteKind.Hold, IsBreak = true },
            new() { Kind = NoteKind.Slide, IsBreak = true });
        var state = new AchievementState(chart);
        Assert.Equal(12000, state.TotalBaseScore);
        Assert.Equal(300, state.TotalExtraScore);
        state.Apply(chart.Notes[0], AchievementGrade.Miss);
        Assert.Equal("96.8333%", state.DisplayText);
        state.Apply(chart.Notes[2], AchievementGrade.Miss);
        Assert.Equal("88.5000%", state.DisplayText);
        state.Apply(chart.Notes[7], AchievementGrade.Miss);
        Assert.Equal("67.3333%", state.DisplayText);
        Assert.Equal(4000, state.LostBaseScore);
        Assert.Equal(100, state.LostExtraScore);
    }

    [Theory]
    [InlineData(AchievementGrade.Perfect, "101.0000%", "100.0000%")]
    [InlineData(AchievementGrade.FastPerfect2nd, "100.7500%", "100.0000%")]
    [InlineData(AchievementGrade.LatePerfect2nd, "100.7500%", "100.0000%")]
    [InlineData(AchievementGrade.FastPerfect3rd, "100.5000%", "100.0000%")]
    [InlineData(AchievementGrade.LatePerfect3rd, "100.5000%", "100.0000%")]
    [InlineData(AchievementGrade.FastGreat, "80.4000%", "80.0000%")]
    [InlineData(AchievementGrade.LateGreat, "80.4000%", "80.0000%")]
    [InlineData(AchievementGrade.FastGreat2nd, "60.4000%", "80.0000%")]
    [InlineData(AchievementGrade.LateGreat2nd, "60.4000%", "80.0000%")]
    [InlineData(AchievementGrade.FastGreat3rd, "50.4000%", "80.0000%")]
    [InlineData(AchievementGrade.LateGreat3rd, "50.4000%", "80.0000%")]
    [InlineData(AchievementGrade.FastGood, "40.3000%", "50.0000%")]
    [InlineData(AchievementGrade.LateGood, "40.3000%", "50.0000%")]
    [InlineData(AchievementGrade.Miss, "0.0000%", "0.0000%")]
    [InlineData(AchievementGrade.TooFast, "0.0000%", "0.0000%")]
    public void EveryGradeMatchesDxBaseAndExtraScore(AchievementGrade grade, string expectedBreak, string expectedNormal)
    {
        var breakNote = new ChartNote { IsBreak = true };
        var breakState = new AchievementState(MakeChart(breakNote));
        breakState.Apply(breakNote, grade);
        Assert.Equal(expectedBreak, breakState.DisplayText);

        var normalNote = new ChartNote { Kind = NoteKind.Slide };
        var normalState = new AchievementState(MakeChart(normalNote));
        normalState.Apply(normalNote, grade);
        Assert.Equal(expectedNormal, normalState.DisplayText);
    }

    [Fact]
    public void BreakExtraIsOnePercentSharedByAllBreaks()
    {
        var chart = MakeChart(new() { IsBreak = true }, new() { IsBreak = true }, new() { IsBreak = true });
        var state = new AchievementState(chart);
        state.Apply(chart.Notes[0], AchievementGrade.FastPerfect2nd);
        Assert.Equal("100.9166%", state.DisplayText);
        state.Apply(chart.Notes[1], AchievementGrade.LatePerfect2nd);
        state.Apply(chart.Notes[2], AchievementGrade.FastPerfect2nd);
        Assert.Equal(100.75m, state.Percentage);
        Assert.Equal(0, state.LostBaseScore);
        Assert.Equal(75, state.LostExtraScore);
    }

    [Fact]
    public void DisplayTruncatesFourDigitsInsteadOfRounding()
    {
        var chart = MakeChart(new(), new(), new());
        var state = new AchievementState(chart);
        state.Apply(chart.Notes[0], AchievementGrade.Miss);
        Assert.InRange(state.Percentage, 66.66666666666666666666666666m, 66.66666666666666666666666667m);
        Assert.Equal("66.6666%", state.DisplayText);
    }

    [Fact]
    public void ExAndEachFlagsDoNotChangeScoreWeight()
    {
        var chart = MakeChart(new() { IsEx = true, IsEach = true }, new() { Kind = NoteKind.Touch, IsEach = true });
        var state = new AchievementState(chart);
        Assert.Equal(1000, state.TotalBaseScore);
        state.Apply(chart.Notes[0], AchievementGrade.Miss);
        Assert.Equal(50m, state.Percentage);
    }

    [Fact]
    public void FinalEventsScoreOnceAndRestartRestoresMaximum()
    {
        var chart = SimaiParser.ParseChart("(120){4}1h[2:1]/2b-6b[2:1],E");
        var auto = new PlaybackSession(chart, PlayMode.AutoPlay);
        auto.Advance(0);
        Assert.Equal(101m, auto.Achievement.Percentage);
        auto.Advance(100);
        Assert.Equal(101m, auto.Achievement.Percentage);

        var noInput = new PlaybackSession(chart, PlayMode.Algorithm);
        noInput.Advance(.15);
        Assert.Equal(0, noInput.Achievement.LostBaseScore);
        noInput.Advance(.15 + PlaybackSession.LogicStep);
        Assert.Equal(2500, noInput.Achievement.LostBaseScore);
        Assert.Equal(100, noInput.Achievement.LostExtraScore);
        var firstValue = noInput.Achievement.Percentage;
        noInput.Advance(.15 + PlaybackSession.LogicStep);
        noInput.Advance(.14);
        Assert.Equal(firstValue, noInput.Achievement.Percentage);
        noInput.Advance(100);
        Assert.Equal(0m, noInput.Achievement.Percentage);
        Assert.Equal("0.0000%", noInput.Achievement.DisplayText);
        noInput.Reset();
        Assert.Equal(101m, noInput.Achievement.Percentage);
        Assert.Equal(0, noInput.Achievement.LostBaseScore);
        Assert.Equal(0, noInput.Achievement.LostExtraScore);
        noInput.Advance(.15 + PlaybackSession.LogicStep);
        Assert.Equal(firstValue, noInput.Achievement.Percentage);
    }

    [Fact]
    public void AllEighteenOptionalUserChartsLoseExactlyTheirFullScore()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "MaiPlay.slnx"))) root = root.Parent;
        var fixtures = Environment.GetEnvironmentVariable("MAIPLAY_CHART_FIXTURES")
            ?? (root == null ? "" : Path.Combine(root.FullName, ".reference", "acceptance"));
        if (!Directory.Exists(fixtures)) return;
        var charts = new[] { "11160", "11377", "226", "239" }
            .SelectMany(id => SimaiParser.LoadSong(Path.Combine(fixtures, id, "maidata.txt")).Charts).ToArray();
        Assert.Equal(18, charts.Length);
        foreach (var chart in charts)
        {
            var noInput = new PlaybackSession(chart, PlayMode.Algorithm);
            noInput.Advance(noInput.FinishTime + 1);
            Assert.Equal(0m, noInput.Achievement.Percentage);
            Assert.Equal(noInput.Achievement.TotalBaseScore, noInput.Achievement.LostBaseScore);
            Assert.Equal(noInput.Achievement.TotalExtraScore, noInput.Achievement.LostExtraScore);
            var auto = new PlaybackSession(chart, PlayMode.AutoPlay);
            var maximum = auto.Achievement.Percentage;
            auto.Advance(auto.FinishTime + 1);
            Assert.Equal(maximum, auto.Achievement.Percentage);
        }
    }

    private static Chart MakeChart(params ChartNote[] notes) =>
        new("Achievement fixture", "", "", 5, "", notes.Select((note, id) => note with { Id = id }).ToArray(), 0);
}
