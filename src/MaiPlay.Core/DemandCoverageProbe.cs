namespace MaiPlay.Core;

/// <summary>用单音符回放确认既有接触是否直接达到 CP，仅供搜索排序使用。</summary>
internal sealed class DemandCoverageProbe(Chart source)
{
    public bool IsCovered(ChartNote note, IReadOnlyList<HandTrack> tracks) =>
        tracks.Count != 0 && IsCoveredByInputs(note, new HandReplay(tracks).Events);

    public bool IsCoveredByInputs(ChartNote note, IEnumerable<InputEvent> inputs)
    {
        if (note.Kind is not (NoteKind.Tap or NoteKind.Touch)) return false;

        // 平移整数个逻辑采样周期，保留判定相位并避免从整首谱面的起点重放。
        var origin = Math.Floor(note.Time * PlaybackSession.LogicHz) / PlaybackSession.LogicHz;
        var localNote = note with { Id = 0, Time = note.Time - origin };
        var chart = source with { Notes = [localNote], Duration = localNote.EndTime };
        var session = new PlaybackSession(chart, PlayMode.Algorithm,
            inputs.Select(input => input with { Time = input.Time - origin }));
        session.Advance(session.FinishTime);
        return session.GetResult(localNote)?.Grade == AchievementGrade.Perfect;
    }
}
