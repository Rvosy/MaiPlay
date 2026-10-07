using System.IO.Compression;
using System.Buffers.Binary;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace MaiPlay.Skins;

/// <summary>Majdata's original filenames are the format. Missing sprites stay missing.</summary>
public sealed class SkinPack : IDisposable
{
    private readonly Dictionary<string, Bitmap> _assets;
    private readonly Dictionary<(string, uint), Bitmap> _tinted = new();
    public string Name { get; }
    public string SourcePath { get; }
    public IReadOnlyDictionary<string, Bitmap> Assets => _assets;
    public IReadOnlyList<string> Warnings { get; }

    internal SkinPack(string source, Dictionary<string, Bitmap> assets)
    {
        SourcePath = source;
        Name = Directory.Exists(source) ? new DirectoryInfo(source).Name : Path.GetFileNameWithoutExtension(source);
        _assets = assets;
        Warnings = SkinLibrary.RequiredAssets.Where(x => Get(x) is null)
            .Select(x => $"缺少 {x}，该素材保持空白（与上游一致）。").ToArray();
    }

    public Bitmap? Get(string path)
    {
        if (_assets.TryGetValue(path, out var bitmap)) return bitmap;
        // These are the ONLY fallbacks in CustomSkin.ProcessFallbacks.
        string? fallback = path switch
        {
            "HoldSkins/hold_on.png" => "HoldSkins/hold.png",
            "HoldSkins/hold_each_on.png" => "HoldSkins/hold_each.png",
            "HoldSkins/hold_break_on.png" => "HoldSkins/hold_break.png",
            _ => null
        };
        return fallback is not null && _assets.TryGetValue(fallback, out bitmap) ? bitmap : null;
    }

    public Bitmap? GetTinted(string path, uint color)
    {
        var original = Get(path);
        if (original is null) return null;
        if (_tinted.TryGetValue((path, color), out var cached)) return cached;
        using var source = new MemoryStream();
        original.Save(source);
        using var decoded = SKBitmap.Decode(source.ToArray());
        using var output = new SKBitmap(decoded.Width, decoded.Height);
        using (var canvas = new SKCanvas(output))
        using (var filter = SKColorFilter.CreateBlendMode(new SKColor(color), SKBlendMode.Modulate))
        using (var paint = new SKPaint { ColorFilter = filter }) canvas.DrawBitmap(decoded, 0, 0, paint);
        using var encoded = output.Encode(SKEncodedImageFormat.Png, 100);
        using var result = encoded.AsStream();
        cached = new Bitmap(result);
        _tinted.Add((path, color), cached);
        return cached;
    }

    public void Dispose()
    {
        foreach (var asset in _assets.Values) asset.Dispose();
        foreach (var asset in _tinted.Values) asset.Dispose();
        _assets.Clear();
        _tinted.Clear();
    }
}

public static class SkinLibrary
{
    private const long MaximumBytes = 256 * 1024 * 1024;
    public static IReadOnlyList<string> RequiredAssets { get; } = BuildRequiredAssets();

