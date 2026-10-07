namespace MaiPlay.Core;

/// <summary>Position/radius use normalized sensor canvas coordinates, not millimetres or note positions.</summary>
public sealed record ContactEvent(double Time, string Source, InputChannel Channel, ChartPoint Position, double Radius, bool IsDown);

/// <summary>Circle/polygon contacts become ordinary region events before entering the game.</summary>
public static partial class ContactMapper
{
    // Button dimensions are an explicit approximation, independent of the upstream touch polygons.
    public static IReadOnlyList<IReadOnlyList<ChartPoint>> ButtonPolygons { get; } = Enumerable.Range(1, 8).Select(button =>
    {
        var centre = (button - 1) * Math.PI / 4 - 3 * Math.PI / 8;
        return (IReadOnlyList<ChartPoint>)Enumerable.Range(0, 9).Select(i => At(1.20, centre - Math.PI / 8 + i * Math.PI / 32))
            .Concat(Enumerable.Range(0, 9).Reverse().Select(i => At(1.04, centre - Math.PI / 8 + i * Math.PI / 32))).ToArray();
    }).ToArray();

    public static IReadOnlyList<string> AreasAt(InputChannel channel, ChartPoint position, double radius)
    {
        if (channel == InputChannel.Button)
            return ButtonPolygons.Select((polygon, i) => (polygon, i)).Where(p => Intersects(p.polygon, position, radius))
                .Select(p => (p.i + 1).ToString()).ToArray();
        return SensorPolygons.Where(p => Intersects(p.Points, position, radius)).Select(p => p.Area.ToString()).Distinct().ToArray();
    }

    public static IReadOnlyList<InputEvent> Convert(IEnumerable<ContactEvent> contacts)
    {
        var states = new Dictionary<(string Source, InputChannel Channel), HashSet<string>>();
        var result = new List<InputEvent>();
        var previousTime = double.NegativeInfinity;
        foreach (var c in contacts)
        {
            if (!double.IsFinite(c.Time) || c.Time < previousTime || !double.IsFinite(c.Position.X) ||
                !double.IsFinite(c.Position.Y) || !double.IsFinite(c.Radius) || c.Radius < 0 ||
                string.IsNullOrWhiteSpace(c.Source) || !Enum.IsDefined(c.Channel))
                throw new InvalidDataException("接触事件需要升序时间、有效来源、通道、有限坐标和非负半径。");
            var key = (c.Source, c.Channel);
            var before = states.GetValueOrDefault(key) ?? [];
            var after = c.IsDown ? AreasAt(c.Channel, c.Position, c.Radius).ToHashSet() : [];
            // Releases precede presses within one contact update; independent sources are kept intact.
            foreach (var area in before.Except(after).Order(StringComparer.Ordinal)) result.Add(new(c.Time, c.Channel, area, false, c.Source));
            foreach (var area in after.Except(before).Order(StringComparer.Ordinal)) result.Add(new(c.Time, c.Channel, area, true, c.Source));
            states[key] = after;
            previousTime = c.Time;
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
