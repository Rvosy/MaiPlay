using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanSchedulingTests
{
    [Fact]
    public void WifiContactCanBeginBeforeAndCoverTheTapAtItsLogicalStart()
    {
        var chart = SimaiParser.ParseChart("(120){#0.298507462687}1x$w5b[0.298507462687##0.149253731343],1bx,E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        var slide = chart.Notes.Single(n => n.Kind == NoteKind.Slide);
        var strokes = plan.Strokes.Where(s => s.NoteId == slide.Id).ToArray();
        Assert.Equal(2, strokes.Length);
        Assert.All(strokes, s => Assert.True(s.Start < slide.SlideStartTime));
        Assert.Equal(0, plan.Diagnostics!.Movement.SpeedLimitViolations);
    }

    [Fact]
    public void ContactSchedulingKeepsTheOriginalDemandTimesAndObjects()
    {
        var chart = SimaiParser.ParseChart("(120){#0.3}1w5[0.3##0.15],1,E", first: 1);
        var demands = GameplayDemandCompiler.Compile(chart);
        var slide = demands.Single(d => d.Note.Kind == NoteKind.Slide);
        var tap = demands.Single(d => d.Note.Kind == NoteKind.Tap && !d.Note.IsStar);
        var patterns = PatternAnalyzer.Analyze(demands, d => d.Note.Kind == NoteKind.Slide ? d.Start - .12 : d.Start);
        var ordered = patterns.SelectMany(p => p.Demands).ToArray();
        Assert.True(Array.IndexOf(ordered, slide) < Array.IndexOf(ordered, tap));
        Assert.Same(slide, ordered.Single(d => d.Id == slide.Id));
        Assert.Equal(slide.Note.SlideStartTime, slide.Start);
        Assert.Equal(tap.Note.Time, tap.Start);
    }
}
