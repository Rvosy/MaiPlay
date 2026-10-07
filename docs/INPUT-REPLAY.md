# 输入日志回放

算法游玩接收带音乐时间戳的操作。开发版可由手顺求解器生成双手输入，也可加载日志；输入只能改变区域状态，不能选择音符、判定等级或分数。基础判定由程序执行，尚无真人设备输入。

## 求解手顺

打开歌曲并选择难度，点击主界面的「求解手顺」。生成期间暂停播放；整曲实际输入核验后切换到算法游玩并重开。点击播放后，判定器重新消费动作输入，不复用核验时的成绩。无法分配的音符保留缺口，后续手顺继续生成；部分完成方案也可播放。位置列表和时间轴红线标出缺口与非 CP 音符。切换歌曲或难度会清除已生成手顺；重开和切换 Auto Play 保留当前谱面的手顺。生成结果不写入个人设置，重启后需重新生成或加载导出的日志。

默认的基线求解器 `NormalHandPlanner` 先把音符拆成占手动作，再保留多种左右手分配，按移动距离、衔接时间和交叉代价选择方案。同键单 Tap 连打优先保持同手；连续至少三下、相邻间隔短于 `FastRepeatInterval`（当前 125 ms，超过每秒 8 下）时才优先交互。占手冲突仍可要求换手，这个阈值是第一版偏好，不是人体能力标定。普通 Tap、Hold 和星头使用外键；Touch、TouchHold 和滑动使用内屏。双押分别交给两只手，星头和轨道独立分配。多 Touch 可以在各自 CP 窗内顺序击打；Wifi 由双手经过两侧及中路判定区。每个接触面为四指近似，不生成掌盖双键、单手内外键复合等特殊技巧。

运行模式下可选择「配置与动作规划（试验）」使用 `HumanPlanner`。它按配置生成局部打法候选，在同一次搜索中组合普通击打、交互、扫键和双路扫键，并对生成的全部移动使用同一速度模型。算法边界、毫米估算、调用方式和按等级对比见[配置与动作规划](HUMAN-PLANNER.md)。两个求解器都通过同一个 `PlaybackSession` 核验整首输入；试验方案不会自动用基线成绩覆盖结果。

规划只产生轨迹与接触，不修改基础判定。滑动的速度与末区释放通过局部实际判定校准，完整方案再次在整首谱面上核验误触、占区和成绩。未通过时尝试局部时机调整，并比较整曲回放结果。移动速度上限是归一化启发式，不是人体标定；尚无完整的腕姿与双手碰撞模型，也不保证搜索能找到所有可行手顺。手顺是否适合真人仍需播放验收。

扫键按解析后的实际音符间隔识别，BPM 和细分共同决定速度。例如 BPM 150 的 16 分与 BPM 75 的 32 分都是 100 ms。同方向相邻键的快速短串和较慢长串分别加入扫键、转圈候选，长度与间隔条件见 `NormalHandPlanner.CreateRotations`，快速阈值见 `FastSweepInterval`。候选包括左右半圈接力和快速段单手连续扫过，单手可以绕完整圈。轨迹沿外圈持续接触，按四指进入下一外键的边沿对齐音符；仍检查前后移动时间和占手冲突。

普通点按、接力和单手扫键分别生成整曲输入并实际核验，先比较非 CP 数，再比较达成率和缺口数。结果相同时，快速段优先单手扫键，较慢转圈优先半圈接力。若快速星星漏掉中间判定区，会尝试延长实际划动时间，再通过原有判定窗口核验。以上候选均不放宽判定窗口。

连续双押可拆为两条保持方向的键流，分别由两手持续扫动，包括反向扫、同向并扫及跨 8／1 的转动。单键与双键之间的分流、合流也可连续衔接。同方向经过空键时，可以保留划动；间隔按经过的键距换算，不能把空键当成瞬移。扫键距离按外圈弧长检查，反向双扫不能迎面穿过另一只手。起手可落在较近的键区边缘；若会覆盖判定窗内的其他待击打键，则向首键中央收，后续按进入边沿对齐。候选继承已通过的局部时机调整，再以整曲实际判定检查额外触发；没有按曲目名或 ID 编写规则。

