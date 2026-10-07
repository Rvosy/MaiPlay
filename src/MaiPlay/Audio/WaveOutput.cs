using NAudio.Wave;

namespace MaiPlay.Audio;

/// <summary>NAudio 2.2.1 does not join its playback worker in Dispose; wait before freeing native buffers.</summary>
internal sealed class WaveOutput : IDisposable
{
    private readonly WaveOutEvent _device;
    private TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<StoppedEventArgs>? PlaybackStopped;
    public PlaybackState PlaybackState => _device.PlaybackState;
    public long GetPosition() => _device.GetPosition();

    public WaveOutput(IWaveProvider source, int latency)
    {
        // The completion signal must run on the audio worker. A UI-posted callback
        // cannot complete while the UI is synchronously stopping or restarting playback.
        var context = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            _device = new WaveOutEvent { DesiredLatency = latency, NumberOfBuffers = 3 };
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }

        _stopped.SetResult();
        _device.PlaybackStopped += (_, args) =>
        {
            try { PlaybackStopped?.Invoke(args); }
            finally { _stopped.TrySetResult(); }
        };
        try { _device.Init(source); }
        catch { _device.Dispose(); throw; }
    }

    public void Play()
    {
        if (_device.PlaybackState == PlaybackState.Stopped)
        {
            _stopped.Task.GetAwaiter().GetResult();
            _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        _device.Play();
    }

    public void Pause() => _device.Pause();

    public void Dispose()
    {
        _device.Stop();
        _stopped.Task.GetAwaiter().GetResult();
        _device.Dispose();
    }
}
