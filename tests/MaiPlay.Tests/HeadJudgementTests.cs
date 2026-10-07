using MaiPlay.Core;

namespace MaiPlay.Tests;

public class HeadJudgementTests
{
    // Independent expectations transcribed from MajdataPlay dc19722d, NoteDrop.Judge,
    // TouchDrop.Judge and each TooLateCheck. Input is on the lattice; chart time varies.
    public static IEnumerable<object[]> TapBoundaries()
    {
        var edges = new (double Edge, AchievementGrade At, AchievementGrade Beyond)[]
        {
            (1d / 60, AchievementGrade.Perfect, AchievementGrade.LatePerfect2nd),
            (2d / 60, AchievementGrade.LatePerfect2nd, AchievementGrade.LatePerfect3rd),
            (3d / 60, AchievementGrade.LatePerfect3rd, AchievementGrade.LateGreat),
            (4d / 60, AchievementGrade.LateGreat, AchievementGrade.LateGreat2nd),
            (5d / 60, AchievementGrade.LateGreat2nd, AchievementGrade.LateGreat3rd),
            (6d / 60, AchievementGrade.LateGreat3rd, AchievementGrade.LateGood),
            (.15, AchievementGrade.LateGood, AchievementGrade.Miss)
        };
        foreach (var (edge, at, beyond) in edges)
        {
            foreach (var direction in new[] { -1, 1 })
            {
                AchievementGrade Side(AchievementGrade grade) => direction > 0 || grade is AchievementGrade.Perfect or AchievementGrade.Miss
                    ? grade : (AchievementGrade)(14 - (int)grade);
                yield return [direction * (edge - .000001), Side(at)];
                yield return [direction * edge, Side(at)];
                yield return [direction * (edge + .000001), Side(beyond)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(TapBoundaries))]
    public void TapEveryClosedBoundaryHasAnIndependentExpectedGrade(double difference, AchievementGrade expected)
    {
        var chart = Chart(new ChartNote { Time = 1 - difference });
        var session = Play(chart, Down(1, "1"));
        Assert.Equal(expected, session.GetResult(chart.Notes[0])!.Grade);
        if (expected != AchievementGrade.Miss)
            Assert.Equal(difference, session.GetResult(chart.Notes[0])!.DifferenceSeconds!.Value, 10);
    }

    public static IEnumerable<object[]> TouchBoundaries()
    {
        yield return [-.150001, AchievementGrade.Miss];
        yield return [-.15, AchievementGrade.Perfect];
        yield return [-.149999, AchievementGrade.Perfect];
        var edges = new (double Edge, AchievementGrade At, AchievementGrade Beyond)[]
        {
            (.15, AchievementGrade.Perfect, AchievementGrade.LatePerfect2nd),
            (.175, AchievementGrade.LatePerfect2nd, AchievementGrade.LatePerfect3rd),
            (.2, AchievementGrade.LatePerfect3rd, AchievementGrade.LateGreat),
            (13d / 60, AchievementGrade.LateGreat, AchievementGrade.LateGreat2nd),
            (14d / 60, AchievementGrade.LateGreat2nd, AchievementGrade.LateGreat3rd),
            (.25, AchievementGrade.LateGreat3rd, AchievementGrade.LateGood),
            (.3, AchievementGrade.LateGood, AchievementGrade.Miss)
        };
        foreach (var (edge, at, beyond) in edges)
        {
            yield return [edge - .000001, at];
            yield return [edge, at];
            yield return [edge + .000001, beyond];
        }
    }

    [Theory]
    [MemberData(nameof(TouchBoundaries))]
    public void TouchUsesItsOwnAsymmetricWindow(double difference, AchievementGrade expected)
    {
        var chart = Chart(new ChartNote { Kind = NoteKind.Touch, Time = 1 - difference, TouchArea = "C" });
        var session = Play(chart, Down(1, "C"));
        Assert.Equal(expected, session.GetResult(chart.Notes[0])!.Grade);
    }

    [Theory]
    [InlineData(NoteKind.Tap, "1", .15, AchievementGrade.Perfect)]
    [InlineData(NoteKind.Tap, "1", .150001, AchievementGrade.Miss)]
    [InlineData(NoteKind.Touch, "C", .25, AchievementGrade.LateGreat3rd)]
    [InlineData(NoteKind.TouchHold, "C", .25, AchievementGrade.LateGreat)]
    public void ExProtectsTapHeadsButDoesNotApplyToTouch(NoteKind kind, string area, double difference, AchievementGrade expected)
    {
        var chart = Chart(new ChartNote { Kind = kind, Time = 1 - difference, Duration = kind == NoteKind.TouchHold ? 1 : 0,
            TouchArea = kind is NoteKind.Touch or NoteKind.TouchHold ? area : "", IsEx = true });
        var session = Play(chart, Down(1, area));
        Assert.Equal(expected, session.GetResult(chart.Notes[0])!.Grade);
    }

    [Fact]
    public void TapAndHoldAcceptButtonOrMatchingASensorButTouchNeedsASensor()
    {
        foreach (var kind in new[] { NoteKind.Tap, NoteKind.Hold })
        {
            var chart = Chart(new ChartNote { Kind = kind, Time = 1, Duration = kind == NoteKind.Hold ? 1 : 0 });
            Assert.Equal(AchievementGrade.Perfect, Play(chart, Down(1, "A1")).GetResult(chart.Notes[0])!.Grade);
            Assert.Equal(AchievementGrade.Perfect, Play(chart, Down(1, "1")).GetResult(chart.Notes[0])!.Grade);
            Assert.Equal(AchievementGrade.Miss, Play(chart, Down(1, "B1")).GetResult(chart.Notes[0])!.Grade);
        }
        var touch = Chart(new ChartNote { Kind = NoteKind.Touch, Time = 1, TouchArea = "A1" });
        Assert.Equal(AchievementGrade.Miss, Play(touch, Down(1, "1")).GetResult(touch.Notes[0])!.Grade);
    }

    [Fact]
    public void OneSensorEdgeIsConsumedByTapBeforeTouchAndTwoChannelsCanHitBoth()
    {
        var chart = Chart(new ChartNote { Time = 1 }, new ChartNote { Kind = NoteKind.Touch, Time = 1, TouchArea = "A1" });
        var one = Play(chart, Down(1, "A1"));
        Assert.Equal(AchievementGrade.Perfect, one.GetResult(chart.Notes[0])!.Grade);
        Assert.Equal(AchievementGrade.Miss, one.GetResult(chart.Notes[1])!.Grade);
        var two = Play(chart, Down(1, "1"), Down(1, "A1"));
        Assert.All(chart.Notes, note => Assert.Equal(AchievementGrade.Perfect, two.GetResult(note)!.Grade));
    }

    [Fact]
    public void HeldInputDoesNotGenerateAnotherHeadEdgeAndQueueKeepsEarlierNoteFirst()
    {
        var chart = Chart(new ChartNote { Time = 1 }, new ChartNote { Time = 1.05 });
        var held = Play(chart, Down(1.05, "1"));
        Assert.Equal(AchievementGrade.LatePerfect3rd, held.GetResult(chart.Notes[0])!.Grade);
        Assert.Equal(AchievementGrade.Miss, held.GetResult(chart.Notes[1])!.Grade);
        var retrigger = Play(chart, Down(1, "1"), Up(1.025, "1"), Down(1.05, "1"));
        Assert.All(chart.Notes, n => Assert.Equal(AchievementGrade.Perfect, retrigger.GetResult(n)!.Grade));
    }

    [Theory]
    [InlineData(NoteKind.Touch, "C")]
    [InlineData(NoteKind.Tap, "1")]
    public void ContactHeldFromBeforeTheEarlyWindowDoesNotHitTheHead(NoteKind kind, string area)
    {
        var chart = Chart(new ChartNote { Kind = kind, Time = 1, TouchArea = kind == NoteKind.Touch ? area : "" });
        Assert.Equal(AchievementGrade.Miss, Play(chart, Down(.8, area)).GetResult(chart.Notes[0])!.Grade);
    }

    [Fact]
    public void HoldHeadLeavesItsQueueBeforeTheBodyFinishes()
    {
        var chart = Chart(new ChartNote { Kind = NoteKind.Hold, Time = 1, Duration = 1 }, new ChartNote { Time = 1.5 });
        var session = Play(chart, Down(1, "1"), Up(1.475, "1"), Down(1.5, "1"));
        Assert.All(chart.Notes, n => Assert.Equal(AchievementGrade.Perfect, session.GetResult(n)!.Grade));
    }

    [Fact]
    public void AHoldOccupyingTheSharedSensorDoesNotManufactureAnEdgeForTheFollowingTap()
    {
        var chart = Chart(new ChartNote { Kind = NoteKind.Hold, Time = 1, Duration = 1 }, new ChartNote { Time = 1.5 });
        var session = Play(chart, Down(1, "A1"));
        Assert.Equal(AchievementGrade.Perfect, session.GetResult(chart.Notes[0])!.Grade);
        Assert.Equal(AchievementGrade.Miss, session.GetResult(chart.Notes[1])!.Grade);
    }

    [Theory]
    [InlineData(NoteKind.Hold, "1", .3)]
    [InlineData(NoteKind.TouchHold, "C", .45)]
    public void ShortHoldsUseOnlyTheHeadGrade(NoteKind kind, string area, double duration)
    {
        var chart = Chart(new ChartNote { Kind = kind, Time = 1, Duration = duration, TouchArea = area == "C" ? area : "" });
        var session = Play(chart, Down(1, area), Up(1.025, area));
        Assert.Equal(AchievementGrade.Perfect, session.GetResult(chart.Notes[0])!.Grade);
    }

    [Theory]
    [InlineData(NoteKind.Hold, "1", .3)]
    [InlineData(NoteKind.Hold, "1", 1)]
    [InlineData(NoteKind.TouchHold, "C", .45)]
    [InlineData(NoteKind.TouchHold, "C", 1)]
    public void AHitHeadShowsHoldingDuringTheIgnoredHeadAndShortHoldBody(NoteKind kind, string area, double duration)
    {
        var chart = Chart(new ChartNote { Kind = kind, Time = 1, Duration = duration, TouchArea = area == "C" ? area : "" });
        var session = new PlaybackSession(chart, PlayMode.Algorithm, [Down(1, area)]);
        session.Advance(.99);
        Assert.False(session.IsHolding(chart.Notes[0]));
        session.Advance(1.05);
        Assert.True(session.IsHolding(chart.Notes[0]));
        session.Advance(1 + duration);
        Assert.False(session.IsHolding(chart.Notes[0]));
    }

    [Theory]
    [InlineData(NoteKind.Hold, "1")]
    [InlineData(NoteKind.TouchHold, "C")]
    public void ReleaseInsideTheIgnoredTailDoesNotChangeTheGradeOrHoldingFeedback(NoteKind kind, string area)
    {
        var chart = Chart(new ChartNote { Kind = kind, Time = 1, Duration = 1, TouchArea = area == "C" ? area : "" });
        var session = new PlaybackSession(chart, PlayMode.Algorithm, [Down(1, area), Up(1.85, area)]);
        var events = session.Advance(1.9);
        Assert.True(session.IsHolding(chart.Notes[0]));
        Assert.DoesNotContain(events, e => e.Kind == PlaybackEventKind.Released);
        session.Advance(2);
        Assert.Equal(AchievementGrade.Perfect, session.GetResult(chart.Notes[0])!.Grade);
    }

    [Theory]
    [InlineData(NoteKind.Hold, "1")]
    [InlineData(NoteKind.TouchHold, "C")]
    public void HoldReleaseGraceCanRecoverAndALongerGapDegradesTheFinalGrade(NoteKind kind, string area)
    {
        var chart = Chart(new ChartNote { Kind = kind, Time = 1, Duration = 1, TouchArea = area == "C" ? area : "" });
        var shortGap = Play(chart, Down(1, area), Up(1.4, area), Down(1.425, area));
        Assert.Equal(AchievementGrade.Perfect, shortGap.GetResult(chart.Notes[0])!.Grade);
        var longGap = Play(chart, Down(1, area), Up(1.4, area), Down(1.5, area));
        Assert.Equal(AchievementGrade.LatePerfect2nd, longGap.GetResult(chart.Notes[0])!.Grade);
        Assert.Equal("hold-head", longGap.GetResult(chart.Notes[0])!.TimingKind);
        Assert.Equal(0, longGap.GetResult(chart.Notes[0])!.DifferenceSeconds);
    }

    [Theory]
    [InlineData(NoteKind.Hold, "1", 8, AchievementGrade.Perfect)]
    [InlineData(NoteKind.Hold, "1", 9, AchievementGrade.Perfect)]
    [InlineData(NoteKind.Hold, "1", 10, AchievementGrade.LatePerfect2nd)]
    [InlineData(NoteKind.TouchHold, "C", 8, AchievementGrade.Perfect)]
    [InlineData(NoteKind.TouchHold, "C", 9, AchievementGrade.Perfect)]
    [InlineData(NoteKind.TouchHold, "C", 10, AchievementGrade.LatePerfect2nd)]
    public void ReleaseGracePreservesTheUpstreamCheckBeforeIncrementBoundary(NoteKind kind, string area, int offTicks, AchievementGrade expected)
    {
        // BodyCheck tests wait <= 2/60 before incrementing. At 240 Hz, a ninth
        // OFF sample is still deferred; the tenth retroactively charges the gap.
        var chart = Chart(new ChartNote { Kind = kind, Time = 1, Duration = 1, TouchArea = area == "C" ? area : "" });
        var session = Play(chart, Down(1, area), Up(1.4, area), Down(1.4 + offTicks / 240d, area));
        Assert.Equal(expected, session.GetResult(chart.Notes[0])!.Grade);
    }

    [Fact]
    public void EachRecoveredShortGapHasItsOwnGracePeriod()
    {
        var chart = Chart(new ChartNote { Kind = NoteKind.Hold, Time = 1, Duration = 1 });
        var session = Play(chart, Down(1, "1"), Up(1.2, "1"), Down(1.225, "1"),
            Up(1.4, "1"), Down(1.425, "1"), Up(1.6, "1"), Down(1.625, "1"));
        Assert.Equal(AchievementGrade.Perfect, session.GetResult(chart.Notes[0])!.Grade);
    }

    [Theory]
    [InlineData(NoteKind.Hold, "1")]
    [InlineData(NoteKind.TouchHold, "C")]
    public void ExDoesNotProtectTheSustainAndNoInputNaturallyMisses(NoteKind kind, string area)
    {
        var chart = Chart(new ChartNote { Kind = kind, Time = 1, Duration = 1, TouchArea = area == "C" ? area : "", IsEx = true });
        var dropped = Play(chart, Down(1, area), Up(1.05, area));
        Assert.Equal(AchievementGrade.LateGood, dropped.GetResult(chart.Notes[0])!.Grade);
        Assert.Equal(AchievementGrade.Miss, Play(chart).GetResult(chart.Notes[0])!.Grade);
    }

    [Theory]
    [InlineData(NoteKind.Hold, "1")]
    [InlineData(NoteKind.TouchHold, "C")]
    public void AHeadMissCanRecoverToGoodFromTheSustain(NoteKind kind, string area)
    {
        var chart = Chart(new ChartNote { Kind = kind, Time = 1, Duration = 1, TouchArea = area == "C" ? area : "" });
        var session = new PlaybackSession(chart, PlayMode.Algorithm, [Down(.8, area)]);
        var events = session.Advance(3);
        Assert.DoesNotContain(events, e => e.Kind == PlaybackEventKind.Started);
        Assert.Equal(AchievementGrade.LateGood, session.GetResult(chart.Notes[0])!.Grade);
        Assert.Equal("head-timeout", session.GetResult(chart.Notes[0])!.Reason);
    }

    [Theory]
    [InlineData(1, AchievementGrade.Perfect)]
    [InlineData(.999999, AchievementGrade.LatePerfect2nd)]
    [InlineData(.67, AchievementGrade.LatePerfect2nd)]
    [InlineData(.669999, AchievementGrade.LateGreat2nd)]
    [InlineData(.33, AchievementGrade.LateGreat2nd)]
    [InlineData(.329999, AchievementGrade.LateGood)]
    [InlineData(.05, AchievementGrade.LateGood)]
    [InlineData(.049999, AchievementGrade.LateGood)]
    public void SustainRatioThresholdsMatchNoteLongDrop(double ratio, AchievementGrade expected)
    {
        Assert.Equal(expected, HeadJudgements.HoldEndGrade(AchievementGrade.Perfect, 0, 1, 1 - ratio));
    }

    [Fact]
    public void SustainTableRetainsTheUpstreamNonCriticalPerfectException()
    {
        Assert.Equal(AchievementGrade.LateGreat,
            HeadJudgements.HoldEndGrade(AchievementGrade.LatePerfect2nd, .02, 1, .5));
        Assert.Equal(AchievementGrade.FastGreat,
            HeadJudgements.HoldEndGrade(AchievementGrade.FastGood, -.12, 1, 0));
        Assert.Equal(AchievementGrade.LateGood,
            HeadJudgements.HoldEndGrade(AchievementGrade.Miss, .15, 1, .95));
        Assert.Equal(AchievementGrade.Miss,
            HeadJudgements.HoldEndGrade(AchievementGrade.Miss, .15, 1, .950001));
    }

    [Fact]
    public void TouchGroupNeedsStrictMajorityAndInheritsTheThresholdCrossingResult()
    {
        var pair = Chart(Touch("B1"), Touch("B2"));
        var half = Play(pair, Down(1, "B1"));
        Assert.Equal(AchievementGrade.Miss, half.GetResult(pair.Notes[1])!.Grade);

        var triple = Chart(Touch("B1"), Touch("B2"), Touch("B3"));
        var majority = Play(triple, Down(1, "B1"), Down(1.25, "B2"));
        Assert.Equal(AchievementGrade.LateGreat3rd, majority.GetResult(triple.Notes[2])!.Grade);
        Assert.Equal(.25, majority.GetResult(triple.Notes[2])!.DifferenceSeconds);
        Assert.Equal("group", majority.GetResult(triple.Notes[2])!.TimingKind);
    }

    [Fact]
    public void TouchGroupingComesFromTimingAndAdjacencyRatherThanTheVisualEachFlag()
    {
        var chart = Chart(Touch("B1") with { IsEach = false }, Touch("B2") with { IsEach = false },
            Touch("B3") with { IsEach = false });
        var session = Play(chart, Down(1, "B1"), Down(1, "B2"));
        Assert.All(chart.Notes, n => Assert.Equal(AchievementGrade.Perfect, session.GetResult(n)!.Grade));
        Assert.Equal("group", session.GetResult(chart.Notes[2])!.TimingKind);
    }

    [Fact]
    public void TouchGroupsUseSensorAdjacencyAndCanMixTouchAndTouchHold()
    {
        var chart = Chart(Touch("B1"), Touch("C"), Touch("B5"), Touch("A3"));
        var session = Play(chart, Down(1, "B1"), Down(1, "C"));
        Assert.Equal(AchievementGrade.Perfect, session.GetResult(chart.Notes[2])!.Grade);
        Assert.Equal(AchievementGrade.Miss, session.GetResult(chart.Notes[3])!.Grade);

        var mixed = Chart(Touch("B1"), Touch("B2"), Touch("B3") with { Kind = NoteKind.TouchHold, Duration = .3 });
        Assert.Equal(AchievementGrade.Perfect,
            Play(mixed, Down(1, "B1"), Down(1, "B2")).GetResult(mixed.Notes[2])!.Grade);
    }

    [Fact]
    public void TouchHoldBodyGroupSuppliesOnlyAStrictMajorityOfActiveMembers()
    {
        var chart = Chart(Touch("B1") with { Kind = NoteKind.TouchHold, Duration = 1 },
            Touch("B2") with { Kind = NoteKind.TouchHold, Duration = 1 },
            Touch("B3") with { Kind = NoteKind.TouchHold, Duration = 1 });
        var majority = Play(chart, Down(1, "B1"), Down(1, "B2"));
        Assert.All(chart.Notes, n => Assert.Equal(AchievementGrade.Perfect, majority.GetResult(n)!.Grade));
        var one = Play(chart, Down(1, "B1"));
        Assert.Equal(AchievementGrade.Miss, one.GetResult(chart.Notes[2])!.Grade);
    }

    [Fact]
    public void EndedTouchHoldMembersLeaveTheBodyGroupDenominator()
    {
        var chart = Chart(Touch("B1") with { Kind = NoteKind.TouchHold, Duration = 1 },
            Touch("B2") with { Kind = NoteKind.TouchHold, Duration = 1 },
            Touch("B3") with { Kind = NoteKind.TouchHold, Duration = .5 });
        var session = Play(chart, Down(1, "B1"), Down(1, "B3"));
        Assert.Equal(AchievementGrade.LateGreat2nd, session.GetResult(chart.Notes[1])!.Grade);
    }

    [Fact]
    public void ResetClearsHeadGroupsReleaseDurationsAndConsumedEdges()
    {
        var chart = Chart(Touch("B1") with { Kind = NoteKind.TouchHold, Duration = 1 }, Touch("B2"), Touch("B3"));
        var session = new PlaybackSession(chart, PlayMode.Algorithm, [Down(1, "B1"), Down(1, "B2"), Up(1.4, "B1")]);
        var first = session.Advance(3).ToArray();
        session.Reset();
        Assert.Equal(first, session.Advance(3));
    }

    private static ChartNote Touch(string area) => new ChartNote { Kind = NoteKind.Touch, Time = 1, TouchArea = area, IsEach = true };
    private static Chart Chart(params ChartNote[] notes) => new("Head fixture", "", "", 5, "",
        notes.Select((n, id) => n with { Id = id }).ToArray(), 3);
    private static InputEvent Down(double time, string area) => new(time, char.IsDigit(area[0]) ? InputChannel.Button : InputChannel.Touch, area, true);
    private static InputEvent Up(double time, string area) => Down(time, area) with { IsDown = false };
    private static PlaybackSession Play(Chart chart, params InputEvent[] events)
    {
        var session = new PlaybackSession(chart, PlayMode.Algorithm, events);
        session.Advance(3);
        return session;
    }
}
