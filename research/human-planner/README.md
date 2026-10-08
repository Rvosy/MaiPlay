# 配置压力样本

按显示等级扫描完整曲库、排序错误并保存统计，见[等级扫描](level-scan.md)。

`PatternValidation` 从本地曲库选取旋转和快速纵连的压力样本，再用同一批谱面比较旧、新求解器。选取依据为 `PatternAnalyzer` 的实际段落、等级、方向、速度和持续长度，最多 30 张。它是研究工具，不参与播放器运行。

构建输出应放在 `artifacts/`，避免覆盖正在运行的播放器。指定 `CoreAssembly` 可以核验已经冻结的程序集；不指定时引用当前 `MaiPlay.Core` 项目。

```powershell
dotnet build tests/MaiPlay.GameplayHarness/MaiPlay.GameplayHarness.csproj -c Release --artifacts-path artifacts/pattern-frozen
$patternCoreAssembly = (Resolve-Path artifacts/pattern-frozen/bin/MaiPlay.GameplayHarness/release/MaiPlay.Core.dll).Path
dotnet build research/human-planner/PatternValidation.csproj -c Release --artifacts-path artifacts/pattern-probe "-p:CoreAssembly=$patternCoreAssembly"
dotnet artifacts/pattern-probe/bin/PatternValidation/release/PatternValidation.dll select .reference/charts/game-extracted artifacts/pattern-selection.json
dotnet artifacts/pattern-probe/bin/PatternValidation/release/PatternValidation.dll evaluate .reference/charts/game-extracted artifacts/pattern-selection.json reports/human-planner
```

`select` 只解析谱面和识别配置，不运行求解器。清单记录谱面 SHA-256、音符编号、配置范围及选取原因。`evaluate` 先检查谱面 SHA-256，再分别运行 `NormalHandPlanner`、`HumanPlanner`，完整回放仍由各求解器使用的 `PlaybackSession` 核验。

报告分别统计整谱成绩、完整旋转段、快纵连的分配覆盖及相邻同手比例。漏掉的音符对不计入同手比例的分母，单独列出缺口。统计定义和本次参数随报告保存；压力样本的成绩不能推算整库完成率。
