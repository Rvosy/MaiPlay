namespace MaiPlay.Core;

public enum HandTechnique { Strike, Slide, Rotation }
public sealed record PlannedStroke(int NoteId, HandSide Hand, double Start, double End, InputChannel Channel,
    HandTechnique Technique = HandTechnique.Strike);
public sealed record HandPlanGap(int NoteId, double Time, double EndTime, string Reason);
public sealed record PatternOutcome(PatternKind Kind, int Detected, int Solved);
public sealed record CoveredDemand(int DemandId, IReadOnlyList<int> ActionDemandIds);
public sealed record HumanPlanDiagnostics(HumanMotionMetrics Movement, double MovementCost,
    IReadOnlyList<PatternOutcome> Patterns, IReadOnlyDictionary<string, int> TechniqueUsage)
{
    public IReadOnlyList<SlideConnectionOutcome> SlideConnections { get; init; } = [];
    public IReadOnlyList<CoveredDemand> CoveredDemands { get; init; } = [];
}

public sealed record HandPlan(Chart Chart, IReadOnlyList<HandTrack> Tracks, HandReplay Replay,
    IReadOnlyList<PlannedStroke> Strokes, IReadOnlyList<PlaybackEvent> Results, decimal Achievement, int DXScore,
    IReadOnlyList<HandPlanGap> Gaps)
{
    public bool AllCriticalPerfect => Results.Count == Chart.Notes.Count && Results.All(e => e.Grade == AchievementGrade.Perfect);
    public IReadOnlyList<PlaybackEvent> Errors => Results.Where(e => e.Grade != AchievementGrade.Perfect).ToArray();
    public HumanPlanDiagnostics? Diagnostics { get; init; }
    /// <summary>Complete allocation within the configured planar motion model, not proof of human playability.</summary>
    public bool IsExecutable => Gaps.Count == 0 && Results.Count == Chart.Notes.Count &&
        Diagnostics?.Movement.SatisfiesMotionConstraints == true;
    public bool IsSolved => IsExecutable && AllCriticalPerfect;
}
