using Avalonia;
using MaiPlay.Core;

namespace MaiPlay.Rendering;

/// <summary>SlideOK sprite placements measured from the upstream Game/Slides prefabs.
/// Coordinates are Unity world units; Convert below maps them to the screen once.</summary>
internal static class SlideJudgementLayout
{
    private readonly record struct Placement(double X, double Y, double Degrees);
    private static readonly Dictionary<string, Placement> Placements = new()
    {
        ["wifi"] = new(-1.61, -3.87, 157.42957),
        ["circle1"] = new(.044, 4.645, 0), ["circle2"] = new(3.26, 3.26, 315),
        ["circle3"] = new(4.64, -.01, 270), ["circle4"] = new(3.25, -3.3, 225),
        ["circle5"] = new(-.04, -4.65, 180), ["circle6"] = new(-3.31, -3.24, 135),
        ["circle7"] = new(-4.62, .07, 90), ["circle8"] = new(-3.24, 3.29, 45),
        ["line3"] = new(3.45, .2, 292.64689), ["line4"] = new(1.7, -2.43, 270),
        ["line5"] = new(-1.08, -2.3, 247.20453), ["line6"] = new(-3.12, -.32, 225),
        ["line7"] = new(-2.398, 2.752, 202.94121),
        ["v1"] = new(1.22, 2.63, 67.5), ["v2"] = new(2.37, .85, 23.02876),
        ["v3"] = new(2.31, -1.15, 338.6318), ["v4"] = new(.92, -2.45, 292.71641),
        ["v6"] = new(-2.44, -.92, 204.13412), ["v7"] = new(-2.3, 1.11, 158.52819),
        ["v8"] = new(-.84, 2.35, 113.21312),
        ["L2"] = new(2.25077, 1.729, 0), ["L3"] = new(2.543, -1.188, 337.2874),
        ["L4"] = new(.21903, -2.97053, 314.75515), ["L5"] = new(-2.71077, -2.61163, 291.71234),
        ["pq1"] = new(1.97472, 2.26154, 90), ["pq2"] = new(3.028, .252, 45),
        ["pq3"] = new(2.345, -1.956, 0), ["pq4"] = new(.29663, -3.06455, 315),
        ["pq5"] = new(-1.97291, -2.4023, 270), ["pq6"] = new(-2.92124, -.14701, 225),
        ["pq7"] = new(-2.21857, 1.944, 180), ["pq8"] = new(-.203, 2.983, 135.34125),
        ["ppqq1"] = new(3.19, 2.62, 123.46132), ["ppqq2"] = new(4.564, -.344, -270.67201),
        ["ppqq3"] = new(2.59, -2.31, 10.68042), ["ppqq4"] = new(.79, -2.47, 293.87865),
        ["ppqq5"] = new(-1.21, -2.36, 249.89143), ["ppqq6"] = new(-2.657, -.513, 214.171),
        ["ppqq7"] = new(-2.472, 2.055, 182.64582), ["ppqq8"] = new(.222, 3.547, 154.01817),
        ["s"] = new(-.61, -3.03, 225.61239)
    };

    public static (string Shape, Point Position, double Angle) Get(SlidePath path)
    {
        var segment = path.Segments[^1];
        var relative = (segment.End - segment.Start + 8) % 8 + 1;
        var upperStart = Upper(segment.Start);
        var mirror = segment.Shape switch
        {
            ">" => !upperStart, "<" => upperStart, "^" => relative > 5,
            "q" or "qq" or "z" => true,
            "V" => (segment.Via - segment.Start + 8) % 8 == 2,
            _ => false
        };
        var number = mirror ? (9 - relative) % 8 + 1 : relative;
        var curved = segment.Shape is ">" or "<" or "^";
        var wifi = segment.Shape == "w";
        var right = curved ? !mirror : wifi ? Upper(segment.End) : segment.End <= 4;
        var key = segment.Shape switch
        {
            "-" => $"line{number}", "v" => $"v{number}", "V" => $"L{number}",
            ">" or "<" or "^" => $"circle{number}", "p" or "q" => $"pq{number}",
            "pp" or "qq" => $"ppqq{number}", "s" or "z" => "s", _ => "wifi"
        };
        var placement = Placements[key];
        var parentRotation = (segment.Start - (mirror ? 0 : 1)) * Math.PI / 4;
        var x = mirror ? -placement.X : placement.X;
        var y = -placement.Y;
        var position = new Point(x * Math.Cos(parentRotation) - y * Math.Sin(parentRotation),
            x * Math.Sin(parentRotation) + y * Math.Cos(parentRotation));
        var angle = parentRotation + (mirror ? 1 : -1) * placement.Degrees * Math.PI / 180;
        if (wifi && !right) angle += Math.PI;
        if (!curved && !wifi && right == mirror)
        {
            angle += Math.PI;
            position = new Point(position.X - Math.Sin(angle) * .27, position.Y + Math.Cos(angle) * .27);
        }
        var shape = wifi ? right ? "wifi_u" : "wifi_d" : $"{(curved ? "curv" : "str")}_{(right ? "r" : "l")}";
        return (shape, position, angle);
    }

    private static bool Upper(int button) => button is 1 or 2 or 7 or 8;
}
