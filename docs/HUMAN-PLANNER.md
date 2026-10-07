# 配置与动作规划

`HumanPlanner` 是开发版的试验求解器。它按谱面配置生成局部打法候选，在同一次搜索中连接各段动作，并通过现有判定器核验整首谱面。默认入口仍使用 `NormalHandPlanner`；新求解器已接入开发版，尚未发布。

## 使用

打开歌曲并选择难度，在主界面「运行模式」区域将「基线手顺」切换为「配置与动作规划（试验）」，再点击「求解手顺」。规划结束后可播放完整或部分完成的方案。回放、缺口定位和输入日志格式见[输入回放指南](INPUT-REPLAY.md)。

离线生成一张 Master 谱面：

```powershell
dotnet run --project tests/MaiPlay.GameplayHarness -c Release -- --plan-human "歌曲目录" artifacts/human-planner-validation/曲目名 5
```

输出 `hands.json` 和 `results.json`。后者包含逐音符判定、分手动作、缺口，以及 `HandPlan.Diagnostics` 中的动作指标、搜索成本、配置完成情况和打法使用次数。部分完成时仍保存轨迹，并返回非零退出码。原有 `--plan` 继续调用基线求解器。

按等级对照同一批谱面，从紫谱 11+ 和红谱 12+ 开始：

```powershell
dotnet run --project tests/MaiPlay.GameplayHarness -c Release -- --compare-planners 5 11+ .reference/charts/game-extracted reports/human-planner/master-11-plus
dotnet run --project tests/MaiPlay.GameplayHarness -c Release -- --compare-planners 4 12+ .reference/charts/game-extracted reports/human-planner/expert-12-plus
```

难度编号 `4`、`5` 分别为 Expert、Master。显示等级参数只选取该等级，可依次改为 `12`、`12+`、`13` 等；`all` 选择该难度的全部等级。定数与显示等级的转换由 [`PlannerComparison.DisplayLevel`](../tests/MaiPlay.GameplayHarness/PlannerComparison.cs) 负责。

每次对照生成 `comparison.json` 和 `comparison.md`，记录逐谱结果、等级汇总、谱面 SHA-256、判定规则版本、程序集指纹和运动模型参数。报告保存在 `reports/human-planner/`；曲库、音频和完整轨迹保留在本机目录。报告对应生成时的程序集，不自动代表之后修改的结果。

退出码 `0` 表示本批对照没有退步，不表示全部谱面全 CP。无匹配谱面或出现解析、求解异常时返回 `1`；存在退步或各项指标有升有降时返回 `2`。改善与退步按 CP 数、达成率、DX 分数和缺口数一起判断，动作指标单独列出。

## 实现分工

| 层 | 入口 | 职责 |
|---|---|---|
| 谱面需求 | [`GameplayDemandCompiler`](../src/MaiPlay.Core/GameplayDemand.cs) | 保留音符、通道、目标区域和谱面要求的时间；星头与滑动轨道分别进入规划 |
| 配置识别 | [`PatternAnalyzer`](../src/MaiPlay.Core/PatternAnalyzer.cs) | 将需求切为单击、双押、双押流、纵连、交互、轴、楼梯、转圈、散点、长按、Slide、Wifi 和 Touch 等段落 |
| 打法候选 | [`TechniqueCandidates`](../src/MaiPlay.Core/TechniqueCandidates.cs) | 为当前段提供同手、交互、单手扫键、左右接力、双手并扫等适用候选，并保留自由分手候选 |
| 轨迹与接触 | [`ContactTrajectoryCompiler`](../src/MaiPlay.Core/ContactTrajectoryCompiler.cs) | 基线与新求解器共用掌面与四指接触、触区中心、Slide 校准和 Wifi 双手轨迹 |
| 搜索与运动约束 | [`HumanPlanner`](../src/MaiPlay.Core/HumanPlanner.cs)、[`HumanMotionModel`](../src/MaiPlay.Core/HumanMotionModel.cs) | 连接各段动作，检查占手与速度，计算移动、偏侧、投影交叉及时机调整成本 |
| 既有接触覆盖 | [`DemandCoverageProbe`](../src/MaiPlay.Core/DemandCoverageProbe.cs) | 用单音符真实回放确认已有接触能否直接打到 Tap 或 Touch，为搜索排序提供依据 |
| 整曲核验 | [`HandReplay`](../src/MaiPlay.Core/HandReplay.cs)、[`PlaybackSession`](../src/MaiPlay.Core/PlaybackSession.cs) | 把轨迹转换为真实输入，再确定判定、成绩及失败位置 |

每段候选都从上一段保留下来的搜索状态继续。长按和滑动占用的手会约束后续段落，星头与轨道允许分手；不会在配置边界重置双手。打法偏好按段内动作数量累计，避免长段的自由分手只承担一次很小的偏好成本。配置名称用于产生候选和统计，不直接决定某段必须使用哪一种打法。识别结果目前是互斥分段，不能当作社区配置叫法的完整分类，也不能据此宣称已经归纳每个等级的全部特点。

