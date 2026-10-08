# 等级扫描

这套脚本枚举曲库中所有难度编号，再用冻结的 `HumanPlanner` 回放所选显示等级。默认范围为 14、14+。定数的小数部分达到 0.6 时归入加号档，也接受显式的 `14+`。

需要 PowerShell 7、Python 3 和已构建的 GameplayHarness。所有命令从仓库根目录执行。运行前把构建输出放到独立的 `artifacts/` 目录，扫描期间不要覆盖这份程序集。

```powershell
dotnet build tests/MaiPlay.GameplayHarness/MaiPlay.GameplayHarness.csproj -c Release --artifacts-path artifacts/level-scan-build
$levelHarness = (Resolve-Path artifacts/level-scan-build/bin/MaiPlay.GameplayHarness/release/MaiPlay.GameplayHarness.dll).Path
$levelCore = Join-Path (Split-Path $levelHarness) MaiPlay.Core.dll
$levelCoreHash = (Get-FileHash -LiteralPath $levelCore).Hash
./research/human-planner/catalog-levels.ps1 -CoreAssembly $levelCore -ExpectedCoreHash $levelCoreHash -ChartRoot .reference/charts/game-extracted -OutputPath artifacts/level-scan/manifest.json
./research/human-planner/export-note-locations.ps1 -ManifestPath artifacts/level-scan/manifest.json -OutputRoot artifacts/level-scan
python research/human-planner/scan-levels.py --manifest artifacts/level-scan/manifest.json --harness $levelHarness --output artifacts/level-scan --workers 8 --report reports/human-planner/level-scan
```

`catalog-levels.ps1` 记录所有解析失败、入选谱面的路径与难度、源谱 SHA-256、Core SHA-256。存在解析失败时扫描器会退出，避免把遗漏当作完整范围。可用 `-Levels @('13+', '14', '14+')` 改变选取范围。

`export-note-locations.ps1` 使用同一份冻结解析器，导出编号、类型、起始与结束时间、按钮或触区位置。扫描器用它核对每张结果的音符数量、唯一编号、类型和原始时间，并为错误附上结构化位置。

`scan-levels.py` 默认同时启动 8 个独立进程，调用现有 `--plan-human`。它逐谱保存 `run.log`、原始 `results.json`、`hands.json` 和 `scan-record.json`，每完成一张就更新 `ranking.json`。单谱进程或校验失败会保留原因，其余谱面继续运行。扫描器返回 0 表示全部证据有效；非 CP 仍是有效结果。进程失败、缺失或不一致的证据会使扫描器返回 1，报告 `complete` 为 `false`。

已有结果的目录不能直接重新运行，避免覆盖证据。只需重新汇总时使用：

```powershell
python research/human-planner/scan-levels.py --manifest artifacts/level-scan/manifest.json --harness $levelHarness --output artifacts/level-scan --collect-only --report reports/human-planner/level-scan
```

`--collect-only` 检查已有源谱、Core、结果和轨迹的指纹，再从原始结果重算统计；它不启动求解器，也不改写逐谱证据。报告中的顶层 `seconds` 是本次运行或重聚合所花的时间，每张谱面的 `seconds` 保留原扫描进程用时。

排序与错误簇口径随报告生成。`reports/` 只保留统计、失败音符的位置与原因、证据路径和指纹。原始谱面位于清单指定的 `chartRoot`，整曲轨迹与原始回放结果保存在 `artifacts/`。`README.md` 给出各档汇总，`charts.md` 保留包括全 CP 谱面在内的完整排行，`one-error-charts.md` 列出只有一个非 CP 的谱面，`ranking.json` 保留可供逐项分析的错误与 Gap 明细。

完成两个冻结版本的同范围扫描后，可比较整谱成绩与逐音符判定：

```powershell
python research/human-planner/compare-levels.py --baseline artifacts/level-scan-base --candidate artifacts/level-scan-fixed --output reports/human-planner/level-scan-comparison
```

两个清单的谱面条目必须完全相同。对照会单独记录原 CP 变低判、原 CP 变 Miss、原低判变 Miss，以及整谱非 CP 净减少却仍有新丢 CP 的情况。完整逐谱和判定变化明细写入 `comparison.json`。完整对照还要求两次扫描的最终校验成功，并核对修复版位置文件的指纹、来源、音符类型与时间。

对照命令返回 0 表示证据完整且校验成功，**不表示没有退步**；退步由 `regressionReasons`、`lostCP`、`newMisses` 等字段单独呈现。扫描尚未结束时可加 `--partial`，只检查已有逐谱记录，返回 0 表示已完成子集的证据有效；此时汇总的分母是已完成子集，不能当作整批结果。
