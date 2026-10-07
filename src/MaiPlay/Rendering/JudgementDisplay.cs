using MaiPlay.Core;

namespace MaiPlay.Rendering;

// Grade-to-sprite mappings follow MajdataPlay dc19722, JudgeTextDisplayer and SlideOK (GPL-3.0).
public static class JudgementDisplay
{
    public static string Family(AchievementGrade grade) => grade == AchievementGrade.Perfect ? "CP"
        : PlaybackEvent.KindFor(grade) switch
        {
            PlaybackEventKind.Perfect => "PERFECT",
            PlaybackEventKind.Great => "GREAT",
            PlaybackEventKind.Good => "GOOD",
            _ => "MISS"
        };

    public static string Label(AchievementGrade grade) => grade switch
    {
        AchievementGrade.Perfect => "CRITICAL PERFECT",
        AchievementGrade.FastPerfect2nd or AchievementGrade.LatePerfect2nd => "PERFECT 2nd",
        AchievementGrade.FastPerfect3rd or AchievementGrade.LatePerfect3rd => "PERFECT 3rd",
        AchievementGrade.FastGreat2nd or AchievementGrade.LateGreat2nd => "GREAT 2nd",
        AchievementGrade.FastGreat3rd or AchievementGrade.LateGreat3rd => "GREAT 3rd",
        AchievementGrade.TooFast => "TOO FAST",
        _ => Family(grade)
    };

    public static string Timing(PlaybackEvent result) => Family(result.Grade) is "CP" or "MISS" ? ""
        : result.IsFast ? "FAST" : result.IsLate ? "LATE" : "";

    public static string JudgeSprite(ChartNote note, AchievementGrade grade)
    {
        if (note.IsBreak)
        {
            var score = grade switch
            {
                AchievementGrade.Perfect => 2600,
                AchievementGrade.FastPerfect2nd or AchievementGrade.LatePerfect2nd => 2550,
                AchievementGrade.FastPerfect3rd or AchievementGrade.LatePerfect3rd => 2500,
                AchievementGrade.FastGreat or AchievementGrade.LateGreat => 2000,
                AchievementGrade.FastGreat2nd or AchievementGrade.LateGreat2nd => 1500,
                AchievementGrade.FastGreat3rd or AchievementGrade.LateGreat3rd => 1250,
                AchievementGrade.FastGood or AchievementGrade.LateGood => 1000,
                _ => 0
            };
            return $"JudgeTextSkins/judge_text_break_{score}.png";
        }
        var name = grade == AchievementGrade.Perfect ? "cPerfect" : Family(grade).ToLowerInvariant();
        return $"JudgeTextSkins/judge_text_{name}.png";
    }

    public static string SlideSprite(string shape, AchievementGrade grade) => grade switch
    {
        AchievementGrade.Perfect => $"SlideOKSkins/just_{shape}.png",
        AchievementGrade.FastPerfect2nd or AchievementGrade.FastPerfect3rd => $"SlideOKSkins/just_{shape}_fast_p.png",
        AchievementGrade.LatePerfect2nd or AchievementGrade.LatePerfect3rd => $"SlideOKSkins/just_{shape}_late_p.png",
        AchievementGrade.FastGreat or AchievementGrade.FastGreat2nd or AchievementGrade.FastGreat3rd => $"SlideOKSkins/just_{shape}_fast_gr.png",
        AchievementGrade.LateGreat or AchievementGrade.LateGreat2nd or AchievementGrade.LateGreat3rd => $"SlideOKSkins/just_{shape}_late_gr.png",
        AchievementGrade.FastGood => $"SlideOKSkins/just_{shape}_fast_gd.png",
        AchievementGrade.LateGood => $"SlideOKSkins/just_{shape}_late_gd.png",
        AchievementGrade.TooFast => $"SlideOKSkins/toofast_{shape}.png",
        _ => $"SlideOKSkins/miss_{shape}.png"
    };
}