时间轴左键拖动、松开定位，滚轮缩放，右键拖动平移；「全曲」恢复完整范围。跳转后保持暂停，输入、判定和成绩重建到该时刻；不会补播过去的敲击音效。左右手用圆形和菱形区分，悬空时空心、实际触发区域时实心。标记在音符下方，不表示手掌面积。

离线生成某一难度（末尾 `4` 为 Expert，`5` 为 Master）：

```powershell
dotnet run --project tests/MaiPlay.GameplayHarness -- --plan "歌曲目录" artifacts/normal-planner-validation/曲目名 4
```

输出 `hands.json` 和整曲 `results.json`，包含逐音符判定、分手动作和缺口；非全 CP 时返回非零退出码，但仍保存可播放的部分方案。日志可在设置 → 判定与诊断加载。导入日志只携带动作；要在主界面查看缺口列表，需为当前谱面点击「求解手顺」。

批量验证本地全部 Expert，包括标准和 DX 谱面：

```powershell
dotnet run --project tests/MaiPlay.GameplayHarness -c Release -- --batch 4 .reference/charts/game-extracted artifacts/expert-validation/red
```

难度编号可改为 `2`、`3`、`5`、`6`，分别验证 Basic、Advanced、Master、Re:Master；`--batch-expert` 保留为难度 4 的简写。优化后先复查失败项，再运行整个难度的曲库，避免只看到局部改进。

