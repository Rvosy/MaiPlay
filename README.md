# MaiPlay

Windows 本地 Simai 游玩器。C# / .NET 10、Avalonia 绘制、NAudio 输出。开发版可为当前谱面求解双手手顺，也可回放时间戳输入日志；两者均通过共享输入状态计算判定。Auto Play 保留理想播放。

## 启动

需要 Windows 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。克隆仓库后，在仓库根目录运行：

```powershell
dotnet run --project src/MaiPlay
```

首次运行会自动还原 NuGet 依赖。源码已包含生成后的判定表、路径和传感多边形，构建不需要 Unity 或 `.reference/` 中的上游仓库。

本地已有的 0.2.0 发布目录为 `artifacts/MaiPlay-0.2.0`，运行其中的 `MaiPlay.exe`，并保留整个目录。该目录不随 Git 仓库上传。手顺求解算法属于当前开发版，尚未发布，需从源码运行；既有发布目录保持冻结。

打开歌曲目录、`maidata.txt` 或单曲 ZIP：

```powershell
dotnet run --project src/MaiPlay -- "C:\Songs\song.zip"
```

运行包含成功、偏早、偏晚、错区、长音符失败及 Wifi 的手写输入样例：

```powershell
dotnet run --project src/MaiPlay -- samples/Gameplay/maidata.txt --input samples/Gameplay/inputs.json
```

点击播放即可观察结果。样例谱面、音乐和默认音效均由本项目生成，没有捆绑商业歌曲或上游皮肤素材。[输入回放指南](docs/INPUT-REPLAY.md) 列出样例操作、JSON 格式、逻辑采样和离线验证命令。

## 使用

- 主界面选择歌曲、难度和模式，显示达成率、DX 分数、判定分布和 Combo；谱面提供数值定数时，同时显示当前 Rating 与上限。切换歌曲、难度或模式会重新开始。
- 空格播放或暂停，R 重开。时间轴左键拖动定位、滚轮缩放、右键拖动平移；也可用「−」「+」「全曲」。跳转后保持暂停。
- 「设置」集中显示 Tap/Touch 流速、皮肤导入与预览、音乐/音效音量及输入诊断。打开时暂停，关闭后仍暂停，由播放按钮继续。
- 选择歌曲和难度后，点击「求解手顺」，再播放查看双手。未解音符保留缺口，已生成的部分仍可播放；位置列表和时间轴红线可定位问题。也可点击「求解示例」。
- 算法游玩使用已生成手顺或已加载日志；无输入时自然 Miss。日志加载或清空会重开。Auto Play 的理想完成不能证明实际输入或双手可执行。
- 含 Break 的谱面达成率从 101% 开始，其余从 100% 开始，实际判定失分后扣减。[计分契约](docs/specs/achievement.md) 说明权重及子档。
- 皮肤支持 MajdataPlay 原 ZIP 和解压目录，音效从独立 SFX 目录加载。Tap/Touch 流速只改变入场显示，不改变判定或 Slide 沿轨时长。

设置保存在 `%LOCALAPPDATA%\MaiPlay\settings.json`；歌曲 ZIP 缓存在该目录的 `ImportedSongs`。皮肤、音效和输入日志保留原路径引用。第一颗音符提前入场时，应用添加音频预卷，音乐时间可为负。

## 验证与兼容

```powershell
dotnet build MaiPlay.slnx -c Release
dotnet test tests/MaiPlay.Tests -c Release
dotnet run --project tests/MaiPlay.GameplayHarness -- samples/Gameplay/maidata.txt samples/Gameplay/inputs.json
dotnet run --project tests/MaiPlay.AudioSmoke
```

- [0.2.0 验收记录](docs/GAMEPLAY-VALIDATION.md)
- [谱面、时序与沿轨运动](docs/CHART-COMPATIBILITY.md)
- [官方谱面转换、版本分类与整库验收](docs/OFFICIAL-CHARTS.md)
- [皮肤兼容](docs/SKIN-COMPATIBILITY.md)、[音频](docs/AUDIO.md)
- [上游机制溯源与差异](research/upstream-gameplay.md)
- [文档入口](docs/README.md)

本版不实现 Classic、Mine、自定义轨迹、真人设备、联网和曲库管理。未支持的语法明确报错。手顺求解的范围、验收曲目和未通过片段见[输入回放指南](docs/INPUT-REPLAY.md)。接触模型使用上游传感多边形与独立的按钮近似，未标定实物尺寸或音频硬件延迟；Avalonia 显示也不复刻 Unity 的全部 Shader 与粒子。

## 源码与许可

判定、区队列、传感多边形及标准 Slide 路径直接复用或适配固定 MajdataPlay 源码/数据。MaiPlay 0.2.0 源码按 [GPL-3.0](LICENSE) 提供，发布时同时保留对应源码包。原作者、固定提交和第三方依赖许可见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md)。皮肤、上游 SFX 与用户歌曲不随发布包分发。

## 仓库目录

| 目录 | 内容 |
| --- | --- |
| `src/MaiPlay.Core` | 谱面解析、判定、计分与手顺规划 |
| `src/MaiPlay` | 桌面界面、绘制和音频输出 |
| `tests` | 行为测试、输入回放 Harness 和音频设备检查 |
| `samples` | 原创谱面、合成音乐和输入日志 |
| `tools` | 上游数据提取与官方谱面转换工具 |
| `docs` | 使用指南、行为契约、架构决策和验收记录 |
| `research` | 上游机制溯源与研究记录 |
| `licenses` | 第三方许可证和归属通知 |

`.reference/` 保存本机参考代码、歌曲与皮肤，`artifacts/` 保存构建、发布及验证产物；两者均由 Git 忽略。文档中的历史截图和验证报告路径指向这些本地目录，克隆仓库不会下载对应文件。源码构建和原创样例可独立使用；依赖本地参考素材的附加验证见各项指南。
