using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class TouchClickFeedbackTests
{
    public static IEnumerable<object[]> Cases => Enumerable.Range(0, 8)
        .SelectMany(rotation => new[] { 0d, 1d / 480, .00548780479547 }.SelectMany(phase => new[] { false, true }
            .Select(hold => new object[] { rotation, phase, hold })));

    [Theory]
    [MemberData(nameof(Cases))]
    public void TouchKeepsItsClickBeforeAFollowingExHeadEntersTheEarlyWindow(int rotation, double phase, bool hold)
    {
        int Position(int button) => (button + rotation - 1) % 8 + 1;
        var following = hold ? $"{Position(7)}hx[#0.15]" : $"{Position(7)}x";
        var chart = SimaiParser.ParseChart(
            $"(120){{#0.05}}{Position(3)}/{Position(4)},{{#0.259146341463}}A{Position(7)},{following},E",
            first: .95 + phase);

        var plan = HumanPlanner.Plan(chart);

        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e =>
            $"{e.Note.Time:F6} {e.Note.Source}: {e.Grade}/{e.DifferenceSeconds}")));
        Assert.Empty(plan.Gaps);
        var touch = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Touch);
        var pose = Assert.IsType<HandPose>(plan.Replay.PoseAt(touch.Hand, touch.Start + .005));
        Assert.Contains(pose.Contacts, c => c.Id == "palm");
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }

    [Fact]
    public void CoincidentButtonClickDoesNotAttributeAMissedTouchToTheExHead()
    {
        var chart = SimaiParser.ParseChart("(120){#0.01}A7,{#0.259146341463}A7,7x,E", first: .99);
        var touch = chart.Notes[1];
        var start = touch.Time + .125;
        var end = start + .025;
        var touchPosition = ContactTrajectoryCompiler.TouchCentre("A7");
        var buttonPosition = SlidePath.ButtonPosition(7, 1.12);
        var tracks = new HandTrack[]
        {
            new(HandSide.Left, ContactTrajectoryCompiler.ContactFrames(
                [new(start, touchPosition), new(end, touchPosition)], InputChannel.Touch, 0)),
            new(HandSide.Right, ContactTrajectoryCompiler.ContactFrames(
                [new(start, buttonPosition), new(end, buttonPosition)], InputChannel.Button, 0))
        };
        var replay = new HandReplay(tracks);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(results, e => e.Note.Id == 0).Grade);
        Assert.Equal(AchievementGrade.Miss, Assert.Single(results, e => e.Note.Id == touch.Id).Grade);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(results, e => e.Note.Id == 2).Grade);
        var plan = new HandPlan(chart, tracks, replay,
            [new(touch.Id, HandSide.Left, start, end, InputChannel.Touch),
                new(2, HandSide.Right, start, end, InputChannel.Button)],
            results, session.Achievement.Percentage, session.DXScore, []);

        Assert.DoesNotContain(touch.Id, PlannerTimingFeedback.EarlierContacts(plan).Keys);
    }
}
