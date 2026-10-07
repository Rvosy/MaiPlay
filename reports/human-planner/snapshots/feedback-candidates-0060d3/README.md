# HumanPlanner 回归验证

本轮 HumanPlanner 回归验证通过：相对修复前的 `D9E96445`，约定样本未发现新增 Human 回退，两处 Expert EX Tap 漏判已恢复。压力样本和部分高难定点仍有缺口。冻结程序集 SHA-256：

```text
0060D326F8A6A89ACB8932F983CDE3372D4ACF74D4EDC489408EDC71F073F6C2
```

Release 测试 1634/1634 通过，零失败、零跳过。测试记录、冻结程序、报告与证据指纹见 [verification.json](verification.json)。

## 等级与固定压力样本

以下批次的 Normal 和 Human 均使用本页程序集重新运行。

| 范围 | Normal 全 CP | Human 全 CP | Human 非 CP 音符 / Gap |
|---|---:|---:|---:|
| [Master 11+](master-11-plus/comparison.md) | 41/50 | 50/50 | 0 / 0 |
| [Expert 12+](expert-12-plus/comparison.md) | 136/139 | 139/139 | 0 / 0 |
| [固定压力样本](patterns/pattern-validation.md) | 12/30 | 26/30 | 18 / 17 |

189 张等级样本共 115268 个音符，Human 全部 CP、零 Gap。VERTeX remix #381 与 Trrricksters!! #713 已恢复，见[逐音符复验](level-regression-resolution.json)。

压力样本沿用 `890106AE` 选定的[同 30 张谱](patterns/pattern-selection.json)，清单 SHA-256 为 `3085369805E8E92422C3784AB233DAB23E2E0420EDDCD08C7C7DDE9BF79CB0E7`。Human 完整旋转为 36/37，快速纵连为 107/107，相邻同手为 30/685 对（4.38%）；全部 30 张整谱成绩与 144 个配置段指标均与 D9 相同。压力样本与等级样本有重叠，不能相加作为独立谱面数。

[D9 对照](revision-vs-d9e964.json)中，只有上述两张 Expert 的 Human 成绩改善；Normal 等级与压力批次没有变化。[C97 对照](revision-vs-c97fa2.json)保留了失败阶段到本版的恢复情况。

## 定点回放与来源

九张 Human 定点谱和怒槌使用本版重新运行；Latent Kingdom 核验并复用同一冻结版本已经生成的实际回放。11 张 Human 单谱的完整逐音符结果均与 D9 一致。

九张 Normal 定点沿用 C97 的实际回放，本轮未重跑；索引保留原始路径、Core 指纹和文件 SHA，不将其标为本版结果。[主索引](run-index.json)、[Human 索引](human-run-index.json)与[审计](spot-audit.json)明确区分这两类来源。

| 谱面 | Normal CP / Gap（沿用 C97） | Human CP / Gap（0060） |
|---|---:|---:|
| Panopticon Master | 886/900 · 14 | 900/900 · 0 |
| Panopticon Re:Master | 878/909 · 27 | 888/909 · 21 |
| 封焔の135秒 Master | 1081/1092 · 11 | 1092/1092 · 0 |
| End Time Master | 947/949 · 0 | 949/949 · 0 |
| Xaleid◆scopiX Re:Master | 2133/2222 · 81 | 2201/2222 · 17 |
| HERA Master | 1014/1031 · 17 | 1031/1031 · 0 |
| Love You Master | 198/198 · 0 | 198/198 · 0 |
| NULCTRL Expert | 523/523 · 0 | 523/523 · 0 |
| B.B.K.K.B.K.K. Expert | 676/676 · 0 | 676/676 · 0 |

六张难谱的 Human 逐音符对照见 [D9](spots-vs-d9e964.json)和 [C97](spots-vs-c97fa2.json)，三个回归谱见 [D9](regression-vs-d9e964.json)和 [C97](regression-vs-c97fa2.json)。

Panopticon Master 的 35.8–37.2 秒目标段为 13/13 CP；Re:Master 的 76.15–77.25 秒为 11/11，108.8–110.7 秒为 12/16，既有 #754、#758、#762、#766 仍未完成。封焔 #830、#832 的提前低判与 #1076 的尾键缺口保持修复。

[两个补充单谱](extra-human-run-index.json)及其[审计](extra-human-audit.json)保留了以下结果：

- Latent Kingdom：1363/1371 CP、7 Gap，与 D9 逐音符一致。相对失败的 C97，#565、#1075、#1333 恢复 CP，#572 回到 D9 原有的 Miss；原 #1356 Slide 仍为 LateGood。
- 怒槌：1187/1188 CP、零 Gap，末尾 8 个音符均为 CP；#503 Slide 仍为 LateGreat。

补点逐音符变化见 [D9 对照](extra-human-vs-d9e964.json)与 [C97 对照](extra-human-vs-c97fa2.json)。C97 的局部得失没有因净 CP 恢复而从报告中删除。

## 剩余风险

本轮没有解决 Normal 在 [D9 阶段](../wifi-d9e964/README.md)记录的 Panopticon Re:Master 星头与后续同键 Tap、Xaleid Touch 组、WiPE OUT MEMORIES 和 Last Brave 退步。沿用的 C97 Normal Xaleid 还包含 29 段既有速度超限，峰值 7380.148 mm/s；相关轨迹与来源保留在定点审计中。

189 张等级样本与 11 张 Human 单谱未发现模型速度超限。未执行新的整库扫描；速度上限、手掌尺寸与动作偏好尚未经过人体标定，回放成绩不能替代真人可打性验证。

本目录保存本版统计与证据索引；[D9](../wifi-d9e964/README.md)和 [C97](../sweep-mask-c97fa2/README.md)失败阶段保持原样。谱面正文与完整手部轨迹保留在引用的本机 `artifacts/` 路径中。
