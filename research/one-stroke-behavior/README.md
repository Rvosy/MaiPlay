# 独立 Slide 的连续划动与接力

这个工具记录真实谱面的一笔画责任手、接缝离屏时长、成绩和运动统计。它读取本地曲库，报告不包含音频或完整谱面。

```powershell
dotnet build research/one-stroke-behavior/OneStrokeBehavior.csproj -c Release --artifacts-path artifacts/one-stroke-probe
dotnet artifacts/one-stroke-probe/bin/OneStrokeBehavior/release/OneStrokeBehavior.dll .reference/charts/game-extracted/21_BUDDiES/011634_青春コンプレックス artifacts/one-stroke-probe/seishun.json
dotnet artifacts/one-stroke-probe/bin/OneStrokeBehavior/release/OneStrokeBehavior.dll --tail-variants
dotnet artifacts/one-stroke-probe/bin/OneStrokeBehavior/release/OneStrokeBehavior.dll --reference-relay
dotnet artifacts/one-stroke-probe/bin/OneStrokeBehavior/release/OneStrokeBehavior.dll --timing-feedback artifacts/one-stroke-probe
```

也可以传入 Simai 文本作为第一个参数；后续参数依次为报告路径、片段开始秒数和结束秒数。使用 `-p:CoreAssembly=<程序集绝对路径>` 构建，可以对照冻结的 Core。`--tail-variants` 只改变链尾后的 Tap 键位，输出八种规划结果。

## 基线证据

`baseline-seishun.json` 来自提交 `5fc13853a00845a96886a16871e716a1a58d79ff` 的 Core。构建结束时 `git diff --name-only -- src/MaiPlay.Core` 没有输出。谱面为《青春コンプレックス》DX MASTER，定数 11.8，整曲 823 个音符。

134.210526 秒的星头开始一组 11 条独立 Slide，轨道按 `1>5`、`5<1` 交替。第一条从 134.526316 秒起滑，最后一条结束于 138 秒。相邻轨道的名义尾点、入口和起滑时间相接。

| 基线 | 全曲 CP | 片段责任手 | 相邻接缝 |
| --- | --- | --- | --- |
| Normal | 823/823 | RRRRRRRRRRR | 10 次断触，36.18～40.13 ms |
| Human | 823/823 | RLRLRLRLRLR | 10 次换手 |

报告中的 `contactGapSeconds` 统计接缝前后各 80 ms 内，同一责任手没有 Touch 接触的总时长。责任手改变时返回 null，不把换手算成同手断触。`movement` 是全曲统计，不能当作这个片段的峰值。

逐谱摘要的 `contactGapSeconds` 是所有同手接缝的合计；Normal 的 10 处断触合计为 0.381579 秒。整段都在换手时，这个合计为 0，须同时查看 `handSwitches`，不能据此宣称连续接触。

当前实现的[对照摘要](../../reports/one-stroke-behavior/comparison.json)记录谱面和 Core 的 SHA-256。Human 保持整曲 823/823 CP，片段变为 `RRRRRRRRRRR`，10 个接缝均无断触。全曲通过当前速度约束，双手同时接触时的平面掌面圆碰撞为 0；这个检测不涵盖离屏高度和前臂。Normal 的判定成绩相同，仍有 10 处断触。

最终证据直接引用全库评估使用的 `artifacts/one-stroke-optimized-build/bin/MaiPlay.Core/release/MaiPlay.Core.dll`，程序集 SHA-256 为 `AA0978A8E0EE46F1F90945A306EB93B26C5D9EDD67300FE69B7C77916BB072B4`。源码聚合 SHA-256 为 `20DB547D8F5D12E5846BD4075137932ED9AF49BCEA04F04097581C5198D9633C`，计算范围和方法随摘要记录，详细文件清单在 `artifacts/one-stroke-optimized-build/source-manifest.json`。不同构建输出目录可能生成不同的程序集哈希，源码一致性应按同一聚合方法核对。

`--visual-tracks <输出路径> tails` 导出 Human 方案的原始关键帧，末尾的 `tails` 可省略。真实歌曲取 134.1～138.3 秒，并保留片段两端最近的原始关键帧，供回放按原有插值衔接；接触部位直接来自规划结果。`artifacts/one-stroke-optimized-evidence/visual-tracks.json` 合并了基线 Human、当前 Human 和 Tap 2/7 对照，记录各自实际加载的 Core SHA，不使用示意轨迹。

## 后续 Tap 对照

对照谱面为：

