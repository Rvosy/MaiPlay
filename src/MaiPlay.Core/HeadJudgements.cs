// Adapted from TeamMajdata/MajdataPlay dc19722d602f099131d93b37091624ad30150ad1:
// NoteDrop, NoteLongDrop, TapDrop, HoldDrop, TouchDrop, TouchHoldDrop,
// TouchGroup, TouchHoldGroup and NoteLoader.TOUCH_GROUPS. GPL-3.0.
namespace MaiPlay.Core;

/// <summary>头判消费按下边沿；长音符共享持续区域状态，最终只结算一次。</summary>
internal sealed class HeadJudgements
{
    private const double Frame = 1.0 / 60;
    internal const double EarlyInputWindow = 9 * Frame;
    private const double BoundaryTolerance = 1e-10;
    private readonly Runtime[] _notes;
    private readonly Runtime?[] _byId;
    private readonly Runtime[][] _byKind;
    private readonly Queue[] _queues;
    private readonly Group[] _groups;
    private readonly BodyGroup[] _bodyGroups;

    public HeadJudgements(Chart chart)
    {
        _notes = chart.Notes.Where(n => n.Kind != NoteKind.Slide)
            .OrderBy(n => n.Time).ThenBy(n => n.Id).Select(n => new Runtime(n)).ToArray();
        _byId = new Runtime?[chart.Notes.Count];
        foreach (var note in _notes) _byId[note.Note.Id] = note;
        _byKind = Enum.GetValues<NoteKind>().Select(kind => _notes.Where(n => n.Note.Kind == kind).ToArray()).ToArray();
        _queues = _notes.GroupBy(n => (n.IsTouch, n.Area)).Select(members =>
        {
            var queue = new Queue();
            var index = 0;
            foreach (var note in members)
            {
                note.Queue = queue;
                note.QueueIndex = index++;
            }
            return queue;
        }).ToArray();
        var groups = new List<Group>();
        var bodyGroups = new List<BodyGroup>();
        foreach (var timing in _notes.Where(n => n.IsTouch).GroupBy(n => n.Note.Time))
        {
            foreach (var members in ConnectedGroups(timing))
            {
                var group = new Group(members.Length);
                groups.Add(group);
                foreach (var note in members) note.Group = group;
            }
            foreach (var members in ConnectedGroups(timing.Where(n => n.Note.Kind == NoteKind.TouchHold)))
            {
                var group = new BodyGroup(members.Length);
                bodyGroups.Add(group);
                foreach (var note in members) note.BodyGroup = group;
            }
        }
        _groups = groups.ToArray();
        _bodyGroups = bodyGroups.ToArray();
    }

    public void ProcessTaps(double time, InputState inputs, List<PlaybackEvent> events) =>
        ProcessHeads(NoteKind.Tap, time, inputs, events);

    public void ProcessHolds(double time, double delta, InputState inputs, List<PlaybackEvent> events) =>
        ProcessLongNotes(NoteKind.Hold, time, delta, inputs, events);

    public void ProcessTouches(double time, InputState inputs, List<PlaybackEvent> events) =>
        ProcessHeads(NoteKind.Touch, time, inputs, events);

    public void ProcessTouchHolds(double time, double delta, InputState inputs, List<PlaybackEvent> events) =>
        ProcessLongNotes(NoteKind.TouchHold, time, delta, inputs, events);

    public bool IsHolding(ChartNote note) => _byId[note.Id] is { Ended: false } state &&
        (state.HoldState == 1 || (state.HoldState == -1 && state.HeadJudged && state.Grade != AchievementGrade.Miss));

    private void ProcessHeads(NoteKind kind, double time, InputState inputs, List<PlaybackEvent> events)
    {
        foreach (var state in _byKind[(int)kind])
        {
            if (state.Note.Time - EarlyInputWindow > time + BoundaryTolerance) break;
            if (state.Ended) continue;
            CheckHead(state, time, inputs, events);
            if (state.HeadJudged) Finish(state, time, events);
        }
    }

    private void ProcessLongNotes(NoteKind kind, double time, double delta, InputState inputs, List<PlaybackEvent> events)
    {
        foreach (var state in _byKind[(int)kind])
        {
            if (state.Note.Time - 9 * Frame > time + BoundaryTolerance) break;
            if (state.Ended) continue;
            CheckHead(state, time, inputs, events);
            CheckBody(state, time, delta, inputs, events);
            if (!state.HeadJudged || time + BoundaryTolerance < state.Note.EndTime) continue;

            // 上游在尾判时补计已确认松开的最后一个采样间隔。
            if (state.HoldState == 0 || (state.HoldState == -1 && state.Grade == AchievementGrade.Miss))
                state.ReleaseTime += delta;
            var ignored = state.IsTouch ? 27 * Frame : 18 * Frame;
            state.Grade = HoldEndGrade(state.Grade, state.Difference ?? 0,
                state.Note.Duration - ignored, state.ReleaseTime);
            Finish(state, time, events);
        }
    }

