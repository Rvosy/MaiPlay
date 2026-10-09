using MaiPlay.Core;
using static MaiPlay.Core.ContactTrajectoryCompiler;

namespace MaiPlay.Tests;

public sealed class DemandCoverageProbeTests
{
    [Theory]
    [InlineData(1.003)]
    [InlineData(200.003)]
    public void WifiCanDirectlyCoverTheFollowingTapAndTouch(double first)
    {
        var chart = SimaiParser.ParseChart(
            "(186){4}1b$w5[0.322580645161##0.161290322581],1b/Cf,E", first: first);
        var tracks = CreateMotions(chart, CancellationToken.None).Where(m => m.Note.Kind == NoteKind.Slide)
            .Select((m, index) => new HandTrack(index == 0 ? HandSide.Left : HandSide.Right,
                ContactFrames(m.Points, m.Channel, .12))).ToArray();
        var inputs = new HandReplay(tracks).Events;
        var session = new PlaybackSession(chart, PlayMode.Algorithm, inputs);
        session.Advance(session.FinishTime);
        var probe = new DemandCoverageProbe(chart);
        var targets = chart.Notes.Where(n => n.Time > first && n.Kind is NoteKind.Tap or NoteKind.Touch).ToArray();

        Assert.Equal(2, targets.Length);
        foreach (var target in targets)
        {
            Assert.Equal(AchievementGrade.Perfect, session.GetResult(target)!.Grade);
            Assert.True(probe.IsCovered(target, tracks));
            Assert.True(probe.IsCoveredByInputs(target, inputs));
        }
        var slide = chart.Notes.Single(n => n.Kind == NoteKind.Slide);
        Assert.Equal(AchievementGrade.Perfect, session.GetResult(slide)!.Grade);
        Assert.True(probe.IsCovered(slide, tracks));
        Assert.True(probe.IsCoveredByInputs(slide, inputs));
    }

    [Theory]
    [InlineData(1.003, 0, true)]
    [InlineData(200.003, 0, true)]
    [InlineData(200.0001, 0, true)]
    [InlineData(200.0001, .243, true)]
    [InlineData(200.003, .243, false)]
    [InlineData(200.003, .3, false)]
    public void SlideCoveragePreservesBothWaitingTimeAndLogicSamplingPhase(double first, double delay, bool covered)
    {
        var chart = SimaiParser.ParseChart("(120)1?-5[0.5##0.4],E", first: first);
        var slide = Assert.Single(chart.Notes);
        // 直线 1→5 的独立输入路线；末区时刻沿用已核验的 0.152 尾区比例。
        var route = new[] { ("A1", .5), ("B1", .58), ("C", .66), ("B5", .74), ("A5", .9 - .4 * .152) };
        var inputs = route.SelectMany(step => new[]
        {
            new InputEvent(first + step.Item2 + delay, InputChannel.Touch, step.Item1, true),
            new InputEvent(first + step.Item2 + delay + .025, InputChannel.Touch, step.Item1, false)
        }).OrderBy(e => e.Time).ToArray();
        var session = new PlaybackSession(chart, PlayMode.Algorithm, inputs);
        session.Advance(session.FinishTime + .1);
        Assert.Equal(covered, session.GetResult(slide)!.Grade == AchievementGrade.Perfect);
        Assert.Equal(covered, new DemandCoverageProbe(chart).IsCoveredByInputs(slide, inputs));
    }

    [Theory]
    [InlineData("1", "A1", .05)]
    [InlineData("C", "C", .2)]
    [InlineData("1", "A8", 0)]
    [InlineData("C", "B1", 0)]
    [InlineData("1h[4:1]", "A1", 0)]
    [InlineData("Ch[4:1]", "C", 0)]
    public void NonCriticalPerfectAndUnsupportedDemandsDoNotCountAsCovered(string notation, string area, double shift)
    {
        var chart = SimaiParser.ParseChart($"(120){notation},E", first: 1.003);
        var note = Assert.Single(chart.Notes);
        var probe = new DemandCoverageProbe(chart);

        Assert.False(probe.IsCovered(note, [Contact(HandSide.Left, area, note.Time + shift)]));
        Assert.False(probe.IsCovered(note, []));
    }

    [Fact]
    public void AContactHeldSinceBeforeTheWindowDoesNotCreateANewClick()
    {
        var chart = SimaiParser.ParseChart("(120)1,E", first: 200.003);
        var note = Assert.Single(chart.Notes);
        var position = TouchCentre("A1");
        HandTrack[] tracks = [new(HandSide.Left,
            [new(note.Time - .2, position, Fingers(position, InputChannel.Touch)), new(note.Time + .1, position, [])])];

        Assert.False(new DemandCoverageProbe(chart).IsCovered(note, tracks));
    }

    [Fact]
    public void TouchGroupInheritanceDoesNotCountAsDirectCoverage()
    {
        var chart = SimaiParser.ParseChart("(120)B1/B2/C,E", first: 1);
        HandTrack[] tracks = [Contact(HandSide.Left, "B1", 1), Contact(HandSide.Right, "B2", 1)];
        var centre = chart.Notes.Single(n => n.TouchArea == "C");
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(tracks).Events);
        session.Advance(session.FinishTime);

        Assert.Equal(AchievementGrade.Perfect, session.GetResult(centre)!.Grade);
        Assert.Equal("touch-group", session.GetResult(centre)!.Reason);
        Assert.False(new DemandCoverageProbe(chart).IsCovered(centre, tracks));
    }

    private static HandTrack Contact(HandSide hand, string area, double time)
    {
        var position = TouchCentre(area);
        return new(hand, [new(time, position, Fingers(position, InputChannel.Touch)), new(time + .025, position, [])]);
    }
}
