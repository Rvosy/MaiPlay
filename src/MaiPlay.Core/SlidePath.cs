namespace MaiPlay.Core;

public sealed record SlideSegment(string Shape, int Start, int End, int Via = 0);

/// <summary>Standard prefab paths with equal-time samples and independent timing for chained segments.</summary>
public sealed class SlidePath
{
    private readonly ChartPoint[][][] _segmentBranches;
    private readonly double[][][] _lengths;
    private readonly double[] _weights;
    private readonly double[][] _branchLengths;
    public IReadOnlyList<SlideSegment> Segments { get; }
    public IReadOnlyList<double> SegmentWeights => _weights;
    public IReadOnlyList<IReadOnlyList<ChartPoint>> Branches { get; }
    public bool IsWifi => Segments.Count == 1 && Segments[0].Shape == "w";
    public double Length { get; }
    public int TimingUnits { get; }

    public SlidePath(IReadOnlyList<SlideSegment> segments, IReadOnlyList<double?> durations)
    {
        Segments = segments;
        _segmentBranches = segments.Select(CreateSegment).ToArray();
        _lengths = _segmentBranches.Select(branches => branches.Select(CumulativeLengths).ToArray()).ToArray();
        Length = _lengths.Sum(segment => segment[0][^1]);
        var timingUnits = _segmentBranches.Select((branches, i) => segments[i].Shape == "w" ? 20 : branches[0].Length - 1).ToArray();
        TimingUnits = timingUnits.Sum();
        _weights = durations.All(d => d.HasValue)
            ? durations.Select(d => d!.Value / durations.Sum(v => v!.Value)).ToArray()
            : timingUnits.Select(units => (double)units / TimingUnits).ToArray();
        var branchCount = IsWifi ? 3 : 1;
        Branches = Enumerable.Range(0, branchCount).Select(branch =>
            (IReadOnlyList<ChartPoint>)_segmentBranches.SelectMany((s, index) => index == 0 ? s[branch] : s[branch].Skip(1)).ToArray()).ToArray();
        _branchLengths = Branches.Select(points => CumulativeLengths((ChartPoint[])points)).ToArray();
    }

    public ChartPoint PointAtDistance(double progress, int branch = 0) =>
        Interpolate((ChartPoint[])Branches[branch], _branchLengths[branch], progress);

    public ChartPoint PointAt(double progress, int branch = 0)
    {
        progress = Math.Clamp(progress, 0, 1);
        var segment = 0;
        while (segment < _weights.Length - 1 && progress > _weights[segment])
            progress -= _weights[segment++];
        var local = _weights[segment] == 0 ? 1 : progress / _weights[segment];
        var points = _segmentBranches[segment][branch];
        var (index, fraction) = SampleAt(local, points.Length);
        return ChartPoint.Lerp(points[index], points[index + 1], fraction);
    }

    public ChartPoint TangentAt(double progress, int branch = 0)
    {
        var a = PointAt(Math.Max(0, progress - .0001), branch);
        var b = PointAt(Math.Min(1, progress + .0001), branch);
        var distance = a.DistanceTo(b);
        return distance == 0 ? new(0, -1) : new((b.X - a.X) / distance, (b.Y - a.Y) / distance);
    }

    /// <summary>Converts elapsed-time progress to the consumed fraction of track length.</summary>
    public double DistanceProgress(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        double distance = 0;
        for (var i = 0; i < _weights.Length; i++)
        {
            var part = Math.Min(progress, _weights[i]);
            var lengths = _lengths[i][0];
            var (index, fraction) = SampleAt(_weights[i] == 0 ? 1 : part / _weights[i], lengths.Length);
            distance += lengths[index] + (lengths[index + 1] - lengths[index]) * fraction;
            progress -= part;
            if (progress <= 0) break;
        }
        return distance / Length;
    }

    internal double SegmentDistanceProgress(int segment, double progress)
    {
        var lengths = _lengths[segment][0];
        var (index, fraction) = SampleAt(progress, lengths.Length);
        var distance = _lengths.Take(segment).Sum(part => part[0][^1]);
        return (distance + lengths[index] + (lengths[index + 1] - lengths[index]) * fraction) / Length;
    }

    public static ChartPoint ButtonPosition(int position, double radius = 1)
    {
        var angle = (position - 1) * Math.PI / 4 - 3 * Math.PI / 8;
        return Polar(angle, radius);
    }

