using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class SplitHoldSweepTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void SplitsFromOneHeadAndKeepsOneHandOnTheHold(bool human, string source)
    {
        var chart = SimaiParser.ParseChart(source, first: 1);
        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        var hold = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Hold);
        var star = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Tap && chart.Notes[s.NoteId].IsStar);
        Assert.NotEqual(hold.Hand, star.Hand);
        foreach (var hand in new[] { HandSide.Left, HandSide.Right })
            for (var i = 0; i <= 20; i++)
            {
                var time = 1.03125 + (1.09375 - 1.03125) * i / 20;
                var pose = plan.Replay.PoseAt(hand, time)!;
                Assert.True(pose.Position.DistanceTo(default) >= 1.1, $"Sweep left the outer ring at {time}.");
                Assert.Contains(pose.Contacts, c => c.Channel == InputChannel.Button);
            }
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var human in new[] { false, true })
            foreach (var direction in new[] { -1, 1 })
                for (var first = 1; first <= 8; first++)
                    foreach (var reverseChord in new[] { false, true })
                    {
                        int Key(int offset) => (first - 1 + offset * direction + 16) % 8 + 1;
                        string Pair(string left, string right) => reverseChord ? $"{right}/{left}" : $"{left}/{right}";
                        var firstPair = Pair(Key(-1).ToString(), Key(1).ToString());
                        var secondPair = Pair(Key(2).ToString(), Key(-2).ToString());
                        var lastPair = Pair($"{Key(3)}h[#3.40625]", Key(-3).ToString());
                        var shape = direction == 1 ? "<" : ">";
                        var back = direction == 1 ? ">" : "<";
                        yield return [human, $"(120){{64}}{first}b,{firstPair},{secondPair},{lastPair}," +
                            $"{Key(4)}${shape}{Key(2)}[0.5##3.375]{back}{Key(4)}[#2.5],E"];
                    }
    }
}
