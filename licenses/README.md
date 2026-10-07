# 第三方许可证文本

这些文件保留依赖的原始许可证和通知。MaiPlay 0.2.0 的 GPL-3.0 许可见根目录 `LICENSE`；本目录的 MajdataPlay 原许可证及归属须一并保留。来源对照 `src/MaiPlay/obj/project.assets.json` 的实际 NuGet 解析版本；收集日期为 2026-10-07。

| 组件与版本 | 本目录文件 | 来源 |
| --- | --- | --- |
| MajdataPlay 判定代码、区表、路径与传感多边形，`dc19722d602f099131d93b37091624ad30150ad1` | `MajdataPlay-GPL-3.0.txt` | [上游同一提交的 LICENSE](https://github.com/TeamMajdata/MajdataPlay/blob/dc19722d602f099131d93b37091624ad30150ad1/LICENSE) 原文；路径数据来源与提取方式见 `docs/CHART-COMPATIBILITY.md` |
| Avalonia 及 Desktop / Native / FreeDesktop / Remote.Protocol / Skia / Themes.Fluent / Win32 / X11，11.3.22 | `Avalonia-LICENSE.md`、`Avalonia-NOTICE.md` | 官方仓库，[许可](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/licence.md)、[通知](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/NOTICE.md)；提交与 NuGet 元数据一致 |
| NAudio / Asio / Core / Midi / Wasapi / WinMM，2.2.1 | `NAudio-LICENSE.txt` | 本地 NuGet `naudio/2.2.1/license.txt` 原文 |
| NAudio.Vorbis 1.5.0 | `NAudio.Vorbis-LICENSE.txt` | 官方仓库 [v1.5.0 的 LICENSE](https://github.com/naudio/Vorbis/blob/adb0443f6a3e87e29fdd0e592efa57396f014832/LICENSE) |
| NVorbis 0.10.4 | `NVorbis-LICENSE.txt` | 本地 NuGet `nvorbis/0.10.4/LICENSE` 原文 |
| SkiaSharp 及 NativeAssets，2.88.9 | `SkiaSharp-LICENSE.txt`、`SkiaSharp-Native-THIRD-PARTY-NOTICES.txt` | 本地 NuGet `skiasharp/2.88.9/LICENSE.txt`、`skiasharp.nativeassets.win32/2.88.9/THIRD-PARTY-NOTICES.txt` 原文 |
| HarfBuzzSharp 及 NativeAssets，8.3.1.1 | `HarfBuzzSharp-LICENSE.txt`、`HarfBuzzSharp-Native-THIRD-PARTY-NOTICES.txt` | 本地 NuGet `harfbuzzsharp/8.3.1.1/LICENSE.txt`、`harfbuzzsharp.nativeassets.win32/8.3.1.1/THIRD-PARTY-NOTICES.txt` 原文 |
| MicroCom.Runtime 0.11.0 | `MicroCom-LICENSE.txt` | NuGet 标明 MIT 但没有嵌入文本；取自[官方仓库](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE)，保留 Nikita Tsukanov 署名 |
| Tmds.DBus.Protocol 0.21.3 | `Tmds.DBus-COPYING.txt` | [官方 COPYING](https://github.com/tmds/Tmds.DBus/blob/8cb04f66c330b64244e996ff68f685f27f7381a0/COPYING)，提交与 NuGet 元数据一致 |
| Avalonia.Angle.Windows.Natives 2.1.25547.20250602 | `ANGLE-LICENSE.txt` | 本地 NuGet 同版本 `LICENSE` 原文；ANGLE 源码提交为 `cb8b4e1307a9d8f5ff56b8c5973bea4158ffead8` |
| ANGLE 内使用的 xxHash | `ANGLE-xxHash-LICENSE.txt` | [上述 ANGLE 提交内的许可证](https://github.com/AvaloniaUI/angle/blob/cb8b4e1307a9d8f5ff56b8c5973bea4158ffead8/src/common/third_party/xxhash/LICENSE) |
| ANGLE 的 ASTC Encoder、RapidJSON、Abseil、zlib 源码依赖 | `ANGLE-ASTC-Encoder-LICENSE.txt`、`ANGLE-RapidJSON-LICENSE.txt`、`ANGLE-Abseil-LICENSE.txt`、`ANGLE-zlib-LICENSE.txt` | 按上述 ANGLE 提交的 [DEPS](https://github.com/AvaloniaUI/angle/blob/cb8b4e1307a9d8f5ff56b8c5973bea4158ffead8/DEPS) 固定版本，原文来源见下文 |
| .NET Runtime 10.0.5 | `DotNet-Runtime-LICENSE.txt`、`DotNet-Runtime-THIRD-PARTY-NOTICES.txt` | 本地 `microsoft.netcore.app.runtime.win-x64/10.0.5` 包内原文；正文与官方 [LICENSE.TXT](https://github.com/dotnet/runtime/blob/v10.0.5/LICENSE.TXT)、[THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/runtime/blob/v10.0.5/THIRD-PARTY-NOTICES.TXT) 一致，保留包内 Windows 换行 |

ANGLE 的附加依赖原文来自：

- [ASTC Encoder `2319d9c4…` / LICENSE.txt](https://github.com/ARM-software/astc-encoder/blob/2319d9c4d4af53a7fc7c52985e264ce6e8a02a9b/LICENSE.txt)
- [RapidJSON `781a4e66…` / license.txt](https://github.com/Tencent/rapidjson/blob/781a4e667d84aeedbeb8184b7b62425ea66ec59f/license.txt)
- [Abseil `24012838…` / LICENSE](https://chromium.googlesource.com/chromium/src/third_party/abseil-cpp/+/2401283880a451e9ea10787920117fb67695abb4/LICENSE)
- [zlib `e8ff3f35…` / LICENSE](https://chromium.googlesource.com/chromium/src/third_party/zlib/+/e8ff3f35551275330696606d7fef3a2a0f73a831/LICENSE)

SkiaSharp 与 HarfBuzzSharp 的完整原生依赖通知在当前包中相同，仍分别保留来源；已核对 Win32、Linux、macOS、WebAssembly 包内通知的 SHA-256 均为 `21504C46C4C58AA64C1055BD2DCBC5F9A136B4B8C412ED3CC6740E22C5B127F5`。这些长通知包含各自携带的第三方组件许可，不能只保留顶层 MIT 文本。

本目录应随发布包一起保留；自包含发布还应保留运行时发布产物自带的许可证/通知。升级依赖或运行时后，应重新核对版本与文本。Avalonia.BuildServices 和测试依赖只参与构建/测试，没有作为应用运行组件列入此清单。参考验收用的歌曲、皮肤、上游 SFX 不属于发布资产。
