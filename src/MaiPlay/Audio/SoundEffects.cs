using MaiPlay.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MaiPlay.Audio;

/// <summary>Independent sound bank and mixer. Skin selection never changes the sounds.</summary>
public sealed class SoundEffects : IDisposable
{
    public static readonly string[] FileNames =
    [
        "tap_perfect.wav", "tap_great.wav", "tap_good.wav", "tap_ex.wav", "break_tap.wav", "break.wav",
        "slide.wav", "slide_break_start.wav", "slide_break_slide.wav", "touch.wav", "touch_Hold_riser.wav",
        "touch_hanabi.wav", "answer.wav", "answer_clock.wav", "answer_mine.wav", "tap_miss.wav"
    ];
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private readonly MixingSampleProvider _mixer = new(Format) { ReadFully = true };
    private readonly VolumeSampleProvider _volumeProvider;
    private Dictionary<string, SoundClip> _clips = BuiltInSounds.Create();
    private readonly HashSet<int> _touchHolds = [];
    private WaveOutput? _output;
    private ClipVoice? _riser;
    private bool _paused = true;
    private bool _enabled = true;
    private float _volume = 0.65f;

    public event Action<Exception>? PlaybackFailed;
    public float Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); UpdateVolume(); }
    }
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; UpdateVolume(); SyncRiser(); }
    }

    public SoundEffects() => _volumeProvider = new VolumeSampleProvider(_mixer) { Volume = _volume };

    /// <returns>Missing filenames that use the original MaiPlay synthesized fallback.</returns>
    public IReadOnlyList<string> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"音效目录不存在：{directory}");
        var next = BuiltInSounds.Create();
        var missing = new List<string>();
        int imported = 0;
        foreach (var name in FileNames)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) { missing.Add(name); continue; }
            try { next[name] = Decode(path); imported++; }
            catch (Exception e) { throw new InvalidDataException($"无法解码音效“{name}”：{e.Message}", e); }
        }
        if (imported == 0) throw new InvalidDataException("此目录未包含 Majdata 音效文件（例如 tap_perfect.wav）。");
        ReplaceBank(next);
        return missing;
    }

    public void UseBuiltIn()
    {
        ReplaceBank(BuiltInSounds.Create());
    }

    public void Handle(IEnumerable<PlaybackEvent> events)
    {
        // Equal logical times coalesce, independently of how many updates the UI skipped.
        foreach (var group in events.GroupBy(e => e.Time))
        {
            var requests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in group)
            {
                if (e.Note.Kind == NoteKind.TouchHold)
                {
                    if (e.Kind is PlaybackEventKind.Started or PlaybackEventKind.Held) _touchHolds.Add(e.Note.Id);
                    else if (e.Kind == PlaybackEventKind.Released || e.IsFinal) _touchHolds.Remove(e.Note.Id);
                }
                requests.UnionWith(GetOneShotClips(e));
            }
            if (_enabled)
                foreach (var name in requests) _mixer.AddMixerInput(new ClipVoice(_clips[name]));
        }
        SyncRiser();
    }

    // Adapted from MajdataPlay dc19722 NoteAudioManager, HoldDrop.End,
    // TouchHoldDrop.End, TouchDrop.PlayJudgeSFX and SlideBase.PlayJudgeSFX (GPL-3.0).
    public static IReadOnlyList<string> GetOneShotClips(PlaybackEvent e)
    {
        if (e.Grade is AchievementGrade.Miss or AchievementGrade.TooFast ||
            e.Kind is PlaybackEventKind.Held or PlaybackEventKind.Released) return [];
        var note = e.Note;
        var clips = new List<string>();
        if (e.Kind == PlaybackEventKind.Started)
        {
            if (note.Kind == NoteKind.Hold) AddTap(clips, e.Grade, note.IsBreak, note.IsEx);
            else if (note.Kind == NoteKind.Slide) clips.Add(note.IsBreak ? "slide_break_start.wav" : "slide.wav");
            else if (note.Kind == NoteKind.TouchHold && e.TimingKind == "ideal")
            {
                AddTap(clips, e.Grade, note.IsBreak, note.IsEx);
                if (note.IsFirework) clips.Add("touch_hanabi.wav");
            }
            return clips;
        }
        switch (note.Kind)
        {
            case NoteKind.Tap:
                AddTap(clips, e.Grade, note.IsBreak, note.IsEx);
                break;
            case NoteKind.Touch:
                if (note.IsBreak) AddTap(clips, e.Grade, true, note.IsEx);
                else clips.Add("touch.wav");
                break;
            case NoteKind.Hold:
            case NoteKind.TouchHold:
                AddTap(clips, e.Grade, false, false);
                break;
            case NoteKind.Slide when note.IsBreak && e.Grade == AchievementGrade.Perfect:
                clips.Add("slide_break_slide.wav");
                clips.Add("break.wav");
                break;
        }
        if (note.IsFirework && note.Kind is NoteKind.Touch or NoteKind.TouchHold) clips.Add("touch_hanabi.wav");
        return clips;
    }

    public void Play()
    {
        EnsureOutput();
        _paused = false;
        _output!.Play();
    }

    public void Pause()
    {
        _paused = true;
        _output?.Pause();
    }

    public void Reset()
    {
        // Drop queued audio as well as mixer voices, so Restart cannot replay a stale hit.
        var old = _output;
        _output = null;
        old?.Dispose();
        _mixer.RemoveAllMixerInputs();
        _touchHolds.Clear();
        _riser = null;
        _paused = true;
    }

    private void ReplaceBank(Dictionary<string, SoundClip> clips)
    {
        var holds = _touchHolds.ToArray();
        bool playing = !_paused;
        Reset();
        _clips = clips;
        _touchHolds.UnionWith(holds);
        SyncRiser();
        if (playing) Play();
    }

    private static void AddTap(List<string> requests, AchievementGrade grade, bool isBreak, bool isEx)
    {
        if (isBreak)
        {
            requests.Add("break_tap.wav");
            if (grade == AchievementGrade.Perfect) requests.Add("break.wav");
        }
        else requests.Add(isEx ? "tap_ex.wav" : PlaybackEvent.KindFor(grade) switch
        {
            PlaybackEventKind.Good => "tap_good.wav",
            PlaybackEventKind.Great => "tap_great.wav",
            _ => "tap_perfect.wav"
        });
    }

    private void UpdateVolume() => _volumeProvider.Volume = _enabled ? _volume : 0;

    private void SyncRiser()
    {
        bool active = _enabled && _touchHolds.Count > 0;
        if (active && _riser == null)
        {
            _riser = new ClipVoice(_clips["touch_Hold_riser.wav"]);
            _mixer.AddMixerInput(_riser);
        }
        else if (!active && _riser != null)
        {
            _mixer.RemoveMixerInput(_riser);
            _riser = null;
        }
    }

    private void EnsureOutput()
    {
        if (_output != null) return;
        var output = new WaveOutput(new SampleToWaveProvider16(_volumeProvider), 30);
        output.PlaybackStopped += e =>
        {
            if (e.Exception != null) PlaybackFailed?.Invoke(e.Exception);
        };
        _output = output;
    }

    private static SoundClip Decode(string path)
    {
        using var reader = AudioDecoder.Open(path);
        if (reader.TotalTime > TimeSpan.FromMinutes(1)) throw new InvalidDataException("单个音效超过 60 秒。");
        ISampleProvider samples = reader.ToSampleProvider();
        samples = samples.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(samples),
            2 => samples,
            _ => throw new InvalidDataException("音效需为单声道或双声道。")
        };
        if (samples.WaveFormat.SampleRate != Format.SampleRate)
            samples = new WdlResamplingSampleProvider(samples, Format.SampleRate);
        var data = new List<float>();
        var buffer = new float[8192];
        int count;
        while ((count = samples.Read(buffer, 0, buffer.Length)) != 0) data.AddRange(buffer.AsSpan(0, count));
        if (data.Count == 0) throw new InvalidDataException("音效文件没有音频数据。");
        return new SoundClip(data.ToArray());
    }

    public void Dispose()
    {
        _paused = true;
        Reset();
    }

    internal sealed record SoundClip(float[] Samples, bool Loop = false);

    private sealed class ClipVoice(SoundClip clip) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat => Format;

        public int Read(float[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = Math.Min(count - total, clip.Samples.Length - _position);
                Array.Copy(clip.Samples, _position, buffer, offset + total, n);
                _position += n;
                total += n;
                if (_position == clip.Samples.Length)
                {
                    if (!clip.Loop) break;
                    _position = 0;
                }
            }
            return total;
        }
    }
}

