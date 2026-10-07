using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MaiPlay.Core;

/// <summary>Reads ordinary Simai/DX notation. Unsupported extensions fail with a source location.</summary>
public static class SimaiParser
{
    public static Song LoadSong(string path)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
            path = Directory.EnumerateFiles(path).FirstOrDefault(p =>
                       Path.GetFileName(p).Equals("maidata.txt", StringComparison.OrdinalIgnoreCase))
                   ?? throw new ChartParseException("歌曲目录中没有 maidata.txt；也可以直接打开谱面文本文件。");
        if (new FileInfo(path).Length > 16 * 1024 * 1024)
            throw new ChartParseException("谱面文件超过 16 MB。");
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
        return ParseSong(text, Path.GetDirectoryName(path)!, path);
    }

    public static Song ParseSong(string text, string directory = "", string chartPath = "")
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        var value = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('&'))
            {
                if (key != null) fields[key] = value.ToString().Trim();
                var split = trimmed.IndexOf('=');
                if (split < 2) throw new ChartParseException($"无效的谱面属性：{line.Trim()}");
                key = trimmed[1..split].Trim();
                value.Clear().AppendLine(trimmed[(split + 1)..]);
            }
            else value.AppendLine(line);
        }
        if (key != null) fields[key] = value.ToString().Trim();
        var title = fields.GetValueOrDefault("title", Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)));
        if (string.IsNullOrWhiteSpace(title)) title = "Untitled";
        var artist = fields.GetValueOrDefault("artist", "");
        var first = fields.TryGetValue("first", out var offset) ? Number(offset, "first") : 0;
        var charts = new List<Chart>();
        foreach (var field in fields.Where(f => f.Key.StartsWith("inote_", StringComparison.OrdinalIgnoreCase)))
        {
            if (!int.TryParse(field.Key.AsSpan(6), out var difficulty) || difficulty < 1)
                throw new ChartParseException($"无效难度：{field.Key}");
            if (string.IsNullOrWhiteSpace(field.Value)) continue;
            var difficultyFirst = fields.TryGetValue($"first_{difficulty}", out var individualOffset)
                ? Number(individualOffset, $"first_{difficulty}") : first;
            try
            {
                charts.Add(ParseChart(field.Value, difficultyFirst, title, artist,
                    fields.GetValueOrDefault($"des_{difficulty}", fields.GetValueOrDefault("des", "")),
                    difficulty, fields.GetValueOrDefault($"lv_{difficulty}", "")));
            }
            catch (ChartParseException ex)
            {
                throw new ChartParseException($"inote_{difficulty}：{ex.Message}");
            }
        }
        if (charts.Count == 0)
        {
            if (fields.Count > 0) throw new ChartParseException("文件中没有可播放的 &inote_n 谱面。");
            charts.Add(ParseChart(text, title: title));
        }
        string? audio = null;
        if (Directory.Exists(directory))
        {
            var extensions = new[] { ".mp3", ".ogg", ".wav", ".flac", ".m4a", ".aac" };
            var audioFiles = Directory.EnumerateFiles(directory)
                .Where(p => extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToArray();
            audio = audioFiles.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals("track", StringComparison.OrdinalIgnoreCase));
            if (audio == null && audioFiles.Length == 1) audio = audioFiles[0];
        }
        return new(directory, chartPath, audio, title, artist, charts.OrderBy(c => c.Difficulty).ToArray());
    }

    public static Chart ParseChart(string text, double first = 0, string title = "Untitled", string artist = "",
        string designer = "", int difficulty = 5, string level = "")
    {
        if (!double.IsFinite(first)) throw new ChartParseException("first 必须是有限秒数。");
        var reader = new Reader(text, first);
        var notes = reader.Read();
        if (!double.IsFinite(reader.Time) || notes.Any(n => !double.IsFinite(n.Time) || !double.IsFinite(n.EndTime)))
            throw new ChartParseException("谱面时间溢出，请检查 BPM 和音符时长。");
        return new(title, artist, designer, difficulty, level, notes,
            Math.Max(reader.Time, notes.Count == 0 ? 0 : notes.Max(n => n.EndTime)));
    }

    private static double Number(string value, string label)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
            throw new ChartParseException($"{label} 不是有效数值：{value}");
        return n;
    }

    private sealed class Reader
    {
        private readonly string _source;
        private readonly string _text;
        private int _at;
        private double _bpm;
        private double _subdivision = 4;
        private double? _fixedStep;
        private readonly List<ChartNote> _notes = [];
        public double Time { get; private set; }

        public Reader(string text, double first)
        {
            _source = text;
            // Keep positions intact so errors still identify the input line/column.
            _text = Regex.Replace(text, @"\|\|[^\r\n]*", m => new string(' ', m.Length));
            Time = first;
        }

        private char Current => _at < _text.Length ? _text[_at] : '\0';
        private void SkipSpace() { while (char.IsWhiteSpace(Current)) _at++; }
        private ChartParseException Error(string message)
        {
            var start = Math.Min(_at, _source.Length);
            var line = 1 + _source[..start].Count(c => c == '\n');
            var column = start - _source[..start].LastIndexOf('\n');
            return new($"第 {line} 行，第 {column} 列：{message}");
        }

        public IReadOnlyList<ChartNote> Read()
        {
            var groupStart = 0;
            var forceEach = false;
            var fakeEachOffset = 0d;
            while (true)
            {
                SkipSpace();
                if (Current == '\0' || Current == 'E' && (_at + 1 == _text.Length || _text[_at + 1] is not (>= '1' and <= '8')))
                {
                    FinishGroup(groupStart, forceEach);
                    if (Current == 'E')
                    {
                        _at++;
                        SkipSpace();
                        while (Current == ',') { _at++; SkipSpace(); }
                        if (Current != '\0') throw Error("结束标记 E 之后仍有内容。");
                    }
                    break;
                }
                if (Current == '(')
                {
                    _bpm = Positive(ReadBracket('(', ')'), "BPM");
                    continue;
                }
                if (Current == '{')
                {
                    var subdivision = ReadBracket('{', '}');
                    _fixedStep = subdivision.StartsWith('#') ? Positive(subdivision[1..], "固定节拍秒数") : null;
                    if (_fixedStep == null) _subdivision = Positive(subdivision, "节拍细分");
                    continue;
                }
                if (Current == ',')
                {
                    FinishGroup(groupStart, forceEach);
                    groupStart = _notes.Count;
                    forceEach = false;
                    fakeEachOffset = 0;
                    RequireBpm();
                    Time += _fixedStep ?? 240 / _bpm / _subdivision;
                    _at++;
                    continue;
                }
                if (Current == '/')
                {
                    _at++;
                    continue;
                }
                if (Current == '`')
                {
                    RequireBpm();
                    FinishGroup(groupStart, forceEach);
                    groupStart = _notes.Count;
                    forceEach = false;
                    // MajSimai fdb2a3e SimaiParser: 伪双子组间隔 128 分音，逗号网格不变。
                    fakeEachOffset += 1.875 / _bpm;
                    _at++;
                    continue;
                }
                if (Current == '0') { forceEach = true; _at++; continue; }
                RequireBpm();
                ReadNote(Time + fakeEachOffset);
                if (_notes.Count > 200000) throw Error("谱面超过 200000 个音符。");
            }
            return _notes.OrderBy(n => n.Time).Select((n, i) => n with { Id = i }).ToArray();
        }

        private void FinishGroup(int start, bool force)
        {
            // A star head and its own trail do not make an EACH pair.
            var heads = _notes.Skip(start).Count(n => n.Kind != NoteKind.Slide);
            var trails = _notes.Skip(start).Count(n => n.Kind == NoteKind.Slide);
            for (var i = start; i < _notes.Count; i++)
                _notes[i] = _notes[i] with { IsEach = force || (_notes[i].Kind == NoteKind.Slide ? trails > 1 : heads > 1) };
        }

        private void RequireBpm()
        {
            if (_bpm == 0) throw Error("请先用 (BPM) 指定有效速度。");
        }

        private void ReadNote(double noteTime)
        {
            var begin = _at;
            var area = "";
            var position = 1;
            if (Current is >= 'A' and <= 'E')
            {
                var group = Current;
                _at++;
                if (group == 'C')
                {
                    area = "C";
                    // C1/C2 are aliases for the centre sensor in common Simai files.
                    if (Current is '1' or '2') _at++;
                }
                else { position = ReadPosition(); area = $"{group}{position}"; }
            }
            else position = ReadPosition();

            var head = new ChartNote { Time = noteTime, StartPosition = position, EndPosition = position, TouchArea = area,
                Kind = area.Length == 0 ? NoteKind.Tap : NoteKind.Touch };
            var noHead = false;
            var forceNormal = false;
            var hold = false;
            var starCount = 0;
            while (true)
            {
                SkipSpace();
                switch (Current)
                {
                    case 'b': head = head with { IsBreak = true }; _at++; break;
                    case 'x': head = head with { IsEx = true }; _at++; break;
                    case 'f': head = head with { IsFirework = true }; _at++; break;
                    case 'h': hold = true; _at++; break;
                    case '$': starCount++; _at++; break;
                    case '@': forceNormal = true; _at++; break;
                    case '?': noHead = true; _at++; break;
                    case '!': noHead = true; _at++; break;
                    default: goto DecorationsDone;
                }
            }
            DecorationsDone:
            if (starCount > 2) throw Error("星形修饰符最多为 $$。");
            if (hold)
            {
                var duration = Current == '[' ? ReadHoldDuration(ReadBracket('[', ']')) : 0;
                head = head with { Kind = area.Length == 0 ? NoteKind.Hold : NoteKind.TouchHold, Duration = duration };
            }
            if (Current == '[' && !hold) throw Error("时长之前缺少 h 或 Slide 轨道。");
            var paths = new List<ChartNote>();
            while (IsSlide(Current))
            {
                if (area.Length != 0) throw Error("Touch 不能作为 Slide 起点。");
                if (hold) throw Error("本版不支持同一音符的 Hold + Slide 组合；请拆为同时音符。");
                paths.Add(ReadSlide(position, head));
                SkipSpace();
                if (Current != '*') break;
                _at++;
                SkipSpace();
                if (!IsSlide(Current)) throw Error("* 后应接 Slide 轨道，不需要重复起点。");
            }
            if (noHead && paths.Count == 0) throw Error("? 和 ! 仅用于无头 Slide。");
            if (area.Length != 0 && (head.IsEx || starCount > 0 || forceNormal))
                throw Error("Touch 不支持 EX 或强制星形修饰。");
            if (area.Length == 0 && head.IsFirework)
                throw Error("烟花 f 只适用于 Touch 和 TouchHold。");
            var source = _source[begin.._at];
            head = head with { IsStar = !forceNormal && (starCount > 0 || paths.Count > 0),
                IsDoubleStar = paths.Count > 1, IsSpinningStar = starCount == 2,
                StarRotateSpeed = paths.Count > 0 ? NoteMotion.StarRotateSpeed(paths[0].Duration, paths[0].SlidePath!.TimingUnits) : starCount == 2 ? -440 : 0,
                Source = source };
            if (!noHead) _notes.Add(head);
            _notes.AddRange(paths.Select((n, index) => n with { Source = source, IsHeadless = noHead || index > 0 }));
        }

        private ChartNote ReadSlide(int start, ChartNote head)
        {
            var segments = new List<SlideSegment>();
            var durations = new List<double?>();
            var delay = 60 / _bpm;
            var slideBreak = false;
            var from = start;
            while (IsSlide(Current))
            {
                var symbol = Current.ToString();
                _at++;
                if (Current.ToString() == symbol && symbol is "p" or "q") { symbol += Current; _at++; }
                var via = 0;
                if (symbol == "V") via = ReadPosition();
                var to = ReadPosition();
                segments.Add(new(symbol, from, to, via));
                from = to;
                SkipSpace();
                if (Current == 'b') { slideBreak = true; _at++; SkipSpace(); }
                double? duration = null;
                if (Current == '[')
                {
                    var timing = ReadSlideDuration(ReadBracket('[', ']'));
                    if (durations.All(d => d == null)) delay = timing.Delay;
                    else if (timing.ExplicitDelay) throw Error("连接 Slide 的后续段不能重新指定等待时间。");
                    duration = timing.Duration;
                }
                SkipSpace();
                if (Current == 'b') { slideBreak = true; _at++; SkipSpace(); }
                durations.Add(duration);
            }
            if (durations.All(d => d == null)) throw Error("Slide 缺少 [时长]。");
            if (durations.Count(d => d.HasValue) != 1 && durations.Any(d => d == null))
                throw Error("连接 Slide 应逐段指定时长，或只在末尾指定总时长。");
            if (durations.Count(d => d.HasValue) == 1 && durations[^1] == null)
                throw Error("连接 Slide 的总时长应位于最后一段。");
            if (segments.Count > 1 && segments.Any(s => s.Shape == "w"))
                throw Error("本版不支持 Wifi 与其他轨迹相连。");
            var path = new SlidePath(segments, durations);
            return new ChartNote { Kind = NoteKind.Slide, Time = head.Time, SlideStartTime = head.Time + delay,
                Duration = durations.Sum(d => d ?? 0), StartPosition = start, EndPosition = from,
                IsBreak = slideBreak, IsSuddenSlide = head.IsSuddenSlide, SlidePath = path };
        }

        private int ReadPosition()
        {
            SkipSpace();
            if (Current is not (>= '1' and <= '8'))
                throw Error($"需要 1–8 的位置；遇到「{(Current == '\0' ? "文件结束" : Current)}」。未知语法不会被忽略。");
            return _text[_at++] - '0';
        }
        private static bool IsSlide(char c) => c is '-' or '^' or '<' or '>' or 'v' or 'V' or 'p' or 'q' or 's' or 'z' or 'w';

        private string ReadBracket(char open, char close)
        {
            if (Current != open) throw Error($"需要 {open}。");
            var start = ++_at;
            while (Current != close && Current != '\0') _at++;
            if (Current == '\0') throw Error($"{open} 缺少闭合的 {close}。");
            return _text[start.._at++].Trim();
        }
        private double Positive(string text, string label)
        {
            var n = Number(text, label);
            if (n <= 0) throw Error($"{label} 必须大于零。");
            return n;
        }
        private double NonNegative(string text, string label)
        {
            var n = Number(text, label);
            if (n < 0) throw Error($"{label} 不可为负。");
            return n;
        }
        private double FractionDuration(string text, double bpm)
        {
            var parts = text.Split(':');
            if (parts.Length != 2) throw Error($"无效时长 [{text}]，需要 分母:拍数。");
            return 240 / bpm / Positive(parts[0], "时长分母") * NonNegative(parts[1], "时长拍数");
        }
        private double ReadHoldDuration(string text)
        {
            if (text.StartsWith('#')) return NonNegative(text[1..], "Hold 秒数");
            var parts = text.Split('#');
            return parts.Length switch
            {
                1 => FractionDuration(text, _bpm),
                2 => FractionDuration(parts[1], Positive(parts[0], "Hold BPM")),
                _ => throw Error($"无效的 Hold 时长 [{text}]。")
            };
        }
        private (double Delay, double Duration, bool ExplicitDelay) ReadSlideDuration(string text)
        {
            var delay = 60 / _bpm;
            var bpm = _bpm;
            var duration = text;
            var explicitDelay = false;
            var waitSplit = text.IndexOf("##", StringComparison.Ordinal);
            if (waitSplit >= 0)
            {
                delay = NonNegative(text[..waitSplit], "Slide 等待秒数");
                duration = text[(waitSplit + 2)..];
                explicitDelay = true;
                var durationBpm = duration.IndexOf('#');
                if (durationBpm > 0)
                {
                    bpm = Positive(duration[..durationBpm], "Slide 时长 BPM");
                    duration = duration[(durationBpm + 1)..];
                }
            }
            else if (text.Contains('#'))
            {
                var parts = text.Split('#');
                if (parts.Length != 2) throw Error($"无效的 Slide 时长 [{text}]。");
                if (parts[0].Length > 0)
                {
                    bpm = Positive(parts[0], "Slide BPM");
                    delay = 60 / bpm;
                    explicitDelay = true;
                }
                duration = parts[1];
            }
            var seconds = duration.Contains(':') ? FractionDuration(duration, bpm) : NonNegative(duration, "Slide 秒数");
            if (seconds == 0) throw Error("Slide 移动时长必须大于零。");
            return (delay, seconds, explicitDelay);
        }
    }
}
