# 谱面、时序与运动兼容记录

核验日期：2026-10-07。这里记录可复现的实现范围；不将 Auto Play 视为真人打法验证。

## 参考版本与复用边界

- [MajdataPlay](https://github.com/TeamMajdata/MajdataPlay/tree/dc19722d602f099131d93b37091624ad30150ad1)：`dc19722d602f099131d93b37091624ad30150ad1`，当次 `dev`。
- 该版本固定的 [MajSimai](https://github.com/LingFeng-bbben/MajSimai/tree/fdb2a3e39d8997a0abbf8b4679062d854473cc77)：`fdb2a3e39d8997a0abbf8b4679062d854473cc77`。
- [SimaiSharp](https://github.com/reflektone-games/SimaiSharp/tree/5ef06f91ffdcc77d494baba46d2df4c8d67ca1d6)：`5ef06f91ffdcc77d494baba46d2df4c8d67ca1d6`，MIT，Copyright 2025 Reflektone Games。仅作为独立解析对照工具，没有成为应用依赖或复制其源代码。
- [MaiMuriDX](https://github.com/Minepig/MaiMuriDX/tree/0ebab7ff6853a159ef6af76d9e3c43b23157bb5e)：`0ebab7ff6853a159ef6af76d9e3c43b23157bb5e`。仅参考轨迹形状与机制，没有复制其 Python 实现或轨迹资源。

谱面解析保留本项目的 C# 接口，游玩判定和区队列已适配固定上游；完整机制溯源见 [研究记录](../research/upstream-gameplay.md)。标准 Slide 的采样坐标直接取自上述 MajdataPlay 版本的 `Assets/Prefabs/Game/Slides/Star_*.prefab`，遵循其 GPL-3.0 许可。`SlidePathSamples.cs` 保留来源与固定提交，`tools/extract-slide-paths.py` 可从该版本重新生成数据。完整第三方说明见 `THIRD-PARTY-NOTICES.md`。皮肤及音效的素材许可单独处理，不由这份记录授权。

## 可读取的谱面

- `maidata.txt` 歌曲目录或直接打开文本；UTF-8/BOM，严格解码失败时回退 Shift-JIS。
- `&title`、`&artist`、`&des` / `&des_n`、`&lv_n`、`&inote_n`，多个难度。
- `&first`，以及覆盖对应难度的 `&first_n`，单位为音乐秒。支持负偏移。
- `(BPM)`、中途 BPM 变化、`{分母}`、`{#每格秒数}`，逗号推进时间，`/` 与紧接数字表示同刻音符，反引号将后续子组依次错开 `1.875/BPM` 秒（128 分音），子组内 EACH 保留，`0` 强制 EACH，`||` 行注释，末尾 `E`。
- Tap、Hold、A/B/C/D/E 区 Touch、TouchHold、Touch/TouchHold 烟花 `f`；Break `b` 支持按钮与 Touch 家族，EX `x` 仅用于按钮音符，静态星 `$`、旋转星 `$$`、普通形状头 `@`。
- 所有标准 Slide 形状：`-`、`^`、`<`、`>`、`v`、`V`、`p`、`q`、`pp`、`qq`、`s`、`z`、`w`；同头分叉 `*`，连接 Slide；Slide Break 和头部 Break 独立。
- Hold 时长 `[分母:数量]`、`[BPM#分母:数量]`、`[#秒数]`。
- Slide 时长 `[分母:数量]`、`[BPM#分母:数量]`、`[BPM#秒数]`、`[等待秒数##移动秒数]`、`[等待秒数##分母:数量]`、`[等待秒数##BPM#分母:数量]`；同时接受 `[#移动秒数]` 与单个数字的移动秒数。
- Slide 默认等待一拍 `60/BPM`，不随 `{细分}` 改变；等待结束后的移动时长独立于音符入场流速。
- `!`、`?` 均去掉可判定星头，保留原等待与正常轨道淡入。这对齐当前 MajSimai，而非 SimaiSharp 对两者淡入类型的区分。

`&wholebpm` 不替代音符流中的初始 `(BPM)`。当前 MajSimai 的初始 BPM 也是 0；本版遇到缺 BPM 会报错，不把所有音符压在同一时刻。

无效位置、无穷/非数值时间、零或负 BPM、未闭合括号、缺少 Slide 时长，以及未知字符均报错。错误所在难度会显示在消息里，不会跳过坏音符后报告成功。当前不支持地雷 `m`、自定义 `K` 轨迹、谱内变速/其他扩展、同一实体的 Hold+Slide、Touch 的 EX/强制星形扩展、非 Touch 家族的烟花修饰。Wifi 不能组成连接 Slide。连接轨只能逐段写时长，或在最后写总时长；末尾总时长括号支持 BPM 与 `##` 等待参数，逐段模式的后续段重新指定等待参数仍会被拒绝。

## 音符生命周期

`PlaybackSession.Advance(musicSeconds)` 使用音频位置，按绝对事件时间推进。大幅跨帧与逐帧推进得到相同事件序列；同一时间反复调用不重复命中；重开显式清空状态。音符 ID 对应已排序谱面的位置。

Auto Play 的 Tap/Touch 在目标时刻 Perfect；Hold/TouchHold 在头部发 Started、尾部 Perfect；Slide 在开始移动时 Started、终点 Perfect。Slide 星头是单独的 Tap，轨迹是另一个计数实体。

算法模式使用带时间戳的输入，经固定逻辑采样更新全局区域状态，再判定头部、持续区间或 Slide 区队列。各档判定统一驱动达成率、DX 分数、Combo、画面和声音；未输入的音符由状态机自然 Miss，构造会话时不预定结果。具体通道、同刻顺序、输入消费、窗口、长音符及组行为见[输入指南](INPUT-REPLAY.md)与[机制溯源](../research/upstream-gameplay.md)。

Tap 与 Touch 的有效延后窗口闭合于目标后 150 ms 和 300 ms，Slide 超时严格晚于谱面尾后 600 ms。首次越过窗口的逻辑采样才结算；Hold/TouchHold 等待尾部和头窗结束。Slide 最后只差尾区时可判 Late Good，其余未完成为 Miss；不能把期限本身当作预定等级。

## 入场运动和流速

`NoteMotion.cs` 以音乐时间直接求位置。外圈与 Touch 的流速设置独立，默认都是 7.5；调整流速不会改变音符命中时刻或音乐速度。逻辑判定圈半径为 4.8；绘制使用归一化半径。

- **Tap/星头**：`GamePlayManager.cs:917` 将面板流速转换为 `107.25 / (71.4184491 × (speed + 0.9975)^(-0.985558604))`。`TapDrop.cs:185` 的径向距离为 `4.8 + (time-hit)×velocity`，默认缩放为 `1 + 0.265×(distance-1.225)`。音符先在半径 1.225 原地从 0 放大到 1，再向判定圈移动；不是从中心平移出来。引导线在缩放超过 0.3 时出现。未命中的 Tap 可以越过判定圈再 Miss。
- **Hold**：`HoldDrop.cs:262` 使用相同头部运动，尾部时间减去 Hold 时长。尾巴未出现时固定在半径 1.225，头到线后保持在 4.8，尾巴继续向外追上；出现的尾端点与三段拉伸由渲染层处理。不是把头尾同时线性缩放，也不是把整个 Hold 固定在线上。
- **Touch/TouchHold**：`TouchDrop.cs:135,383` 与 `TouchHoldDrop.cs:357`：整段入场时间为 `3.209385682×speed^(-0.9549621752)`；前 20% 扇片淡入，后 80% 以原指数曲线收拢。中心点定在传感器位置，扇片从距局部中心 `0.226+0.4` 逐渐收至 `0.226`，整个物件不沿外圈移动。Touch 点不跟随扇片透明度。坐标采用 `NoteHelper.GetTouchAreaPosition` 的实际参数 A=4.0、B=2.2、D=4.1、E=3.1，D/E 偏转半个键位。
- **Slide**：`SlideDrop.cs:141` 与 `SlideBase.SlideBarFadeIn`：轨道开始淡入为 `headTime-3.926913/velocity`；渐变最长 0.2 s，普通轨先停在 0.5 透明度，Wifi 为 1；到 `headTime-0.05` 全亮。`SingleSlideBase.cs:57` 的移动星在头部时刻出现，等待期间淡入并由 0.5 倍长到 1.5 倍，然后沿轨移动。轨道的出现时间、星头的出现时间、开始滑动时间是三个不同值。
- **星头旋转**：真实 Slide 按 `NoteLoader.UpdateStarRotateSpeed` 的轨段数量/移动时长计算并限幅，`$$` 为 Unity -440°/s，`$` 不旋转。轨段数量取该上游版本标准 prefab 的实际子对象计数；代码按屏幕坐标转换旋转正负号。

应用从最早入场时刻之前开始预卷，因此音频 0 秒和负 `first` 附近的第一颗音符也能完整入场。

## Slide 沿轨速度与采样

移动进度为 `(音乐时间 - SlideStartTime) / Duration`，限制在 0 到 1。出发时刻取星头时间加谱面等待，移动时长取对应括号；面板的 Tap/Touch 流速不参与沿轨位置计算。比如 `(120)1-3-7[0.2##1]` 在星头后 0.2 秒出发，再用 1 秒走完整条连接轨；`[240#4:1]` 则等待 0.25 秒、移动 0.25 秒。

普通 Slide 使用 41 种 prefab 的 1,222 个箭头采样点，加上起终键位，按 `SingleSlideBase` 的等索引方式插值：每对相邻点分得相同时间，点间距离不同会产生局部速度变化。坐标转换为判定圈半径 1，并按起点旋转；q/qq、z、反向圆弧及另一侧 V 按上游镜像方式变换。Wifi 按 `WifiDrop` 分别在三条直线上插值，三颗星同时出发和到达。

星星和轨道共用这套采样折线。`DistanceProgress` 将等时进度换成已走弧长，供自动模式清除星星后方的轨道；它与 `PointAt` 的位置一致。采样数据生成保留 12 位有效数字，相对原 prefab 数值的最大归一化舍入误差为 `4.715×10^-13`。轨道箭头的显示间距、旋转和皮肤合成仍由 Avalonia 渲染层处理，未复用 Unity 渲染系统。

连接轨仅指定总时长时，按上述版本 prefab 子对象数量分配，而不是按等段时间分配。明确逐段写时长时保留谱面的每段时长。参考 dev 的 `NoteLoader.cs:1127` 当前无条件重分配总时间，连逐段模式也执行；本版没有复刻这一行为。所有段的星头等待与总完成时间仍保持谱面值。

`CoreSlideSpeedTests` 覆盖总时长括号的等待与 BPM、逐段速度、等时采样的末段速度和折角位置、轨道消退、圆弧与 Wifi 到达时刻。

## 用户提供的四份 ZIP 验证

未把用户音乐、图片或完整谱面加入发布内容。下表为用户本地 `11160.zip`、`11377.zip`、`226.zip`、`239.zip` 中全部 18 个难度的解析结果。时长是谱面结束时间，不是 MP3 文件长度。计数包含独立的 Slide 星头和轨迹。

| ZIP / 歌曲 | 难度 | 级别 | 音符数 | 谱面结束秒 |
|---|---|---:|---:|---:|
| 11160 · Mutation | Basic | 5.0 | 221 | 139.742 |
| 11160 · Mutation | Advanced | 8.5 | 375 | 139.355 |
| 11160 · Mutation | Expert | 12.5 | 647 | 139.548 |
| 11160 · Mutation | Master | 14.0 | 991 | 139.403 |
| 11377 · パラマウント☆ショータイム！！ | Basic | 5.0 | 207 | 130.000 |
| 11377 · パラマウント☆ショータイム！！ | Advanced | 8.0 | 406 | 130.811 |
| 11377 · パラマウント☆ショータイム！！ | Expert | 11.7 | 673 | 130.000 |
| 11377 · パラマウント☆ショータイム！！ | Master | 14.0 | 946 | 129.865 |
| 226 · Blew Moon | Basic | 5.0 | 208 | 120.333 |
| 226 · Blew Moon | Advanced | 9.2 | 485 | 119.833 |
| 226 · Blew Moon | Expert | 12.5 | 495 | 119.500 |
| 226 · Blew Moon | Master | 13.4 | 608 | 120.167 |
| 226 · Blew Moon | Re:Master | 13.4 | 787 | 120.167 |
| 239 · System “Z” | Basic | 6.0 | 142 | 110.102 |
| 239 · System “Z” | Advanced | 9.1 | 249 | 110.306 |
| 239 · System “Z” | Expert | 12.8 | 507 | 110.306 |
| 239 · System “Z” | Master | 14.4 | 777 | 110.306 |
| 239 · System “Z” | Re:Master | 14.2 | 753 | 110.204 |

0.1.1 的独立解析记录：SimaiSharp 对同一输入解析，18 个难度的计数全部一致。按顺序逐个比较开始和结束时间，最大差为 **0.001169 秒**；SimaiSharp 使用 float 累计，本版使用 double。该对照核验了解析与总时间，不替代视觉检查，也没有把 SimaiSharp 的已知语义差异当成正确性标准。

0.2.0 回归仍对每个难度以 73 Hz 调用推进，内部按固定逻辑采样模拟完整两种模式：Auto 全部 Perfect，算法无输入全部 Miss，剩余均为 0。另有 BPM/first、等待与连接时长、E 区与结束符、Break/EX 归属、Touch EACH、未知语法拒绝、所有轨迹连续性、跳帧/暂停/重开，以及上述上游运动公式的针对性测试。随项目附带的原创 Demo 两难度也经过解析检查。

运行 `dotnet test tests/MaiPlay.Tests/MaiPlay.Tests.csproj -c Release --filter FullyQualifiedName~Core`。可选用户验收目录默认为 `.reference/acceptance/{id}/maidata.txt`，也可用 `MAIPLAY_CHART_FIXTURES` 指向同样结构的父目录；不存在该目录时不运行受版权素材的附加检查。

## 官方曲库

官方 MA2 转为 Simai 后沿用同一播放器和判定路径。版本分类、转换命令、非中心 Touch Hold 和整库验收结果见[官方谱面转换与验收](OFFICIAL-CHARTS.md)。
