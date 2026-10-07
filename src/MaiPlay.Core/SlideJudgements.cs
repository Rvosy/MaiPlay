// Adapted from MajdataPlay by TeamMajdata and contributors, licensed under GPL-3.0.
// Revision: dc19722d602f099131d93b37091624ad30150ad1
// Sources: SlideBase.SensorCheck/Judge/TooLateJudge, SingleSlideBase.SlideCheck,
// SlideDrop.InitializeSlideGroup/UpdateJudgeQueue, SlideArea.Area.Check, WifiDrop.
// Original license: licenses/MajdataPlay-GPL-3.0.txt
// Unity update phases are retained on the deterministic gameplay clock.
namespace MaiPlay.Core;

public sealed record SlideTrace(double Time, int NoteId, int Segment, int Branch, int RemainingAreas, string Reason);

internal sealed class SlideJudgements
{
    private const double BoundaryTolerance = 1e-10;
    private readonly Chart _chart;
    private Track[] _tracks = [];
    private readonly List<SlideTrace> _trace = [];
    public IReadOnlyList<SlideTrace> Trace => _trace;

    public SlideJudgements(Chart chart)
    {
        _chart = chart;
        Reset();
    }

    public void Reset()
    {
        _tracks = _chart.Notes.Where(note => note.Kind == NoteKind.Slide).Select(note => new Track(note)).ToArray();
        _trace.Clear();
    }

    public void Process(double time, InputState inputs, List<PlaybackEvent> output)
    {
        // 上游先检查上一采样的完成状态，再推进本次采样的区域队列。
        foreach (var track in _tracks)
        {
            if (track.Finished) continue;
            for (var i = 0; i < track.Segments.Length; i++)
            {
                var segment = track.Segments[i];
                segment.Checkable |= i == 0 ? time + BoundaryTolerance >= track.Note.Time - .05 : track.Segments[i - 1].Remaining <= 1;
            }
            var last = track.Segments[^1];
            if (last.Remaining == 0)
            {
                var difference = time - track.JudgeTime;
                var grade = Grade(difference, track.TailTime);
                Finish(track, time, grade, difference, "末区完成", output);
            }
            else if (time > track.Note.EndTime + .6 + BoundaryTolerance)
            {
                var grade = last.Remaining == 1 ? AchievementGrade.LateGood : AchievementGrade.Miss;
                Finish(track, time, grade, null, $"超时，末段剩余 {last.Remaining} 区", output);
            }
        }
        foreach (var track in _tracks)
        {
            if (track.Finished) continue;
            for (var i = 0; i < track.Segments.Length; i++)
            {
                var segment = track.Segments[i];
                if (!segment.Checkable || segment.Remaining == 0) continue;
                var triggered = false;
                for (var branch = 0; branch < segment.Queues.Length; branch++)
                {
                    var queue = segment.Queues[branch];
                    var beforeRemaining = queue.Remaining;
                    var beforeProgress = queue.Progress;
                    var beforeOn = queue.HasTriggered;
                    triggered |= queue.Check(inputs);
                    if (beforeRemaining != queue.Remaining || beforeProgress != queue.Progress || beforeOn != queue.HasTriggered)
                        _trace.Add(new(time, track.Note.Id, i, branch, queue.Remaining,
                            queue.Remaining == 0 ? "区队列完成" : queue.Remaining < beforeRemaining ? "推进判定区" : "判定区按下"));
                }
                if (!triggered) continue;
                if (!track.Started)
                {
                    track.Started = true;
                    output.Add(new(PlaybackEventKind.Started, track.Note, time));
                }
                if (i > 0 && track.Segments[i - 1].Remaining != 0)
                {
                    foreach (var queue in track.Segments[i - 1].Queues) queue.ForceFinish();
                    _trace.Add(new(time, track.Note.Id, i - 1, 0, 0, "后续连接段已触发"));
                }
            }
        }
    }

    public double GetProgress(ChartNote note, int branch = 0)
    {
        var track = _tracks.FirstOrDefault(track => track.Note.Id == note.Id);
        if (track is null) return 0;
        var path = note.SlidePath!;
        if (path.IsWifi)
        {
            var queue = track.Segments[0].Queues[branch];
            return queue.Remaining == 0 ? 1 : queue.Progress / 9.0;
        }
        var segment = 0;
        while (segment < track.Segments.Length - 1 && track.Segments[segment].Remaining == 0) segment++;
        var current = track.Segments[segment];
        var localProgress = current.Remaining == 0 ? 1 : (double)current.Queues[0].Progress / (current.ArrowCount + 1);
        return path.SegmentDistanceProgress(segment, localProgress);
    }

    private void Finish(Track track, double time, AchievementGrade grade, double? difference, string reason, List<PlaybackEvent> output)
    {
        track.Finished = true;
        output.Add(new(PlaybackEvent.KindFor(grade), track.Note, time)
        {
            Grade = grade,
            DifferenceSeconds = difference,
            TimingKind = "slide-tail",
            Reason = reason
        });
        _trace.Add(new(time, track.Note.Id, track.Segments.Length - 1, -1, track.Segments[^1].Remaining, reason));
    }

