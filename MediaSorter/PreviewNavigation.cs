using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Sorter.Core;
using Windows.System;

namespace MediaSorter;

public sealed partial class MainWindow
{
    private List<FileEntry> previewItems = [];
    private ListView? navigationList;
    private bool syncingPreviewSelection;
    private Button? firstMedia, previousMedia, nextMedia, lastMedia;
    private TextBlock? previewPosition;
    private int previewWheelDelta;
    private bool wheelNavigating;
    private long lastWheelNavigation;

    private void PreviewWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(previewHost).Properties;
        // In gallery mode the wheel scrolls thumbnails normally; media mode changes files.
        if (busy || PreviewIndex < 0 || previewHost.Child is GridView || properties.IsHorizontalMouseWheel) return;
        e.Handled = true;
        _ = NavigatePreviewWheel(properties.MouseWheelDelta);
    }
    private async Task NavigatePreviewWheel(int delta)
    {
        if (busy || wheelNavigating || PreviewIndex < 0 || previewHost.Child is GridView || delta == 0) return;
        if (Environment.TickCount64 - lastWheelNavigation < 180) return;
        if (Math.Sign(previewWheelDelta) != Math.Sign(delta)) previewWheelDelta = 0;
        previewWheelDelta += delta;
        if (Math.Abs(previewWheelDelta) < 120) return;
        var direction = previewWheelDelta > 0 ? "previous" : "next";
        previewWheelDelta = 0; lastWheelNavigation = Environment.TickCount64; wheelNavigating = true;
        try { await NavigatePreview(direction); }
        finally { wheelNavigating = false; }
    }

    private FrameworkElement BuildPreviewNavigation()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        Button Nav(string key, string glyph, string command, VirtualKey shortcut)
        {
            var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Padding = new Thickness(10, 7, 10, 7) };
            ToolTipService.SetToolTip(button, T(key));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(key));
            button.Click += (_, _) => _ = NavigatePreview(command);
            var accelerator = new KeyboardAccelerator { Key = shortcut, Modifiers = VirtualKeyModifiers.Control, IsEnabled = true };
            accelerator.Invoked += (sender, e) => { if (!busy) _ = NavigatePreview(command); e.Handled = true; };
            button.KeyboardAccelerators.Add(accelerator);
            return button;
        }
        firstMedia = Nav("FirstMedia", "\uE892", "first", VirtualKey.Home);
        previousMedia = Nav("PreviousMedia", "\uE76B", "previous", VirtualKey.Left);
        nextMedia = Nav("NextMedia", "\uE76C", "next", VirtualKey.Right);
        lastMedia = Nav("LastMedia", "\uE893", "last", VirtualKey.End);
        previewPosition = Text("0 / 0", 12, true); previewPosition.VerticalAlignment = VerticalAlignment.Center; previewPosition.MinWidth = 52; previewPosition.TextAlignment = TextAlignment.Center;
        bar.Children.Add(firstMedia); bar.Children.Add(previousMedia); bar.Children.Add(previewPosition); bar.Children.Add(nextMedia); bar.Children.Add(lastMedia);
        UpdatePreviewNavigation();
        return new ScrollViewer { Content = bar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled };
    }
    private void SetPreviewItems(IEnumerable<FileEntry> entries, ListView? list)
    {
        previewItems = entries.Where(x => !x.IsDirectory && MediaFiles.Kind(x.Path) != null).ToList();
        navigationList = list;
        UpdatePreviewNavigation();
    }
    private int PreviewIndex => previewItems.FindIndex(x => string.Equals(x.Path, selectedFile, StringComparison.OrdinalIgnoreCase));
    private void UpdatePreviewNavigation()
    {
        if (previewPosition == null) return;
        var index = PreviewIndex; var available = !busy && previewItems.Count > 0;
        previewPosition.Text = $"{index + 1} / {previewItems.Count}";
        firstMedia!.IsEnabled = available && index != 0;
        previousMedia!.IsEnabled = available && index > 0;
        nextMedia!.IsEnabled = available && index < previewItems.Count - 1;
        lastMedia!.IsEnabled = available && index != previewItems.Count - 1;
    }
    private async Task NavigatePreview(string command)
    {
        if (busy || previewItems.Count == 0) return;
        var index = PreviewIndex;
        var target = command switch { "first" => 0, "last" => previewItems.Count - 1, "previous" => index - 1, _ => index + 1 };
        if (target < 0 || target >= previewItems.Count || target == index) return;
        var entry = previewItems[target];
        if (navigationList != null)
        {
            syncingPreviewSelection = true;
            try { navigationList.SelectedItems.Clear(); navigationList.SelectedItem = entry; navigationList.ScrollIntoView(entry); }
            finally { syncingPreviewSelection = false; }
        }
        await ShowEntry(entry);
    }
}