每首保存达成率、Rating、逐音符错误、缺口和谱面 SHA-256；`summary.json` 汇总全部结果、算法程序集指纹、平均达成率、Rating 合计、旧 35 / 新 15 的 B50 与理论上限。版本分组和边界规则见 [Rating 契约](specs/achievement.md#rating-与-b50)。不把缺少该难度或解析失败当作通过。可再传入旧 `summary.json` 路径，只复查其中未通过的谱面，子集报告不代表整库 B50。批量模式不保存全部手部轨迹；用 `--plan` 可单独导出供回放。

直接打开指定难度并回放已导出的手顺：

```powershell
dotnet run --project src/MaiPlay -- "歌曲目录" --difficulty 4 --input "artifacts/normal-planner-validation/曲目名/hands.json"
```

2026-10-08 的本地官方 DX 红谱整曲验证（达成率含 Break 额外分）：

| 曲目 | 等级 | CP / 全部判定 | 达成率 |
|---|---|---|---|
| SQUAD-Phvntom- | 12+（12.8） | 717 / 717 | 101.0000% |
| ozma | 12+（12.9） | 623 / 623 | 101.0000% |
| Synthesis. | 12+（12.6） | 645 / 645 | 101.0000% |
| Sky Trails | 12+（12.8） | 676 / 676 | 101.0000% |
| ATLAS RUSH | 12+（12.6） | 592 / 592 | 101.0000% |

首次验证日志位于 `artifacts/normal-planner-validation` 各曲目目录。行为测试 `NormalHandPlannerTests` 在参考谱面存在时重新生成并以 29 Hz UI 推进整首输入，验证无漏判、无剩余音符、DX 满分。2026-10-08 的双手扫键版本重新通过以上五首红谱及全部 502 项测试，结果见 `artifacts/double-sweep-validation/tests/double-sweep-final.trx`。这五首的通过不能外推为任意谱面均能通过。

同版完整 Expert 扫描读取 1,512 份谱面文件，其中 1,511 份有红谱：1,494 首全 CP，17 首部分完成，无解析或运行错误。767,711 个判定中有 40 个非 CP，保留 37 个未解缺口。与前一轮完整扫描相比，PANDORA PARADOXXX、sølips 新增全 CP，系ぎて与宙天也有改善，没有逐谱 CP 数或达成率退步。平均达成率为 100.9961%。

红谱范围的 B50 为 **14,802**（旧 35：10,484；新 15：4,318），理论上限 14,809。新版本取本地库中的 CiRCLE、CiRCLE PLUS；全部红谱 Rating 合计为 372,854。完整结果和逐谱错误位置见 `artifacts/double-sweep-validation/red-final/summary.json`，与上版的对比见同目录 `comparison.json`。程序集指纹、谱面指纹和源码清单用于对应这次结果，不代表跨全部难度的 B50。

早期 Master 搜索中断记录保存在 `artifacts/normal-planner-validation/master-*`。当前版本遇到局部缺口会继续生成，可用于逐段检查；尚未宣称 Master 全部通过。

同日双手扫键版本的高难度抽查如下。完整动作、缺口和逐音符结果位于 `artifacts/double-sweep-validation/entry-checks`，所有方案均播放至曲末：

| 曲目 | 难度 | CP / 全部判定 | 达成率 |
|---|---|---:|---:|
| SQUAD-Phvntom- | Master 14.3 | 1029 / 1064 | 98.2921% |
| ozma | Master 13.9 | 786 / 792 | 100.5951% |
| Synthesis. | Master 14.1 | 1090 / 1103 | 98.9257% |
| Sky Trails | Master 14.7 | 1007 / 1052 | 96.4370% |
| ATLAS RUSH | Master 14.7 | 1012 / 1045 | 97.6849% |
| Xaleid◆scopiX | Re:Master 15.0 | 2085 / 2222 | 95.2442% |

剩余错误包括密集双押、多个 TouchHold 占手以及 Slide 与星头的衔接。Xaleid◆scopiX 后半段仍有缺口，当前默认方案没有启用掌盖双 A 区等特殊技巧。扫键可行性只经过接触几何、移动速度、占手与实际判定检查，尚未标定手腕姿态和双手碰撞。

ジングルベル（ID 70）的独立回归覆盖快速双扫、分流与合流、连续转动经过空键：

| 难度 | 修改前 CP | 当前 CP | 当前达成率 |
|---|---:|---:|---:|
| Master 14.4 | 642 / 648 | 648 / 648 | 101.0000% |
| Re:Master 14.2 | 685 / 737 | 730 / 737 | 99.9400% |

紫谱 93.300～93.413 秒的双手反向扫键已全 CP。白谱修复 45 个非 CP，没有新增非 CP；42 秒以后的全部音符已全 CP。剩余 7 个错误位于 4.200、27.900、28.800、37.500、38.400 秒，其中两处双押各有两颗漏判。回放及逐音符结果分别位于 `artifacts/double-sweep-validation/preview-master` 和 `preview-white`，修改前的证据保留在同级 `before`、`white-before`。

## 运行样例

从仓库根目录启动：

```powershell
dotnet run --project src/MaiPlay -- samples/Gameplay/maidata.txt --input samples/Gameplay/inputs.json
```

发布版可运行：

```powershell
.\MaiPlay.exe samples/Gameplay/maidata.txt --input samples/Gameplay/inputs.json
```

`--input` 自动选择算法游玩。点击播放后，样例包含下列操作；时间为音乐秒：

| 时间 | 操作与预期行为 |
|---|---|
| 1.000 | 按钮 1，CP |
| 1.925 | 按钮 2，提前 75 ms，Fast Great 2nd |
| 3.125 | 按钮 3，延后 125 ms，Late Good |
| 4.000 | 错按按钮 5，按钮 4 的 Tap 到期 Miss |
| 5.025 | Break 5，Late Perfect 2nd，扣 Break 额外分 |
| 5.875 | EX 6，有效窗口内提升为 CP |
| 7.000～8.400 | Hold 7 保持到有效体区结束，CP |
| 9.200 | A1 Touch，Late Perfect 3rd |
| 10.700～11.400 | C 过早按住，没有新的按下边沿，11 秒的 Touch Miss |
| 12.000～13.500 | C TouchHold 成功 |
| 14.000～14.850 | 星头和 `1-5` 分别接收按钮与传感区输入，轨道成功 |
| 17.000 | 第二条 Slide 只打头，错误区域不能推进轨道，轨道 Miss |
| 20.000～20.200 | Hold 打头后过早放开，最终降为 Good |
| 22.000 | A2、B2 成功后，相邻组 C 继承判定 |
| 25.000～25.850 | Wifi 三路分别完成，整体成功 |

设置 → 判定与诊断可以加载或清空 JSON，并查看最近输入、最终判定和 Slide 剩余区。加载和清空都会重新开始。打开设置先暂停，关闭后继续保持暂停。开启传感区显示可检查按钮和各 Touch 区的 ON/OFF。

`--algorithm` 选择算法游玩，并沿用已保存的输入日志。检查无输入状态时，先在设置中清空日志；音符会由各自状态机到期 Miss。Auto Play 走独立理想路径，不用于验证输入。

## 双手回放示例

```powershell
dotnet run --project src/MaiPlay -- samples/HandReplay/maidata.txt --input samples/HandReplay/hands.json
```

示例包含普通击打、双手接双押、Touch、连续星头与换手滑动；`hands.json` 由手顺求解算法生成。主界面的「求解示例」会重新规划这份谱面。特殊技巧的手写接触用例保留在 `samples/HandTechniques`，只用于测试接触表达能力，不作为默认手顺。

设置 → 显示提供触区亮起、左右手位和短拖尾三个独立开关。默认点亮触区并显示手位，拖尾关闭。蓝色圆形代表左手，橙色菱形代表右手；空实状态和遮挡规则见[显示契约](specs/player-settings.md#二级设置内容)。区域底色读取判定器当前实际输入，两手同时覆盖时分为蓝、橙两半。无手部归属的旧日志使用绿色，不根据 `source` 的名称猜测左右手。设置 → 判定与诊断仍可打开区域边界和编号。

手位和拖尾按音乐时间取样，暂停冻结，重开回到起始时间。手位在轨迹首帧之前、末帧之后隐藏。Auto Play 不显示虚构的手部输入。关闭显示选项不改变输入、判定或成绩。

## 区域日志

```json
{
  "ruleVersion": "majdata-dc19722-240-v1",
  "events": [
    {"time": 1.0, "channel": "Button", "area": "1", "isDown": true, "source": "left"},
    {"time": 1.1, "channel": "Button", "area": "1", "isDown": false, "source": "left"},
    {"time": 2.0, "channel": "Touch", "area": "C", "isDown": true, "source": "right"},
    {"time": 2.1, "channel": "Touch", "area": "C", "isDown": false, "source": "right"}
  ]
}
```

`time` 是音乐坐标，谱面的 `first` 已加到音符时间上，不应再给日志加第二次偏移。当前判定偏移和显示偏移均为零。事件按时间升序，同刻保留文件顺序。`source` 默认 `log`；多个来源覆盖同一区时，最后一个来源离开才释放。

按钮区域为 `1`～`8`。Touch 为 `A1`～`A8`、`B1`～`B8`、`C`、`D1`～`D8`、`E1`～`E8`，共 33 区。Tap/星头/Hold 接收对应按钮或 A 区，按钮优先；Touch/TouchHold/Slide 只接收 Touch 通道。按住不会重复产生头判边沿。

日志在导入时检查时间顺序、通道、区域和规则版本。未知字段也会报错，例如 `noteId` 或 `grade` 不属于输入格式。无效导入保留原会话。

## 逻辑采样

`PlaybackSession` 固定使用 240 Hz，以音乐零点为采样网格。每次处理截至当前采样的全部事件，按下又释放的短脉冲保留 ON 一次，在下一次无覆盖采样变为 OFF。一次采样内，每个通道的每个区域最多消费一个头判边沿；Slide 和长音符体读取共享持续状态。

同次采样依次处理 Tap、Hold、Slide、Touch、TouchHold，同类按谱面时间和 ID 排序。Slide 保留上游先检查上一采样完成状态、再推进本次区域队列的顺序。晚于闭窗口端点的首次采样才判超时。240 Hz 是 MaiPlay 为确定性选择的运行规则；上游的 `1/60` 是时间窗单位，不代表上游固定以 60 Hz 判定。

原始时间戳到有效采样的延迟小于一个逻辑间隔；记录中的判定时间和偏差使用有效采样时间。UI 29/60/144 Hz、一次推进整曲和分次推进使用同一网格。暂停不调用逻辑推进，重开清空输入游标、持续状态和成绩。

## 接触日志与几何

`samples/Gameplay/contacts.json` 演示位置、半径、按下/离屏和两个接触源的重叠。同一日志选择 `events`、`contacts` 或 `hands`，不能混用。

接触位置以传感画布中心为 `(0,0)`，右为正 X，下为正 Y，画布边界为 `[-1,1]`。半径使用同一单位。Touch 多边形直接取自固定 MajdataPlay 的 `TouchSensorMapper`，将 1440 像素画布归一化；两个 C 多边形合并为一个逻辑 C。转换检查接触圆与全部区域的交集，不按最近中心点分配。圆心部分超出画布时，仍可覆盖边缘区域；`isDown:false` 明确表示离屏并移除该来源的全部覆盖。

外键接触是独立近似：每键占 45°，使用内半径 1.04、外半径 1.20 的环形扇区。它不模拟实物键行程，不代表毫米尺寸。传感多边形、音符显示中心和画面判定圈分别定义；可视化只把它们按归一化坐标叠加，不宣称硬件标定一致。来源及差异见[研究记录](../research/upstream-gameplay.md)。

## 手部轨迹格式

`hands` 使用以下结构；定义见 `src/MaiPlay.Core/HandReplay.cs`。以下是特殊技巧的格式示例：一只左手在 1～2 秒覆盖 A7、A8、D8、E8 的圆形掌面近似，默认规划器不生成该动作。

```json
{
  "ruleVersion": "majdata-dc19722-240-v1",
  "hands": [{
    "hand": "Left",
    "frames": [
      {"time": 0, "position": {"x": -0.601019, "y": -0.601019}, "contacts": []},
      {"time": 1, "position": {"x": -0.601019, "y": -0.601019}, "contacts": [
        {"id": "palm", "channel": "Touch", "offset": {"x": 0, "y": 0}, "radius": 0.18}
      ]},
      {"time": 2, "position": {"x": -0.601019, "y": -0.601019}, "contacts": []}
    ]
  }]
}
```

- `hand` 为 `Left` 或 `Right`，每只手至多一条轨迹。位置与接触日志使用同一归一化坐标。
- 帧时间严格递增。`position` 是手位标记的锚点，帧间线性移动；算法需自行提供绕行轨迹，显示层不会添加避障曲线。
- 每帧 `contacts` 是该时刻完整的接触部位集合，空数组表示离屏。`offset` 相对手位，圆心为 `position + offset`。同一只手可有四个指腹、掌面或指拇等多个部位，分别指定 Button / Touch。
- 部位 `id` 在一帧内唯一。相邻帧中同 id、同通道的部位连续插值偏移和半径；新增或移除的部位在新帧时间生效。最后一帧必须释放全部接触。
- 轨迹在 240 Hz 网格和全部原始关键帧时刻转换为接触事件，再经过原有区域映射与判定器。保留关键帧可避免丢失短于一个采样间隔的按下／释放。显示位置可平滑插值，区域底色始终以判定器的 ON 状态为准。

输入文件大小限制仍由 `InputLog.Load` 持有；展开采样数量由 `HandReplay.MaximumSamples` 限制，避免极长的稀疏轨迹使导入无界展开。手部显示和接触模型没有毫米、腕部姿态或人体碰撞标定，不将回放通过等同于真人可执行。

自带样例的生成器与结果复现：

```powershell
dotnet run --project tests/MaiPlay.GameplayHarness -- --hand-samples samples/HandReplay
```

可在命令末尾附加本机歌曲目录，生成该曲 Expert 的整首手顺。日志与报告输出到 `artifacts/normal-planner-validation`，官方谱面及音乐不随样例分发。旧的前 20 秒手写实验保留在 `artifacts/hand-replay-validation`，不代表当前规划器的整曲结果。

## 离线验证和结果

```powershell
dotnet run --project tests/MaiPlay.GameplayHarness -- samples/Gameplay/maidata.txt samples/Gameplay/inputs.json artifacts/gameplay-validation/sample-results.json
```

省略输入文件时运行无输入。Harness 对谱面内每个难度输出最终成绩、全部过程/判定事件和 Slide 区推进轨迹。结果的 `TimingKind` 区分头判、长音符头判、组继承和 Slide 尾判。长音符最终偏差沿用头判记录；`Reason=head-timeout` 时的 0.15 / 0.3 秒是上游的头判超时标记，不是实测输入偏差。组继承复用成功成员的偏差。Slide 超时无一次可测的尾输入时，偏差为 null，保留失败原因和剩余区数。

达成率沿用[现有契约](specs/achievement.md)。DX 分数 CP/P/G/Good-Miss 分别为 3/2/1/0；Combo 在成功判定增长，Miss/TooFast 清零。Fast/Late 显示与计数采用上游 `BelowCP`：CP 不显示，其他成功判定按方向统计，零偏差降级和超时 Good 归 Late。每个音符只结算一次。
