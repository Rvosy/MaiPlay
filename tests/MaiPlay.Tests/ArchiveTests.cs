using System.IO.Compression;

namespace MaiPlay.Tests;

public sealed class ArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MaiPlay-tests-" + Guid.NewGuid().ToString("N"));

    public ArchiveTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("folder/../../escaped.txt")]
    [InlineData("C:/escaped.txt")]
    [InlineData("/escaped.txt")]
    public void RejectsUnsafePathsBeforeWritingAnything(string name)
    {
        var zip = CreateZip(("maidata.txt", "&inote_5=(120){4}1,E"), (name, "unsafe"));
        Assert.Throws<InvalidDataException>(() => SongArchive.Import(zip, Path.Combine(_root, "cache")));
        Assert.False(Directory.Exists(Path.Combine(_root, "cache")));
    }

    [Fact]
    public void OpensNestedSongAndPreservesUtf8Text()
    {
        const string text = "&title=测试曲目\n&inote_5=(120){4}1,E";
        var zip = CreateZip(("song/maidata.txt", text), ("song/track.mp3", "test"));
        var path = SongArchive.Import(zip, Path.Combine(_root, "cache"));
        Assert.Equal(text, File.ReadAllText(path));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "track.mp3")));
        Assert.Equal(path, SongArchive.Import(zip, Path.Combine(_root, "cache")));
    }

    [Fact]
    public void RejectsAmbiguousMultiSongArchive()
    {
        var zip = CreateZip(("one/maidata.txt", "x"), ("two/maidata.txt", "y"));
        Assert.Throws<InvalidDataException>(() => SongArchive.Import(zip, Path.Combine(_root, "cache")));
    }

    [Fact]
    public void ReopeningArchiveDoesNotOverwritePlayingMusic()
    {
        var zip = CreateZip(("maidata.txt", "&inote_5=(120){4}1,E"), ("track.mp3", "music"));
        var cache = Path.Combine(_root, "cache");
        var chart = SongArchive.Import(zip, cache);
        using var playing = new FileStream(Path.Combine(Path.GetDirectoryName(chart)!, "track.mp3"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(chart, SongArchive.Import(zip, cache));
    }

    private string CreateZip(params (string name, string content)[] files)
    {
        var path = Path.Combine(_root, "song.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in files)
        {
            using var stream = new StreamWriter(archive.CreateEntry(name).Open());
            stream.Write(content);
        }
        return path;
    }

    public void Dispose() => Directory.Delete(_root, true);
}
