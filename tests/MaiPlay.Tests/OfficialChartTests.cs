using System.Collections.Concurrent;
using System.Text.Json;
using MaiPlay.Core;

namespace MaiPlay.Tests;

public class OfficialChartTests
{
    public static IEnumerable<object[]> TouchAreas() =>
        new[] { 'A', 'B', 'D', 'E' }.SelectMany(region => Enumerable.Range(1, 8).Select(position => new object[] { $"{region}{position}" }));

    [Theory]
    [MemberData(nameof(TouchAreas))]
    public void NonCentralTouchHoldUsesItsOwnRegionForTheEntireHold(string area)
    {
        var chart = SimaiParser.ParseChart($"(120){{4}}{area}hf[#1],E", first: 1);
        var note = Assert.Single(chart.Notes);
        Assert.Equal(NoteKind.TouchHold, note.Kind);
        Assert.Equal(area, note.TouchArea);
        Assert.True(note.IsFirework);
        Assert.Equal(1, note.Time);
        Assert.Equal(2, note.EndTime);

        var correct = Play(chart, new InputEvent(1, InputChannel.Touch, area, true));
        Assert.Equal(AchievementGrade.Perfect, correct.GetResult(note)!.Grade);

        var wrong = Play(chart, new InputEvent(1, InputChannel.Touch, "C", true));
        Assert.Equal(AchievementGrade.Miss, wrong.GetResult(note)!.Grade);

        var released = Play(chart, new InputEvent(1, InputChannel.Touch, area, true),
            new InputEvent(1.1, InputChannel.Touch, area, false));
        Assert.NotEqual(AchievementGrade.Perfect, released.GetResult(note)!.Grade);

        var restored = Play(chart, new InputEvent(1, InputChannel.Touch, area, true),
            new InputEvent(1.1, InputChannel.Touch, area, false), new InputEvent(1.2, InputChannel.Touch, area, true));
        Assert.Equal(AchievementGrade.Perfect, restored.GetResult(note)!.Grade);
    }

    [Fact]
    [Trait("Category", "OfficialLibrary")]
    public void ConvertedOfficialLibraryLoadsAudioAndSettlesEveryNoteInBothModes()
    {
        // The game assets remain local. Opt in to this full-library check explicitly.
        var root = Environment.GetEnvironmentVariable("MAIPLAY_OFFICIAL_CHARTS");
        if (string.IsNullOrWhiteSpace(root)) return;
        root = Path.GetFullPath(root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "report.json")));
        var entries = manifest.RootElement.GetProperty("songs").EnumerateArray().ToArray();
        Assert.NotEmpty(entries);
        var failures = new ConcurrentBag<string>();
        var songs = 0; var charts = 0; var notes = 0;
        Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) }, entry =>
        {
            var relative = entry.GetProperty("directory").GetString()!;
            try
            {
                var song = SimaiParser.LoadSong(Path.Combine(root, relative));
                Assert.NotNull(song.AudioPath);
                Assert.True(new FileInfo(song.AudioPath).Length > 0);
                Assert.Equal(entry.GetProperty("charts").GetArrayLength(), song.Charts.Count);
                foreach (var chart in song.Charts)
                {
                    var expected = entry.GetProperty("charts").EnumerateArray()
                        .Single(c => c.GetProperty("difficulty").GetInt32() == chart.Difficulty).GetProperty("stats");
                    Assert.Equal(expected.GetProperty("count").GetInt32(), chart.Notes.Count);
                    foreach (var mode in Enum.GetValues<PlayMode>())
                    {
                        var session = new PlaybackSession(chart, mode);
                        session.Advance(session.FinishTime + .1);
                        Assert.Equal(0, session.Remaining);
                        Assert.Equal(chart.Notes.Count, mode == PlayMode.AutoPlay ? session.Completed : session.Missed);
                        Assert.All(chart.Notes, n => Assert.NotNull(session.GetResult(n)));
                    }
                    Interlocked.Increment(ref charts);
                    Interlocked.Add(ref notes, chart.Notes.Count);
                }
                Interlocked.Increment(ref songs);
            }
            catch (Exception e) { failures.Add(relative + ": " + e.Message); }
        });
        var output = JsonSerializer.Serialize(new { songs, charts, notes, failures = failures.Order().ToArray() },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "playback-validation.json"), output);
        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Order()));
    }

    private static PlaybackSession Play(Chart chart, params InputEvent[] events)
    {
        var session = new PlaybackSession(chart, PlayMode.Algorithm, events);
        session.Advance(session.FinishTime + .1);
        return session;
    }
}