    private static void CheckHead(Runtime state, double time, InputState inputs, List<PlaybackEvent> events)
    {
        if (state.HeadJudged) return;
        var difference = time - state.Note.Time;
        var lateWindow = state.IsTouch ? 18 * Frame : 9 * Frame;
        if (difference > lateWindow + BoundaryTolerance)
        {
            state.Grade = AchievementGrade.Miss;
            state.Difference = state.IsLong ? lateWindow : null;
            state.Reason = "head-timeout";
            JudgeHead(state);
            return;
        }
        if (state.Group is { HasMajority: true } group)
        {
            state.Grade = group.Grade;
            state.Difference = group.Difference;
            state.TimingKind = "group";
            state.Reason = "touch-group";
            JudgeHead(state);
            if (state.IsLong)
            {
                state.BodyGroup?.Pressed.Add(state.Note.Id);
                events.Add(Event(PlaybackEventKind.Started, state, time));
            }
            return;
        }
        if (difference < -EarlyInputWindow - BoundaryTolerance || state.QueueIndex > state.Queue.Current) return;

        var clicked = (!state.IsTouch && inputs.TryUseButtonClick(state.Note.StartPosition)) || inputs.TryUseSensorClick(state.Sensor);
        if (!clicked) return;
        state.Difference = difference;
        state.Grade = state.IsTouch ? TouchGrade(difference) : TapGrade(difference, state.Note.IsEx);
        JudgeHead(state);
        state.Group?.Register(state.Grade, difference);
        if (state.IsLong)
        {
            state.BodyGroup?.Pressed.Add(state.Note.Id);
            events.Add(Event(PlaybackEventKind.Started, state, time));
        }
    }

    private static void JudgeHead(Runtime state)
    {
        state.HeadJudged = true;
        state.Queue.Current++;
    }

    private static void CheckBody(Runtime state, double time, double delta, InputState inputs, List<PlaybackEvent> events)
    {
        var headIgnore = state.IsTouch ? 15 * Frame : 6 * Frame;
        var tailIgnore = 12 * Frame;
        if (state.Note.Duration <= headIgnore + tailIgnore + BoundaryTolerance ||
            time + BoundaryTolerance < state.Note.Time + headIgnore ||
            time > state.Note.EndTime - tailIgnore + BoundaryTolerance) return;

        var pressed = inputs.IsSensorOn(state.Sensor) || (!state.IsTouch && inputs.IsButtonOn(state.Note.StartPosition));
        if (state.BodyGroup is { } group)
        {
            if (pressed) group.Pressed.Add(state.Note.Id);
            else group.Pressed.Remove(state.Note.Id);
            pressed |= group.Pressed.Count * 2 > group.ActiveCount;
        }
        if (pressed)
        {
            state.WaitRelease = 0;
            ChangeHoldState(state, 1, time, events);
        }
        else
        {
            if (state.HeadJudged && state.HoldState != 0 && state.WaitRelease <= 2 * Frame + BoundaryTolerance)
            {
                state.WaitRelease += delta;
                return;
            }
            state.ReleaseTime += state.WaitRelease + delta;
            state.WaitRelease = 0;
            ChangeHoldState(state, 0, time, events);
        }
    }

    private static void ChangeHoldState(Runtime state, int value, double time, List<PlaybackEvent> events)
    {
        if (state.HoldState == value) return;
        var initialSilence = state.HoldState == -1 && value == 0 && state.Grade == AchievementGrade.Miss;
        state.HoldState = value;
        if (initialSilence) return;
        events.Add(Event(value == 1 ? PlaybackEventKind.Held : PlaybackEventKind.Released, state, time));
    }

    private static void Finish(Runtime state, double time, List<PlaybackEvent> events)
    {
        state.Ended = true;
        if (state.BodyGroup is { } group)
        {
            group.Pressed.Remove(state.Note.Id);
            group.ActiveCount--;
        }
        events.Add(Event(PlaybackEvent.KindFor(state.Grade), state, time));
    }

