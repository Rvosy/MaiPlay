using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class SlideContactRecoveryTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 79.11111111109507)]
    [InlineData(true, 79.11111111109507)]
    public void AdjacentSlideTailFeedbackDoesNotDelayTheInterferingSlidePastItsOwnWindow(bool human, double first)
    {
        var chart = SimaiParser.ParseChart("(135){4}7<4[4:1],,2>5[8:1],1>4[8:1],8<5[8:1],,1>4[8:1]/8<5[8:1],E", first: first);
        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source} {e.Grade} {e.DifferenceSeconds}")));
        var interferingSlide = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Slide && n.StartPosition == 2);
        var action = Assert.Single(plan.Strokes, s => s.NoteId == interferingSlide.Id);
        Assert.True(action.Start < interferingSlide.SlideStartTime);
        Assert.Equal("palm", Assert.Single(plan.Replay.PoseAt(action.Hand, action.Start + .01)!.Contacts).Id);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }
}
