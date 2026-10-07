using MaiPlay.Core;

namespace MaiPlay.Tests;

// Fixed routes and tail constants transcribed from MajdataPlay dc19722d... SlideTables.cs.
// Expected routes never come from SlideJudgeTables or SlideJudgements.
public class CoreSlideJudgementTests
{
    private const double Tick = 1.0 / 240;

    public static IEnumerable<object[]> StandardRoutes()
    {
        yield return ["1>1[1]", "A1 A2 A3 A4 A5 A6 A7 A8 A1", .058];
        yield return ["1>2[1]", "A1 A2", .465];
        yield return ["1>3[1]", "A1 A2 A3", .233];
        yield return ["1>4[1]", "A1 A2 A3 A4", .155];
        yield return ["1>5[1]", "A1 A2 A3 A4 A5", .116];
        yield return ["1>6[1]", "A1 A2 A3 A4 A5 A6", .093];
        yield return ["1>7[1]", "A1 A2 A3 A4 A5 A6 A7", .078];
        yield return ["1>8[1]", "A1 A2 A3 A4 A5 A6 A7 A8", .066];
        yield return ["1-3[1]", "A1 B2 A3", .182];
        yield return ["1-4[1]", "A1 B2 B3 A4", .19];
        yield return ["1-5[1]", "A1 B1 C B5 A5", .152];
        yield return ["1-6[1]", "A1 B8 B7 A6", .19];
        yield return ["1-7[1]", "A1 B8 A7", .182];
        yield return ["1v1[1]", "A1 B1 C B1 A1", .185];
        yield return ["1v2[1]", "A1 B1 C B2 A2", .15];
        yield return ["1v3[1]", "A1 B1 C B3 A3", .158];
        yield return ["1v4[1]", "A1 B1 C B4 A4", .158];
        yield return ["1v6[1]", "A1 B1 C B6 A6", .158];
        yield return ["1v7[1]", "A1 B1 C B7 A7", .158];
        yield return ["1v8[1]", "A1 B1 C B8 A8", .154];
        yield return ["1pp1[1]", "A1 B1 C B4 A3 A2 A1", .065];
        yield return ["1pp2[1]", "A1 B1 C B4 A3 A2", .086];
        yield return ["1pp3[1]", "A1 B1 C B4 A3", .157];
        yield return ["1pp4[1]", "A1 B1 C B4 A3 A2 B1 C B4 A4", .065];
        yield return ["1pp5[1]", "A1 B1 C B4 A3 A2 B1 C B5 A5", .065];
        yield return ["1pp6[1]", "A1 B1 C B4 A3 A2 B1 B8 B6 A6", .067];
        yield return ["1pp7[1]", "A1 B1 C B4 A3 A2 B1 B8 A7", .079];
        yield return ["1pp8[1]", "A1 B1 C B4 A3 A2 A1 A8", .0626];
        yield return ["1V72[1]", "A1 A8 A7 B8 B1 A2", .1];
        yield return ["1V73[1]", "A1 A8 A7 B7 C B3 A3", .104];
        yield return ["1V74[1]", "A1 A8 A7 B6 B5 A4", .098];
        yield return ["1V75[1]", "A1 A8 A7 A6 A5", .105];
        yield return ["1s5[1]", "A1 B8 B7 C B3 B4 A5", .13];
        yield return ["1p1[1]", "A1 B8 B7 B6 B5 B4 B3 B2 A1", .095];
        yield return ["1p2[1]", "A1 B8 B7 B6 B5 B4 B3 A2", .112];
        yield return ["1p3[1]", "A1 B8 B7 B6 B5 B4 A3", .125];
        yield return ["1p4[1]", "A1 B8 B7 B6 B5 A4", .139];
        yield return ["1p5[1]", "A1 B8 B7 B6 A5", .16];
        yield return ["1p6[1]", "A1 B8 B7 B6 B5 B4 B3 B2 B1 B8 B7 A6", .08];
        yield return ["1p7[1]", "A1 B8 B7 B6 B5 B4 B3 B2 B1 B8 A7", .084];
        yield return ["1p8[1]", "A1 B8 B7 B6 B5 B4 B3 B2 B1 A8", .0895];
    }

