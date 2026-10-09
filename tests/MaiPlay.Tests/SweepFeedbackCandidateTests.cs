using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class SweepFeedbackCandidateTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(69.253731343282)]
    public void MaskedSweepRepairDoesNotDiscardSuccessfulEarlyContactRepair(double first)
    {
        var chart = SimaiParser.ParseChart(
            "(201){8}1/7h[1:1],1,1,1,{32}1,2,3,4,5,6,,8,{48}1,2,3,4,5,6,,8,1,2,3,4,5,{#1}," +
            "(100){#0.15}6,6,7/8,1/2,3/4,{#0.075}6,5,6,E", first: first);
        var plan = HumanPlanner.Plan(chart);
        var hold = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Hold);
        var laterNotes = chart.Notes.TakeLast(11).Select(n => n.Id).ToHashSet();
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(plan.Results, e => e.Note.Id == hold.Id).Grade);
        Assert.All(plan.Results.Where(e => laterNotes.Contains(e.Note.Id)), e => Assert.Equal(AchievementGrade.Perfect, e.Grade));

        var firstEight = chart.Notes.First(n => n.Kind == NoteKind.Tap && n.StartPosition == 8 && n.Time < hold.EndTime);
        var precedingSix = chart.Notes.Last(n => n.Kind == NoteKind.Tap && n.StartPosition == 6 && n.Time < firstEight.Time);
        var sixStroke = Assert.Single(plan.Strokes, s => s.NoteId == precedingSix.Id);
        var eightStroke = Assert.Single(plan.Strokes, s => s.NoteId == firstEight.Id);
        var holdStroke = Assert.Single(plan.Strokes, s => s.NoteId == hold.Id);
        Assert.Equal(sixStroke.Hand, eightStroke.Hand);
        Assert.NotEqual(holdStroke.Hand, eightStroke.Hand);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(plan.Results, e => e.Note.Id == firstEight.Id).Grade);
        var frames = plan.Tracks.Single(t => t.Hand == eightStroke.Hand).Frames;
        var flight = Assert.Single(frames.Zip(frames.Skip(1)), pair => pair.First.Contacts.Count == 0 &&
            pair.First.Time >= sixStroke.Start && pair.Second.Time <= eightStroke.Start && pair.Second.Time > pair.First.Time);
        Assert.True(flight.Second.Time - flight.First.Time > PlaybackSession.LogicStep);
        foreach (var time in new[] { flight.First.Time, (flight.First.Time + flight.Second.Time) / 2, flight.Second.Time })
        {
            var pose = plan.Replay.PoseAt(holdStroke.Hand, time)!;
            Assert.Contains(pose.Contacts, c => c.Channel == InputChannel.Button && ContactMapper.AreasAt(c.Channel,
                new(pose.Position.X + c.Offset.X, pose.Position.Y + c.Offset.Y), c.Radius).Contains(hold.StartPosition.ToString()));
        }

        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(plan.Results.OrderBy(e => e.Note.Id), results.OrderBy(e => e.Note.Id));
        var movement = new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes);
        Assert.Equal(0, movement.SpeedLimitViolations);
        Assert.Equal(0, movement.Collisions.Count);
    }
}
