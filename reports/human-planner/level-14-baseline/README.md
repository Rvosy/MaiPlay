# HumanPlanner 等级扫描

冻结 Core：`0060D326F8A6A89ACB8932F983CDE3372D4ACF74D4EDC489408EDC71F073F6C2`。清单 SHA-256：`A59FA07CB45A4D78AD0A49D89B68CA5E27536F854AD687D9756A6E6739C81AF3`。

曲库包含 1512 个 maidata.txt，解析失败 0 个；选取显示等级 14、14+，遍历所有难度编号。

已核验 311/311 张谱面，进程或证据校验错误 0 张。全 CP 211 张；308311 个音符中有 425 个非 CP，其中 Miss/TooFast 292 个。Gap 333 条，非 CP 与 Gap 合并去重后影响 425 个音符，共 183 簇。

运动诊断共记录 0 次速度限制违例，涉及 0 张谱面。

| 难度 | 显示等级 | 谱面 | 全 CP | 非 CP | Miss/TooFast | Gap | 错误簇 |
|---|---|---:|---:|---:|---:|---:|---:|
| Master | 14 | 197 | 149 | 117 | 56 | 72 | 71 |
| Master | 14+ | 75 | 39 | 198 | 153 | 172 | 75 |
| Re:Master | 14 | 29 | 18 | 58 | 42 | 48 | 25 |
| Re:Master | 14+ | 10 | 5 | 52 | 41 | 41 | 12 |

## 统计口径

扫描清单包含曲库中所有所选显示等级与难度编号的组合；同曲不同难度分别计数。定数的小数部分达到 0.6 时显示为加号，显式加号也可识别。

CP 对应判定枚举 Perfect。非 CP 是其余所有判定；Miss 统计包含 TooFast。Gap 单独计数，不能与非 CP 直接相加。成绩来自冻结求解器生成输入后交给 PlaybackSession 的完整回放。

错误簇先合并非 CP 与 Gap 的音符编号，再按原始 Note.Time、编号排序。相邻时间差不超过 1 秒时传递合并，浮点容差为 1e-9；簇总跨度可以超过 1 秒。Hold/Slide 使用起始时刻，不使用判定发生时刻或持续区间。

排序依次为非 CP 数、错误簇数、谱面路径、难度编号，均升序；进程或校验错误排在最后。完整排行保留零错误谱面。错误少只是调查顺序，不代表问题容易修复。

结果数量、唯一音符编号、音符类型与起始时刻均与冻结解析器导出的谱面记录比对；同时检查源谱、Core、原始结果与轨迹 SHA-256，以及进程退出码与 AllCriticalPerfect。运动诊断保留速度限制违例和逐谱峰值速度；它们按当前输入模型计算，不代表实机人体测量。

## 非 CP 类型

| 音符类型 | 数量 |
|---|---:|
| Hold | 40 |
| Slide | 73 |
| Tap | 212 |
| Touch | 93 |
| TouchHold | 7 |

## 判定分布

| 判定 | 数量 |
|---|---:|
| FastGood | 53 |
| FastGreat | 3 |
| FastGreat2nd | 3 |
| FastGreat3rd | 4 |
| LateGood | 38 |
| LateGreat | 16 |
| LateGreat2nd | 5 |
| LateGreat3rd | 2 |
| LatePerfect2nd | 6 |
| LatePerfect3rd | 3 |
| Miss | 292 |

## 每谱错误簇分布

| 错误簇数 | 谱面数 |
|---|---:|
| 0 | 211 |
| 1 | 57 |
| 2 | 24 |
| 3 | 9 |
| 4 | 7 |
| 5 | 1 |
| 8 | 1 |
| 10 | 1 |

[完整排行](charts.md) 保留全部谱面，[单错清单](one-error-charts.md) 单独列出仅有一个非 CP 的谱面；[机器可读报告](ranking.json) 还保留每个非 CP 与 Gap 的位置、持续时间、判定偏差、原因和簇内音符编号。原始谱面不随报告发布；整曲轨迹与原始回放结果保存在 artifacts/。

## 复现输入

- 冻结 Harness：`artifacts/human-planner-validation/sweep-candidate-build/bin/MaiPlay.GameplayHarness/release/MaiPlay.GameplayHarness.dll`。
- 扫描清单：`artifacts/human-level-14-baseline-0060/manifest.json`。
- 原始结果目录：`artifacts/human-level-14-baseline-0060`。
- 曲库根目录：`.reference/charts/game-extracted`。

使用仓库中的 `research/human-planner/scan-levels.py --collect-only` 可从这些证据重算统计。报告生成时间和本次重聚合用时会变化；逐谱证据及其原始字节指纹保持不变。
