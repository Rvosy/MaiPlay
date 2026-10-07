using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MaiPlay.Skins;

/// <summary>Shows all imported images, including judgements not produced by these two modes.</summary>
public sealed class SkinPreviewWindow : Window
{
    public SkinPreviewWindow(SkinPack skin)
    {
        Title = $"皮肤素材预览 · {skin.Name}";
        Width = 1080;
        Height = 760;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tabs = new TabControl();
        var tabItems = new List<TabItem>();
        foreach (var group in skin.Assets.OrderBy(x => x.Key).GroupBy(x => x.Key.Contains('/') ? x.Key.Split('/')[0] : "根目录"))
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var item in group)
            {
                panel.Children.Add(new Border
                {
                    Width = 236, Margin = new Thickness(6), Padding = new Thickness(9),
                    Background = Brush.Parse("#17263C"), CornerRadius = new CornerRadius(8),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new Image { Source = item.Value, Width = 210, Height = 124, Stretch = Stretch.Uniform },
                            new TextBlock { Text = item.Key.Split('/').Last(), Foreground = Brushes.White, FontSize = 11,
                                Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = $"{item.Value.PixelSize.Width} × {item.Value.PixelSize.Height} px · 100 PPU",
                                Foreground = Brush.Parse("#94ADC8"), FontSize = 11 }
                        }
                    }
                });
            }
            tabItems.Add(new TabItem { Header = group.Key, Content = new ScrollViewer { Content = panel } });
        }
        tabs.ItemsSource = tabItems;
        var message = skin.Warnings.Count == 0 ? "本版播放所需素材齐全。缺失的可选状态素材遵循上游空白行为。"
            : string.Join("\n", skin.Warnings);
        var layout = new DockPanel { Margin = new Thickness(14) };
        var status = new TextBlock
        {
            Text = $"{skin.Name} · {skin.Assets.Count} 张原始素材\n{message}",
            Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap, MaxHeight = 100
        };
        DockPanel.SetDock(status, Dock.Top);
        layout.Children.Add(status);
        layout.Children.Add(tabs);
        Content = layout;
    }
}
