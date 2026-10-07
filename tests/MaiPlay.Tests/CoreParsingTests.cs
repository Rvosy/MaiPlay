using MaiPlay.Core;

namespace MaiPlay.Tests;

public class CoreParsingTests
{
    [Fact]
    public void SongPreservesMetadataAndPerDifficultyOffset()
    {
        var song = SimaiParser.ParseSong("&title=中文 ☆\n&artist=作者\n&first=1.2\n&first_5=2.3\n&lv_2=5\n&inote_2=(120){4}1,E\n&lv_5=14+\n&des_5=谱师\n&inote_5=(180){4}2,E");
        Assert.Equal("中文 ☆", song.Title);
        Assert.Equal([2, 5], song.Charts.Select(c => c.Difficulty));
        Assert.Equal(1.2, song.Charts[0].Notes[0].Time);
        Assert.Equal(2.3, song.Charts[1].Notes[0].Time);
        Assert.Equal("14+", song.Charts[1].Level);
        Assert.Equal("谱师", song.Charts[1].Designer);
    }

    [Fact]
    public void BreakAndExBelongToTheirHeadOrTrail()
    {
        var chart = SimaiParser.ParseChart("(120){4}1bx-5[4:1],2-6b[4:1],3hbx[4:1],E");
        var first = chart.Notes.Where(n => n.Time == 0).ToArray();
        Assert.True(first[0].IsBreak && first[0].IsEx && first[0].IsStar);
        Assert.False(first[1].IsBreak || first[1].IsEx);
        var second = chart.Notes.Where(n => n.Time == .5).ToArray();
        Assert.False(second[0].IsBreak);
        Assert.True(second[1].IsBreak);
        Assert.True(chart.Notes[^1].IsBreak && chart.Notes[^1].IsEx);
        Assert.Equal(NoteKind.Hold, chart.Notes[^1].Kind);
    }

    [Fact]
    public void EachIncludesTouchButNotTheSameSlidesHeadAndTrail()
    {
        var chart = SimaiParser.ParseChart("(120){4}1/A1/C,2-6[4:1],3-7[4:1]*-8[4:1],4`5,E");
        Assert.All(chart.Notes.Where(n => n.Time == 0), n => Assert.True(n.IsEach));
        Assert.All(chart.Notes.Where(n => n.Time == .5), n => Assert.False(n.IsEach));
        Assert.All(chart.Notes.Where(n => n.Time == 1 && n.Kind == NoteKind.Slide), n => Assert.True(n.IsEach));
        Assert.All(chart.Notes.Where(n => n.Time == 1.5), n => Assert.False(n.IsEach));
        Assert.Single(chart.Notes, n => n.IsDoubleStar);
    }

    [Fact]
    public void TouchEAndEndMarkerAreDistinctAndCommentsAreIgnored()
    {
        var chart = SimaiParser.ParseChart("(120){4}A1/B8/C/D2/E7,|| 9m is a comment\nChf[4:1],E");
        Assert.Equal(6, chart.Notes.Count);
        Assert.Contains(chart.Notes, n => n.TouchArea == "E7");
        Assert.True(chart.Notes[^1].IsFirework);
        Assert.Equal(NoteKind.TouchHold, chart.Notes[^1].Kind);
    }

    [Theory]
    [InlineData("(120){4}1m,E")]
    [InlineData("(0){4}1,E")]
    [InlineData("(NaN){4}1,E")]
    [InlineData("(120){0}1,E")]
    [InlineData("(120){4}1h[-1:2],E")]
    [InlineData("(120){4}1h[4:1,E")]
    [InlineData("(120){4}1-5,E")]
    [InlineData("(120){4}1-2[4:1],E")]
    [InlineData("(120){4}1s4[4:1],E")]
    [InlineData("(120){4}1,E,2")]
    [InlineData("1,2,E")]
    [InlineData("(120){4}1-3[4:1]-5,E")]
    public void InvalidOrUnsupportedNotationFailsInsteadOfDroppingContent(string text) =>
        Assert.Throws<ChartParseException>(() => SimaiParser.ParseChart(text));

    [Theory]
    [InlineData("1-5[4:1]", 5)]
    [InlineData("1<7[4:1]", 7)]
    [InlineData("3>7[4:1]", 7)]
    [InlineData("5^8[4:1]", 8)]
    [InlineData("1v3[4:1]", 3)]
    [InlineData("1V73[4:1]", 3)]
    [InlineData("1p6[4:1]", 6)]
    [InlineData("1q4[4:1]", 4)]
    [InlineData("1pp4[4:1]", 4)]
    [InlineData("1qq6[4:1]", 6)]
    [InlineData("1s5[4:1]", 5)]
    [InlineData("1z5[4:1]", 5)]
    [InlineData("1w5[4:1]", 5)]
    public void EveryStandardSlideShapeHasFiniteContinuousGeometry(string syntax, int end)
    {
        var path = Assert.Single(SimaiParser.ParseChart($"(120){{4}}{syntax},E").Notes, n => n.Kind == NoteKind.Slide).SlidePath!;
        Assert.InRange(SlidePath.ButtonPosition(end).DistanceTo(path.PointAt(1)), 0, .00001);
        for (var branch = 0; branch < path.Branches.Count; branch++)
        {
            var previous = path.PointAt(0, branch);
            for (var i = 1; i <= 1000; i++)
            {
                var point = path.PointAt(i / 1000.0, branch);
                Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y));
                Assert.InRange(previous.DistanceTo(point), 0, .02);
                previous = point;
            }
        }
    }

    [Fact]
    public void PAndPpRetainTheirLoopInsteadOfTakingTheShortcut()
    {
        var p = SimaiParser.ParseChart("(120){4}1p6[4:1],1pp4[4:1],E").Notes.Where(n => n.Kind == NoteKind.Slide).ToArray();
        Assert.True(p[0].SlidePath!.Length > 4);
        Assert.True(p[1].SlidePath!.Length > 4);
    }
}
