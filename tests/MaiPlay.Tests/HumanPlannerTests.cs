using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanPlannerTests
{
    [Fact]
    public void MixedWindowsKeepTheirOwnTechniqueAndReplayAsOneChart()
    {
        var chart = SimaiParser.ParseChart("(120){#0.075}1,2,3,4,,,,,5/8,,,,,A2,E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Contains(plan.Strokes, s => s.Technique == HandTechnique.Rotation);
        Assert.Contains(plan.Strokes, s => s.Technique == HandTechnique.Strike);
        var chordIds = chart.Notes.Where(n => n.Source is "5" or "8").Select(n => n.Id).ToHashSet();
        Assert.Equal(2, plan.Strokes.Where(s => chordIds.Contains(s.NoteId)).Select(s => s.Hand).Distinct().Count());
        VerifyReplay(plan);
    }

    [Fact]
    public void InfeasibleChordLeavesAGapAndStillPlansTheRemainingMusic()
    {
        var chart = SimaiParser.ParseChart("(120){4}1/2/3,8,7,6,E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        var gap = Assert.Single(plan.Gaps);
        Assert.Contains(chart.Notes.Where(n => n.Time == 1), n => n.Id == gap.NoteId);
        Assert.Equal(5, plan.Results.Count(e => e.Grade == AchievementGrade.Perfect));
        Assert.Single(plan.Errors);
        Assert.All(plan.Results.Where(e => e.Note.Time > 1), e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        VerifyReplay(plan);
    }

    [Fact]
    public void SustainedContactOccupiesItsHandAcrossPatternWindows()
    {
        var chart = SimaiParser.ParseChart("(120){#0.075}Ch[#1]/1,2,3,4,5,6,7,8,1,2,3,4,E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        var hold = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.TouchHold);
        Assert.All(plan.Strokes.Where(s => chart.Notes[s.NoteId].Kind == NoteKind.Tap), s => Assert.NotEqual(hold.Hand, s.Hand));
        VerifyReplay(plan);
    }

    [Fact]
    public void SlideCanUseTheOtherHandAfterItsStarHead()
    {
        var chart = SimaiParser.ParseChart("(120){#1}8-4[1##1]/2h[#0.8],8h[#1],E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        var head = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].IsStar);
        var slide = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Slide);
        Assert.NotEqual(head.Hand, slide.Hand);
        Assert.Equal(HandTechnique.Slide, slide.Technique);
        VerifyReplay(plan);
    }

    [Theory]
    [InlineData("A1/A3/A5")]
    [InlineData("A1/A3/A5/A7")]
    public void MoreThanTwoSimultaneousTouchesCanUseTheirTimingWindow(string touches)
    {
        var chart = SimaiParser.ParseChart($"(120){touches},E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Empty(plan.Gaps);
        VerifyReplay(plan);
    }

    [Theory]
    [InlineData("1/8,2/7,3/6,4/5")]
    [InlineData("1/5,2/6,3/7,4/8,5/1,6/2,7/3,8/4")]
    public void PairedSweepsCompleteBothLanesThroughActualContact(string notes)
    {
        var chart = SimaiParser.ParseChart("(120){#0.075}" + notes + ",E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Empty(plan.Gaps);
        Assert.All(plan.Strokes, s => Assert.Equal(HandTechnique.Rotation, s.Technique));
        foreach (var pair in plan.Strokes.GroupBy(s => chart.Notes[s.NoteId].Time))
            Assert.Equal(2, pair.Select(s => s.Hand).Distinct().Count());
        VerifyReplay(plan);
    }

    [Fact]
    public void AdjacentChordUsesTwoSeparateButtonContacts()
    {
        var plan = HumanPlanner.Plan(SimaiParser.ParseChart("(120){4}12,78,E", first: 1));
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        foreach (var chord in plan.Strokes.GroupBy(s => plan.Chart.Notes[s.NoteId].Time))
            Assert.Equal(2, chord.Select(s => s.Hand).Distinct().Count());
        foreach (var frame in plan.Tracks.SelectMany(t => t.Frames).Where(f => f.Contacts.Count > 0))
            Assert.Single(frame.Contacts.SelectMany(c => ContactMapper.AreasAt(c.Channel,
                new(frame.Position.X + c.Offset.X, frame.Position.Y + c.Offset.Y), c.Radius)).Distinct());
    }

    [Theory]
    [InlineData("1", HandSide.Right)]
    [InlineData("8", HandSide.Left)]
    public void EquallyAccurateSingleStrikesPreferTheNearbyHand(string note, HandSide nearbyHand)
    {
        var chart = SimaiParser.ParseChart($"(120){note},E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Equal(nearbyHand, Assert.Single(plan.Strokes).Hand);
        Assert.NotNull(plan.Diagnostics);
        Assert.True(double.IsFinite(plan.Diagnostics.MovementCost));
        Assert.Equal(0, plan.Diagnostics.Movement.ProjectedCrossingCount);
    }

    [Fact]
    public void PlanningIsCancellableAndDeterministic()
    {
        var chart = SimaiParser.ParseChart("(120){#0.075}1,2,3,4,,,,,78,E", first: 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => HumanPlanner.Plan(chart, cancellation.Token));
        var first = HumanPlanner.Plan(chart);
        var second = HumanPlanner.Plan(chart);
        Assert.Equal(first.Strokes, second.Strokes);
        Assert.Equal(first.Replay.Events, second.Replay.Events);
    }

    [Fact]
    public void SlideThatExceedsTheMotionLimitRemainsAGapEvenWhenBaselineCanJudgeIt()
    {
        var chart = SimaiParser.ParseChart("(120)1s5[0.5##0.02],E", first: 1);
        Assert.True(NormalHandPlanner.Plan(chart).AllCriticalPerfect);
        var model = new HumanMotionModel(maxSpeedMillimetersPerSecond: 1000, preferredSpeedMillimetersPerSecond: 800);
        var plan = HumanPlanner.Plan(chart, new HumanPlannerOptions { MotionModel = model });
        var slide = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Slide);
        Assert.Contains(plan.Gaps, g => g.NoteId == slide.Id);
        Assert.Contains(plan.Errors, e => e.Note.Id == slide.Id);
        Assert.Equal(0, model.Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        VerifyReplay(plan);
    }

    [Fact]
    public void SweepSpeedLimitUsesTheTravelledArcRatherThanItsChord()
    {
        const double interval = .075;
        var chart = SimaiParser.ParseChart("(120){#0.075}Ch[#1]/1,2,3,4,5,6,7,8,1,2,3,4,E", first: 1);
        Assert.True(HumanPlanner.Plan(chart).AllCriticalPerfect);
        var radius = 1.12 * CabinetGeometry.Approximate43Inch.JudgementRadiusMillimeters;
        var chordSpeed = 2 * radius * Math.Sin(Math.PI / 8) / interval;
        var arcSpeed = radius * Math.PI / 4 / interval;
        var limit = (chordSpeed + arcSpeed) / 2;
        var model = new HumanMotionModel(maxSpeedMillimetersPerSecond: limit);
        var plan = HumanPlanner.Plan(chart, new HumanPlannerOptions { MotionModel = model });
        Assert.False(plan.AllCriticalPerfect);
        Assert.NotEmpty(plan.Gaps);
        Assert.Equal(0, model.Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        VerifyReplay(plan);
    }

    [Fact]
    public void SlowMotionLimitKeepsLaterReachableNotesAfterEarlierGaps()
    {
        var chart = SimaiParser.ParseChart("(120){#0.05}1,2,3,4,E", first: 1);
        var final = chart.Notes[^1] with { Id = 4, Time = 4, StartPosition = 5, EndPosition = 5, Source = "5" };
        chart = chart with { Notes = chart.Notes.Append(final).ToArray(), Duration = 5 };
        var model = new HumanMotionModel(maxSpeedMillimetersPerSecond: 500, preferredSpeedMillimetersPerSecond: 400);
        var plan = HumanPlanner.Plan(chart, new HumanPlannerOptions { MotionModel = model });
        Assert.NotEmpty(plan.Gaps);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(plan.Results, e => e.Note.Id == final.Id).Grade);
        Assert.Equal(chart.Notes.Count, plan.Results.Count);
        Assert.Equal(0, model.Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        VerifyReplay(plan);
    }

    [Fact]
    public void DelayedSlideTailAvoidsJudgingAnUpcomingTapEarly()
    {
        var chart = SimaiParser.ParseChart("(135){4}1/8<5[4:1],,4>1[4:1]/5,,8h[2:1],E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Empty(plan.Gaps);
        var interferingSlide = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Slide && n.StartPosition == 8);
        var delayed = Assert.Single(plan.Strokes, s => s.NoteId == interferingSlide.Id);
        Assert.True(delayed.Start > interferingSlide.SlideStartTime);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        VerifyReplay(plan);
    }

    [Fact]
    public void HoldHeadInterferenceIsRepairedAtTheHeadTimeRatherThanAtRelease()
    {
        var chart = SimaiParser.ParseChart("(135){4}1/8<5[4:1],,4>1[4:1]/5h[#0.3],,8h[2:1],E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Empty(plan.Gaps);
        var hold = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Hold && n.StartPosition == 5);
        var judgement = Assert.Single(plan.Results, e => e.Note.Id == hold.Id);
        Assert.Equal(AchievementGrade.Perfect, judgement.Grade);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        VerifyReplay(plan);
    }

    [Fact]
    public void TimingFeedbackKeepsTheSlidesWhoseDelayedContactsCompleteTheUpcomingTaps()
    {
        var chart = SimaiParser.ParseChart("(159){#0.471698113208}2-6[0.377358490566##1.509433962264]/3-7[0.377358490566##1.509433962264],23,E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Equal(6, plan.Results.Count);
        Assert.Empty(plan.Gaps);
        Assert.Equal(2, plan.Strokes.Count(s => s.Technique == HandTechnique.Slide));
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        VerifyReplay(plan);
    }

    [Fact]
    public void ShortParallelSlidesCanSlowDownWithinTheirActualJudgementWindows()
    {
        var chart = SimaiParser.ParseChart("(135)1>4[0.5##0.111111]/8<5[0.5##0.111111],E", first: 1);
        var baseline = NormalHandPlanner.Plan(chart);
        var model = new HumanMotionModel(maxSpeedMillimetersPerSecond: 4500);
        Assert.True(baseline.AllCriticalPerfect);
        Assert.True(model.Analyze(baseline.Tracks, baseline.Strokes).PeakSpeedMillimetersPerSecond > model.MaxSpeedMillimetersPerSecond);
        var plan = HumanPlanner.Plan(chart, new HumanPlannerOptions { MotionModel = model });
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Empty(plan.Gaps);
        foreach (var slide in plan.Strokes.Where(s => s.Technique == HandTechnique.Slide))
        {
            var before = Assert.Single(baseline.Strokes, s => s.NoteId == slide.NoteId);
            Assert.True(slide.End - slide.Start > before.End - before.Start);
        }
        var movement = model.Analyze(plan.Tracks, plan.Strokes);
        Assert.Equal(0, movement.SpeedLimitViolations);
        Assert.True(movement.PeakSpeedMillimetersPerSecond <= model.MaxSpeedMillimetersPerSecond);
        VerifyReplay(plan);
    }

    private static string Explain(HandPlan plan) => string.Join("; ", plan.Errors.Select(e => $"{e.Note.Time:F4} {e.Note.Source}: {e.Grade}"));

    private static void VerifyReplay(HandPlan plan)
    {
        var session = new PlaybackSession(plan.Chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = new List<PlaybackEvent>();
        for (var time = -1d; time < session.FinishTime; time += 1d / 29)
            results.AddRange(session.Advance(time).Where(e => e.IsFinal));
        results.AddRange(session.Advance(session.FinishTime + .1).Where(e => e.IsFinal));
        Assert.Equal(plan.Results.OrderBy(e => e.Note.Id), results.OrderBy(e => e.Note.Id));
        Assert.Equal(plan.Achievement, session.Achievement.Percentage);
        Assert.Equal(plan.DXScore, session.DXScore);
    }
}
