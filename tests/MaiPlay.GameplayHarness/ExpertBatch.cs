using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using MaiPlay.Core;

internal static class ExpertBatch
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static int Run(string root, string output, string? retrySummary = null, int difficulty = 4, bool humanPlanner = false)
    {
        if (difficulty is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(difficulty));
        root = Path.GetFullPath(root);
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var files = Directory.GetFiles(root, "maidata.txt", SearchOption.AllDirectories).Order().ToArray();
        int? Version(string path) => int.TryParse(path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0].Split('_')[0], out var version) ? version : null;
        var newVersions = files.Select(f => Version(Path.GetRelativePath(root, f))).OfType<int>().Distinct().OrderDescending().Take(2).ToHashSet();
        if (retrySummary is not null)
        {
            using var previous = JsonDocument.Parse(File.ReadAllText(retrySummary));
            var paths = previous.RootElement.GetProperty("charts").EnumerateArray()
                .Where(c => c.GetProperty("status").GetString() is "partial" or "error")
                .Select(c => c.GetProperty("path").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            files = files.Where(f => paths.Contains(Path.GetRelativePath(root, f))).ToArray();
        }
        var reports = new ConcurrentBag<Report>();
        var watch = Stopwatch.StartNew();
        var done = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, file =>
        {
            var relative = Path.GetRelativePath(root, file);
            var timer = Stopwatch.StartNew();
            Report report;
            try
            {
                var song = SimaiParser.LoadSong(file);
                var chart = song.Charts.SingleOrDefault(c => c.Difficulty == difficulty);
                if (chart is null) report = new(relative, song.Title, "", "missing-difficulty", 0, 0, 0, 0, [], [], null, 0);
                else
                {
                    var plan = humanPlanner ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
                    report = new(relative, chart.Title, chart.Level, plan.AllCriticalPerfect ? "all-cp" : "partial",
                        chart.Notes.Count, plan.Results.Count(e => e.Grade == AchievementGrade.Perfect), plan.Achievement,
                        plan.DXScore, plan.Gaps, plan.Errors.Select(e => new Failure(e.Note.Id, e.Note.Kind,
                            e.Note.Time, e.Note.Source, e.Grade, e.DifferenceSeconds, e.Reason)).ToArray(), null, timer.Elapsed.TotalSeconds);
                    if (humanPlanner && plan.Diagnostics is { } diagnostics)
                        report = report with
                        {
                            IsExecutable = plan.IsExecutable, IsSolved = plan.IsSolved,
                            Collisions = diagnostics.Movement.Collisions.Count,
                            CollisionSeconds = diagnostics.Movement.Collisions.Seconds,
                            SpeedLimitViolations = diagnostics.Movement.SpeedLimitViolations,
                            ContinuousConnections = diagnostics.SlideConnections.Count(c => c.Continuous),
                            RelayConnections = diagnostics.SlideConnections.Count(c => c.IsRelay),
                            SameHandBreaks = diagnostics.SlideConnections.Count(c => c.PreviousHand is not null && c.PreviousHand == c.NextHand && !c.Continuous),
                            SameHandContactGapSeconds = diagnostics.SlideConnections.Where(c => c.PreviousHand is not null && c.PreviousHand == c.NextHand)
                                .Sum(c => c.ContactGapSeconds),
                            UnassignedConnections = diagnostics.SlideConnections.Count(c => c.PreviousHand is null || c.NextHand is null)
                        };
                }
            }
            catch (Exception e)
            {
                report = new(relative, Path.GetFileName(Path.GetDirectoryName(file))!, "", "error", 0, 0, 0, 0, [], [], e.ToString(), timer.Elapsed.TotalSeconds);
            }
            reports.Add(report);
            var destination = Path.Combine(output, Path.ChangeExtension(relative, ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, JsonSerializer.Serialize(new { report, difficulty, version = Version(relative),
                rating = decimal.TryParse(report.Level, NumberStyles.Number, CultureInfo.InvariantCulture, out var level) ? (int?)RatingCalculator.Calculate(level, report.Achievement) : null,
                chartSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) }, Options));
            var count = Interlocked.Increment(ref done);
            if (count % 25 == 0 || count == files.Length)
                Console.WriteLine($"{count}/{files.Length} · 全 CP {reports.Count(r => r.Status == "all-cp")} · {watch.Elapsed.TotalSeconds:F1}s");
        });
        var eligible = reports.Where(r => r.Status is "all-cp" or "partial").ToArray();
        var inspected = eligible.Where(r => r.IsExecutable.HasValue).ToArray();
        var ratings = eligible.Where(r => Version(r.Path) is not null && decimal.TryParse(r.Level, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            .Select(r => new RatingEntry($"{r.Path}#{difficulty}", r.Title, Version(r.Path)!.Value,
                decimal.Parse(r.Level, CultureInfo.InvariantCulture), r.Achievement)).ToArray();
        var summary = new
        {
            createdAt = DateTimeOffset.Now, root, difficulty, ruleVersion = InputLog.CurrentRuleVersion,
            planner = humanPlanner ? nameof(HumanPlanner) : nameof(NormalHandPlanner),
            plannerAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(NormalHandPlanner).Assembly.Location))),
            newVersions = newVersions.Order().ToArray(), ratingScope = retrySummary is null ? "full-library" : "retry-subset",
            averageAchievement = eligible.Length == 0 ? 0 : eligible.Average(r => r.Achievement),
            totalRating = ratings.Sum(r => r.Rating), ratingExcluded = eligible.Length - ratings.Length,
            best50 = RatingCalculator.SelectBest50(ratings, newVersions),
            maximumBest50 = RatingCalculator.SelectBest50(ratings.Select(r => r with { Achievement = 101 }), newVersions),
            seconds = watch.Elapsed.TotalSeconds, files = files.Length,
            allCP = reports.Count(r => r.Status == "all-cp"), partial = reports.Count(r => r.Status == "partial"),
            errors = reports.Count(r => r.Status == "error"), missingDifficulty = reports.Count(r => r.Status == "missing-difficulty"),
            notes = reports.Sum(r => r.Notes), cp = reports.Sum(r => r.CP),
            gaps = reports.Sum(r => r.Gaps.Count), nonCP = reports.Sum(r => r.Failures.Count),
            motionInspected = inspected.Length,
            executable = inspected.Length == 0 ? (int?)null : inspected.Count(r => r.IsExecutable == true),
            solved = inspected.Length == 0 ? (int?)null : inspected.Count(r => r.IsSolved == true),
            physicalViolations = inspected.Length == 0 ? (int?)null : inspected.Count(r => r.Collisions > 0 || r.SpeedLimitViolations > 0),
            collisions = inspected.Length == 0 ? (int?)null : inspected.Sum(r => r.Collisions),
            collisionSeconds = inspected.Length == 0 ? (double?)null : inspected.Sum(r => r.CollisionSeconds),
            speedLimitViolations = inspected.Length == 0 ? (int?)null : inspected.Sum(r => r.SpeedLimitViolations),
            continuousConnections = inspected.Length == 0 ? (int?)null : inspected.Sum(r => r.ContinuousConnections),
            relayConnections = inspected.Length == 0 ? (int?)null : inspected.Sum(r => r.RelayConnections),
            sameHandBreaks = inspected.Length == 0 ? (int?)null : inspected.Sum(r => r.SameHandBreaks),
            sameHandContactGapSeconds = inspected.Length == 0 ? (double?)null : inspected.Sum(r => r.SameHandContactGapSeconds),
            unassignedConnections = inspected.Length == 0 ? (int?)null : inspected.Sum(r => r.UnassignedConnections),
            charts = reports.OrderBy(r => r.Path).ToArray()
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Options));
        Console.WriteLine($"难度 {difficulty} 扫描完成：全 CP {summary.allCP}，部分完成 {summary.partial}，错误 {summary.errors}，缺少该难度 {summary.missingDifficulty}。B50 {summary.best50.Total}（旧 {summary.best50.OldTotal} + 新 {summary.best50.NewTotal}）。");
        if (inspected.Length > 0)
            Console.WriteLine($"动作已检查 {inspected.Length} 张：模型内可执行 {summary.executable}，已解 {summary.solved}，含物理违例 {summary.physicalViolations}，同手断触接缝 {summary.sameHandBreaks}，接力接缝 {summary.relayConnections}。");
        return summary.partial + summary.errors == 0 ? 0 : 2;
    }

    private sealed record Failure(int NoteId, NoteKind Kind, double Time, string Source, AchievementGrade Grade, double? DifferenceSeconds, string Reason);
    private sealed record Report(string Path, string Title, string Level, string Status, int Notes, int CP,
        decimal Achievement, int DXScore, IReadOnlyList<HandPlanGap> Gaps, IReadOnlyList<Failure> Failures, string? Error, double Seconds)
    {
        public bool? IsExecutable { get; init; }
        public bool? IsSolved { get; init; }
        public int? Collisions { get; init; }
        public double? CollisionSeconds { get; init; }
        public int? SpeedLimitViolations { get; init; }
        public int? ContinuousConnections { get; init; }
        public int? RelayConnections { get; init; }
        public int? SameHandBreaks { get; init; }
        public double? SameHandContactGapSeconds { get; init; }
        public int? UnassignedConnections { get; init; }
    }
}
