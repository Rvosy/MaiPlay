using PointFrame = MaiPlay.Core.ContactPoint;

namespace MaiPlay.Core;

internal sealed record ContactPoint(double Time, ChartPoint Position);
internal sealed record ContactMotion(ChartNote Note, InputChannel Channel, ContactPoint[] Points);

internal static class ContactTrajectoryCompiler
{
    private const double Pulse = .025;
    internal static IEnumerable<ContactMotion> CreateMotions(Chart chart, CancellationToken token)
    {
        foreach (var note in chart.Notes)
        {
            token.ThrowIfCancellationRequested();
            if (note.Kind != NoteKind.Slide)
            {
                var channel = note.Kind is NoteKind.Tap or NoteKind.Hold ? InputChannel.Button : InputChannel.Touch;
                var p = channel == InputChannel.Button ? SlidePath.ButtonPosition(note.StartPosition, 1.12) : TouchCentre(note.TouchArea);
                var end = note.Kind is NoteKind.Hold or NoteKind.TouchHold
                    ? Math.Max(note.Time + Pulse, note.EndTime - Math.Min(.04, note.Duration * .1)) : note.Time + Pulse;
                yield return new(note, channel, [new(note.Time, p), new(end, p)]);
                continue;
            }
            if (note.SlidePath!.IsWifi)
            {
                // Wifi 的显示直线不经过全部判定区。双手分别扫过两侧 B 区，
                // 一手同时经过中路，不能把三条显示线直接当作接触轨迹。
                foreach (var branch in new[] { 1, 2 })
                {
                    SensorArea[] areas = branch == 1
                        ? [SensorArea.A1, SensorArea.B1, SensorArea.B2, SensorArea.C, SensorArea.B3, SensorArea.B5, SensorArea.A4]
                        : [SensorArea.A1, SensorArea.B8, SensorArea.B7, SensorArea.A6];
                    var start = Math.Max(note.Time + .05, note.SlideStartTime - .12);
                    var end = note.EndTime + .025;
                    var points = areas.Select((area, i) => new PointFrame(start + (end - start) * i / (areas.Length - 1),
                        TouchCentre(SlideJudgements.Transform(area, note.StartPosition, false).ToString())))
                        .Append(new PointFrame(end + PlaybackSession.LogicStep, TouchCentre(
                            SlideJudgements.Transform(areas[^1], note.StartPosition, false).ToString()))).ToArray();
                    yield return new(note, InputChannel.Touch, points);
                }
            }
            else
            {
                var points = SlidePoints(note, note.Duration);
                // 末区完成后释放，给后续击打保留移动时间；完成时刻仍由判定器决定。
                var result = JudgeSlide(chart, note, points);
                if (result.Grade != AchievementGrade.Perfect && result.DifferenceSeconds is { } difference && result.Time > note.SlideStartTime)
                {
                    // 宽接触面可能让慢速轨道过早进入末区，保持起点时刻并调整滑动速度。
                    var scale = (result.Time - note.SlideStartTime - difference) / (result.Time - note.SlideStartTime);
                    var adjusted = points.Select(p => p with { Time = note.SlideStartTime + (p.Time - note.SlideStartTime) * scale }).ToArray();
                    if (scale > 0 && JudgeSlide(chart, note, adjusted) is { Grade: AchievementGrade.Perfect } corrected)
                    {
                        points = adjusted;
                        result = corrected;
                    }
                }
                if (result.Grade == AchievementGrade.Perfect && result.Time < points[^1].Time)
                {
                    var after = Array.FindIndex(points, p => p.Time >= result.Time);
                    var before = Math.Max(0, after - 1);
                    var position = after == before ? points[after].Position : ChartPoint.Lerp(points[before].Position, points[after].Position,
                        (result.Time - points[before].Time) / (points[after].Time - points[before].Time));
                    points = points.Where(p => p.Time < result.Time).Append(new PointFrame(result.Time, position)).ToArray();
                }
                yield return new(note, InputChannel.Touch, points);
            }
        }
    }

    private static PlaybackEvent JudgeSlide(Chart chart, ChartNote note, PointFrame[] points)
    {
        var track = new HandTrack(HandSide.Left, ContactFrames(points, InputChannel.Touch, 0));
        var replay = new HandReplay([track]);
        var single = chart with { Notes = [note with { Id = 0 }], Duration = note.EndTime + .7 };
        var session = new PlaybackSession(single, PlayMode.Algorithm, replay.Events);
        return session.Advance(note.EndTime + .7).Single(e => e.IsFinal);
    }

    internal static PointFrame[] SlidePoints(ChartNote note, double duration)
    {
        var count = Math.Max(2, (int)Math.Ceiling(duration * PlaybackSession.LogicHz));
        return Enumerable.Range(0, count + 1).Select(i =>
            new PointFrame(note.SlideStartTime + duration * i / count, note.SlidePath!.PointAt((double)i / count))).ToArray();
    }

    internal static ChartPoint TouchCentre(string area)
    {
        var points = ContactMapper.SensorPolygons.Where(p => p.Area.ToString() == area).SelectMany(p => p.Points).ToArray();
        return new(points.Average(p => p.X), points.Average(p => p.Y));
    }

    internal static HandFrame[] ContactFrames(PointFrame[] points, InputChannel channel, double shift) =>
        points.Select((p, i) => new HandFrame(p.Time + shift, p.Position,
            i == points.Length - 1 ? [] : Fingers(p.Position, channel))).ToArray();

    internal static HandContact[] Fingers(ChartPoint position, InputChannel channel)
    {
        var angle = Math.Atan2(position.Y, position.X) + Math.PI / 2;
        return new[] { -.075, -.025, .025, .075 }.Select((d, i) =>
            new HandContact($"finger{i + 1}", channel, new(d * Math.Cos(angle), d * Math.Sin(angle)), .035)).ToArray();
    }
}
