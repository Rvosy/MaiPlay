namespace MaiPlay.Core;

// Sensor order and frame latch semantics adapted from TeamMajdata/MajdataPlay
// dc19722d602f099131d93b37091624ad30150ad1, IO/Base/Enums/SensorArea.cs,
// InputManager.DequeueSensor.cs and NoteManager.GameIOUpdate. GPL-3.0.
public enum SensorArea
{
    A1, A2, A3, A4, A5, A6, A7, A8,
    B1, B2, B3, B4, B5, B6, B7, B8, C,
    D1, D2, D3, D4, D5, D6, D7, D8,
    E1, E2, E3, E4, E5, E6, E7, E8
}

public enum InputChannel { Button, Touch }

/// <summary>Music seconds, including the chart's first offset. Equal times retain file order.</summary>
public sealed record InputEvent(double Time, InputChannel Channel, string Area, bool IsDown, string Source = "log");

/// <summary>Global input shared by every note. Sources overlap without releasing each other.</summary>
public sealed class InputState
{
    private readonly HashSet<string>[] _sources = Enumerable.Range(0, 41).Select(_ => new HashSet<string>()).ToArray();
    private readonly HashSet<string>[] _activeSources = Enumerable.Range(0, 41).Select(_ => new HashSet<string>()).ToArray();
    private readonly bool[] _on = new bool[41];
    private readonly bool[] _clicked = new bool[41];
    private readonly bool[] _used = new bool[41];

    public bool IsButtonOn(int button) => _on[button - 1];
    public bool IsSensorOn(SensorArea area) => _on[8 + (int)area];
    public bool IsSensorOn(string area) => IsSensorOn(Enum.Parse<SensorArea>(area));
    public IReadOnlySet<string> ButtonSources(int button) => _activeSources[button - 1];
    public IReadOnlySet<string> SensorSources(SensorArea area) => _activeSources[8 + (int)area];
    public bool TryUseButtonClick(int button) => TryUse(button - 1);
    public bool TryUseSensorClick(SensorArea area) => TryUse(8 + (int)area);
    public bool TryUseSensorClick(string area) => TryUseSensorClick(Enum.Parse<SensorArea>(area));

    internal void Sample(IEnumerable<InputEvent> changes)
    {
        foreach (var sources in _activeSources) sources.Clear();
        foreach (var change in changes)
        {
            var index = change.Channel == InputChannel.Button ? int.Parse(change.Area) - 1 : 8 + (int)Enum.Parse<SensorArea>(change.Area);
            if (change.IsDown)
            {
                _sources[index].Add(change.Source);
                _activeSources[index].Add(change.Source);
            }
            else _sources[index].Remove(change.Source);
        }
        for (var i = 0; i < _on.Length; i++)
        {
            _activeSources[i].UnionWith(_sources[i]);
            var next = _activeSources[i].Count != 0;
            _clicked[i] = !_on[i] && next;
            _on[i] = next;
            _used[i] = false;
        }
    }

    private bool TryUse(int index)
    {
        if (!_clicked[index] || _used[index]) return false;
        _used[index] = true;
        return true;
    }

    internal void Reset()
    {
        foreach (var sources in _sources) sources.Clear();
        foreach (var sources in _activeSources) sources.Clear();
        Array.Clear(_on);
        Array.Clear(_clicked);
        Array.Clear(_used);
    }
}
