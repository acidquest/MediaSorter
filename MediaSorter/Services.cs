using System.Globalization;
using System.Text.Json;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Sorter.Core;
using Windows.Storage;

namespace MediaSorter;

public sealed class Preferences
{
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "system";
    public string Pattern { get; set; } = @"yyyy\MM-dd";
    public static readonly string[] BuiltInPatterns = [@"yyyy\MM-dd", @"yyyy\dd.MM.yy", "yy-MM-dd", "yyyy-MM-dd", "dd.MM.yyyy", @"yyyy\MM\dd", @"yyyy\MM", @"yyyy\MM-yyyy", @"yyyy\'Photos'\MM-dd", @"'Media'\yyyy\MM-dd", @"'Travel'\yyyy-MM-dd", @"yyyy\'Family'\dd-MM"];
    public List<string> Patterns { get; set; } = [.. BuiltInPatterns];
    public bool Recursive { get; set; } = true;
    public double SplitX { get; set; } = .48;
    public double SplitY { get; set; } = .49;
    public double SplitYRight { get; set; } = .49;
    public string[] PanelOrder { get; set; } = ["source", "settings", "output", "preview"];
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaSorter");
    public static Preferences Load()
    {
        try
        {
            var result = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DataDirectory, "settings.json"))) ?? new();
            result.Patterns = result.Patterns.Concat(BuiltInPatterns).Distinct().ToList();
            return result;
        }
        catch { return new(); }
    }
    public void Save()
    {
        System.IO.Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}

public sealed class Localizer
{
    private Dictionary<string, string> words = [];
    private Dictionary<string, string> fallback = [];
    public string this[string key] => words.GetValueOrDefault(key) ?? fallback.GetValueOrDefault(key) ?? key;
    public void Load(string language)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Locales");
        fallback = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, "en.json")))!;
        var code = language == "system" ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : language;
        try { words = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, code + ".json")))!; }
        catch { words = fallback; }
    }
    public static IEnumerable<string> Languages() => System.IO.Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Locales"), "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>();
}

public static class MetadataService
{
    public static async Task<MediaDate> ReadAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var details = new Dictionary<string, string>();
        DateTime? date = null; var source = "";
        try
        {
            var directories = await Task.Run(() => ImageMetadataReader.ReadMetadata(path), token);
            foreach (var directory in directories)
                foreach (var tag in directory.Tags)
                    if (!string.IsNullOrWhiteSpace(tag.Description)) details[$"{directory.Name} / {tag.Name}"] = tag.Description;
            foreach (var tagId in new[] { ExifDirectoryBase.TagDateTimeOriginal, ExifDirectoryBase.TagDateTimeDigitized, ExifDirectoryBase.TagDateTime })
            {
                foreach (var directory in directories.OfType<ExifDirectoryBase>())
                    if (directory.TryGetDateTime(tagId, out var value) && Valid(value)) { date = value; source = tagId == ExifDirectoryBase.TagDateTimeOriginal ? "ExifOriginal" : "ExifDate"; break; }
                if (date != null) break;
            }
            if (date == null)
            {
                foreach (var directory in directories)
                {
                    foreach (var tag in directory.Tags.Where(t => t.Name.Contains("Created", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Creation", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Date/Time Original", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (directory.TryGetDateTime(tag.Type, out var created) && Valid(created))
                        { date = created.Kind == DateTimeKind.Utc ? created.ToLocalTime() : created; source = "EmbeddedDate"; break; }
                    }
                    if (date != null) break;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { details["Metadata reader"] = ex.Message; }
        token.ThrowIfCancellationRequested();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.RetrievePropertiesAsync(new[] { "System.Photo.DateTaken", "System.Media.DateEncoded", "System.Image.HorizontalSize", "System.Image.VerticalSize", "System.Video.FrameWidth", "System.Video.FrameHeight", "System.Media.Duration", "System.Photo.CameraManufacturer", "System.Photo.CameraModel" });
            foreach (var pair in properties)
                if (pair.Value != null) details[pair.Key] = pair.Value.ToString() ?? "";
            if (date == null)
                foreach (var key in new[] { "System.Photo.DateTaken", "System.Media.DateEncoded" })
                    if (properties.TryGetValue(key, out var value) && value is DateTimeOffset offset && Valid(offset.DateTime)) { date = offset.LocalDateTime; source = key == "System.Photo.DateTaken" ? "DateTaken" : "DateEncoded"; break; }
        }
        catch (Exception ex) { details["Windows metadata"] = ex.Message; }
        var info = new FileInfo(path);
        // Modification time normally survives copying from a device; creation time often becomes the import date.
        if (date == null && Valid(info.LastWriteTime)) { date = info.LastWriteTime; source = "Modified"; }
        if (date == null && Valid(info.CreationTime)) { date = info.CreationTime; source = "Created"; }
        if (date == null) throw new IOException("NoDate");
        details["File"] = info.Name;
        details["Bytes"] = info.Length.ToString("N0");
        details["Created"] = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
        details["Modified"] = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        return new(date.Value, source, details);
    }
    private static bool Valid(DateTime date) => date.Year is >= 1900 and <= 2200;
}