    internal static AchievementGrade Grade(double difference, double tailTime)
    {
        const double frame = 1.0 / 60;
        var distance = Math.Abs(difference);
        var perfectWindow = 14 * frame + Math.Min(tailTime / 4, 22 * frame);
        // 上游默认关闭 SubdivideSlideJudgeGrade；所有 Slide Perfect 子档最终按 CP 结算。
        if (distance <= perfectWindow + BoundaryTolerance) return AchievementGrade.Perfect;
        var fast = difference < 0;
        if (distance <= 21 * frame + BoundaryTolerance) return fast ? AchievementGrade.FastGreat : AchievementGrade.LateGreat;
        if (distance <= 25 * frame + BoundaryTolerance) return fast ? AchievementGrade.FastGreat2nd : AchievementGrade.LateGreat2nd;
        if (distance <= 29 * frame + BoundaryTolerance) return fast ? AchievementGrade.FastGreat3rd : AchievementGrade.LateGreat3rd;
        return fast ? AchievementGrade.FastGood : AchievementGrade.LateGood;
    }

    private sealed class Track
    {
        public ChartNote Note { get; }
        public Segment[] Segments { get; }
        public double JudgeTime { get; }
        public double TailTime { get; }
        public bool Started;
        public bool Finished;

        public Track(ChartNote note)
        {
            Note = note;
            var path = note.SlidePath!;
            Segments = path.Segments.Select(segment => new Segment(segment)).ToArray();
            if (Segments.Length > 1)
            {
                var totalAreas = Segments.Sum(segment => segment.Remaining) - Segments.Length + 1;
                for (var i = 0; i < Segments.Length; i++)
                {
                    var areas = Segments[i].Queues[0].Areas;
                    areas[^1].Last = i == Segments.Length - 1;
                    if (totalAreas >= 4)
                        foreach (var area in areas) area.Skippable = true;
                    else if (i == 0)
                    {
                        areas[0].Skippable = true;
                        areas[1].Skippable = false;
                    }
                    else if (i == Segments.Length - 1)
                    {
                        areas[0].Skippable = false;
                        areas[1].Skippable = true;
                    }
                }
            }
            TailTime = note.Duration * path.SegmentWeights[^1] * Segments[^1].TailConstant;
            JudgeTime = note.EndTime - TailTime;
        }
    }

    private sealed class Segment
    {
        public Queue[] Queues { get; }
        public int Remaining => Queues.Max(queue => queue.Remaining);
        public double TailConstant { get; }
        public int ArrowCount { get; }
        public bool Checkable;

        public Segment(SlideSegment segment)
        {
            var (name, mirror) = SlidePath.GetTemplate(segment);
            var table = SlideJudgeTables.Get(name);
            Queues = table.Queues.Select(definitions => new Queue(definitions, segment.Start, mirror)).ToArray();
            TailConstant = table.TailConstant;
            ArrowCount = name == "wifi" ? 9 : SlidePathSamples.Get(name).Count;
        }
    }

    private sealed class Queue
    {
        public Area[] Areas { get; }
        public int Remaining => Areas.Length - _cursor;
        public int Progress { get; private set; }
        public bool HasTriggered { get; private set; }
        private int _cursor;

        public Queue(SlideAreaDefinition[] definitions, int start, bool mirror) =>
            Areas = definitions.Select(definition => new Area(definition, start, mirror)).ToArray();

        public void ForceFinish() => _cursor = Areas.Length;

        public bool Check(InputState inputs)
        {
            var triggered = false;
            while (Remaining > 0)
            {
                var first = Areas[_cursor];
                first.Check(inputs);
                if (Remaining >= 2 && (first.Skippable || first.On))
                {
                    var second = Areas[_cursor + 1];
                    second.Check(inputs);
                    if (second.Finished)
                    {
                        _cursor += 2;
                        Record(second);
                        triggered = true;
                        continue;
                    }
                    if (second.On)
                    {
                        _cursor++;
                        Record(second);
                        triggered = true;
                        continue;
                    }
                }
                if (first.Finished)
                {
                    _cursor++;
                    Record(first);
                    triggered = true;
                }
                else if (first.On)
                {
                    Record(first);
                    triggered = true;
                }
                break;
            }
            return triggered;
        }

        private void Record(Area area)
        {
            Progress = Math.Max(Progress, area.Finished ? area.Definition.ProgressOff : area.Definition.ProgressOn);
            HasTriggered = true;
        }
    }

    private sealed class Area
    {
        public SlideAreaDefinition Definition { get; }
        public bool Skippable;
        public bool Last;
        public bool On { get; private set; }
        public bool Finished { get; private set; }
        private readonly SensorArea[] _sensors;
        private readonly bool[] _on;
        private readonly bool[] _off;

        public Area(SlideAreaDefinition definition, int start, bool mirror)
        {
            Definition = definition;
            Skippable = definition.Skippable;
            Last = definition.Last;
            _sensors = definition.Areas.Select(sensor => Transform(sensor, start, mirror)).ToArray();
            _on = new bool[_sensors.Length];
            _off = new bool[_sensors.Length];
        }

        public void Check(InputState inputs)
        {
            for (var i = 0; i < _sensors.Length; i++)
            {
                if (inputs.IsSensorOn(_sensors[i])) _on[i] = true;
                else if (_on[i]) _off[i] = true;
                On |= _on[i];
                Finished |= Last ? _on[i] : _on[i] && _off[i];
            }
        }
    }

    internal static SensorArea Transform(SensorArea area, int start, bool mirror)
    {
        if (area == SensorArea.C) return area;
        var value = (int)area;
        var basis = value < 8 ? 0 : value < 16 ? 8 : value < 25 ? 17 : 25;
        var index = value - basis;
        // Mirror(A1): A/B 的 1 不动；D/E 的 1 和 2 互换。
        if (mirror) index = (basis >= 17 ? 9 - index : 8 - index) % 8;
        return (SensorArea)(basis + (index + start - 1) % 8);
    }
}
