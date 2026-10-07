# MajdataPlay 皮肤兼容说明

本版直接读取原 ZIP 或解压根目录。文件夹须直接含 `TapSkins/tap.png`，ZIP 内容不落盘，不要求格式转换。导入的是路径引用；移动或删除原文件后需要重新导入。应用设置保存已导入路径及上次选择。音效独立于皮肤。

核对基准（2026-10-07）：

- [MajdataPlay dev](https://github.com/TeamMajdata/MajdataPlay)，commit `dc19722d602f099131d93b37091624ad30150ad1`。
- [MajdataPlay-Skins](https://github.com/TeamMajdata/MajdataPlay-Skins)，commit `f1ea03c5ea785719c83351c64141ce379c48f5d5`。
- 资源加载：`CustomSkin.cs`、`SkinManager.cs`；组合/状态：`TapDrop`、`HoldDrop`、`TouchDrop`、`TouchHoldDrop`、`SlideDrop`、`WifiDrop`、`SlideOK`、`TouchBorder`；坐标和对象变换：`NoteHelper` 及 `Assets/Prefabs/Game`。

## 已实现的资源行为

| 资源 | 播放时的组合与状态 |
| --- | --- |
| Tap / Star | Normal、Each、Break 按 Break 优先选择；EX 为额外图层，普通 Tap/Hold 粉色、Each/Break 黄色、普通 Star 白色调制；双星单独选图。 |
| Hold | Normal / Each / Break、按住 On、松开 Off；本体和 EX 使用上下各 58 源像素切片，不拉长端帽；Guide 与尾标记分别绘制。 |
| Slide | 原始箭头按轨迹方向和弧长排列，轨道在星星之前；自动模式随时间进度清除，算法模式随实际区队列推进清除，未完成部分保留至最终判定。 |
| Wifi | 11 张不同宽度的原始切片，采用上游 prefab 的中心位置、水平镜像和起点旋转；三颗引导星独立沿三分支运动。 |
| Touch | 四扇片、独立中心点、到达时出现的 just 边框；同一传感区排队的第二、第三个 Touch 决定两层额外边框的颜色。 |
| TouchHold | 四张不同颜色的扇片分别旋转，中心点复用 Touch；按住/松开边框采用径向进度裁切，边框位于扇片下方。 |
| 判定 | 普通及 Break 分数、Break shine；SlideOK 根据末段形状与方向选择曲线/直线/扇形的左右或上下素材，布局坐标来自对应 prefab。 |
| Guide / Outline | 原文件名、原比例；同时线、音符 Guide 和 Hold 尾标记各自保留层级。GeometryCrystal 的外围交叉白线就是原始 outline 设计。 |

所有普通 Sprite 使用 100 PPU、中心 pivot，保留源图片宽高比。Gamble 的 180px Tap 不会被强制压成其他皮肤的 122px。`SubBackgourd.png` 保留上游拼写；它与 `now_loading.png` 可在素材预览中查看，MVP 不设置上游副屏及加载动画场景。

缺省行为只有 `hold_on → hold`、`hold_each_on → hold_each`、`hold_break_on → hold_break` 三条上游回退。外部皮肤的其他缺图保持空白并在必要资源检查中报告，绝不混入另一套皮肤。未选择外部皮肤时，应用使用自己的内置几何图形。

素材预览列出包中全部 PNG。输入回放可实际产生 Perfect 子档、Great、Good、Fast/Late 与 Miss，并选择相应素材；TooFast 仍只用于查看，不启用 Mine 等扩展规则。

运动使用 `NoteMotion` 按音频时钟直接求值。Tap/Hold/星头先在半径 1.225 处缩放，再移向 4.8 判定圈；Hold 头到圈停留、尾展开后向外收束。Touch/TouchHold 在目标传感区先用 20% 出现时间淡入扇片，再用 80% 时间按上游指数函数收拢；中心点不随扇片淡入，到达后才出现判定边框。Slide 轨道与引导星有独立淡入时机，普通轨道先到半透明再全亮，Wifi 直接渐至全亮；真实星头角速度根据上游轨迹段数量与谱面时长计算。Tap 流速与 Touch 流速各自默认 7.5，并分别影响对应家族。内置星形为五角星；导入皮肤的星形按原素材显示。

## 验证方式

五包均由原仓库取得：Ex Lite-星鹿（166 PNG）、Gamble-classic-Omeanga（170）、Gamble-modern-Omeanga（170）、GeometryCrystal-Omeanga（166）、Light2-星鹿（160）。自动检查覆盖全部 PNG 解码、关键数组尾项、ZIP 与解压目录资源一致性、缺省回退、Break/Each/EX 选图，以及两个模式的组合绘制。另检查 ZIP 越界路径、损坏 PNG 的可读错误。

```powershell
git clone --depth 1 https://github.com/TeamMajdata/MajdataPlay-Skins.git .reference/MajdataPlay-Skins
dotnet test tests/MaiPlay.Tests -c Release --filter FullyQualifiedName~SkinTests
```

存在参考目录时，测试在 `artifacts/gameplay-validation/skins` 输出两个模式的 40 张组合帧；`motion` 输出流速 3 / 7.5 / 12 的 15 张出现阶段帧。若本地还有用户提供的 `.reference/acceptance`，另对 Mutation、パラマウント☆ショータイム！！、Blew Moon、System “Z” 四首 Master × 五包生成 20 张代表性帧。0.2.0 新增手写输入在矢量与五套皮肤下的过程/判定帧，输出至 `artifacts/gameplay-validation/input-feedback`。实际检查范围见[本版验收记录](GAMEPLAY-VALIDATION.md)，旧版图像仍保留在原目录。参考包和验收歌曲不进入源码或发布包；没有参考目录时，相关分支不执行。截图测试验证可解码、可组合、可实际绘制，不是与 Unity 的逐像素回归比较。

## 仍然存在的差异

Avalonia 使用原素材合成，不运行 Unity Shader、Animator、ParticleSystem。Break 的 GPU 发光/闪烁、判定动画曲线和原粒子并未逐像素复刻，打击反馈使用简洁的圆环/烟花。普通 Slide 的星星和轨道共用上游 prefab 采样折线；箭头显示间距与旋转仍由本项目绘制，详见 [Slide 沿轨速度与采样](CHART-COMPATIBILITY.md#slide-沿轨速度与采样)。

本版没有主仓库的 Mine 扩展、全局无轨/无引导星显示选项或 Classic 游戏规则。经典命名的皮肤也使用本版统一播放规则。未支持的谱面语法由解析器明确拒绝，不能把“已导入一套皮肤”理解为支持 MajdataPlay 的全部玩法扩展。

## 素材与许可

皮肤仓库在核对版本未附统一授权许可证。提交要求“无侵权风险、可正常游玩”不是对所有下游项目的版权授权。五套素材仅在本地 `.reference` 作用户要求的兼容验收，不随 MaiPlay 分发。用户应自行取得使用、展示或再分发许可，作者名保留在原包名称中。

MajdataPlay 主仓库许可证适用于其代码，不能自动套用于这些皮肤。源码参考与第三方依赖的声明另见项目第三方说明；本兼容层为 Avalonia 实现，不执行皮肤内任何脚本。
