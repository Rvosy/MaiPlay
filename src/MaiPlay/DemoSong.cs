namespace MaiPlay;

public static class DemoSong
{
    public static string Ensure(string dataDirectory)
    {
        var directory = Path.Combine(dataDirectory, "Demo");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(AppContext.BaseDirectory, "samples", "Demo", "maidata.txt");
        var chartPath = Path.Combine(directory, "maidata.txt");
        File.Copy(source, chartPath, true);
        var music = Path.Combine(directory, "track.wav");
        if (!File.Exists(music)) WriteMusic(music);
        return chartPath;
    }

    // Original 120 BPM audio fixture: quiet metronome and a four-chord synth phrase.
    private static void WriteMusic(string path)
    {
        const int rate = 44100, seconds = 48, count = rate * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(count * 2);
        double[] roots = [220, 174.614, 261.626, 195.998];
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)rate;
            var beatTime = t % .5;
            var root = roots[(int)(t / 4) % roots.Length];
            var envelope = Math.Min(t, Math.Min(1, seconds - t));
            var chord = .035 * (Math.Sin(2 * Math.PI * root * t) + .5 * Math.Sin(2 * Math.PI * root * 1.5 * t));
            var click = beatTime < .055 ? .12 * Math.Sin(2 * Math.PI * 1100 * beatTime) * Math.Exp(-beatTime * 85) : 0;
            writer.Write((short)((chord + click) * envelope * short.MaxValue));
        }
    }
}