    [Theory]
    [MemberData(nameof(StandardRoutes))]
    public void EveryStandardTemplateAcceptsItsFixedRouteAtEveryRotationAndMirror(string syntax, string route, double tailConstant)
    {
        var original = ReadNote(syntax);
        var segment = original.SlidePath!.Segments[0];
        for (var rotation = 0; rotation < 8; rotation++)
        foreach (var mirror in segment.Shape is ">" or "p" or "pp" or "V" or "s" ? new[] { false, true } : [false])
        {
            var start = rotation + 1;
            var shape = segment.Shape == ">" ? (start is 1 or 2 or 7 or 8) == !mirror ? ">" : "<" :
                mirror ? segment.Shape switch { "p" => "q", "pp" => "qq", "s" => "z", _ => segment.Shape } : segment.Shape;
            var end = mirror ? MirrorButton(segment.End) : segment.End;
            var via = segment.Via == 0 ? 0 : mirror ? MirrorButton(segment.Via) : segment.Via;
            var rotated = new SlideSegment(shape, start, RotateButton(end, rotation), via == 0 ? 0 : RotateButton(via, rotation));
            var note = original with { SlidePath = new SlidePath([rotated], [1]), StartPosition = start, EndPosition = rotated.End };
            var play = new Run(note);
            play.Route(route.Split(' ').Select(area => RotateArea(mirror ? MirrorArea(area) : area, rotation)), 3.5 - tailConstant);
            var result = Assert.Single(play.Results, e => e.IsFinal);
            Assert.Equal(AchievementGrade.Perfect, result.Grade);
            Assert.Equal(0, result.DifferenceSeconds!.Value, 9);
            Assert.Equal(1, play.Judgements.GetProgress(note));
        }
    }

    public static IEnumerable<object[]> StandardSyntaxes() => StandardRoutes().Select(row => new[] { row[0] });

    [Theory]
    [MemberData(nameof(StandardSyntaxes))]
    public void EveryTemplateMissesWithoutInputOrWithOnlyWrongRegions(string syntax)
    {
        foreach (var wrongRegion in new[] { false, true })
        {
            var play = new Run(ReadNote(syntax));
            if (wrongRegion) play.Step(2.5, ("E1", true));
            play.Step(4.101);
            Assert.Equal(AchievementGrade.Miss, Assert.Single(play.Results).Grade);
        }
    }

    [Theory]
    [InlineData("1<8[1]", "A1 A8", .465)]
    [InlineData("1q8[1]", "A1 B2 B3 B4 B5 B6 B7 A8", .112)]
    [InlineData("1qq4[1]", "A1 B1 C B6 A7 A8 B1 B2 B4 A4", .067)]
    [InlineData("1z5[1]", "A1 B2 B3 C B7 B6 A5", .13)]
    [InlineData("1V38[1]", "A1 A2 A3 B2 B1 A8", .1)]
    public void MirroredTemplatesUseMirroredSensorQueues(string syntax, string route, double constant)
    {
        var play = new Run(ReadNote(syntax));
        play.Route(route.Split(' '), 3.5 - constant);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Theory]
    [InlineData("A2")]
    [InlineData("B2")]
    public void OrAreaAcceptsEitherSensorAndCanSkipOnlyItsPredecessor(string middle)
    {
        var play = new Run(ReadNote("1-3[1]"));
        play.Step(2.1, ("A3", true));
        Assert.Empty(play.Judgements.Trace);
        play.Step(2.2, ("A3", false), (middle, true));
        play.Step(3.318 - Tick, ("A3", true));
        play.Step(3.318);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Fact]
    public void ShortArcRequiresFirstAreaBeforeItsTail()
    {
        var play = new Run(ReadNote("1>2[1]"));
        play.Step(2, ("A2", true));
        play.Step(3);
        Assert.Empty(play.Judgements.Trace);
        play.Step(3.03, ("A1", true));
        play.Step(3.03 + Tick);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Fact]
    public void NonTailAreaCompletesOnReleaseAndTailNeedsOnlyOn()
    {
        var play = new Run(ReadNote("1>2[1]"));
        play.Step(2.5, ("A1", true));
        Assert.Equal(2, play.Judgements.Trace[^1].RemainingAreas);
        play.Step(2.6, ("A1", false));
        Assert.Equal(1, play.Judgements.Trace[^1].RemainingAreas);
        play.Step(3.035 - Tick, ("A2", true));
        Assert.Equal(0, play.Judgements.Trace[^1].RemainingAreas);
        Assert.DoesNotContain(play.Results, result => result.IsFinal);
        play.Step(3.035);
        Assert.Single(play.Results, result => result.IsFinal);
    }

