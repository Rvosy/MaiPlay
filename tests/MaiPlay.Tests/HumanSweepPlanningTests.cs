using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanSweepPlanningTests
{
    public static IEnumerable<object[]> SweepCases()
    {
        foreach (var bpm in new[] { 180, 200, 240 })
            foreach (var direction in new[] { -1, 1 })
                foreach (var start in Enumerable.Range(1, 8))
                    yield return [bpm, direction, start];
    }

    public static IEnumerable<object[]> SweepExitCases()
    {
        foreach (var bpm in new[] { 180, 205, 240 })
            foreach (var direction in new[] { -1, 1 })
                foreach (var start in Enumerable.Range(1, 8))
                    yield return [bpm, direction, start];
    }

    [Theory]
    [MemberData(nameof(SweepCases))]
    public void FullCircleKeepsContinuousContactAcrossTemposAndOrientations(int bpm, int direction, int start)
    {
        var keys = Enumerable.Range(0, 9).Select(i => Key(start, direction, i));
        var chart = SimaiParser.ParseChart($"({bpm}){{32}}{string.Join(',', keys)},E", first: 1.003);
        Verify(HumanPlanner.Plan(chart));
    }

    [Theory]
    [MemberData(nameof(SweepCases))]
    public void SingleSweepCanContinueIntoTwoParallelSweeps(int bpm, int direction, int start)
    {
        var singles = Enumerable.Range(0, 4).Select(i => Key(start, direction, i).ToString());
        var pairs = Enumerable.Range(0, 5).Select(i => $"{Key(start, direction, i)}/{Key(start, direction, i + 4)}");
        var chart = SimaiParser.ParseChart($"({bpm}){{32}}{string.Join(',', singles.Concat(pairs))},E", first: 1.003);
        var plan = HumanPlanner.Plan(chart);
        Verify(plan);
        foreach (var pair in plan.Strokes.GroupBy(s => chart.Notes[s.NoteId].Time).Where(g => g.Count() == 2))
            Assert.Equal(2, pair.Select(s => s.Hand).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(SweepExitCases))]
    public void TwoSweepsCanConvergeIntoASingleFinishingKey(int bpm, int direction, int start)
    {
        var pairs = Enumerable.Range(0, 3).Select(i => $"{Key(start, direction, i)}/{Key(start, direction, 6 - i)}");
        var notes = pairs.Append(Key(start, direction, 3).ToString());
        var chart = SimaiParser.ParseChart($"({bpm}){{32}}{string.Join(',', notes)},E", first: 1.003);
        var plan = HumanPlanner.Plan(chart);
        Verify(plan);
        Assert.Equal(HandTechnique.Rotation, Assert.Single(plan.Strokes, s => s.NoteId == chart.Notes[^1].Id).Technique);
    }

    [Fact]
    public void AnIsolatedSingleKeyDoesNotStartASweep()
    {
        var plan = HumanPlanner.Plan(SimaiParser.ParseChart("(120)4,E", first: 1));
        Assert.True(plan.AllCriticalPerfect);
        Assert.Equal(HandTechnique.Strike, Assert.Single(plan.Strokes).Technique);
    }

    [Theory]
    [MemberData(nameof(SweepCases))]
    public void SweepFinishesItsLastKeyAsPartOfATwoHandChord(int bpm, int direction, int start)
    {
        var sweep = Enumerable.Range(0, 8).Select(i => Key(start, direction, i).ToString());
        var tail = $"{start}b/{Key(start, direction, 4)}b";
        var chart = SimaiParser.ParseChart($"({bpm}){{32}}{string.Join(',', sweep.Append(tail))},E", first: 1.003);
        var plan = HumanPlanner.Plan(chart);
        Verify(plan);
        var preceding = Assert.Single(plan.Strokes, s => s.NoteId == 7);
        var finishing = Assert.Single(plan.Strokes, s => s.NoteId == 8);
        var partner = Assert.Single(plan.Strokes, s => s.NoteId == 9);
        Assert.Equal(HandTechnique.Rotation, finishing.Technique);
        Assert.Equal(preceding.Hand, finishing.Hand);
        Assert.NotEqual(finishing.Hand, partner.Hand);
    }

    [Theory]
    [InlineData("1/5")]
    [InlineData("5/1")]
    [InlineData("1b/5b")]
    [InlineData("5b/1b")]
    [InlineData("1-5[4:1]/5")]
    [InlineData("5/1-5[4:1]")]
    [InlineData("1/5h[4:1]")]
    [InlineData("5h[4:1]/1")]
    [InlineData("1h[#0.2]/5")]
    [InlineData("5/1h[#0.2]")]
    public void SweepTailDoesNotDependOnChordNotationOrDemandOrder(string tail)
    {
        var chart = SimaiParser.ParseChart($"(200){{32}}4,5,6,7,8,{tail},E", first: 1.003);
        var plan = HumanPlanner.Plan(chart);
        Verify(plan);
        var last = chart.Notes.Single(n => n.Kind is NoteKind.Tap or NoteKind.Hold && n.StartPosition == 1);
        Assert.Equal(HandTechnique.Rotation, Assert.Single(plan.Strokes, s => s.NoteId == last.Id).Technique);
    }

    [Fact]
    public void SweepCanFinishAChordWhoseOtherKeyIsCoveredByAnExistingSlide()
    {
        var chart = SimaiParser.ParseChart("(200){#0.3975}1-5[0.5##1],{32}8,7,6,5/1,E", first: .49);
        var plan = HumanPlanner.Plan(chart);
        Verify(plan);
        var tail = chart.Notes.Where(n => n.Kind == NoteKind.Tap).OrderBy(n => n.Time).TakeLast(2).ToArray();
        Assert.Equal(HandTechnique.Rotation, Assert.Single(plan.Strokes, s => s.NoteId == tail[0].Id).Technique);
        Assert.DoesNotContain(plan.Strokes, s => s.NoteId == tail[1].Id);
    }

    private static int Key(int start, int direction, int offset) => (start - 1 + direction * offset + 64) % 8 + 1;

    private static void Verify(HandPlan plan)
    {
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade} ({e.DifferenceSeconds:F4})")));
        Assert.Empty(plan.Gaps);
        Assert.Contains(plan.Strokes, s => s.Technique == HandTechnique.Rotation);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var session = new PlaybackSession(plan.Chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(plan.Chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(plan.DXScore, session.DXScore);
    }
}
