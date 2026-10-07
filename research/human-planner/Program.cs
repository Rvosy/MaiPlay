using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaiPlay.Core;

return PatternValidation.Run(args);

internal static class PatternValidation
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static int Run(string[] args)
    {
        if (args.Length == 4 && args[0] == "evaluate") return Evaluate(args[1], args[2], args[3]);
        if (args.Length != 3 || args[0] != "select")
        {
            Console.Error.WriteLine("用法：select 曲库目录 输出清单.json");
            Console.Error.WriteLine("核验：evaluate 曲库目录 清单.json 报告目录");
            return 1;
        }
        var root = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        var charts = new List<ChartCase>();
        var failures = new List<ScanFailure>();
        var files = Directory.GetFiles(root, "maidata.txt", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var eligible = 0;
        foreach (var file in files)
        {
            var path = Path.GetRelativePath(root, file).Replace('\\', '/');
            Song song;
            try { song = SimaiParser.LoadSong(file); }
            catch (Exception error)
            {
                failures.Add(new(path, $"{error.GetType().Name}: {error.Message}"));
                continue;
            }
            foreach (var chart in song.Charts.Where(c => c.Difficulty is 4 or 5 &&
                LevelOrder(DisplayLevel(c.Level)) >= (c.Difficulty == 4 ? 12.6m : 11.6m)))
            {
                eligible++;
                var segments = Describe(chart);
                if (segments.Count == 0) continue;
                charts.Add(new(path, chart.Title, chart.Difficulty, chart.Level, DisplayLevel(chart.Level),
                    Hash(file), chart.Notes.Count, segments));
            }
        }

        var selected = new Dictionary<string, (ChartCase Chart, List<string> Reasons)>(StringComparer.Ordinal);
        void Add(ChartCase chart, string reason)
        {
            var key = $"{chart.Path}#{chart.Difficulty}";
            if (!selected.TryGetValue(key, out var choice))
            {
                if (selected.Count == 30) return;
                selected.Add(key, choice = (chart, []));
            }
            if (!choice.Reasons.Contains(reason)) choice.Reasons.Add(reason);
        }
        var cases = charts.SelectMany(c => c.Segments.Select(s => (Chart: c, Segment: s))).ToArray();
        var rotations = cases.Where(c => c.Segment.Kind == PatternKind.Rotation).ToArray();
        var jacks = cases.Where(c => c.Segment.Kind == PatternKind.Jack).ToArray();
        foreach (var group in charts.GroupBy(c => (c.Difficulty, c.DisplayLevel)).OrderBy(g => g.Key.Difficulty).ThenBy(g => LevelOrder(g.Key.DisplayLevel)))
        {
            var levelRotations = rotations.Where(c => c.Chart.Difficulty == group.Key.Difficulty && c.Chart.DisplayLevel == group.Key.DisplayLevel).ToArray();
            if (levelRotations.Length > 0)
                Add(levelRotations.OrderByDescending(c => c.Segment.AverageTurnsPerSecond).ThenByDescending(c => c.Segment.NoteIds.Count).First().Chart,
                    $"{group.Key.Difficulty}/{group.Key.DisplayLevel} 旋转平均速度最高");
            var levelJacks = jacks.Where(c => c.Chart.Difficulty == group.Key.Difficulty && c.Chart.DisplayLevel == group.Key.DisplayLevel).ToArray();
            if (levelJacks.Length > 0)
                Add(levelJacks.OrderByDescending(c => c.Segment.End - c.Segment.Start).ThenByDescending(c => c.Segment.NoteIds.Count).First().Chart,
                    $"{group.Key.Difficulty}/{group.Key.DisplayLevel} 快速纵连持续最长");
        }
        foreach (var direction in new[] { 1, -1 })
        {
            var directional = rotations.Where(c => c.Segment.Direction == direction).ToArray();
            if (directional.Length == 0) continue;
            var label = direction > 0 ? "顺时针" : "逆时针";
            Add(directional.OrderByDescending(c => c.Segment.Turns).ThenByDescending(c => c.Segment.NoteIds.Count).First().Chart, $"{label}旋转圈数最多");
            Add(directional.OrderByDescending(c => c.Segment.PeakTurnsPerSecond).ThenByDescending(c => c.Segment.NoteIds.Count).First().Chart, $"{label}旋转峰值速度最高");
        }
        if (jacks.Length > 0)
        {
            Add(jacks.OrderByDescending(c => c.Segment.NoteIds.Count).ThenByDescending(c => c.Segment.NotesPerSecond).First().Chart, "快速纵连音符数最多");
            Add(jacks.OrderByDescending(c => c.Segment.NotesPerSecond).ThenByDescending(c => c.Segment.NoteIds.Count).First().Chart, "快速纵连平均速度最高");
        }
        // 在等级覆盖之外轮流加入长旋转、高速旋转与长纵连，直到达到 30 张。
        var rankings = new[]
        {
            rotations.OrderByDescending(c => c.Segment.Turns).ToArray(),
            rotations.OrderByDescending(c => c.Segment.AverageTurnsPerSecond).ToArray(),
            jacks.OrderByDescending(c => c.Segment.NoteIds.Count).ThenByDescending(c => c.Segment.NotesPerSecond).ToArray(),
            jacks.OrderByDescending(c => c.Segment.NotesPerSecond).ThenByDescending(c => c.Segment.NoteIds.Count).ToArray()
        };
        var reasons = new[] { "全库长旋转代表", "全库高速旋转代表", "全库长纵连代表", "全库高速纵连代表" };
        for (var rank = 0; selected.Count < 30 && rank < rankings.Max(r => r.Length); rank++)
            for (var type = 0; type < rankings.Length && selected.Count < 30; type++)
                if (rank < rankings[type].Length) Add(rankings[type][rank].Chart, reasons[type]);

        var choices = selected.Values.Select(c => new SelectedChart(c.Chart, c.Reasons))
            .OrderBy(c => c.Chart.Difficulty).ThenBy(c => LevelOrder(c.Chart.DisplayLevel)).ThenBy(c => c.Chart.Path, StringComparer.Ordinal).ToArray();
        var inventory = charts.GroupBy(c => (c.Difficulty, c.DisplayLevel)).OrderBy(g => g.Key.Difficulty).ThenBy(g => LevelOrder(g.Key.DisplayLevel))
            .Select(g => new Cohort(g.Key.Difficulty, g.Key.DisplayLevel, g.Count(),
                g.Sum(c => c.Segments.Count(s => s.Kind == PatternKind.Rotation)), g.Sum(c => c.Segments.Count(s => s.Kind == PatternKind.Jack)),
                choices.Count(c => c.Chart.Difficulty == g.Key.Difficulty && c.Chart.DisplayLevel == g.Key.DisplayLevel))).ToArray();
        var selection = new Selection(DateTimeOffset.UtcNow, Hash(typeof(HumanPlanner).Assembly.Location),
            "Expert >= 12+; Master >= 11+; Re:Master excluded", NormalHandPlanner.FastRepeatInterval,
            files.Length, eligible, charts.Count, inventory, failures, choices);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(selection, JsonOptions));
        File.WriteAllText(Path.ChangeExtension(output, ".md"), SelectionMarkdown(selection));
        Console.WriteLine($"扫描 {files.Length} 个文件、{eligible} 张范围内谱面，选取 {choices.Length} 张；解析异常 {failures.Count}。");
        foreach (var cohort in inventory)
            Console.WriteLine($"{cohort.Difficulty}/{cohort.Level}：{cohort.Selected} 张；全层旋转 {cohort.Rotations} 段、快纵连 {cohort.FastJacks} 段。");
        return failures.Count == 0 ? 0 : 1;
    }

    private static int Evaluate(string root, string selectionFile, string output)
    {
        root = Path.GetFullPath(root);
        selectionFile = Path.GetFullPath(selectionFile);
        output = Path.GetFullPath(output);
        var selection = JsonSerializer.Deserialize<Selection>(File.ReadAllText(selectionFile), JsonOptions)!;
        var model = new HumanMotionModel();
        var results = new ConcurrentBag<ChartValidation>();
        var watch = Stopwatch.StartNew();
        var done = 0;
        Parallel.ForEach(selection.Charts, new ParallelOptions { MaxDegreeOfParallelism = 4 }, chosen =>
        {
            var entry = chosen.Chart;
            var file = Path.GetFullPath(Path.Combine(root, entry.Path));
            var timer = Stopwatch.StartNew();
            try
            {
                if (Hash(file) != entry.ChartSha256) throw new InvalidOperationException("谱面 SHA-256 与选取清单不一致。");
                var chart = SimaiParser.LoadSong(file).Charts.Single(c => c.Difficulty == entry.Difficulty);
                var baseline = Measure(chart, entry.Segments, c => NormalHandPlanner.Plan(c), model);
                var candidate = Measure(chart, entry.Segments, c => HumanPlanner.Plan(c), model);
                results.Add(new(chosen, baseline, candidate, Regressions(baseline, candidate), null));
            }
            catch (Exception error)
            {
                results.Add(new(chosen, null, null, [], $"{error.GetType().Name}: {error.Message}"));
            }
            var count = Interlocked.Increment(ref done);
            Console.WriteLine($"{count}/{selection.Charts.Count} · {entry.Title} #{entry.Difficulty} · {timer.Elapsed.TotalSeconds:F1}s");
        });
        var ordered = results.OrderBy(c => c.Selection.Chart.Difficulty).ThenBy(c => LevelOrder(c.Selection.Chart.DisplayLevel))
            .ThenBy(c => c.Selection.Chart.Path, StringComparer.Ordinal).ToArray();
        var report = new ValidationReport(DateTimeOffset.UtcNow, Hash(selectionFile), selection.AnalysisAssemblySha256,
            Hash(typeof(HumanPlanner).Assembly.Location), InputLog.CurrentRuleVersion,
            new(model.Geometry.JudgementRadiusMillimeters, model.MaxSpeedMillimetersPerSecond, model.PreferredSpeedMillimetersPerSecond,
                model.PreferredStrikeIntervalSeconds, selection.FastJackIntervalExclusive),
            watch.Elapsed.TotalSeconds, selection.Scope, ordered.Count(c => c.Error is not null || c.Baseline?.Error is not null || c.Candidate?.Error is not null),
            ordered.Count(c => c.Regressions.Count > 0),
            Aggregate(ordered.Select(c => c.Baseline).OfType<PlanMetrics>()), Aggregate(ordered.Select(c => c.Candidate).OfType<PlanMetrics>()), ordered);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "pattern-validation.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(output, "pattern-validation.md"), ValidationMarkdown(report));
        Console.WriteLine($"完成 {ordered.Length} 张：回退 {report.RegressedCharts}，异常 {report.Errors}；旋转完整段 {report.Baseline.CompletedRotations} → {report.Candidate.CompletedRotations}/{report.Candidate.Rotations}。");
        return report.Errors > 0 ? 1 : report.RegressedCharts > 0 ? 2 : 0;
    }

    private static PlanMetrics Measure(Chart chart, IReadOnlyList<SegmentCase> segments, Func<Chart, HandPlan> planner, HumanMotionModel model)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var plan = planner(chart);
            watch.Stop();
            var cp = plan.Results.Where(r => r.Grade == AchievementGrade.Perfect).Select(r => r.Note.Id).ToHashSet();
            var hands = plan.Strokes.GroupBy(s => s.NoteId).ToDictionary(g => g.Key,
                g => g.Select(s => s.Hand).Distinct().ToArray());
            var patterns = segments.Select(segment =>
            {
                var assignedPairs = 0;
                var sameHandPairs = 0;
                if (segment.Kind == PatternKind.Jack)
                    for (var index = 1; index < segment.NoteIds.Count; index++)
                        if (hands.TryGetValue(segment.NoteIds[index - 1], out var before) && before.Length == 1 &&
                            hands.TryGetValue(segment.NoteIds[index], out var after) && after.Length == 1)
                        {
                            assignedPairs++;
                            if (before[0] == after[0]) sameHandPairs++;
                        }
                return new PatternMetrics(segment.Kind, segment.Direction, segment.Start, segment.End, segment.NoteIds.Count,
                    segment.NoteIds.Count(cp.Contains), segment.NoteIds.All(cp.Contains),
                    segment.Kind == PatternKind.Jack ? segment.NoteIds.Count - 1 : 0, assignedPairs, sameHandPairs);
            }).ToArray();
            return new(plan.AllCriticalPerfect ? "all-cp" : "partial", chart.Notes.Count, cp.Count, plan.Gaps.Count,
                plan.Achievement, plan.DXScore, watch.Elapsed.TotalSeconds, model.Analyze(plan.Tracks, plan.Strokes), patterns, null);
        }
        catch (Exception error)
        {
            return new("error", chart.Notes.Count, 0, 0, 0, 0, watch.Elapsed.TotalSeconds, null, [], $"{error.GetType().Name}: {error.Message}");
        }
    }

    private static IReadOnlyList<string> Regressions(PlanMetrics baseline, PlanMetrics candidate)
    {
        if (baseline.Error is not null || candidate.Error is not null) return [];
        var reasons = new List<string>();
        if (candidate.CP < baseline.CP || candidate.Achievement < baseline.Achievement || candidate.DXScore < baseline.DXScore)
            reasons.Add("judgement");
        if (candidate.Gaps > baseline.Gaps) reasons.Add("gaps");
        if (candidate.Patterns.Count(p => p.Kind == PatternKind.Rotation && p.Completed) < baseline.Patterns.Count(p => p.Kind == PatternKind.Rotation && p.Completed))
            reasons.Add("rotation-completion");
        var before = Aggregate([baseline]);
        var after = Aggregate([candidate]);
        if (after.AssignedFastJackPairs < before.AssignedFastJackPairs) reasons.Add("fast-jack-assignment");
        if (after.FastJackSameHandRatio is { } afterRatio && before.FastJackSameHandRatio is { } beforeRatio && afterRatio > beforeRatio + 1e-9)
            reasons.Add("fast-jack-same-hand");
        return reasons;
    }

    private static Totals Aggregate(IEnumerable<PlanMetrics> source)
    {
        var plans = source.Where(p => p.Error is null).ToArray();
        var patterns = plans.SelectMany(p => p.Patterns).ToArray();
        return new(plans.Length, plans.Count(p => p.Status == "all-cp"), plans.Sum(p => p.Notes), plans.Sum(p => p.CP), plans.Sum(p => p.Gaps),
            plans.Sum(p => p.Seconds), patterns.Count(p => p.Kind == PatternKind.Rotation), patterns.Count(p => p.Kind == PatternKind.Rotation && p.Completed),
            patterns.Count(p => p.Kind == PatternKind.Jack), patterns.Count(p => p.Kind == PatternKind.Jack && p.Completed),
            patterns.Sum(p => p.FastJackPairs), patterns.Sum(p => p.AssignedFastJackPairs), patterns.Sum(p => p.SameHandFastJackPairs));
    }

    private static string ValidationMarkdown(ValidationReport report)
    {
        var text = new StringBuilder("# 高速旋转与纵连回放验证\n\n");
        text.AppendLine("清单按实际 PatternAnalyzer 配置与等级选取，选取不依赖曲名。每张谱分别运行 NormalHandPlanner 和 HumanPlanner，并核验整曲输入回放。此处为压力样本，不能代替整库或对应等级的通过率。").AppendLine();
        text.AppendLine(FormattableString.Invariant($"模型：判定环半径 {report.Model.JudgementRadiusMillimeters:F3} mm，速度上限 {report.Model.MaxSpeedMillimetersPerSecond:F0} mm/s，偏好速度 {report.Model.PreferredSpeedMillimetersPerSecond:F0} mm/s，偏好单手击打间隔 {report.Model.PreferredStrikeIntervalSeconds:F3} 秒。速度和击打偏好是未标定的搜索参数。")).AppendLine();
        text.AppendLine(FormattableString.Invariant($"Rotation 完整段要求段内全部音符 CP。快纵连来自 Jack 内间隔小于 {report.Model.FastJackIntervalExclusive:F3} 秒、至少 3 音符的连续子段；同手重复比例为相邻两音都有唯一分配手时，同手的音符对数除以可比较音符对数。未分配的音符对另记，避免漏打使比例看起来更低。")).AppendLine();
        text.AppendLine("| 求解器 | 全 CP 谱 | CP / 音符 | Gap | Rotation 完整 / 总段 | 快纵连完整 / 总段 | 同手 / 已分配音符对 | 未分配音符对 | 累计秒数 |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        AppendTotal("NormalHandPlanner", report.Baseline);
        AppendTotal("HumanPlanner", report.Candidate);
        text.AppendLine().AppendLine($"异常 {report.Errors} 张；出现至少一项回退 {report.RegressedCharts} 张。回退包含成绩下降、Gap 增加、Rotation 完整段减少、快纵连分配减少或同手重复比例上升，逐项原因保存在 JSON。").AppendLine();
        text.AppendLine("| 谱面 | 难度 / 等级 | CP 基线 → 候选 | Gap | 完整 Rotation | 快纵连同手比例 | 回退项 |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var item in report.Charts)
        {
            var chart = item.Selection.Chart;
            if (item.Baseline is null || item.Candidate is null)
            {
                text.AppendLine($"| {chart.Title.Replace("|", "\\|")} | {chart.Difficulty}/{chart.DisplayLevel} | 异常 | | | | {item.Error?.Replace("|", "\\|")} |");
                continue;
            }
            var before = Aggregate([item.Baseline]);
            var after = Aggregate([item.Candidate]);
            text.AppendLine($"| {chart.Title.Replace("|", "\\|")} | {chart.Difficulty}/{chart.DisplayLevel} | {item.Baseline.CP} → {item.Candidate.CP} / {chart.Notes} | {item.Baseline.Gaps} → {item.Candidate.Gaps} | {before.CompletedRotations} → {after.CompletedRotations} / {after.Rotations} | {Percent(before.FastJackSameHandRatio)} → {Percent(after.FastJackSameHandRatio)} | {string.Join(", ", item.Regressions)} |");
        }
        text.AppendLine().AppendLine("[pattern-validation.json](pattern-validation.json) 保留选取理由、谱面与程序集 SHA-256、各段判定和动作统计；不含谱面正文或手部轨迹。");
        return text.ToString();

        void AppendTotal(string name, Totals metrics) => text.AppendLine(FormattableString.Invariant(
            $"| {name} | {metrics.AllCP}/{metrics.Charts} | {metrics.CP}/{metrics.Notes} | {metrics.Gaps} | {metrics.CompletedRotations}/{metrics.Rotations} | {metrics.CompletedFastJacks}/{metrics.FastJacks} | {metrics.SameHandFastJackPairs}/{metrics.AssignedFastJackPairs} ({Percent(metrics.FastJackSameHandRatio)}) | {metrics.FastJackPairs - metrics.AssignedFastJackPairs} | {metrics.Seconds:F2} |"));
        static string Percent(double? ratio) => ratio is null ? "—" : (ratio.Value * 100).ToString("F2", CultureInfo.InvariantCulture) + "%";
    }

    private static IReadOnlyList<SegmentCase> Describe(Chart chart)
    {
        var result = new List<SegmentCase>();
        foreach (var pattern in PatternAnalyzer.Analyze(GameplayDemandCompiler.Compile(chart)))
        {
            if (pattern.Kind == PatternKind.Rotation)
            {
                var links = pattern.Demands.Zip(pattern.Demands.Skip(1), (a, b) =>
                    (Interval: b.Start - a.Start, Steps: Math.Abs(ButtonStep(a.Note.StartPosition, b.Note.StartPosition)))).ToArray();
                var duration = pattern.Demands[^1].Start - pattern.Start;
                var turns = links.Sum(l => l.Steps) / 8.0;
                result.Add(new(PatternKind.Rotation, pattern.Direction, pattern.Start, pattern.Demands[^1].Start,
                    pattern.Demands.Select(d => d.Id).ToArray(), links.Min(l => l.Interval), links.Max(l => l.Interval),
                    (pattern.Demands.Count - 1) / duration, turns, turns / duration, links.Max(l => l.Steps / (8 * l.Interval))));
            }
            if (pattern.Kind != PatternKind.Jack) continue;
            var index = 0;
            while (index < pattern.Demands.Count)
            {
                var end = index + 1;
                while (end < pattern.Demands.Count && pattern.Demands[end].Start - pattern.Demands[end - 1].Start < NormalHandPlanner.FastRepeatInterval - 1e-8)
                    end++;
                if (end - index >= 3)
                {
                    var notes = pattern.Demands.Skip(index).Take(end - index).ToArray();
                    var intervals = notes.Zip(notes.Skip(1), (a, b) => b.Start - a.Start).ToArray();
                    result.Add(new(PatternKind.Jack, 0, notes[0].Start, notes[^1].Start, notes.Select(d => d.Id).ToArray(),
                        intervals.Min(), intervals.Max(), (notes.Length - 1) / (notes[^1].Start - notes[0].Start), 0, 0, 0));
                }
                index = end;
            }
        }
        return result;
    }

    private static int ButtonStep(int from, int to)
    {
        var step = (to - from + 8) % 8;
        return step > 4 ? step - 8 : step;
    }

    private static string DisplayLevel(string level)
    {
        if (level.EndsWith('+')) return level;
        if (!decimal.TryParse(level, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return "unknown";
        var integer = decimal.Floor(value);
        return integer.ToString(CultureInfo.InvariantCulture) + (value - integer >= .6m ? "+" : "");
    }

    private static decimal LevelOrder(string level) => decimal.TryParse(level.TrimEnd('+'), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        ? value + (level.EndsWith('+') ? .6m : 0) : -1;
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

    private static string SelectionMarkdown(Selection selection)
    {
        var text = new StringBuilder("# 旋转与快速纵连验证清单\n\n");
        text.AppendLine(FormattableString.Invariant($"范围为 Expert 12+ 起、Master 11+ 起。配置来自 PatternAnalyzer；快速纵连取 Jack 内相邻间隔小于 {selection.FastJackIntervalExclusive:F3} 秒、至少 3 音符的连续子段。先覆盖各等级的最快旋转及最长快速纵连，再补齐顺逆方向和全库速度、长度极端，最多 30 张。曲名不参与选取。")).AppendLine();
        text.AppendLine("| 难度 | 等级 | 曲目 | Rotation 段 | 快速 Jack 段 | 选取理由 |");
        text.AppendLine("|---|---|---|---:|---:|---|");
        foreach (var choice in selection.Charts)
            text.AppendLine($"| {choice.Chart.Difficulty} | {choice.Chart.DisplayLevel} | {choice.Chart.Title.Replace("|", "\\|")} | {choice.Chart.Segments.Count(s => s.Kind == PatternKind.Rotation)} | {choice.Chart.Segments.Count(s => s.Kind == PatternKind.Jack)} | {string.Join("；", choice.Reasons)} |");
        text.AppendLine().AppendLine("清单 JSON 包含相对路径、谱面 SHA-256、音符编号、配置起止时间、旋转圈数/速度和纵连间隔；不包含谱面正文或手部轨迹。");
        return text.ToString();
    }

    internal sealed record SegmentCase(PatternKind Kind, int Direction, double Start, double End, IReadOnlyList<int> NoteIds,
        double MinimumIntervalSeconds, double MaximumIntervalSeconds, double NotesPerSecond, double Turns, double AverageTurnsPerSecond, double PeakTurnsPerSecond);
    internal sealed record ChartCase(string Path, string Title, int Difficulty, string Level, string DisplayLevel,
        string ChartSha256, int Notes, IReadOnlyList<SegmentCase> Segments);
    internal sealed record SelectedChart(ChartCase Chart, IReadOnlyList<string> Reasons);
    internal sealed record ScanFailure(string Path, string Error);
    internal sealed record Cohort(int Difficulty, string Level, int ChartsWithPatterns, int Rotations, int FastJacks, int Selected);
    internal sealed record Selection(DateTimeOffset CreatedAtUtc, string AnalysisAssemblySha256, string Scope, double FastJackIntervalExclusive,
        int Files, int EligibleCharts, int ChartsWithPatterns, IReadOnlyList<Cohort> Inventory, IReadOnlyList<ScanFailure> ParseErrors, IReadOnlyList<SelectedChart> Charts);
    private sealed record PatternMetrics(PatternKind Kind, int Direction, double Start, double End, int Notes, int CP, bool Completed,
        int FastJackPairs, int AssignedFastJackPairs, int SameHandFastJackPairs);
    private sealed record PlanMetrics(string Status, int Notes, int CP, int Gaps, decimal Achievement, int DXScore,
        double Seconds, HumanMotionMetrics? Movement, IReadOnlyList<PatternMetrics> Patterns, string? Error);
    private sealed record ChartValidation(SelectedChart Selection, PlanMetrics? Baseline, PlanMetrics? Candidate, IReadOnlyList<string> Regressions, string? Error);
    private sealed record Totals(int Charts, int AllCP, int Notes, int CP, int Gaps, double Seconds, int Rotations, int CompletedRotations,
        int FastJacks, int CompletedFastJacks, int FastJackPairs, int AssignedFastJackPairs, int SameHandFastJackPairs)
    {
        public double? FastJackSameHandRatio => AssignedFastJackPairs == 0 ? null : (double)SameHandFastJackPairs / AssignedFastJackPairs;
    }
    private sealed record ModelParameters(double JudgementRadiusMillimeters, double MaxSpeedMillimetersPerSecond,
        double PreferredSpeedMillimetersPerSecond, double PreferredStrikeIntervalSeconds, double FastJackIntervalExclusive);
    private sealed record ValidationReport(DateTimeOffset CreatedAtUtc, string SelectionSha256, string SelectionAnalysisAssemblySha256,
        string PlannerAssemblySha256, string RuleVersion, ModelParameters Model, double Seconds, string Scope, int Errors, int RegressedCharts,
        Totals Baseline, Totals Candidate, IReadOnlyList<ChartValidation> Charts);
}
