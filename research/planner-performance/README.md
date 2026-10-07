# 求解器性能测量

`PlannerPerformance` 顺序运行指定谱面，记录规划与整曲核验的耗时、累计托管分配量和完整输出指纹。解析、序列化和启动预热不计入耗时。输出指纹覆盖轨迹、输入事件、逐音符判定、缺口、动作诊断和成绩。

`cases.json` 包含四张本机谱面，分别覆盖 Slide、转圈、DX Touch 和密集 Expert 配置。它们用于性能回归，不代表整库分布。

## 冻结与运行

在修改前、后分别构建，并保留各自的 Core 程序集。修改前先执行以下命令；修改后将路径中的 `before` 改为 `after` 再执行。使用独立构建目录和 `Rebuild`，避免复制文件保留时间戳时误用旧程序集。

```powershell
dotnet build src/MaiPlay.Core -t:Rebuild -c Release --artifacts-path artifacts/perf-before-core
$perfCore = (Resolve-Path artifacts/perf-before-core/bin/MaiPlay.Core/release/MaiPlay.Core.dll).Path
dotnet build research/planner-performance -t:Rebuild -c Release --artifacts-path artifacts/perf-before-runner "-p:CoreAssembly=$perfCore"
dotnet artifacts/perf-before-runner/bin/PlannerPerformance/release/PlannerPerformance.dll .reference/charts/game-extracted research/planner-performance/cases.json artifacts/perf-before.json 3
```

最后一个可选参数为 `human` 或 `normal`，默认 `human`。两个版本使用同一清单、求解器、运行时和默认求解参数。测量时尽量避免同时运行其他重任务；需要比较负载波动下的结果时，可将清单拆成单谱，在两版之间交替运行，并分别汇总每张谱面的多次记录。

比较原始报告：

```powershell
python research/planner-performance/compare.py artifacts/perf-before.json artifacts/perf-after.json artifacts/perf-comparison.md
```

比较脚本先检查程序集指纹、谱面哈希与完整输出指纹，再计算每张谱面的耗时中位数。汇总提速是两版各谱面中位数之和的比值。分配量不等同于峰值内存；单谱与整库的提速也不能相互替代。

已保存的测量与适用范围见[第一版报告](../../reports/planner-performance/2026-10-08/README.md)和[第二版 Panopticon 对照](../../reports/planner-performance/2026-10-08-v2/README.md)。
