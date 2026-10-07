using System.Text.Json;

namespace MaiPlay;

public sealed class AppSettings
{
    // 隔离实例和窗口验收可使用独立目录，普通启动沿用本机设置。
    public static string DataDirectory => Environment.GetEnvironmentVariable("MAIPLAY_DATA_DIRECTORY") is { Length: > 0 } directory
        ? Path.GetFullPath(directory) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaiPlay");
    private static string FilePath => Path.Combine(DataDirectory, "settings.json");
    public string? LastSong { get; set; }
    public List<string> Skins { get; set; } = [];
    public string? SelectedSkin { get; set; }
    public string? SoundDirectory { get; set; }
    public double MusicVolume { get; set; } = .7;
    public double SoundVolume { get; set; } = .55;
    public bool SoundEnabled { get; set; } = true;
    public double TapSpeed { get; set; } = 7.5;
    public double TouchSpeed { get; set; } = 7.5;
    public bool ShowSensors { get; set; }
    public bool HighlightContacts { get; set; } = true;
    public bool ShowHandMarkers { get; set; } = true;
    public bool ShowHandTrails { get; set; }
    public string? InputLogPath { get; set; }

    public static AppSettings Load()
    {
        try
        {
            var settings = File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new();
            settings.Skins ??= [];
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
