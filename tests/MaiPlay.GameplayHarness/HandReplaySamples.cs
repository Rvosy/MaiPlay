using System.Text.Json;
using System.Text.Json.Serialization;
using MaiPlay.Core;

internal static class HandReplaySamples
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static int Write(string output, string? officialSong)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "maidata.txt"), """
            &title=正攻手顺示例
            &artist=MaiPlay
            &des=MaiPlay
            &first=1
            &lv_4=双押与换手
            &inote_4=(120){#1}7,2,78,2/B7,{#0.15}7,7,{#0.7},{#1}8-3[0.3##2.4],8$,8$,,6,2,E
            """);
        WriteClockAudio(Path.Combine(output, "track.wav"));
        var chart = SimaiParser.LoadSong(output).Charts.Single();
        var plan = NormalHandPlanner.Plan(chart);
        if (!plan.AllCriticalPerfect) throw new InvalidDataException("正攻示例未通过整首判定核验。");
        File.WriteAllText(Path.Combine(output, "hands.json"), JsonSerializer.Serialize(new InputLog { Hands = plan.Tracks }, Json));
        var reports = Path.Combine("artifacts", "normal-planner-validation");
        Directory.CreateDirectory(reports);
        Verify(chart, Path.Combine(output, "hands.json"), Path.Combine(reports, "sample-results.json"), 12);
        WriteTechniques(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "HandTechniques"));
        if (officialSong is not null)
        {
            var official = SimaiParser.LoadSong(officialSong).Charts.Single(c => c.Difficulty == 4);
            var officialPlan = NormalHandPlanner.Plan(official);
            var path = Path.Combine(reports, "official-expert-hands.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new InputLog { Hands = officialPlan.Tracks }, Json));
            Verify(official, path, Path.ChangeExtension(path, ".results.json"), official.Duration + 2);
        }
        return 0;
    }

    private static void WriteTechniques(string output)
    {
        Directory.CreateDirectory(output);
        var sample = new Builder();
        sample.Hit(HandSide.Left, 1, 1.08, 7);
        sample.Hit(HandSide.Right, 2, 2.08, 2);
        sample.Press(HandSide.Left, 3, 3.08, new(-.6010189638431336, -.6010189638431331),
            [new("palm", InputChannel.Touch, new(0, 0), .18)]);
        var button = SlidePath.ButtonPosition(2, 1.12);
        var thumb = SlidePath.ButtonPosition(2, .88);
        sample.Press(HandSide.Right, 4, 4.08, button,
            Fingers(button, InputChannel.Button).Append(new HandContact("thumb", InputChannel.Touch,
                new(thumb.X - button.X, thumb.Y - button.Y), .045)).ToArray());
        sample.Hit(HandSide.Left, 5, 5.055, 7, InputChannel.Button);
        sample.Hit(HandSide.Left, 5.15, 5.205, 7);
        sample.Hit(HandSide.Left, 6, 6.055, 8, InputChannel.Button);
        sample.Hit(HandSide.Left, 7, 7.055, 8, InputChannel.Button);
        sample.Hit(HandSide.Left, 8, 8.055, 8, InputChannel.Button);
        var chart = SimaiParser.ParseChart("(120){#1}7,2,78,2/A2,{#0.15}7,7,{#0.7},{#1}8-3[0.3##2.4],8$,8$,,6,2,E", first: 1);
        sample.Slide(HandSide.Right, chart.Notes.Single(n => n.Kind == NoteKind.Slide));
        sample.Hit(HandSide.Left, 10, 10.08, 6);
        sample.Hit(HandSide.Left, 11, 11.08, 2);
        var logPath = Path.Combine(output, "hands.json");
        sample.Save(logPath, 12);
        File.WriteAllText(Path.Combine(output, "maidata.txt"), """
            &title=特殊技巧接触测试
            &artist=MaiPlay
            &des=MaiPlay
            &first=1
            &lv_4=手顺与触区
            &inote_4=(120){#1}7,2,78,2/A2,{#0.15}7,7,{#0.7},{#1}8-3[0.3##2.4],8$,8$,,6,2,E
            """);
        WriteClockAudio(Path.Combine(output, "track.wav"));
        var reports = Path.Combine("artifacts", "hand-techniques-validation");
        Directory.CreateDirectory(reports);
        Verify(SimaiParser.LoadSong(output).Charts.Single(), logPath, Path.Combine(reports, "sample-results.json"), 12);
    }

    private static void Verify(Chart chart, string path, string report, double end)
    {
        var log = InputLog.Load(path);
        var session = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
        var results = session.Advance(end).Where(e => e.IsFinal).ToArray();
        var payload = new
        {
            chart.Title, chart.Difficulty, end, scope = "全谱输入回放；人体尺寸与舒适度待人工验收。",
            inputEvents = log.Events.Count, completed = results.Count(e => e.Kind != PlaybackEventKind.Miss),
            missed = results.Count(e => e.Kind == PlaybackEventKind.Miss),
            results = results.Select(e => new { e.Note.Id, e.Note.Kind, e.Note.Time, e.Grade, e.DifferenceSeconds })
        };
        File.WriteAllText(report, JsonSerializer.Serialize(payload, Json));
        Console.WriteLine($"{chart.Title}: {results.Length} 个判定，Miss {payload.missed}；{Path.GetFullPath(path)}");
    }

    private static HandContact[] Fingers(ChartPoint p, InputChannel channel)
    {
        var angle = Math.Atan2(p.Y, p.X) + Math.PI / 2;
        return new[] { -.075, -.025, .025, .075 }.Select((d, i) =>
            new HandContact($"finger{i + 1}", channel, new(d * Math.Cos(angle), d * Math.Sin(angle)), .035)).ToArray();
    }

    private sealed class Builder
    {
        private readonly Dictionary<HandSide, List<HandFrame>> _frames = new()
        {
            [HandSide.Left] = [new(0, new(-.5, .5), [])],
            [HandSide.Right] = [new(0, new(.5, .5), [])]
        };

        public void Hit(HandSide hand, double start, double end, int button, InputChannel channel = InputChannel.Touch)
        {
            var p = SlidePath.ButtonPosition(button, channel == InputChannel.Button ? 1.12 : .88);
            Press(hand, start, end, p, Fingers(p, channel));
        }

        public void Press(HandSide hand, double start, double end, ChartPoint p, IReadOnlyList<HandContact> contacts)
        {
            Add(hand, new(start - .02, p, []));
            Add(hand, new(start, p, contacts));
            Add(hand, new(end, p, []));
        }

        public void Slide(HandSide hand, ChartNote note)
        {
            var path = note.SlidePath!;
            Add(hand, new(note.SlideStartTime - .02, path.PointAt(0), []));
            var count = (int)Math.Ceiling(note.Duration * 240);
            for (var i = 0; i <= count; i++)
            {
                var progress = (double)i / count;
                var p = path.PointAt(progress);
                Add(hand, new(note.SlideStartTime + progress * note.Duration, p, Fingers(p, InputChannel.Touch)));
            }
            Add(hand, new(note.EndTime + .01, path.PointAt(1), []));
        }

        private void Add(HandSide hand, HandFrame frame)
        {
            var list = _frames[hand];
            if (frame.Time <= list[^1].Time) throw new InvalidDataException($"手写手顺发生重叠：{hand} {frame.Time:F4}s。");
            list.Add(frame);
        }

        public void Save(string path, double end)
        {
            foreach (var (hand, frames) in _frames) Add(hand, new(end, frames[^1].Position, []));
            var log = new InputLog { Hands = _frames.Select(p => new HandTrack(p.Key, p.Value)).ToArray() };
            File.WriteAllText(path, JsonSerializer.Serialize(log, Json));
        }
    }

    private static void WriteClockAudio(string path)
    {
        const int rate = 22050, count = rate * 13;
        using var file = new BinaryWriter(File.Create(path));
        file.Write("RIFF"u8); file.Write(36 + count * 2); file.Write("WAVEfmt "u8);
        file.Write(16); file.Write((short)1); file.Write((short)1); file.Write(rate); file.Write(rate * 2);
        file.Write((short)2); file.Write((short)16); file.Write("data"u8); file.Write(count * 2);
        for (var i = 0; i < count; i++)
        {
            var time = i / (double)rate; var phase = time % 1;
            var tone = phase < .04 ? Math.Sin(2 * Math.PI * 700 * time) * .12 * (1 - phase / .04) : 0;
            file.Write((short)(tone * short.MaxValue));
        }
    }
}