配置通常按接触模板的开始时刻排序，谱面需求保留原来的逻辑时间。Wifi 的提前接触属于可选打法，因此按谱面起滑时刻排入搜索，避免提前占用双手、截断此前的扫键。动作候选可以延后入屏并保留原末区时刻，按实际缩短的移动时间检查速度，再以完整回放核验。首个接触仍可提前，但须与此前已分配动作相容。

同刻 Touch 与 TouchHold 会尝试先完成短 Touch、再进入持续按压，也会比较按触区位置从左到右、从右到左以及依次访问相邻触区的顺序。Touch 的击打时机由 `HumanPlanner.Shifts` 提供，允许在窗口内连续完成多个接触；是否能够共用一只手仍由占手和移动约束决定。

搜索每步先按未分配接触动作数，再按成本排序；双手 Wifi 对应两个基础接触动作，最终方案仍按音符的真实成绩比较。剪枝在最低未分配动作数的状态中，先保留不同左右手分配各自的最低成本方案，再保留各布局下左右手分别最早释放的方案，最后按成本补满搜索宽度。这样既保留不同分手，也为后续密集输入留下较早腾出手的候选；各类候选仍共享同一个宽度限制。状态包含双手当前动作、Wifi 分支和接近动作的起始时位；另一只手的位置按当前候选时刻插值。对同方向连续扫键，接近轨迹沿外圈持续接触，替换逐键抬手，并按弧长检查移动速度和计算成本。

`HumanPlannerOptions` 持有搜索宽度和整曲核验候选数。候选经过真实回放后，依次比较超速分段数、非 CP 数、达成率、缺口数和搜索成本。因此，在已核验候选的判定表现相同时，会保留成本较低的方案。搜索会剪枝，状态也没有保存完整判定器历史，结果不保证全局最优或找到所有可行手顺。

## 速度与反馈

速度检查覆盖空中移动、滑动内部各段和连续扫键。搜索与最终轨迹使用同一段移动时间。扫键与普通按钮共用 `HumanPlanner.Shifts` 的微小时移候选，让密集双押扫在原 CP 窗口内衔接，仍检查实际弧长和速度。同手、同向的相邻扫键可以跨配置段继续，单扫转双扫不再强制抬手；跨段连接仍须符合邻键、时间和速度约束。连续扫键以四指刚进入目标键的边缘作为到达点，只有新落手才检查邻键误触并选择更靠内的安全落点。短非 Wifi Slide 若原轨迹超速，可以在 `HumanPlanner.PrepareMotion` 规定的时机搜索范围内延长划动，再接受整曲核验；仍须实际经过判定区。

同手连续击打同一个键时，`HumanMotionModel.RepetitionCost` 根据击打间隔增加成本。搜索取谱面与实际间隔中较短的一项，避免判定窗口内的微小时机调整把快连打算成慢连打。快速纵连优先比较双手交替，较慢连打仍可用同手；混合节奏候选逐次切换手序。另一手被长按或滑动占用时，单手打法保留为候选。这项偏好按每次接触计算，不依赖歌曲、整段是否匀速或位移是否为零。

Hold 和 TouchHold 通过 `HumanPlanner.MotionVariants` 同时保留原尾部与提前释放候选。提前释放可以为紧接的大位移或密集 Touch 腾出移动时间；能否保持 CP 由整曲回放中的实际持续按压比例决定。

扫键可以接入单个 Hold，也可以从单键分成双手扫动、以含 Hold 的双押结束。持续按压从沿弧到达长条时开始，随后沿弧落到长条键心，释放入口相邻键；被 Hold 占用的手不能继续扫走，下一键由仍可用的手承接。配置识别同时保留另一只手的初始双押和末尾普通键，不把长条从扫键末端单独拆走。

整曲核验发现 Slide 提前结束，或触摸轨迹提前触发 Tap、Hold 时，[`PlannerTimingFeedback`](../src/MaiPlay.Core/PlannerTimingFeedback.cs) 从实际提前量计算接触延后提示。Hold 的反馈回到头判时刻归因，不使用尾部结算时刻。`HumanPlanner.Plan` 最多进行两轮反馈，每轮分别核验恢复原始时机、提前离开干扰区和按提前量延后的候选，只有比较结果改善才保留调整。提前方案复用已有 Slide 时机范围，不扩大判定窗口。被反馈调整的接触动作只要存在可行分配就优先保留；全部候选都无法衔接时才允许留下该动作的缺口。反馈复用同一搜索、占手约束和判定流程，不修改判定窗口，也不直接改写成绩。

