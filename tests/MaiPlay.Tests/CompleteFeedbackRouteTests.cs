using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class CompleteFeedbackRouteTests
{
    [Theory]
    [InlineData(1.21)]
    [InlineData(1.2125)]
    public void KeepsTheCompleteRouteThatRecoversChordsAfterBranchingSlides(double first)
    {
        var chart = SimaiParser.ParseChart(
            "(190){4}4$-1[8:1]*-2[8:1]/8,4$-6[8:1]*-7[8:1],4$-8[8:1]*w8[8:1],," +
            "2b/6b$-1[8:1],3$-8[8:1]/6,3/7$-4[8:1],1$-4[8:1]/7,1/5$-7[8:1]*-8[8:1]," +
            "5$-2[8:1]*-3[8:1],5$-1[8:1]*w1[8:1],,3b$-6[8:1]/7b,3/7$-2[8:1],3$-6[8:1]/7," +
            "1$-6[8:1]/3,1/5$>2[8:1],{8}6,{4}7$-3[8:1],{8}2,{4}1$-5[8:1],1,E", first: first);

        var plan = HumanPlanner.Plan(chart);

        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e =>
            $"{e.Note.Time:F6} {e.Note.Source}: {e.Grade}/{e.DifferenceSeconds}")));
        Assert.Empty(plan.Gaps);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }
}
