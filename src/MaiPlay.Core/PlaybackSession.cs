namespace MaiPlay.Core;

/// <summary>Audio-clock replay with a fixed logic lattice, independent of render/update rate.</summary>
public sealed class PlaybackSession
{
    public const int LogicHz = 240;
    public const double LogicStep = 1d / LogicHz;
    private readonly InputEvent[] _input;
    private readonly List<InputEvent> _sample = [];
    private readonly List<InputEvent> _inputHistory = [];
    private readonly PlaybackEvent[] _autoTimeline;
    private readonly PlaybackEvent?[] _results;
    private readonly NoteState[] _states;
    private readonly HeadJudgements _heads;
    private readonly SlideJudgements _slides;
    private readonly int[] _gradeCounts = new int[15];
    private readonly long _firstTick;
    private long _nextTick;
    private int _inputCursor, _autoCursor;

    public Chart Chart { get; }
    public PlayMode Mode { get; }
    public AchievementState Achievement { get; }
    public InputState Inputs { get; } = new();
    public IReadOnlyList<InputEvent> InputHistory => _inputHistory;
    public IReadOnlyList<SlideTrace> SlideTrace => _slides.Trace;
    public IReadOnlyList<int> GradeCounts => _gradeCounts;
    public double Time { get; private set; } = double.NegativeInfinity;
    public int Combo { get; private set; }
    public int Completed { get; private set; }
    public int Missed { get; private set; }
    public int Remaining => Chart.Notes.Count - Completed - Missed;
    public int DXScore { get; private set; }
    public int MaximumDXScore => Chart.Notes.Count * 3;
    public int FastCount { get; private set; }
    public int LateCount { get; private set; }
    public PlaybackEvent? LastEvent { get; private set; }
    public double FinishTime { get; }

    public PlaybackSession(Chart chart, PlayMode mode, IEnumerable<InputEvent>? input = null)
    {
        Chart = chart;
        Mode = mode;
        _input = input?.ToArray() ?? [];
        InputLog.Validate(_input);
        Achievement = new AchievementState(chart);
        _states = new NoteState[chart.Notes.Count];
        _results = new PlaybackEvent?[chart.Notes.Count];
        _heads = new HeadJudgements(chart);
        _slides = new SlideJudgements(chart);
        // 首个音符窗口和首条输入之前，所有区域与判定状态都保持初始值。
        var first = chart.Notes.Count == 0 ? 0 : chart.Notes.Min(n => n.Time) - .5;
        if (_input.Length > 0) first = Math.Min(first, _input[0].Time);
        _firstTick = (long)Math.Floor(first * LogicHz);
        _nextTick = _firstTick;
        FinishTime = Math.Max(chart.Duration, chart.Notes.Count == 0 ? 0 : chart.Notes.Max(MissDeadline) + 2 * LogicStep);
        _autoTimeline = mode == PlayMode.AutoPlay ? chart.Notes.SelectMany(n =>
        {
            var end = new PlaybackEvent(PlaybackEventKind.Perfect, n, n.EndTime);
            return n.Kind is NoteKind.Hold or NoteKind.TouchHold or NoteKind.Slide
                ? new[] { new PlaybackEvent(PlaybackEventKind.Started, n, n.Kind == NoteKind.Slide ? n.SlideStartTime : n.Time), end }
                : new[] { end };
        }).OrderBy(e => e.Time).ThenBy(e => e.Kind).ThenBy(e => e.Note.Id).ToArray() : [];
    }

    /// <summary>Upper bound for no-input retirement; strict windows expire on the next logic sample.</summary>
    public static double MissDeadline(ChartNote note) => note.Kind switch
    {
        NoteKind.Tap => note.Time + .15,
        NoteKind.Touch => note.Time + .3,
        NoteKind.Hold => Math.Max(note.EndTime, note.Time + .15),
        NoteKind.TouchHold => Math.Max(note.EndTime, note.Time + .3),
        NoteKind.Slide => note.EndTime + .6,
        _ => note.EndTime
    };

