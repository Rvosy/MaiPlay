using MaiPlay.Core;
using static MaiPlay.Core.ContactTrajectoryCompiler;

namespace MaiPlay.Tests;

public sealed class ContactTrajectoryCompilerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(.06)]
    [InlineData(.12)]
    public void ClippedSlideKeepsStrictFrameOrderAndPerfectJudgementAfterTimingShift(double shift)
    {
        var chart = SimaiParser.ParseChart("(150){4},,,,,,,,4$<8[0.4##0.4],,8,E");
        var motion = Assert.Single(CreateMotions(chart, CancellationToken.None), m => m.Note.Kind == NoteKind.Slide);
        var frames = ContactFrames(motion.Points, motion.Channel, shift);
        var replay = new HandReplay([new(HandSide.Left, frames)]);
        var slideChart = chart with { Notes = [motion.Note with { Id = 0 }] };
        var session = new PlaybackSession(slideChart, PlayMode.Algorithm, replay.Events);
        var result = Assert.Single(session.Advance(session.FinishTime + .1), e => e.IsFinal);
        Assert.Equal(AchievementGrade.Perfect, result.Grade);
    }
}
