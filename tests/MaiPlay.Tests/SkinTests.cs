using System.IO.Compression;
using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using MaiPlay.Core;
using MaiPlay.Rendering;
using MaiPlay.Skins;

namespace MaiPlay.Tests;

[CollectionDefinition("Skin rendering", DisableParallelization = true)]
public sealed class SkinRenderingCollection;

[Collection("Skin rendering")]
public sealed class SkinTests
{
    private static readonly Lazy<bool> AvaloniaReady = new(() =>
    {
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia().SetupWithoutStarting();
        return true;
    });

    [Fact]
    public void TimelineDragSeeksOnReleaseAndZoomKeepsTargetVisible()
    {
        _ = AvaloniaReady.Value;
        var timeline = new SeekTimeline { Height = 46 };
        var window = new Window { Content = timeline, Width = 600, Height = 46 };
        var targets = new List<double>();
        var started = 0;
        timeline.ScrubStarted += () => started++;
        timeline.SeekRequested += targets.Add;
        window.Show();
        try
        {
            timeline.SetRange(-2, 120);
            timeline.SetTime(60);
            window.MouseDown(new Point(150, 18), MouseButton.Left);
            window.MouseMove(new Point(300, 18));
            Assert.Equal(1, started);
            Assert.Empty(targets);
            window.MouseUp(new Point(300, 18), MouseButton.Left);
            Assert.InRange(Assert.Single(targets), 58, 62);
            timeline.Zoom(.5);
            Assert.InRange(60, timeline.ViewStart, timeline.ViewEnd);
            Assert.Equal(61, timeline.ViewEnd - timeline.ViewStart, 5);
            window.MouseDown(new Point(300, 18), MouseButton.Right);
            window.MouseMove(new Point(400, 18));
            window.MouseUp(new Point(400, 18), MouseButton.Right);
            Assert.Single(targets);
            Assert.True(timeline.ViewStart < 20);
            timeline.Fit();
            Assert.Equal(-2, timeline.ViewStart);
            Assert.Equal(120, timeline.ViewEnd);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void UnsafeZipPathsAreRejectedWithoutWritingFiles()
    {
        _ = AvaloniaReady.Value;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) zip.CreateEntry("../escape.png");
            Assert.Throws<InvalidDataException>(() => SkinLibrary.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingHoldOnFallsBackButMissingVariantsDoNot()
    {
        _ = AvaloniaReady.Value;
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "TapSkins"));
        Directory.CreateDirectory(Path.Combine(root, "HoldSkins"));
        try
        {
            using var image = new RenderTargetBitmap(new PixelSize(2, 2));
            image.Save(Path.Combine(root, "TapSkins/tap.png"));
            image.Save(Path.Combine(root, "HoldSkins/hold.png"));
            using var skin = SkinLibrary.Load(root);
            Assert.Same(skin.Get("HoldSkins/hold.png"), skin.Get("HoldSkins/hold_on.png"));
            Assert.Null(skin.Get("TapSkins/tap_each.png"));
            Assert.Null(skin.Get("HoldSkins/hold_each_on.png"));
            Assert.Null(skin.Get("TouchSkins/touch_break.png"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BreakOverridesEachWhileExRemainsAnOverlay()
    {
        var note = new ChartNote { IsEach = true, IsBreak = true, IsEx = true, IsStar = true, IsDoubleStar = true };
        Assert.Equal("StarSkins/star_break_double.png", SkinSprites.Tap(note));
        Assert.Equal("StarSkins/star_ex_double.png", SkinSprites.Ex(note));
        Assert.Equal("HoldSkins/hold_break_on.png", SkinSprites.Hold(note, true, false));
        Assert.Equal("HoldSkins/hold_off.png", SkinSprites.Hold(note, true, true));
        Assert.Equal("TouchSkins/touch_break_point.png", SkinSprites.TouchPoint(note));
    }

    [Fact]
    public void CorruptPngNamesTheResourceInError()
    {
        _ = AvaloniaReady.Value;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("TapSkins/tap.png").Open())) writer.Write("not a png");
            Assert.Contains("TapSkins/tap.png", Assert.Throws<InvalidDataException>(() => SkinLibrary.Load(path)).Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HugePngHeaderIsRejectedBeforeImageDecode()
    {
        _ = AvaloniaReady.Value;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            var header = new byte[24];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), 1_000_000);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20, 4), 1_000_000);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var output = zip.CreateEntry("TapSkins/tap.png").Open()) output.Write(header);
            var error = Assert.Throws<InvalidDataException>(() => SkinLibrary.Load(path));
            Assert.Contains("尺寸过大", error.Message);
            Assert.Contains("TapSkins/tap.png", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait("Category", "ReferenceSkins")]
    public void AvailableUpstreamPacksDecodeAndRenderBothModes()
    {
        _ = AvaloniaReady.Value;
        var root = FindWorkspace();
        var reference = Path.Combine(root, ".reference", "MajdataPlay-Skins");
        // The licensed reference artwork is intentionally not shipped with MaiPlay.
        // Clone the documented upstream repository to opt into this integration check.
        if (!Directory.Exists(reference)) return;
        var packages = Directory.GetFiles(reference, "*.zip");
        Assert.Equal(5, packages.Length);
        var output = Path.Combine(root, "artifacts", "gameplay-validation", "skins");
        Directory.CreateDirectory(output);
        var chart = PreviewChart();
        foreach (var package in packages)
        {
            using var skin = SkinLibrary.Load(package);
            File.WriteAllLines(Path.Combine(output, $"{skin.Name}-resources.txt"),
                new[] { $"{skin.Assets.Count} PNG decoded" }.Concat(skin.Warnings));
            Assert.InRange(skin.Assets.Count, 150, 200);
            Assert.NotNull(skin.Get("WifiSkins/wifi_10.png"));
            Assert.NotNull(skin.Get("SlideOKSkins/miss_wifi_d.png"));
            foreach (var mode in Enum.GetValues<PlayMode>())
            foreach (var time in new[] { 1.7, 2.3, 3.1, 4.2 })
            {
                RenderFrame(chart, skin, time, mode, Path.Combine(output, $"{skin.Name}-{mode}-{time:F1}.png"));
            }
            // The extracted-directory path must preserve exactly the same images.
            var extracted = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                ZipFile.ExtractToDirectory(package, extracted);
                using var directorySkin = SkinLibrary.Load(extracted);
                Assert.Equal(skin.Assets.Keys.Order().ToArray(), directorySkin.Assets.Keys.Order().ToArray());
                Assert.Equal(skin.Get("TapSkins/tap.png")!.PixelSize, directorySkin.Get("TapSkins/tap.png")!.PixelSize);
            }
            finally { Directory.Delete(extracted, true); }
        }
    }

