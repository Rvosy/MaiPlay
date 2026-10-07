using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class GameplayDemandTests
{
    [Fact]
    public void CompilerKeepsHeadAndSlideAsIndependentRequirements()
    {
        var chart = SimaiParser.ParseChart("(120){4}1-5[0.5##1]/Ch[#1],2,E", first: 1);
        var demands = GameplayDemandCompiler.Compile(chart);
        Assert.Equal(chart.Notes.Count, demands.Count);
        Assert.Equal(chart.Notes.Select(n => n.Id).Order(), demands.Select(d => d.Id).Order());
        var head = Assert.Single(demands, d => d.Note.IsStar);
        var slide = Assert.Single(demands, d => d.Note.Kind == NoteKind.Slide);
        Assert.Equal(InputChannel.Button, head.Channel);
        Assert.Equal("1", head.Target);
        Assert.Equal(1, head.Start);
        Assert.Equal(InputChannel.Touch, slide.Channel);
        Assert.Equal("A1", slide.Target);
        Assert.Equal(slide.Note.SlideStartTime, slide.Start);
        Assert.Equal(slide.Note.EndTime, slide.End);
        Assert.NotEqual(head.Id, slide.Id);
        var touchHold = Assert.Single(demands, d => d.Note.Kind == NoteKind.TouchHold);
        Assert.Equal("C", touchHold.Target);
        Assert.Equal(2, touchHold.End);
    }

    [Theory]
    [InlineData("1,1,1,1", PatternKind.Jack)]
    [InlineData("1,5,1,5,1,5", PatternKind.Trill)]
    [InlineData("1,4,1,5,1,6", PatternKind.Anchor)]
    [InlineData("4,1,5,1,6,1", PatternKind.Anchor)]
    [InlineData("8,1,2,3", PatternKind.Stair)]
    [InlineData("2,1,8,7", PatternKind.Stair)]
    [InlineData("1,2,3,4,5,6,7,8,1", PatternKind.Rotation)]
    [InlineData("1,5,2", PatternKind.Stream)]
    public void RecognizesButtonConfigurationsFromTheirTimingAndPositions(string notes, PatternKind expected)
    {
        var chart = SimaiParser.ParseChart("(120){#0.1}" + notes + ",E", first: 1);
        var segment = Assert.Single(PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart)));
        Assert.Equal(expected, segment.Kind);
        Assert.Equal(chart.Notes.Count, segment.Demands.Count);
    }

    [Fact]
    public void ChordsAndSustainedContactsDivideButtonRunsWithoutLosingRequirements()
    {
        var chart = SimaiParser.ParseChart("(120){#0.1}1,2,3,4/5,6h[#1],7,8,1,B2,2w6[0.3##0.5],E", first: 1);
        var demands = GameplayDemandCompiler.Compile(chart);
        var segments = PatternAnalyzer.Analyze(demands);
        Assert.Equal(demands.Select(d => d.Id).Order(), segments.SelectMany(s => s.Demands).Select(d => d.Id).Order());
        Assert.Contains(segments, s => s.Kind == PatternKind.Chord && s.Demands.Count == 2);
        Assert.Contains(segments, s => s.Kind == PatternKind.HoldLock);
        Assert.Contains(segments, s => s.Kind == PatternKind.Touch);
        Assert.Contains(segments, s => s.Kind == PatternKind.Wifi);
        Assert.All(segments.Where(s => s.Kind is PatternKind.Stair or PatternKind.Rotation), s =>
            Assert.All(s.Demands, d => Assert.Equal(NoteKind.Tap, d.Note.Kind)));
        Assert.True(segments.Zip(segments.Skip(1), (a, b) => a.Start <= b.Start).All(ordered => ordered));
    }

    [Fact]
    public void PatternAfterUnstructuredNotesStillGetsItsOwnCandidateWindow()
    {
        var chart = SimaiParser.ParseChart("(120){#0.1}6,1,1,1,1,E", first: 1);
        var segments = PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart));
        Assert.Equal(2, segments.Count);
        Assert.Equal(PatternKind.SingleTap, segments[0].Kind);
        Assert.Equal(PatternKind.Jack, segments[1].Kind);
        Assert.Equal(4, segments[1].Demands.Count);
    }

    [Theory]
    [InlineData(.25)]
    [InlineData(.125)]
    [InlineData(.1)]
    public void RepeatCandidatesKeepSameHandAlternatingAndFreeAssignmentsAtEverySpeed(double interval)
    {
        var chart = SimaiParser.ParseChart("(120)1,E", first: 1);
        chart = chart with { Notes = Enumerable.Range(0, 4).Select(i => chart.Notes[0] with { Id = i, Time = 1 + i * interval }).ToArray() };
        var segment = Assert.Single(PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart)));
        var candidates = TechniqueCandidates.For(segment);
        Assert.Contains(candidates, c => c.Hands[0] == c.Hands[1] && c.Hands[0] is not null);
        Assert.Contains(candidates, c => c.Hands[0] != c.Hands[1]);
        Assert.Contains(candidates, c => c.Hands.All(h => h is null));
    }

    [Theory]
    [InlineData(.125, false)]
    [InlineData(.2, true)]
    public void RepeatCandidatesFollowMixedRhythmsUsingTheConfiguredStrikeInterval(double preferredInterval, bool alternateEveryNote)
    {
        var chart = SimaiParser.ParseChart("(120){#0.08}1,1{#0.16},1{#0.08},1{#0.16},1{#0.08},1,E", first: 1);
        var segment = Assert.Single(PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart)));
        Assert.Equal(PatternKind.Jack, segment.Kind);
        var candidates = TechniqueCandidates.For(segment, preferredInterval);
        foreach (var first in new[] { HandSide.Left, HandSide.Right })
        {
            var other = first == HandSide.Left ? HandSide.Right : HandSide.Left;
            HandSide?[] expected = alternateEveryNote
                ? [first, other, first, other, first, other]
                : [first, other, other, first, first, other];
            Assert.Contains(candidates, c => c.Hands.SequenceEqual(expected));
        }
    }

    [Fact]
    public void SingleTapKeepsOrdinaryStrikesAlongsideContinuationOnlySweeps()
    {
        var chart = SimaiParser.ParseChart("(120)4,E", first: 1);
        var segment = Assert.Single(PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart)));
        var candidates = TechniqueCandidates.For(segment);
        Assert.Contains(candidates, c => c.SweepDirection == -1 && c.RequiresSweepContinuation);
        Assert.Contains(candidates, c => c.SweepDirection == 1 && c.RequiresSweepContinuation);
        Assert.Contains(candidates, c => c.SweepDirection == 0 && !c.RequiresSweepContinuation && c.PreferenceCost == 0);
    }

    [Fact]
    public void SweepOffersBothHandsAndDirectionFollowsRingWrap()
    {
        var chart = SimaiParser.ParseChart("(120){#0.075}2,1,8,7,E", first: 1);
        var segment = Assert.Single(PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart)));
        Assert.Equal(-1, segment.Direction);
        var sweeps = TechniqueCandidates.For(segment).Where(c => c.SweepDirection == -1).ToArray();
        Assert.Contains(sweeps, c => c.Hands.All(h => h == HandSide.Left));
        Assert.Contains(sweeps, c => c.Hands.All(h => h == HandSide.Right));
        Assert.Contains(sweeps, c => c.Hands.Distinct().Count() == 2);
    }

    [Theory]
    [InlineData("1/5,2/6,3/7,4/8", 1, 1)]
    [InlineData("8/4,7/3,6/2,5/1", -1, -1)]
    [InlineData("1/8,2/7,3/6,4/5", 1, -1)]
    [InlineData("4/5,3/6,2/7,1/8", -1, 1)]
    [InlineData("1/5,6/2,3/7,8/4", 1, 1)]
    [InlineData("1/5,,3/7,,5/1", 1, 1)]
    public void PairedSweepsKeepTheirLanesAcrossNoteOrderAndSkippedButtons(string notes, int firstDirection, int secondDirection)
    {
        var chart = SimaiParser.ParseChart("(120){#0.075}" + notes + ",E", first: 1);
        var segment = Assert.Single(PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart)));
        Assert.Equal(PatternKind.ChordStream, segment.Kind);
        Assert.Equal(firstDirection, segment.Direction);
        Assert.Equal(secondDirection, segment.SecondDirection);
        var candidates = TechniqueCandidates.For(segment).Where(c => c.SweepDirections is not null).ToArray();
        Assert.Equal(2, candidates.Length);
        foreach (var candidate in candidates)
        {
            for (var i = 0; i < candidate.Hands.Count; i += 2)
            {
                Assert.NotEqual(candidate.Hands[i], candidate.Hands[i + 1]);
                Assert.Equal(candidate.Hands[0], candidate.Hands[i]);
                Assert.Equal(candidate.Hands[1], candidate.Hands[i + 1]);
                Assert.Equal(firstDirection, candidate.SweepDirections![i]);
                Assert.Equal(secondDirection, candidate.SweepDirections![i + 1]);
            }
        }
        Assert.Contains(TechniqueCandidates.For(segment), c => c.Hands.All(h => h is null));
    }

    [Fact]
    public void OpposingPairedSweepsStopBeforeTheHandsPassThroughEachOther()
    {
        var chart = SimaiParser.ParseChart("(120){#0.075}1/4,2/3,3/2,4/1,E", first: 1);
        var segments = PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart));
        Assert.DoesNotContain(segments, s => s.Kind == PatternKind.ChordStream);
        Assert.Equal(chart.Notes.Count, segments.Sum(s => s.Demands.Count));
    }

    [Fact]
    public void TouchChordOffersSpatialOrdersBeforeStartingItsSustainedContact()
    {
        var chart = SimaiParser.ParseChart("(120)Ch[#1]/A1/A3/A5/A7,E", first: 1);
        var segment = Assert.Single(PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart)));
        var candidates = TechniqueCandidates.For(segment).Where(c => c.DemandOrder is not null).ToArray();
        Assert.All(candidates, c =>
        {
            Assert.Equal(Enumerable.Range(0, segment.Demands.Count), c.DemandOrder!.Order());
            Assert.Equal(NoteKind.TouchHold, segment.Demands[c.DemandOrder![^1]].Note.Kind);
            Assert.All(c.Hands, hand => Assert.Null(hand));
        });
        var orders = candidates.Select(c => c.DemandOrder!.SkipLast(1)
            .Select(i => SlidePath.TouchPosition(segment.Demands[i].Target).X).ToArray()).ToArray();
        Assert.Contains(orders, xs => xs.SequenceEqual(xs.Order()));
        Assert.Contains(orders, xs => xs.SequenceEqual(xs.OrderDescending()));
    }

    [Fact]
    public void CompilerHonorsCancellationEvenForEmptyCharts()
    {
        var chart = SimaiParser.ParseChart("(120)E");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => GameplayDemandCompiler.Compile(chart, cancellation.Token));
    }
}
