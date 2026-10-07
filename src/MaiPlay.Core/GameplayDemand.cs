namespace MaiPlay.Core;

/// <summary>谱面要求的输入；时间不包含具体打法的提前、延后和释放调整。</summary>
public sealed record GameplayDemand(int Id, ChartNote Note, InputChannel Channel, double Start, double End, string Target);

public static class GameplayDemandCompiler
{
    public static IReadOnlyList<GameplayDemand> Compile(Chart chart, CancellationToken cancellationToken = default)
    {
        var demands = new List<GameplayDemand>(chart.Notes.Count);
        foreach (var note in chart.Notes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = note.Kind is NoteKind.Tap or NoteKind.Hold ? InputChannel.Button : InputChannel.Touch;
            var start = note.Kind == NoteKind.Slide ? note.SlideStartTime : note.Time;
            var target = channel == InputChannel.Button ? note.StartPosition.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : note.Kind == NoteKind.Slide ? $"A{note.StartPosition}" : note.TouchArea;
            demands.Add(new(note.Id, note, channel, start, note.EndTime, target));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return demands.OrderBy(d => d.Start).ThenBy(d => d.Id).ToArray();
    }
}