    [Fact]
    [Trait("Category", "ReferenceSkins")]
    public void AvailableAcceptanceMastersRenderWithAllFiveSkins()
    {
        _ = AvaloniaReady.Value;
        var root = FindWorkspace();
        var acceptance = Path.Combine(root, ".reference", "acceptance");
        var skins = Path.Combine(root, ".reference", "MajdataPlay-Skins");
        if (!Directory.Exists(acceptance) || !Directory.Exists(skins)) return;
        var songs = Directory.GetFiles(acceptance, "maidata.txt", SearchOption.AllDirectories)
            .Select(SimaiParser.LoadSong).ToArray();
        Assert.Equal(4, songs.Length);
        var output = Path.Combine(root, "artifacts", "gameplay-validation", "skins", "acceptance");
        Directory.CreateDirectory(output);
        foreach (var package in Directory.GetFiles(skins, "*.zip"))
        {
            using var skin = SkinLibrary.Load(package);
            foreach (var song in songs)
            {
                var chart = song.Charts.Single(c => c.Difficulty == 5);
                // Pick an actual busy passage, weighted toward simultaneous long-note
                // families, instead of a fixed frame that might show only empty space.
                var time = chart.Notes.Select(n => n.Time + .08).OrderByDescending(t =>
                {
                    var active = chart.Notes.Where(n => NoteMotion.AppearTime(n, 7.5, 7.5) <= t && n.EndTime > t).ToArray();
                    return active.Select(n => n.Kind).Distinct().Count() * 15
                        + active.Sum(n => n.Kind is NoteKind.Slide or NoteKind.Hold or NoteKind.TouchHold ? 3 : 1);
                }).First();
                var name = new DirectoryInfo(song.Directory).Name;
                RenderFrame(chart, skin, time, PlayMode.AutoPlay, Path.Combine(output, $"{name}-{skin.Name}.png"));
                File.WriteAllText(Path.Combine(output, $"{name}-frame.txt"), $"{song.Title}\nMaster {chart.Level}\nFrame: {time:F6}s\nNotes: {chart.Notes.Count}");
            }
        }
    }

