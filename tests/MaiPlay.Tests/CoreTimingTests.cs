using MaiPlay.Core;

namespace MaiPlay.Tests;

public class CoreTimingTests
{
    [Fact]
    public void BpmChangesAndFirstOffsetUseMusicSeconds()
    {
        var chart = SimaiParser.ParseChart("(120){8}1,2,(240){4}3,4,E", first: 1.25);
        Assert.Equal([1.25, 1.5, 1.75, 2.0], chart.Notes.Select(n => n.Time));
        Assert.Equal(2.25, chart.Duration, 10);
    }

    [Fact]
    public void SlideWaitIsAQuarterNoteIndependentOfSubdivision()
    {
        var chart = SimaiParser.ParseChart("(120){16}1-5[4:1],2-6[240#4:1],3-7[0.1##0.7],E");
        var slides = chart.Notes.Where(n => n.Kind == NoteKind.Slide).ToArray();
        Assert.Equal(.5, slides[0].SlideStartTime, 10);
        Assert.Equal(.5, slides[0].Duration, 10);
        Assert.Equal(.375, slides[1].SlideStartTime, 10);
        Assert.Equal(.25, slides[1].Duration, 10);
        Assert.Equal(.35, slides[2].SlideStartTime, 10);
        Assert.Equal(.7, slides[2].Duration, 10);
    }

    [Fact]
    public void ChainKeepsIndividualSegmentDurations()
    {
        var note = Assert.Single(SimaiParser.ParseChart("(120){4}1-3[8:1]-5[2:1],E").Notes, n => n.Kind == NoteKind.Slide);
        Assert.Equal(.5, note.SlideStartTime, 10);
        Assert.Equal(1.25, note.Duration, 10);
        AssertPoint(SlidePath.ButtonPosition(3), note.SlidePath!.PointAt(.2));
        Assert.InRange(note.SlidePath.DistanceProgress(.2), .499999, .500001);
    }

    [Fact]
    public void ChainWithSingleTotalDurationUsesUpstreamPrefabTimingUnits()
    {
        var note = Assert.Single(SimaiParser.ParseChart("(120){4}1-3-7[1:1],E").Notes, n => n.Kind == NoteKind.Slide);
        var ratio = 14.0 / (14 + 20);
        Assert.Equal(2, note.Duration);
        AssertPoint(SlidePath.ButtonPosition(3), note.SlidePath!.PointAt(ratio));
    }

    [Fact]
    public void AutoAndNoInputHaveDifferentLongNoteLifecycles()
    {
        var chart = SimaiParser.ParseChart("(120){4}1/2h[2:1]/Ch[1:1]/3-7[2:1],E");
        var auto = new PlaybackSession(chart, PlayMode.AutoPlay);
        var initial = auto.Advance(0);
        Assert.Equal(2, initial.Count(e => e.Kind == PlaybackEventKind.Perfect));
        Assert.Equal(2, initial.Count(e => e.Kind == PlaybackEventKind.Started));
        Assert.Equal(3, auto.Remaining);
        Assert.Single(auto.Advance(.5), e => e.Kind == PlaybackEventKind.Started);
        auto.Advance(2);
        Assert.Equal(5, auto.Completed);
        Assert.Equal(5, auto.Combo);
        Assert.Equal(0, auto.Missed);

        var noInput = new PlaybackSession(chart, PlayMode.Algorithm);
        Assert.Empty(noInput.Advance(0));
        Assert.Empty(noInput.Advance(.15));
        Assert.Equal(2, noInput.Advance(.15 + PlaybackSession.LogicStep).Count(e => e.IsFinal));
        Assert.Empty(noInput.Advance(.99));
        Assert.Single(noInput.Advance(1));
        Assert.Empty(noInput.Advance(1.5));
        Assert.Single(noInput.Advance(2));
        Assert.Empty(noInput.Advance(2.1));
        Assert.Single(noInput.Advance(2.1 + PlaybackSession.LogicStep));
        Assert.Equal(5, noInput.Missed);
        Assert.Equal(0, noInput.Completed);
        Assert.Equal(0, noInput.Remaining);
    }

    [Fact]
    public void LargeAudioPositionJumpsProduceExactlyTheSameEventsAsSmallFrames()
    {
        var chart = SimaiParser.ParseChart("(137){16}1,2,3h[2:1],4,5-1[1:1],C,Ch[2:1],7b,E");
        foreach (var mode in Enum.GetValues<PlayMode>())
        {
            var fast = new PlaybackSession(chart, mode);
            var expected = fast.Advance(100).ToArray();
            var slow = new PlaybackSession(chart, mode);
            var actual = new List<PlaybackEvent>();
            for (var i = 0; i <= 600; i++) actual.AddRange(slow.Advance(i / 100.0));
            Assert.Equal(expected, actual);
            Assert.Equal(fast.Combo, slow.Combo);
            Assert.Equal(fast.Completed, slow.Completed);
            Assert.Equal(fast.Missed, slow.Missed);
        }
    }

    [Fact]
    public void PauseDoesNotRetriggerAndRestartClearsEveryCounter()
    {
        var session = new PlaybackSession(SimaiParser.ParseChart("(120){4}1,2,3,E"), PlayMode.AutoPlay);
        var before = session.Advance(.5).ToArray();
        Assert.Empty(session.Advance(.5));
        Assert.Empty(session.Advance(.499));
        Assert.Equal(2, session.Combo);
        session.Reset();
        Assert.Equal(3, session.Remaining);
        Assert.Equal(0, session.Combo);
        Assert.Null(session.LastEvent);
        Assert.Equal(before, session.Advance(.5));
    }

    [Fact]
    public void NegativeOffsetIsProcessedWithoutMissingEarlyNotes()
    {
        var chart = SimaiParser.ParseChart("(120){4}1,2,E", first: -.5);
        var session = new PlaybackSession(chart, PlayMode.AutoPlay);
        Assert.Equal(2, session.Advance(0).Count);
        Assert.Equal(0, session.Remaining);
    }

    private static void AssertPoint(ChartPoint expected, ChartPoint actual) => Assert.InRange(expected.DistanceTo(actual), 0, .00001);
}
