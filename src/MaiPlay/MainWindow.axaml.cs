using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MaiPlay.Audio;
using MaiPlay.Core;
using MaiPlay.Rendering;
using MaiPlay.Skins;

namespace MaiPlay;

public sealed partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly AudioPlayer _audio = new();
    private readonly SoundEffects _sounds = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000d / 60) };
    private Song? _song;
    private PlaybackSession? _session;
    private SkinPack? _skin;
    private string? _musicPath;
    private InputLog? _inputLog;
    private HandPlan? _generatedPlan;
    private CancellationTokenSource? _planning;
    private bool _closed;
    private bool _ready, _loading, _audioReady, _wasPlaying;

    public MainWindow()
    {
        InitializeComponent();
        Progress.ScrubStarted += Pause;
        Progress.SeekRequested += Seek;
        _timer.Tick += (_, _) => Tick();
        _audio.PlaybackFailed += e => Dispatcher.UIThread.Post(() => AudioFailure(e));
        _sounds.PlaybackFailed += e => Dispatcher.UIThread.Post(() => SetStatus($"音效设备错误：{e.Message}", true));
        Opened += async (_, _) => await InitializeAsync();
        Closed += (_, _) => { _closed = true; _planning?.Cancel(); _timer.Stop(); SaveSettings(); _sounds.Dispose(); _audio.Dispose(); _skin?.Dispose(); };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private async Task InitializeAsync()
    {
        MusicSlider.Value = Math.Clamp(_settings.MusicVolume, 0, 1) * 100;
        SoundSlider.Value = Math.Clamp(_settings.SoundVolume, 0, 1) * 100;
        SoundToggle.IsChecked = _settings.SoundEnabled;
        TapSpeedInput.Value = (decimal)Math.Clamp(_settings.TapSpeed, .25, 20);
        TouchSpeedInput.Value = (decimal)Math.Clamp(_settings.TouchSpeed, .25, 20);
        SensorToggle.IsChecked = _settings.ShowSensors;
        Playfield.ShowSensors = _settings.ShowSensors;
        ContactHighlightToggle.IsChecked = Playfield.HighlightContacts = _settings.HighlightContacts;
        HandMarkerToggle.IsChecked = Playfield.ShowHandMarkers = _settings.ShowHandMarkers;
        HandTrailToggle.IsChecked = Playfield.ShowHandTrails = _settings.ShowHandTrails;
        RefreshSkinChoices();
        PreviewButton.IsEnabled = false;
        SkinHint.Text = "内置矢量样式";
        _ready = true;
        ApplyVolumes();
        ApplySpeeds();
        var startupWarnings = new List<string>();
        if (_settings.SelectedSkin is { } selected)
        {
            try { SetSkin(selected); }
            catch (Exception e) { startupWarnings.Add($"上次选择的皮肤无法读取：{e.Message}"); }
        }
        if (_settings.SoundDirectory is { } soundPath)
        {
            try { LoadSounds(soundPath); }
            catch (Exception e) { startupWarnings.Add($"上次的音效目录无法读取：{e.Message}"); }
        }
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string? songArgument = null;
        int? difficultyArgument = null;
        var inputPath = _settings.InputLogPath;
        var algorithm = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--input" && i + 1 < arguments.Length) { inputPath = arguments[++i]; algorithm = true; }
            else if (arguments[i] == "--difficulty" && i + 1 < arguments.Length)
            {
                if (int.TryParse(arguments[++i], out var difficulty) && difficulty is >= 1 and <= 7) difficultyArgument = difficulty;
                else startupWarnings.Add("--difficulty 需要 1～7 的难度编号。");
            }
            else if (arguments[i] == "--algorithm") algorithm = true;
            else if (!arguments[i].StartsWith('-') && songArgument is null) songArgument = arguments[i];
        }
        if (inputPath is not null)
        {
            try { _inputLog = InputLog.Load(inputPath); _settings.InputLogPath = inputPath; }
            catch (Exception e) { startupWarnings.Add($"输入日志无法读取：{e.Message}"); }
        }
        if (algorithm) ModePicker.SelectedIndex = 1;
        RefreshInputHint();
        var path = songArgument ?? _settings.LastSong;
        if (path != null && (File.Exists(path) || Directory.Exists(path))) await LoadSongAsync(path, difficultyArgument);
        else await LoadDemoAsync();
        if (startupWarnings.Count > 0) SetStatus(string.Join("\n", startupWarnings), true);
        _timer.Start();
    }

    private async Task LoadSongAsync(string path, int? difficulty = null)
    {
        if (_loading) return;
        _planning?.Cancel();
        _loading = true;
        Pause();
        PlayButton.IsEnabled = RestartButton.IsEnabled = false;
        SetStatus("正在读取谱面与音乐…");
        try
        {
            var source = path;
            var song = await Task.Run(() =>
            {
                if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    source = SongArchive.Import(path, Path.Combine(AppSettings.DataDirectory, "ImportedSongs"));
                return SimaiParser.LoadSong(source);
            });
            var choices = song.Charts.Select(x => new DifficultyChoice(x)).ToArray();
            var selected = difficulty is { } number
                ? choices.FirstOrDefault(x => x.Chart.Difficulty == number) ?? throw new InvalidDataException($"谱面没有难度 {number}。")
                : choices.LastOrDefault(x => x.Chart.Difficulty <= 5) ?? choices.LastOrDefault();
            _song = song;
            _musicPath = song.AudioPath;
            SongTitle.Text = song.Title;
            SongSubtitle.Text = string.IsNullOrWhiteSpace(song.Artist) ? "本地 Simai 谱面" : song.Artist;
            SongPath.Text = song.ChartPath;
            ToolTip.SetTip(SongPath, song.ChartPath);
            DifficultyPicker.ItemsSource = choices;
            DifficultyPicker.SelectedItem = selected;
            _settings.LastSong = source;
            ConfigureChart();
            SaveSettings();
        }
        catch (Exception e) { SetStatus($"无法打开谱面：{e.Message}", true); }
        finally { _loading = false; UpdateTransport(); }
    }

    private void ConfigureChart()
    {
        if (DifficultyPicker.SelectedItem is not DifficultyChoice choice) return;
        _planning?.Cancel();
        if (_generatedPlan?.Chart != choice.Chart) _generatedPlan = null;
        Pause();
        _sounds.Reset();
        _session = new PlaybackSession(choice.Chart, ModePicker.SelectedIndex == 1 ? PlayMode.Algorithm : PlayMode.AutoPlay,
            _generatedPlan?.Replay.Events ?? _inputLog?.Events);
        Playfield.Chart = choice.Chart;
        Playfield.Engine = _session;
        Playfield.HandReplay = _generatedPlan?.Replay ?? _inputLog?.HandReplay;
        Playfield.Time = 0;
        ChartDetails.Text = $"{choice.Chart.Notes.Count} 个音符  ·  {FormatTime(choice.Chart.Duration)}" +
                            (string.IsNullOrWhiteSpace(choice.Chart.Designer) ? "" : $"\n谱师：{choice.Chart.Designer}");
        _audioReady = false;
        if (_musicPath != null)
        {
            try
            {
                _audio.Load(_musicPath, _session.FinishTime + .8, LeadIn());
                _audioReady = true;
                SetStatus($"已加载 {choice.Chart.DifficultyName} {choice.Chart.Level} · {_song?.Charts.Count} 个难度 · {Path.GetFileName(_musicPath)}");
            }
            catch (Exception e) { SetStatus(e.Message, true); }
        }
        else SetStatus("已读取谱面，但目录中没有找到 track.mp3 / track.ogg / track.wav。点击「选择音乐」后播放。", true);
        UpdateFrame(_audioReady ? _audio.StartPosition : 0);
        Progress.SetRange(_audioReady ? _audio.StartPosition : 0, _audioReady ? _audio.Duration : choice.Chart.Duration);
        var issues = _generatedPlan is { } plan
            ? plan.Gaps.Select(g => new PlanIssue(g.NoteId, g.Time, $"未解 · {g.Reason}"))
                .Concat(plan.Errors.Where(e => !plan.Gaps.Any(g => g.NoteId == e.Note.Id))
                    .Select(e => new PlanIssue(e.Note.Id, e.Note.Kind == NoteKind.Slide ? e.Note.SlideStartTime : e.Note.Time,
                        $"{e.Note.Kind} · {JudgementDisplay.Label(e.Grade)}"))).OrderBy(i => i.Time).ToArray()
            : [];
        PlanIssuePicker.ItemsSource = issues;
        PlanIssuePicker.SelectedIndex = -1;
        PlanIssuePicker.IsVisible = issues.Length > 0;
        Progress.SetIssues(issues.Select(i => i.Time).ToArray());
        RefreshInputHint();
        UpdateTransport();
    }

    private void Tick()
    {
        if (_session == null || !_audioReady) return;
        var playing = _audio.IsPlaying;
        if (playing || _wasPlaying)
        {
            var time = _audio.Position;
            var events = _session.Advance(time);
            if (playing) _sounds.Handle(events);
            UpdateFrame(time);
            if (!playing) { _sounds.Reset(); UpdateTransport(); }
        }
        _wasPlaying = playing;
    }

    private void UpdateFrame(double time)
    {
        Playfield.Time = time;
        Playfield.InvalidateVisual();
        ComboText.Text = (_session?.Combo ?? 0).ToString();
        CompletedText.Text = (_session?.Completed ?? 0).ToString();
        MissText.Text = (_session?.Missed ?? 0).ToString();
        RemainingText.Text = (_session?.Remaining ?? 0).ToString();
        DxScoreText.Text = $"DX  {_session?.DXScore ?? 0} / {_session?.MaximumDXScore ?? 0}";
        RatingText.Text = _session is { } ratingSession && decimal.TryParse(ratingSession.Chart.Level,
            System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var constant)
            ? $"Rating  {RatingCalculator.Calculate(constant, ratingSession.Achievement.Percentage)} / {RatingCalculator.Calculate(constant, 101)} · 定数 {constant}"
            : "Rating —（缺少定数）";
        TimingSummary.Text = $"FAST {_session?.FastCount ?? 0} · LATE {_session?.LateCount ?? 0}";
        int Count(string family) => _session is null ? 0 : Enum.GetValues<AchievementGrade>()
            .Where(x => JudgementDisplay.Family(x) == family).Sum(x => _session.GradeCounts[(int)x]);
        GradeSummary.Text = $"CP {Count("CP")} · PERFECT {Count("PERFECT")}\nGREAT {Count("GREAT")} · GOOD {Count("GOOD")}";
        var duration = _audioReady ? _audio.Duration : _session?.Chart.Duration ?? 0;
        Progress.SetTime(time);
        TimeText.Text = $"{FormatTime(time)} / {FormatTime(duration)}";
        JudgmentText.Text = _session?.LastEvent is { } last
            ? $"{JudgementDisplay.Label(last.Grade)} {JudgementDisplay.Timing(last)} · {last.Note.Kind} · {FormatTime(last.Time)}"
            : "空格：播放 / 暂停 · R：重开";
        ToolTip.SetTip(JudgmentText, JudgmentText.Text);
        if (_audioReady && time >= _audio.Duration) PlaybackLabel.Text = "播放结束";
        else if (_audio.IsPlaying) PlaybackLabel.Text = time < 0 ? "准备中" : "播放中";
    }

    private void Play_Click(object? sender, RoutedEventArgs e)
    {
        if (!_audioReady || _session == null || _loading || _planning != null || SettingsPanel.IsVisible) return;
        try
        {
            if (_audio.IsPlaying) Pause();
            else
            {
                if (_audio.Position >= _audio.Duration) Restart(false);
                _sounds.Play();
                _audio.Play();
                _wasPlaying = true;
            }
            UpdateTransport();
        }
        catch (Exception ex) { AudioFailure(ex); }
    }

    private void Pause()
    {
        var wasPlaying = _audio.IsPlaying;
        _audio.Pause();
        if (wasPlaying && _session != null && _audioReady)
        {
            _sounds.Handle(_session.Advance(_audio.Position));
            UpdateFrame(_audio.Position);
        }
        _sounds.Pause();
        _wasPlaying = false;
        UpdateTransport();
    }

    private void Restart_Click(object? sender, RoutedEventArgs e)
    {
        try { Restart(_audio.IsPlaying); }
        catch (Exception ex) { AudioFailure(ex); }
    }

    private void Seek(double time)
    {
        if (!_audioReady || _session is null || _loading || _planning != null) return;
        try
        {
            Pause();
            _audio.Seek(time);
            _session.Seek(_audio.Position);
            _sounds.Reset();
            _sounds.Handle(_session.Chart.Notes.Where(n => n.Kind == NoteKind.TouchHold && _session.IsHolding(n))
                .Select(n => new PlaybackEvent(PlaybackEventKind.Held, n, _audio.Position)));
            UpdateFrame(_audio.Position);
            UpdateTransport();
        }
        catch (Exception ex) { AudioFailure(ex); }
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => Progress.Zoom(.5);
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => Progress.Zoom(2);
    private void FitTimeline_Click(object? sender, RoutedEventArgs e) => Progress.Fit();
    private void PlanIssue_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (PlanIssuePicker.SelectedItem is PlanIssue issue) Seek(issue.Time - 1);
    }
    private sealed record PlanIssue(int NoteId, double Time, string Reason)
    {
        public override string ToString() => $"{Time:F3}s · #{NoteId} · {Reason}";
    }

    private void Restart(bool play)
    {
        if (!_audioReady) return;
        _sounds.Reset();
        _session?.Reset();
        var leadIn = LeadIn();
        if (Math.Abs(_audio.StartPosition + leadIn) > .0001)
        {
            _audio.Load(_musicPath!, _session!.FinishTime + .8, leadIn);
            Progress.SetRange(_audio.StartPosition, _audio.Duration);
            if (play) _audio.Play();
        }
        else _audio.Restart(play);
        if (play) _sounds.Play();
        _wasPlaying = play;
        UpdateFrame(_audio.StartPosition);
        UpdateTransport();
    }

    private double LeadIn() => _session?.Chart.Notes.Count > 0
        ? Math.Max(0, .3 - _session.Chart.Notes.Min(n => NoteMotion.AppearTime(n, _settings.TapSpeed, _settings.TouchSpeed)))
        : 0;

    private void UpdateTransport()
    {
        if (!_ready) return;
        PlayButton.IsEnabled = RestartButton.IsEnabled = _audioReady && !_loading && _planning is null;
        Progress.IsEnabled = _audioReady && !_loading && _planning is null;
        GeneratePlanButton.IsEnabled = _session != null && !_loading && _planning is null;
        GeneratePlanButton.Content = _planning is null ? "生成正攻手顺" : "正在生成并核验…";
        PlayButton.Content = _audio.IsPlaying ? "暂停" : "播放";
        PlaybackLabel.Text = _audio.IsPlaying ? "播放中" : _audioReady && _audio.Position >= _audio.Duration ? "播放结束" : "已暂停";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (SettingsPanel.IsVisible)
        {
            if (e.Key == Key.Escape) { CloseSettings_Click(this, new()); e.Handled = true; }
            return;
        }
        if (e.Source is TextBox or ComboBox or ComboBoxItem or NumericUpDown || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Space) { Play_Click(this, new()); e.Handled = true; }
        if (e.Key == Key.R) { Restart_Click(this, new()); e.Handled = true; }
    }

    private void Difficulty_Changed(object? sender, SelectionChangedEventArgs e)
    { if (_ready && !_loading) ConfigureChart(); }

    private void Mode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        RefreshInputHint();
        ConfigureChart();
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        Pause();
        RefreshDiagnostics();
        SettingsStatus.Text = "设置自动保存。";
        SettingsPanel.IsVisible = true;
        MainPage.IsEnabled = false;
        CloseSettingsButton.Focus();
    }

    private void CloseSettings_Click(object? sender, RoutedEventArgs e)
    {
        SettingsPanel.IsVisible = false;
        MainPage.IsEnabled = true;
        SaveSettings();
        SettingsButton.Focus();
    }

    private void Sensors_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.ShowSensors = SensorToggle.IsChecked == true;
        Playfield.ShowSensors = _settings.ShowSensors;
        Playfield.InvalidateVisual();
        SaveSettings();
    }

    private void HandDisplay_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.HighlightContacts = Playfield.HighlightContacts = ContactHighlightToggle.IsChecked == true;
        _settings.ShowHandMarkers = Playfield.ShowHandMarkers = HandMarkerToggle.IsChecked == true;
        _settings.ShowHandTrails = Playfield.ShowHandTrails = HandTrailToggle.IsChecked == true;
        Playfield.InvalidateVisual();
        SaveSettings();
    }

    private async void GeneratePlan_Click(object? sender, RoutedEventArgs e) => await GeneratePlanAsync();

    private async Task GeneratePlanAsync()
    {
        if (_session is null || _planning != null || _loading) return;
        Pause();
        var chart = _session.Chart;
        using var cancellation = new CancellationTokenSource();
        _planning = cancellation;
        UpdateTransport();
        SetStatus("正在分配双手，并通过实际输入核验整首谱面…");
        try
        {
            var plan = await Task.Run(() => NormalHandPlanner.Plan(chart, cancellation.Token), cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _session?.Chart != chart) return;
            _generatedPlan = plan;
            _inputLog = null;
            _settings.InputLogPath = null;
            if (ModePicker.SelectedIndex != 1) ModePicker.SelectedIndex = 1;
            else ConfigureChart();
            SaveSettings();
            SetStatus($"正攻手顺已生成：CP {plan.Results.Count - plan.Errors.Count}/{chart.Notes.Count}，{plan.Achievement:F4}%。" +
                (plan.AllCriticalPerfect ? "点击播放查看手顺。" : $"有 {plan.Gaps.Count} 处手顺缺口、{plan.Errors.Count} 个非 CP 音符；可播放，并从位置列表或时间轴查看。"), !plan.AllCriticalPerfect);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !cancellation.IsCancellationRequested && _session?.Chart == chart)
                SetStatus($"无法生成正攻手顺：{ex.Message}", true);
        }
        finally
        {
            _planning = null;
            if (!_closed) UpdateTransport();
        }
    }

    private async void LoadInputLog_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickFile("加载时间戳输入日志", "JSON", ["*.json"]);
        if (path is null) return;
        try
        {
            var input = InputLog.Load(path);
            _generatedPlan = null;
            _inputLog = input;
            _settings.InputLogPath = path;
            RefreshInputHint();
            ConfigureChart();
            RefreshDiagnostics();
            SaveSettings();
            SetStatus($"已加载 {input.Events.Count} 条输入并重新开始。选择算法游玩后播放。");
        }
        catch (Exception ex) { SetStatus($"无法加载输入日志：{ex.Message}", true); }
    }

    private void ClearInputLog_Click(object? sender, RoutedEventArgs e)
    {
        _generatedPlan = null;
        _inputLog = null;
        _settings.InputLogPath = null;
        RefreshInputHint();
        ConfigureChart();
        RefreshDiagnostics();
        SaveSettings();
        SetStatus("已清空输入并重新开始。算法游玩将按无输入状态判定。");
    }

    private void RefreshInputHint()
    {
        var algorithm = ModePicker.SelectedIndex == 1;
        ModeLabel.Text = algorithm ? _generatedPlan is null ? "ALGORITHM / INPUT REPLAY" : "ALGORITHM / NORMAL PLAN" : "AUTO PLAY";
        ModeHint.Text = !algorithm ? "按谱面时机自动完成，显示理想判定。"
            : _generatedPlan is { } plan ? $"正攻手顺 · CP {plan.Results.Count - plan.Errors.Count}/{plan.Chart.Notes.Count} · 缺口 {plan.Gaps.Count}\n○ 蓝色左手 · ◇ 橙色右手"
            : _inputLog is null ? "未加载输入。可在设置中加载日志；无输入时按实际状态判定。"
            : $"回放 {Path.GetFileName(_settings.InputLogPath)} · {_inputLog.Events.Count} 条区域输入。" +
              (_inputLog.HandReplay is null ? "" : "\n○ 蓝色左手 · ◇ 橙色右手");
        ToolTip.SetTip(ModeHint, algorithm ? _settings.InputLogPath : null);
        InputLogHint.Text = _generatedPlan is { } generated ? $"当前谱面的正攻手顺 · {generated.Strokes.Count} 个动作\n整首核验 {generated.Results.Count} CP · {generated.Achievement:F4}%"
            : _inputLog is null ? "未加载输入日志。算法游玩将按无输入状态判定。"
            : $"{_settings.InputLogPath}\n{_inputLog.Events.Count} 条输入" +
              (_inputLog.HandReplay is null ? " · 无手位轨迹" : $" · {_inputLog.Hands.Count} 条手部轨迹");
    }

    private void RefreshDiagnostics()
    {
        InputHistoryText.Text = _session?.InputHistory.Count > 0
            ? string.Join("\n", _session.InputHistory.TakeLast(8).Select(x =>
                $"{x.Time:F4}s  {x.Channel} {x.Area} {(x.IsDown ? "ON" : "OFF")}  {x.Source}"))
            : "尚无输入";
        ResultDetails.Text = _session?.LastEvent is { } result
            ? $"#{result.Note.Id} {result.Note.Kind} · {JudgementDisplay.Label(result.Grade)} {JudgementDisplay.Timing(result)}\n" +
              $"判定 {result.Time:F4}s · {result.TimingKind}" +
              (result.DifferenceSeconds is { } diff ? $" · 偏差 {diff * 1000:+0.00;-0.00;0.00} ms" : "") +
              (string.IsNullOrEmpty(result.Reason) ? "" : $"\n{result.Reason}")
            : "尚无判定";
        SlideProgressText.Text = _session?.SlideTrace.Count > 0
            ? string.Join("\n", _session.SlideTrace.TakeLast(6).Select(x =>
                $"{x.Time:F4}s #{x.NoteId} 段{x.Segment + 1} 路{x.Branch + 1} 剩余{x.RemainingAreas} {x.Reason}"))
            : "尚无 Slide 进度";
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolder("选择含 maidata.txt 与音乐的歌曲目录");
        if (path != null) await LoadSongAsync(path);
    }

    private async void OpenChart_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickFile("打开 Simai 谱面或歌曲 ZIP", "Simai", ["*.txt", "*.zip"]);
        if (path != null) await LoadSongAsync(path);
    }

    private async void OpenMusic_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickFile("选择歌曲音乐", "音频", ["*.mp3", "*.ogg", "*.wav", "*.flac"]);
        if (path == null) return;
        _musicPath = path;
        ConfigureChart();
    }

    private async void Demo_Click(object? sender, RoutedEventArgs e) => await LoadDemoAsync();

    private async void NormalDemo_Click(object? sender, RoutedEventArgs e)
    {
        if (_loading || _planning != null) return;
        await LoadSongAsync(Path.Combine(AppContext.BaseDirectory, "samples", "HandReplay", "maidata.txt"));
        await GeneratePlanAsync();
    }

    private async Task LoadDemoAsync()
    {
        try { await LoadSongAsync(await Task.Run(() => DemoSong.Ensure(AppSettings.DataDirectory))); }
        catch (Exception e) { SetStatus($"无法生成示例：{e.Message}", true); }
    }

    private void RefreshSkinChoices()
    {
        var choices = new[] { new SkinChoice(null) }.Concat(_settings.Skins.Select(x => new SkinChoice(x))).ToArray();
        SkinPicker.ItemsSource = choices;
        SkinPicker.SelectedItem = choices.FirstOrDefault(x => x.Path == _settings.SelectedSkin) ?? choices[0];
    }

    private void SetSkin(string? path)
    {
        var pack = path == null ? null : SkinLibrary.Load(path);
        var old = _skin;
        _skin = pack;
        Playfield.Skin = pack;
        Playfield.InvalidateVisual();
        old?.Dispose();
        _settings.SelectedSkin = path;
        PreviewButton.IsEnabled = pack != null;
        SkinHint.Text = pack == null ? "内置矢量样式" : $"{pack.Assets.Count} 张素材 · {pack.Warnings.Count} 项缺省";
        ToolTip.SetTip(SkinHint, pack == null ? "MaiPlay 原创矢量样式" : string.Join("\n", pack.Warnings.Take(12)));
    }

    private void Skin_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || SkinPicker.SelectedItem is not SkinChoice choice) return;
        Pause();
        try { SetSkin(choice.Path); SaveSettings(); }
        catch (Exception ex)
        {
            _ready = false;
            RefreshSkinChoices();
            _ready = true;
            SetStatus($"无法加载皮肤：{ex.Message}", true);
        }
    }

    private async Task ImportSkin(string? path)
    {
        if (path == null) return;
        Pause();
        try
        {
            SetStatus("正在解码皮肤素材…");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            SetSkin(path);
            if (!_settings.Skins.Contains(path, StringComparer.OrdinalIgnoreCase)) _settings.Skins.Add(path);
            _ready = false;
            RefreshSkinChoices();
            _ready = true;
            SaveSettings();
            SetStatus($"已切换皮肤：{_skin!.Name}。{(_skin.Warnings.Count == 0 ? "" : "缺省素材可在预览中查看。")}");
        }
        catch (Exception e) { SetStatus($"无法导入皮肤：{e.Message}", true); }
    }

    private async void ImportSkinZip_Click(object? sender, RoutedEventArgs e) => await ImportSkin(await PickFile("导入 MajdataPlay 原始皮肤 ZIP", "ZIP", ["*.zip"]));
    private async void ImportSkinFolder_Click(object? sender, RoutedEventArgs e) => await ImportSkin(await PickFolder("选择包含 TapSkins 的皮肤根目录"));
    private async void PreviewSkin_Click(object? sender, RoutedEventArgs e)
    {
        if (_skin == null) return;
        Pause();
        await new SkinPreviewWindow(_skin).ShowDialog(this);
    }

    private void Volume_Changed(object? sender, RangeBaseValueChangedEventArgs e) { if (_ready) ApplyVolumes(); }
    private void SoundEnabled_Changed(object? sender, RoutedEventArgs e) { if (_ready) ApplyVolumes(); }

    private void Speed_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_ready) return;
        ApplySpeeds();
        SaveSettings();
    }

    private void ApplySpeeds()
    {
        _settings.TapSpeed = (double)(TapSpeedInput.Value ?? 7.5m);
        _settings.TouchSpeed = (double)(TouchSpeedInput.Value ?? 7.5m);
        Playfield.TapSpeed = _settings.TapSpeed;
        Playfield.TouchSpeed = _settings.TouchSpeed;
        Playfield.InvalidateVisual();
    }

    private void ApplyVolumes()
    {
        _settings.MusicVolume = MusicSlider.Value / 100;
        _settings.SoundVolume = SoundSlider.Value / 100;
        _settings.SoundEnabled = SoundToggle.IsChecked == true;
        _audio.MusicVolume = (float)_settings.MusicVolume;
        _sounds.Volume = (float)_settings.SoundVolume;
        _sounds.Enabled = _settings.SoundEnabled;
        MusicLevel.Text = $"{MusicSlider.Value:0}%";
        SoundLevel.Text = $"{SoundSlider.Value:0}%";
    }

    private void LoadSounds(string path)
    {
        var missing = _sounds.LoadDirectory(path);
        _settings.SoundDirectory = path;
        SoundHint.Text = $"{Path.GetFileName(path)} · {missing.Count} 项使用内置音效";
        ToolTip.SetTip(SoundHint, string.Join("\n", missing));
    }

    private async void ImportSound_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolder("选择 MajdataPlay SFX 目录");
        if (path == null) return;
        Pause();
        try { LoadSounds(path); SaveSettings(); SetStatus("已加载音效目录。"); }
        catch (Exception ex) { SetStatus($"无法读取音效：{ex.Message}", true); }
    }

    private void BuiltInSound_Click(object? sender, RoutedEventArgs e)
    {
        _sounds.UseBuiltIn();
        _settings.SoundDirectory = null;
        SoundHint.Text = "内置合成音效";
        SaveSettings();
    }

    private async Task<string?> PickFolder(string title)
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new() { Title = title, AllowMultiple = false });
        return selected.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickFile(string title, string type, string[] patterns)
    {
        var selected = await StorageProvider.OpenFilePickerAsync(new()
        { Title = title, AllowMultiple = false, FileTypeFilter = [new FilePickerFileType(type) { Patterns = patterns }] });
        return selected.FirstOrDefault()?.TryGetLocalPath();
    }

    private void AudioFailure(Exception ex)
    {
        Pause();
        _audioReady = false;
        UpdateTransport();
        SetStatus($"音频播放失败：{ex.Message}。重新选择音乐可重试。", true);
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        SettingsStatus.Text = message;
        SettingsStatus.Foreground = Brush.Parse(error ? "#9C3E36" : "#426078");
        StatusText.Foreground = Brush.Parse(error ? "#9C3E36" : "#426078");
        StatusBorder.Background = Brush.Parse(error ? "#FCE7E1" : "#E1EBF2");
    }

    private void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { SetStatus($"设置无法保存：{e.Message}", true); }
    }

    private static string FormatTime(double time) => $"{(time < 0 ? "−" : "")}{(int)Math.Abs(time) / 60:00}:{Math.Abs(time) % 60:00.0}";
    private sealed record DifficultyChoice(Chart Chart) { public override string ToString() => $"{Chart.DifficultyName}  ·  {Chart.Level}"; }
    private sealed record SkinChoice(string? Path) { public override string ToString() => Path == null ? "MaiPlay · 内置" : System.IO.Path.GetFileNameWithoutExtension(Path); }
}
