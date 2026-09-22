using Sorter.Core;

namespace MediaSorter;

public static class MetadataSummary
{
    public static string Format(string path, MediaDate metadata, Localizer loc)
    {
        var file = new FileInfo(path);
        var d = metadata.Details;
        string? Value(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (d.TryGetValue(key, out var exact) && !string.IsNullOrWhiteSpace(exact) && exact != "0") return exact;
                var found = d.FirstOrDefault(x => x.Key.EndsWith(" / " + key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Value) && x.Value != "0");
                if (found.Value != null) return found.Value;
            }
            return null;
        }
        var lines = new List<string> { file.Name, "", $"{loc["DateUsed"]}: {metadata.Date:yyyy-MM-dd HH:mm:ss}", loc[metadata.Source], "", $"{loc["Format"]}: {file.Extension.TrimStart('.').ToUpperInvariant()}", $"{loc["Size"]}: {file.Length / 1048576d:0.##} MB" };
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{loc[label]}: {value}"); }
        var width = Value("System.Image.HorizontalSize", "System.Video.FrameWidth", "Exif Image Width", "Image Width");
        var height = Value("System.Image.VerticalSize", "System.Video.FrameHeight", "Exif Image Height", "Image Height");
        if (width != null && height != null) Add("Dimensions", width + " × " + height);
        if (ulong.TryParse(Value("System.Media.Duration"), out var duration) && duration > 0) Add("Duration", TimeSpan.FromTicks((long)Math.Min(duration, (ulong)long.MaxValue)).ToString(@"hh\:mm\:ss"));
        Add("Camera", Value("System.Photo.CameraModel", "Model"));
        Add("Lens", Value("Lens Model", "Lens Specification"));
        Add("Exposure", Value("Exposure Time"));
        Add("Aperture", Value("F-Number"));
        Add("ISO", Value("ISO Speed Ratings", "ISO Speed"));
        Add("FocalLength", Value("Focal Length"));
        lines.Add(""); Add("Modified", file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
        Add("Created", file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
        lines.Add(""); Add("Location", file.DirectoryName);
        return string.Join("\n", lines);
    }
}
