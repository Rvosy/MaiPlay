# 掌面与扫键修复检查点

2026-10-08。此检查点包含连续扫键、扫键接 Hold、掌面覆盖与防蹭反馈修复。Wi-Fi 双手横向并拢的接触形状仍待检查；此前的完整批次保留在相邻快照中，不代表此版本已完成整库验收。

独立求解器构建的 Core SHA-256 为 `86CBD39429A04A362B73A93CC0CCDBA6F6D8ADB641E394CAB4233AA04C7B29A3`，完整测试 **1431/1431** 通过。原始记录：`artifacts/human-planner-validation/tests/palm-repaired/palm-repaired.trx`。

合入工作区性能优化后的预览构建 Core SHA-256 为 `DF33BC7C13C8A68B9E452676DB76600D0357CAD1AF76F61FAFD07F47D132280E`，完整测试 **1438/1438** 通过。原始记录：`artifacts/human-planner-validation/tests/workspace-repaired/workspace-repaired.trx`。

预览构建实测《怒槌》Master 14.8 为 **1187/1188 CP，100.9657%**。结尾四组双押扫的八颗音符全部 CP；剩余非 CP 是 64.2 秒的 #503 Slide（LateGreat）。结果与轨迹保存在 `artifacts/human-planner-validation/checkpoint-779-human/`。

修复没有按歌曲、编号或时间分支。扫键复用普通按钮的微小时移候选，并继续检查原判定窗口和速度；扫入 Hold 后沿弧落到键心，避免占住入口相邻键；掌面导致 TouchHold 头判超时时按真实阻挡动作尝试小手；Slide 防蹭同时核验已有范围内的提前离开方案，避免只延后干扰动作而令其自身掉判。

这个检查点尚未重跑分级与压力批次，也未证明全部高难谱无局部回退。
