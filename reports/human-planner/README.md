# HumanPlanner 验证记录

当前为修复过程中的检查点，尚未完成最终回归验收。修复版 `MaiPlay.Core` 程序集 SHA-256：

```text
86CBD39429A04A362B73A93CC0CCDBA6F6D8ADB641E394CAB4233AA04C7B29A3
```

该版本尚未运行完整的 189 张等级样本与固定 30 张压力样本。WiFi 双掌路径及防蹭方案也尚未完成最终验收，不能将下列历史成绩视为该修复版的验证结果。

| 阶段 | 冻结指纹前缀 | 已完成验证 | 状态 |
|---|---|---|---|
| [扫尾双押阶段](snapshots/sweep-tail-890106/README.md) | `890106AE` | 189 张等级样本、30 张压力样本、单谱复验；1098 项测试通过 | 保存该阶段成绩与剩余缺口 |
| [掌面与衔接阶段](snapshots/palm-854c/README.md) | `854C2B4C` | 同 189 张等级样本与 30 张压力样本、六谱双规划器逐音符对照；1389 项测试通过 | 实曲发现新增回退，未通过回归验收 |

854C 的回退包括 Love You、NULCTRL、B.B.K.K.B.K.K.，以及封焔、Xaleid、Last Brave 的局部降级或漏判。完整判定、音符编号与前后版本对照保存在该[阶段报告](snapshots/palm-854c/README.md)中；后续修复不改写这份冻结证据。

根目录的 [Master 11+](master-11-plus/comparison.md)、[Expert 12+](expert-12-plus/comparison.md)、[压力样本](patterns/pattern-validation.md)、[版本对照](revision-comparison.json)和[单谱记录](spot-summary.json)仍对应 `890106AE`。各 JSON 的程序集 SHA-256 标明实际执行版本，读取统计时应同时核对该字段。

[固定清单](patterns/pattern-selection.json)包含 30 张谱、37 段旋转和 107 段快速纵连，选样分析指纹为 `890106AE`。后续版本沿用相同清单比较，不将重新选样产生的不同谱面混入回归结果。压力样本与等级样本有重叠，不能相加作为独立谱面数。

[baseline-regression.json](baseline-regression.json)保留重构早期的全 Expert 基线检查，版本与上述阶段均不同。复验命令和选样工具见 [research/human-planner](../../research/human-planner/README.md)。模型中的速度与击打偏好尚未经过人体标定；回放成绩不等同于真人可打性验证。

本目录保存统计、元数据和指纹，不包含谱面正文或完整手部轨迹。原始轨迹与测试记录保留在各报告引用的本机 `artifacts/` 路径中。
