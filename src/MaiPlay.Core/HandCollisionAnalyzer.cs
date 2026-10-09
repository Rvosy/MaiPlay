namespace MaiPlay.Core;

/// <summary>A connected interval of overlapping circular hand bodies while both hands contact the cabinet.</summary>
public sealed record HandCollisionInterval(double StartTime, double EndTime, double ClosestTime,
    double MinimumDistanceMillimeters);

/// <summary>Distances measure hand anchors, independently of sensor contact radii. Null means no shared contact interval.</summary>
public sealed record HandCollisionAnalysis(IReadOnlyList<HandCollisionInterval> Intervals,
    double? MinimumDistanceMillimeters, double? ClosestTime, double RequiredSeparationMillimeters)
{
    public static HandCollisionAnalysis Empty { get; } = new([], null, null, 0);
    public int Count => Intervals.Count;
    public double Seconds => Intervals.Sum(interval => interval.EndTime - interval.StartTime);
}

/// <summary>
/// Continuous collision checks for linear hand tracks. The fixed body circle approximates the palm around
/// the hand anchor; contact offsets/radii only describe sensor coverage and do not resize the body.
/// Both button and touch contacts occupy this plane. A released hand has unknown height and is excluded.
/// This does not model wrist orientation, extended fingers, forearms or airborne collisions.
/// </summary>
internal static class HandCollisionAnalyzer
{
    private const double TimeEpsilon = 1e-9;
    private const double DistanceEpsilonMillimeters = 1e-7;

    public static bool HasCollision(IReadOnlyList<HandFrame> left, IReadOnlyList<HandFrame> right,
        CabinetGeometry geometry, double handBodyRadiusMillimeters, double endTime = double.PositiveInfinity)
    {
        if (left.Count < 2 || right.Count < 2) return false;
        var start = Math.Max(left[0].Time, right[0].Time);
        var end = Math.Min(endTime, Math.Max(left[^1].Time, right[^1].Time));
        if (left[^1].Contacts.Count == 0) end = Math.Min(end, left[^1].Time);
        if (right[^1].Contacts.Count == 0) end = Math.Min(end, right[^1].Time);
        if (start >= end) return false;
        var leftIndex = IndexAt(left, start);
        var rightIndex = IndexAt(right, start);
        var separation = 2 * handBodyRadiusMillimeters - DistanceEpsilonMillimeters;
        // 只合并重叠时间里的断点；保留相对运动的解析最近距离，不按逻辑帧抽样。
        while (start < end)
        {
            var leftNext = leftIndex + 1 < left.Count ? left[leftIndex + 1].Time : end;
            var rightNext = rightIndex + 1 < right.Count ? right[rightIndex + 1].Time : end;
            var next = Math.Min(end, Math.Min(leftNext, rightNext));
            if (left[leftIndex].Contacts.Count > 0 && right[rightIndex].Contacts.Count > 0)
            {
                var leftStart = At(left, leftIndex, start);
                var rightStart = At(right, rightIndex, start);
                var leftEnd = At(left, leftIndex, next);
                var rightEnd = At(right, rightIndex, next);
                var from = geometry.ToMillimeters(new(leftStart.X - rightStart.X, leftStart.Y - rightStart.Y));
                var to = geometry.ToMillimeters(new(leftEnd.X - rightEnd.X, leftEnd.Y - rightEnd.Y));
                var delta = new ChartPoint(to.X - from.X, to.Y - from.Y);
                var speedSquared = Dot(delta, delta);
                var fraction = speedSquared > 0 ? Math.Clamp(-Dot(from, delta) / speedSquared, 0, 1) : 0;
                if (ChartPoint.Lerp(from, to, fraction).DistanceTo(default) < separation) return true;
            }
            start = next;
            if (leftIndex + 1 < left.Count && left[leftIndex + 1].Time <= start) leftIndex++;
            if (rightIndex + 1 < right.Count && right[rightIndex + 1].Time <= start) rightIndex++;
        }
        return false;
    }