    [Fact]
    public void RepresentativeMotionStagesRenderAtThreeSpeeds()
    {
        _ = AvaloniaReady.Value;
        var chart = SimaiParser.ParseChart("(120){4},,,,1/3h[2:1]/B5/Ch[2:1]/8-4[2:1],,,,E");
        var output = Path.Combine(FindWorkspace(), "artifacts", "gameplay-validation", "skins", "motion");
        Directory.CreateDirectory(output);
        foreach (var speed in new[] { 3d, 7.5, 12d })
        foreach (var offset in new[] { -.48, -.28, -.1, .2, .6 })
            RenderFrame(chart, null, 2 + offset, PlayMode.AutoPlay, Path.Combine(output, $"speed-{speed:F1}-offset-{offset:F2}.png"), speed);
    }

    [Fact]
    public void AchievementDisplayRendersBeforeDuringAndAfterMisses()
    {
        _ = AvaloniaReady.Value;
        var chart = SimaiParser.ParseChart("(120){4},,,,1b,3,5,7,E");
        var output = Path.Combine(FindWorkspace(), "artifacts", "gameplay-validation", "skins", "achievement");
        Directory.CreateDirectory(output);
        foreach (var time in new[] { 1d, 2.7, 5d })
            RenderFrame(chart, null, time, PlayMode.Algorithm, Path.Combine(output, $"time-{time:F1}.png"));
    }

    [Fact]
    public void ApproachSpeedDoesNotMoveStarsAlreadySliding()
    {
        _ = AvaloniaReady.Value;
        var chart = SimaiParser.ParseChart("(120){4}1-5[2:1]*-3[4:1]/4w8[2:1],E");
        var output = Path.Combine(FindWorkspace(), "artifacts", "gameplay-validation", "skins", "slide-speed");
        Directory.CreateDirectory(output);
        byte[]? baseline = null;
        foreach (var speed in new[] { .25, 7.5, 20d })
        {
            var path = Path.Combine(output, $"approach-{speed:F2}.png");
            RenderFrame(chart, null, .8, PlayMode.AutoPlay, path, speed);
            var pixels = File.ReadAllBytes(path);
            if (baseline is null) baseline = pixels;
            else Assert.Equal(baseline, pixels);
        }
    }

    [Fact]
    public void NonCentralTouchHoldsRenderWithBuiltinAndAvailableSkins()
    {
        _ = AvaloniaReady.Value;
        var root = FindWorkspace();
        var chart = SimaiParser.ParseChart("(120){4}B2h[#2]/B3h[#2]/E3h[#2]/B6h[#2]/B7h[#2]/E7h[#2],E", first: 1);
        var output = Path.Combine(root, "artifacts", "gameplay-validation", "official-touchholds");
        Directory.CreateDirectory(output);
        var reference = Path.Combine(root, ".reference", "MajdataPlay-Skins");
        var packages = new string?[] { null }.Concat(Directory.Exists(reference) ? Directory.GetFiles(reference, "*.zip") : []);
        foreach (var package in packages)
        {
            using var skin = package is null ? null : SkinLibrary.Load(package);
            foreach (var time in new[] { .8, 1.2, 2.4 })
                RenderFrame(chart, skin, time, PlayMode.AutoPlay, Path.Combine(output, $"{skin?.Name ?? "BuiltIn"}-{time:F1}.png"));
        }
    }

    private static void RenderFrame(Chart chart, SkinPack? skin, double time, PlayMode mode, string path, double speed = 7.5)
    {
        var session = new PlaybackSession(chart, mode);
        session.Advance(time);
        RenderSession(session, skin, time, path, speed);
    }