    public static ChartPoint TouchPosition(string area)
    {
        if (area.Length == 0 || area[0] == 'C') return new(0, 0);
        var position = area[1] - '0';
        var radius = area[0] switch { 'A' => 4.0 / 4.8, 'B' => 2.2 / 4.8, 'D' => 4.1 / 4.8, 'E' => 3.1 / 4.8, _ => 0 };
        var angle = (position - 1) * Math.PI / 4 - (area[0] is 'D' or 'E' ? Math.PI / 2 : 3 * Math.PI / 8);
        return Polar(angle, radius);
    }

    private static ChartPoint[][] CreateSegment(SlideSegment s)
    {
        var start = ButtonPosition(s.Start);
        var end = ButtonPosition(s.End);
        var (name, mirror) = GetTemplate(s);
        if (name == "wifi")
            return [[start, end], [start, ButtonPosition(s.End - 1)], [start, ButtonPosition(s.End + 1)]];
        var samples = SlidePathSamples.Get(name);
        var points = new ChartPoint[samples.Count + 2];
        points[0] = start;
        points[^1] = end;
        var angle = (mirror ? s.Start : s.Start - 1) * Math.PI / 4;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        for (var i = 0; i < samples.Count; i++)
        {
            var x = mirror ? -samples[i].X : samples[i].X;
            var y = samples[i].Y;
            points[i + 1] = new(x * cos - y * sin, x * sin + y * cos);
        }
        return [points];
    }

    internal static (string Name, bool Mirror) GetTemplate(SlideSegment s)
    {
        var difference = (s.End - s.Start + 8) % 8;
        var mirroredDifference = (8 - difference) % 8;
        var mirror = false;
        string name;
        switch (s.Shape)
        {
            case "-":
                if (difference < 2 || difference > 6) Invalid("直线 Slide 起终点至少隔一个键位");
                name = $"line{difference + 1}";
                break;
            case "v":
                if (difference == 4) Invalid("v 形不能以对面键位为终点；请使用直线");
                name = $"v{difference + 1}";
                break;
            case "V":
                var turn = (s.Via - s.Start + 8) % 8;
                if (turn == 6 ? difference is < 1 or > 4 : turn != 2 || difference < 4)
                    Invalid("V 形转折/终点组合无效");
                mirror = turn == 2;
                name = $"L{(mirror ? mirroredDifference : difference) + 1}";
                break;
            case "^":
            case "<":
            case ">":
                if (s.Shape == "^" && difference is 0 or 4) Invalid("^ 圆弧不能同点或正好半圈");
                var upperHalf = s.Start is 1 or 2 or 7 or 8;
                var clockwise = s.Shape == "^" ? difference < 4 : s.Shape == ">" ? upperHalf : !upperHalf;
                mirror = !clockwise;
                name = $"circle{(mirror ? mirroredDifference : difference) + 1}";
                break;
            case "p":
            case "q":
                mirror = s.Shape == "q";
                name = $"pq{(mirror ? mirroredDifference : difference) + 1}";
                break;
            case "pp":
            case "qq":
                mirror = s.Shape == "qq";
                name = $"ppqq{(mirror ? mirroredDifference : difference) + 1}";
                break;
            case "s":
            case "z":
                if (difference != 4) Invalid("s/z Slide 必须到对面键位");
                mirror = s.Shape == "z";
                name = "s";
                break;
            case "w":
                if (difference != 4) Invalid("Wifi Slide 必须到对面键位");
                name = "wifi";
                break;
            default:
                throw new ChartParseException($"不支持的 Slide 形状 {s.Shape}");
        }
        return (name, mirror);
    }

    private static void Invalid(string message) => throw new ChartParseException(message);
    private static ChartPoint Polar(double angle, double radius) => new(Math.Cos(angle) * radius, Math.Sin(angle) * radius);

    private static (int Index, double Fraction) SampleAt(double progress, int count)
    {
        var sample = Math.Clamp(progress, 0, 1) * (count - 1);
        var index = Math.Min((int)sample, count - 2);
        return (index, sample - index);
    }

    private static double[] CumulativeLengths(ChartPoint[] points)
    {
        var lengths = new double[points.Length];
        for (var i = 1; i < points.Length; i++) lengths[i] = lengths[i - 1] + points[i - 1].DistanceTo(points[i]);
        return lengths;
    }

    private static ChartPoint Interpolate(ChartPoint[] points, double[] lengths, double progress)
    {
        var distance = Math.Clamp(progress, 0, 1) * lengths[^1];
        var index = Array.BinarySearch(lengths, distance);
        if (index >= 0) return points[index];
        index = ~index;
        if (index >= points.Length) return points[^1];
        return ChartPoint.Lerp(points[index - 1], points[index],
            (distance - lengths[index - 1]) / (lengths[index] - lengths[index - 1]));
    }
}
