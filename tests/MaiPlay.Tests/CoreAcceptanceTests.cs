using MaiPlay.Core;

namespace MaiPlay.Tests;

public class CoreAcceptanceTests
{
    [Fact]
    public void IncludedDemoParsesBothDifficulties()
    {
        var root = FindRoot();
        Assert.NotNull(root);
        var song = SimaiParser.LoadSong(Path.Combine(root, "samples", "Demo"));
        Assert.Equal(2, song.Charts.Count);
        Assert.All(song.Charts, c => Assert.NotEmpty(c.Notes));
    }

    [Fact]
    public void OptionalUserFixturesMatchIndependentlyCheckedCountsAndFinishInBothModes()
    {
        var root = FindRoot();
        var directory = Environment.GetEnvironmentVariable("MAIPLAY_CHART_FIXTURES")
            ?? (root == null ? "" : Path.Combine(root, ".reference", "acceptance"));
        if (!Directory.Exists(directory)) return; // Copyrighted user songs are intentionally not distributed.
        var expected = new Dictionary<string, int[]>
        {
            ["11160"] = [221, 375, 647, 991], ["11377"] = [207, 406, 673, 946],
            ["226"] = [208, 485, 495, 608, 787], ["239"] = [142, 249, 507, 777, 753]
        };
        foreach (var (id, counts) in expected)
        {
            var path = Path.Combine(directory, id, "maidata.txt");
            Assert.True(File.Exists(path), $"Missing optional acceptance fixture {path}");
            var song = SimaiParser.LoadSong(path);
            Assert.Equal(counts, song.Charts.Select(c => c.Notes.Count));
            foreach (var chart in song.Charts)
            {
                Assert.All(chart.Notes.Where(n => n.Kind == NoteKind.Slide), n =>
                    Assert.True(double.IsFinite(n.SlidePath!.Length) && n.Duration > 0));
                foreach (var mode in Enum.GetValues<PlayMode>())
                {
                    var session = new PlaybackSession(chart, mode);
                    for (var tick = 0; tick < (chart.Duration + 2) * 73; tick++)
                        session.Advance(tick / 73.0);
                    Assert.Equal(0, session.Remaining);
                    Assert.Equal(chart.Notes.Count, mode == PlayMode.AutoPlay ? session.Completed : session.Missed);
                    Assert.Equal(0, mode == PlayMode.AutoPlay ? session.Missed : session.Completed);
                }
            }
        }
    }

    private static string? FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MaiPlay.slnx")) && !File.Exists(Path.Combine(directory.FullName, "MaiPlay.sln")))
            directory = directory.Parent;
        return directory?.FullName;
    }
}
