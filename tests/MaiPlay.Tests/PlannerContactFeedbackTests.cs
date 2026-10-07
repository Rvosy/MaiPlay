using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class PlannerContactFeedbackTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["(100){8}1-4[0.6##0.15],8-5[0.6##0.15],1-5[0.6##0.15],8-4[0.6##0.15],1-6[0.6##0.15],E"];
        yield return ["(220){#1.636363636364}4>5[0.272727272727##2.727272727273]<4[#2.727272727273],E1/D1,E"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NormalRepairsTheContactThatTriggeredOrMaskedAnotherNote(string source) =>
        Verify(NormalHandPlanner.Plan(SimaiParser.ParseChart(source, first: 1)));

    [Theory]
    [MemberData(nameof(Cases))]
    public void HumanRepairsTheContactThatTriggeredOrMaskedAnotherNote(string source) =>
        Verify(HumanPlanner.Plan(SimaiParser.ParseChart(source, first: 1)));

    private static void Verify(HandPlan plan)
    {
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var session = new PlaybackSession(plan.Chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(plan.Chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(plan.DXScore, session.DXScore);
    }
}
