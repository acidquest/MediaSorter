namespace Sorter.Core;

public static class FileTransferPlan
{
    public static PlanItem Snapshot(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("SourceChanged", path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("ReparseBlocked");
        SortEngine.RejectReparseAncestors(file.DirectoryName!);
        return new(file.FullName, "", file.Length, file.LastWriteTimeUtc, file.LastWriteTime, "ManualTransfer");
    }
    public static List<PlanItem> Create(IEnumerable<PlanItem> snapshots, string destination, CancellationToken token)
    {
        destination = Path.GetFullPath(destination);
        SortEngine.RejectReparseAncestors(destination);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<PlanItem>();
        foreach (var item in snapshots)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(item.Source) || string.Equals(Path.GetDirectoryName(item.Source), Path.TrimEndingDirectorySeparator(destination), StringComparison.OrdinalIgnoreCase)) continue;
            plan.Add(item with { Destination = SortEngine.UniquePath(Path.Combine(destination, Path.GetFileName(item.Source)), reserved) });
        }
        return plan;
    }
}
