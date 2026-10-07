namespace MaiPlay.Core;

public enum HandSide { Left, Right }

// 接触部位相对手位记录偏移；同一只手可以同时使用多个部位和输入通道。
public sealed record HandContact(string Id, InputChannel Channel, ChartPoint Offset, double Radius);
public sealed record HandFrame(double Time, ChartPoint Position, IReadOnlyList<HandContact> Contacts);
public sealed record HandTrack(HandSide Hand, IReadOnlyList<HandFrame> Frames);
public sealed record HandPose(ChartPoint Position, IReadOnlyList<HandContact> Contacts);

/// <summary>同一份手部轨迹生成实际输入和显示位置，显示开关不参与输入生成。</summary>
public sealed class HandReplay
{
    // 稀疏日志也可能展开成过量采样；导入时限制实际展开工作量。
    public const int MaximumSamples = 1_000_000;
    private readonly Dictionary<HandSide, HandTrack> _tracks;
    private readonly Dictionary<string, HandSide> _sourceHands = new(StringComparer.Ordinal);
    private readonly Dictionary<(HandSide Hand, string Part), string> _sources = new();
    public IReadOnlyList<HandSide> Hands { get; }
    public IReadOnlyList<InputEvent> Events { get; }

    public HandReplay(IReadOnlyList<HandTrack> tracks)
    {
        Validate(tracks);
        _tracks = tracks.ToDictionary(t => t.Hand);
        Hands = _tracks.Keys.Order().ToArray();
        foreach (var track in tracks)
            foreach (var part in track.Frames.SelectMany(f => f.Contacts))
            {
                var key = (track.Hand, part.Id);
                if (_sources.ContainsKey(key)) continue;
                var source = Source(track.Hand, part.Id);
                _sources.Add(key, source);
                _sourceHands[source] = track.Hand;
            }
        Events = ContactMapper.Convert(GenerateContacts());
    }

    public HandSide? HandForSource(string source) => _sourceHands.TryGetValue(source, out var hand) ? hand : null;

    public HandPose? PoseAt(HandSide hand, double time)
    {
        if (!_tracks.TryGetValue(hand, out var track)) return null;
        var frames = track.Frames;
        var index = FrameIndexAt(frames, time);
        if (index < 0) return null;
        var before = frames[index];
        if (index == frames.Count - 1) return new(before.Position, before.Contacts);
        var after = frames[index + 1];
        var t = (time - before.Time) / (after.Time - before.Time);
        var position = ChartPoint.Lerp(before.Position, after.Position, t);
        var contacts = before.Contacts.Select(part =>
        {
            var (offset, radius) = ContactGeometryAt(part, after, t);
            return part with { Offset = offset, Radius = radius };
        }).ToArray();
        return new(position, contacts);
    }

