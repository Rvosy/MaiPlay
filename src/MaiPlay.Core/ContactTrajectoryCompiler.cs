using PointFrame = MaiPlay.Core.ContactPoint;

namespace MaiPlay.Core;

internal sealed record ContactPoint(double Time, ChartPoint Position, ChartPoint? ContactAxis = null);
internal enum ContactProfile { Palm, Fingertips }
internal sealed record ContactMotion(ChartNote Note, InputChannel Channel, ContactPoint[] Points,
    ContactProfile Profile = ContactProfile.Palm);

internal static class ContactTrajectoryCompiler
{
    private const double Pulse = .025;
    // 以掌面圆近似整掌接触，默认机台比例下直径约 89.4 mm。
    private const double PalmRadius = .19;
    // 横放手掌用三个等半径圆近似，长约 178.8 mm、宽约 89.4 mm；两手内端相接。
    private const double WifiPalmHalfAxis = .19;
    private const double WifiHandOffset = PalmRadius + WifiPalmHalfAxis;
    internal static IEnumerable<ContactMotion> CreateMotions(Chart chart, CancellationToken token,
        IReadOnlySet<int>? fingertipNotes = null)
    {
        foreach (var note in chart.Notes)
        {
            token.ThrowIfCancellationRequested();
            var profile = note.Kind is NoteKind.Tap or NoteKind.Hold || fingertipNotes?.Contains(note.Id) == true
                ? ContactProfile.Fingertips : ContactProfile.Palm;
            if (note.Kind != NoteKind.Slide)
            {
                var channel = note.Kind is NoteKind.Tap or NoteKind.Hold ? InputChannel.Button : InputChannel.Touch;
                var p = channel == InputChannel.Button ? SlidePath.ButtonPosition(note.StartPosition, 1.12) : TouchCentre(note.TouchArea);
                var end = note.Kind is NoteKind.Hold or NoteKind.TouchHold
                    ? Math.Max(note.Time + Pulse, note.EndTime - Math.Min(.04, note.Duration * .1)) : note.Time + Pulse;
                yield return new(note, channel, [new(note.Time, p), new(end, p)], profile);
                continue;
            }
            if (note.SlidePath!.IsWifi)
            {
                foreach (var points in WifiPoints(note, profile))
                    yield return new(note, InputChannel.Touch, points, profile);
            }
            else
            {
                var points = SlidePoints(note, note.Duration);
                // 末区完成后释放，给后续击打保留移动时间；完成时刻仍由判定器决定。
                var result = JudgeSlide(chart, note, points, profile);
                if (result.Grade != AchievementGrade.Perfect && result.DifferenceSeconds is { } difference && result.Time > note.SlideStartTime)
                {
                    // 宽接触面可能让慢速轨道过早进入末区，保持起点时刻并调整滑动速度。
                    var scale = (result.Time - note.SlideStartTime - difference) / (result.Time - note.SlideStartTime);
                    var adjusted = points.Select(p => p with { Time = note.SlideStartTime + (p.Time - note.SlideStartTime) * scale }).ToArray();
                    if (scale > 0 && JudgeSlide(chart, note, adjusted, profile) is { Grade: AchievementGrade.Perfect } corrected)
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
                    // 合并浮点误差内的末点，避免整体平移后两个时间戳重合。
                    points = points.Where(p => result.Time - p.Time > 1e-8).Append(new PointFrame(result.Time, position)).ToArray();
                }
                yield return new(note, InputChannel.Touch, points, profile);
            }
        }
    }

