# HumanPlanner 验证记录

本轮 HumanPlanner 回归验证通过：相对修复前的 D9 基准，约定样本未发现新增 Human 回退。高难谱仍有缺口。`MaiPlay.Core` 程序集 SHA-256：

```text
0060D326F8A6A89ACB8932F983CDE3372D4ACF74D4EDC489408EDC71F073F6C2
```

本版已通过 Release 1634 项测试，重新运行同一批 189 张等级样本与固定 30 张压力样本的两个规划器。Human 等级样本 189/189 全 CP，压力样本 26/30；九张 Human 定点、怒槌及同冻结版本的 Latent 回放均完成核验。九张 Normal 定点明确沿用 C97 证据，未标为本版重跑。完整成绩、已知退步与来源见[本版记录](snapshots/feedback-candidates-0060d3/README.md)。

| 阶段 | 冻结指纹前缀 | 已完成验证 | 状态 |
|---|---|---|---|
| [扫尾双押阶段](snapshots/sweep-tail-890106/README.md) | `890106AE` | 189 张等级样本、30 张压力样本、单谱复验；1098 项测试通过 | 保存该阶段成绩与剩余缺口 |
| [掌面与衔接阶段](snapshots/palm-854c/README.md) | `854C2B4C` | 同 189 张等级样本与 30 张压力样本、六谱双规划器逐音符对照；1389 项测试通过 | 实曲发现新增回退，未通过回归验收 |
| [WiFi 与扫键衔接阶段](snapshots/wifi-d9e964/README.md) | `D9E96445` | 同 189 张等级样本与 30 张压力样本、18 组定点及怒槌复验；1600 项测试通过 | 两处 Human Expert 漏判，Normal 高难存在新增及既有退步 |
| [持续按住遮蔽修复阶段](snapshots/sweep-mask-c97fa2/README.md) | `C97FA203` | 同 189 张等级样本与 30 张压力样本、18 组定点及怒槌复验；1632 项测试通过 | 等级样本全 CP；Latent Kingdom 出现新回退 |
| [反馈候选独立比较](snapshots/feedback-candidates-0060d3/README.md) | `0060D326` | 同 189 张等级样本与 30 张压力样本、11 张 Human 单谱核验；1634 项测试通过 | 相对 D9 无新增 Human 回退；保留高难缺口与 Normal 风险 |

854C 的回退包括 Love You、NULCTRL、B.B.K.K.B.K.K.，以及封焔、Xaleid、Last Brave 的局部降级或漏判。完整判定、音符编号与前后版本对照保存在该[阶段报告](snapshots/palm-854c/README.md)中；后续修复不改写这份冻结证据。

根目录的 [Master 11+](master-11-plus/comparison.md)、[Expert 12+](expert-12-plus/comparison.md)、[压力样本](patterns/pattern-validation.md)、[版本对照](revision-comparison.json)和[单谱记录](spot-summary.json)仍对应 `890106AE`。各 JSON 的程序集 SHA-256 标明实际执行版本，读取统计时应同时核对该字段。

[固定清单](patterns/pattern-selection.json)包含 30 张谱、37 段旋转和 107 段快速纵连，选样分析指纹为 `890106AE`。后续版本沿用相同清单比较，不将重新选样产生的不同谱面混入回归结果。压力样本与等级样本有重叠，不能相加作为独立谱面数。

[baseline-regression.json](baseline-regression.json)保留重构早期的全 Expert 基线检查，版本与上述阶段均不同。复验命令和选样工具见 [research/human-planner](../../research/human-planner/README.md)。模型中的速度与击打偏好尚未经过人体标定；回放成绩不等同于真人可打性验证。

本目录保存统计、元数据和指纹，不包含谱面正文或完整手部轨迹。原始轨迹与测试记录保留在各报告引用的本机 `artifacts/` 路径中。
