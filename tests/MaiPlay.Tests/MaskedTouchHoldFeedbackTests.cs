using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class MaskedTouchHoldFeedbackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlidePalmDoesNotMaskTheHeadsOfLaterTouchHolds(bool human)
    {
        var chart = SimaiParser.ParseChart(
            "(100){#0.6}1p8[0.6##7.8]/8,{#2.4}Ch[#0.6],Ch[#0.6],Ch[#0.6],Ch[#0.6],E", first: 1);
        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}/{e.Reason}")));
        Assert.Empty(plan.Gaps);
        var slide = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Slide);
        Assert.All(plan.Replay.PoseAt(slide.Hand, slide.Start + .01)!.Contacts, c => Assert.StartsWith("finger", c.Id));
        foreach (var hold in plan.Strokes.Where(s => chart.Notes[s.NoteId].Kind == NoteKind.TouchHold))
            Assert.Equal("palm", Assert.Single(plan.Replay.PoseAt(hold.Hand, hold.Start + .01)!.Contacts).Id);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }
}