    private static IEnumerable<PointFrame[]> WifiPoints(ChartNote note, ContactProfile profile)
    {
        var start = Math.Max(note.Time + .05, note.SlideStartTime - .12);
        var end = note.EndTime + .025;
        if (profile == ContactProfile.Fingertips)
        {
            // 仅在防蹭反馈选中缩手后使用分路窄接触；默认横掌保持双手同步直线下滑。
            foreach (var areas in new SensorArea[][]
            {
                [SensorArea.A1, SensorArea.B1, SensorArea.B2, SensorArea.C, SensorArea.B3, SensorArea.B5, SensorArea.A4],
                [SensorArea.A1, SensorArea.B8, SensorArea.B7, SensorArea.A6]
            })
                yield return areas.Select((area, i) => new PointFrame(start + (end - start) * i / (areas.Length - 1),
                    TouchCentre(SlideJudgements.Transform(area, note.StartPosition, false).ToString())))
                    .Append(new PointFrame(end + PlaybackSession.LogicStep, TouchCentre(
                        SlideJudgements.Transform(areas[^1], note.StartPosition, false).ToString()))).ToArray();
            yield break;
        }
        var down = SlidePath.ButtonPosition(note.EndPosition);
        var axis = new ChartPoint(down.Y, -down.X);
        foreach (var side in new[] { -1, 1 })
        {
            ChartPoint At(double radial) => new(down.X * radial + axis.X * side * WifiHandOffset,
                down.Y * radial + axis.Y * side * WifiHandOffset);
            // 掌面前缘覆盖中路 B 区和两侧末区，手心沿同一轴移动，横向间距与朝向不变。
            yield return [new(start, At(-.8), axis), new(end, At(.35), axis),
                new(end + PlaybackSession.LogicStep, At(.35), axis)];
        }
    }

    private static PlaybackEvent JudgeSlide(Chart chart, ChartNote note, PointFrame[] points, ContactProfile profile)
    {
        var track = new HandTrack(HandSide.Left, ContactFrames(points, InputChannel.Touch, 0, profile));
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

    internal static PointFrame[] HoldSweepPoints(double start, double end, ChartPoint entry, int button, int direction)
    {
        var target = SlidePath.ButtonPosition(button, 1.12);
        if (entry.DistanceTo(target) < 1e-8) return [new(start, entry), new(end, target)];
        var angle = Math.Atan2(entry.Y, entry.X);
        var sweep = ((Math.Atan2(target.Y, target.X) - angle) * direction + 2 * Math.PI) % (2 * Math.PI);
        var arrival = Math.Min(end, start + Pulse);
        var count = Math.Max(2, (int)Math.Ceiling((arrival - start) * PlaybackSession.LogicHz));
        // 扫入长条后继续落到键心，避免一直压住入口旁的外键，遮住另一只手的后续击打。
        var points = Enumerable.Range(0, count + 1).Select(i =>
        {
            var current = angle + direction * sweep * i / count;
            return new PointFrame(start + (arrival - start) * i / count, new(1.12 * Math.Cos(current), 1.12 * Math.Sin(current)));
        }).ToList();
        if (end > arrival) points.Add(new(end, target));
        return points.ToArray();
    }

    internal static ChartPoint TouchCentre(string area)
    {
        var points = ContactMapper.SensorPolygons.Where(p => p.Area.ToString() == area).SelectMany(p => p.Points).ToArray();
        return new(points.Average(p => p.X), points.Average(p => p.Y));
    }

    internal static HandFrame[] ContactFrames(PointFrame[] points, InputChannel channel, double shift,
        ContactProfile profile = ContactProfile.Palm) =>
        points.Select((p, i) => new HandFrame(p.Time + shift, p.Position,
            i == points.Length - 1 ? [] : Contacts(p.Position, channel, profile, p.ContactAxis))).ToArray();

    internal static HandContact[] Contacts(ChartPoint position, InputChannel channel, ContactProfile profile,
        ChartPoint? axis = null) =>
        channel == InputChannel.Touch && profile == ContactProfile.Palm
            ? axis is { } direction
                ? [new("palm", channel, new(0, 0), PalmRadius),
                    new("palm1", channel, new(-direction.X * WifiPalmHalfAxis, -direction.Y * WifiPalmHalfAxis), PalmRadius),
                    new("palm2", channel, new(direction.X * WifiPalmHalfAxis, direction.Y * WifiPalmHalfAxis), PalmRadius)]
                : [new("palm", channel, new(0, 0), PalmRadius)]
            : Fingers(position, channel);

    internal static HandContact[] Fingers(ChartPoint position, InputChannel channel)
    {
        var angle = Math.Atan2(position.Y, position.X) + Math.PI / 2;
        return new[] { -.075, -.025, .025, .075 }.Select((d, i) =>
            new HandContact($"finger{i + 1}", channel, new(d * Math.Cos(angle), d * Math.Sin(angle)), .035)).ToArray();
    }
}