/// <summary>Original procedural tones; no upstream audio files are redistributed.</summary>
internal static class BuiltInSounds
{
    internal static Dictionary<string, SoundEffects.SoundClip> Create()
    {
        var clips = new Dictionary<string, SoundEffects.SoundClip>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < SoundEffects.FileNames.Length; i++)
        {
            string name = SoundEffects.FileNames[i];
            bool riser = name == "touch_Hold_riser.wav";
            bool sweep = name.StartsWith("slide") || name == "touch_hanabi.wav";
            double duration = riser ? 2 : sweep ? 0.25 : name == "break.wav" ? 0.3 : 0.08;
            double frequency = riser ? 220 : name == "tap_miss.wav" ? 110 : 600 + i * 71;
            int frames = (int)(duration * 44100);
            var data = new float[frames * 2];
            double phase = 0;
            for (int j = 0; j < frames; j++)
            {
                double t = j / 44100d;
                double envelope = riser ? 0.1 : Math.Min(1, t / 0.003) * Math.Exp(-t * 6 / duration) * 0.3;
                phase += 2 * Math.PI * frequency * (sweep ? 1 + t / duration : 1) / 44100;
                float value = (float)((Math.Sin(phase) + 0.2 * Math.Sin(phase * 2)) * envelope);
                data[j * 2] = data[j * 2 + 1] = value;
            }
            clips[name] = new SoundEffects.SoundClip(data, riser);
        }
        return clips;
    }
}
