# 掌面与衔接阶段验证

本次冻结快照发现了行为回退，尚未通过回归验收。程序集 SHA-256：

```text
854C2B4C42B026C622C1CD1AD6997480304966408813D815DAE35C40FA9E2D59
```

完整测试为 1389/1389 通过，原始记录位于 `artifacts/human-planner-validation/tests/palm-final.trx`。以下实曲回放揭示了测试尚未覆盖的退步。

| 范围 | Normal 全 CP | Human 全 CP | Human 非 CP 音符 / Gap |
|---|---:|---:|---:|
| [Master 11+](master-11-plus/comparison.md) | 40/50 | 49/50 | 1 / 0 |
| [Expert 12+](expert-12-plus/comparison.md) | 135/139 | 137/139 | 6 / 0 |
| [固定压力样本](patterns/pattern-validation.md) | 12/30 | 24/30 | 29 / 25 |

压力测试沿用[上一阶段](../sweep-tail-890106/README.md)选定的同 30 张谱、37 段旋转和 107 段快速纵连。清单分析指纹仍为 `890106…`，回放求解器指纹为本页的 `854C…`。重新选样会更换一张谱，未将该谱加入本轮验证。

[逐规划器版本比较](revision-comparison.json)记录了以下退步：

- Human《Love You》198→197 CP。
- 《NULCTRL》两种规划器均从 523 降至 519 CP；Human《B.B.K.K.B.K.K.》676→674 CP。
- Normal《Last Brave ～ Go to zero》虽增加 3 个 CP，但达成率下降、Gap 20→21，其中 42.955–43.182 秒旋转从 7/9 降至 4/9 CP。

[六谱逐音符对照](spot-comparison.json)还保留了净成绩掩盖的局部回退：封焔 Master 两种规划器出现新的键 5 漏判；Normal 的 Xaleid Re:Master 出现新的 Hold/Tap 漏判和一枚 Slide 降级。具体音符编号、时间、前后判定及轨迹证据路径均在 JSON 中。

Human 的目标配置改善已在完整回放确认：Panopticon Master 899→900 CP、Re:Master 861→888 CP；Xaleid Re:Master 2189→2200 CP，开头扫键接 Hold、中心 C 与 WiFi 均命中，未新增漏判或丢失 CP。中心 C 没有独立分手，由正在执行 Slide 的掌面顺带触发。HERA 保持 1031/1031，End Time 保持 949/949 CP；封焔 96.9 秒双押仍为 CP。

本目录保存冻结阶段的统计与证据索引；完整手部轨迹保留在所引用的本机 `artifacts/` 路径中。
