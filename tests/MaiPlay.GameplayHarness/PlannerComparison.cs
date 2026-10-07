using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaiPlay.Core;

internal static class PlannerComparison
{
    private static readonly HumanMotionModel MotionModel = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static int Run(string root, string output, int difficulty, string displayLevel,
        Func<Chart, HandPlan> baseline, Func<Chart, HandPlan> candidate,
        string baselineName, string candidateName)
    {
        if (difficulty is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(difficulty));
        if (displayLevel != "all" && DisplayLevel(displayLevel) != displayLevel)
            throw new ArgumentException("等级须为 12、12+ 这样的显示等级，或 all。", nameof(displayLevel));
        root = Path.GetFullPath(root);
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var files = Directory.GetFiles(root, "maidata.txt", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var charts = new ConcurrentBag<ChartComparison>();
        var parseErrors = new ConcurrentBag<ParseFailure>();
        var missingDifficulty = 0;
        var excludedLevel = 0;
        var done = 0;
        var watch = Stopwatch.StartNew();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, file =>
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            Chart? chart;
            try
            {
                chart = SimaiParser.LoadSong(file).Charts.SingleOrDefault(c => c.Difficulty == difficulty);
            }
            catch (Exception error)
            {
                parseErrors.Add(new(relative, error.GetType().Name, error.Message.Replace(root, "<charts>", StringComparison.OrdinalIgnoreCase)));
                return;
            }
            if (chart is null)
            {
                Interlocked.Increment(ref missingDifficulty);
                return;
            }
            var level = DisplayLevel(chart.Level);
            if (displayLevel != "all" && level != displayLevel)
            {
                Interlocked.Increment(ref excludedLevel);
                return;
            }
            var before = Evaluate(chart, baseline);
            var after = Evaluate(chart, candidate);
            charts.Add(new(relative, chart.Title, chart.Difficulty, chart.Level, level ?? "unknown", chart.Notes.Count,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), before, after, Change(before, after)));
            var count = Interlocked.Increment(ref done);
            if (count % 25 == 0) Console.WriteLine($"已对比 {count} 张谱面 · {watch.Elapsed.TotalSeconds:F1}s");
        });
        var ordered = charts.OrderBy(c => c.Path, StringComparer.Ordinal).ToArray();
        var summary = new ComparisonReport(DateTimeOffset.UtcNow, InputLog.CurrentRuleVersion,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(HandPlan).Assembly.Location))),
            baselineName, candidateName,
            new(MotionModel.Geometry.JudgementRadiusMillimeters, MotionModel.MaxSpeedMillimetersPerSecond, MotionModel.PreferredSpeedMillimetersPerSecond),
            difficulty, displayLevel, files.Length, missingDifficulty, excludedLevel,
            watch.Elapsed.TotalSeconds, Aggregate(ordered),
            ordered.GroupBy(c => c.DisplayLevel).OrderBy(g => LevelOrder(g.Key)).Select(g => new LevelSummary(g.Key, Aggregate(g.ToArray()))).ToArray(),
            parseErrors.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray(), ordered);
        File.WriteAllText(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(summary, JsonOptions));
        File.WriteAllText(Path.Combine(output, "comparison.md"), Markdown(summary));
        Console.WriteLine($"难度 {difficulty} / {displayLevel}：对比 {ordered.Length} 张，改善 {summary.Totals.Improved}，退步 {summary.Totals.Regressed}，有得有失 {summary.Totals.Mixed}，求解异常 {summary.Totals.Errors}，解析异常 {parseErrors.Count}。");
        return ordered.Length == 0 || parseErrors.Count > 0 || summary.Totals.Errors > 0 ? 1
            : summary.Totals.Regressed + summary.Totals.Mixed > 0 ? 2 : 0;
    }

    internal static string? DisplayLevel(string level)
    {
        level = level.Trim();
        if (level.EndsWith('+'))
            return int.TryParse(level[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var whole) && whole >= 0
                ? whole.ToString(CultureInfo.InvariantCulture) + "+" : null;
        if (!decimal.TryParse(level, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var constant) || constant < 0)
            return null;
        var integer = decimal.Floor(constant);
        return integer.ToString(CultureInfo.InvariantCulture) + (constant - integer >= .6m ? "+" : "");
    }

    private static decimal LevelOrder(string level) =>
        decimal.TryParse(level.TrimEnd('+'), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value + (level.EndsWith('+') ? .6m : 0) : decimal.MaxValue;

    private static Metrics Evaluate(Chart chart, Func<Chart, HandPlan> planner)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var plan = planner(chart);
            return new(plan.AllCriticalPerfect ? "all-cp" : "partial",
                plan.Results.Count(e => e.Grade == AchievementGrade.Perfect), plan.Achievement, plan.DXScore,
                plan.Gaps.Count, watch.Elapsed.TotalSeconds,
                plan.Errors.GroupBy(e => e.Note.Kind).ToDictionary(g => g.Key, g => g.Count()), null,
                MotionModel.Analyze(plan.Tracks, plan.Strokes), plan.Diagnostics?.MovementCost,
                plan.Diagnostics?.Patterns, plan.Diagnostics?.TechniqueUsage);
        }
        catch (Exception error)
        {
            return new("error", 0, 0, 0, 0, watch.Elapsed.TotalSeconds, new Dictionary<NoteKind, int>(), $"{error.GetType().Name}: {error.Message}");
        }
    }

    internal static string Change(Metrics baseline, Metrics candidate)
    {
        if (baseline.Status == "error" || candidate.Status == "error") return "error";
        var improved = candidate.CP > baseline.CP || candidate.Achievement > baseline.Achievement || candidate.DXScore > baseline.DXScore || candidate.Gaps < baseline.Gaps;
        var regressed = candidate.CP < baseline.CP || candidate.Achievement < baseline.Achievement || candidate.DXScore < baseline.DXScore || candidate.Gaps > baseline.Gaps;
        return improved && regressed ? "mixed" : regressed ? "regressed" : improved ? "improved" : "unchanged";
    }

    private static Totals Aggregate(IReadOnlyList<ChartComparison> charts) => new(charts.Count,
        charts.Count(c => c.Change == "improved"), charts.Count(c => c.Change == "regressed"), charts.Count(c => c.Change == "mixed"),
        charts.Count(c => c.Change == "unchanged"), charts.Count(c => c.Change == "error"), charts.Sum(c => c.Notes),
        Sum(charts.Select(c => c.Baseline)), Sum(charts.Select(c => c.Candidate)));

    private static PlannerTotals Sum(IEnumerable<Metrics> source)
    {
        var metrics = source.ToArray();
        var completed = metrics.Where(m => m.Status != "error").ToArray();
        return new(metrics.Count(m => m.Status == "all-cp"), metrics.Count(m => m.Status == "partial"), metrics.Count(m => m.Status == "error"),
            completed.Sum(m => m.CP), completed.Sum(m => m.Gaps), completed.Length == 0 ? 0 : completed.Average(m => m.Achievement), metrics.Sum(m => m.Seconds),
            completed.Sum(m => m.Movement?.TotalDistanceMillimeters ?? 0),
            completed.Select(m => m.Movement?.PeakSpeedMillimetersPerSecond ?? 0).DefaultIfEmpty().Max(),
            completed.Sum(m => m.Movement?.SpeedLimitViolations ?? 0), completed.Sum(m => m.Movement?.ProjectedCrossingCount ?? 0),
            completed.Sum(m => m.Movement?.ProjectedCrossingSeconds ?? 0), completed.Sum(m => m.Movement?.DirectionReversalCount ?? 0),
            completed.SelectMany(m => m.Patterns ?? []).GroupBy(p => p.Kind)
                .OrderBy(g => g.Key).Select(g => new PatternOutcome(g.Key, g.Sum(p => p.Detected), g.Sum(p => p.Solved))).ToArray());
    }

    private static string Markdown(ComparisonReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# 求解器对比").AppendLine();
        text.AppendLine($"基线：`{report.Baseline}`；候选：`{report.Candidate}`。难度编号 {report.Difficulty}，显示等级 `{report.DisplayLevel}`。定数小数部分从 `.6` 起归入 `+`。").AppendLine();
        text.AppendLine($"判定规则：`{report.RuleVersion}`。输入为同一份谱面，两个求解器分别生成轨迹并通过 PlaybackSession 核验。耗时包含规划与复验；并行扫描的单谱耗时仅供参考。").AppendLine();
        text.AppendLine("| 等级 | 谱面 | 基线全 CP | 候选全 CP | 基线非 CP | 候选非 CP | 基线 Gap | 候选 Gap | 改善 | 退步 | 有得有失 | 异常 |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var level in report.Levels)
        {
            var t = level.Totals;
            text.AppendLine($"| {level.Level} | {t.Charts} | {t.Baseline.AllCP} | {t.Candidate.AllCP} | {t.Notes - t.Baseline.CP} | {t.Notes - t.Candidate.CP} | {t.Baseline.Gaps} | {t.Candidate.Gaps} | {t.Improved} | {t.Regressed} | {t.Mixed} | {t.Errors} |");
        }
        text.AppendLine().AppendLine($"扫描 {report.Files} 个文件；缺少所选难度 {report.MissingDifficulty}，等级不符 {report.ExcludedLevel}，解析异常 {report.ParseErrors.Count}。");
        text.AppendLine().AppendLine("CP 数、达成率、DX 分数、Gap 数均无下降且至少一项改善时记为改善；同时有改善和下降时单列为“有得有失”。退出码 0 表示没有退步，不能据此认定全部谱面全 CP。退出码 1 表示无匹配谱面或出现异常，2 表示存在退步或有得有失。");
        text.AppendLine().AppendLine("## 动作指标").AppendLine();
        text.AppendLine("距离和速度由 HumanMotionModel 按同一机台几何换算；速度阈值尚未经过人体标定，不能作为真人可打性的结论。投影交叉只统计左右手在屏幕坐标中的相对位置。").AppendLine();
        text.AppendLine(FormattableString.Invariant($"本次参数：判定环半径 {report.MotionParameters.JudgementRadiusMillimeters:F3} mm，速度上限 {report.MotionParameters.MaxSpeedMillimetersPerSecond:F0} mm/s，偏好速度 {report.MotionParameters.PreferredSpeedMillimetersPerSecond:F0} mm/s。")).AppendLine();
        text.AppendLine("| 求解器 | 总路程 mm | 峰值速度 mm/s | 超速分段 | 投影交叉次数 | 投影交叉秒数 | 方向反转次数 |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        AppendMovement(report.Baseline, report.Totals.Baseline);
        AppendMovement(report.Candidate, report.Totals.Candidate);
        var patterns = report.Levels.SelectMany(level => level.Totals.Candidate.Patterns.Select(pattern => (level.Level, Pattern: pattern))).ToArray();
        if (patterns.Length > 0)
        {
            text.AppendLine().AppendLine("## 候选方案配置覆盖").AppendLine();
            text.AppendLine("配置按 PatternAnalyzer 的段落切分；成功表示该配置涉及的音符全部为 CP。").AppendLine();
            text.AppendLine("| 等级 | 配置 | 检出 | 全 CP | 成功率 |");
            text.AppendLine("|---|---|---:|---:|---:|");
            foreach (var (level, pattern) in patterns)
                text.AppendLine(FormattableString.Invariant($"| {level} | {pattern.Kind} | {pattern.Detected} | {pattern.Solved} | {(pattern.Detected == 0 ? 0 : 100.0 * pattern.Solved / pattern.Detected):F2}% |"));
        }
        var changed = report.Charts.Where(c => c.Change != "unchanged").ToArray();
        if (changed.Length > 0)
        {
            text.AppendLine().AppendLine("## 发生变化的谱面").AppendLine();
            text.AppendLine("| 谱面 | 定数 | 变化 | CP | Gap | 达成率 |");
            text.AppendLine("|---|---:|---|---:|---:|---:|");
            foreach (var chart in changed)
                text.AppendLine(FormattableString.Invariant($"| {chart.Title.Replace("|", "\\|")} (`{chart.Path.Replace("|", "\\|")}#{chart.Difficulty}`) | {chart.Level} | {chart.Change} | {chart.Baseline.CP} → {chart.Candidate.CP} | {chart.Baseline.Gaps} → {chart.Candidate.Gaps} | {chart.Baseline.Achievement:F4} → {chart.Candidate.Achievement:F4} |"));
        }
        text.AppendLine().AppendLine("逐谱指标、谱面 SHA-256 和求解器程序集 SHA-256 保存在同目录的 [comparison.json](comparison.json)。报告不包含谱面正文或手部轨迹。");
        return text.ToString();

        void AppendMovement(string name, PlannerTotals metrics) =>
            text.AppendLine(FormattableString.Invariant($"| {name} | {metrics.TotalDistanceMillimeters:F1} | {metrics.PeakSpeedMillimetersPerSecond:F1} | {metrics.SpeedLimitViolations} | {metrics.ProjectedCrossingCount} | {metrics.ProjectedCrossingSeconds:F3} | {metrics.DirectionReversalCount} |"));
    }

    internal sealed record Metrics(string Status, int CP, decimal Achievement, int DXScore, int Gaps,
        double Seconds, IReadOnlyDictionary<NoteKind, int> FailuresByKind, string? Error,
        HumanMotionMetrics? Movement = null, double? MovementCost = null, IReadOnlyList<PatternOutcome>? Patterns = null,
        IReadOnlyDictionary<string, int>? TechniqueUsage = null);
    private sealed record ChartComparison(string Path, string Title, int Difficulty, string Level, string DisplayLevel,
        int Notes, string ChartSha256, Metrics Baseline, Metrics Candidate, string Change);
    private sealed record ParseFailure(string Path, string Type, string Message);
    private sealed record PlannerTotals(int AllCP, int Partial, int Errors, int CP, int Gaps, decimal AverageAchievement, double Seconds,
        double TotalDistanceMillimeters, double PeakSpeedMillimetersPerSecond, int SpeedLimitViolations,
        int ProjectedCrossingCount, double ProjectedCrossingSeconds, int DirectionReversalCount, IReadOnlyList<PatternOutcome> Patterns);
    private sealed record Totals(int Charts, int Improved, int Regressed, int Mixed, int Unchanged, int Errors,
        int Notes, PlannerTotals Baseline, PlannerTotals Candidate);
    private sealed record LevelSummary(string Level, Totals Totals);
    private sealed record MotionParameters(double JudgementRadiusMillimeters, double MaxSpeedMillimetersPerSecond, double PreferredSpeedMillimetersPerSecond);
    private sealed record ComparisonReport(DateTimeOffset CreatedAtUtc, string RuleVersion, string PlannerAssemblySha256,
        string Baseline, string Candidate, MotionParameters MotionParameters, int Difficulty, string DisplayLevel, int Files, int MissingDifficulty,
        int ExcludedLevel, double Seconds, Totals Totals, IReadOnlyList<LevelSummary> Levels,
        IReadOnlyList<ParseFailure> ParseErrors, IReadOnlyList<ChartComparison> Charts);
}