    private static PlaybackEvent Event(PlaybackEventKind kind, Runtime state, double time) => new(kind, state.Note, time)
    {
        Grade = state.Grade,
        DifferenceSeconds = state.Difference,
        TimingKind = state.TimingKind,
        Reason = state.Reason ?? ""
    };

    // 各闭区间端点来自 NoteDrop.Judge；输入窗口由 CheckHead 单独控制。
    internal static AchievementGrade TapGrade(double difference, bool isEx = false)
    {
        if (isEx) return AchievementGrade.Perfect;
        var absolute = Math.Abs(difference);
        var step = absolute <= Frame + BoundaryTolerance ? 0 :
            absolute <= 2 * Frame + BoundaryTolerance ? 1 :
            absolute <= 3 * Frame + BoundaryTolerance ? 2 :
            absolute <= 4 * Frame + BoundaryTolerance ? 3 :
            absolute <= 5 * Frame + BoundaryTolerance ? 4 :
            absolute <= 6 * Frame + BoundaryTolerance ? 5 : 6;
        return (AchievementGrade)((int)AchievementGrade.Perfect + (difference < 0 ? step : -step));
    }

    internal static AchievementGrade TouchGrade(double difference)
    {
        var absolute = Math.Abs(difference);
        return absolute switch
        {
            <= 9 * Frame + BoundaryTolerance => AchievementGrade.Perfect,
            <= 10.5 * Frame + BoundaryTolerance => AchievementGrade.LatePerfect2nd,
            <= 12 * Frame + BoundaryTolerance => AchievementGrade.LatePerfect3rd,
            <= 13 * Frame + BoundaryTolerance => AchievementGrade.LateGreat,
            <= 14 * Frame + BoundaryTolerance => AchievementGrade.LateGreat2nd,
            <= 15 * Frame + BoundaryTolerance => AchievementGrade.LateGreat3rd,
            _ => AchievementGrade.LateGood
        };
    }

    // 对应 NoteLongDrop.HoldEndJudge。普通 Good 头可由完整持续提升到 Great；
    // CP 头在 33%～67% 为 Great2，其余 Perfect 子档在此比例为 Great1。
    internal static AchievementGrade HoldEndGrade(AchievementGrade head, double difference, double bodyLength, double released)
    {
        if (bodyLength <= BoundaryTolerance) return head;
        var percent = Math.Clamp((bodyLength - released) / bodyLength, 0, 1);
        var isFast = head > AchievementGrade.Perfect || (head == AchievementGrade.Perfect && difference < 0);
        var good = isFast ? AchievementGrade.FastGood : AchievementGrade.LateGood;
        var great = isFast ? AchievementGrade.FastGreat : AchievementGrade.LateGreat;
        if (head is AchievementGrade.Miss or AchievementGrade.TooFast)
            return percent >= .05 - BoundaryTolerance ? good : head;
        if (percent < .33 - BoundaryTolerance) return good;
        if (percent < .67 - BoundaryTolerance)
            return head == AchievementGrade.Perfect
                ? (isFast ? AchievementGrade.FastGreat2nd : AchievementGrade.LateGreat2nd) : great;
        if (head == AchievementGrade.Perfect && percent < 1 - BoundaryTolerance)
            return isFast ? AchievementGrade.FastPerfect2nd : AchievementGrade.LatePerfect2nd;
        if (head is >= AchievementGrade.LatePerfect3rd and <= AchievementGrade.FastPerfect3rd) return head;
        return great;
    }

    public void Reset()
    {
        foreach (var queue in _queues) queue.Current = 0;
        foreach (var group in _groups) group.Reset();
        foreach (var group in _bodyGroups) group.Reset();
        foreach (var state in _notes) state.Reset();
    }

    private sealed class Queue { public int Current; }

    private sealed class Runtime(ChartNote note)
    {
        public readonly ChartNote Note = note;
        public bool IsTouch => Note.Kind is NoteKind.Touch or NoteKind.TouchHold;
        public bool IsLong => Note.Kind is NoteKind.Hold or NoteKind.TouchHold;
        public readonly string Area = note.Kind is NoteKind.Touch or NoteKind.TouchHold ? note.TouchArea : $"A{note.StartPosition}";
        public readonly SensorArea Sensor = note.Kind is NoteKind.Touch or NoteKind.TouchHold
            ? Enum.Parse<SensorArea>(note.TouchArea) : (SensorArea)(note.StartPosition - 1);
        public Queue Queue = null!;
        public int QueueIndex;
        public Group? Group;
        public BodyGroup? BodyGroup;
        public bool HeadJudged;
        public bool Ended;
        public AchievementGrade Grade = AchievementGrade.Miss;
        public double? Difference;
        public string TimingKind = note.Kind is NoteKind.Hold or NoteKind.TouchHold ? "hold-head" : "head";
        public string? Reason;
        public int HoldState = -1;
        public double WaitRelease;
        public double ReleaseTime;