    [Fact]
    public void CheckingOpensFiftyMillisecondsBeforeTheHeadAndIgnoresButtonInput()
    {
        var play = new Run(ReadNote("1>2[1]"));
        play.Step(1.949, ("A1", true));
        Assert.Empty(play.Results);
        play.Step(1.95);
        Assert.Single(play.Results);
        play = new Run(ReadNote("1>2[1]"));
        play.Inputs.Sample([new(2, InputChannel.Button, "1", true), new(2, InputChannel.Button, "2", true)]);
        play.Judgements.Process(2, play.Inputs, play.Results);
        play.Step(4.101);
        Assert.Equal(AchievementGrade.Miss, Assert.Single(play.Results).Grade);
    }

    [Theory]
    [InlineData(false, AchievementGrade.Miss)]
    [InlineData(true, AchievementGrade.LateGood)]
    public void DeadlineDistinguishesOneRemainingAreaFromAnIncompleteRoute(bool nearTail, AchievementGrade expected)
    {
        var play = new Run(ReadNote("1-5[1]"));
        play.Step(2.5, ("B1", true));
        play.Step(2.6, ("B1", false));
        if (nearTail)
        {
            play.Step(2.7, ("B5", true));
            play.Step(2.8, ("B5", false));
        }
        play.Step(4.1);
        Assert.DoesNotContain(play.Results, e => e.IsFinal);
        play.Step(4.1 + Tick);
        var result = Assert.Single(play.Results, e => e.IsFinal);
        Assert.Equal(expected, result.Grade);
        Assert.Null(result.DifferenceSeconds);
        play.Step(5);
        Assert.Single(play.Results, e => e.IsFinal);
    }

    [Theory]
    [InlineData(-.6, AchievementGrade.FastGood)]
    [InlineData(-.4, AchievementGrade.FastGreat2nd)]
    [InlineData(0, AchievementGrade.Perfect)]
    [InlineData(.4, AchievementGrade.LateGreat2nd)]
    [InlineData(.6, AchievementGrade.LateGood)]
    public void ActualRouteCompletionDeterminesEarlyAndLateGrades(double difference, AchievementGrade expected)
    {
        var play = new Run(ReadNote("1-5[1]"));
        play.Route("A1 B1 C B5 A5".Split(' '), 3.348 + difference);
        var result = Assert.Single(play.Results, e => e.IsFinal);
        Assert.Equal(expected, result.Grade);
        Assert.Equal(difference, result.DifferenceSeconds!.Value, 9);
    }

