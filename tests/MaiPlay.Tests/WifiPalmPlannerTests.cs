using System.Globalization;
using MaiPlay.Core;
using static MaiPlay.Core.ContactTrajectoryCompiler;

namespace MaiPlay.Tests;

public sealed class WifiPalmPlannerTests
{
    public static IEnumerable<object[]> DirectionsAndDurations() =>
        from start in Enumerable.Range(1, 8)
        from duration in new[] { .075, .166666666667, .5, 1.5, 4d }
        select new object[] { start, duration };

    [Theory]
    [MemberData(nameof(DirectionsAndDurations))]
    public void SideBySidePalmsMoveStraightAndAdvanceAllThreeQueuesTogether(int start, double duration)
    {
        var chart = Parse(start, duration);
        var slide = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Slide);
        var motions = CreateMotions(chart, CancellationToken.None).Where(m => m.Note.Id == slide.Id).ToArray();
        Assert.Equal(2, motions.Length);
        var down = SlidePath.ButtonPosition(slide.EndPosition);
        var axis = new ChartPoint(down.Y, -down.X);
        var tracks = motions.Select((m, i) => new HandTrack((HandSide)i, ContactFrames(m.Points, m.Channel, 0, m.Profile))).ToArray();
        var separation = tracks[0].Frames[0].Position.DistanceTo(tracks[1].Frames[0].Position);
        var radius = CabinetGeometry.Approximate43Inch.JudgementRadiusMillimeters;
        Assert.InRange(separation * radius, 160, 200);
        foreach (var track in tracks)
        {
            var first = track.Frames[0];
            var contacts = first.Contacts;
            Assert.Contains(contacts, c => c.Id == "palm");
            var lateral = contacts.Select(c => c.Offset.X * axis.X + c.Offset.Y * axis.Y).ToArray();
            var length = (lateral.Max() - lateral.Min() + 2 * contacts[0].Radius) * radius;
            Assert.InRange(length, 160, 200);
            Assert.InRange(2 * contacts[0].Radius * radius, 85, 95);
            foreach (var frame in track.Frames.Where(f => f.Contacts.Count > 0))
            {
                var displacement = new ChartPoint(frame.Position.X - first.Position.X, frame.Position.Y - first.Position.Y);
                Assert.InRange(Math.Abs(displacement.X * axis.X + displacement.Y * axis.Y), 0, 1e-9);
                Assert.Equal(contacts, frame.Contacts);
            }
        }
        Assert.Equal(tracks[0].Frames.Select(f => f.Time), tracks[1].Frames.Select(f => f.Time));
        for (var i = 0; i < tracks[0].Frames.Count; i++)
            Assert.Equal(separation, tracks[0].Frames[i].Position.DistanceTo(tracks[1].Frames[i].Position), 9);

        var session = Replay(chart with { Notes = [slide with { Id = 0 }] }, tracks);
        foreach (var remaining in new[] { 3, 2, 0 })
        {
            var times = Enumerable.Range(0, 3).Select(branch => session.SlideTrace
                .Where(t => t.Branch == branch && t.RemainingAreas <= remaining).Min(t => t.Time)).ToArray();
            // 三路传感器边界不同，推进可错开少量行程，不能变成逐路完成。
            Assert.True(times.Max() - times.Min() <= (duration + .145) * .1 + PlaybackSession.LogicStep,
                $"{remaining}: {string.Join(", ", times)}");
        }
    }

    [Theory]
    [MemberData(nameof(DirectionsAndDurations))]
    public void BothPlannersCanJudgeThePalmSweep(int start, double duration)
    {
        var chart = Parse(start, duration);
        foreach (var human in new[] { false, true })
        {
            var plan = human ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
            Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{human}: {e.Note.Source} {e.Grade}")));
            Assert.Empty(plan.Gaps);
            var strokes = plan.Strokes.Where(s => s.Technique == HandTechnique.Slide).ToArray();
            Assert.Equal(2, strokes.Length);
            Assert.Equal(strokes[0].Start, strokes[1].Start);
            Assert.Equal(strokes[0].End, strokes[1].End);
            Assert.All(plan.Tracks.SelectMany(t => t.Frames).SelectMany(f => f.Contacts).Where(c => c.Channel == InputChannel.Touch),
                c => Assert.StartsWith("palm", c.Id));
            Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
            Replay(chart, plan.Tracks);
        }
    }

    [Theory]
    [MemberData(nameof(DirectionsAndDurations))]
    public void FeedbackSelectedFingertipsKeepACompleteNarrowRoute(int start, double duration)
    {
        var chart = Parse(start, duration);
        var slide = Assert.Single(chart.Notes, n => n.Kind == NoteKind.Slide);
        var motions = CreateMotions(chart, CancellationToken.None, new HashSet<int> { slide.Id })
            .Where(m => m.Note.Id == slide.Id).ToArray();
        var tracks = motions.Select((m, i) => new HandTrack((HandSide)i, ContactFrames(m.Points, m.Channel, 0, m.Profile))).ToArray();
        Assert.All(tracks.SelectMany(t => t.Frames).SelectMany(f => f.Contacts), c => Assert.StartsWith("finger", c.Id));
        Assert.Equal(0, new HumanMotionModel().Analyze(tracks).SpeedLimitViolations);
        Replay(chart with { Notes = [slide with { Id = 0 }] }, tracks);
    }

    private static Chart Parse(int start, double duration) => SimaiParser.ParseChart(
        $"(120){start}w{(start + 3) % 8 + 1}[0.5##{duration.ToString(CultureInfo.InvariantCulture)}],E", first: 1);

    private static PlaybackSession Replay(Chart chart, IReadOnlyList<HandTrack> tracks)
    {
        var session = new PlaybackSession(chart, PlayMode.Algorithm, new HandReplay(tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        return session;
    }
}
