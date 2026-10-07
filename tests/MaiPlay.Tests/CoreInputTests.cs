using MaiPlay.Core;

namespace MaiPlay.Tests;

public class CoreInputTests
{
    [Fact]
    public void SustainedPressHasOneEdgeAndSourcesReleaseIndependently()
    {
        var input = new InputState();
        input.Sample([new(0, InputChannel.Touch, "A1", true, "left")]);
        Assert.True(input.TryUseSensorClick(SensorArea.A1));
        Assert.False(input.TryUseSensorClick(SensorArea.A1));
        input.Sample([new(.1, InputChannel.Touch, "A1", true, "right"), new(.1, InputChannel.Touch, "A1", false, "left")]);
        Assert.True(input.IsSensorOn(SensorArea.A1));
        Assert.False(input.TryUseSensorClick(SensorArea.A1));
        input.Sample([new(.2, InputChannel.Touch, "A1", false, "right")]);
        Assert.False(input.IsSensorOn(SensorArea.A1));
        input.Sample([new(.3, InputChannel.Touch, "A1", true, "right")]);
        Assert.True(input.TryUseSensorClick(SensorArea.A1));
    }

    [Fact]
    public void SubSamplePulseIsLatchedOnceAndReleasedAtNextSample()
    {
        var input = new InputState();
        input.Sample([new(0, InputChannel.Button, "1", true), new(.001, InputChannel.Button, "1", false)]);
        Assert.True(input.IsButtonOn(1));
        Assert.True(input.TryUseButtonClick(1));
        Assert.False(input.IsSensorOn(SensorArea.A1));
        input.Sample([]);
        Assert.False(input.IsButtonOn(1));
        Assert.False(input.TryUseButtonClick(1));
    }

    [Fact]
    public void SameTimestampFileOrderDeterminesPersistentState()
    {
        var down = new InputEvent(0, InputChannel.Button, "1", true);
        var up = down with { IsDown = false };
        var a = new InputState(); var b = new InputState();
        a.Sample([down, up]); b.Sample([up, down]);
        a.Sample([]); b.Sample([]);
        Assert.False(a.IsButtonOn(1));
        Assert.True(b.IsButtonOn(1));
    }

    [Fact]
    public void ContactUsesCopiedPolygonsAndCanCoverMultipleRegions()
    {
        Assert.Equal(["C"], ContactMapper.AreasAt(InputChannel.Touch, new(0, 0), 0));
        // Original source vertex (720, 424) is shared by the B1-side boundary.
        Assert.Contains("B1", ContactMapper.AreasAt(InputChannel.Touch, new(0, -296d / 720), .001));
        var covered = ContactMapper.AreasAt(InputChannel.Touch, new(0, 0), .6);
        Assert.Contains("C", covered);
        Assert.Contains("B1", covered);
        Assert.Contains("B5", covered);
        Assert.True(covered.Count > 8);
        Assert.Empty(ContactMapper.AreasAt(InputChannel.Touch, new(2, 0), .1));
        Assert.Contains("D3", ContactMapper.AreasAt(InputChannel.Touch, new(1.01, 0), .1));
        Assert.Empty(ContactMapper.AreasAt(InputChannel.Button, new(0, 0), .1));
        Assert.Equal(["1"], ContactMapper.AreasAt(InputChannel.Button, SlidePath.ButtonPosition(1, 1.12), .01));
    }

    [Fact]
    public void MovingContactReleasesOnlyItsOwnCoverageAndOffscreenClearsIt()
    {
        ContactEvent Contact(double time, string source, bool down) => new(time, source, InputChannel.Touch, new(0, 0), .01, down);
        var events = ContactMapper.Convert([Contact(0, "left", true), Contact(.1, "right", true), Contact(.2, "left", false), Contact(.3, "right", false)]);
        var input = new InputState();
        foreach (var e in events.Where(e => e.Time <= .2)) input.Sample([e]);
        Assert.True(input.IsSensorOn(SensorArea.C));
        input.Sample(events.Where(e => e.Time > .2));
        Assert.False(input.IsSensorOn(SensorArea.C));
    }