    [Theory]
    [InlineData(.152, .27133333333333337, AchievementGrade.Perfect, AchievementGrade.LateGreat)]
    [InlineData(.152, .35, AchievementGrade.LateGreat, AchievementGrade.LateGreat2nd)]
    [InlineData(.152, .4166666666666667, AchievementGrade.LateGreat2nd, AchievementGrade.LateGreat3rd)]
    [InlineData(.152, .48333333333333334, AchievementGrade.LateGreat3rd, AchievementGrade.LateGood)]
    [InlineData(3, .6, AchievementGrade.Perfect, AchievementGrade.LateGood)]
    public void ModernGradeWindowsIncludeTheBoundary(double tail, double boundary, AchievementGrade inside, AchievementGrade outside)
    {
        Assert.Equal(inside, SlideJudgements.Grade(boundary - 1e-9, tail));
        Assert.Equal(inside, SlideJudgements.Grade(boundary, tail));
        Assert.Equal(outside, SlideJudgements.Grade(boundary + 1e-9, tail));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WifiNeedsAllThreeQueuesAndAcceptsItsOuterAlternativeSensors(bool alternate)
    {
        var play = new Run(ReadNote("1w5[1]"));
        play.Step(2.5, ("A1", true));
        play.Step(2.6, ("A1", false), ("B1", true), ("B2", true), ("B8", true));
        play.Step(2.7, ("B1", false), ("B2", false), ("B8", false));
        play.Step(2.8, ("C", true), ("B3", true), ("B7", true));
        play.Step(2.9, ("C", false), ("B3", false), ("B7", false));
        play.Step(3.1, (alternate ? "B5" : "A5", true));
        play.Step(3.2, (alternate ? "D5" : "A4", true));
        Assert.DoesNotContain(play.Results, e => e.IsFinal);
        play.Step(3.33713 - Tick, (alternate ? "D6" : "A6", true));
        play.Step(3.33713);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Fact]
    public void CompletingOnlyWifiCentreStillMisses()
    {
        var play = new Run(ReadNote("1w5[1]"));
        play.Route("A1 B1 C A5".Split(' '), 3.33713);
        play.Step(4.2);
        Assert.Equal(AchievementGrade.Miss, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void WifiRotationPreservesAllThreeTailAlternatives(int rotation)
    {
        var original = ReadNote("1w5[1]");
        var segment = new SlideSegment("w", rotation + 1, RotateButton(5, rotation));
        var play = new Run(original with { SlidePath = new SlidePath([segment], [1]) });
        void Step(double time, params (string Area, bool Down)[] changes) =>
            play.Step(time, changes.Select(change => (RotateArea(change.Area, rotation), change.Down)).ToArray());
        Step(2.5, ("B1", true), ("B2", true), ("B8", true));
        Step(2.6, ("B1", false), ("B2", false), ("B8", false));
        Step(2.7, ("C", true), ("B3", true), ("B7", true));
        Step(2.8, ("C", false), ("B3", false), ("B7", false));
        Step(3.33713 - Tick, ("B5", true), ("D5", true), ("D6", true));
        Step(3.33713);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Fact]
    public void WifiWithThreeUnfinishedTailsGetsLateGoodAtDeadline()
    {
        var play = new Run(ReadNote("1w5[1]"));
        play.Step(2.5, ("B1", true), ("B2", true), ("B8", true));
        play.Step(2.6, ("B1", false), ("B2", false), ("B8", false));
        play.Step(2.7, ("C", true), ("B3", true), ("B7", true));
        play.Step(2.8, ("C", false), ("B3", false), ("B7", false));
        play.Step(4.101);
        Assert.Equal(AchievementGrade.LateGood, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Fact]
    public void AnOrAreaRequiresTheReleaseOfASensorThatWasActuallyPressed()
    {
        var play = new Run(ReadNote("1-3[1]"));
        play.Step(2.5, ("B2", true));
        play.Step(2.6, ("A2", false));
        Assert.Equal(2, play.Judgements.Trace[^1].RemainingAreas);
        play.Step(2.7, ("B2", false));
        Assert.Equal(1, play.Judgements.Trace[^1].RemainingAreas);
    }

    [Fact]
    public void ConnectionPassesProgressToChildAndSettlesOnlyOnce()
    {
        var play = new Run(ReadNote("1-3[.5]-5[.5]"));
        play.Step(2.5, ("B2", true));
        play.Step(2.6, ("B2", false));
        play.Step(2.7); // 子段在下一采样开放；父段只剩连接点 A3。
        play.Step(2.8, ("B4", true));
        Assert.Contains(play.Judgements.Trace, e => e.Segment == 0 && e.Reason == "后续连接段已触发");
        Assert.DoesNotContain(play.Results, e => e.IsFinal);
        play.Step(3.409 - Tick, ("A5", true));
        play.Step(3.409);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Fact]
    public void AThreeAreaConnectionRetainsItsMandatoryJoint()
    {
        var play = new Run(ReadNote("1>2[.5]>3[.5]"));
        play.Step(2.2, ("A2", true)); // 头段允许跳过起点 A1。
        play.Step(2.3, ("A2", false));
        play.Step(2.4, ("A3", true));
        play.Step(2.5);
        Assert.DoesNotContain(play.Results, e => e.IsFinal);
        play.Step(3.2675 - Tick, ("A2", true));
        play.Step(3.2675);
        Assert.Equal(AchievementGrade.Perfect, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Fact]
    public void SameHeadBranchesReadTheSameGlobalRegionsWithoutConsumingEachOther()
    {
        var chart = SimaiParser.ParseChart("(120){4}1-3[1]*-5[1],E", 2);
        var play = new Run(chart);
        play.Step(2.5, ("B2", true), ("B1", true));
        play.Step(2.6, ("B2", false), ("B1", false));
        play.Step(2.7, ("B5", true));
        play.Step(3.32, ("A3", true), ("A5", true));
        play.Step(3.32 + Tick);
        var results = play.Results.Where(e => e.IsFinal).ToArray();
        Assert.Equal(2, results.Length);
        Assert.All(results, e => Assert.Equal(AchievementGrade.Perfect, e.Grade));
        Assert.Equal(2, results.Select(e => e.Note.Id).Distinct().Count());
    }

    [Fact]
    public void HeldTailFromOtherActivityFinishesAsSoonAsTheQueueReachesIt()
    {
        var play = new Run(ReadNote("1-3[1]"));
        play.Step(2, ("A3", true));
        play.Step(2.01, ("B2", true));
        play.Step(2.01 + Tick);
        Assert.Equal(AchievementGrade.FastGood, Assert.Single(play.Results, e => e.IsFinal).Grade);
    }

    [Fact]
    public void RestartClearsAreaHistoryProgressAndTrace()
    {
        var note = ReadNote("1-3[1]");
        var play = new Run(note);
        play.Route("A1 B2 A3".Split(' '), 3.318);
        play.Judgements.Reset();
        play.Inputs.Reset();
        play.Results.Clear();
        Assert.Empty(play.Judgements.Trace);
        Assert.Equal(0, play.Judgements.GetProgress(note));
        play.Step(4.2);
        Assert.Equal(AchievementGrade.Miss, Assert.Single(play.Results).Grade);
    }

    private static ChartNote ReadNote(string syntax) =>
        Assert.Single(SimaiParser.ParseChart($"(120){{4}}{syntax},E", 2).Notes, note => note.Kind == NoteKind.Slide);

    private static int RotateButton(int button, int rotation) => (button - 1 + rotation) % 8 + 1;
    private static int MirrorButton(int button) => (9 - button) % 8 + 1;
    private static string MirrorArea(string area) => area == "C" ? area : $"{area[0]}{MirrorButton(area[1] - '0')}";
    private static string RotateArea(string area, int rotation) =>
        area == "C" ? area : $"{area[0]}{RotateButton(area[1] - '0', rotation)}";

    private sealed class Run
    {
        public InputState Inputs { get; } = new();
        public List<PlaybackEvent> Results { get; } = [];
        public SlideJudgements Judgements { get; }

        public Run(ChartNote note) : this(new Chart("test", "", "", 1, "", [note], note.EndTime + 1)) { }
        public Run(Chart chart) => Judgements = new(chart);

        public void Step(double time, params (string Area, bool Down)[] changes)
        {
            Inputs.Sample(changes.Select(change => new InputEvent(time, InputChannel.Touch, change.Area, change.Down)));
            Judgements.Process(time, Inputs, Results);
        }

        public void Route(IEnumerable<string> sequence, double judgeAt)
        {
            var route = sequence.ToArray();
            for (var i = 0; i < route.Length; i++)
            {
                var time = judgeAt - Tick - (route.Length - 1 - i) * Tick * 2;
                Step(time, (route[i], true));
                Step(time + Tick, (route[i], false));
            }
        }
    }
}
