using System.Text.Json;
using System.Text.Json.Serialization;
using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HandReplayTests
{
    private static readonly HandContact Finger = new("fingers", InputChannel.Touch, new(0, 0), .035);
    private static Chart EmptyChart => SimaiParser.ParseChart("(120)E");

    [Fact]
    public void OneHandCanCoverAdjacentSensorsAndBothInputChannels()
    {
        var p = new ChartPoint(-.6010189638431336, -.6010189638431331);
        var button = SlidePath.ButtonPosition(7, 1.12);
        var replay = new HandReplay([new(HandSide.Left,
        [
            new(0, p, []),
            new(1, p, [new("palm", InputChannel.Touch, new(0, 0), .18),
                new("fingers", InputChannel.Button, new(button.X - p.X, button.Y - p.Y), .04)]),
            new(2, p, [])
        ])]);
        var session = new PlaybackSession(EmptyChart, PlayMode.Algorithm, replay.Events);
        session.Advance(1.1);
        Assert.Single(replay.Hands);
        Assert.Equal(p, replay.PoseAt(HandSide.Left, 1.1)!.Position);
        foreach (var area in new[] { SensorArea.A7, SensorArea.A8, SensorArea.D8, SensorArea.E8 })
        {
            Assert.True(session.Inputs.IsSensorOn(area));
            Assert.All(session.Inputs.SensorSources(area), s => Assert.Equal(HandSide.Left, replay.HandForSource(s)));
        }
        Assert.True(session.Inputs.IsButtonOn(7));
        session.Advance(2.1);
        Assert.False(session.Inputs.IsButtonOn(7));
        Assert.False(session.Inputs.IsSensorOn(SensorArea.A7));
        Assert.Null(replay.PoseAt(HandSide.Left, 2.1));
    }

    [Fact]
    public void LiftedTravelMovesMarkerWithoutActivatingIntermediateRegions()
    {
        var replay = new HandReplay([new(HandSide.Left,
        [
            new(0, new(-.8, 0), [Finger]), new(.1, new(-.8, 0), []),
            new(1.1, new(.8, 0), []), new(1.2, new(.8, 0), [Finger]), new(1.3, new(.8, 0), [])
        ])]);
        var pose = replay.PoseAt(HandSide.Left, .6)!;
        Assert.InRange(pose.Position.DistanceTo(new(0, 0)), 0, 1e-10);
        Assert.Empty(pose.Contacts);
        Assert.DoesNotContain(replay.Events, e => e.Area == "C");
        var session = new PlaybackSession(EmptyChart, PlayMode.Algorithm, replay.Events);
        session.Advance(.6);
        Assert.All(Enum.GetValues<SensorArea>(), a => Assert.False(session.Inputs.IsSensorOn(a)));
    }

    [Fact]
    public void ContactMovementBetweenKeyframesProducesRealIntermediateInput()
    {
        var replay = new HandReplay([new(HandSide.Right,
        [
            new(0, new(-.8, 0), []), new(1, new(-.8, 0), [Finger]),
            new(2, new(.8, 0), [Finger]), new(2.1, new(.8, 0), [])
        ])]);
        var session = new PlaybackSession(EmptyChart, PlayMode.Algorithm, replay.Events);
        session.Advance(1.5);
        Assert.True(session.Inputs.IsSensorOn(SensorArea.C));
        Assert.Contains(replay.Events, e => e.Area == "C" && e.IsDown && e.Time > 1 && e.Time < 1.5);
        session.Advance(2.1);
        Assert.False(session.Inputs.IsSensorOn(SensorArea.C));
    }

    [Fact]
    public void ShortPulseKeepsItsHandAttributionForTheLatchedSample()
    {
        var replay = new HandReplay([new(HandSide.Left,
        [new(1, new(0, 0), []), new(1.001, new(0, 0), [Finger]), new(1.002, new(0, 0), [])])]);
        var session = new PlaybackSession(EmptyChart, PlayMode.Algorithm, replay.Events);
        session.Advance(241d / 240);
        Assert.True(session.Inputs.IsSensorOn(SensorArea.C));
        Assert.Equal(HandSide.Left, replay.HandForSource(Assert.Single(session.Inputs.SensorSources(SensorArea.C))));
        session.Advance(242d / 240);
        Assert.False(session.Inputs.IsSensorOn(SensorArea.C));
        Assert.Empty(session.Inputs.SensorSources(SensorArea.C));
    }

    [Fact]
    public void OverlappingPartsAndHandsReleaseIndependentlyAndResetClearsOwnership()
    {
        var replay = new HandReplay([
            new(HandSide.Left, [new(0, new(0, 0), [Finger, Finger with { Id = "thumb" }]),
                new(1, new(0, 0), [Finger]), new(2, new(0, 0), [])]),
            new(HandSide.Right, [new(.5, new(0, 0), [Finger]), new(3, new(0, 0), [])])
        ]);
        var session = new PlaybackSession(EmptyChart, PlayMode.Algorithm, replay.Events);
        session.Advance(1.1);
        Assert.Equal(2, session.Inputs.SensorSources(SensorArea.C).Count);
        session.Advance(2.1);
        Assert.True(session.Inputs.IsSensorOn(SensorArea.C));
        Assert.Equal(HandSide.Right, replay.HandForSource(Assert.Single(session.Inputs.SensorSources(SensorArea.C))));
        session.Reset();
        Assert.Empty(session.Inputs.SensorSources(SensorArea.C));
        Assert.False(session.Inputs.IsSensorOn(SensorArea.C));
    }

    [Fact]
    public void HandsImportReplaysThroughNormalJudgementsAtDifferentUpdateRates()
    {
        var p = SlidePath.ButtonPosition(1, .88);
        var log = Load(new InputLog { Hands = [new(HandSide.Right,
            [new(0, p, []), new(1, p, [Finger]), new(1.05, p, [])])] });
        Assert.NotNull(log.HandReplay);
        var chart = SimaiParser.ParseChart("(120)1,E", first: 1);
        var bulk = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
        var expected = bulk.Advance(3);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(expected, e => e.IsFinal).Grade);
        foreach (var hz in new[] { 29, 60, 144 })
        {
            var session = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
            var events = new List<PlaybackEvent>();
            for (var i = 0; i <= 3 * hz; i++) events.AddRange(session.Advance(i / (double)hz));
            Assert.Equal(expected, events);
        }
    }

    [Fact]
    public void ImportRejectsMixedInputsUnreleasedHandsAndUnboundedExpansion()
    {
        var track = new HandTrack(HandSide.Left, [new(0, new(0, 0), []), new(1, new(0, 0), [])]);
        Assert.Throws<InvalidDataException>(() => Load(new InputLog { Hands = [track],
            Events = [new(0, InputChannel.Button, "1", true)] }));
        Assert.Throws<InvalidDataException>(() => Load(new InputLog { Hands = [track, track] }));
        Assert.Throws<InvalidDataException>(() => Load(new InputLog { Hands =
            [track with { Frames = [new(0, new(0, 0), []), new(1, new(0, 0), [Finger])] }] }));
        Assert.Throws<InvalidDataException>(() => Load(new InputLog { Hands =
            [track with { Frames = [new(0, new(0, 0), []), new(1e8, new(0, 0), [])] }] }));
    }

    private static InputLog Load(InputLog input)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(input, new JsonSerializerOptions
                { Converters = { new JsonStringEnumConverter() } }));
            return InputLog.Load(path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IncludedHandReplayCompletesWithRealInput()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "samples", "HandReplay");
        var chart = SimaiParser.LoadSong(directory).Charts.Single();
        var log = InputLog.Load(Path.Combine(directory, "hands.json"));
        var session = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
        var events = session.Advance(session.FinishTime + .1);
        Assert.Equal(14, session.Completed);
        Assert.Equal(0, session.Missed);
        Assert.All(events.Where(e => e.IsFinal), e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Empty(session.Inputs.SensorSources(SensorArea.A2));
        Assert.All(Enumerable.Range(1, 8), b => Assert.Empty(session.Inputs.ButtonSources(b)));
    }
}
