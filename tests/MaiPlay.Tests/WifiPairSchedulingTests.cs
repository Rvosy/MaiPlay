using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class WifiPairSchedulingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void WifiWaitsForBothHandsBeforeStarting(int start)
    {
        var end = (start + 3) % 8 + 1;
        var hold = (start + 5) % 8 + 1;
        var chart = SimaiParser.ParseChart($"(120){hold}h[#0.55]/{start}w{end}[0.5##0.5],E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        var wifi = chart.Notes.Single(n => n.Kind == NoteKind.Slide);
        var pair = plan.Strokes.Where(s => s.NoteId == wifi.Id).ToArray();
        Assert.Equal(2, pair.Length);
        Assert.Equal(2, pair.Select(s => s.Hand).Distinct().Count());
        Assert.Equal(pair[0].Start, pair[1].Start, 8);
        Assert.Equal(pair[0].End, pair[1].End, 8);
        var holding = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Hold);
        Assert.True(pair[0].Start > holding.End);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}:{e.Grade}")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void WifiDoesNotLeaveOneHandDrawingWhenTheOtherHandCannotJoin(int start)
    {
        var end = (start + 3) % 8 + 1;
        var hold = (start + 5) % 8 + 1;
        var chart = SimaiParser.ParseChart($"(120){hold}h[#4]/{start}w{end}[0.5##0.5],E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        var wifi = chart.Notes.Single(n => n.Kind == NoteKind.Slide);
        Assert.DoesNotContain(plan.Strokes, s => s.NoteId == wifi.Id);
        Assert.Contains(plan.Gaps, g => g.NoteId == wifi.Id);
        Assert.Contains(plan.Errors, e => e.Note.Id == wifi.Id);
        Assert.Contains(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Hold);
    }
}
