using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class PalmSlideCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WifiCanCoverAConcurrentTouchGroupAfterFinishingItsButtonHead(bool human)
    {
        var touches = string.Join('/', Enumerable.Range(1, 8).SelectMany(i => new[] { $"B{i}", $"E{i}" }));
        var chart = SimaiParser.ParseChart($"(120){{#0.333333333333}}1w5[0.333333333333##0.166666666667],{{#0.003472222222}}1b/{touches},Cf,E", first: 1);
        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        var wifi = chart.Notes.Single(n => n.Kind == NoteKind.Slide);
        Assert.Equal(2, plan.Strokes.Where(s => s.NoteId == wifi.Id).Select(s => s.Hand).Distinct().Count());
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TwoOccupiedSlidingHandsCoverTheCentreTouchWithTheirPalms(bool human, bool reverse)
    {
        var slides = reverse ? "7-2[0.5##0.4]/3-6[0.5##0.4]" : "2-7[0.5##0.4]/6-3[0.5##0.4]";
        var chart = SimaiParser.ParseChart($"(120){{#0.7}}{slides},C,E", first: 1);
        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        var touch = chart.Notes.Single(n => n.Kind == NoteKind.Touch);
        var judged = plan.Results.Single(e => e.Note.Id == touch.Id);
        var slidesInProgress = plan.Strokes.Where(s => s.Technique == HandTechnique.Slide &&
            s.Start < judged.Time && s.End >= judged.Time - PlaybackSession.LogicStep).ToArray();
        Assert.Equal(2, slidesInProgress.Select(s => s.Hand).Distinct().Count());
        Assert.DoesNotContain(plan.Strokes, s => s.NoteId == touch.Id && s.Start <= judged.Time);
        Assert.Contains(plan.Replay.Events, e => e.Channel == InputChannel.Touch && e.Area == "C" && e.IsDown);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }
}
