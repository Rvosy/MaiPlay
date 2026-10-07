using System.Numerics;

namespace MaiPlay.Core;

/// <summary>Position/radius use normalized sensor canvas coordinates, not millimetres or note positions.</summary>
public sealed record ContactEvent(double Time, string Source, InputChannel Channel, ChartPoint Position, double Radius, bool IsDown);

/// <summary>Circle/polygon contacts become ordinary region events before entering the game.</summary>
public static partial class ContactMapper
{
    private static readonly string[] ButtonNames = Enumerable.Range(1, 8).Select(i => i.ToString()).ToArray();
    private static readonly string[] SensorNames = Enum.GetNames<SensorArea>();

    private sealed class ContactPolygon(ulong bit, IReadOnlyList<ChartPoint> points)
    {
        public ulong Bit { get; } = bit;
        public IReadOnlyList<ChartPoint> Points { get; } = points;
        public double MinX { get; } = points.Min(p => p.X);
        public double MaxX { get; } = points.Max(p => p.X);
        public double MinY { get; } = points.Min(p => p.Y);
        public double MaxY { get; } = points.Max(p => p.Y);
    }

    // 延迟到首次映射时读取另一 partial 文件中的传感器数据。
    private static class Geometry
    {
        public static readonly ContactPolygon[] Buttons = ButtonPolygons.Select((p, i) => new ContactPolygon(1UL << i, p)).ToArray();
        public static readonly ContactPolygon[] Sensors = SensorPolygons.Select(p => new ContactPolygon(1UL << (int)p.Area, p.Points)).ToArray();
    }
    // Button dimensions are an explicit approximation, independent of the upstream touch polygons.
    public static IReadOnlyList<IReadOnlyList<ChartPoint>> ButtonPolygons { get; } = Enumerable.Range(1, 8).Select(button =>
    {
        var centre = (button - 1) * Math.PI / 4 - 3 * Math.PI / 8;
        return (IReadOnlyList<ChartPoint>)Enumerable.Range(0, 9).Select(i => At(1.20, centre - Math.PI / 8 + i * Math.PI / 32))
            .Concat(Enumerable.Range(0, 9).Reverse().Select(i => At(1.04, centre - Math.PI / 8 + i * Math.PI / 32))).ToArray();
    }).ToArray();

    public static IReadOnlyList<string> AreasAt(InputChannel channel, ChartPoint position, double radius)
    {
        var mask = AreaMaskAt(channel, position, radius);
        var names = channel == InputChannel.Button ? ButtonNames : SensorNames;
        var areas = new string[BitOperations.PopCount(mask)];
        for (var i = 0; mask != 0; i++, mask &= mask - 1)
            areas[i] = names[BitOperations.TrailingZeroCount(mask)];
        return areas;
    }

    private static ulong AreaMaskAt(InputChannel channel, ChartPoint position, double radius)
    {
        ulong mask = 0;
        foreach (var polygon in channel == InputChannel.Button ? Geometry.Buttons : Geometry.Sensors)
        {
            // 包围框只排除不可能相交的区域，边界仍走原有圆与多边形判定。
            var reach = radius + 2e-12;
            if (position.X < polygon.MinX - reach || position.X > polygon.MaxX + reach ||
                position.Y < polygon.MinY - reach || position.Y > polygon.MaxY + reach) continue;
            if (Intersects(polygon.Points, position, radius)) mask |= polygon.Bit;
        }
        return mask;
    }

    public static IReadOnlyList<InputEvent> Convert(IEnumerable<ContactEvent> contacts)
    {
        var states = new Dictionary<(string Source, InputChannel Channel), ulong>();
        var result = new List<InputEvent>();
        var previousTime = double.NegativeInfinity;
        foreach (var c in contacts)
        {
            if (!double.IsFinite(c.Time) || c.Time < previousTime || !double.IsFinite(c.Position.X) ||
                !double.IsFinite(c.Position.Y) || !double.IsFinite(c.Radius) || c.Radius < 0 ||
                string.IsNullOrWhiteSpace(c.Source) || !Enum.IsDefined(c.Channel))
                throw new InvalidDataException("接触事件需要升序时间、有效来源、通道、有限坐标和非负半径。");
            var key = (c.Source, c.Channel);
            var before = states.GetValueOrDefault(key);
            var after = c.IsDown ? AreaMaskAt(c.Channel, c.Position, c.Radius) : 0;
            // Releases precede presses within one contact update; independent sources are kept intact.
            AppendChanges(before & ~after, false);
            AppendChanges(after & ~before, true);
            states[key] = after;
            previousTime = c.Time;

            void AppendChanges(ulong changed, bool isDown)
            {
                // 区域编号与原有字符串排序一致；C 的两块多边形共用一位。
                var names = c.Channel == InputChannel.Button ? ButtonNames : SensorNames;
                for (; changed != 0; changed &= changed - 1)
                    result.Add(new(c.Time, c.Channel, names[BitOperations.TrailingZeroCount(changed)], isDown, c.Source));
            }
        }
        return result;
    }

    private static bool Intersects(IReadOnlyList<ChartPoint> polygon, ChartPoint centre, double radius)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared == 0 ? 0 : Math.Clamp(((centre.X - a.X) * dx + (centre.Y - a.Y) * dy) / lengthSquared, 0, 1);
            if (centre.DistanceTo(ChartPoint.Lerp(a, b, t)) <= radius + 1e-12) return true;
            if ((a.Y > centre.Y) != (b.Y > centre.Y) && centre.X < dx * (centre.Y - a.Y) / dy + a.X) inside = !inside;
        }
        return inside;
    }

    private static ChartPoint At(double radius, double angle) => new(radius * Math.Cos(angle), radius * Math.Sin(angle));
}
