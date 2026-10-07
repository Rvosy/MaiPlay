using MaiPlay.Core;

namespace MaiPlay.Tests;

public class CoreModifierTests
{
    [Fact]
    public void TouchBreakAndFireworkFlagsReachTheirOwnNoteKinds()
    {
        var chart = SimaiParser.ParseChart("(120){4}B1bf,Chbf[2:1],E", first: 1);
        Assert.Equal(NoteKind.Touch, chart.Notes[0].Kind);
        Assert.Equal(NoteKind.TouchHold, chart.Notes[1].Kind);
        Assert.All(chart.Notes, n =>
        {
            Assert.True(n.IsBreak);
            Assert.True(n.IsFirework);
            Assert.False(n.IsEx);
        });
        var session = new PlaybackSession(chart, PlayMode.Algorithm,
        [new(1.175, InputChannel.Touch, "B1", true), new(1.5, InputChannel.Touch, "C", true)]);
        session.Advance(3);
        Assert.Equal(AchievementGrade.LatePerfect2nd, session.GetResult(chart.Notes[0])!.Grade);
        Assert.Equal(AchievementGrade.Perfect, session.GetResult(chart.Notes[1])!.Grade);
        Assert.Equal(5000, session.Achievement.TotalBaseScore);
        Assert.Equal(25, session.Achievement.LostExtraScore);
        Assert.Equal(100.875m, session.Achievement.Percentage);
    }

    [Theory]
    [InlineData("B1x")]
    [InlineData("Chx[4:1]")]
    [InlineData("B1$")]
    [InlineData("Ch$[4:1]")]
    [InlineData("1f")]
    [InlineData("1hf[4:1]")]
    [InlineData("1f-5[4:1]")]
    public void ModifiersWithoutSupportedGameplayMeaningFailClearly(string note)
    {
        Assert.Throws<ChartParseException>(() => SimaiParser.ParseChart($"(120){{4}}{note},E"));
    }

    [Theory]
    [InlineData("!")]
    [InlineData("?")]
    public void BothNoHeadFormsKeepTheMajSimaiWait(string modifier)
    {
        var note = Assert.Single(SimaiParser.ParseChart($"(120){{4}}1{modifier}-5[4:1],E").Notes);
        Assert.Equal(NoteKind.Slide, note.Kind);
        Assert.True(note.IsHeadless);
        Assert.Equal(.5, note.SlideStartTime);
        Assert.False(note.IsSuddenSlide);
    }

    [Fact]
    public void FakeEachOffsetsEachSubgroupByA128thWithoutMovingTheCommaGrid()
    {
        // MajSimai fdb2a3e SimaiParser: fakeTime += 1.875 / bpm.
        var chart = SimaiParser.ParseChart("(120){4}1`2-6[4:1]`B3,4,E", first: 1);
        var heads = chart.Notes.Where(n => n.Kind != NoteKind.Slide).ToArray();
        Assert.Equal([1d, 1.015625, 1.03125, 1.5], heads.Select(n => n.Time));
        Assert.All(heads, n => Assert.False(n.IsEach));
        var slide = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Slide);
        Assert.Equal(1.015625, slide.Time);
        Assert.Equal(1.515625, slide.SlideStartTime);
    }

    [Fact]
    public void EachWithinFakeEachSubgroupsIsPreservedButTouchGroupsDoNotCrossTheirTimes()
    {
        var chart = SimaiParser.ParseChart("(120){4}B1/B2`B3/B4,E", first: 1);
        Assert.All(chart.Notes, n => Assert.True(n.IsEach));
        var session = new PlaybackSession(chart, PlayMode.Algorithm,
            [new(1, InputChannel.Touch, "B1", true), new(1, InputChannel.Touch, "B2", true)]);
        session.Advance(2);
        Assert.Equal(2, session.Completed);
        Assert.Equal(2, session.Missed);
    }
}
