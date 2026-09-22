using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MediaSorter;

public sealed partial class MainWindow
{
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
