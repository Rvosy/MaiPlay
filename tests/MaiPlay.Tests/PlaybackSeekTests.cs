using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class PlaybackSeekTests
{
    [Theory]
    [InlineData(PlayMode.Algorithm)]
    [InlineData(PlayMode.AutoPlay)]
    public void RepeatedForwardAndBackwardSeeksMatchFreshPlayback(PlayMode mode)
    {
        var chart = SimaiParser.ParseChart("(120){4}7h[#2]/Ch[#1.5],2,3,2,8-4[4:1],,,1,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        var actual = new PlaybackSession(chart, mode, plan.Replay.Events);
        foreach (var time in new[] { 2.2, 5.5, 1.1, -.5, 3.7, 1.6, 6 })
        {
            var expected = new PlaybackSession(chart, mode, plan.Replay.Events);
            expected.Advance(time);
            actual.Seek(time);
            Assert.Equal(expected.DXScore, actual.DXScore);
            Assert.Equal(expected.Achievement.Percentage, actual.Achievement.Percentage);
            Assert.Equal(expected.GradeCounts, actual.GradeCounts);
            Assert.Equal(expected.InputHistory, actual.InputHistory);
            foreach (var note in chart.Notes)
            {
                Assert.Equal(expected.GetState(note), actual.GetState(note));
                Assert.Equal(expected.GetResult(note), actual.GetResult(note));
                Assert.Equal(expected.IsHolding(note), actual.IsHolding(note));
            }
        }
        actual.Advance(actual.FinishTime + .1);
        var final = new PlaybackSession(chart, mode, plan.Replay.Events);
        final.Advance(final.FinishTime + .1);
        Assert.Equal(final.DXScore, actual.DXScore);
        Assert.Equal(final.GradeCounts, actual.GradeCounts);
    }
}
