using MaiPlay.Audio;
using MaiPlay.Core;
using NAudio.Wave;

// Explicit Windows-device check, separate from ordinary unit tests. Both mixers are muted.
var temporary = Path.Combine(Path.GetTempPath(), $"maiplay-audio-{Guid.NewGuid():N}.wav");
try
{
    using (var wav = new WaveFileWriter(temporary, new WaveFormat(44100, 16, 1)))
        wav.Write(new byte[17640], 0, 17640); // 0.2 seconds of silence.

    using var player = new AudioPlayer { MusicVolume = 0 };
    Exception? deviceError = null;
    player.PlaybackFailed += e => deviceError = e;
    player.Load(temporary, 1.4);
    Check(Math.Abs(player.Duration - 1.4) < .001, "silence-tail duration");
    player.Play();
    Thread.Sleep(420); // Deliberately no UI updates while the device continues consuming PCM.
    double first = player.Position;
    Console.WriteLine($"Device clock after 420 ms (past WAV EOF): {first:F4} s");
    Check(first > .25 && first < .75, "device clock advances after music EOF without UI ticks");
    player.Pause();
    double paused = player.Position;
    Thread.Sleep(160);
    Check(player.Position == paused, "paused clock freezes");
    player.Play();
    Thread.Sleep(180);
    Check(player.Position > paused + .10, "resume advances from the paused output position");
    player.Restart();
    Check(player.Position == 0 && !player.IsPlaying, "restart discards buffered PCM and resets clock");
    player.Play();
    Thread.Sleep(1700);
    Check(Math.Abs(player.Position - 1.4) < .002 && !player.IsPlaying, "padded playback ends at duration");
    player.Restart(true);
    Thread.Sleep(120);
    Check(player.Position > 0 && player.Position < .5, "playing restart creates a fresh device clock");
    player.Pause();

    player.Seek(.65);
    Check(player.Position == .65 && !player.IsPlaying, "seek into padded tail keeps exact paused position");
    player.Play();
    Thread.Sleep(160);
    Check(player.Position > .74 && player.Position < 1.1, "seek resumes from output origin, not old queued PCM");
    player.Seek(.05);
    Check(player.Position == .05 && !player.IsPlaying, "backward seek clears the old device clock");
    player.Seek(player.Duration);
    Check(player.Position == player.Duration && !player.IsPlaying, "seek to EOF stays stopped");
    player.Seek(.1, true);
    Thread.Sleep(100);
    Check(player.Position > .1 && player.Position < .5, "seek away from EOF resumes normally");
    player.Pause();

    var previousContext = SynchronizationContext.Current;
    try
    {
        // A blocked UI synchronization context must never block audio completion or shutdown.
        SynchronizationContext.SetSynchronizationContext(new DeferredContext());
        using var deferred = new AudioPlayer { MusicVolume = 0 };
        deferred.Load(temporary);
        deferred.Play();
        Thread.Sleep(500);
        Check(!deferred.IsPlaying && Math.Abs(deferred.Position - deferred.Duration) < .002,
            "EOF completes with its final device position without waiting for a UI callback");
    }
    finally { SynchronizationContext.SetSynchronizationContext(previousContext); }

    player.Load(temporary, leadIn: .35);
    Check(player.StartPosition == -.35 && player.Position == -.35 && Math.Abs(player.Duration - .2) < .002,
        "lead-in starts at negative music time without changing the music end coordinate");
    player.Play();
    Thread.Sleep(120);
    Check(player.Position > -.35 && player.Position < 0, "device clock advances through negative lead-in");
    player.Pause();
    var negativePause = player.Position;
    Thread.Sleep(100);
    Check(player.Position == negativePause, "pause freezes negative time");
    player.Play();
    Thread.Sleep(330);
    Check(player.Position > 0, "resume crosses music time zero");
    player.Restart();
    Check(player.Position == -.35 && !player.IsPlaying, "restart restores negative starting position");
    player.Seek(-.15);
    Check(player.Position == -.15, "negative seek preserves music coordinates");
    player.Play();
    Thread.Sleep(230);
    Check(player.Position > 0 && player.Position < .2, "negative seek inserts only remaining lead-in silence");
    player.Restart();
    player.Play();
    Thread.Sleep(750);
    Check(!player.IsPlaying && Math.Abs(player.Position - .2) < .002, "lead-in ends at original music end time");

    using var sounds = new SoundEffects { Volume = 0 };
    sounds.PlaybackFailed += e => deviceError = e;
    sounds.Play();
    var hold = new ChartNote { Id = 1, Kind = NoteKind.TouchHold };
    sounds.Handle([new(PlaybackEventKind.Started, hold, 0)]);
    sounds.Pause();
    sounds.UseBuiltIn(); // Replacing the bank while paused preserves the active hold.
    sounds.Play();
    sounds.Handle([new(PlaybackEventKind.Perfect, hold, 1)]);
    sounds.Reset();
    sounds.Handle([new(PlaybackEventKind.Miss, new ChartNote { Id = 2, Kind = NoteKind.Tap }, 1)]);
    Console.WriteLine("PASS: independent SFX mixer, touch-hold lifecycle, pause/resume/bank replacement/reset");

    // Exercise the native worker teardown race, including immediately queued and paused workers.
    for (int i = 0; i < 40; i++)
    {
        sounds.Play();
        sounds.Handle([new(PlaybackEventKind.Started, hold, 0)]);
        if (i % 2 == 0) sounds.Pause();
        else Thread.Yield();
        sounds.Reset();
    }
    for (int i = 0; i < 20; i++)
    {
        player.Restart(true);
        if (i % 2 == 0) player.Pause();
        else Thread.Yield();
    }
    player.Restart();
    Check(deviceError == null, "rapid reset and restart wait for the native playback worker before freeing buffers");

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--sfx")
        {
            var missing = sounds.LoadDirectory(args[++i]);
            Console.WriteLine($"SFX imported; missing ({missing.Count}): {string.Join(", ", missing)}");
        }
        else
        {
            player.Load(args[i]);
            Check(player.Duration > 0 && player.Position == 0, $"decode and zero starting position: {args[i]}");
            Console.WriteLine($"Duration: {player.Duration:F3} s");
            player.Play();
            Thread.Sleep(120);
            Check(player.Position > 0, "decoded music advances on the device clock");
            player.Pause();
        }
    }
    Check(deviceError == null, $"no asynchronous device errors: {deviceError?.Message}");
    Console.WriteLine("Audio smoke passed. This checks device timing and decoding, not audible loudspeaker output.");
}
finally { File.Delete(temporary); }

static void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}");
}

sealed class DeferredContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state) { }
}
