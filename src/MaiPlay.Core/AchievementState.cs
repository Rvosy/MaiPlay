using System.Globalization;

namespace MaiPlay.Core;

public enum AchievementGrade
{
    Miss, LateGood, LateGreat3rd, LateGreat2nd, LateGreat, LatePerfect3rd, LatePerfect2nd,
    Perfect, FastPerfect2nd, FastPerfect3rd, FastGreat, FastGreat2nd, FastGreat3rd, FastGood, TooFast
}

/// <summary>DX 达成率的减分显示。每个音符的最终判定由调用方提交一次。</summary>
public sealed class AchievementState
{
    public long TotalBaseScore { get; }
    public long TotalExtraScore { get; }
    public long LostBaseScore { get; private set; }
    public long LostExtraScore { get; private set; }

    public decimal Percentage => TotalBaseScore == 0 ? 100m :
        100m * (TotalBaseScore - LostBaseScore) / TotalBaseScore +
        (TotalExtraScore == 0 ? 0m : (decimal)(TotalExtraScore - LostExtraScore) / TotalExtraScore);

    public string DisplayText =>
        (decimal.Floor(Percentage * 10000) / 10000).ToString("F4", CultureInfo.InvariantCulture) + "%";

    public AchievementState(Chart chart)
    {
        TotalBaseScore = chart.Notes.Sum(note => (long)BaseScore(note));
        TotalExtraScore = chart.Notes.Count(note => note.IsBreak) * 100L;
    }

    public void Apply(ChartNote note, AchievementGrade grade)
    {
        if (note.IsBreak)
        {
            (int Base, int Extra) loss = grade switch
            {
                AchievementGrade.Perfect => (0, 0),
                AchievementGrade.FastPerfect2nd or AchievementGrade.LatePerfect2nd => (0, 25),
                AchievementGrade.FastPerfect3rd or AchievementGrade.LatePerfect3rd => (0, 50),
                AchievementGrade.FastGreat or AchievementGrade.LateGreat => (500, 60),
                AchievementGrade.FastGreat2nd or AchievementGrade.LateGreat2nd => (1000, 60),
                AchievementGrade.FastGreat3rd or AchievementGrade.LateGreat3rd => (1250, 60),
                AchievementGrade.FastGood or AchievementGrade.LateGood => (1500, 70),
                AchievementGrade.Miss or AchievementGrade.TooFast => (2500, 100),
                _ => throw new ArgumentOutOfRangeException(nameof(grade))
            };
            LostBaseScore += loss.Base;
            LostExtraScore += loss.Extra;
        }
        else
        {
            var score = BaseScore(note);
            LostBaseScore += grade switch
            {
                AchievementGrade.Perfect or AchievementGrade.FastPerfect2nd or AchievementGrade.LatePerfect2nd
                    or AchievementGrade.FastPerfect3rd or AchievementGrade.LatePerfect3rd => 0,
                AchievementGrade.FastGreat or AchievementGrade.LateGreat or AchievementGrade.FastGreat2nd
                    or AchievementGrade.LateGreat2nd or AchievementGrade.FastGreat3rd or AchievementGrade.LateGreat3rd => score / 5,
                AchievementGrade.FastGood or AchievementGrade.LateGood => score / 2,
                AchievementGrade.Miss or AchievementGrade.TooFast => score,
                _ => throw new ArgumentOutOfRangeException(nameof(grade))
            };
        }
    }

    public void Reset() => LostBaseScore = LostExtraScore = 0;

    private static int BaseScore(ChartNote note) => note.IsBreak ? 2500 : note.Kind switch
    {
        NoteKind.Tap or NoteKind.Touch => 500,
        NoteKind.Hold or NoteKind.TouchHold => 1000,
        NoteKind.Slide => 1500,
        _ => throw new ArgumentOutOfRangeException(nameof(note))
    };
}
