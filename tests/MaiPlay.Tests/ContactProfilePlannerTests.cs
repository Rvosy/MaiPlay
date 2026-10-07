using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class ContactProfilePlannerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryTouchesKeepPalmContact(bool human)
    {
        var chart = SimaiParser.ParseChart("(120)C,E", first: 1);
        var plan = Plan(chart, human);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        var pose = ContactPose(plan, Assert.Single(plan.Strokes));
        var contact = Assert.Single(pose.Contacts);
        Assert.Equal("palm", contact.Id);
        Assert.InRange(contact.Radius * 2 * CabinetGeometry.Approximate43Inch.JudgementRadiusMillimeters, 89, 90);
        VerifyReplay(plan);
    }

    [Theory]
    [InlineData(false, "E2h[#0.025]", "1")]
    [InlineData(true, "E2", "1")]
    [InlineData(false, "E2h[#0.025]", "1h[#0.5]")]
    [InlineData(true, "E2", "1h[#0.5]")]
    public void TouchContactAvoidsEarlyHitsAndUnrelatedTouchKeepsPalm(bool human, string touch, string next)
    {
        var chart = SimaiParser.ParseChart($"(120){{#0.1}}{touch},{next},{{#1}}C,E", first: 1);
        var plan = Plan(chart, human);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Empty(plan.Gaps);
        var hit = Assert.Single(plan.Results, e => e.IsFinal && e.Note.Source == next);
        Assert.Equal(AchievementGrade.Perfect, hit.Grade);
        var palm = ContactPose(plan, Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].TouchArea == "C"));
        Assert.Equal("palm", Assert.Single(palm.Contacts).Id);
        VerifyReplay(plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdjacentButtonChordStillUsesTwoHandsAndTheOriginalFingerWidth(bool human)
    {
        var chart = SimaiParser.ParseChart("(120)1/2,E", first: 1);
        var plan = Plan(chart, human);
        Assert.True(plan.AllCriticalPerfect, Explain(plan));
        Assert.Equal(2, plan.Strokes.Select(s => s.Hand).Distinct().Count());
        foreach (var stroke in plan.Strokes)
        {
            var pose = ContactPose(plan, stroke);
            Assert.Equal(4, pose.Contacts.Count);
            Assert.All(pose.Contacts, c =>
            {
                Assert.Equal(InputChannel.Button, c.Channel);
                Assert.Equal(.035, c.Radius);
            });
            var covered = pose.Contacts.SelectMany(c => ContactMapper.AreasAt(c.Channel,
                new(pose.Position.X + c.Offset.X, pose.Position.Y + c.Offset.Y), c.Radius)).Distinct();
            Assert.Equal(chart.Notes[stroke.NoteId].StartPosition.ToString(), Assert.Single(covered));
        }
        VerifyReplay(plan);
    }

    private static HandPlan Plan(Chart chart, bool human) => human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);

    private static HandPose ContactPose(HandPlan plan, PlannedStroke stroke) =>
        Assert.IsType<HandPose>(plan.Replay.PoseAt(stroke.Hand, stroke.Start + .005));

    private static string Explain(HandPlan plan) => string.Join("; ", plan.Errors.Select(e =>
        $"{e.Note.Time:F4} {e.Note.Source}: {e.Grade}"));

    private static void VerifyReplay(HandPlan plan)
    {
        var replay = new HandReplay(plan.Tracks);
        var session = new PlaybackSession(plan.Chart, PlayMode.Algorithm, replay.Events);
        var results = new List<PlaybackEvent>();
        for (var time = -1d; time < session.FinishTime; time += 1d / 29)
            results.AddRange(session.Advance(time).Where(e => e.IsFinal));
        results.AddRange(session.Advance(session.FinishTime + .1).Where(e => e.IsFinal));
        Assert.Equal(plan.Results.OrderBy(e => e.Note.Id), results.OrderBy(e => e.Note.Id));
        Assert.All(results, result => Assert.Equal(AchievementGrade.Perfect, result.Grade));
        Assert.Equal(plan.Achievement, session.Achievement.Percentage);
        Assert.Equal(plan.DXScore, session.DXScore);
    }
}
