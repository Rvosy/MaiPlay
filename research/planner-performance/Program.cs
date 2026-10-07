using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using MaiPlay.Core;

if (args.Length is < 3 or > 5)
{
    Console.Error.WriteLine("用法：曲库目录 样本清单.json 输出.json [重复次数，默认3] [human或normal，默认human]");
    return 1;
}
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
var cases = JsonSerializer.Deserialize<Case[]>(File.ReadAllText(args[1]), json)!;
var repetitions = args.Length > 3 ? int.Parse(args[3]) : 3;
var planner = args.Length > 4 ? args[4] : "human";
if (repetitions < 1 || planner is not ("human" or "normal")) throw new ArgumentException("重复次数须为正数，求解器须为 human 或 normal。");
HandPlan Solve(Chart chart) => planner == "human" ? HumanPlanner.Plan(chart) : NormalHandPlanner.Plan(chart);
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
var rows = new List<object>();
using var process = Process.GetCurrentProcess();
var assemblyHash = Hash(File.ReadAllBytes(typeof(HumanPlanner).Assembly.Location));
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
// 先预热搜索、Slide、Touch 和回放路径，计时不包含解析和报告序列化。
Solve(SimaiParser.ParseChart("(120){#0.125}1,2,3,4,5,6,7,8,{4}1-5[0##1],A1/A3/A5,Ch[#1],E", first: 1));
foreach (var item in cases)
{
    var file = Path.Combine(args[0], item.Path);
    var chart = SimaiParser.LoadSong(file).Charts.Single(c => c.Difficulty == item.Difficulty);
    var runs = new List<object>();
    for (var run = 0; run < repetitions; run++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var cpu = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        var plan = Solve(chart);
        timer.Stop();
        var cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
        var allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
        // 包含全部轨迹、输入事件、判定与诊断，防止只比较总分漏掉行为变化。
        var fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            plan.Tracks, plan.Replay.Events, plan.Strokes, plan.Gaps, plan.Diagnostics,
            Results = plan.Results.Select(e => new { e.Note.Id, e.Kind, e.Grade, e.Time, e.DifferenceSeconds, e.Reason }),
            plan.Achievement, plan.DXScore
        }, json));
        runs.Add(new { seconds = timer.Elapsed.TotalSeconds, cpuSeconds, allocatedBytes, fingerprint,
            cp = plan.Results.Count(e => e.Grade == AchievementGrade.Perfect), plan.Achievement, plan.DXScore,
            gaps = plan.Gaps.Count, speedLimitViolations = plan.Diagnostics?.Movement.SpeedLimitViolations });
        Console.WriteLine($"{chart.Title} [{item.Difficulty}] #{run + 1}: {timer.Elapsed.TotalSeconds:F3}s, CPU {cpuSeconds:F3}s, {allocatedBytes / 1048576d:F1} MiB");
    }
    rows.Add(new { item.Path, chart.Title, item.Difficulty, chart.Level, notes = chart.Notes.Count,
        chartSha256 = Hash(File.ReadAllBytes(file)), runs });
    File.WriteAllText(output, JsonSerializer.Serialize(new
    {
        createdAtUtc = DateTimeOffset.UtcNow, planner, assemblyHash, InputLog.CurrentRuleVersion,
        framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        processorCount = Environment.ProcessorCount, repetitions, charts = rows
    }, json));
}
return 0;

internal sealed record Case(string Path, int Difficulty);
