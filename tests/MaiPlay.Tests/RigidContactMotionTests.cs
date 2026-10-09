using System.Text.Json;
using System.Text.Json.Serialization;
using MaiPlay.Core;
using static MaiPlay.Core.ContactTrajectoryCompiler;

namespace MaiPlay.Tests;

public sealed class RigidContactMotionTests
{
    [Fact]
    public void BentParallelSlidesCanCoverTheirNextTapsWithoutCollidingInTheCentre()
    {
        var chart = SimaiParser.ParseChart("(159){#0.471698113208}2-6[0.377358490566##1.509433962264]/3-7[0.377358490566##1.509433962264],23,E", first: 1);
        var tracks = chart.Notes.Where(n => n.Kind == NoteKind.Slide).Select(note =>
        {
            var hand = note.StartPosition == 2 ? HandSide.Right : HandSide.Left;
            var motion = Assert.IsType<ContactMotion>(CreateBentSlideMotion(chart, note, ContactProfile.Palm,
                new(hand == HandSide.Left ? -.2 : .2, 0), radialScale: .8));
            var start = chart.Notes.Single(n => n.Kind == NoteKind.Tap && !n.IsStar && n.StartPosition == note.StartPosition).Time;
            var frames = ContactFrames(motion.Points, motion.Channel, start - note.SlideStartTime, motion.Profile);
            var button = SlidePath.ButtonPosition(note.StartPosition, 1.12);
            return new HandTrack(hand, new HandFrame[] {
                new(1, button, Fingers(button, InputChannel.Button)), new(1.025, button, []), new(1.3, button, [])
            }.Concat(frames).ToArray());
        }).ToArray();
        var replay = new HandReplay(tracks);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(6, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.True(new HumanMotionModel().Analyze(tracks).SatisfiesMotionConstraints);
    }

    [Theory]
    [InlineData("(120){64}3b,4/2,1/5,8h[#3.40625]/6,7$>1[0.5##3.375]<7[#2.5],E")]
    [InlineData("(120){64}3b,2/4,5/1,6h[#3.40625]/8,7$<5[0.5##3.375]>7[#2.5],E")]
    public void InsetArcKeepsPerfectJudgementWhilePassingAnOuterButtonHold(string source)
    {
        var original = SimaiParser.ParseChart(source, first: 1);
        var chart = original with { Notes = original.Notes.Where(n => n.Kind is NoteKind.Hold or NoteKind.Slide)
            .Select((note, id) => note with { Id = id }).ToArray() };
        var hold = Assert.Single(CreateMotions(chart, CancellationToken.None), m => m.Note.Kind == NoteKind.Hold);
        var slide = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Slide);
        var inset = Assert.IsType<ContactMotion>(CreateInsetSlideMotion(chart, slide, ContactProfile.Palm));
        var tracks = new HandTrack[] {
            new(HandSide.Left, ContactFrames(hold.Points, hold.Channel, 0, hold.Profile)),
            new(HandSide.Right, ContactFrames(inset.Points, inset.Channel, 0, inset.Profile)) };
        var replay = new HandReplay(tracks);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, replay.Events);
        Assert.All(session.Advance(session.FinishTime + .1).Where(e => e.IsFinal),
            e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(0, new HumanMotionModel().Analyze(tracks).Collisions.Count);
    }

