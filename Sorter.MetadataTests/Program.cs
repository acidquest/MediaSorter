using MediaSorter;
using Windows.Graphics.Imaging;
using Windows.Storage;

var keepFixtures = args.Length == 2 && args[0] == "--fixtures";
var root = keepFixtures ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "MediaSorter-metadata-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var folder = await StorageFolder.GetFolderFromPathAsync(root);
    var file = await folder.CreateFileAsync("camera.jpg", CreationCollisionOption.ReplaceExisting);
    using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
    {
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        var pixels = Enumerable.Range(0, 32 * 32).SelectMany(i => new byte[] { (byte)(i % 256), 120, 210, 255 }).ToArray();
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, 32, 32, 96, 96, pixels);
        await encoder.FlushAsync();
    }
    var properties = await file.Properties.GetImagePropertiesAsync();
    properties.DateTaken = new DateTimeOffset(2024, 3, 19, 14, 25, 0, TimeSpan.FromHours(3));
    await properties.SavePropertiesAsync();
    File.SetLastWriteTime(file.Path, new DateTime(2026, 9, 22, 10, 0, 0));
    var metadata = await MetadataService.ReadAsync(file.Path, default);
    if (metadata.Date.Year != 2024 || metadata.Date.Month != 3 || metadata.Date.Day != 19 || metadata.Source is "Modified" or "Created") throw new Exception("Embedded photo date was not prioritized: " + metadata);
    Console.WriteLine($"PASS real JPEG embedded date: {metadata.Date:O} ({metadata.Source}), {metadata.Details.Count} metadata fields");
    var video = Path.Combine(root, "damaged.mp4"); await File.WriteAllTextAsync(video, "unsupported video fixture"); File.SetLastWriteTime(video, new DateTime(2022, 6, 10));
    var fallback = await MetadataService.ReadAsync(video, default);
    if (fallback.Date.Year != 2022 || fallback.Source != "Modified") throw new Exception("File date fallback failed");
    Console.WriteLine("PASS unreadable video metadata falls back to file modification time");
    var engine = new Sorter.Core.SortEngine(MetadataService.ReadAsync);
    var plan = await engine.PlanAsync(root, Path.Combine(root, "out"), @"yyyy\MM-dd", true, null, null, default);
    if (plan.Count != 2 || plan.Any(x => x.Error != null) || !plan.Any(x => x.Destination.Contains(Path.Combine("2024", "03-19")))) throw new Exception("Metadata integration plan failed");
    Console.WriteLine("PASS real metadata integration into folder plan");
    if (keepFixtures)
    {
        var movie = await folder.CreateFileAsync("sample.mp4", CreationCollisionOption.ReplaceExisting);
        var clip = await Windows.Media.Editing.MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromSeconds(2));
        var composition = new Windows.Media.Editing.MediaComposition(); composition.Clips.Add(clip);
        var result = await composition.RenderToFileAsync(movie, Windows.Media.Editing.MediaTrimmingPreference.Precise, Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.Vga));
        if (result != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new Exception(result.ToString());
        Console.WriteLine("Created real H.264 fixture: " + movie.Path);
    }
}
finally
{
    if (!keepFixtures && Path.GetFileName(root).StartsWith("MediaSorter-metadata-tests-") && Path.GetDirectoryName(root) == Path.TrimEndingDirectorySeparator(Path.GetTempPath())) Directory.Delete(root, true);
}
