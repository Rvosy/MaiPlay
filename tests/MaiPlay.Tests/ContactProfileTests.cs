using MaiPlay.Core;
using static MaiPlay.Core.ContactTrajectoryCompiler;

namespace MaiPlay.Tests;

public sealed class ContactProfileTests
{
    [Fact]
    public void TouchDefaultsToAPalmWhileButtonContactKeepsItsOriginalWidth()
    {
        var position = TouchCentre("E2");
        var palm = ContactFrames([new(1, position), new(1.025, position)], InputChannel.Touch, 0)[0];
        var contact = Assert.Single(palm.Contacts);
        Assert.InRange(contact.Radius * 2 * CabinetGeometry.Approximate43Inch.JudgementRadiusMillimeters, 89, 90);
        Assert.Contains("A1", Areas(palm));

        var small = ContactFrames([new(1, position), new(1.025, position)], InputChannel.Touch, 0, ContactProfile.Fingertips)[0];
        Assert.DoesNotContain("A1", Areas(small));
        Assert.Equal(Fingers(position, InputChannel.Button),
            ContactFrames([new(1, position), new(1.025, position)], InputChannel.Button, 0)[0].Contacts);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1h[#0.5]")]
    public void ActualEarlyContactIdentifiesOnlyTheInterferingPalmAndFingertipsAvoidIt(string next)
    {
        var chart = SimaiParser.ParseChart($"(120){{#0.1}}E2,{next},{{#1}}C,E", first: 1);
        var initial = Replay(chart, CreateMotions(chart, CancellationToken.None).ToArray());
        var interference = Assert.Single(initial.Errors);
        Assert.True(interference.DifferenceSeconds < 0);
        var candidates = PlannerTimingFeedback.FingertipCandidates(initial);
        Assert.Equal([0], candidates.Order().ToArray());
        Assert.Equal(1.1, PlannerTimingFeedback.DelayedContacts(initial)[0], 6);

        var repairedMotions = CreateMotions(chart, CancellationToken.None, candidates).ToArray();
        var repaired = Replay(chart, repairedMotions);
        Assert.True(repaired.AllCriticalPerfect, string.Join(", ", repaired.Errors.Select(e => e.Note.Source + ":" + e.Grade)));
        Assert.Equal(ContactProfile.Fingertips, repairedMotions.Single(m => m.Note.Id == 0).Profile);
        Assert.Equal(ContactProfile.Palm, repairedMotions.Single(m => m.Note.TouchArea == "C").Profile);
        Assert.Empty(PlannerTimingFeedback.FingertipCandidates(repaired));
    }

    [Theory]
    [InlineData("4-1[0.5##9.25]", false)]
    [InlineData("4-1[0.5##9.25]", true)]
    [InlineData("4<8[0.4##0.4]", false)]
    [InlineData("4<8[0.4##0.4]", true)]
    public void SlideCalibrationUsesTheSameContactProfileAsItsActualReplay(string notation, bool small)
    {
        var chart = SimaiParser.ParseChart("(120)" + notation + ",E", first: 1);
        var slide = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Slide);
        var motion = Assert.Single(CreateMotions(chart, CancellationToken.None, small ? new HashSet<int> { slide.Id } : null),
            m => m.Note.Id == slide.Id);
        var isolated = chart with { Notes = [slide with { Id = 0 }] };
        var plan = Replay(isolated, [motion with { Note = isolated.Notes[0] }]);
        Assert.True(plan.AllCriticalPerfect, string.Join(", ", plan.Errors.Select(e => e.Grade)));
        Assert.Equal(small ? ContactProfile.Fingertips : ContactProfile.Palm, motion.Profile);
    }

    private static IReadOnlyList<string> Areas(HandFrame frame) => frame.Contacts.SelectMany(c =>
        ContactMapper.AreasAt(c.Channel, new(frame.Position.X + c.Offset.X, frame.Position.Y + c.Offset.Y), c.Radius)).ToArray();

    private static HandPlan Replay(Chart chart, ContactMotion[] motions)
    {
        var tracks = motions.GroupBy(m => m.Channel == InputChannel.Touch ? HandSide.Left : HandSide.Right)
            .Select(g => new HandTrack(g.Key, g.OrderBy(m => m.Points[0].Time).SelectMany(m =>
                ContactFrames(m.Points, m.Channel, 0, m.Profile)).ToArray())).ToArray();
        var replay = new HandReplay(tracks);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        return new(chart, tracks, replay, motions.Select(m => new PlannedStroke(m.Note.Id,
                m.Channel == InputChannel.Touch ? HandSide.Left : HandSide.Right, m.Points[0].Time, m.Points[^1].Time, m.Channel)).ToArray(),
            results, session.Achievement.Percentage, session.DXScore, []);
    }
}
