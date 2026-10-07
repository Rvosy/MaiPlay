using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class WifiContactFeedbackTests
{
    public static IEnumerable<object[]> Cases => Enumerable.Range(1, 8)
        .SelectMany(start => new[] { false, true }.SelectMany(human => new[] { 1.829166666667, 1.85 }
            .Select(time => new object[] { start, human, time })));

    [Theory]
    [MemberData(nameof(Cases))]
    public void WifiPrefersPalmTimingWhenItCanAvoidEarlyTailContact(int start, bool human, double time)
    {
        var end = (start + 3) % 8 + 1;
        var button = (start + 2) % 8 + 1;
        var basis = SimaiParser.ParseChart($"(120){{#2}}{start}w{end}[0.5##0.3],C,E", first: 1);
        var original = NormalHandPlanner.Plan(basis);
        Assert.True(original.AllCriticalPerfect);
        var wifi = basis.Notes.Single(n => n.Kind == NoteKind.Slide);
        var defaultPair = original.Strokes.Where(s => s.NoteId == wifi.Id).ToArray();
        Assert.Equal(2, defaultPair.Length);
        Assert.All(defaultPair, s => Assert.Contains(ContactPose(original, s).Contacts, c => c.Id == "palm"));

        var next = SimaiParser.ParseChart($"(120){button},E", first: time).Notes[0] with { Id = basis.Notes.Count };
        var chart = basis with { Notes = basis.Notes.Append(next).ToArray() };
        var unadjustedInputs = original.Replay.Events.Concat(new[]
        {
            new InputEvent(next.Time, InputChannel.Button, button.ToString(), true, "next-tap"),
            new InputEvent(next.Time + .025, InputChannel.Button, button.ToString(), false, "next-tap")
        }).OrderBy(e => e.Time).ToArray();
        var unadjusted = new PlaybackSession(chart, PlayMode.Algorithm, unadjustedInputs);
        var early = unadjusted.Advance(unadjusted.FinishTime + .1).Single(e => e.IsFinal && e.Note.Id == next.Id);
        Assert.NotEqual(AchievementGrade.Perfect, early.Grade);
        Assert.True(early.DifferenceSeconds < 0);

        var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}/{e.Reason}")));
        Assert.Empty(plan.Gaps);
        var pair = plan.Strokes.Where(s => s.NoteId == wifi.Id).ToArray();
        Assert.Equal(2, pair.Length);
        Assert.All(pair, s => Assert.Contains(ContactPose(plan, s).Contacts, c => c.Id == "palm"));
        var unrelated = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Touch);
        Assert.Equal("palm", Assert.Single(ContactPose(plan, unrelated).Contacts).Id);

        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }

    private static HandPose ContactPose(HandPlan plan, PlannedStroke stroke) =>
        Assert.IsType<HandPose>(plan.Replay.PoseAt(stroke.Hand, stroke.Start + .005));
}
