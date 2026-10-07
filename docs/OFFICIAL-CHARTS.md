# 官方谱面转换与验收

MaiPlay 使用 Simai 歌曲目录：`maidata.txt` 保存各难度，`track.ogg` 保存音乐。官方 MA2 先通过本地工具转换，播放器不直接读取游戏资源包。

## 本机曲库

已转换的歌曲位于 `.reference/charts/game-extracted/`，按 `Music.xml` 的 `AddVersion/id` 和 `AddVersion/str` 分类，歌曲目录包含编号和标题。例如：

```text
24_PRiSMPLUS/
  011820_Xaleid◆scopiX/
    maidata.txt
    track.ogg
```

同一版本编号的名称缺失时，使用其他歌曲记录中该编号的名称。歌曲的归属版本不等于某个追加难度的上线版本。标准谱和 DX 谱保留各自的歌曲编号。

这次本机 `SDEZ166_nobga` 数据共转换 27 个版本、1,512 个普通歌曲条目、6,190 个难度。宴会谱不纳入曲库。`report.json` 记录每个转换结果、版本、原文件路径和失败原因；`audio-report.json` 记录音乐来源。全部素材位于 Git 忽略的 `.reference/` 内，不进入源码或发布目录。

在播放器中用「打开歌曲目录」选择具体歌曲目录。也可运行：

```powershell
dotnet run --project src/MaiPlay -- ".reference/charts/game-extracted/24_PRiSMPLUS/011820_Xaleid◆scopiX"
```

## 从游戏文件转换

本机资源入口为 `C:\SEGA\SDEZ166_nobga\Package\Sinmai_Data\StreamingAssets`。`A000/music/musicXXXXXX` 下的 `Music.xml` 描述歌曲和难度，同目录的 `.ma2` 保存音符；`A003` 是另一个资源层。音乐在资源层的 `SoundData` 中，通过 `Music.xml` 的 `cueName/id` 关联。不能直接用 DX 谱的歌曲编号猜音乐编号。

转换需要 Python 3.11 或更高版本、PATH 中的 FFmpeg，以及单独下载的 [vgmstream CLI](https://github.com/vgmstream/vgmstream/releases)。工具不修改游戏源文件。

```powershell
python tools/convert_official_charts.py `
  "C:\SEGA\SDEZ166_nobga\Package\Sinmai_Data\StreamingAssets" `
  ".reference/charts/new-import" `
  --decoder ".reference/tools/vgmstream/vgmstream-cli.exe"
```

省略 `--decoder` 只转换谱面。输出目录必须是新目录；`--update` 允许更新已有转换目录中的谱面，不负责清理已删除或变更归属的旧歌曲。多个资源层有相同歌曲编号时，按目录名称排序后选用最后一层。

转换保留 BPM 变化后的实际秒数、Hold 时长、Slide 等待与逐段时长、Break/EX、Touch 区域和烟花标记。生成的 Simai 使用固定秒数间隔，避免节拍细分取整。未知音符命令会写入错误记录并使工具返回失败，不静默丢弃。MA2 的节拍器、场景效果和触摸外观尺寸字段不进入 Simai；这不包括音符位置、时长或判定修饰。

音乐从 AWB 解码后转为 OGG，并完整解码检查。相同音乐只转换一次；歌曲目录中的 `track.ogg` 与 `audio/` 缓存使用本地硬链接，共享磁盘内容。单独复制歌曲目录时，它仍是一份普通音频文件。

## 已验证的范围

这次转换的 6,190 个难度、2,812,385 个音符全部通过 MaiPlay 加载、Auto Play 完整结算及无输入完整 Miss 结算。未逐首人工检查音画，也未证明全部谱面上的任意输入都与机台判定逐帧一致。

`Xaleid◆scopiX` 的 Re:Master 有 46 个 Touch Hold，其中 44 个位于非中心区域：A 区 4 个、B 区 26 个、D 区 4 个、E 区 10 个。当前 Simai 解析、绘制和判定已经保留这些位置。`OfficialChartTests` 覆盖全部 32 个非中心位置的正确区域输入、误触中心、提前松开及重新按住；`SkinTests.NonCentralTouchHoldsRenderWithBuiltinAndAvailableSkins` 提供内置外观及本地可用皮肤的渲染检查。

整库验收需显式指定本地转换目录：

```powershell
$env:MAIPLAY_OFFICIAL_CHARTS = "$PWD\.reference\charts\game-extracted"
dotnet test tests/MaiPlay.Tests --filter Category=OfficialLibrary
```

结果写入曲库的 `playback-validation.json`。未设置环境变量时，日常测试不加载整套游戏素材。这里的兼容结论限于上述本地数据，不将未取得的新版本谱面视为已经验证。
