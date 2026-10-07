# HumanPlanner 扫尾双押阶段快照

2026-10-08 本轮等级比较、配置压力测试和单谱复验均使用同一份冻结 `MaiPlay.Core` 程序集，SHA-256 为：

```text
890106AE0D1CD28C9874FDCBE6663A4C09EC8DFD4463DE608C31DEA582726824
```

该冻结快照的完整测试为 **1098/1098 通过**，本机原始记录位于 `artifacts/human-planner-validation/tests/sweep-tail-final.trx`。两个规划器都包含扫键末尾接双押的通用修复；下表比较的是同一版本的 `NormalHandPlanner` 与 `HumanPlanner`。

| 验证范围 | 谱面数 | Normal 全 CP | Human 全 CP | 非 CP 音符：Normal → Human | Gap：Normal → Human |
|---|---:|---:|---:|---:|---:|
| [Master 11+](master-11-plus/comparison.md) | 50 | 40 | 50 | 12 → 0 | 11 → 0 |
| [Expert 12+](expert-12-plus/comparison.md) | 139 | 133 | 137 | 23 → 5 | 23 → 5 |
| [旋转与快速纵连压力样本](patterns/pattern-validation.md) | 30 | 12 | 20 | 284 → 74 | 272 → 71 |

两个等级批次均无成绩退步、无异常。Human 的 Expert 12+ 仍有《封焔の135秒》1 个非 CP 和《ZEUS》4 个非 CP。压力样本可能与等级批次重叠，不能将三行相加作为独立谱面数。

压力样本的完整 Rotation 段数从 33/37 增至 35/37，完整快速 Jack 从 106/107 增至 107/107。快速 Jack 相邻同手比例从 40/684（5.85%）降至 34/685（4.96%）；未分配音符对从 1 降至 0。《HERA》1031/1031 全 CP，6 段快速纵连的 133 对相邻音符全部交替用手。

压力样本仍保留 1 项局部回退：《Latent Kingdom》的纵连同手音符对由 Normal 的 3/11 增至 Human 的 4/11，其整谱 CP 从 1341 增至 1355、Gap 从 28 降至 13。Human 在《Last Brave ～ Go to zero》42.955–43.182 秒的旋转为 6/9 CP，在《Latent Kingdom》69.888–70.448 秒的旋转为 7/18 CP，尚未完整解决。压力样本没有整谱成绩回退或运行异常。

与上一份 `CBB26E…` 冻结版本的[逐规划器比较](revision-comparison.json)显示：两个等级的整谱成绩和 Gap 均不变；压力样本中 Normal 有 9 张改善，全 CP 从 10 增至 12，Human 有 2 张改善，CP 从 26475 增至 26477、Gap 从 74 降至 71。两个规划器均无版本退步，144 段配置的 CP、纵连分配及同手音符对数均未变化。

[单谱复验](spot-summary.json)补充了具体扫尾段的判定和实际按钮输入：

| 谱面 | Normal CP / Gap | Human CP / Gap |
|---|---:|---:|
| 封焔の135秒 Expert | 608/609 · 1 | 608/609 · 1 |
| 封焔の135秒 Master | 1080/1092 · 11 | 1082/1092 · 5 |
| End Time Master | 947/949 · 0 | 949/949 · 0 |

封焔 Master 相对上一冻结版本分别增加 6、5 个 CP，没有新增降级音符。96.9 秒的 #759/#760 均为 Break Tap：Normal 右手扫到键 1、左手打键 5；Human 左手接扫到键 1、右手打键 5，两音符均为 CP。Expert 剩余漏判是 60.525 秒的 #269。End Time 的结果与上一冻结版本一致。

[选取清单](patterns/pattern-selection.md)覆盖 Expert 12+ 起和 Master 11+ 起，按 `PatternAnalyzer` 的实际配置、方向、速度和持续长度选出 30 张谱面，涉及 29 首曲目。谱面 SHA-256、选取理由与配置音符编号保存在[清单 JSON](patterns/pattern-selection.json)中。压力样本有意包含极端配置，其通过率不代表整库水平。

各批次最多同时处理 4 张谱面，三个批次并行运行，期间另有单谱复验。Master 11+、Expert 12+ 和压力批次墙钟时间分别为 149.5、326.8、327.7 秒；这些耗时不用于比较版本性能。模型参数、完整成绩与手序指标分别保存在各报告的 JSON 中。判定环半径为 235.224 mm，默认速度上限约 7056.72 mm/s，偏好速度 1300 mm/s，偏好单手击打间隔 0.125 秒；这些参数尚未经过人体标定。

[baseline-regression.json](../../baseline-regression.json) 是本次重构早期提取共享轨迹生成代码时的全 Expert 基线检查：1511 张有效谱面中 1494 张全 CP，逐谱判定和 Gap 时段与历史快照一致。该记录使用自身列出的早期程序集与历史 SHA-256，范围和版本均不同于本页的冻结比较。

复验命令与选样工具见 [research/human-planner](../../../../research/human-planner/README.md)。本目录仅保存统计、元数据和指纹，不包含谱面正文或手部轨迹。
