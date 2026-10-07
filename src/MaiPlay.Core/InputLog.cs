using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaiPlay.Core;

public sealed record InputLog
{
    public const string CurrentRuleVersion = "majdata-dc19722-240-v1";
    public string RuleVersion { get; init; } = CurrentRuleVersion;
    public IReadOnlyList<InputEvent> Events { get; init; } = [];
    public IReadOnlyList<ContactEvent> Contacts { get; init; } = [];
    public IReadOnlyList<HandTrack> Hands { get; init; } = [];
    [JsonIgnore] public HandReplay? HandReplay { get; private init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static InputLog Load(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("输入日志超过 16 MB。");
        var log = JsonSerializer.Deserialize<InputLog>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("输入日志为空。");
        if (log.RuleVersion != CurrentRuleVersion) throw new InvalidDataException($"输入日志规则版本不匹配：{log.RuleVersion}");
        if (log.Events == null || log.Contacts == null || log.Hands == null) throw new InvalidDataException("events、contacts 和 hands 不能为 null。");
        if (new[] { log.Events.Count, log.Contacts.Count, log.Hands.Count }.Count(count => count != 0) > 1)
            throw new InvalidDataException("一份输入日志只能使用 events、contacts 或 hands 中的一种。");
        var replay = log.Hands.Count == 0 ? null : new HandReplay(log.Hands);
        var events = replay?.Events ?? (log.Contacts.Count == 0 ? log.Events : ContactMapper.Convert(log.Contacts));
        Validate(events);
        return log with { Events = events, HandReplay = replay };
    }

    internal static void Validate(IEnumerable<InputEvent> events)
    {
        var previous = double.NegativeInfinity;
        foreach (var e in events)
        {
            if (!double.IsFinite(e.Time) || e.Time < previous) throw new InvalidDataException("输入事件必须按有限的时间戳升序排列。");
            if (string.IsNullOrWhiteSpace(e.Source)) throw new InvalidDataException("输入源 source 不能为空。");
            var validArea = e.Channel switch
            {
                InputChannel.Button => e.Area is { Length: 1 } && e.Area[0] is >= '1' and <= '8',
                InputChannel.Touch => Enum.TryParse<SensorArea>(e.Area, out var area) && Enum.IsDefined(area) && area.ToString() == e.Area,
                _ => false
            };
            if (!validArea) throw new InvalidDataException($"无效输入区域：{e.Channel}/{e.Area}");
            previous = e.Time;
        }
    }
}
