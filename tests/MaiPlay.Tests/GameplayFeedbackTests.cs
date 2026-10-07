using MaiPlay.Audio;
using MaiPlay.Core;
using MaiPlay.Rendering;

namespace MaiPlay.Tests;

public sealed class GameplayFeedbackTests
{
    [Theory]
    [InlineData(AchievementGrade.Perfect, "tap_perfect.wav", "judge_text_cPerfect.png")]
    [InlineData(AchievementGrade.FastPerfect3rd, "tap_perfect.wav", "judge_text_perfect.png")]
    [InlineData(AchievementGrade.LateGreat2nd, "tap_great.wav", "judge_text_great.png")]
    [InlineData(AchievementGrade.FastGood, "tap_good.wav", "judge_text_good.png")]
    public void TapFeedbackUsesTheActualGrade(AchievementGrade grade, string sound, string sprite)
    {
        var note = new ChartNote { Kind = NoteKind.Tap };
        var result = new PlaybackEvent(PlaybackEvent.KindFor(grade), note, 1) { Grade = grade };
        Assert.Equal([sound], SoundEffects.GetOneShotClips(result));
        Assert.Equal($"JudgeTextSkins/{sprite}", JudgementDisplay.JudgeSprite(note, grade));
    }

    [Theory]
    [InlineData(AchievementGrade.Perfect, 2600, true)]
    [InlineData(AchievementGrade.FastPerfect2nd, 2550, false)]
    [InlineData(AchievementGrade.LatePerfect3rd, 2500, false)]
    [InlineData(AchievementGrade.FastGreat, 2000, false)]
    [InlineData(AchievementGrade.LateGreat2nd, 1500, false)]
    [InlineData(AchievementGrade.FastGreat3rd, 1250, false)]
    [InlineData(AchievementGrade.LateGood, 1000, false)]
    public void BreakNumberAndExtraSoundFollowUpstreamSubgrade(AchievementGrade grade, int score, bool extra)
    {
        var note = new ChartNote { Kind = NoteKind.Tap, IsBreak = true };
        var result = new PlaybackEvent(PlaybackEvent.KindFor(grade), note, 1) { Grade = grade };
        Assert.Equal($"JudgeTextSkins/judge_text_break_{score}.png", JudgementDisplay.JudgeSprite(note, grade));
        Assert.Equal(extra ? new[] { "break_tap.wav", "break.wav" } : ["break_tap.wav"], SoundEffects.GetOneShotClips(result));
        var slideResult = result with { Note = note with { Kind = NoteKind.Slide } };
        Assert.Equal(extra ? new[] { "slide_break_slide.wav", "break.wav" } : [], SoundEffects.GetOneShotClips(slideResult));
    }

    [Fact]
    public void LongNoteTailsUseOrdinaryGradeSoundAndManualTouchHoldHeadOnlyStartsRiser()
    {
        var note = new ChartNote { Kind = NoteKind.TouchHold, IsBreak = true, IsEx = true, IsFirework = true };
        var head = new PlaybackEvent(PlaybackEventKind.Started, note, 1) { TimingKind = "head" };
        Assert.Empty(SoundEffects.GetOneShotClips(head));
        var end = new PlaybackEvent(PlaybackEventKind.Great, note, 2) { Grade = AchievementGrade.LateGreat };
        Assert.Equal(new[] { "tap_great.wav", "touch_hanabi.wav" }, SoundEffects.GetOneShotClips(end));
        Assert.Equal(["tap_great.wav"], SoundEffects.GetOneShotClips(end with { Note = note with { Kind = NoteKind.Hold } }));
    }

    [Theory]
    [InlineData(AchievementGrade.Miss)]
    [InlineData(AchievementGrade.TooFast)]
    public void FailedNotesDoNotTriggerFireworksOrHitSounds(AchievementGrade grade)
    {
        foreach (var kind in Enum.GetValues<NoteKind>())
        {
            var result = new PlaybackEvent(PlaybackEventKind.Miss, new ChartNote { Kind = kind, IsBreak = true, IsFirework = true }, 1)
                { Grade = grade };
            Assert.Empty(SoundEffects.GetOneShotClips(result));
            Assert.Equal("", JudgementDisplay.Timing(result));
        }
    }

    [Theory]
    [InlineData(AchievementGrade.FastPerfect2nd, "just_str_r_fast_p.png")]
    [InlineData(AchievementGrade.LatePerfect3rd, "just_str_r_late_p.png")]
    [InlineData(AchievementGrade.FastGreat2nd, "just_str_r_fast_gr.png")]
    [InlineData(AchievementGrade.LateGood, "just_str_r_late_gd.png")]
    [InlineData(AchievementGrade.Miss, "miss_str_r.png")]
    [InlineData(AchievementGrade.TooFast, "toofast_str_r.png")]
    public void SlideFeedbackUsesDirectionAndGrade(AchievementGrade grade, string sprite)
    {
        Assert.Equal($"SlideOKSkins/{sprite}", JudgementDisplay.SlideSprite("str_r", grade));
    }

    [Fact]
    public void TimingLabelsUseActualDifferenceAndBelowCriticalPerfectPolicy()
    {
        var result = new PlaybackEvent(PlaybackEventKind.Perfect, new ChartNote(), 1)
            { Grade = AchievementGrade.FastPerfect2nd, DifferenceSeconds = -.025 };
        Assert.Equal("FAST", JudgementDisplay.Timing(result));
        Assert.Equal("LATE", JudgementDisplay.Timing(result with { DifferenceSeconds = .025 }));
        Assert.Equal("", JudgementDisplay.Timing(result with { Grade = AchievementGrade.Perfect }));
        Assert.Equal("LATE", JudgementDisplay.Timing(result with { DifferenceSeconds = null }));
    }
}
