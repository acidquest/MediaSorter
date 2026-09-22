using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sorter.Core;

public enum MediaKind { Photo, Video }
public sealed record MediaDate(DateTime Date, string Source, IReadOnlyDictionary<string, string> Details);
public sealed record PlanItem(string Source, string Destination, long Length, DateTime ModifiedUtc, DateTime Date, string DateSource, string? Error = null);
public sealed record MoveResult(PlanItem Item, bool Success, string? Error);

public static class MediaFiles
{
    private static readonly HashSet<string> Photos = new(".jpg .jpeg .png .heic .heif .avif .webp .tif .tiff .bmp .gif .dng .cr2 .cr3 .nef .arw .orf .rw2 .raf".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Videos = new(".mp4 .mov .m4v .avi .mkv .mts .m2ts .wmv .webm .3gp .mpg .mpeg".Split(' '), StringComparer.OrdinalIgnoreCase);
    public static MediaKind? Kind(string path) => Photos.Contains(Path.GetExtension(path)) ? MediaKind.Photo : Videos.Contains(Path.GetExtension(path)) ? MediaKind.Video : null;
    public static bool IsWithin(string path, string parent) => Path.GetFullPath(path).Equals(Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static IEnumerable<string> Enumerate(string source, string? excluded, bool recursive, MediaKind? kind, CancellationToken token)
    {
        var pending = new Stack<string>(); pending.Push(Path.GetFullPath(source));
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                var k = Kind(file);
                if (k != null && (kind == null || k == kind)) yield return file;
            }
            if (!recursive) continue;
            foreach (var child in Directory.EnumerateDirectories(folder))
                if ((excluded == null || !IsWithin(child, excluded)) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
        }
    }
}

public static partial class FolderPattern
{
    [GeneratedRegex("yyyy|yy|MM|mm|dd", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();
    public static string Format(string pattern, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("PatternEmpty");
        var parts = pattern.Replace('/', '\\').TrimEnd('\\').Split('\\');
        if (parts.Length > 12) throw new ArgumentException("PatternInvalid");
        var result = new List<string>();
        foreach (var part in parts)
        {
            var expanded = new StringBuilder();
            for (var i = 0; i < part.Length;)
            {
                if (part[i] == '\'')
                {
                    var end = part.IndexOf('\'', i + 1);
                    if (end < 0) throw new ArgumentException("PatternQuotes");
                    expanded.Append(part[(i + 1)..end]); i = end + 1;
                }
                else
                {
                    var match = Tokens().Match(part, i);
                    if (match.Success && match.Index == i)
                    {
                        expanded.Append(date.ToString(match.Value == "mm" ? "MM" : match.Value, CultureInfo.InvariantCulture));
                        i += match.Length;
                    }
                    else expanded.Append(part[i++]);
                }
            }
            var name = expanded.ToString(); ValidateName(name); result.Add(name);
        }
        return Path.Combine(result.ToArray());
    }
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || name.Any(char.IsControl)) throw new ArgumentException("PatternInvalid");
        var stem = name.Split('.')[0];
        if (Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) throw new ArgumentException("PatternInvalid");
    }
}

public sealed class SortEngine(Func<string, CancellationToken, Task<MediaDate>> metadata)
{
    public async Task<List<PlanItem>> PlanAsync(string source, string output, string pattern, bool recursive, MediaKind? filter, IProgress<int>? progress, CancellationToken token)
    {
        source = Path.GetFullPath(source); output = Path.GetFullPath(output);
        if (MediaFiles.IsWithin(source, output)) throw new ArgumentException("OutputInvalid");
        FolderPattern.Format(pattern, DateTime.Today);
        RejectReparseAncestors(source); RejectReparseAncestors(output);
        var result = new List<PlanItem>(); var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in MediaFiles.Enumerate(source, output, recursive, filter, token))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                var date = await metadata(file, token);
                var target = UniquePath(Path.Combine(output, FolderPattern.Format(pattern, date.Date), info.Name), reserved);
                if (!MediaFiles.IsWithin(target, output)) throw new IOException("OutputInvalid");
                result.Add(new(file, target, info.Length, info.LastWriteTimeUtc, date.Date, date.Source));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Add(new(file, "", 0, default, default, "", ex.Message)); }
            progress?.Report(result.Count);
        }
        return result;
    }
    public static string UniquePath(string path, HashSet<string>? reserved = null)
    {
        var candidate = path; var n = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate) || reserved?.Contains(candidate) == true)
            candidate = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)} ({n++}){Path.GetExtension(path)}");
        reserved?.Add(candidate); return candidate;
    }
    public static void RejectReparseAncestors(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("ReparseBlocked");
    }
    public static async Task MoveVerifiedAsync(PlanItem item, CancellationToken token, bool forceVerifiedCopy = false)
    {
        token.ThrowIfCancellationRequested();
        var info = new FileInfo(item.Source);
        if (!info.Exists || info.Length != item.Length || info.LastWriteTimeUtc != item.ModifiedUtc) throw new IOException("SourceChanged");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("ReparseBlocked");
        RejectReparseAncestors(Path.GetDirectoryName(item.Source)!);
        RejectReparseAncestors(Path.GetDirectoryName(item.Destination)!);
        Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
        if (!forceVerifiedCopy && Path.GetPathRoot(item.Source)!.Equals(Path.GetPathRoot(item.Destination), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(item.Source, item.Destination, false); return;
        }
        // Cross-volume: copy to an exclusive temporary file, verify, commit, then delete the source.
        var temporary = item.Destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            byte[] originalHash;
            await using (var input = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                { await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(true); }
                input.Position = 0; originalHash = await SHA256.HashDataAsync(input, token);
                await using var verify = File.OpenRead(temporary);
                var copiedHash = await SHA256.HashDataAsync(verify, token);
                if (!originalHash.SequenceEqual(copiedHash)) throw new IOException("CopyVerificationFailed");
            }
            info.Refresh();
            if (info.Length != item.Length || info.LastWriteTimeUtc != item.ModifiedUtc) throw new IOException("SourceChanged");
            File.SetLastWriteTimeUtc(temporary, item.ModifiedUtc);
            File.SetCreationTimeUtc(temporary, info.CreationTimeUtc);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, item.Destination, false);
            File.Delete(item.Source);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task<List<MoveResult>> ExecuteAsync(IReadOnlyList<PlanItem> plan, string journalPath, IProgress<int>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        await using var journal = new StreamWriter(new FileStream(journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        var results = new List<MoveResult>();
        foreach (var item in plan)
        {
            if (token.IsCancellationRequested) break;
            MoveResult result;
            try
            {
                if (item.Error != null) throw new IOException(item.Error);
                await journal.WriteLineAsync(JsonSerializer.Serialize(new { state = "pending", item })); await journal.FlushAsync();
                await MoveVerifiedAsync(item, token); result = new(item, true, null);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { result = new(item, false, ex.Message); }
            results.Add(result);
            await journal.WriteLineAsync(JsonSerializer.Serialize(result)); await journal.FlushAsync();
            progress?.Report(results.Count);
        }
        return results;
    }
}
