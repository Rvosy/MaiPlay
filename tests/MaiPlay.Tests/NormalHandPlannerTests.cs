using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class NormalHandPlannerTests
{
    [Fact]
    public void AdjacentDoubleTapsUseTwoHandsAndSingleButtonContacts()
    {
        var chart = SimaiParser.ParseChart("(120){4}78,12,34,56,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        foreach (var group in plan.Strokes.GroupBy(s => s.Start))
            Assert.Equal(2, group.Select(s => s.Hand).Distinct().Count());
        foreach (var frame in plan.Tracks.SelectMany(t => t.Frames).Where(f => f.Contacts.Count > 0))
        {
            Assert.Equal(4, frame.Contacts.Count);
            Assert.All(frame.Contacts, p => Assert.Equal(InputChannel.Button, p.Channel));
            Assert.Single(frame.Contacts.SelectMany(p => ContactMapper.AreasAt(p.Channel,
                new(frame.Position.X + p.Offset.X, frame.Position.Y + p.Offset.Y), p.Radius)).Distinct());
        }
    }

    [Fact]
    public void OrdinaryTouchPairUsesTwoHandsAtTheWrittenTime()
    {
        var chart = SimaiParser.ParseChart("(120){4}B7/B8,A7/A8,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        foreach (var group in plan.Strokes.GroupBy(s => chart.Notes[s.NoteId].Time))
        {
            Assert.Equal(2, group.Select(s => s.Hand).Distinct().Count());
            Assert.All(group, s => Assert.Equal(group.Key, s.Start));
        }
    }

    [Fact]
    public void OccupiedHandLeavesTouchPairToSequentialStrikesByFreeHand()
    {
        var chart = SimaiParser.ParseChart("(120){4}7h[#2],,B2/E3,,,,,,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        var touches = plan.Strokes.Where(s => chart.Notes[s.NoteId].Kind == NoteKind.Touch).ToArray();
        Assert.Equal(2, touches.Length);
        Assert.Single(touches.Select(s => s.Hand).Distinct());
        Assert.True(touches[0].End < touches[1].Start);
        var hold = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Hold);
        Assert.NotEqual(hold.Hand, touches[0].Hand);
    }

    [Fact]
    public void HoldingHandRemainsOccupiedWhileOtherHandStrikes()
    {
        var chart = SimaiParser.ParseChart("(120){4}7h[#2]/2,3,4,3,2,3,4,3,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        var hold = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Hold);
        Assert.All(plan.Strokes.Where(s => s.NoteId != hold.NoteId), s => Assert.NotEqual(hold.Hand, s.Hand));
    }

    [Fact]
    public void StarHeadAndSlideCanUseDifferentHands()
    {
        var chart = SimaiParser.ParseChart("(120){#1}8-4[1##1]/2h[#0.8],8h[#1],E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        var head = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].IsStar);
        var slide = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.Slide);
        Assert.NotEqual(head.Hand, slide.Hand);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void WifiUsesTwoHandsAndCompletesEveryBranch(int start)
    {
        var end = (start + 3) % 8 + 1;
        var chart = SimaiParser.ParseChart($"(120){start}w{end}[0.3##0.166666666667],E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        Assert.Equal(2, plan.Strokes.Where(s => chart.Notes[s.NoteId].Kind == NoteKind.Slide).Select(s => s.Hand).Distinct().Count());
    }

    [Fact]
    public void InfeasibleThreeButtonChordDoesNotBecomeOnePalm()
    {
        var chart = SimaiParser.ParseChart("(120)1/2/3,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.Single(plan.Gaps);
        Assert.Equal(2, plan.Strokes.Count);
        Assert.False(plan.AllCriticalPerfect);
        Assert.Single(plan.Errors);
    }

    [Theory]
    [InlineData(.25, false)]
    [InlineData(.125, false)]
    [InlineData(.1, true)]
    public void SameKeyRepeatAlternatesOnlyAboveSpeedThreshold(double interval, bool alternating)
    {
        var chart = SimaiParser.ParseChart("(120)1,E", first: 1);
        chart = chart with { Notes = Enumerable.Range(0, 6).Select(i => chart.Notes[0] with { Id = i, Time = 1 + i * interval }).ToArray(), Duration = 3 };
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        for (var i = 1; i < plan.Strokes.Count; i++)
            Assert.Equal(alternating, plan.Strokes[i - 1].Hand != plan.Strokes[i].Hand);
    }

    [Fact]
    public void UnplannedChordDoesNotPreventLaterNotesFromPlaying()
    {
        var plan = NormalHandPlanner.Plan(SimaiParser.ParseChart("(120){4}1/2/3,8,7,6,E", first: 1));
        Assert.Single(plan.Gaps);
        Assert.Equal(5, plan.Results.Count(e => e.Grade == AchievementGrade.Perfect));
        Assert.Single(plan.Errors);
    }

    [Fact]
    public void SlowSlideAdjustsContactTimingInsteadOfCompletingTooEarly()
    {
        var chart = SimaiParser.ParseChart("(120)4-1[0.5##9.25],E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        Assert.Empty(plan.Gaps);
    }

    [Fact]
    public void SlideAvoidsJudgingNextTapEarlyAtItsTail()
    {
        var chart = SimaiParser.ParseChart("(135){4}1/8<5[4:1],,4>1[4:1]/5,,8h[2:1],E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join(", ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
    }

    [Theory]
    [InlineData("1,2,3,4,5,6,7,8,1,2,3,4,5,6,7,8", .06, false)]
    [InlineData("8,7,6,5,4,3,2,1,8,7,6,5,4,3,2,1", .06, false)]
    [InlineData("1,2,3,4,5,6,7,8,1,2,3,4,5,6,7,8", .15, true)]
    [InlineData("8,7,6,5,4,3,2,1,8,7,6,5,4,3,2,1", .15, true)]
    public void RotatingTapsChooseSingleHandOrHalfCircleRelay(string notes, double interval, bool relay)
    {
        var chart = SimaiParser.ParseChart("(120){#" + interval.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}" + notes + ",E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        Assert.Empty(plan.Gaps);
        Assert.All(plan.Strokes, s => Assert.Equal(HandTechnique.Rotation, s.Technique));
        Assert.Equal(relay ? 2 : 1, plan.Strokes.Select(s => s.Hand).Distinct().Count());
        foreach (var action in relay ? plan.Strokes : [])
            Assert.True((chart.Notes[action.NoteId].StartPosition <= 4 ? HandSide.Right : HandSide.Left) == action.Hand,
                string.Join(", ", plan.Strokes.Select(s => $"{chart.Notes[s.NoteId].StartPosition}:{s.Hand}")));
    }

    [Theory]
    [InlineData("(150){16}1,2,3,E")]
    [InlineData("(75){32}1,2,3,E")]
    public void SweepSpeedUsesBpmAndSubdivisionTogether(string source)
    {
        var plan = NormalHandPlanner.Plan(SimaiParser.ParseChart(source, first: 1));
        Assert.True(plan.AllCriticalPerfect);
        Assert.Single(plan.Strokes.Select(s => s.Hand).Distinct());
        Assert.All(plan.Strokes, s => Assert.Equal(HandTechnique.Rotation, s.Technique));
    }

    [Theory]
    [InlineData("1/8,2/7,3/6,4b/5b")]
    [InlineData("4/5,3/6,2/7,1b/8b")]
    [InlineData("1/5,2/6,3/7,4/8,5/1,6/2,7/3,8b/4b")]
    [InlineData("8/4,7/3,6/2,5/1,4/8,3/7,2/6,1b/5b")]
    public void FastParallelTapStreamsSweepWithTwoIndependentHands(string notes)
    {
        var chart = SimaiParser.ParseChart("(100){64}" + notes + ",E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Time:F4} {e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.All(plan.Strokes, s => Assert.Equal(HandTechnique.Rotation, s.Technique));
        foreach (var pair in plan.Strokes.GroupBy(s => chart.Notes[s.NoteId].Time))
            Assert.Equal(2, pair.Select(s => s.Hand).Distinct().Count());
        foreach (var hand in plan.Strokes.GroupBy(s => s.Hand))
        {
            var keys = hand.Select(s => chart.Notes[s.NoteId].StartPosition).ToArray();
            var directions = keys.Zip(keys.Skip(1), (a, b) => (b - a + 8) % 8).Distinct();
            Assert.Contains(Assert.Single(directions), new[] { 1, 7 });
        }
    }

    [Theory]
    [InlineData("8,1,2,3,4,5,6,,8,1,2,3,4,5,6,,8")]
    [InlineData("1,8,7,6,5,4,3,,1,8,7,6,5,4,3,,1")]
    public void FastSweepCanPassAnEmptyButtonWithoutStopping(string notes)
    {
        var chart = SimaiParser.ParseChart("(100){64}" + notes + ",E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Single(plan.Strokes.Select(s => s.Hand).Distinct());
        Assert.All(plan.Strokes, s => Assert.Equal(HandTechnique.Rotation, s.Technique));
    }

    [Theory]
    [InlineData("7b,6/8,1/5,2/4,3b")]
    [InlineData("3b,2/4,1/5,6/8,7b")]
    public void FastSweepCanSplitIntoTwoStreamsAndFinishOnOneKey(string notes)
    {
        var plan = NormalHandPlanner.Plan(SimaiParser.ParseChart("(100){64}" + notes + ",E", first: 1));
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        Assert.All(plan.Strokes, s => Assert.Equal(HandTechnique.Rotation, s.Technique));
        Assert.Equal(2, plan.Strokes.Select(s => s.Hand).Distinct().Count());
    }

    [Fact]
    public void SweepCanEnterAtTheNearEdgeAfterAChordWhenItDoesNotHitAnUpcomingNote()
    {
        var plan = NormalHandPlanner.Plan(SimaiParser.ParseChart("(120){#0.05}1/4,8,7,6,E", first: 1));
        Assert.True(plan.AllCriticalPerfect);
        Assert.Empty(plan.Gaps);
    }

    public static IEnumerable<object[]> SweepFinishingChordCases()
    {
        foreach (var bpm in new[] { 180, 200, 240 })
            foreach (var direction in new[] { -1, 1 })
                foreach (var start in Enumerable.Range(1, 8))
                    foreach (var reverseChord in new[] { false, true })
                        foreach (var isBreak in new[] { false, true })
                            yield return [bpm, direction, start, reverseChord, isBreak];
    }

    [Theory]
    [MemberData(nameof(SweepFinishingChordCases))]
    public void SweepContinuesIntoOneSideOfTheFinishingChord(int bpm, int direction, int start, bool reverseChord, bool isBreak)
    {
        int Key(int offset) => (start - 1 + direction * offset + 64) % 8 + 1;
        var singles = Enumerable.Range(0, 5).Select(i => Key(i).ToString());
        var suffix = isBreak ? "b" : "";
        var pair = new[] { $"{Key(5)}{suffix}", $"{Key(1)}{suffix}" };
        if (reverseChord) Array.Reverse(pair);
        var chart = SimaiParser.ParseChart($"({bpm}){{32}}{string.Join(',', singles.Append(string.Join('/', pair)))},E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        var previous = Assert.Single(plan.Strokes, s => s.NoteId == 4);
        var finishing = Assert.Single(plan.Strokes, s => s.NoteId == (reverseChord ? 6 : 5));
        var other = Assert.Single(plan.Strokes, s => s.NoteId == (reverseChord ? 5 : 6));
        Assert.Equal(HandTechnique.Rotation, finishing.Technique);
        Assert.Equal(previous.Hand, finishing.Hand);
        Assert.NotEqual(finishing.Hand, other.Hand);
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(plan.DXScore, session.DXScore);
    }

    [Theory]
    [InlineData(.15)]
    [InlineData(.2)]
    public void SlowerFinishingChordPreservesTheCompletedFastSweep(double tailInterval)
    {
        var interval = tailInterval.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var chart = SimaiParser.ParseChart($"(200){{32}}1,2,3,4,5,6,7,8,1,2,3,4,5,6,7,8{{#{interval}}},1/5,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        var pair = plan.Strokes.Where(s => s.NoteId >= 16).ToArray();
        Assert.Equal(2, pair.Select(s => s.Hand).Distinct().Count());
        var session = new PlaybackSession(chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(plan.DXScore, session.DXScore);
    }

    [Fact]
    public void SweepTailAndLaterPairedSweepsRemainCompleteInTheSamePlan()
    {
        var chart = SimaiParser.ParseChart("(200){32}4,5,6,7,8,1/5,,,,,,,,1,8,7,6,1/5,4/8,3/7,2/6,1/5,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect, string.Join("; ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
        foreach (var pair in plan.Strokes.GroupBy(s => chart.Notes[s.NoteId].Time).Where(g => g.Count() == 2))
            Assert.Equal(2, pair.Select(s => s.Hand).Distinct().Count());
        Assert.Equal(0, new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes).SpeedLimitViolations);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, plan.Replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Assert.Equal(chart.Notes.Count, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(plan.DXScore, session.DXScore);
    }

    [Fact]
    public void AvailableJingleBellsMasterCompletesIncludingItsFinalDoubleSweep()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        var song = Path.Combine(directory?.FullName ?? "", ".reference", "charts", "game-extracted", "00_maimai", "000070_ジングルベル");
        if (!Directory.Exists(song)) return;
        var chart = SimaiParser.LoadSong(song).Charts.Single(c => c.Difficulty == 5);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        Assert.Equal(648, plan.Results.Count);
        Assert.Equal(101m, plan.Achievement);
        var ending = plan.Strokes.Where(s => chart.Notes[s.NoteId].Time >= 93.29).ToArray();
        Assert.Equal(8, ending.Length);
        Assert.All(ending, s => Assert.Equal(chart.Notes[s.NoteId].StartPosition <= 4 ? HandSide.Right : HandSide.Left, s.Hand));
    }

    [Fact]
    public void AvailableJingleBellsReMasterCompletesItsFastSweepSections()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        var song = Path.Combine(directory?.FullName ?? "", ".reference", "charts", "game-extracted", "00_maimai", "000070_ジングルベル");
        if (!Directory.Exists(song)) return;
        var chart = SimaiParser.LoadSong(song).Charts.Single(c => c.Difficulty == 6);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.Equal(737, plan.Results.Count);
        Assert.True(plan.Errors.Count <= 7);
        Assert.True(plan.Achievement >= 99.94m);
        Assert.All(plan.Results.Where(e => e.Note.Time >= 42), e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
    }

    [Fact]
    public void FreeHandCanRotateWhileOtherHandHoldsCentre()
    {
        var chart = SimaiParser.ParseChart("(120){#0.075}Ch[#2]/1,2,3,4,5,6,7,8,1,2,3,4,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.AllCriticalPerfect);
        var hold = Assert.Single(plan.Strokes, s => chart.Notes[s.NoteId].Kind == NoteKind.TouchHold);
        Assert.All(plan.Strokes.Where(s => chart.Notes[s.NoteId].Kind == NoteKind.Tap), s => Assert.NotEqual(hold.Hand, s.Hand));
        Assert.Contains(plan.Strokes, s => s.Technique == HandTechnique.Rotation);
    }

    [Fact]
    public void SweepBeyondTravelLimitLeavesPlayableGaps()
    {
        var chart = SimaiParser.ParseChart("(120){#0.01}1,2,3,4,5,6,7,8,E", first: 1);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.False(plan.AllCriticalPerfect);
        Assert.NotEmpty(plan.Gaps);
        Assert.Equal(chart.Notes.Count, plan.Results.Count);
        Assert.NotEmpty(plan.Strokes);
    }

    [Fact]
    public void ShortSweepCandidatePreservesAvailableAtlasMasterRelayResult()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        var song = Path.Combine(directory?.FullName ?? "", ".reference", "charts", "game-extracted", "24_PRiSMPLUS", "011810_ATLAS RUSH");
        if (!Directory.Exists(song)) return;
        var chart = SimaiParser.LoadSong(song).Charts.Single(c => c.Difficulty == 5);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.True(plan.Results.Count(e => e.Grade == AchievementGrade.Perfect) >= 1000);
        Assert.True(plan.Achievement >= 96.9395m);
    }

    [Theory]
    [InlineData("1>1[0.5##0.02]")]
    [InlineData("8<8[0.5##0.02]")]
    [InlineData("1s5[0.5##0.02]")]
    public void VeryFastSlidesTraverseJudgementAreasWithinThePerfectWindow(string source)
    {
        var plan = NormalHandPlanner.Plan(SimaiParser.ParseChart("(120)" + source + ",E", first: 1));
        Assert.True(plan.AllCriticalPerfect, string.Join(", ", plan.Errors.Select(e => $"{e.Note.Source}: {e.Grade}")));
        Assert.Empty(plan.Gaps);
    }

    [Fact]
    public void PlanningCanBeCancelledAndIsDeterministic()
    {
        var chart = SimaiParser.ParseChart("(120){4}78,12,34,56,E", first: -1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => NormalHandPlanner.Plan(chart, cancellation.Token));
        var a = NormalHandPlanner.Plan(chart);
        var b = NormalHandPlanner.Plan(chart);
        Assert.Equal(a.Replay.Events, b.Replay.Events);
        Assert.Equal(a.Strokes, b.Strokes);
    }

    [Theory]
    [InlineData("21_BUDDiES/011624_SQUAD-Phvntom-", 717)]
    [InlineData("21_BUDDiES/011647_ozma", 623)]
    [InlineData("24_PRiSMPLUS/011842_Synthesis", 645)]
    [InlineData("25_CiRCLE/011897_Sky Trails", 676)]
    [InlineData("24_PRiSMPLUS/011810_ATLAS RUSH", 592)]
    public void AvailablePilotExpertChartsCompleteWithRealInput(string relative, int count)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        var song = Path.Combine(directory?.FullName ?? "", ".reference", "charts", "game-extracted", relative);
        if (!Directory.Exists(song)) return;
        var chart = SimaiParser.LoadSong(song).Charts.Single(c => c.Difficulty == 4);
        var plan = NormalHandPlanner.Plan(chart);
        Assert.Equal(count, plan.Results.Count);
        Assert.True(plan.AllCriticalPerfect, string.Join("\n", plan.Errors.Select(e => $"{e.Note.Time:F4}s #{e.Note.Id}: {e.Grade}")));
        Assert.Equal(101m, plan.Achievement);
        // 以不同 UI 刷新率重新消费同一份输入，核验完整成绩，不能复用规划时的结果。
        var session = new PlaybackSession(chart, PlayMode.Algorithm, plan.Replay.Events);
        for (var time = -2d; time < session.FinishTime; time += 1d / 29) session.Advance(time);
        session.Advance(session.FinishTime + .1);
        Assert.Equal(count, session.Completed);
        Assert.Equal(0, session.Missed);
        Assert.Equal(count * 3, session.DXScore);
        Assert.Equal(101m, session.Achievement.Percentage);
        Assert.Equal(0, session.Remaining);
    }
}