Slide 提前结束时，反馈按尾区完成前一采样的实际输入源寻找干扰动作，不假定一定是该 Slide 自己的轨迹。延后量由原判定器的 CP 窗口计算，避免把前一条轨道整体拖到下一条的目标时刻。若 Touch 或 TouchHold 因另一掌面从早窗之前持续压住同一区、遮住计划输入的上升沿而头判超时，也会对这个实际阻挡动作尝试小手。TouchHold 可能在尾部结算为 Good，因此按头判超时原因归因，不只检查最终 Miss。

当前状态无法为 Tap 或 Touch 分配独立动作时，搜索会检查双手已有的触摸轨迹。若 `DemandCoverageProbe` 的单音符回放确认直接 CP，该需求不增加搜索缺口代价；例如 Wifi 经过外圈与中心时可以同时覆盖后继按钮和 Touch。同一个双手轨迹组合的输入在当前配置段内复用，局部检查保留原采样相位，不计 TouchGroup 继承，也不把结果写入最终成绩。

所有方案都回放至曲末。未分配需求若被已有接触或 TouchGroup 继承判为 CP，最终不列为缺口；其余缺口和非 CP 位置保留在报告中。全 CP 只表示这份输入通过当前判定器，不能替代真人回放验收。

## 运动模型的范围

[`CabinetGeometry.Approximate43Inch`](../src/MaiPlay.Core/CabinetGeometry.cs) 为现有归一化坐标提供估算的毫米尺度。`ChartPoint` 以判定圈半径为单位；源传感画布和外键近似尚未独立对齐到实物尺寸。该尺度没有经过机台测量，不能把屏幕圆孔、判定圈、传感边界和外键视为同一物理半径。

速度上限、偏好速度和单手偏好击打间隔由 `HumanMotionModel` 构造参数持有，都是可配置的搜索假设，尚未经过人体标定。默认速度上限换算自基线连续扫键已允许的每秒 30 个判定半径；新模型将这一上限统一用于所有移动，因此也比基线的普通空中移动限制宽。它提供搜索范围，不能视为一般玩家的能力标准。搜索成本混合了距离、速度、偏侧、交叉、重复击打、打法偏好和时机调整，只用于方案排序，不表示能量消耗或体力负荷。

`HumanMotionMetrics` 统计生成轨迹的左右手路程、峰值速度、超速分段数、投影交叉次数与时长、方向反转次数，以及单手一秒内的动作峰值。投影交叉只表示屏幕平面中左手位于右手右侧；方向反转指相邻非零移动方向的夹角超过直角。动作峰值使用半开的一秒窗口，同手同刻开始的共享动作只计一次。这些统计不包含手臂上下关系、肩肘腕姿态、三维碰撞、加速度限制或疲劳模型。

Touch、TouchHold 和 Slide 默认使用掌面圆，半径由 `ContactTrajectoryCompiler.PalmRadius` 持有，当前比例下直径约 89.4 mm。按钮继续使用原四指接触。掌面是固定估算，不会在搜索中不断放大。整曲回放确认提前误触后，`PlannerTimingFeedback.FingertipCandidates` 找出相关触摸动作，规划器尝试把这些动作改成四指小手，其余动作继续使用掌面。缩小后的 Slide 会按相同接触形状重新校准；只有实际成绩改善才保留缩小方案，时机调整仍是另一条可选修复路径。

回放显示真实触摸接触的轮廓，手位圆形和菱形另行保留。接触形状尚未经过人体与机台标定；二维轨迹缺少腕姿和碰撞约束，不能仅从成绩推断真人可执行性。

## 验证入口

配置识别、实际输入回放和运动约束的回归用例分别位于 [`GameplayDemandTests`](../tests/MaiPlay.Tests/GameplayDemandTests.cs)、[`HumanPlannerTests`](../tests/MaiPlay.Tests/HumanPlannerTests.cs) 和 [`HumanMotionModelTests`](../tests/MaiPlay.Tests/HumanMotionModelTests.cs)。Touch 长按衔接与密集同刻输入另有 [`HumanTouchSchedulingTests`](../tests/MaiPlay.Tests/HumanTouchSchedulingTests.cs) 和 [`HumanTouchCoverageTests`](../tests/MaiPlay.Tests/HumanTouchCoverageTests.cs)。[`HumanSweepPlanningTests`](../tests/MaiPlay.Tests/HumanSweepPlanningTests.cs) 覆盖不同速度、方向和起键的转圈及单扫接双扫；[`HumanRepetitionPlanningTests`](../tests/MaiPlay.Tests/HumanRepetitionPlanningTests.cs) 覆盖快慢纵连和另一手被长按占用的情况。这些用例均通过独立回放核对实际输入。

已保存的分级对照见[紫谱 11+](../reports/human-planner/master-11-plus/comparison.md)和[红谱 12+](../reports/human-planner/expert-12-plus/comparison.md)。每份报告列出自身的验证范围、改善和退步；批量复验使用上面的 Harness 命令。

架构取舍见 [ADR-0002](adr/0002-pattern-human-planner.md)。基线既有整库结果仍保留在[输入回放指南](INPUT-REPLAY.md)，不作为新求解器的验证结果。
