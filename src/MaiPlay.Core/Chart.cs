namespace MaiPlay.Core;

public enum NoteKind { Tap, Hold, Slide, Touch, TouchHold }
public enum PlayMode { AutoPlay, Algorithm }
public enum NoteState { Pending, Active, Perfect, Miss, Great, Good }
public enum PlaybackEventKind { Started, Perfect, Miss, Great, Good, Held, Released }

/// <summary>Screen coordinates: centre (0,0), judgement ring radius 1, positive Y down.</summary>
public readonly record struct ChartPoint(double X, double Y)
{
    public static ChartPoint Lerp(ChartPoint a, ChartPoint b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    public double DistanceTo(ChartPoint other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}

public sealed record ChartNote
{
    public int Id { get; init; }
    public NoteKind Kind { get; init; }
    /// <summary>Absolute music time in seconds, including &amp;first.</summary>
    public double Time { get; init; }
    public double Duration { get; init; }
    public double SlideStartTime { get; init; }
    public double EndTime => Kind == NoteKind.Slide ? SlideStartTime + Duration : Time + Duration;
    /// <summary>Simai button number, 1–8.</summary>
    public int StartPosition { get; init; } = 1;
    public int EndPosition { get; init; } = 1;
    public string TouchArea { get; init; } = "";
    public bool IsBreak { get; init; }
    public bool IsEx { get; init; }
    public bool IsEach { get; init; }
    public bool IsStar { get; init; }
    public bool IsDoubleStar { get; init; }
    public bool IsSpinningStar { get; init; }
    /// <summary>Upstream Unity degrees/second; screen rotation uses the opposite sign.</summary>
    public double StarRotateSpeed { get; init; }
    public bool IsFirework { get; init; }
    public bool IsHeadless { get; init; }
    public bool IsSuddenSlide { get; init; }
    public SlidePath? SlidePath { get; init; }
    public string Source { get; init; } = "";
}

public sealed record Chart(string Title, string Artist, string Designer, int Difficulty, string Level,
    IReadOnlyList<ChartNote> Notes, double Duration)
{
    public string DifficultyName => Difficulty switch
    {
        1 => "Easy", 2 => "Basic", 3 => "Advanced", 4 => "Expert", 5 => "Master", 6 => "Re:Master", 7 => "Original", _ => $"Level {Difficulty}"
    };
}

public sealed record Song(string Directory, string ChartPath, string? AudioPath, string Title,
    string Artist, IReadOnlyList<Chart> Charts);

public sealed record PlaybackEvent(PlaybackEventKind Kind, ChartNote Note, double Time)
{
    public AchievementGrade Grade { get; init; } = Kind == PlaybackEventKind.Miss ? AchievementGrade.Miss : AchievementGrade.Perfect;
    public double? DifferenceSeconds { get; init; }
    public string TimingKind { get; init; } = "ideal";
    public string Reason { get; init; } = "";
    public bool IsFinal => Kind is PlaybackEventKind.Perfect or PlaybackEventKind.Great or PlaybackEventKind.Good or PlaybackEventKind.Miss;
    public bool IsFast => DifferenceSeconds < 0;
    public bool IsLate => !IsFast && Grade is not (AchievementGrade.Perfect or AchievementGrade.Miss or AchievementGrade.TooFast);

    public static PlaybackEventKind KindFor(AchievementGrade grade) => grade switch
    {
        AchievementGrade.Miss or AchievementGrade.TooFast => PlaybackEventKind.Miss,
        AchievementGrade.FastGood or AchievementGrade.LateGood => PlaybackEventKind.Good,
        AchievementGrade.FastGreat or AchievementGrade.FastGreat2nd or AchievementGrade.FastGreat3rd or
            AchievementGrade.LateGreat or AchievementGrade.LateGreat2nd or AchievementGrade.LateGreat3rd => PlaybackEventKind.Great,
        _ => PlaybackEventKind.Perfect
    };
}

public sealed class ChartParseException(string message) : FormatException(message);
