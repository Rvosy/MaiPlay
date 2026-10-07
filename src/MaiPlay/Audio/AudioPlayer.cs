using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MaiPlay.Audio;

/// <summary>Music time is the number of PCM bytes consumed by the output device, never decoder read-ahead.</summary>
public sealed class AudioPlayer : IDisposable
{
    private WaveStream? _reader;
    private WaveOutput? _output;
    private VolumeSampleProvider? _volumeProvider;
    private int _bytesPerSecond;
    private float _volume = 0.75f;
    private double _pausedPosition;
    private double _outputOrigin;
    private volatile bool _ended;
    private bool _playing;

    public event Action<Exception>? PlaybackFailed;
    public double StartPosition { get; private set; }
    public double Duration { get; private set; }
    public bool IsPlaying => _playing && !_ended && _output?.PlaybackState == PlaybackState.Playing;
    // The native device can stop before its completion callback reaches the UI thread.
    // Keep its final byte position available during that interval instead of jumping to the last pause.
    public double Position => _ended ? Duration : _playing ? DevicePosition() : _pausedPosition;
    public float MusicVolume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); if (_volumeProvider != null) _volumeProvider.Volume = _volume; }
    }

    public void Load(string path, double minimumDuration = 0, double leadIn = 0)
    {
        DisposePlayback();
        Duration = 0;
        try
        {
            _reader = AudioDecoder.Open(path);
            StartPosition = -Math.Max(0, leadIn);
            Duration = Math.Max(_reader.TotalTime.TotalSeconds, minimumDuration);
            CreateOutput(StartPosition);
        }
        catch (Exception e)
        {
            DisposePlayback();
            throw new InvalidOperationException($"无法加载音乐“{Path.GetFileName(path)}”：{e.Message}", e);
        }
    }

    public void Play()
    {
        if (_output == null) throw new InvalidOperationException("请先加载歌曲音乐。");
        if (_ended) Restart();
        _output!.Play();
        _playing = true;
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        _output!.Pause();
        _pausedPosition = DevicePosition();
        _playing = false;
    }

    public void Restart(bool play = false)
        => Seek(StartPosition, play);

    public void Seek(double time, bool play = false)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (_reader == null) return;
        time = Math.Clamp(time, StartPosition, Duration);
        DisposeOutput();
        _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(time, 0, _reader.TotalTime.TotalSeconds));
        CreateOutput(time);
        _ended = time >= Duration;
        if (play && !_ended) Play();
    }

    private void CreateOutput(double origin)
    {
        _pausedPosition = _outputOrigin = origin;
        _ended = false;
        var prefixed = new SilencePrefixProvider(_reader!.ToSampleProvider(), Math.Max(0, -origin));
        var source = new SilenceTailProvider(prefixed, Duration - origin);
        _volumeProvider = new VolumeSampleProvider(source) { Volume = _volume };
        var pcm = new SampleToWaveProvider16(_volumeProvider);
        _bytesPerSecond = pcm.WaveFormat.AverageBytesPerSecond;
        var output = new WaveOutput(pcm, 50);
        _output = output;
        output.PlaybackStopped += args =>
        {
            // Stop/Restart may dispatch an old device's completion after it was replaced.
            if (!ReferenceEquals(_output, output)) return;
            if (args.Exception != null)
            {
                _pausedPosition = DevicePosition();
                _playing = false;
                PlaybackFailed?.Invoke(args.Exception);
            }
            else _ended = true;
        };
    }

    private double DevicePosition() => Math.Clamp(_outputOrigin + (_output?.GetPosition() ?? 0) / (double)_bytesPerSecond,
        StartPosition, Duration);

    private void DisposeOutput()
    {
        var old = _output;
        _output = null;
        _playing = false;
        old?.Dispose();
    }

    private void DisposePlayback()
    {
        DisposeOutput();
        _reader?.Dispose();
        _reader = null;
        _volumeProvider = null;
        StartPosition = 0;
        _pausedPosition = 0;
        _ended = false;
    }

    public void Dispose() => DisposePlayback();
}

internal static class AudioDecoder
{
    internal static WaveStream Open(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ogg" or ".oga" => new VorbisWaveReader(path),
        _ => new AudioFileReader(path)
    };
}

/// <summary>Use the same output clock for the notes' entrance before music time zero.</summary>
internal sealed class SilencePrefixProvider(ISampleProvider source, double duration) : ISampleProvider
{
    private long _remaining = (long)Math.Ceiling(duration * source.WaveFormat.SampleRate) * source.WaveFormat.Channels;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int silence = (int)Math.Min(count, _remaining);
        Array.Clear(buffer, offset, silence);
        _remaining -= silence;
        if (silence == count) return silence;
        return silence + source.Read(buffer, offset + silence, count - silence);
    }
}

/// <summary>Keep the device clock running through late judgements after a short music file ends.</summary>
internal sealed class SilenceTailProvider(ISampleProvider source, double duration) : ISampleProvider
{
    private readonly long _length = (long)Math.Ceiling(duration * source.WaveFormat.SampleRate) * source.WaveFormat.Channels;
    private long _read;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        count = (int)Math.Min(count, _length - _read);
        if (count == 0) return 0;
        int read = source.Read(buffer, offset, count);
        if (read < count) Array.Clear(buffer, offset + read, count - read);
        _read += count;
        return count;
    }
}
