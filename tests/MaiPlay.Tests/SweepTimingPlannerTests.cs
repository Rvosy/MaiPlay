using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class SweepTimingPlannerTests
{
    [Theory]
    [InlineData(false, "1b/8b,2b/7b,3b/6b,4b/5b")]
    [InlineData(true, "1b/8b,2b/7b,3b/6b,4b/5b")]
    [InlineData(false, "4b/5b,3b/6b,2b/7b,1b/8b")]
    [InlineData(true, "4b/5b,3b/6b,2b/7b,1b/8b")]
    public void FastDoubleSweepUsesTheJudgementWindowWithoutExceedingSpeedLimit(bool human, string notes)
    {
        var chart = SimaiParser.ParseChart("(200){#0.01875}" + notes + ",E", first: 1);
        var model = new HumanMotionModel();
        var plan = human ? HumanPlanner.Plan(chart, new HumanPlannerOptions { MotionModel = model }) : NormalHandPlanner.Plan(chart);

        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(8, plan.Strokes.Count);
        Assert.Equal(0, model.Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        foreach (var pair in plan.Strokes.GroupBy(s => chart.Notes[s.NoteId].Time))
            Assert.Equal(2, pair.Select(s => s.Hand).Distinct().Count());

        var replay = new HandReplay(plan.Tracks);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(8, results.Length);
        Assert.All(results, result => Assert.Equal(AchievementGrade.Perfect, result.Grade));
    }
}