    private static int FrameIndexAt(IReadOnlyList<HandFrame> frames, double time)
    {
        var low = 0; var high = frames.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (frames[mid].Time <= time) low = mid + 1; else high = mid;
        }
        return low - 1;
    }

    private static (ChartPoint Offset, double Radius) ContactGeometryAt(HandContact part, HandFrame after, double t)
    {
        foreach (var next in after.Contacts)
        {
            if (next.Id == part.Id && next.Channel == part.Channel)
                return (ChartPoint.Lerp(part.Offset, next.Offset, t), part.Radius + (next.Radius - part.Radius) * t);
        }
        return (part.Offset, part.Radius);
    }

    private IEnumerable<ContactEvent> GenerateContacts()
    {
        var times = new SortedSet<double>(_tracks.Values.SelectMany(t => t.Frames).Select(f => f.Time));
        foreach (var track in _tracks.Values)
        {
            var first = (long)Math.Ceiling(track.Frames[0].Time * PlaybackSession.LogicHz);
            var last = (long)Math.Floor(track.Frames[^1].Time * PlaybackSession.LogicHz);
            for (var tick = first; tick <= last; tick++) times.Add(tick / (double)PlaybackSession.LogicHz);
        }
        var previous = new Dictionary<(string Source, InputChannel Channel), ContactEvent>();
        var current = new Dictionary<(string Source, InputChannel Channel), ContactEvent>();
        foreach (var time in times)
        {
            foreach (var hand in Hands)
            {
                var frames = _tracks[hand].Frames;
                var index = FrameIndexAt(frames, time);
                if (index < 0) continue;
                var before = frames[index];
                var after = index + 1 < frames.Count ? frames[index + 1] : null;
                var t = after is null ? 0 : (time - before.Time) / (after.Time - before.Time);
                var position = after is null ? before.Position : ChartPoint.Lerp(before.Position, after.Position, t);
                foreach (var part in before.Contacts)
                {
                    var source = _sources[(hand, part.Id)];
                    var key = (source, part.Channel);
                    var (offset, radius) = after is null ? (part.Offset, part.Radius) : ContactGeometryAt(part, after, t);
                    var centre = new ChartPoint(position.X + offset.X, position.Y + offset.Y);
                    current[key] = previous.TryGetValue(key, out var old) && old.Position == centre && old.Radius == radius
                        ? old : new(time, source, part.Channel, centre, radius, true);
                }
            }
            foreach (var (key, old) in previous)
                if (!current.ContainsKey(key)) yield return old with { Time = time, IsDown = false };
            foreach (var (key, contact) in current)
                if (!previous.TryGetValue(key, out var old) || old.Position != contact.Position || old.Radius != contact.Radius)
                    yield return contact;
            previous.Clear();
            (previous, current) = (current, previous);
        }
    }

    private static string Source(HandSide hand, string part) => $"hand/{hand}/{part}";

    private static void Validate(IReadOnlyList<HandTrack> tracks)
    {
        var hands = new HashSet<HandSide>();
        double samples = 0;
        foreach (var track in tracks)
        {
            if (track is null || !Enum.IsDefined(track.Hand) || !hands.Add(track.Hand))
                throw new InvalidDataException("每只手只能有一条轨迹，hand 必须为 Left 或 Right。");
            if (track.Frames is null || track.Frames.Count < 2)
                throw new InvalidDataException("手部轨迹至少需要起始帧和结束帧。");
            var previous = double.NegativeInfinity;
            foreach (var frame in track.Frames)
            {
                if (frame is null || !double.IsFinite(frame.Time) || frame.Time <= previous ||
                    Math.Abs(frame.Time * PlaybackSession.LogicHz) >= long.MaxValue || !Finite(frame.Position))
                    throw new InvalidDataException("手部帧需要严格递增的有限时间戳和有限坐标。");
                if (frame.Contacts is null) throw new InvalidDataException("手部帧的 contacts 不能为 null；离屏使用空数组。");
                var parts = new HashSet<string>(StringComparer.Ordinal);
                foreach (var part in frame.Contacts)
                    if (part is null || string.IsNullOrWhiteSpace(part.Id) || !parts.Add(part.Id) ||
                        !Enum.IsDefined(part.Channel) || !Finite(part.Offset) || !double.IsFinite(part.Radius) || part.Radius < 0 ||
                        !double.IsFinite(frame.Position.X + part.Offset.X) || !double.IsFinite(frame.Position.Y + part.Offset.Y))
                        throw new InvalidDataException("接触部位需要唯一 id、有效通道、有限偏移和非负半径。");
                previous = frame.Time;
            }
            if (track.Frames[^1].Contacts.Count != 0)
                throw new InvalidDataException("手部轨迹的最后一帧必须释放全部接触部位。");
            samples += (track.Frames[^1].Time - track.Frames[0].Time) * PlaybackSession.LogicHz + track.Frames.Count + 1;
            if (samples > MaximumSamples) throw new InvalidDataException("手部轨迹展开后的采样数量过多，请缩短回放范围。");
        }
    }

    private static bool Finite(ChartPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
}
