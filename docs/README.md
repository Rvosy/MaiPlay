# 项目文档

## 行为契约

- [游玩机制与算法输入](specs/gameplay.md)：基础判定、输入契约与开发版手顺求解行为；0.2.0 的验收范围见本版记录。
- [播放界面与二级设置](specs/player-settings.md)：设置按钮、主画面布局和设置生效行为。
- [DX 达成率与 Rating](specs/achievement.md)：减分计分、单曲 Rating 和批量验证的 B50 分组规则。

## 架构决策

- [ADR-0001：以固定上游实现建立完整游玩核心](adr/0001-upstream-gameplay.md)。
- [ADR-0002：分离配置候选与动作搜索，保留基线对照](adr/0002-pattern-human-planner.md)。

## 使用、兼容与证据

- [输入回放](INPUT-REPLAY.md)、[0.2.0 验收记录](GAMEPLAY-VALIDATION.md)。
- [配置与动作规划](HUMAN-PLANNER.md)：试验求解器、运动模型范围与分级对照报告。
- [程序使用](../README.md)。
- [谱面与时序](CHART-COMPATIBILITY.md)、[皮肤](SKIN-COMPATIBILITY.md)、[音频](AUDIO.md)：现有版本的兼容行为及限制。
- [官方谱面转换](OFFICIAL-CHARTS.md)：转换工具、版本分类与独立的整库验收记录。
- [水鱼成绩上传](DIVING-FISH.md)：从算法评估报告生成上传包，使用个人 Import-Token 上传并读回核对。
- [既有验收记录](VALIDATION.md)：对应记录中的版本，不代表下一阶段已完成。
- [上游研究记录](../research/upstream-gameplay.md)：固定源码、调查证据、差异与后续核验入口。

实现发生变化时，同步更新对应契约与兼容说明；历史结果留在对应版本的验收记录中。新文件进入后续发布，既有发布目录保持其原版本内容。
