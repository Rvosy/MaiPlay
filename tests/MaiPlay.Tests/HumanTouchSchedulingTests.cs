using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanTouchSchedulingTests
{
    [Fact]
    public void TouchHoldReleaseTimingLeavesRoomForTheFollowingDenseTouchChord()
    {
        var chart = SimaiParser.ParseChart("(110){#1.090909090909}Cfh[#1.090909090909]/B3/B4/B5/B6,B2/A2/B3/A3/D3/B6/A6/B7/A7/D7,E",
            first: 2.181818181818);
        var plan = HumanPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);

        var session = new PlaybackSession(chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = new List<PlaybackEvent>();
        for (var time = 0d; time < session.FinishTime; time += 1d / 29)
            results.AddRange(session.Advance(time).Where(e => e.IsFinal));
        results.AddRange(session.Advance(session.FinishTime + .1).Where(e => e.IsFinal));
        Assert.Equal(chart.Notes.Count, results.Count);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(plan.Achievement, session.Achievement.Percentage);
    }
}
