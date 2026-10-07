using MaiPlay.Core;

namespace MaiPlay.Tests;

public class CoreSlideSpeedTests
{
    [Fact]
    public void SameTrackUsesEachSlidesAuthoredDurationForAutoPlay()
    {
        var chart = SimaiParser.ParseChart("(120){4}1-5[16:1]*-5[4:1]*-5[1:1],E");
        var slides = chart.Notes.Where(n => n.Kind == NoteKind.Slide).ToArray();
        Assert.Equal(new[] { .125, .5, 2d }, slides.Select(n => n.Duration));
        Assert.All(slides, n => Assert.Equal(.5, n.SlideStartTime));

        var session = new PlaybackSession(chart, PlayMode.AutoPlay);
        session.Advance(.625);
        Assert.Equal(NoteState.Perfect, session.GetState(slides[0]));
        Assert.Equal(NoteState.Active, session.GetState(slides[1]));
        Assert.Equal(NoteState.Active, session.GetState(slides[2]));
        session.Advance(1);
        Assert.Equal(NoteState.Perfect, session.GetState(slides[1]));
        Assert.Equal(NoteState.Active, session.GetState(slides[2]));
        session.Advance(2.5);
        Assert.All(slides, n => Assert.Equal(NoteState.Perfect, session.GetState(n)));
    }

    [Theory]
    [InlineData("240#4:1", .25, .25)]
    [InlineData("240#0.7", .25, .7)]
    [InlineData("0.2##1", .2, 1)]
    [InlineData("0.4##8:1", .4, .25)]
    [InlineData("0##240#2:1", 0, .5)]
    [InlineData("#1.25", .5, 1.25)]
    public void FinalTotalDurationSetsTheWholeChainsWaitAndMovement(string timing, double wait, double duration)
    {
        var slide = ReadSlide($"1-3-7[{timing}]", first: 2.3);
        Assert.Equal(2.3 + wait, slide.SlideStartTime, 10);
        Assert.Equal(duration, slide.Duration, 10);
        AssertPoint(SlidePath.ButtonPosition(1), PositionAt(slide, slide.SlideStartTime));
        AssertPoint(SlidePath.ButtonPosition(3), PositionAt(slide, slide.SlideStartTime + duration * 14 / 34));
        AssertPoint(SlidePath.ButtonPosition(7), PositionAt(slide, slide.EndTime));
    }

    [Fact]
    public void WaitingChangesDepartureWithoutChangingSpeedAlongTheTrack()
    {
        var immediate = ReadSlide("1-5[0##1]");
        var delayed = ReadSlide("1-5[2##1]");
        AssertPoint(SlidePath.ButtonPosition(1), PositionAt(delayed, 1.5));
        foreach (var elapsed in new[] { 0, .125, .5, .875, 1 })
            AssertPoint(PositionAt(immediate, elapsed), PositionAt(delayed, elapsed + 2));
    }

    [Fact]
    public void IndividualDurationsControlSpeedOnEachConnectedSegment()
    {
        var slide = ReadSlide("1-3[0.2##0.25]-7[1]");
        var first = SlidePath.ButtonPosition(1);
        var joint = SlidePath.ButtonPosition(3);
        var last = SlidePath.ButtonPosition(7);
        AssertPoint(ChartPoint.Lerp(first, joint, .49), PositionAt(slide, .325));
        AssertPoint(joint, PositionAt(slide, .45));
        AssertPoint(ChartPoint.Lerp(joint, last, .49), PositionAt(slide, .95));
        AssertPoint(last, PositionAt(slide, 1.45));
        Assert.Equal(first.DistanceTo(joint) / (first.DistanceTo(joint) + joint.DistanceTo(last)),
            slide.SlidePath!.DistanceProgress(.25 / 1.25), 10);
    }

    [Fact]
    public void EqualDurationsTraverseDifferentLengthsAtDifferentSpeeds()
    {
        var shorter = ReadSlide("1-3[0##1]");
        var longer = ReadSlide("1-5[0##1]");
        var start = SlidePath.ButtonPosition(1);
        Assert.InRange(Math.Abs(Math.Sqrt(2) * .49 - start.DistanceTo(PositionAt(shorter, .5))), 0, .000001);
        Assert.InRange(Math.Abs(.98 - start.DistanceTo(PositionAt(longer, .5))), 0, .000001);
    }

    [Fact]
    public void SourceSamplesKeepTheLocalSpeedAndCornerOffsets()
    {
        var straight = ReadSlide("1-5[0##1]");
        AssertPoint(ChartPoint.Lerp(SlidePath.ButtonPosition(1), SlidePath.ButtonPosition(5), .931), PositionAt(straight, .95));
        var folded = ReadSlide("1v1[0##1]");
        AssertPoint(new(.074999 / 4.8, -.182001 / 4.8), PositionAt(folded, .5));
    }

    [Theory]
    [InlineData("1-5[1]")]
    [InlineData("1v1[1]")]
    [InlineData("1V73[1]")]
    [InlineData("1p6[1]")]
    [InlineData("1q4[1]")]
    [InlineData("1pp4[1]")]
    [InlineData("1qq6[1]")]
    [InlineData("1s5[1]")]
    [InlineData("1z5[1]")]
    [InlineData("1>1[1]")]
    [InlineData("1<7[1]")]
    [InlineData("1-3[0.25]-7[1]")]
    [InlineData("1-3-7[1]")]
    public void ConsumedTrackDistanceAlwaysEndsAtTheMovingStar(string syntax)
    {
        var path = ReadSlide(syntax).SlidePath!;
        for (var i = 0; i <= 100; i++)
        {
            var progress = i / 100.0;
            AssertPoint(path.PointAt(progress), path.PointAtDistance(path.DistanceProgress(progress)));
        }
    }

    [Fact]
    public void ArcMovementFollowsItsTimeFraction()
    {
        var slide = ReadSlide("1>5[0##1]");
        AssertPoint(SlidePath.ButtonPosition(2), PositionAt(slide, .25));
        AssertPoint(SlidePath.ButtonPosition(3), PositionAt(slide, .5));
        AssertPoint(SlidePath.ButtonPosition(4), PositionAt(slide, .75));
    }

    [Fact]
    public void WifiBranchesShareDepartureAndArrivalDespiteTheirDifferentLengths()
    {
        var slide = ReadSlide("1w5[0.2##0.8]");
        var start = SlidePath.ButtonPosition(1);
        var ends = new[] { 5, 4, 6 };
        for (var branch = 0; branch < ends.Length; branch++)
        {
            var end = SlidePath.ButtonPosition(ends[branch]);
            AssertPoint(start, PositionAt(slide, .2, branch));
            AssertPoint(ChartPoint.Lerp(start, end, .25), PositionAt(slide, .4, branch));
            AssertPoint(ChartPoint.Lerp(start, end, .75), PositionAt(slide, .8, branch));
            AssertPoint(end, PositionAt(slide, 1, branch));
        }
    }

    private static ChartNote ReadSlide(string source, double first = 0) =>
        Assert.Single(SimaiParser.ParseChart($"(120){{4}}{source},E", first).Notes, n => n.Kind == NoteKind.Slide);

    private static ChartPoint PositionAt(ChartNote slide, double time, int branch = 0) =>
        slide.SlidePath!.PointAt((time - slide.SlideStartTime) / slide.Duration, branch);

    private static void AssertPoint(ChartPoint expected, ChartPoint actual) =>
        Assert.InRange(expected.DistanceTo(actual), 0, .000001);
}
