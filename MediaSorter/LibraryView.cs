using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MediaSorter;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, double> libraryOffsets = new(StringComparer.OrdinalIgnoreCase);
    private async Task EnterOutput(string path)
    {
        if (busy) return;
        if (!string.IsNullOrEmpty(outputCurrent)) libraryOffsets[outputCurrent] = FindScrollViewer(outputList)?.VerticalOffset ?? 0;
        outputCurrent = path;
        await RefreshOutput();
    }
    private async Task NavigateUp()
    {
        if (busy || string.IsNullOrEmpty(outputCurrent) || outputCurrent.Equals(outputRoot, StringComparison.OrdinalIgnoreCase)) return;
        var child = outputCurrent; var parent = System.IO.Path.GetDirectoryName(child);
        if (parent == null || !Sorter.Core.MediaFiles.IsWithin(parent, outputRoot)) return;
        outputCurrent = parent; await RefreshOutput();
        var entry = outputItems.FirstOrDefault(x => x.Path.Equals(child, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;
        syncingPreviewSelection = true;
        try { outputList.SelectedItem = entry; }
        finally { syncingPreviewSelection = false; }
        RestoreOffset(outputList, libraryOffsets.GetValueOrDefault(parent));
        outputList.ScrollIntoView(entry, ScrollIntoViewAlignment.Default);
        outputList.UpdateLayout();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (outputList.SelectedItem == entry && outputList.ContainerFromItem(entry) is ListViewItem container) container.Focus(FocusState.Programmatic);
        });
    }
    private static void RestoreOffset(ListView list, double offset)
    {
        list.UpdateLayout(); var scroll = FindScrollViewer(list);
        scroll?.ChangeView(null, Math.Min(offset, scroll.ScrollableHeight), null, true);
    }
    private void RemoveSourceEntries(IEnumerable<FileEntry> removed, double offset)
    {
        var paths = removed.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        syncingPreviewSelection = true;
        try { for (var i = sourceItems.Count - 1; i >= 0; i--) if (paths.Contains(sourceItems[i].Path)) sourceItems.RemoveAt(i); }
        finally { syncingPreviewSelection = false; }
        count.Text = $"{sourceItems.Count:N0} {T("Files")}";
        RestoreOffset(sourceList, offset);
    }
    private void ReplaceRenamedEntry(FileEntry oldEntry, string destination, double offset)
    {
        var index = outputItems.IndexOf(oldEntry);
        if (index < 0) return;
        var replacement = Entry(destination, oldEntry.IsDirectory);
        syncingPreviewSelection = true;
        try
        {
            outputItems[index] = replacement;
            outputList.SelectedItem = replacement;
            for (var i = 0; i < sourceItems.Count; i++)
            {
                var item = sourceItems[i];
                if (item.Path.Equals(oldEntry.Path, StringComparison.OrdinalIgnoreCase)) sourceItems[i] = Entry(destination);
                else if (oldEntry.IsDirectory && Sorter.Core.MediaFiles.IsWithin(item.Path, oldEntry.Path))
                    sourceItems[i] = Entry(System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(oldEntry.Path, item.Path)));
            }
        }
        finally { syncingPreviewSelection = false; }
        SetPreviewItems(outputItems, outputList);
        UpdateOutputStatistics(); RestoreOffset(outputList, offset);
        if (outputList.ContainerFromItem(replacement) is ListViewItem container) container.Focus(FocusState.Programmatic);
    }
    private static void FocusPrimaryButton(ContentDialog dialog)
    {
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Opened += async (_, _) =>
        {
            // ContentDialog completes its initial content focus after the Opened event.
            await Task.Delay(100);
            if (!dialog.IsLoaded) return;
            FindNamedButton(dialog, "PrimaryButton")?.Focus(FocusState.Programmatic);
        };
    }
    private static Button? FindNamedButton(DependencyObject node, string name)
    {
        if (node is Button button && button.Name == name) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindNamedButton(VisualTreeHelper.GetChild(node, i), name) is { } found) return found;
        return null;
    }
    private void UpdateOutputStatistics()
    {
        if (outputStatistics == null) return;
        var folders = outputItems.Count(x => x.IsDirectory);
        var files = outputItems.Count - folders;
        outputStatistics.Text = folders > 0 && files > 0 ? $"{T("FoldersCount")}: {folders:N0} · {T("FilesCount")}: {files:N0}"
            : folders > 0 ? $"{T("FoldersCount")}: {folders:N0}" : $"{T("FilesCount")}: {files:N0}";
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
    private void RemoveOutputEntries(IEnumerable<FileEntry> removed, double offset)
    {
        // Update the existing collection: keep containers, selection and scroll state for surviving items.
        var paths = removed.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        syncingPreviewSelection = true;
        try
        {
            for (var i = outputItems.Count - 1; i >= 0; i--)
                if (paths.Contains(outputItems[i].Path)) outputItems.RemoveAt(i);
        }
        finally { syncingPreviewSelection = false; }
        UpdateOutputStatistics();
        outputList.UpdateLayout();
        var scroll = FindScrollViewer(outputList);
        scroll?.ChangeView(null, Math.Min(offset, scroll.ScrollableHeight), null, true);
    }
}
