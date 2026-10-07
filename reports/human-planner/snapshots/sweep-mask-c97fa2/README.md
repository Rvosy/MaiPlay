# 持续按住遮蔽修复阶段验证

本阶段修复了两处 Expert EX Tap 漏判，但在 Latent Kingdom Master 引入新的退步，尚未通过回归验收。冻结程序集 SHA-256：

```text
C97FA203E4748CA41F87E7A341838B733DAB1BBC2CDBD5BCCF5C567D9133A056
```

Release 测试 1632/1632 通过。以下批次均使用本页程序集实际重跑；测试、固定清单与报告指纹见 [verification.json](verification.json)。

| 范围 | Normal 全 CP | Human 全 CP | Human 非 CP 音符 / Gap |
|---|---:|---:|---:|
| [Master 11+](master-11-plus/comparison.md) | 41/50 | 50/50 | 0 / 0 |
| [Expert 12+](expert-12-plus/comparison.md) | 136/139 | 139/139 | 0 / 0 |
| [固定压力样本](patterns/pattern-validation.md) | 12/30 | 26/30 | 20 / 17 |

189 张等级样本共 115268 个音符，Human 全部 CP、零 Gap；VERTeX remix #381 与 Trrricksters!! #713 已恢复，见[逐音符复验](level-regression-resolution.json)。

压力样本继续使用同一份 [30 张清单](patterns/pattern-selection.json)，包含 37 段旋转和 107 段快速纵连。Human 完整旋转仍为 36/37，快速纵连为 107/107，相邻同手为 30/685 对（4.38%）。压力样本与等级样本存在重叠，不能相加作为独立谱面数。

## Latent Kingdom 新回退

[逐音符证据](latent-regression.json)显示，Human 相对 D9 从 1363 降至 1361/1371 CP，达成率 100.556858%→100.398804%，Gap 仍为 7：

- #565，70.223881 秒 Tap：Perfect→Miss。
- #572，70.422886 秒 Tap：Miss→Perfect。
- #1075，130.646766 秒 Tap：Perfect→FastGood，提前 134.266 ms。
- #1333，158.208955 秒 Break Tap：Perfect→FastGood，提前 108.955 ms。

#565 与 #572 位于同一个已选旋转段，二者交换结果后，该段 CP 数保持不变。因此，配置段计数相同不能替代逐音符回退检查。完整版本比较见 [D9 对照](revision-vs-d9e964.json)、[890106 对照](revision-vs-890106.json)和 [854C 对照](revision-vs-854c.json)。

## 定点与保留的风险

[18 组定点索引](run-index.json)与[审计](spot-audit.json)确认，9 张谱面、两个入口相对 D9 均无判定等级变化。9 张 Normal 的逐音符结果、动作、Gap 和轨迹文件均与 D9 完全一致。Human 对指定旧版也没有新增丢 CP 或 Miss，详见 [D9](spots-vs-d9e964.json)、[890106](spots-vs-890106.json)、[854C](spots-vs-854c.json)、[DF33](spots-vs-df33.json) 对照。

Panopticon Master 保持 900/900 CP，目标段 13/13；Re:Master 保持 888/909，两个目标段分别为 11/11 和 12/16，既有 #754、#758、#762、#766 仍未完成。封焔 Master 保持 1092/1092。其他定点成绩及 Normal 残留退步与 [D9 记录](../wifi-d9e964/README.md)相同，没有因本次修复而消失。

[怒槌复验](rage-comparison.json)保持 Human 1187/1188、零 Gap，Normal 1174/1188、14 Gap；两者末尾 8 个音符均为 CP，Human 仍有 #503 Slide LateGreat。

189 张等级样本与 9 张 Human 定点谱均未发现模型速度超限。Normal Xaleid 既有 29 段超限保持不变，峰值 7380.148 mm/s。未运行新的整库扫描；速度上限、手掌尺寸与动作偏好尚未经过人体标定。本目录保留失败阶段证据，不代表后续修复结果。