    [Fact]
    public void MinimumSizeMainPageAndSettingsPagesRemainUsable()
    {
        _ = AvaloniaReady.Value;
        var oldDirectory = Environment.GetEnvironmentVariable("MAIPLAY_DATA_DIRECTORY");
        var profile = Path.Combine(Path.GetTempPath(), $"maiplay-ui-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("MAIPLAY_DATA_DIRECTORY", profile);
        MainWindow? window = null;
        Window? host = null;
        try
        {
            window = new MainWindow();
            var page = (Control)window.Content!;
            var main = window.FindControl<Grid>("MainPage")!;
            var settings = window.FindControl<Border>("SettingsPanel")!;
            var play = window.FindControl<Button>("PlayButton")!;
            var field = window.FindControl<GameView>("Playfield")!;
            var open = window.FindControl<Button>("SettingsButton")!;
            var close = window.FindControl<Button>("CloseSettingsButton")!;
            var pageControls = new Control[][]
            {
                [window.FindControl<NumericUpDown>("TapSpeedInput")!, window.FindControl<NumericUpDown>("TouchSpeedInput")!,
                    window.FindControl<CheckBox>("SensorToggle")!, window.FindControl<CheckBox>("ContactHighlightToggle")!,
                    window.FindControl<CheckBox>("HandMarkerToggle")!,
                    window.FindControl<CheckBox>("HandTrailToggle")!],
                [window.FindControl<ComboBox>("SkinPicker")!, window.FindControl<Button>("PreviewButton")!],
                [window.FindControl<Slider>("MusicSlider")!, window.FindControl<Slider>("SoundSlider")!, window.FindControl<CheckBox>("SoundToggle")!],
                [window.FindControl<TextBlock>("InputLogHint")!,
                    window.FindControl<TextBlock>("InputHistoryText")!, window.FindControl<TextBlock>("ResultDetails")!,
                    window.FindControl<TextBlock>("SlideProgressText")!]
            };
            // Show a plain headless host so templates and visual descendants attach.
            // MainWindow itself stays unopened: its startup loader and audio never run.
            window.Content = null;
            host = new Window { Content = page, Width = 960, Height = 720, Background = window.Background,
                SystemDecorations = SystemDecorations.None };
            host.Show();
            var output = Path.Combine(FindWorkspace(), "artifacts", "normal-planner-validation", "layout");
            Directory.CreateDirectory(output);
            void Draw(string name)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                Assert.Equal(new Size(960, 720), page.Bounds.Size);
                using var bitmap = new RenderTargetBitmap(new PixelSize(960, 720));
                bitmap.Render(host);
                bitmap.Save(Path.Combine(output, name));
            }
            void AssertVisible(Control control)
            {
                Assert.True(control.IsEffectivelyVisible, $"{control.Name} is not visible.");
                Assert.InRange(control.Bounds.Width, 1, 960);
                Assert.InRange(control.Bounds.Height, 1, 720);
                var start = control.TranslatePoint(default, page)!.Value;
                var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), page)!.Value;
                Assert.InRange(start.X, 0, 959);
                Assert.InRange(start.Y, 0, 719);
                Assert.InRange(end.X, 1, 960);
                Assert.InRange(end.Y, 1, 720);
            }
            Draw("main-960x720.png");
            AssertVisible(play);
            AssertVisible(open);
            AssertVisible(window.FindControl<Button>("OpenFolderButton")!);
            AssertVisible(window.FindControl<ComboBox>("ModePicker")!);
            AssertVisible(window.FindControl<Button>("GeneratePlanButton")!);
            Assert.InRange(field.Bounds.Width, 300, 960);
            Assert.InRange(field.Bounds.Height, 300, 720);
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(settings.IsVisible);
            Assert.False(main.IsEnabled);
            var tabs = settings.GetVisualDescendants().OfType<TabControl>().Single();
            Assert.Equal(["显示", "皮肤", "声音", "判定与诊断"], tabs.Items.Cast<TabItem>().Select(t => t.Header));
            for (var i = 0; i < 4; i++)
            {
                tabs.SelectedIndex = i;
                Draw($"settings-{i}-960x720.png");
                AssertVisible(tabs);
                AssertVisible(close);
                foreach (var control in pageControls[i]) AssertVisible(control);
            }
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(settings.IsVisible);
            Assert.True(main.IsEnabled);
            Assert.Equal("播放", play.Content);
        }
        finally
        {
            host?.Close();
            window?.Close();
            Environment.SetEnvironmentVariable("MAIPLAY_DATA_DIRECTORY", oldDirectory);
            if (Directory.Exists(profile)) Directory.Delete(profile, true);
        }
    }

    [Fact]
    public void HandwrittenReplayRendersActualGradesLongNotesAndSensors()
    {
        _ = AvaloniaReady.Value;
        var root = FindWorkspace();
        var sample = Path.Combine(root, "samples", "Gameplay");
        var chart = SimaiParser.LoadSong(Path.Combine(sample, "maidata.txt")).Charts.Single();
        var input = InputLog.Load(Path.Combine(sample, "inputs.json"));
        var output = Path.Combine(root, "artifacts", "hand-replay-validation", "input-feedback");
        Directory.CreateDirectory(output);
        var reference = Path.Combine(root, ".reference", "MajdataPlay-Skins");
        var packages = new string?[] { null }.Concat(Directory.Exists(reference) ? Directory.GetFiles(reference, "*.zip") : []);
        foreach (var package in packages)
        {
            using var skin = package is null ? null : SkinLibrary.Load(package);
            foreach (var time in new[] { 1.02, 1.95, 3.15, 4.2, 7.5, 13.25, 14.15, 14.5, 14.9, 17.6, 18.75, 20.1, 21.5, 22.03, 25.1, 25.85 })
            {
                var session = new PlaybackSession(chart, PlayMode.Algorithm, input.Events);
                session.Advance(time);
                var before = (session.Time, session.Completed, session.Missed, session.DXScore, session.InputHistory.Count);
                RenderSession(session, skin, time, Path.Combine(output, $"{skin?.Name ?? "BuiltIn"}-{time:F2}.png"), sensors: true);
                Assert.Equal(before, (session.Time, session.Completed, session.Missed, session.DXScore, session.InputHistory.Count));
            }
        }
    }

    private static void RenderSession(PlaybackSession session, SkinPack? skin, double time, string path, double speed = 7.5, bool sensors = false)
    {
        var chart = session.Chart;
        var view = new GameView { Chart = chart, Session = session, Time = time, Skin = skin, TapSpeed = speed, TouchSpeed = speed, ShowSensors = sensors };
        view.Measure(new Size(900, 900));
        view.Arrange(new Rect(0, 0, 900, 900));
        using var bitmap = new RenderTargetBitmap(new PixelSize(900, 900));
        bitmap.Render(view);
        bitmap.Save(path);
    }

    [Fact]
    public void HandMarkersAndHighlightsRenderWithoutChangingReplayAndDisappearInAuto()
    {
        _ = AvaloniaReady.Value;
        var root = FindWorkspace();
        var directory = Path.Combine(root, "samples", "HandReplay");
        var chart = SimaiParser.LoadSong(directory).Charts.Single();
        var log = InputLog.Load(Path.Combine(directory, "hands.json"));
        var output = Path.Combine(root, "artifacts", "normal-planner-validation", "rendering");
        Directory.CreateDirectory(output);
        var skinPath = Path.Combine(root, ".reference", "MajdataPlay-Skins", "Light2-星鹿.zip");
        using var skin = File.Exists(skinPath) ? SkinLibrary.Load(skinPath) : null;
        byte[] Draw(PlaybackSession session, double time, SkinPack? pack, string name, bool markers, bool highlight, bool trails,
            int width = 650, int height = 650)
        {
            var before = (session.Time, session.Completed, session.Missed, session.DXScore, session.InputHistory.Count);
            var view = new GameView { Chart = session.Chart, Session = session, HandReplay = log.HandReplay, Time = time,
                Skin = pack, ShowHandMarkers = markers, HighlightContacts = highlight, ShowHandTrails = trails };
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
            bitmap.Render(view);
            using var bytes = new MemoryStream();
            bitmap.Save(bytes);
            File.WriteAllBytes(Path.Combine(output, name + ".png"), bytes.ToArray());
            Assert.Equal(before, (session.Time, session.Completed, session.Missed, session.DXScore, session.InputHistory.Count));
            return bytes.ToArray();
        }
        foreach (var pack in new[] { null, skin }.Distinct())
            foreach (var time in new[] { 1.02, 3.02, 4.02, 5.17, 7.02, 10.5 })
            {
                var session = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
                session.Advance(time);
                Draw(session, time, pack, $"sample-{pack?.Name ?? "vector"}-{time:F2}", true, true, false);
            }
        var paused = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
        paused.Advance(3.02);
        var a = Draw(paused, 3.02, null, "paused", true, true, true);
        var b = Draw(paused, 3.02, null, "paused-again", true, true, true);
        Assert.Equal(a, b);
        Assert.False(a.SequenceEqual(Draw(paused, 3.02, null, "hidden", false, false, false)));
        foreach (var (width, height) in new[] { (480, 480), (1100, 1100), (1400, 900) })
            foreach (var time in new[] { 1.02, 3.02 })
            {
                var session = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
                session.Advance(time);
                Draw(session, time, skin, $"hand-scale-{width}x{height}-{time:F2}", true, true, true, width, height);
            }
        var auto = new PlaybackSession(chart, PlayMode.AutoPlay);
        auto.Advance(3.02);
        Assert.Equal(Draw(auto, 3.02, null, "auto-on", true, true, true), Draw(auto, 3.02, null, "auto-off", false, false, false));

        var official = Path.Combine(root, ".reference", "charts", "game-extracted", "21_BUDDiES", "011624_SQUAD-Phvntom-");
        var officialLog = Path.Combine(root, "artifacts", "hand-replay-validation", "squad-expert-hands.json");
        if (Directory.Exists(official) && File.Exists(officialLog))
        {
            chart = SimaiParser.LoadSong(official).Charts.Single(c => c.Difficulty == 4);
            log = InputLog.Load(officialLog);
            foreach (var time in new[] { 2.6, 10.85, 16.56, 19.3 })
            {
                var session = new PlaybackSession(chart, PlayMode.Algorithm, log.Events);
                session.Advance(time);
                Draw(session, time, skin, $"squad-{time:F2}", true, true, false);
            }
        }
    }

    private static string FindWorkspace()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static Chart PreviewChart()
    {
        var notes = new List<ChartNote>
        {
            new() { Kind = NoteKind.Tap, Time = 2, StartPosition = 1, IsEx = true },
            new() { Kind = NoteKind.Tap, Time = 2, StartPosition = 8, IsEach = true },
            new() { Kind = NoteKind.Hold, Time = 2, Duration = 2, StartPosition = 3, IsBreak = true, IsEx = true },
            new() { Kind = NoteKind.Hold, Time = 2.4, Duration = 1, StartPosition = 7, IsEach = true },
            new() { Kind = NoteKind.Touch, Time = 2.7, TouchArea = "B2", StartPosition = 2 },
            new() { Kind = NoteKind.Touch, Time = 3, TouchArea = "B2", StartPosition = 2, IsEach = true },
            new() { Kind = NoteKind.Touch, Time = 3.4, TouchArea = "B2", StartPosition = 2, IsBreak = true },
            new() { Kind = NoteKind.TouchHold, Time = 2, Duration = 2, TouchArea = "C" },
            new() { Kind = NoteKind.Tap, Time = 2.1, StartPosition = 5, IsStar = true, IsDoubleStar = true, IsEx = true },
            new() { Kind = NoteKind.Slide, Time = 2.1, SlideStartTime = 2.6, Duration = 1.3, StartPosition = 5, EndPosition = 1,
                Source = "5w1[4:1]", SlidePath = new SlidePath([new("w", 5, 1)], [1.3]) },
            new() { Kind = NoteKind.Slide, Time = 2.1, SlideStartTime = 2.6, Duration = 1.3, StartPosition = 5, EndPosition = 8,
                IsEach = true, Source = "5^8[4:1]", SlidePath = new SlidePath([new("^", 5, 8)], [1.3]) }
        };
        notes = notes.OrderBy(n => n.Time).Select((n, index) => n with { Id = index }).ToList();
        return new Chart("Skin preview", "", "", 5, "", notes, 5);
    }
}
