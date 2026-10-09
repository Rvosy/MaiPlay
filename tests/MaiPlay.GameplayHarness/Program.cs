using System.Text.Json;
using System.Text.Json.Serialization;
using MaiPlay.Core;

if (args.Length >= 5 && args[0] == "--compare-planners")
    return PlannerComparison.Run(args[3], args[4], int.Parse(args[1]), args[2],
        chart => NormalHandPlanner.Plan(chart), chart => HumanPlanner.Plan(chart),
        nameof(NormalHandPlanner), nameof(HumanPlanner));

if (args.Length >= 3 && args[0] == "--batch-expert")
    return ExpertBatch.Run(args[1], args[2], args.Length > 3 ? args[3] : null);
if (args.Length >= 4 && args[0] == "--batch-human")
    return ExpertBatch.Run(args[2], args[3], args.Length > 4 ? args[4] : null, int.Parse(args[1]), true);
if (args.Length >= 4 && args[0] == "--batch")
    return ExpertBatch.Run(args[2], args[3], args.Length > 4 ? args[4] : null, int.Parse(args[1]));

if (args.Length >= 2 && args[0] == "--hand-samples")
    return HandReplaySamples.Write(args[1], args.Length > 2 ? args[2] : null);

if (args.Length >= 3 && args[0] is "--plan" or "--plan-human")
{
    var output = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(output);
    try
    {
        var chart = SimaiParser.LoadSong(args[1]).Charts.Single(c => c.Difficulty == (args.Length > 3 ? int.Parse(args[3]) : 4));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = args[0] == "--plan-human" ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
        var inspected = PlannerComparison.WithMotionDiagnostics(plan);
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(Path.Combine(output, "hands.json"), JsonSerializer.Serialize(new InputLog { Hands = plan.Tracks }, options));
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            chart.Title, chart.Difficulty, chart.Level, notes = chart.Notes.Count, plan.AllCriticalPerfect,
            inspected.IsExecutable, inspected.IsSolved, plan.Achievement, plan.DXScore,
            seconds = watch.Elapsed.TotalSeconds, plan.Strokes, plan.Gaps,
            diagnostics = new
            {
                inspected.Diagnostics!.Movement,
                MovementCost = plan.Diagnostics?.MovementCost,
                plan.Diagnostics?.Patterns, plan.Diagnostics?.TechniqueUsage,
                inspected.Diagnostics.SlideConnections, plan.Diagnostics?.CoveredDemands
            },
            results = plan.Results.Select(e => new { e.Note.Id, e.Note.Kind, e.Note.Time, e.Note.Source, e.Grade, e.DifferenceSeconds, e.Reason })
        }, options));
        Console.WriteLine($"{chart.Title}: {plan.Achievement:F4}%，CP {plan.Results.Count(e => e.Grade == AchievementGrade.Perfect)}/{chart.Notes.Count}，模型内可执行 {inspected.IsExecutable}，已解 {inspected.IsSolved}，{watch.Elapsed.TotalSeconds:F2}s");
        var movement = inspected.Diagnostics!.Movement;
        Console.WriteLine($"手部圆碰撞 {movement.Collisions.Count} 次 / {movement.Collisions.Seconds:F4}s，超速 {movement.SpeedLimitViolations} 段。");
        foreach (var error in plan.Errors.Take(30)) Console.WriteLine($"#{error.Note.Id} {error.Note.Time:F3}s {error.Note.Source}: {error.Grade} {error.Reason}");
        return inspected.IsSolved ? 0 : 2;
    }
    catch (Exception e)
    {
        File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new { status = "未生成完整方案", error = e.Message }));
        Console.Error.WriteLine(e.Message);
        return 1;
    }
}

if (args.Length < 1)
{
    Console.WriteLine("用法：dotnet run --project tests/MaiPlay.GameplayHarness -- 谱面路径 [输入日志.json] [结果.json]");
    Console.WriteLine("单谱：--plan-human 谱面路径 输出目录 [难度编号]");
    Console.WriteLine("对比：--compare-planners 难度编号 显示等级或all 曲库目录 输出目录");
    return 1;
}
try
{
    var song = SimaiParser.LoadSong(args[0]);
    var log = args.Length > 1 ? InputLog.Load(args[1]) : new InputLog();
    var reports = song.Charts.Select(chart =>
    {
        var session = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
        var events = session.Advance(session.FinishTime + .1);
        return new
        {
            chart.Title, chart.Difficulty, chart.Level, RuleVersion = InputLog.CurrentRuleVersion,
            session.Completed, session.Missed, session.Remaining, session.Combo,
            Achievement = session.Achievement.DisplayText, session.DXScore, session.MaximumDXScore,
            session.FastCount, session.LateCount,
            Events = events.Select(e => new { NoteId = e.Note.Id, NoteKind = e.Note.Kind, e.Kind, e.Grade, e.Time, e.DifferenceSeconds, e.TimingKind, e.Reason }),
            SlideTrace = session.SlideTrace
        };
    }).ToArray();
    var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    var json = JsonSerializer.Serialize(reports, options);
    if (args.Length > 2)
    {
        var path = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        Console.WriteLine(path);
    }
    else Console.WriteLine(json);
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
