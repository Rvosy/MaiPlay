using System.Globalization;
using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class HumanRepetitionPlanningTests
{
    private const int StrikeCount = 16;

    public static IEnumerable<object[]> FastRepeatCases()
    {
        foreach (var interval in new[] { .06, .081081, .1, .111, .12, .124 })
            foreach (var key in new[] { 1, 3, 5, 7 })
                foreach (var strikeCount in new[] { 3, StrikeCount })
                    yield return [interval, key, strikeCount];
    }

    public static IEnumerable<object[]> MixedRepeatCases()
    {
        foreach (var (fastInterval, preferredInterval) in new[] { (.12, .125), (.16, .2) })
            foreach (var key in new[] { 1, 3, 5, 7 })
                yield return [fastInterval, preferredInterval, key];
    }

    [Theory]
    [MemberData(nameof(FastRepeatCases))]
    public void FastSameKeyRepeatsAlternateHandsWhenBothAreAvailable(double interval, int key, int strikeCount)
    {
        var plan = HumanPlanner.Plan(RepeatedNotes(interval, key, strikeCount));
        Verify(plan);
        var hands = TapHands(plan, strikeCount);
        for (var i = 1; i < hands.Length; i++)
            Assert.True(hands[i - 1] != hands[i], $"Strike {i + 1} repeated {hands[i]}; sequence: {string.Join(", ", hands)}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void SlowSameKeyRepeatsCanStayOnOneHand(int key)
    {
        var plan = HumanPlanner.Plan(RepeatedNotes(.2, key));
        Verify(plan);
        Assert.Single(TapHands(plan).Distinct());
    }

    [Theory]
    [MemberData(nameof(MixedRepeatCases))]
    public void MixedSpeedRepeatsAlternateInsideFastBurstsUsingTheConfiguredInterval(double fastInterval, double preferredInterval, int key)
    {
        const int count = 18;
        var notes = Enumerable.Range(0, count).Select(i =>
            $"{key}{{#{(i % 3 == 2 ? .25 : fastInterval).ToString(CultureInfo.InvariantCulture)}}}");
        var chart = SimaiParser.ParseChart($"(120){string.Join(',', notes)},E", first: 1);
        var model = new HumanMotionModel(preferredStrikeIntervalSeconds: preferredInterval);
        var plan = HumanPlanner.Plan(chart, new HumanPlannerOptions { MotionModel = model });
        Verify(plan);
        var hands = TapHands(plan, count);
        for (var i = 1; i < hands.Length; i++)
        {
            if (i % 3 == 0) Assert.Equal(hands[i - 1], hands[i]);
            else Assert.NotEqual(hands[i - 1], hands[i]);
        }
    }

    [Theory]
    [MemberData(nameof(FastRepeatCases))]
    public void FastRepeatsUseOneHandWhenTheOtherMustKeepATouchHold(double interval, int key, int strikeCount)
    {
        var plan = HumanPlanner.Plan(RepeatedNotes(interval, key, strikeCount, touchHold: true));
        Verify(plan);
        var hold = Assert.Single(plan.Strokes, s => plan.Chart.Notes[s.NoteId].Kind == NoteKind.TouchHold);
        Assert.All(TapHands(plan, strikeCount), hand => Assert.NotEqual(hold.Hand, hand));
    }

    private static Chart RepeatedNotes(double interval, int key, int strikeCount = StrikeCount, bool touchHold = false)
    {
        var notes = string.Join(',', Enumerable.Repeat(key, strikeCount));
        var hold = touchHold ? $"Ch[#{(strikeCount * interval + .5).ToString(CultureInfo.InvariantCulture)}]/" : "";
        return SimaiParser.ParseChart($"(120){{#{interval.ToString(CultureInfo.InvariantCulture)}}}{hold}{notes},E", first: 1);
    }

    private static HandSide[] TapHands(HandPlan plan, int strikeCount = StrikeCount)
    {
        var taps = plan.Strokes.Where(s => plan.Chart.Notes[s.NoteId].Kind == NoteKind.Tap)
            .OrderBy(s => plan.Chart.Notes[s.NoteId].Time).Select(s => s.Hand).ToArray();
        Assert.Equal(strikeCount, taps.Length);
        return taps;
    }

    private static void Verify(HandPlan plan)
    {
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade} ({e.DifferenceSeconds:F4})")));
        Assert.Empty(plan.Gaps);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var session = new PlaybackSession(plan.Chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(plan.Chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(plan.DXScore, session.DXScore);
    }
}
