namespace MaiPlay.Core;

/// <summary>独立滑轨之间的名义连接；星头仍是单独的输入需求。</summary>
public sealed record SlideConnection(int PreviousId, int NextId, double Time);

public sealed record SlideConnectionOutcome(int PreviousId, int NextId, double Time,
    HandSide? PreviousHand, HandSide? NextHand, double ContactGapSeconds,
    double EndpointDistanceMillimeters, double VelocityChangeMillimetersPerSecond)
{
    public bool IsRelay => PreviousHand is not null && NextHand is not null && PreviousHand != NextHand;
    public bool Continuous => PreviousHand is not null && PreviousHand == NextHand && ContactGapSeconds < 1e-8;
}

public static class SlideConnections
{
    private const double Epsilon = 1e-7;

    public static IReadOnlyList<SlideConnection> Analyze(Chart chart)
    {
        var slides = chart.Notes.Where(n => n.Kind == NoteKind.Slide && n.SlidePath is { IsWifi: false })
            .OrderBy(n => n.SlideStartTime).ToArray();
        var starts = slides.GroupBy(n => n.StartPosition).ToDictionary(g => g.Key, g => g.ToArray());
        var candidates = new List<SlideConnection>();
        foreach (var before in slides)
        {
            if (!starts.TryGetValue(before.EndPosition, out var next)) continue;
            foreach (var after in next)
            {
                if (after.SlideStartTime > before.EndTime + Epsilon) break;
                if (after.Id == before.Id || after.SlideStartTime <= before.SlideStartTime ||
                    Math.Abs(after.SlideStartTime - before.EndTime) > Epsilon ||
                    before.SlidePath!.PointAt(1).DistanceTo(after.SlidePath!.PointAt(0)) > Epsilon) continue;
                candidates.Add(new(before.Id, after.Id, before.EndTime));
            }
        }
        // 分岔和汇合保留给自由分手；第一阶段只连接前后身份均明确的独立轨道。
        var outgoing = candidates.GroupBy(c => c.PreviousId).ToDictionary(g => g.Key, g => g.Count());
        var incoming = candidates.GroupBy(c => c.NextId).ToDictionary(g => g.Key, g => g.Count());
        return candidates.Where(c => outgoing[c.PreviousId] == 1 && incoming[c.NextId] == 1).ToArray();
    }

    public static IReadOnlyList<SlideConnectionOutcome> Measure(Chart chart, IReadOnlyList<HandTrack> tracks,
        IReadOnlyList<PlannedStroke> strokes, CabinetGeometry geometry)
    {
        var byNote = strokes.Where(s => s.Technique == HandTechnique.Slide).GroupBy(s => s.NoteId)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var byHand = tracks.ToDictionary(t => t.Hand);
        var outcomes = new List<SlideConnectionOutcome>();
        foreach (var link in Analyze(chart))
        {
            byNote.TryGetValue(link.PreviousId, out var before);
            byNote.TryGetValue(link.NextId, out var after);
            if (before is null || after is null)
            {
                outcomes.Add(new(link.PreviousId, link.NextId, link.Time, before?.Hand, after?.Hand, 0, 0, 0));
                continue;
            }
            var a = byHand[before.Hand].Frames;
            var b = byHand[after.Hand].Frames;
            var lo = Math.Min(before.End, after.Start);
            var hi = Math.Max(before.End, after.Start);
            var gap = 0d;
            if (before.Hand == after.Hand)
            {
                for (var i = 0; i + 1 < a.Count; i++)
                    if (a[i].Contacts.Count == 0)
                        gap += Math.Max(0, Math.Min(hi, a[i + 1].Time) - Math.Max(lo, a[i].Time));
            }
            else gap = Math.Max(0, after.Start - before.End);
            ChartPoint Velocity(IReadOnlyList<HandFrame> frames, double time, bool incoming)
            {
                for (var i = 1; i < frames.Count; i++)
                    if (incoming ? frames[i].Time >= time - Epsilon : frames[i].Time > time + Epsilon)
                    {
                        var dt = frames[i].Time - frames[i - 1].Time;
                        return new((frames[i].Position.X - frames[i - 1].Position.X) / dt,
                            (frames[i].Position.Y - frames[i - 1].Position.Y) / dt);
                    }
                return new(0, 0);
            }
            outcomes.Add(new(link.PreviousId, link.NextId, link.Time, before.Hand, after.Hand, gap,
                geometry.DistanceMillimeters(HumanMotionModel.PositionAt(a, before.End), HumanMotionModel.PositionAt(b, after.Start)),
                geometry.DistanceMillimeters(Velocity(a, before.End, true), Velocity(b, after.Start, false))));
        }
        return outcomes;
    }
}