```text
(150){4}4-7[4:1]/8h[#0.3],7<5[4:1],{#0.43},2,E
```

解析时设置 `first: 1`。第一条 Slide 为 1.4～1.8 秒的 `4→7`，第二条为 1.8～2.2 秒的 `7→5`，最后 Tap 在 2.23 秒。另一版本只将最后的 `2` 换成 `7`。

`--reference-relay` 用独立编写的 `HandTrack` 比较 RR 连续划动和 RL 接力。它没有调用规划器或接触编译器来安排动作；两种方案的最后一条均在轨道 90% 处释放。接力方案的右手在第一条 90% 处释放，并在 40 ms 内向右退让 0.6 个判定半径。输出保存在 `reference-relay.jsonl`。

| 最后 Tap | Slide 分工 | 实际回放 CP | 速度超限次数 | 全段峰速 |
| --- | --- | --- | --- | --- |
| 2 | RR | 6/6 | 0 | 6.77 m/s |
| 2 | RL | 6/6 | 0 | 3.53 m/s |
| 7 | RR | 6/6 | 0 | 2.02 m/s |
| 7 | RL | 6/6 | 0 | 4.63 m/s |

这些参考动作说明两种分工都能过判，且后续键位会改变动作收益。数值沿用项目默认的估算尺寸和速度上限；它们没有经过真人标定，也不构成完整身体无碰撞证明。参考动作不宣称是最优解，选择标准仍由规划器的行为回归验证。

最终规划器在 Tap 2 版本选择 RL 接力，在 Tap 7 版本选择 RR 连续。两者均为 6/6 CP，速度超限和接触平面碰撞次数均为 0。接力有实际释放与退让；回归测试检查交接前后各 60 ms 的双手位置，不能只改责任手标签。

## 时机反馈后的连续性

以下两例都从 `(190){4}1>5[4:1],5<1[4:1],1>5[4:1],E`、`first: 1` 出发，额外加入一个普通 Tap。输入按时间排序并重新编号。

| 额外 Tap | 最终三条轨道时移 | 责任手 | 独立回放 | 链内离屏总时长 |
| --- | --- | --- | --- | --- |
| 1.35 秒，键 1 | 全部延后 120 ms | LLL | 7/7 CP | 0 |
| 1.5 秒，键 2 | 全部提前 94.1667 ms | LLL | 7/7 CP | 0 |

两个方案均通过当前速度和接触平面碰撞约束。后一例的偏移来自判定反馈，不在固定的 60 ms 候选间隔上；后继按前驱的实际结束时间继续，避免修正第一条后在连接点重新抬手。`--timing-feedback` 输出小型诊断；提供目录参数时将完整轨迹写入该目录，报告只保留摘要。

`tests/MaiPlay.Tests/OneStrokeBehaviorTests.cs` 的 17 个用例已通过。测试检查最终轨迹和独立输入回放：无接力收益的 11 条链保持连续接触，覆盖镜像和绝对时间平移；Tap 对照及其镜像检查继续与接力选择，以及交接附近的退让；两种时移用例检查反馈后仍保持连续。负例覆盖名义时间偏移、不同端点、Wifi、分叉、汇合和单音符内部连锁，避免把它们误连成独立 Slide 链。报告的换手、断触和端点诊断也与实际轨迹核对。

## 最终全库验证

同一冻结版本已完成 MASTER 1512 张、Re:MASTER 149 张的全库回放。全 CP 且模型内已解分别为 1086、90 张；可执行分别为 1122、95 张。两批均无运行异常、速度超限或接触平面碰撞记录，仍有 390、54 张存在规划缺口，另有 36、5 张没有缺口但未全 CP。同手断触接缝仍有 100、14 处，接力接缝为 2263、294 处；接力本身不算退步。这些接缝统计只覆盖名义路径中前驱和后继唯一的 Slide 衔接。

[精简汇总](../../reports/one-stroke-behavior/library-summary.json)保存分级统计、历史成绩参考和运行来源，[详细结果](../../reports/one-stroke-behavior/library/README.md)附未解谱面索引。历史保存成绩的全 CP 数为 1367、125 张，当前成绩存在退步；历史运行缺少冻结源码清单及动作诊断，因此不能据此断言旧版轨迹更合理。MASTER 的 305 张已完成前缀与后续 604、603 张互斥子批次已经合并，全部 1512 个唯一谱面路径和原谱哈希通过核对。Re:MASTER 扫描同一 1512 个文件，其中 1363 个没有白谱；149 张包含本地文件中定数为 0.0 的 `[X]`。
