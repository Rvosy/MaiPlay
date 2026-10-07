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

        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(plan.Results.OrderBy(e => e.Note.Id), results.OrderBy(e => e.Note.Id));
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
    }
}