        public void Reset()
        {
            HeadJudged = Ended = false;
            Grade = AchievementGrade.Miss;
            Difference = null;
            TimingKind = IsLong ? "hold-head" : "head";
            Reason = null;
            HoldState = -1;
            WaitRelease = ReleaseTime = 0;
        }
    }

    private sealed class Group(int count)
    {
        private int _successes;
        public bool HasMajority => _successes * 2 > count;
        public AchievementGrade Grade;
        public double Difference;
        public void Register(AchievementGrade grade, double difference)
        {
            if (!HasMajority) Grade = grade;
            Difference = difference;
            _successes++;
        }
        public void Reset() { _successes = 0; Grade = AchievementGrade.Miss; Difference = 0; }
    }

    private sealed class BodyGroup(int count)
    {
        private readonly int _initialCount = count;
        public readonly HashSet<int> Pressed = new();
        public int ActiveCount = count;
        public void Reset() { Pressed.Clear(); ActiveCount = _initialCount; }
    }

    private static IEnumerable<Runtime[]> ConnectedGroups(IEnumerable<Runtime> notes)
    {
        var pending = notes.ToList();
        while (pending.Count != 0)
        {
            var members = new List<Runtime> { pending[0] };
            pending.RemoveAt(0);
            for (var i = 0; i < members.Count; i++)
            {
                var area = members[i].Area;
                for (var j = 0; j < pending.Count; j++)
                {
                    if (pending[j].Area != area && !TouchNeighbours[area].Contains(pending[j].Area)) continue;
                    members.Add(pending[j]);
                    pending.RemoveAt(j--);
                }
            }
            yield return members.ToArray();
        }
    }

    // NoteLoader.TOUCH_GROUPS 的原始邻接表；不是按显示坐标估算的相邻区域。
    private static readonly IReadOnlyDictionary<string, string[]> TouchNeighbours = new Dictionary<string, string[]>
    {
        ["A1"] = ["D1", "D2", "E1", "E2", "B1"],
        ["A2"] = ["D2", "D3", "E2", "E3", "B2"],
        ["A3"] = ["D3", "D4", "E3", "E4", "B3"],
        ["A4"] = ["D4", "D5", "E4", "E5", "B4"],
        ["A5"] = ["D5", "D6", "E5", "E6", "B5"],
        ["A6"] = ["D6", "D7", "E6", "E7", "B6"],
        ["A7"] = ["D7", "D8", "E7", "E8", "B7"],
        ["A8"] = ["D8", "D1", "E8", "E1", "B8"],
        ["D1"] = ["A1", "A8", "E1"], ["D2"] = ["A2", "A1", "E2"],
        ["D3"] = ["A3", "A2", "E3"], ["D4"] = ["A4", "A3", "E4"],
        ["D5"] = ["A5", "A4", "E5"], ["D6"] = ["A6", "A5", "E6"],
        ["D7"] = ["A7", "A6", "E7"], ["D8"] = ["A8", "A7", "E8"],
        ["E1"] = ["D1", "A1", "A8", "B1", "B8"],
        ["E2"] = ["D2", "A2", "A1", "B2", "B1"],
        ["E3"] = ["D3", "A3", "A2", "B3", "B2"],
        ["E4"] = ["D4", "A4", "A3", "B4", "B3"],
        ["E5"] = ["D5", "A5", "A4", "B5", "B4"],
        ["E6"] = ["D6", "A6", "A5", "B6", "B5"],
        ["E7"] = ["D7", "A7", "A6", "B7", "B6"],
        ["E8"] = ["D8", "A8", "A7", "B8", "B7"],
        ["B1"] = ["E1", "E2", "B8", "B2", "A1", "C"],
        ["B2"] = ["E2", "E3", "B1", "B3", "A2", "C"],
        ["B3"] = ["E3", "E4", "B2", "B4", "A3", "C"],
        ["B4"] = ["E4", "E5", "B3", "B5", "A4", "C"],
        ["B5"] = ["E5", "E6", "B4", "B6", "A5", "C"],
        ["B6"] = ["E6", "E7", "B5", "B7", "A6", "C"],
        ["B7"] = ["E7", "E8", "B6", "B8", "A7", "C"],
        ["B8"] = ["E8", "E1", "B7", "B1", "A8", "C"],
        ["C"] = ["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8"]
    };
}
