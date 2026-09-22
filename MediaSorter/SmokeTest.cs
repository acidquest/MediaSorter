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
            var nativeModules = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().ToArray();
            Check(new[] { "coreclr.dll", "Microsoft.ui.xaml.dll" }.All(name => nativeModules.Any(module => module.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(module.FileName)!.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))), "Portable app loads .NET and WinUI from its own directory");
            var toolbarLabels = libraryToolbar.Children.Cast<Microsoft.UI.Xaml.Controls.Button>().SelectMany(button => ((Microsoft.UI.Xaml.Controls.StackPanel)button.Content).Children.OfType<Microsoft.UI.Xaml.Controls.TextBlock>()).ToArray();
            Check(toolbarLabels.Length == 0 && libraryToolbar.Children.Count == 7, "Library actions always use icons without labels");
            var compactButtons = operationControls.OfType<Microsoft.UI.Xaml.Controls.Button>().Where(button => button.Width == 36).ToArray();
            Check(compactButtons.Length == 12 && compactButtons.All(button => button.Content is Microsoft.UI.Xaml.Controls.StackPanel content && !content.Children.OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Any()), "Library, source, browse and save buttons are icon-only");
            Check(libraryToolbar.Children.Cast<Microsoft.UI.Xaml.Controls.Button>().Count(button => ((Microsoft.UI.Xaml.Controls.StackPanel)button.Content).Children.OfType<Microsoft.UI.Xaml.Controls.PathIcon>().Any()) == 2, "Merge and Misc use distinct custom vector icons");
            var saveButton = compactButtons.Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == T("Save"));
            Check(Math.Abs(saveButton.ActualHeight - pattern.ActualHeight) < 1 && Math.Abs(saveButton.TransformToVisual(root).TransformPoint(default).Y - pattern.TransformToVisual(root).TransformPoint(default).Y) < 1, "Save button aligns exactly with pattern selector");
            var sortButton = operationControls.OfType<Microsoft.UI.Xaml.Controls.Button>().Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == T("Sort"));
            Check(ReferenceEquals(sortButton.Parent, filter.Parent) && ReferenceEquals(sortButton.Parent, recursive.Parent), "Sort action shares row with filter and recursive checkbox");
            Check(libraryToolbar.Children.Cast<Microsoft.UI.Xaml.Controls.Button>().All(button => Microsoft.UI.Xaml.Controls.ToolTipService.GetToolTip(button) != null && !string.IsNullOrEmpty(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button))), "Icon buttons retain tooltips and accessible names");
            DependencyObject? toolbarParent = libraryToolbar;
            while (toolbarParent != null && toolbarParent is not Microsoft.UI.Xaml.Controls.ScrollViewer) toolbarParent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(toolbarParent);
            Check(toolbarParent is Microsoft.UI.Xaml.Controls.ScrollViewer toolbarViewport && toolbarViewport.VerticalScrollMode == Microsoft.UI.Xaml.Controls.ScrollMode.Disabled && toolbarViewport.VerticalScrollBarVisibility == Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled, "Library toolbar cannot show vertical scrollbar arrows");
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
            var darkExampleColor = ((Microsoft.UI.Xaml.Media.SolidColorBrush)example.Foreground).Color;
            var darkButtonColor = ((Microsoft.UI.Xaml.Media.SolidColorBrush)saveButton.Foreground).Color;
            Check(root.ActualTheme == ElementTheme.Dark && AppWindow.TitleBar.ButtonForegroundColor == Microsoft.UI.Colors.White, "Dark theme caption buttons");
            root.RequestedTheme = ElementTheme.Light; await Task.Delay(100);
            Check(darkExampleColor != ((Microsoft.UI.Xaml.Media.SolidColorBrush)example.Foreground).Color && darkButtonColor != ((Microsoft.UI.Xaml.Media.SolidColorBrush)saveButton.Foreground).Color, "Example and action colors adapt to light and dark themes");
            Check(root.ActualTheme == ElementTheme.Light && AppWindow.TitleBar.ButtonForegroundColor != Microsoft.UI.Colors.White, "Light theme caption buttons");
            Check(((Microsoft.UI.Xaml.Media.SolidColorBrush)root.Background).Color.A == 255 && ((Microsoft.UI.Xaml.Media.SolidColorBrush)panels["output"].Background).Color == Microsoft.UI.Colors.White && ((Microsoft.UI.Xaml.Media.SolidColorBrush)root.Background).Color != Microsoft.UI.Colors.White, "Light theme separates opaque canvas and white cards");
            Check(((Microsoft.UI.Xaml.Media.SolidColorBrush)saveButton.Background).Color.A == 255 && previewHost.BorderThickness.Left == 1, "Light theme uses solid pastel buttons and framed preview");
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
            await NavigatePreviewWheel(120); Check(PreviewIndex == 0 && previewImage != null, "Mouse wheel up selects previous photo");
            await Task.Delay(200);
            await NavigatePreviewWheel(-120); Check(PreviewIndex == 1 && player != null, "Mouse wheel down selects next video");
            await Task.Delay(200);
            await NavigatePreviewWheel(-120); Check(PreviewIndex == 1, "Mouse wheel respects end of list");
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
            outputItems.Clear();
            for (var i = 0; i < 120; i++) outputItems.Add(new FileEntry { Path = Path.Combine(fixtureDirectory, "folder-" + i.ToString("D3")), IsDirectory = true });
            UpdateOutputStatistics(); outputList.UpdateLayout(); await Task.Delay(150);
            Check(outputStatistics.Text == T("FoldersCount") + ": 120", "Library footer counts folders");
            var libraryScroll = FindScrollViewer(outputList)!;
            libraryScroll.ChangeView(null, 950, null, true); await Task.Delay(150);
            var previousOffset = libraryScroll.VerticalOffset;
            Check(previousOffset > 500, "Library regression fixture is scrolled away from start");
            var collection = outputList.ItemsSource;
            RemoveOutputEntries(new[] { outputItems[22] }, previousOffset); await Task.Delay(150);
            Check(ReferenceEquals(collection, outputList.ItemsSource) && outputItems.Count == 119, "Deleting library item updates the existing list");
            Check(Math.Abs(libraryScroll.VerticalOffset - previousOffset) < 2, "Library deletion preserves scroll offset");
            var renamed = outputItems[24];
            ReplaceRenamedEntry(renamed, renamed.Path + "-renamed", previousOffset); await Task.Delay(150);
            Check(outputItems[24].Path.EndsWith("-renamed") && Math.Abs(libraryScroll.VerticalOffset - previousOffset) < 2, "Renaming library folder preserves index and scroll");
            sourceItems.Clear();
            for (var i = 0; i < 120; i++) sourceItems.Add(new FileEntry { Path = Path.Combine(fixtureDirectory, $"source-{i:D3}.jpg") });
            sourceList.UpdateLayout(); await Task.Delay(150);
            var sourceScroll = FindScrollViewer(sourceList)!; sourceScroll.ChangeView(null, 950, null, true); await Task.Delay(150);
            var sourceOffset = sourceScroll.VerticalOffset; var sourceCollection = sourceList.ItemsSource;
            RemoveSourceEntries(new[] { sourceItems[25] }, sourceOffset); await Task.Delay(150);
            Check(sourceOffset > 500 && sourceItems.Count == 119 && ReferenceEquals(sourceCollection, sourceList.ItemsSource) && Math.Abs(sourceScroll.VerticalOffset - sourceOffset) < 2, "Source deletion preserves collection and scroll");
            var focusDialog = new Microsoft.UI.Xaml.Controls.ContentDialog { XamlRoot = root.XamlRoot, Content = new Microsoft.UI.Xaml.Controls.TextBox { Text = "rename" }, PrimaryButtonText = T("Confirm"), CloseButtonText = T("Close") };
            FocusPrimaryButton(focusDialog); var focusShowing = focusDialog.ShowAsync(); await Task.Delay(300);
            Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot), FindNamedButton(focusDialog, "PrimaryButton")), "Rename dialog focuses Continue");
            focusDialog.Hide(); await focusShowing;
            var navigationRoot = Path.Combine(fixtureDirectory, "navigation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(navigationRoot);
            for (var i = 0; i < 100; i++) Directory.CreateDirectory(Path.Combine(navigationRoot, $"folder-{i:D3}"));
            outputRoot = outputCurrent = navigationRoot; await RefreshOutput(); outputList.UpdateLayout(); await Task.Delay(150);
            libraryScroll.ChangeView(null, 950, null, true); await Task.Delay(150);
            var childPath = outputItems[25].Path;
            await EnterOutput(childPath); await NavigateUp(); await Task.Delay(200);
            Check(outputList.SelectedItem is FileEntry selectedParent && selectedParent.Path == childPath, "Up selects the folder just exited");
            Check(libraryScroll.VerticalOffset > 500, "Up does not jump to the first folder");
            var miscSource = Path.Combine(childPath, "camera.jpg"); File.Copy(Path.Combine(fixtureDirectory, "camera.jpg"), miscSource);
            var miscPath = Path.Combine(outputRoot, MiscFolderName); Directory.CreateDirectory(miscPath);
            var existingMisc = Path.Combine(miscPath, "camera.jpg"); File.Copy(miscSource, existingMisc);
            var miscPlan = BuildMiscPlan(new[] { Entry(childPath, true), Entry(miscPath, true) }, miscPath, default);
            Check(miscPlan.Count == 1 && miscPlan[0].Destination.EndsWith("camera (2).jpg"), "Misc excludes itself and resolves collisions");
            var miscResults = await Sorter.Core.SortEngine.ExecuteAsync(miscPlan, Path.Combine(navigationRoot, "journal.jsonl"), null, default);
            ApplyTransferResults(miscResults);
            Check(miscResults.Single().Success && !File.Exists(miscSource) && File.Exists(existingMisc) && File.Exists(miscPlan[0].Destination), "Misc moves without keeping originals or overwriting");
            Directory.CreateDirectory(Path.Combine(childPath, "empty-nested"));
            await CleanupMiscFolders(new[] { Entry(childPath, true) }, miscPlan, Array.Empty<Sorter.Core.MoveResult>(), miscPath, default);
            Check(Directory.Exists(childPath), "Incomplete transfers do not remove source folders");
            var keepNote = Path.Combine(childPath, "keep.txt"); await File.WriteAllTextAsync(keepNote, "Synthetic non-media fixture");
            await CleanupMiscFolders(new[] { Entry(childPath, true) }, miscPlan, miscResults, miscPath, default);
            Check(File.Exists(keepNote), "Misc cleanup preserves folders containing non-media files");
            File.Delete(keepNote);
            await CleanupMiscFolders(new[] { Entry(childPath, true), Entry(miscPath, true), Entry(outputRoot, true) }, miscPlan, miscResults, miscPath, default);
            Check(!Directory.Exists(childPath) && !outputItems.Any(x => x.Path == childPath), "Successful whole-folder transfer recycles emptied tree and removes list entry");
            Check(Directory.Exists(miscPath) && Directory.Exists(outputRoot), "Misc cleanup preserves destination and library root");
            await ShowGallery(miscPath, default);
            var miscGallery = (Microsoft.UI.Xaml.Controls.GridView)previewHost.Child;
            Check(miscGallery.Items.Cast<Microsoft.UI.Xaml.Controls.StackPanel>().All(tile => ((Microsoft.UI.Xaml.Controls.Image)tile.Children[0]).Stretch == Microsoft.UI.Xaml.Media.Stretch.Uniform), "Thumbnails fit portrait images without cropping");
            outputItems.Clear(); outputItems.Add(Entry(Path.Combine(fixtureDirectory, "camera.jpg"))); outputItems.Add(Entry(Path.Combine(fixtureDirectory, "sample.mp4"))); UpdateOutputStatistics();
            Check(outputStatistics.Text == T("FilesCount") + ": 2", "Library footer counts files inside folder");
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { success = true, checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { success = false, checks = results, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
        finally { preferences.SplitX = originalX; preferences.SplitY = originalY; preferences.SplitYRight = originalRightY; root.RequestedTheme = originalTheme; StopPreview(); Close(); }
    }
}
