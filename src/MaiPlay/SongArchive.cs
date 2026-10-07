using System.IO.Compression;
using System.Security.Cryptography;

namespace MaiPlay;

public static class SongArchive
{
    /// <summary>Extract local chart archives into the user's cache without trusting ZIP paths.</summary>
    public static string Import(string path, string cacheDirectory)
    {
        using var file = File.OpenRead(path);
        var key = Convert.ToHexString(SHA256.HashData(file))[..16];
        var root = Path.GetFullPath(Path.Combine(cacheDirectory, key));
        file.Position = 0;
        using var zip = new ZipArchive(file);
        if (zip.Entries.Count > 2048 || zip.Entries.Sum(e => e.Length) > 1024L * 1024 * 1024)
            throw new InvalidDataException("歌曲 ZIP 不能超过 1 GiB 或 2048 个文件。");
        var entries = zip.Entries.Select(entry =>
        {
            var name = entry.FullName.Replace('\\', '/');
            var target = Path.GetFullPath(Path.Combine(root, name));
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains("..") ||
                !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException($"ZIP 包含不安全路径：{entry.FullName}");
            return (entry, target);
        }).ToArray();
        var charts = entries.Where(x => Path.GetFileName(x.target).Equals("maidata.txt", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (charts.Length != 1) throw new InvalidDataException("歌曲 ZIP 需要包含一份 maidata.txt；多首歌曲请分别打开解压后的目录。");
        foreach (var (entry, target) in entries)
        {
            if (entry.Name.Length == 0) continue;
            if (!new[] { ".txt", ".mp3", ".ogg", ".wav", ".flac", ".m4a", ".aac", ".aiff", ".png", ".jpg", ".jpeg" }
                .Contains(Path.GetExtension(target).ToLowerInvariant())) continue;
            // A content-addressed cache may still be open in the music decoder.
            // Reopening the same archive does not need to overwrite its files.
            if (File.Exists(target) && new FileInfo(target).Length == entry.Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
        return charts[0].target;
    }
}
