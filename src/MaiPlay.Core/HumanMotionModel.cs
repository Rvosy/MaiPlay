namespace MaiPlay.Core;

public sealed record HumanMotionTransition(bool Feasible, double DistanceMillimeters,
    double SpeedMillimetersPerSecond, double Cost, bool Crossed);

/// <summary>Measurements of the generated planar trajectories, not evidence of human playability.</summary>
public sealed record HumanMotionMetrics(double LeftDistanceMillimeters, double RightDistanceMillimeters,
    double PeakSpeedMillimetersPerSecond, int SpeedLimitViolations, int ProjectedCrossingCount,
    double ProjectedCrossingSeconds, int DirectionReversalCount, int? PeakLeftActionsPerSecond,
    int? PeakRightActionsPerSecond)
{
    public double TotalDistanceMillimeters => LeftDistanceMillimeters + RightDistanceMillimeters;
}

/// <summary>Configurable search assumptions; speed limits and costs have not been measured on players.</summary>
public sealed class HumanMotionModel
{
    private const double Epsilon = 1e-9;

    public CabinetGeometry Geometry { get; }
    public double MaxSpeedMillimetersPerSecond { get; }
    public double PreferredSpeedMillimetersPerSecond { get; }

    public HumanMotionModel(CabinetGeometry? geometry = null, double maxSpeedMillimetersPerSecond = 4500,
        double preferredSpeedMillimetersPerSecond = 1300)
    {
        if (!double.IsFinite(maxSpeedMillimetersPerSecond) || maxSpeedMillimetersPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSpeedMillimetersPerSecond));
        if (!double.IsFinite(preferredSpeedMillimetersPerSecond) || preferredSpeedMillimetersPerSecond <= 0 ||
            preferredSpeedMillimetersPerSecond > maxSpeedMillimetersPerSecond)
            throw new ArgumentOutOfRangeException(nameof(preferredSpeedMillimetersPerSecond));
        Geometry = geometry ?? CabinetGeometry.Approximate43Inch;
        MaxSpeedMillimetersPerSecond = maxSpeedMillimetersPerSecond;
        PreferredSpeedMillimetersPerSecond = preferredSpeedMillimetersPerSecond;
    }

    public HumanMotionTransition EvaluateTransition(HandSide hand, ChartPoint from, double departureTime,
        ChartPoint to, double arrivalTime, ChartPoint? otherPositionAtArrival = null, double? pathDistanceMillimeters = null)
    {
        var distance = pathDistanceMillimeters ?? Geometry.DistanceMillimeters(from, to);
        var duration = arrivalTime - departureTime;
        var speed = duration > 0 ? distance / duration : double.PositiveInfinity;
        var feasible = duration > 0 && speed <= MaxSpeedMillimetersPerSecond + Epsilon;
        var crossing = otherPositionAtArrival is { } other
            ? (hand == HandSide.Left ? to.X - other.X : other.X - to.X) : 0;
        if (!feasible) return new(false, distance, speed, double.PositiveInfinity, crossing > 0);

        var distanceInRadii = distance / Geometry.JudgementRadiusMillimeters;
        var speedExcess = Math.Max(0, speed / PreferredSpeedMillimetersPerSecond - 1);
        var wrongSide = hand == HandSide.Left ? to.X : -to.X;
        var cost = .15 * distanceInRadii * distanceInRadii / Math.Min(duration, 1) +
            .15 * speedExcess * speedExcess + .8 * Math.Max(0, wrongSide) + 2 * Math.Max(0, crossing);
        return new(true, distance, speed, cost, crossing > 0);
    }

    /// <summary>Samples the known path at the requested time; an idle hand stays at its nearest endpoint.</summary>
    public static ChartPoint PositionAt(IReadOnlyList<HandFrame> frames, double time)
    {
        if (frames.Count == 0) throw new ArgumentException("A hand path must contain a frame.", nameof(frames));
        if (time <= frames[0].Time) return frames[0].Position;
        if (time >= frames[^1].Time) return frames[^1].Position;
        var low = 1;
        var high = frames.Count - 1;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (frames[mid].Time < time) low = mid + 1; else high = mid;
        }
        var before = frames[low - 1];
        var after = frames[low];
        return ChartPoint.Lerp(before.Position, after.Position, (time - before.Time) / (after.Time - before.Time));
    }

    public HumanMotionMetrics Analyze(IReadOnlyList<HandTrack> tracks, IReadOnlyList<PlannedStroke>? strokes = null)
    {
        double leftDistance = 0, rightDistance = 0, peakSpeed = 0;
        var speedLimitViolations = 0;
        var directionReversals = 0;
        foreach (var track in tracks)
        {
            ChartPoint? previousDirection = null;
            for (var i = 1; i < track.Frames.Count; i++)
            {
                var before = track.Frames[i - 1];
                var after = track.Frames[i];
                var distance = Geometry.DistanceMillimeters(before.Position, after.Position);
                if (track.Hand == HandSide.Left) leftDistance += distance; else rightDistance += distance;
                var speed = distance / (after.Time - before.Time);
                peakSpeed = Math.Max(peakSpeed, speed);
                if (speed > MaxSpeedMillimetersPerSecond + Epsilon) speedLimitViolations++;
                if (distance <= Epsilon) continue;
                var direction = new ChartPoint(after.Position.X - before.Position.X, after.Position.Y - before.Position.Y);
                if (previousDirection is { } previous &&
                    previous.X * direction.X + previous.Y * direction.Y <
                    -Epsilon * previous.DistanceTo(default) * direction.DistanceTo(default)) directionReversals++;
                previousDirection = direction;
            }
        }
        var (crossingCount, crossingSeconds) = AnalyzeCrossings(tracks);
        return new(leftDistance, rightDistance, peakSpeed, speedLimitViolations, crossingCount, crossingSeconds,
            directionReversals, strokes is null ? null : PeakActions(strokes, HandSide.Left),
            strokes is null ? null : PeakActions(strokes, HandSide.Right));
    }

    private static (int Count, double Seconds) AnalyzeCrossings(IReadOnlyList<HandTrack> tracks)
    {
        var left = tracks.FirstOrDefault(t => t.Hand == HandSide.Left)?.Frames;
        var right = tracks.FirstOrDefault(t => t.Hand == HandSide.Right)?.Frames;
        if (left is null || right is null || left.Count == 0 || right.Count == 0) return (0, 0);
        var start = Math.Max(left[0].Time, right[0].Time);
        var end = Math.Min(left[^1].Time, right[^1].Time);
        if (end <= start) return (0, 0);
        var times = left.Select(f => f.Time).Concat(right.Select(f => f.Time))
            .Where(t => t >= start && t <= end).Append(start).Append(end).Distinct().Order().ToArray();
        var count = 0;
        double seconds = 0, previousEnd = double.NegativeInfinity;
        for (var i = 1; i < times.Length; i++)
        {
            var a = times[i - 1];
            var b = times[i];
            var separationA = PositionAt(left, a).X - PositionAt(right, a).X;
            var separationB = PositionAt(left, b).X - PositionAt(right, b).X;
            if (separationA <= 0 && separationB <= 0) continue;
            var crossedStart = a;
            var crossedEnd = b;
            if (separationA <= 0 || separationB <= 0)
            {
                var crossingTime = a + (b - a) * -separationA / (separationB - separationA);
                if (separationA <= 0) crossedStart = crossingTime; else crossedEnd = crossingTime;
            }
            if (crossedStart > previousEnd + Epsilon) count++;
            seconds += crossedEnd - crossedStart;
            previousEnd = crossedEnd;
        }
        return (count, seconds);
    }

    // A half-open one-second window counts simultaneous shared contacts as one action per hand.
    private static int PeakActions(IReadOnlyList<PlannedStroke> strokes, HandSide hand)
    {
        var times = strokes.Where(s => s.Hand == hand).Select(s => s.Start).Distinct().Order().ToArray();
        var peak = 0;
        var start = 0;
        for (var end = 0; end < times.Length; end++)
        {
            while (times[end] - times[start] >= 1) start++;
            peak = Math.Max(peak, end - start + 1);
        }
        return peak;
    }
}
