using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class NormalSweepChordHoldTests
{
    [Theory]
    [InlineData("1h[#0.2]/5")]
    [InlineData("5/1h[#0.2]")]
    public void SweepContinuesIntoTheHoldInAChord(string tail)
    {
        var chart = SimaiParser.ParseChart($"(200){{32}}4,5,6,7,8,{tail},E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => e.Note.Source + ": " + e.Grade)));
        Assert.Empty(plan.Gaps);
        var hold = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Hold);
        Assert.Equal(HandTechnique.Rotation, hold.Technique);
        var other = Assert.Single(plan.Strokes, s => s.NoteId != hold.NoteId && chart.Notes[s.NoteId].Time == chart.Notes[hold.NoteId].Time);
        Assert.NotEqual(hold.Hand, other.Hand);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }
}