    public static HandCollisionAnalysis Analyze(IReadOnlyList<HandFrame> left, IReadOnlyList<HandFrame> right,
        CabinetGeometry geometry, double handBodyRadiusMillimeters, double endTime = double.PositiveInfinity)
    {
        var separation = 2 * handBodyRadiusMillimeters;
        if (left.Count < 2 || right.Count < 2) return new([], null, null, separation);
        var times = left.Select(frame => frame.Time).Concat(right.Select(frame => frame.Time)).Distinct().Order().ToArray();
        var intervals = new List<HandCollisionInterval>();
        double? minimumDistance = null, closestTime = null;
        var leftIndex = -1;
        var rightIndex = -1;
        for (var i = 1; i < times.Length; i++)
        {
            var start = times[i - 1];
            if (start >= endTime) break;
            var end = Math.Min(times[i], endTime);
            while (leftIndex + 1 < left.Count && left[leftIndex + 1].Time <= start) leftIndex++;
            while (rightIndex + 1 < right.Count && right[rightIndex + 1].Time <= start) rightIndex++;
            if (leftIndex < 0 || rightIndex < 0 || left[leftIndex].Contacts.Count == 0 || right[rightIndex].Contacts.Count == 0)
                continue;

            var leftStart = At(left, leftIndex, start);
            var rightStart = At(right, rightIndex, start);
            var leftEnd = At(left, leftIndex, end);
            var rightEnd = At(right, rightIndex, end);
            var from = geometry.ToMillimeters(new(leftStart.X - rightStart.X, leftStart.Y - rightStart.Y));
            var to = geometry.ToMillimeters(new(leftEnd.X - rightEnd.X, leftEnd.Y - rightEnd.Y));
            var delta = new ChartPoint(to.X - from.X, to.Y - from.Y);
            var speedSquared = Dot(delta, delta);
            var projection = speedSquared > 0 ? -Dot(from, delta) / speedSquared : 0;
            var closestFraction = Math.Clamp(projection, 0, 1);
            var distance = ChartPoint.Lerp(from, to, closestFraction).DistanceTo(default);
            var time = start + (end - start) * closestFraction;
            if (minimumDistance is null || distance < minimumDistance)
            {
                minimumDistance = distance;
                closestTime = time;
            }
            // 恰好贴边允许并拢，浮点误差不能把 Wifi 的相邻掌面判成碰撞。
            if (distance >= separation - DistanceEpsilonMillimeters) continue;

            double begin = 0, finish = 1;
            if (speedSquared > 0)
            {
                var perpendicularSquared = Math.Max(0, Dot(from, from) - projection * projection * speedSquared);
                var halfWidth = Math.Sqrt(Math.Max(0, separation * separation - perpendicularSquared) / speedSquared);
                begin = Math.Clamp(projection - halfWidth, 0, 1);
                finish = Math.Clamp(projection + halfWidth, 0, 1);
            }
            var collisionStart = start + (end - start) * begin;
            var collisionEnd = start + (end - start) * finish;
            if (intervals.Count > 0 && collisionStart <= intervals[^1].EndTime + TimeEpsilon)
            {
                var previous = intervals[^1];
                var nearer = distance < previous.MinimumDistanceMillimeters;
                intervals[^1] = previous with
                {
                    EndTime = collisionEnd,
                    ClosestTime = nearer ? time : previous.ClosestTime,
                    MinimumDistanceMillimeters = Math.Min(previous.MinimumDistanceMillimeters, distance)
                };
            }
            else intervals.Add(new(collisionStart, collisionEnd, time, distance));
        }
        return new(intervals, minimumDistance, closestTime, separation);
    }

    private static ChartPoint At(IReadOnlyList<HandFrame> frames, int index, double time)
    {
        var before = frames[index];
        if (index == frames.Count - 1) return before.Position;
        var after = frames[index + 1];
        return ChartPoint.Lerp(before.Position, after.Position, (time - before.Time) / (after.Time - before.Time));
    }

    private static int IndexAt(IReadOnlyList<HandFrame> frames, double time)
    {
        var low = 0;
        var high = frames.Count - 1;
        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (frames[mid].Time <= time) low = mid; else high = mid - 1;
        }
        return low;
    }

    private static double Dot(ChartPoint a, ChartPoint b) => a.X * b.X + a.Y * b.Y;
}
