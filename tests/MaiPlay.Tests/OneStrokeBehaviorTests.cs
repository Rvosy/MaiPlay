using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class OneStrokeBehaviorTests
{
    [Theory]
    [InlineData(1.35, 1)]
    [InlineData(1.5, 2)]
    public void ChainTimingCorrectionsPreserveContinuousContactAcrossSuccessors(double tapTime, int position)
    {
        var chart = SimaiParser.ParseChart("(190){4}1>5[4:1],5<1[4:1],1>5[4:1],E", first: 1);
        var entranceTap = new ChartNote
        {
            Id = chart.Notes.Count, Kind = NoteKind.Tap, Time = tapTime,
            StartPosition = position, EndPosition = position, Source = position.ToString()
        };
        chart = chart with
        {
            Notes = chart.Notes.Append(entranceTap).OrderBy(n => n.Time).ThenBy(n => n.Id)
                .Select((n, id) => n with { Id = id }).ToArray()
        };
        var plan = HumanPlanner.Plan(chart);
        AssertPerfectReplay(plan);
        var slides = plan.Strokes.Where(s => s.Technique == HandTechnique.Slide).OrderBy(s => s.Start).ToArray();
        Assert.Equal(3, slides.Length);
        var hand = Assert.Single(slides.Select(s => s.Hand).Distinct());
        var frames = plan.Tracks.Single(t => t.Hand == hand).Frames;
        Assert.All(frames.Where(f => f.Time >= slides[0].Start && f.Time < slides[^1].End - 1e-8),
            frame => Assert.Contains(frame.Contacts, c => c.Channel == InputChannel.Touch));
        Assert.Equal(2, plan.Diagnostics!.SlideConnections.Count);
        Assert.All(plan.Diagnostics.SlideConnections, connection => Assert.True(connection.Continuous));
    }

    [Theory]
    [InlineData(2, true, false)]
    [InlineData(7, false, false)]
    [InlineData(7, true, true)]
    [InlineData(2, false, true)]
    public void ChangingOnlyTheFollowingTapChangesWhetherTheSlideTailIsRelayed(int tail, bool relay, bool mirror)
    {
        var prefix = mirror ? "5-2[4:1]/1h[#0.3],2>4[4:1]" : "4-7[4:1]/8h[#0.3],7<5[4:1]";
        var chart = SimaiParser.ParseChart($"(150){{4}}{prefix},{{#0.43}},{tail},E", first: 1);
        var plan = HumanPlanner.Plan(chart);
        AssertPerfectReplay(plan);
        var slides = plan.Strokes.Where(s => chart.Notes.Single(n => n.Id == s.NoteId).Kind == NoteKind.Slide)
            .OrderBy(s => s.Start).ToArray();
        Assert.Equal(2, slides.Length);
        Assert.Equal(relay, slides[0].Hand != slides[1].Hand);
        var connection = Assert.Single(plan.Diagnostics!.SlideConnections);
        Assert.Equal(slides[0].NoteId, connection.PreviousId);
        Assert.Equal(slides[1].NoteId, connection.NextId);
        Assert.Equal(relay, connection.IsRelay);
        Assert.Equal(!relay, connection.Continuous);
        Assert.Equal(Math.Max(0, slides[1].Start - slides[0].End), connection.ContactGapSeconds, 8);
        var nextTap = Assert.Single(plan.Strokes, s => s.NoteId == chart.Notes.Last().Id);
        Assert.Equal(relay ? slides[0].Hand : Other(slides[0].Hand), nextTap.Hand);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);

        if (relay)
        {
            Assert.True(slides[0].End < slides[1].Start);
            var seam = chart.Notes.Single(n => n.Id == slides[1].NoteId).SlideStartTime;
            for (var time = seam - .06; time <= seam + .06; time += PlaybackSession.LogicStep)
            {
                var left = plan.Replay.PoseAt(HandSide.Left, time)!;
                var right = plan.Replay.PoseAt(HandSide.Right, time)!;
                Assert.True(left.Position.DistanceTo(right.Position) >= .38 - 1e-6,
                    $"Hands overlap at {time:F5}: {left.Position.DistanceTo(right.Position):F5}");
            }
        }
        else
        {
            Assert.InRange(slides[1].Start - slides[0].End, -1e-8, 1e-8);
            Assert.Contains(plan.Replay.PoseAt(slides[0].Hand, slides[0].End)!.Contacts,
                c => c.Channel == InputChannel.Touch);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 121.2)]
    public void IndependentSlidesKeepAContinuousDrawingHandWhenThereIsNoRelayBenefit(bool mirror, double first)
    {
        var forward = mirror ? "8<4" : "1>5";
        var backward = mirror ? "4>8" : "5<1";
        var source = "(190){4}" + string.Join(',', Enumerable.Range(0, 11)
            .Select(i => $"{(i % 2 == 0 ? forward : backward)}[4:1]")) + ",E";
        var chart = SimaiParser.ParseChart(source, first: first);
        var plan = HumanPlanner.Plan(chart);
        AssertPerfectReplay(plan);

        var slideIds = chart.Notes.Where(n => n.Kind == NoteKind.Slide).Select(n => n.Id).ToHashSet();
        var slides = plan.Strokes.Where(s => slideIds.Contains(s.NoteId)).OrderBy(s => s.Start).ToArray();
        Assert.Equal(11, slides.Length);
        Assert.Equal(10, plan.Diagnostics!.SlideConnections.Count);
        Assert.All(plan.Diagnostics.SlideConnections, c =>
        {
            Assert.True(c.Continuous);
            Assert.False(c.IsRelay);
            Assert.InRange(c.ContactGapSeconds, 0, 1e-8);
            Assert.InRange(c.EndpointDistanceMillimeters, 0, .02);
        });
        var hand = Assert.Single(slides.Select(s => s.Hand).Distinct());
        var frames = plan.Tracks.Single(t => t.Hand == hand).Frames;
        Assert.All(frames.Where(f => f.Time >= slides[0].Start && f.Time < slides[^1].End - 1e-8),
            frame => Assert.Contains(frame.Contacts, c => c.Channel == InputChannel.Touch));
        Assert.All(slides.SkipLast(1), slide =>
        {
            var next = slides[Array.IndexOf(slides, slide) + 1];
            Assert.InRange(next.Start - slide.End, -1e-8, 1e-8);
            var endpoint = chart.Notes.Single(n => n.Id == slide.NoteId).SlidePath!.PointAt(1);
            Assert.InRange(plan.Replay.PoseAt(hand, slide.End)!.Position.DistanceTo(endpoint), 0, .02);
        });
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
    }

    [Theory]
    [InlineData("(120){#0.52}1>5[0.5##0.5],5<1[0.5##0.5],E")]
    [InlineData("(120){#0.48}1>5[0.5##0.5],5<1[0.5##0.5],E")]
    [InlineData("(120){4}1>5[4:1],6<2[4:1],E")]
    [InlineData("(120){4}1w5[4:1],5<1[4:1],E")]
    [InlineData("(120){4}1>5[4:1],5w1[4:1],E")]
    [InlineData("(120){4}1>5[4:1],5<1[4:1]*-2[4:1],E")]
    [InlineData("(120){4}1>5[4:1]/2-5[4:1],5<1[4:1],E")]
    [InlineData("(120){4}1>5[4:1]<1[4:1],E")]
    public void DisconnectedOrAmbiguousSlidesDoNotBecomeASingleStroke(string source)
    {
        var chart = SimaiParser.ParseChart(source, first: 1);
        Assert.Empty(SlideConnections.Analyze(chart));
    }

    private static void AssertPerfectReplay(HandPlan plan)
    {
        Assert.True(plan.AllCriticalPerfect,
            string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.True(plan.IsSolved);
        var session = new PlaybackSession(plan.Chart, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(plan.Chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }

    private static HandSide Other(HandSide hand) => hand == HandSide.Left ? HandSide.Right : HandSide.Left;
}
