# 游玩机制上游基准与核验记录

记录日期：2026-10-07。本文保存固定来源、规则取舍和 0.2.0 的实现/测试对应关系。实际运行证据见 [0.2.0 验收记录](../docs/GAMEPLAY-VALIDATION.md)。

## 固定来源

| 来源 | 版本与当前用途 |
|---|---|
| [MajdataPlay](https://github.com/TeamMajdata/MajdataPlay/tree/dc19722d602f099131d93b37091624ad30150ad1) | `dc19722d602f099131d93b37091624ad30150ad1`，本地 `.reference/MajdataPlay` 已核对 HEAD；现代 DX 的主要行为基准，仓库附 GPL-3.0 |
| [AstroDX 当前主分支](https://github.com/2394425147/astrodx/tree/590ab7da61bdcf58c63c244e875bfe32cf19e2a0) | `590ab7da61bdcf58c63c244e875bfe32cf19e2a0`；README 仍引用 core-dump，该提交目录树没有该目录 |
| [AstroDX 历史 core-dump](https://github.com/2394425147/astrodx/tree/4cfd3a3ca7092fc1c910554677076429d6051e65/core-dump) | `4cfd3a3ca7092fc1c910554677076429d6051e65`；已核验 Slide、Regular/Fan 队列和许可情况；仅用于行为对照，未复制源码 |

AstroDX 的删除提交为 `964d8eabdb7ab93e65dfb4de6cd92b3d5ae951ec`，提交说明为 “Remove legacy assets, upload skinnable elements document”；上述历史基准是其父提交。不能将这份旧快照描述为当前完整 AstroDX 游戏源码。该提交的完整递归树共 77 项，GitHub 返回 truncated=false，无 LICENSE/COPYING/NOTICE。全部 33 个 C# 文件未见 copyright、license、licence 或 SPDX 授权头；README 的 Open-source projects 描述没有提供授权条款，因此未复制其源码。

皮肤、解析与既有时序来源继续见 [CHART-COMPATIBILITY](../docs/CHART-COMPATIBILITY.md)、[SKIN-COMPATIBILITY](../docs/SKIN-COMPATIBILITY.md) 和 [AUDIO](../docs/AUDIO.md)，不在本文复制整份说明。

## MajdataPlay 机制溯源

下列路径均相对固定提交 `Assets/Scripts/`。规则和代码数据直接适配 GPL-3.0 来源，Unity 环境依赖替换为 .NET 数据和确定性逻辑时钟。运行值由实现代码维护，边界样例在测试中维护。

| 机制 | 固定源码与符号 | MaiPlay 实现 | 独立行为验证 |
|---|---|---|---|
| 区域、脉冲、消费 | `IO/Base/Enums/SensorArea.cs`、`InputManager.DequeueSensor/DequeueButton`、`NoteControllers/NoteManager.GameIOUpdate/TryUse*` | `InputState.Sample`、`PlaybackSession.Advance` | `CoreInputTests` 多源覆盖、短脉冲、同刻顺序、跨刷新率重放；`HeadJudgementTests` 共享队列和按钮/A 区消费 |
| Tap/星头、EX | `NoteBehaviours/NoteDrop.Judge`、`TapDrop.Check/TooLateCheck` | `HeadJudgements.CheckHead/TapGrade` | 每档正负窗口的内部、端点、外部；EX 在有效窗口内提升，窗口外不命中 |
| Touch | `TouchDrop.Judge/Check/TooLateCheck` | `HeadJudgements.TouchGrade` | 独立非对称窗口、过早持续按住、按钮无效、同区排序 |
| Hold/TouchHold | `HoldDrop/TouchHoldDrop.BodyCheck/End`、`NoteLongDrop.HoldEndJudge` | `HeadJudgements.CheckBody/HoldEndGrade/IsHolding` | 两种短阈值、头尾忽略、8/9/10 OFF 采样的宽限边界、间断恢复、持续比例、头 Miss 后恢复及 EX 例外 |
| TouchGroup 与持续组 | `NoteLoader.TOUCH_GROUPS/AllocTouchGroup/AllocTouchHoldGroup`、`Misc/Notes/Touch/TouchGroup` 与 `TouchHoldGroup` | `HeadJudgements.ConnectedGroups/Group/BodyGroup` | 严格多数、最后跨阈值等级、混合 Touch/TouchHold、邻接与非邻接、成员结束后分母变化 |
| Slide 标准模板 | `Utils/SlideTables` 的 41 个 StaticSlideTable，`SlideArea.Area.Check`、`SlideBase.SensorCheck` | `SlideJudgeTables`、`SlideJudgements.Queue/Area` | `CoreSlideJudgementTests.StandardRoutes` 手写冻结的 41 条源表路线和 Const；每种路线遍历 8 旋转及适用镜像；每模板无输入与错区 |
| Slide 时间窗与尾判 | `SlideBase.Judge/TooLateJudge/JudgeGradeCorrection`、`SingleSlideBase.SlideCheck` | `SlideJudgements.Grade/Process` | 开放时刻、提前完成、正负窗口端点、剩一尾区 Good、其他未完成 Miss |
| 连接与同头分叉 | `SlideDrop.InitializeSlideGroup/UpdateJudgeQueue`、`SlideBase.SensorCheck` | `SlideJudgements.Track/Process`、`SlidePath.SegmentWeights` | 父段剩一格开放后段、后段触发完成父段、短连接不可跳区、分叉共享全局输入、各轨独立结算 |
| Wifi | `Utils/SlideTables` 现代 Wifi 表、`WifiDrop` | `SlideJudgeTables` 三队列、`SlideJudgements` | 三路独立推进、只走中心失败、D 区替代尾、整体尾 Good、8 旋转 |
| Break、EACH、烟花 | `NoteLoader.CreateTouch/CreateTouchHold/NoteCreateHelper`、MajSimai `SimaiParser`、`ObjectCounter`、各 Note 的 `PlayJudgeSFX` | `SimaiParser`、`AchievementState`、`JudgementDisplay`、`SoundEffects` | `CoreModifierTests` Touch Break/烟花、反引号错时和子组 EACH；`GameplayFeedbackTests` 分档声音、Break 子档、失败无烟花 |
| 成绩 | `ObjectCounter.UpdateNoteScoreCount/UpdateComboCount/UpdateFastLateCount` | `AchievementState`、`PlaybackSession.Apply` | `CoreAchievementTests` 与 `CoreInputTests.FinalGradesDriveAchievementDxComboAndStatisticsOnce` |
| 接触几何 | `IO/PdxTouch/TouchSensorMapper` 的 34 个原多边形 | `SensorPolygons`、`ContactMapper`（C 合并为逻辑一区） | `CoreInputTests` 固定源顶点、圆覆盖多区、按钮/Touch 分离、多源离屏及部分出界 |
| 声音、画面 | `NoteAudioManager.PlayTapSound`、各 Note 的 `PlaySFX/PlayJudgeSFX`、`SlideOK` | `SoundEffects`、`GameView` | `GameplayFeedbackTests`、`SkinTests.HandwrittenReplayRendersActualGradesLongNotesAndSensors` 及本版窗口记录 |

输入路线和预期等级来自固定源码的人工核对，测试没有读取被测规则表来生成成功路线或窗口预期。`tools/extract-slide-judgements.py`、`extract-slide-paths.py`、`extract-sensor-polygons.py` 分别复现生产使用的区表、路径采样和多边形；这些生成器不生成行为测试预期。

源码包包含生成后的 C# 数据，普通构建不需要上游工程或 Unity。重新提取数据时，先从上述 MajdataPlay 仓库取得提交 `dc19722d602f099131d93b37091624ad30150ad1`，放在 `.reference/MajdataPlay`，再从 MaiPlay 根目录执行：

```powershell
python tools/extract-slide-paths.py .reference/MajdataPlay
python tools/extract-slide-judgements.py .reference/MajdataPlay
python tools/extract-sensor-polygons.py
```

原歌曲、皮肤和 SFX 验收资源不随源码包分发。缺少这些资源时，相应集成测试会直接返回；测试总数不能证明重新完成了本机素材验收。

### 保留的源行为

- 每个 Tap/Hold 键位共用队列，每个 Touch/TouchHold 传感区共用另一队列。按钮与 Touch A 区独立，Tap/Hold 可用两者；Slide 不消费头判边沿，因此一次 Touch 操作可影响多个当前轨道和其他音符。
- Touch 过早保持接触不自动补头判。TouchGroup 使用固定邻接连通分量，成功成员必须严格超过一半；继承不增加真实成功成员数。
- 持续判定保留源表中的特殊情况：Miss 头可由持续体恢复到 Good；Good 头完整持续可升 Great；CP 头在中间持续比例档降为 Great 2nd，而非 CP 的 Perfect 头在同档降为 Great。释放宽限按采样累加，恢复时清空本次短间断，不将多个短间断合并。
- Slide 区保存 On/Off 历史，普通区需先 On 后 Off，尾区只需 On；只允许源表定义的跳区。默认 `SubdivideSlideJudgeGrade=false`，因此现代 Slide 的 Perfect 子档最终合并 CP。
- Touch Break 沿用上游计分；上游 Loader 强制 Touch EX=false，本版明确拒绝该修饰。`!`/`?` 只去头并保留等待。

### 明确的适配与差异

1. 上游 GameUpdater 每次渲染更新输入，长音符使用可变 DeltaTime。MaiPlay 固定 240 Hz、以音乐零点为网格，保留每次采样内的调用顺序；`1/60` 只作为原时间窗单位。原始事件在下一采样生效，不由 UI 帧率决定。双精度计算在闭边界使用 `1e-10` 秒数值容差。
2. Slide 在上游锁定等级后，还会为显示保留 LastWaitTime 再 ReportResult。MaiPlay 在锁定等级时立即发送唯一最终事件，效果使用统一短时显示；等级和测量偏差不加这段显示等待。
3. 连接轨逐段显式时长继续按谱面值保留；固定上游 NoteLoader 当前会无条件按 prefab 单位重分配总时长。沿用 0.1.1 已记录的选择，判定末段与引导星使用同一份段权重。
4. Touch 多边形直接复用上游 1440 画布数据；归一化坐标未标定为实物毫米。接触圆允许中心部分出界仍覆盖边缘传感区，离屏由显式状态表示；外键另用半径 1.04～1.20 的环形扇区近似。画面叠加不是硬件标定。
5. Fast/Late 采用上游 BelowCP。DX 实时显示已结算音符的累计得分，最终值与上游每档 3/2/1/0 一致；未结算音符尚未贡献得分。
6. 当前判定偏移和显示偏移固定为零，`first` 由解析一次应用。Classic、Mine、自定义轨道、ButtonRingForTouch 等 Mod 不启用。应用未接入人体约束或设备延迟校准。

## AstroDX 历史源码入口

相对历史版本的 `core-dump/Scripts/`：

- `Contexts/Gameplay/Behaviours/SlideBehaviour.cs`。
- `Contexts/Gameplay/Behaviours/Slide/Handlers/SlideSegmentHandler.cs`、`RegularSlideSegmentHandler.cs`、`FanSlideSegmentHandler.cs`。
- `Contexts/Gameplay/SlideGenerators/` 下的直线、圆弧、折线、环形和 S/Z 生成器。
- `Models/Scoring/Metrics/Internal/` 下的 `AchievementStats.cs`、`ComboStats.cs`、`DxScoreStats.cs`、`JudgementStats.cs`，以及 `Models/Scoring/NoteRecord.cs`。

该历史快照缺少被引用的 `TouchInteractable`、`StatisticsManager` 和 `Models.Scoring.TimeSpan`，不能从中复现完整游戏或核对全部分档时间窗。

已核验且未混入本版的差异：

- `RegularSlideSegmentHandler.GenerateArrows/GetClearCount` 从箭头最近传感区生成交互队列，并按可配置 slideTolerance 向前扫描；Majdata 使用固定区表、逐区跳过标记和 On/Off 历史。
- `FanSlideSegmentHandler.GenerateSensors/CheckInteraction` 使用起点 A、两侧 B、三个 A 尾区的三阶段；Majdata 现代 Wifi 是三条四区队列，中心经过 C，外侧尾可由 D 区替代。
- `SlideBehaviour.UpdateSegments/CheckInteraction` 等前段 Cleared 后才开放下一段，外层在 TimeSinceStart>=0 后才 Judge；Majdata 父段剩一区即可开放后段，并允许移动前提前完成。

本版全部采用 Majdata 的上述规则。AstroDX 仅用于识别差异，没有复制未授权代码或宣称覆盖当前完整 AstroDX。

## 实现、验证与发布

0.2.0 的机制实现在源码中，验收命令、运行结果、窗口检查和发布位置统一记录于 [GAMEPLAY-VALIDATION](../docs/GAMEPLAY-VALIDATION.md)。0.1.1 及更早发布目录、原皮肤图和原验收记录保持原版本。四首歌曲和五套皮肤仍只留在本机参考目录，原创最小谱面与日志位于 `samples/Gameplay`。
