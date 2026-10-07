using MaiPlay.Core;

namespace MaiPlay.Tests;

public class CoreMotionTests
{
    [Fact]
    public void TapGrowsAtFixedInnerRingThenMovesOutward()
    {
        const double speed = 7.5;
        var first = 10 - NoteMotion.TapLeadIn(speed);
        Assert.False(NoteMotion.Tap(first - .001, 10, speed).Visible);
        var early = NoteMotion.Tap(first + .001, 10, speed);
        Assert.True(early.Visible);
        Assert.Equal(1.225 / 4.8, early.Radius, 12);
        Assert.InRange(early.Scale, 0, .01);
        Assert.False(early.GuideVisible);
        var fullScaleTime = 10 - (4.8 - 1.225) / NoteMotion.TapVelocity(speed);
        Assert.Equal(1, NoteMotion.Tap(fullScaleTime + .00001, 10, speed).Scale);
        Assert.Equal(1, NoteMotion.Tap(10, 10, speed).Radius, 12);
        Assert.True(NoteMotion.Tap(10.1, 10, speed).Radius > 1);
    }

    [Fact]
    public void HoldHeadStopsAtRingAndTailTravelsOutward()
    {
        var atHead = NoteMotion.Hold(10, 10, 2, 7.5);
        Assert.Equal(1, atHead.HeadRadius);
        Assert.Equal(1.225 / 4.8, atHead.TailRadius);
        Assert.False(atHead.EndPointVisible);
        var almostEnd = NoteMotion.Hold(11.9, 10, 2, 7.5);
        Assert.Equal(1, almostEnd.HeadRadius);
        Assert.InRange(almostEnd.TailRadius, .7, 1);
        Assert.True(almostEnd.EndPointVisible);
        var atEnd = NoteMotion.Hold(12, 10, 2, 7.5);
        Assert.Equal(1, atEnd.HeadRadius);
        Assert.Equal(1, atEnd.TailRadius);
    }

    [Fact]
    public void TouchFadesInAtItsSensorThenClosesItsFourFans()
    {
        var lead = NoteMotion.TouchLeadIn(7.5);
        Assert.False(NoteMotion.Touch(10 - lead - .001, 10, 7.5).Visible);
        var fade = NoteMotion.Touch(10 - .9 * lead, 10, 7.5);
        Assert.Equal(.5, fade.FanAlpha, 12);
        Assert.Equal(.4, fade.FanOffset);
        var moving = NoteMotion.Touch(10 - .4 * lead, 10, 7.5);
        Assert.Equal(1, moving.FanAlpha);
        Assert.InRange(moving.FanOffset, 0, .4);
        Assert.False(moving.Arrived);
        Assert.Equal(0, NoteMotion.Touch(10, 10, 7.5).FanOffset);
        Assert.True(NoteMotion.Touch(10, 10, 7.5).Arrived);
    }

    [Fact]
    public void SlideTrackAndMovingStarHaveDifferentAppearanceTimings()
    {
        var lead = NoteMotion.SlideLeadIn(7.5);
        Assert.Equal(0, NoteMotion.SlideAlpha(10 - lead - .001, 10, 7.5, false));
        Assert.Equal(.25, NoteMotion.SlideAlpha(10 - lead + .1, 10, 7.5, false), 10);
        Assert.Equal(.5, NoteMotion.SlideAlpha(10 - lead + .1, 10, 7.5, true), 10);
        Assert.Equal(1, NoteMotion.SlideAlpha(9.951, 10, 7.5, false));
        Assert.False(NoteMotion.SlideStar(9.99, 10, 10.5).Visible);
        Assert.Equal(new SlideStarPose(true, .5, 1), NoteMotion.SlideStar(10.25, 10, 10.5));
        Assert.Equal(new SlideStarPose(true, 1, 1.5), NoteMotion.SlideStar(10.5, 10, 10.5));
    }

    [Fact]
    public void TwoSpeedSettingsAffectOnlyTheirOwnNoteFamilies()
    {
        var notes = SimaiParser.ParseChart("(120){4}1/C/2-6[4:1],E").Notes;
        var tap = notes.First(n => n.Kind == NoteKind.Tap);
        var touch = notes.First(n => n.Kind == NoteKind.Touch);
        Assert.Equal(NoteMotion.AppearTime(tap, 7.5, 1), NoteMotion.AppearTime(tap, 7.5, 12));
        Assert.Equal(NoteMotion.AppearTime(touch, 1, 7.5), NoteMotion.AppearTime(touch, 12, 7.5));
        Assert.True(NoteMotion.AppearTime(tap, 2, 7.5) < NoteMotion.AppearTime(tap, 10, 7.5));
        Assert.True(NoteMotion.AppearTime(touch, 7.5, 2) < NoteMotion.AppearTime(touch, 7.5, 10));
    }

    [Fact]
    public void HeadlessSlideKeepsWaitAndNormalFadeLikeCurrentMajSimai()
    {
        var chart = SimaiParser.ParseChart("(120){4}1?-5[4:1],2!-6[4:1],E");
        Assert.All(chart.Notes, n => { Assert.Equal(NoteKind.Slide, n.Kind); Assert.True(n.IsHeadless); Assert.False(n.IsSuddenSlide); });
        Assert.Equal(.5, chart.Notes[0].SlideStartTime);
        Assert.Equal(1, chart.Notes[1].SlideStartTime);
    }

    [Fact]
    public void RealAndFakeStarsHaveUpstreamRotationRates()
    {
        var chart = SimaiParser.ParseChart("(120){4}1-5[4:1],2$,3$$,E");
        var star = chart.Notes[0];
        Assert.Equal(NoteMotion.StarRotateSpeed(.5, 20), star.StarRotateSpeed, 10);
        Assert.Equal(0, chart.Notes.First(n => n.Source.Trim() == "2$").StarRotateSpeed);
        Assert.Equal(-440, chart.Notes.First(n => n.Source.Trim() == "3$$").StarRotateSpeed);
    }
}
