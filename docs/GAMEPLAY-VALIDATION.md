# MaiPlay 0.2.0 验收记录

日期：2026-10-07 至 2026-10-08。环境：Windows 11 x64、.NET SDK 10.0.201。规则基准和逐机制对应关系见[研究记录](../research/upstream-gameplay.md)。这是 0.2.0 的记录，原 [0.1.x 验收记录](VALIDATION.md) 和发布目录没有改写。

## 已实现

- 算法模式使用时间戳按钮/33 区 Touch 输入、多接触源持续状态、边沿消费、固定 240 Hz 采样和全局干扰。JSON 日志与接触位置入口共用后续判定。
- Tap/星头、Hold、Touch、TouchHold、41 个 Slide 模板、连接、同头分叉、Wifi 三路、TouchGroup 与 TouchHold 持续组；适用的 Break、EX、EACH 和烟花。
- 最终事件统一更新减分达成率、DX 分数、判定计数和 Combo，并驱动画面/音效。每颗音符只结算一次，重开清空状态。
- 主界面保留歌曲、难度、模式、控制和成绩；显示、皮肤、声音和诊断移至设置面板。

## 已运行的核心与资源验证

隔离构建路径为 `artifacts/build-gameplay`，不覆盖正在使用的开发窗口。命令：

```powershell
dotnet test tests/MaiPlay.Tests -c Release --artifacts-path artifacts/build-gameplay --logger "trx;LogFileName=gameplay-final.trx" --results-directory artifacts/gameplay-validation/tests
dotnet run --project tests/MaiPlay.GameplayHarness -c Release --artifacts-path artifacts/build-gameplay -- samples/Gameplay/maidata.txt samples/Gameplay/inputs.json artifacts/gameplay-validation/sample-results.json
dotnet run --project tests/MaiPlay.AudioSmoke -c Release --artifacts-path artifacts/build-gameplay
```

完整测试通过 429 项、失败 0、跳过 0，结果保存在 `artifacts/gameplay-validation/tests/gameplay-final.trx`。验证包括：

- 各头判时间窗的内部、端点和外部，按下消费、按钮与 Touch 通道、同刻顺序、多来源释放、短脉冲、短/长 Hold、宽限与恢复、两类组阈值、共享占区干扰及重复结算防护。
- 41 条手写源表路线各 8 个旋转与适用镜像、所有模板错区/无输入、OR 区、不可跳区、释放、连接传递、分叉、Wifi 三路及尾判窗口。预期输入不从被测表生成。
- 一次推进与 29/60/144 Hz 调用的事件、Slide 轨迹和成绩一致；暂停重复时刻不变，重开可得到相同完整结果。
- 本机四首歌曲的全部 18 个难度载入并在 Auto/无输入两种模式完成结算。五套皮肤原 ZIP/目录、素材解码、选图与渲染回归通过；受版权素材未分发。
- 新图像位于 `artifacts/gameplay-validation/skins` 和 `input-feedback`，原 `artifacts/skin-validation` 保留。检查了矢量及五套皮肤的代表图，含实际 Great/Fast、Good/Late、持续长音符、Slide 成功/失败及 Wifi。
- Windows 真实输出设备的静音检查通过：设备时钟、音频 EOF/前后静音、暂停、恢复、重开、TouchHold 持续音、40 次音效 Reset 与 20 次音乐重开，无异步设备错误。未进行扬声器听音和硬件延迟标定。

随后新增 960×720 离屏界面检查，通过后重跑 `SkinTests` 全组，13 项全部通过（`ui-rendering-final.trx`）。其中 12 项已包含在前述 429 项中。离屏检查使用真实应用样式和控件，覆盖主面板、四个设置页、设置开启时禁用主区和关闭后的按钮状态；图像保存在 `artifacts/gameplay-validation/layout`。