    public static SkinPack Load(string path)
    {
        path = Path.GetFullPath(path);
        var assets = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(path)) LoadDirectory(path, assets);
            else if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) LoadZip(path, assets);
            else throw new InvalidDataException("请选择 MajdataPlay 皮肤 ZIP 或包含 TapSkins 的解压目录。");
            if (!assets.ContainsKey("TapSkins/tap.png"))
                throw new InvalidDataException("皮肤中缺少 TapSkins/tap.png。请选择皮肤根目录，或原始皮肤 ZIP。");
            return new SkinPack(path, assets);
        }
        catch
        {
            foreach (var image in assets.Values) image.Dispose();
            throw;
        }
    }

    private static void LoadDirectory(string root, Dictionary<string, Bitmap> assets)
    {
        long bytes = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetRelativePath(root, path).Replace('\\', '/');
            using var stream = File.OpenRead(path);
            bytes += stream.Length;
            if (bytes > MaximumBytes) throw new InvalidDataException("皮肤图片总大小超过 256 MiB。");
            AddImage(name, stream, assets);
        }
    }

    private static void LoadZip(string path, Dictionary<string, Bitmap> assets)
    {
        using var zip = ZipFile.OpenRead(path);
        long bytes = 0;
        // No ZIP content is written to disk. Validate even unused entries so a bad
        // archive cannot silently become an accepted import.
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(x => x == ".."))
                throw new InvalidDataException($"ZIP 包含不安全路径：{entry.FullName}");
            bytes += entry.Length;
            if (bytes > MaximumBytes || zip.Entries.Count > 4096)
                throw new InvalidDataException("皮肤 ZIP 过大，或包含超过 4096 个文件。");
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            using var input = entry.Open();
            using var stream = new MemoryStream();
            input.CopyTo(stream);
            stream.Position = 0;
            AddImage(name, stream, assets);
        }
    }

    private static void AddImage(string name, Stream stream, Dictionary<string, Bitmap> assets)
    {
        if (assets.ContainsKey(name)) throw new InvalidDataException($"皮肤包含重复路径：{name}");
        try
        {
            Span<byte> header = stackalloc byte[24];
            stream.ReadExactly(header);
            stream.Position = 0;
            if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException($"无法解码皮肤图片 {name}：不是 PNG 文件。");
            var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 32 * 1024 * 1024)
                throw new InvalidDataException($"皮肤图片 {name} 尺寸过大或无效。");
            var pixels = assets.Values.Sum(x => (long)x.PixelSize.Width * x.PixelSize.Height) + (long)width * height;
            if (pixels > 128 * 1024 * 1024) throw new InvalidDataException("皮肤解码后的图片总量超过 128 Mpx。");
            var image = new Bitmap(stream);
            assets.Add(name, image);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"无法解码皮肤图片 {name}：{ex.Message}", ex);
        }
    }

    private static string[] BuildRequiredAssets()
    {
        var names = new List<string>();
        void Add(string folder, params string[] files) => names.AddRange(files.Select(x => $"{folder}/{x}.png"));
        Add("TapSkins", "tap", "tap_each", "tap_break", "tap_ex");
        Add("HoldSkins", "hold", "hold_each", "hold_break", "hold_ex", "hold_off", "hold_on", "hold_each_on", "hold_break_on");
        Add("StarSkins", "star", "star_each", "star_break", "star_double", "star_each_double", "star_break_double", "star_ex", "star_ex_double");
        Add("SlideSkins", "slide", "slide_each", "slide_break");
        for (var i = 0; i < 11; i++) Add("WifiSkins", $"wifi_{i}", $"wifi_each_{i}", $"wifi_break_{i}");
        Add("TouchSkins", "touch", "touch_each", "touch_break", "touch_point", "touch_point_each", "touch_break_point", "touch_just",
            "touch_border_2", "touch_border_3", "touch_border_2_each", "touch_border_3_each", "touch_break_border_2", "touch_break_border_3");
        for (var i = 0; i < 4; i++) Add("TouchHoldSkins", $"touchhold_{i}", $"touchhold_break_{i}");
        Add("TouchHoldSkins", "touchhold_border", "touchhold_break_border", "touchhold_off");
        Add("NoteGuideSkins", "Normal", "Each", "Slide", "Break", "Hold_End", "Hold_Each_End", "Hold_Break_End", "EachLine1", "EachLine2", "EachLine3", "EachLine4");
        Add("JudgeTextSkins", "judge_text_miss", "judge_text_good", "judge_text_great", "judge_text_perfect", "judge_text_cPerfect",
            "judge_text_cPerfect_break", "judge_text_perfect_break", "judge_text_break_2600_shine", "fast", "late");
        foreach (var value in new[] { 0, 1000, 1250, 1500, 2000, 2500, 2550, 2600 }) Add("JudgeTextSkins", $"judge_text_break_{value}");
        foreach (var state in new[] { "curv_r", "str_r", "wifi_u", "curv_l", "str_l", "wifi_d" })
            Add("SlideOKSkins", $"just_{state}", $"just_{state}_p", $"just_{state}_fast_gr", $"just_{state}_fast_gd",
                $"just_{state}_late_gr", $"just_{state}_late_gd", $"miss_{state}", $"toofast_{state}");
        names.Add("outline.png");
        return names.ToArray();
    }
}
