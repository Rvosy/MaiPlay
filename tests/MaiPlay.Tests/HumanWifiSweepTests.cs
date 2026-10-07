using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanWifiSweepTests
{
    [Theory]
    [InlineData(110, 1, -1, false)]
    [InlineData(100, 5, 1, false)]
    [InlineData(120, 3, -1, false)]
    [InlineData(110, 1, -1, true)]
    [InlineData(100, 5, 1, true)]
    [InlineData(120, 3, -1, true)]
    public void SweepFinishesBeforeHandsLeaveForWifi(int bpm, int start, int direction, bool human)
    {
        int Key(int offset) => (start - 1 + direction * offset + 64) % 8 + 1;
        var taps = Enumerable.Range(1, 7).Select(i => Key(i).ToString());
        var text = $"({bpm}){{32}}{start}w{Key(4)}[2:1],{string.Join(',', taps)},{start}b,E";
        var chart = SimaiParser.ParseChart(text, first: 1);
        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}:{e.Grade}")));
        Assert.Empty(plan.Gaps);
        var tail = chart.Notes.Where(n => n.Kind == NoteKind.Tap).TakeLast(2).ToArray();
        Assert.Contains(plan.Strokes, s => s.NoteId == tail[0].Id && s.Technique == HandTechnique.Rotation);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }
}
