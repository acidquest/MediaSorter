using System.Text.Json;
using Microsoft.UI.Xaml;

namespace MediaSorter;

public sealed partial class MainWindow
{
    // Explicit opt-in regression harness; operates only on a supplied directory of synthetic fixtures.
    internal async Task RunSmokeTestAsync(string fixtureDirectory)
    {
        var results = new List<string>();
        var report = Path.Combine(fixtureDirectory, "ui-smoke-result.json");
        var originalTheme = root.RequestedTheme;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); results.Add(label); }
        try
        {
            await Task.Delay(500);
            Check(root.IsLoaded && workspace.ActualWidth > 500 && panels.Count == 4, "Four panels loaded and measured");
            await ShowEntry(Entry(Path.Combine(fixtureDirectory, "camera.jpg")));
            Check(previewImage?.Source != null, "JPEG preview decoded");
            Check(Math.Abs(previewImage!.Width - previewHost.ActualWidth) < 2 && Math.Abs(previewImage.Height - previewHost.ActualHeight) < 2, "Photo fits preview viewport");
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 820)); await Task.Delay(200);
            Check(Math.Abs(previewImage.Width - previewHost.ActualWidth) < 2 && Math.Abs(previewImage.Height - previewHost.ActualHeight) < 2, "Photo follows window resize");
            for (var i = 0; i < 4; i++)
            {
                await ShowEntry(Entry(Path.Combine(fixtureDirectory, "sample.mp4"))); await Task.Delay(350);
                Check(player != null && videoPlayer != null, "Video player attached " + i);
                Check(Math.Abs(player!.Width - previewHost.ActualWidth) < 2 && Math.Abs(player.Height - previewHost.ActualHeight) < 2, "Video fits viewport " + i);
                videoPlayer!.Play(); await Task.Delay(250);
                await ShowEntry(Entry(Path.Combine(fixtureDirectory, "camera.jpg"))); await Task.Delay(150);
                Check(player == null && videoPlayer == null && previewImage?.Source != null, "Playing video to photo switch " + i);
            }
            root.RequestedTheme = ElementTheme.Dark; await Task.Delay(100);
            Check(root.ActualTheme == ElementTheme.Dark && AppWindow.TitleBar.ButtonForegroundColor == Microsoft.UI.Colors.White, "Dark theme caption buttons");
            root.RequestedTheme = ElementTheme.Light; await Task.Delay(100);
            Check(root.ActualTheme == ElementTheme.Light && AppWindow.TitleBar.ButtonForegroundColor != Microsoft.UI.Colors.White, "Light theme caption buttons");
            Check(!info.Text.Contains("Compression Type") && info.Text.Contains("2024-03-19"), "Metadata summary prioritizes shooting date");
            await ShowGallery(fixtureDirectory, CancellationToken.None);
            Check(previewHost.Child is Microsoft.UI.Xaml.Controls.GridView gallery && gallery.Items.Count >= 2, "Folder thumbnails loaded");
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { success = true, checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { success = false, checks = results, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
        finally { root.RequestedTheme = originalTheme; StopPreview(); Close(); }
    }
}
