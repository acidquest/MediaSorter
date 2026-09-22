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
        var originalX = preferences.SplitX; var originalY = preferences.SplitY; var originalRightY = preferences.SplitYRight;
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
            SetPreviewItems(new[] { Entry(Path.Combine(fixtureDirectory, "camera.jpg")), Entry(Path.Combine(fixtureDirectory, "sample.mp4")) }, null);
            await NavigatePreview("first");
            Check(PreviewIndex == 0 && !firstMedia!.IsEnabled && !previousMedia!.IsEnabled && nextMedia!.IsEnabled, "Viewer first file and boundary buttons");
            await NavigatePreview("next");
            Check(PreviewIndex == 1 && player != null && !nextMedia!.IsEnabled && !lastMedia!.IsEnabled, "Viewer next video and last boundary");
            await NavigatePreview("previous"); Check(PreviewIndex == 0 && previewImage != null, "Viewer previous photo");
            await NavigatePreview("last"); Check(PreviewIndex == 1, "Viewer last file");
            StopPreview();
            ResizeVertical(-10000); ResizeHorizontal(leftColumn, -10000, false); await Task.Delay(100);
            Check(workspace.ColumnDefinitions[0].ActualWidth < 150 && leftColumn.RowDefinitions[0].ActualHeight < 110, "Panels shrink to 120 by 80 DIP");
            preferences.SplitX = originalX; preferences.SplitY = originalY; preferences.SplitYRight = originalRightY;
            workspace.ColumnDefinitions[0].Width = new(originalX, GridUnitType.Star); workspace.ColumnDefinitions[2].Width = new(1 - originalX, GridUnitType.Star);
            leftColumn.RowDefinitions[0].Height = new(originalY, GridUnitType.Star); leftColumn.RowDefinitions[2].Height = new(1 - originalY, GridUnitType.Star);
            outputRoot = Path.Combine(fixtureDirectory, "out");
            var planned = new List<Sorter.Core.PlanItem>
            {
                new(Path.Combine(fixtureDirectory, "camera.jpg"), Path.Combine(outputRoot, "2024", "03-19", "camera (2).jpg"), 12345, default, new DateTime(2024,3,19), "ExifOriginal"),
                new(Path.Combine(fixtureDirectory, "sample.mp4"), Path.Combine(outputRoot, "2026", "09-22", "sample.mp4"), 987654, default, new DateTime(2026,9,22), "Modified"),
                new(Path.Combine(fixtureDirectory, "missing.jpg"), "", 0, default, default, "", "SourceChanged")
            };
            var groups = PlanGroups(planned);
            Check(groups.Count == 3 && groups[0].Files[0].Notice == T("PlanRenamed") && groups[2].Name == T("PlanBlocked"), "Plan groups destinations, rename conflicts and failures separately");
            var planPanel = BuildPlanPreview(planned);
            var planDialog = new Microsoft.UI.Xaml.Controls.ContentDialog { XamlRoot = root.XamlRoot, Content = planPanel, Title = T("Plan"), CloseButtonText = T("Close") };
            planDialog.Resources["ContentDialogMaxWidth"] = 820d;
            var showing = planDialog.ShowAsync(); await Task.Delay(300);
            Check(planPanel.ActualWidth > 300 && planPanel.ActualHeight > 200, "Visual folder preview dialog renders");
            planDialog.Hide(); await showing;
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { success = true, checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { success = false, checks = results, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
        finally { preferences.SplitX = originalX; preferences.SplitY = originalY; preferences.SplitYRight = originalRightY; root.RequestedTheme = originalTheme; StopPreview(); Close(); }
    }
}
