# 第三方组件与参考资料

MaiPlay 0.2.0 包含 MajdataPlay 的派生代码与数据，完整源码按 GPL-3.0 提供，原文见根目录 `LICENSE`。对应源码包包含生成后的区表、路径、多边形及其生成器；构建不需要 Unity。发布包须保留许可、归属和对应源码。

## 运行依赖

NuGet 依赖由各自作者按其许可证提供。主要依赖：

| 组件 | 版本 | 许可证 / 来源 |
| --- | --- | --- |
| Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent | 11.3.22 | MIT — https://github.com/AvaloniaUI/Avalonia |
| NAudio | 2.2.1 | MIT — https://github.com/naudio/NAudio |
| NAudio.Vorbis | 1.5.0 | MIT — https://github.com/naudio/Vorbis |
| NVorbis（传递依赖） | 由 NuGet 解析 | MIT — https://github.com/NVorbis/NVorbis |
| SkiaSharp（Avalonia 传递依赖） | 由 NuGet 解析 | MIT；Skia 另有第三方许可 — https://github.com/mono/SkiaSharp |
| .NET 运行时 | 10.0 | MIT 及其第三方通知 — https://github.com/dotnet/runtime |

精确的传递依赖版本可通过 `dotnet list src/MaiPlay package --include-transitive` 查看。发布目录包含依赖许可文件，测试依赖不随程序发布。

## 兼容性参考

- PaperPig/MaimaiData，提交 `30ca1c3cce9069051d7d3a244b6623c0a650aae4`，Apache-2.0。`RatingCalculator` 参考 `ConvertUtils.achievementToRating` 的分档系数和两个达成率边界，改为 decimal 百分比运算；B50 保留旧曲 35、新曲 15 的名额。版本分组采用本地库最新两个版本的约定。原许可证见 `licenses/MaimaiData-Apache-2.0.txt`。
- TeamMajdata/MajdataPlay 与其贡献者，`dev` 提交 `dc19722d602f099131d93b37091624ad30150ad1`，GPL-3.0。直接复用/适配范围包括 `NoteDrop/NoteLongDrop` 与各 Note 的判定分支、`NoteManager` 输入/消费规则、Touch 邻接/群组、41 个标准 Slide 区表与 Wifi、Slide 区状态转移和时间窗、标准 prefab 采样坐标、`TouchSensorMapper` 多边形、计分及声音/选图分支。移除了 Unity 生命周期、池和渲染依赖，保留所需规则与数据；原文件、符号、测试和差异见 [机制溯源](research/upstream-gameplay.md)。源文件头保留上游归属和固定提交；原始许可证另存 `licenses/MajdataPlay-GPL-3.0.txt`。
- TeamMajdata/MajdataPlay-Skins，当前五套 ZIP：Ex Lite-星鹿、Gamble-classic-Omeanga、Gamble-modern-Omeanga、GeometryCrystal-Omeanga、Light2-星鹿。作者由包名及仓库说明标识。它们仅作为本机兼容验收数据；仓库未给出足以据此将全部皮肤素材重新分发的统一许可证，不能套用 MajdataPlay 主仓库的代码许可。发布包不含这些皮肤。
- reflektone-games/SimaiSharp，MIT。用于独立对照谱面计数和时间；未作为运行依赖，也未复制其源码。

上游 SFX、用户提供的歌曲 ZIP 和背景图同样没有捆绑发布。用户从原路径自行加载这些资源。内置示例谱面、合成音乐、合成音效与矢量图形由本项目生成。

AstroDX 历史 core-dump `4cfd3a3ca7092fc1c910554677076429d6051e65` 未找到明确复制许可，仅作行为对照；没有复制其源码。