    [Fact]
    public void ReplayIsIndependentOfAdvanceCadenceAndResetClearsInputsAndScores()
    {
        var chart = SimaiParser.ParseChart("(120){#1}1,2h[#1.5],,A1,1?-5[0##1],E", first: 1);
        InputEvent[] input = [new(1, InputChannel.Button, "1", true), new(1.1, InputChannel.Button, "1", false),
            new(2.025, InputChannel.Button, "2", true), new(3.4, InputChannel.Button, "2", false),
            new(4.2, InputChannel.Touch, "A1", true), new(4.3, InputChannel.Touch, "A1", false),
            new(5.2, InputChannel.Touch, "B1", true), new(5.3, InputChannel.Touch, "B1", false),
            new(5.6, InputChannel.Touch, "B5", true), new(5.7, InputChannel.Touch, "B5", false),
            new(5.8, InputChannel.Touch, "A5", true), new(5.9, InputChannel.Touch, "A5", false)];
        var bulk = new PlaybackSession(chart, PlayMode.Algorithm, input);
        var expected = bulk.Advance(8).ToArray();
        foreach (var rate in new[] { 29, 60, 144 })
        {
            var stepped = new PlaybackSession(chart, PlayMode.Algorithm, input);
            var actual = new List<PlaybackEvent>();
            for (var i = 0; i <= rate * 8; i++) actual.AddRange(stepped.Advance(i / (double)rate));
            Assert.Equal(expected, actual);
            Assert.Equal(bulk.DXScore, stepped.DXScore);
            Assert.Equal(bulk.Achievement.DisplayText, stepped.Achievement.DisplayText);
            Assert.Equal(bulk.SlideTrace, stepped.SlideTrace);
            Assert.Equal(0, stepped.Remaining);
        }
        Assert.Empty(bulk.Advance(8));
        Assert.Empty(bulk.Advance(7.99));
        bulk.Reset();
        Assert.Empty(bulk.InputHistory);
        Assert.Equal(0, bulk.DXScore);
        Assert.Equal(0, bulk.Combo);
        Assert.False(bulk.Inputs.IsSensorOn(SensorArea.A5));
        Assert.Equal(expected, bulk.Advance(8));
    }

    [Fact]
    public void SharedSensorEdgeCanHitTapAndAdvanceSlideButCannotHitTouchTwice()
    {
        var chart = SimaiParser.ParseChart("(120){#1}1/A1/1?-3[0##1],E", first: 1);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, [new(1, InputChannel.Touch, "A1", true)]);
        var events = session.Advance(3).Where(e => e.IsFinal).ToArray();
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(events, e => e.Note.Kind == NoteKind.Tap).Grade);
        Assert.Equal(AchievementGrade.Miss, Assert.Single(events, e => e.Note.Kind == NoteKind.Touch).Grade);
        Assert.Contains(session.SlideTrace, e => e.Reason == "判定区按下");
    }

    [Fact]
    public void FinalGradesDriveAchievementDxComboAndStatisticsOnce()
    {
        var chart = SimaiParser.ParseChart("(120){#1}1b,2,3,4,E", first: 1);
        var input = new[] { new InputEvent(1.025, InputChannel.Button, "1", true), new(2.075, InputChannel.Button, "2", true), new(3.125, InputChannel.Button, "3", true) };
        var session = new PlaybackSession(chart, PlayMode.Algorithm, input);
        session.Advance(3.2);
        Assert.Equal(3, session.Combo);
        Assert.Equal(3, session.DXScore);
        Assert.Equal(3, session.LateCount);
        Assert.Equal("92.0000%", session.Achievement.DisplayText); // (4000-100-250)/4000*100 + .75
        session.Advance(5);
        Assert.Equal(0, session.Combo);
        Assert.Equal("79.5000%", session.Achievement.DisplayText);
        Assert.Equal(4, session.GradeCounts.Sum());
    }

    [Theory]
    [InlineData("{\"ruleVersion\":\"wrong\",\"events\":[]}")]
    [InlineData("{\"events\":[{\"time\":1,\"channel\":\"Button\",\"area\":\"9\",\"isDown\":true}]}")]
    [InlineData("{\"events\":[{\"time\":1,\"channel\":\"Touch\",\"area\":\"C\",\"isDown\":true,\"noteId\":0}]}")]
    [InlineData("{\"events\":[{\"channel\":\"Touch\",\"area\":\"C\",\"isDown\":true}]}")]
    [InlineData("{\"events\":[{\"time\":1,\"area\":\"1\",\"isDown\":true}]}")]
    [InlineData("{\"events\":[{\"time\":1,\"channel\":\"Touch\",\"area\":\"C\"}]}")]
    public void InvalidInputLogFailsAtImport(string json)
    {
        var path = Path.GetTempFileName();
        try { File.WriteAllText(path, json); Assert.ThrowsAny<Exception>(() => InputLog.Load(path)); }
        finally { File.Delete(path); }
    }
}
