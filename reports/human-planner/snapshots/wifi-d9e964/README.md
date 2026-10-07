# WiFi 与扫键衔接阶段验证

本次冻结验证发现两处 Human Expert 回退，尚未通过回归验收。程序集 SHA-256：

```text
D9E96445550FE5A7B988F60178E525AB182DECBB2E67C6E816F9C6232371A672
```

Release 测试 1600/1600 通过。测试记录、报告指纹与冻结程序路径见 [verification.json](verification.json)。所有成绩均来自本页指纹对应的实际回放，不包含后续修复。

| 范围 | Normal 全 CP | Human 全 CP | Human 非 CP 音符 / Gap |
|---|---:|---:|---:|
| [Master 11+](master-11-plus/comparison.md) | 41/50 | 50/50 | 0 / 0 |
| [Expert 12+](expert-12-plus/comparison.md) | 136/139 | 137/139 | 2 / 0 |
| [固定压力样本](patterns/pattern-validation.md) | 12/30 | 26/30 | 18 / 17 |

压力样本沿用 `890106AE` 选定的同 30 张谱，包含 37 段旋转与 107 段快速纵连，[清单](patterns/pattern-selection.json) SHA-256 为 `3085369805E8E92422C3784AB233DAB23E2E0420EDDCD08C7C7DDE9BF79CB0E7`。Human 完整旋转为 36/37，快速纵连为 107/107，相邻同手为 30/685 对（4.38%）。压力样本与等级样本有重叠，不能相加作为独立谱面数。

## 新发现的回退

[等级回退证据](level-regressions.json)记录了两个已经分配动作、仍被完整回放判为 Miss 的 EX Tap：

- VERTeX (rintaro soma deconstructed remix) Expert：#381，102.341772 秒，Left Rotation，618→617 CP。
- Trrricksters!! Expert：#713，135.870968 秒，Left Strike，775→774 CP。

两首在 `890106AE` 和 `854C2B4C` 中均全 CP，在补跑的 `DF33BC7C` 中已同样漏判。零 Gap 不能替代实际判定验证。该回退不是本次 WiFi 冻结后才出现。

Normal 的新回退也保留在对照中：Panopticon Re:Master 相对 `DF33BC7C` 的 #855 WiFi 星头未分配，后一个同键 Tap #857 的输入先被 #855 消费，造成 LateGood 与 Miss；Xaleid Re:Master 的 #1159、#1161–#1168 是同一时间的 Touch 组，由 CP 降至 LatePerfect2nd，没有新增 Miss。压力样本中 WiPE OUT MEMORIES 相对 `854C2B4C` 从 1103 降至 1102 CP、Gap 2→3；Last Brave 相对 `890106AE` 的局部退步仍存在。

完整版本对照见 [890106 对照](revision-vs-890106.json)、[854C 对照](revision-vs-854c.json)；定点逐音符对照见 [890106](spots-vs-890106.json)、[854C](spots-vs-854c.json)、[DF33](spots-vs-df33.json)。

## 定点回放

[18 组索引](run-index.json)与[审计记录](spot-audit.json)保存 9 张谱面、两个规划器的文件指纹、逐音符回退检查及速度统计。

| 谱面 | Normal CP / Gap | Human CP / Gap |
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

六张难谱的 Human 相对三个旧版本均没有新增丢 CP 或 Miss，三个回归谱相对 DF33 也无回退。封焔 #830、#832 的提前低判与 #1076 的尾键缺口已恢复。

Panopticon Master 的 35.8–37.2 秒目标段为 13/13 CP；Re:Master 的 76.15–77.25 秒为 11/11 CP，108.8–110.7 秒为 12/16 CP，既有 #754、#758、#762、#766 仍未完成。不能把前一段的完成推广到全部扫键接 Hold 配置。

[怒槌单谱复验](rage-comparison.json)中，Human 为 1187/1188 CP、零 Gap，剩余 #503 Slide 为 LateGreat；Normal 为 1174/1188、14 Gap。两个规划器的末尾 8 个音符均为 CP，相对 `854C2B4C` 没有新增丢 CP 或 Miss。

## 验证范围与剩余风险

189 张等级样本与 9 张 Human 定点谱均未发现模型速度超限。Normal Xaleid 仍有 29 段超限，峰值 7380.148 mm/s，高于模型上限 7056.72 mm/s；`854C2B4C` 和 `DF33BC7C` 已存在相同 29 段，详见定点审计。

本轮没有重新扫描整库。速度上限、手掌尺寸与动作偏好尚未经过人体标定，回放得分不能替代真人可打性验证。此目录保存统计与证据索引；谱面正文和完整手部轨迹保留在所引用的本机 `artifacts/` 中。