    [Fact]
    public void FourFingersKeepTheirIdentityAndSpacingWhenCrossingTheCentre()
    {
        var frames = ContactFrames([
            new(1, new(.8, 0)), new(1.4, new(.01, 0)), new(1.5, new(0, 0)),
            new(1.6, new(-.01, 0)), new(2, new(-.8, 0)), new(2.1, new(-.8, 0))
        ], InputChannel.Touch, 0, ContactProfile.Fingertips);
        var replay = new HandReplay([new(HandSide.Left, frames)]);
        var reference = frames[0].Contacts;
        foreach (var time in new[] { 1.0, 1.45, 1.5, 1.55, 1.9, 2.0 })
        {
            var pose = replay.PoseAt(HandSide.Left, time)!;
            Assert.Equal(4, pose.Contacts.Count);
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(reference[i].Id, pose.Contacts[i].Id);
                Assert.InRange(reference[i].Offset.DistanceTo(pose.Contacts[i].Offset), 0, 1e-10);
            }
            Assert.Equal(.15, pose.Contacts[0].Offset.DistanceTo(pose.Contacts[^1].Offset), 10);
        }
    }

    [Fact]
    public void HalfTurnPreservesRigidFingerSpacingAndUsesDisplayedGeometryForInput()
    {
        var position = new ChartPoint(.5, .1);
        var replay = new HandReplay([new(HandSide.Right, [
            new(1, position, Fingers(new(0, -1), InputChannel.Touch)),
            new(2, position, Fingers(new(0, 1), InputChannel.Touch)),
            new(2.1, position, [])
        ])]);
        foreach (var time in new[] { 1.25, 1.5, 1.75 })
        {
            var pose = replay.PoseAt(HandSide.Right, time)!;
            Assert.Equal(.15, pose.Contacts[0].Offset.DistanceTo(pose.Contacts[3].Offset), 10);
            Assert.Equal(.05, pose.Contacts[1].Offset.DistanceTo(pose.Contacts[2].Offset), 10);
            foreach (var part in pose.Contacts)
            {
                var expected = ContactMapper.AreasAt(part.Channel,
                    new(position.X + part.Offset.X, position.Y + part.Offset.Y), part.Radius).Order().ToArray();
                var actual = replay.Events.Where(e => e.Time <= time && e.Source == $"hand/Right/{part.Id}")
                    .GroupBy(e => e.Area).Where(g => g.Last().IsDown).Select(g => g.Key).Order().ToArray();
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void RigidContactGroupsSurviveInputLogRoundTrip()
    {
        var position = new ChartPoint(.5, .1);
        var tracks = new HandTrack[] { new(HandSide.Right, [
            new(1, position, Fingers(new(0, -1), InputChannel.Touch)),
            new(2, position, Fingers(new(0, 1), InputChannel.Touch)),
            new(2.1, position, [])
        ]) };
        var expected = new HandReplay(tracks);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new InputLog { Hands = tracks }, new JsonSerializerOptions
                { Converters = { new JsonStringEnumConverter() } }));
            var actual = InputLog.Load(path).HandReplay!;
            Assert.Equal(expected.Events, actual.Events);
            Assert.Equal(expected.PoseAt(HandSide.Right, 1.5)!.Contacts, actual.PoseAt(HandSide.Right, 1.5)!.Contacts);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ContinuousSlideOptionKeepsTheWholeNominalPathAndCanKeepTailContact()
    {
        var chart = SimaiParser.ParseChart("(150){4},,,,,,,,4$<8[0.4##0.4],,8,E");
        var clipped = Assert.Single(CreateMotions(chart, CancellationToken.None), m => m.Note.Kind == NoteKind.Slide);
        var complete = Assert.Single(CreateMotions(chart, CancellationToken.None,
            fullSlideNotes: new HashSet<int> { clipped.Note.Id }), m => m.Note.Kind == NoteKind.Slide);
        Assert.True(clipped.Points[^1].Time < clipped.Note.EndTime);
        Assert.Equal(complete.Note.SlideStartTime, complete.Points[0].Time);
        Assert.Equal(complete.Note.EndTime, complete.Points[^1].Time);
        Assert.Equal(complete.Note.SlidePath!.PointAt(0), complete.Points[0].Position);
        Assert.Equal(complete.Note.SlidePath.PointAt(1), complete.Points[^1].Position);
        var frames = ContactFrames(complete.Points, complete.Channel, 0, complete.Profile, releaseAtEnd: false);
        Assert.NotEmpty(frames[^1].Contacts);
        Assert.Empty(ContactFrames(complete.Points, complete.Channel, 0, complete.Profile)[^1].Contacts);
    }
}
