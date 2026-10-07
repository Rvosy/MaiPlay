# 音频与音效

音乐使用 NAudio 2.2.1 的 Windows `WaveOutEvent`，OGG 使用 NAudio.Vorbis 1.5.0 解码。WAV、MP3、OGG 已进行实际解码与静音设备播放检查；其他格式取决于系统解码器，失败会显示在界面中。

`AudioPlayer.Position` 读取输出设备已经消费的 PCM 字节数，而不是解码器读到的位置或 UI 帧时间。音乐以原采样率播放，经音量控制后转成 16-bit PCM。`Load(path, minimumDuration, leadIn)` 可在音乐前增加静音，`StartPosition` 为 `-leadIn`，让零秒或负 `first` 的音符完整入场；位置跨过零后开始音乐，`Duration` 仍表示原音乐坐标的结尾。暂停先暂停设备，再保存位置；重开重建输出设备并把解码器归零、恢复 `StartPosition`，丢弃排队的旧声音。音乐结尾也可补静音至 `minimumDuration`，确保末尾 Hold、Slide、Miss 期限仍能完成。设备失败通过 `PlaybackFailed` 报告。没有做设备延迟校准或变速。

`Seek(time)` 丢弃输出设备的旧缓冲，定位解码器，再以目标音乐时间作为设备时钟原点。负时间只补剩余预卷，音乐文件结束后的目标位置使用静音尾；定位到整段结尾时保持停止。界面跳转保持暂停，玩法状态重建和音效恢复见[播放界面契约](specs/player-settings.md)。

MP3 的界面时长采用 Windows 解码器报告的 PCM 长度，可能包含一个编码帧的填充。验收用的四首 MP3 与 ffprobe 容器时长均相差 24 ms；这不是 UI 累积漂移。此版没有提供硬件/编码延迟校准，不宣称与机台经过校准的延迟完全相同。

音乐与音效使用独立输出、独立音量。音效在 44.1 kHz 双声道混音器中播放，导入的单声道音效会复制到双声道，其他采样率会重采样。同一逻辑采样时刻的相同声音合并播放，避免同时押产生不必要的叠音。音效由谱面时间推进产生的事件触发，因此可能有一帧调度误差和输出缓冲延迟；画面时间始终以音乐设备为准。

`WaveOutput` 统一处理设备关闭顺序：先停止，等 NAudio 工作线程发出完成事件，再释放原生缓冲区，避免快速重开或换音效时出现 `WaveHeaderUnprepared`。完成事件在音频工作线程处理，不等待 UI 消息队列；界面的错误提示再由应用调度回 UI。

## 音效来源与导入

默认声音是 MaiPlay 代码现场生成的原创简短合成音，不是机台音色，也不是 Majdata 音频的重分发。皮肤 ZIP 不包含音效，切换皮肤不更改音效。

界面可直接选择 MajdataPlay 的 `Assets/StreamingAssets/SFX` 目录。文件名保持上游原名，缺失的文件使用内置合成音并在界面列出；损坏文件使此次导入失败，保留之前的声音库。音效文件限单/双声道、每个不超过 60 秒。用户负责提供有权使用的音频；本项目没有打包参考仓库里的声音。

## 事件语义

参考 MajdataPlay `dev` 提交 `dc19722d602f099131d93b37091624ad30150ad1` 的 `NoteAudioManager.cs` 及 `TapDrop.cs`、`HoldDrop.cs`、`TouchDrop.cs`、`TouchHoldDrop.cs`、`SlideBase.cs`、`WifiDrop.cs` 调用链。分档与触发分支已按 GPL-3.0 适配，源文件保留固定版本归属。

| 事件 | 声音 |
| --- | --- |
| 普通 Tap / 星头 | Perfect 各档 `tap_perfect.wav`，Great 各档 `tap_great.wav`，Good `tap_good.wav` |
| EX Tap / Hold 头 | `tap_ex.wav` |
| Break Tap / Hold 头 | `break_tap.wav`，仅 CP 叠加 `break.wav`（Break 优先于 EX） |
| Hold 头 / 尾 | 头按上述标记；尾按最终等级的普通 Tap 声音，不重放 Break/EX 头音 |
| 普通 Touch | `touch.wav`；Break Touch 按 Break Tap |
| TouchHold 头 / 持续 / 尾 | 真实头判只启动 riser；尾按最终等级的普通 Tap 声音。理想 Auto Play 保留上游头部 Tap 声音 |
| Firework Touch / TouchHold 的成功判定 | 额外 `touch_hanabi.wav`；真实 TouchHold 在尾判触发；理想 Auto Play 按上游调用在头尾触发 |
| Slide / Wifi 实际首次推进（Auto 为开始移动） | `slide.wav`；Break 轨迹使用 `slide_break_start.wav` |
| Break Slide / Wifi 完成 | 仅 CP 播放 `slide_break_slide.wav` + `break.wav` |
| 普通 Slide / Wifi 完成 | 没有额外音效 |
| 普通音符 Miss | 静音 |

`tap_miss.wav` 在当前上游用于 Mine 失败，并非普通 Miss 提示音；本版不支持 Mine，因此不会播放它。`answer.wav`、`answer_clock.wav`、`answer_mine.wav` 是独立的谱面引导/计时声，不代表命中；本版没有开启此类额外声音。`break_slide.wav` 不是当前 `NoteAudioManager` 的 Break Slide 完成声音，不能按名字猜映射。

算法模式由实际判定触发分档音效；无输入不发成功音。TouchHold 释放和恢复分别停止与恢复持续声，头未命中但体区有效按住也按实际体状态反馈。TouchHold 持续声会在完成、重开时停止，暂停时冻结、继续时恢复，多个同时进行的 TouchHold 共用一条持续声。外部 riser 遵循上游单次播放，按活动区间截断；内置原创持续音循环到活动区间结束。

## 验证

常规测试不要求连接声卡。可另外运行真实 Windows 音频设备 smoke，音量为零：

```powershell
dotnet run --project tests/MaiPlay.AudioSmoke
dotnet run --project tests/MaiPlay.AudioSmoke -- "D:\Songs\example\track.mp3" "D:\Songs\example\track.ogg" --sfx "D:\MajdataPlay\Assets\StreamingAssets\SFX"
```

它检查音乐前导静音与负时间、结尾静音尾、无 UI Tick 时设备时钟继续、暂停冻结、恢复、重开清队列、前后跳转、负时间与曲末定位、TouchHold 音效生命周期，以及可选文件的解码、时长与零起点。每次还执行 40 次音效快速 Reset 和 20 次音乐重开/暂停，覆盖设备工作线程关闭竞态。它验证设备运行和时钟，不把静音测试声称为听音验收。