手写样例共有 21 个计分实体。离线结果：完成 18、Miss 3、剩余 0，达成率 `81.0441%`，DX `44/63`，最终 Combo 6，Fast 1、Late 4。原始事件与区推进保存在 `sample-results.json`；[输入指南](INPUT-REPLAY.md) 给出逐项操作。

## 原生窗口

真实窗口中的手写输入样例得到 DX `44/63`，CP 13、Perfect 2、Great 1、Good 2、Miss 3，Fast 1、Late 4，剩余 0，与离线结果一致。默认窗口和最大化窗口中，播放区、成绩及播放按钮均可见。

发布程序 `artifacts/MaiPlay-0.2.0/MaiPlay.exe` 使用独立的 `MAIPLAY_DATA_DIRECTORY=artifacts/gameplay-validation/profile` 验收。默认实例使用 `%LOCALAPPDATA%\MaiPlay`，两者的设置和导入目录分开。独立实例使用 `Xaleid◆scopiX`、Re:Master 15.0 和 Light2 皮肤，已观察到区域边界、皮肤素材、Auto Play 成绩及播放画面。

- 播放中打开设置，时间停在 `01:39.4`、DX `1926/6666`、CP 642。将音乐音量改为 23%、Tap 流速改为 8.50 后，时间和成绩保持不变；关闭设置仍暂停，点击播放后继续前进到 `01:48.3`。
- 原生窗口缩至最小客户区 `960×720`，含标题栏与边框的截图为 `962×752`。播放区与底部控制完整，左栏内容通过滚动访问。显示、皮肤、声音、判定与诊断四个设置页均可见，关闭按钮可用。
- 在诊断页点击清空输入，时间、成绩和历史归零，保持暂停；再通过文件对话框加载 `samples/Gameplay/inputs.json`，显示 62 条输入，仍从零开始并保持暂停。
- 正常关闭独立实例后，用同一数据目录无参数启动，恢复了已保存的歌曲，停在 `00:00.0`。关闭前已核对 `settings.json` 保存了 Tap 8.5、Touch 7.5、音乐 23%、音效开启及 100% 音量、Light2 皮肤、区域显示和输入日志路径；重启后未逐页复核这些设置控件。

原生截图位于 `artifacts/gameplay-validation/window`：`live-imported-song.png`、`live-isolated-instance.png`、`settings-close-paused.png`、`minimum-window.png`、四张 `minimum-settings-*.png` 及 `input-log-reloaded.png`。样例各档和长音符的离屏图像另存于 `input-feedback`，不作为原生窗口证据。

## 发布与已知边界

Windows x64 自包含版本位于 `artifacts/MaiPlay-0.2.0`，程序版本为 `0.2.0`，附带 .NET Runtime `10.0.5`。在上述 SDK 环境复现发布的命令：

```powershell
dotnet publish src/MaiPlay/MaiPlay.csproj -c Release -r win-x64 --self-contained true --artifacts-path artifacts/build-gameplay -p:RuntimeFrameworkVersion=10.0.5 -o artifacts/MaiPlay-0.2.0
```

源码在独立目录 `artifacts/source-validation/MaiPlay-0.2.0` 构建通过，0 警告、0 错误；该目录不含 `.reference`、外部歌曲、皮肤或上游 SFX。源码中的生成数据可直接构建，重新提取数据所需的固定上游版本与命令见[研究记录](../research/upstream-gameplay.md)。

分发文件为 `artifacts/MaiPlay-0.2.0-win-x64.zip` 与 `artifacts/MaiPlay-0.2.0-source.zip`。源码包含项目、测试、原创样例、提取工具、文档和许可证；发布包包含 GPL 及依赖通知。旧版 `artifacts/MaiPlay-0.1.1` 保留原样。

确定性采样、Slide 即时报分、显式连接段时长和归一化接触模型的差异见研究记录。未实现规划器、真人设备、Classic、Mine 或自定义轨迹；不把 Auto 全 CP 当作实际输入或双手可执行的证据。未宣称运行了完整 Unity/Majdata 机台或 AstroDX 的当前完整游戏。
