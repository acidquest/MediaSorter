using Sorter.Core;

namespace MediaSorter;

public sealed partial class MainWindow
{
    private List<PlanItem> cutFiles = [];
    private const string MiscFolderName = "Разное";

    private async void CutSelected()
    {
        if (busy) return;
        try
        {
            var selected = outputList.SelectedItems.Cast<FileEntry>().ToArray();
            if (selected.Length == 0 || selected.Any(x => x.IsDirectory)) throw new InvalidOperationException("SelectFiles");
            SetBusy(true, T("Working"));
            cutFiles = await Task.Run(() => selected.Select(x => FileTransferPlan.Snapshot(x.Path)).ToList());
            status.Text = $"{T("CutReady")}: {cutFiles.Count}. {T("PasteHint")}";
        }
        catch (Exception ex) { _ = Error(ex); }
        finally { SetBusy(false); }
    }
    private async Task PasteFiles()
    {
        if (busy) return;
        try
        {
            if (cutFiles.Count == 0) throw new InvalidOperationException("PasteEmpty");
            var destination = outputList.SelectedItems.Count == 1 && outputList.SelectedItem is FileEntry { IsDirectory: true } folder ? folder.Path : outputCurrent;
            if (!MediaFiles.IsWithin(destination, outputRoot) || cutFiles.Any(x => !MediaFiles.IsWithin(x.Source, outputRoot))) throw new IOException("OutputInvalid");
            SetBusy(true, T("Working"));
            var token = operation!.Token;
            var snapshots = cutFiles.ToArray();
            var plan = await Task.Run(() => FileTransferPlan.Create(snapshots, destination, token), token);
            if (plan.Count == 0) { status.Text = T("AlreadyThere"); return; }
            if (!await ShowPlan(plan, true, T("Move"))) return;
            var results = await Execute(plan, token);
            ApplyTransferResults(results);
        }
        catch (OperationCanceledException) { status.Text = T("Cancelled"); }
        catch (Exception ex) { await Error(ex); }
        finally { SetBusy(false); }
    }
    private async Task MoveToMisc()
    {
        if (busy || string.IsNullOrWhiteSpace(outputRoot)) return;
        try
        {
            var selected = outputList.SelectedItems.Count > 0 ? outputList.SelectedItems.Cast<FileEntry>().ToArray() : outputItems.ToArray();
            var destination = Path.Combine(outputRoot, MiscFolderName);
            SetBusy(true, T("Working")); var token = operation!.Token;
            var plan = await Task.Run(() => BuildMiscPlan(selected, destination, token), token);
            if (plan.Count == 0) { status.Text = T("NoItems"); return; }
            if (!await ShowPlan(plan, true, T("ToMisc"), T("MiscCleanupNotice"))) return;
            var results = await Execute(plan, token);
            ApplyTransferResults(results);
            await CleanupMiscFolders(selected, plan, results, destination, token);
        }
        catch (OperationCanceledException) { status.Text = T("Cancelled"); }
        catch (Exception ex) { await Error(ex); }
        finally { SetBusy(false); }
    }
    private async Task CleanupMiscFolders(IEnumerable<FileEntry> selected, IReadOnlyList<PlanItem> plan, IReadOnlyList<MoveResult> results, string destination, CancellationToken token)
    {
        var succeeded = results.Where(x => x.Success).Select(x => x.Item.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = new List<FileEntry>(); var errors = new List<string>();
        var offset = FindScrollViewer(outputList)?.VerticalOffset ?? 0;
        foreach (var folder in selected.Where(x => x.IsDirectory))
        {
            if (token.IsCancellationRequested) break;
            var path = Path.GetFullPath(folder.Path);
            if (!MediaFiles.IsWithin(path, outputRoot) || path.Equals(Path.TrimEndingDirectorySeparator(outputRoot), StringComparison.OrdinalIgnoreCase)
                || MediaFiles.IsWithin(path, destination) || MediaFiles.IsWithin(destination, path)) continue;
            var folderPlan = plan.Where(x => MediaFiles.IsWithin(x.Source, path)).ToArray();
            if (folderPlan.Length == 0 || folderPlan.Any(x => !succeeded.Contains(x.Source))) continue;
            try
            {
                var deleted = await Task.Run(() =>
                {
                    SortEngine.RejectReparseAncestors(path);
                    if (!HasOnlyEmptyDirectories(path)) return false;
                    token.ThrowIfCancellationRequested();
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                    return true;
                });
                if (deleted) removed.Add(folder);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { errors.Add(folder.Name + ": " + T(ex.Message)); }
        }
        RemoveOutputEntries(removed, offset);
        if (errors.Count > 0) await Dialog(T("Errors"), Text(string.Join("\n", errors)));
    }
    private static bool HasOnlyEmptyDirectories(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0 || !HasOnlyEmptyDirectories(entry)) return false;
        }
        return true;
    }
    private List<PlanItem> BuildMiscPlan(IEnumerable<FileEntry> selected, string destination, CancellationToken token)
    {
        var files = new List<string>();
        foreach (var entry in selected)
        {
            token.ThrowIfCancellationRequested();
            if (!MediaFiles.IsWithin(entry.Path, outputRoot)) throw new IOException("OutputInvalid");
            if (MediaFiles.IsWithin(entry.Path, destination)) continue;
            if (entry.IsDirectory)
            {
                SortEngine.RejectReparseAncestors(entry.Path);
                files.AddRange(MediaFiles.Enumerate(entry.Path, destination, true, null, token));
            }
            else if (MediaFiles.Kind(entry.Path) != null) files.Add(entry.Path);
        }
        return FileTransferPlan.Create(files.Select(FileTransferPlan.Snapshot), destination, token);
    }
    private void ApplyTransferResults(IReadOnlyList<MoveResult> results)
    {
        ++outputVersion; ++sourceVersion;
        var successful = results.Where(x => x.Success).ToArray();
        var offset = FindScrollViewer(outputList)?.VerticalOffset ?? 0;
        var sourceOffset = FindScrollViewer(sourceList)?.VerticalOffset ?? 0;
        var removed = successful.Select(x => new FileEntry { Path = x.Item.Source }).ToArray();
        RemoveOutputEntries(removed, offset); RemoveSourceEntries(removed, sourceOffset);
        var moved = successful.Select(x => x.Item.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        cutFiles.RemoveAll(x => moved.Contains(x.Source));
        foreach (var result in successful)
        {
            if (!MediaFiles.IsWithin(result.Item.Destination, outputCurrent)) continue;
            var relative = Path.GetRelativePath(outputCurrent, result.Item.Destination);
            var first = relative.Split(Path.DirectorySeparatorChar)[0];
            var visiblePath = Path.Combine(outputCurrent, first);
            if (outputItems.Any(x => x.Path.Equals(visiblePath, StringComparison.OrdinalIgnoreCase))) continue;
            outputItems.Add(Entry(visiblePath, !visiblePath.Equals(result.Item.Destination, StringComparison.OrdinalIgnoreCase)));
        }
        UpdateOutputStatistics(); RestoreOffset(outputList, offset);
    }
}