    public PlaybackEvent? GetResult(ChartNote note) => _results[note.Id];
    public NoteState GetState(ChartNote note)
    {
        var state = _states[note.Id];
        if (state != NoteState.Pending) return state;
        return Time >= (note.Kind == NoteKind.Slide ? note.SlideStartTime : note.Time) ? NoteState.Active : NoteState.Pending;
    }
    public bool IsHolding(ChartNote note) => Mode == PlayMode.AutoPlay
        ? Time >= note.Time && Time < note.EndTime && note.Kind is NoteKind.Hold or NoteKind.TouchHold
        : _heads.IsHolding(note);
    public double GetSlideProgress(ChartNote note, int branch = 0) => Mode == PlayMode.AutoPlay
        ? note.SlidePath!.DistanceProgress((Time - note.SlideStartTime) / note.Duration)
        : _slides.GetProgress(note, branch);

    public IReadOnlyList<PlaybackEvent> Advance(double time)
    {
        // Device position may briefly retreat. Only Reset starts another playthrough.
        if (time <= Time) return Array.Empty<PlaybackEvent>();
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        var output = new List<PlaybackEvent>();
        if (Mode == PlayMode.AutoPlay)
        {
            while (_autoCursor < _autoTimeline.Length && _autoTimeline[_autoCursor].Time <= time)
                output.Add(_autoTimeline[_autoCursor++]);
        }
        else
        {
            // Integer tick arithmetic avoids accumulating dt rounding across successive Advance calls.
            var lastTick = (long)Math.Floor(time * LogicHz + 1e-9);
            while (_nextTick <= lastTick)
            {
                var now = _nextTick++ / (double)LogicHz;
                _sample.Clear();
                while (_inputCursor < _input.Length && _input[_inputCursor].Time <= now + 1e-12)
                {
                    var change = _input[_inputCursor++];
                    _sample.Add(change);
                    _inputHistory.Add(change);
                }
                Inputs.Sample(_sample);
                _heads.ProcessTaps(now, Inputs, output);
                _heads.ProcessHolds(now, LogicStep, Inputs, output);
                _slides.Process(now, Inputs, output);
                _heads.ProcessTouches(now, Inputs, output);
                _heads.ProcessTouchHolds(now, LogicStep, Inputs, output);
            }
        }
        Time = time;
        foreach (var item in output) Apply(item);
        return output;
    }

    private void Apply(PlaybackEvent item)
    {
        if (!item.IsFinal)
        {
            if (item.Kind == PlaybackEventKind.Started) _states[item.Note.Id] = NoteState.Active;
            return;
        }
        if (_results[item.Note.Id] != null) throw new InvalidOperationException($"Note {item.Note.Id} settled twice.");
        _results[item.Note.Id] = LastEvent = item;
        Achievement.Apply(item.Note, item.Grade);
        _gradeCounts[(int)item.Grade]++;
        DXScore += item.Grade == AchievementGrade.Perfect ? 3 : item.Kind switch
        {
            PlaybackEventKind.Perfect => 2,
            PlaybackEventKind.Great => 1,
            _ => 0
        };
        if (item.Kind == PlaybackEventKind.Miss)
        {
            _states[item.Note.Id] = NoteState.Miss;
            Missed++;
            Combo = 0;
        }
        else
        {
            _states[item.Note.Id] = item.Kind switch
            {
                PlaybackEventKind.Great => NoteState.Great,
                PlaybackEventKind.Good => NoteState.Good,
                _ => NoteState.Perfect
            };
            Completed++;
            Combo++;
            // Upstream BelowCP display: retain the measured difference but do not count CP.
            if (item.Grade != AchievementGrade.Perfect)
            {
                if (item.IsFast) FastCount++;
                else LateCount++;
            }
        }
    }

    public void Seek(double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        Reset();
        Advance(time);
    }

    public void Reset()
    {
        Array.Clear(_states);
        Array.Clear(_results);
        Array.Clear(_gradeCounts);
        _autoCursor = _inputCursor = Combo = Completed = Missed = DXScore = FastCount = LateCount = 0;
        _nextTick = _firstTick;
        Time = double.NegativeInfinity;
        LastEvent = null;
        _inputHistory.Clear();
        Inputs.Reset();
        _heads.Reset();
        _slides.Reset();
        Achievement.Reset();
    }
}
