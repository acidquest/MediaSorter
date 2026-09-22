using Sorter.Core;

var root = Path.Combine(Path.GetTempPath(), "MediaSorter-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
void Check(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine("PASS " + description); passed++; }
void Reject(Action action, string description) { try { action(); } catch (ArgumentException) { Check(true, description); return; } throw new Exception(description); }
try
{
    var date = new DateTime(2026, 4, 28);
    Check(FolderPattern.Format(@"yyyy\mm-dd\", date) == Path.Combine("2026", "04-28"), "lowercase month alias and trailing separator");
    Check(FolderPattern.Format(@"yyyy\dd.MM.yy", date) == Path.Combine("2026", "28.04.26"), "date tokens");
    Check(FolderPattern.Format(@"'My yyyy photos'\yy-MM-dd", date) == Path.Combine("My yyyy photos", "26-04-28"), "quoted literals");
    Reject(() => FolderPattern.Format(@"..\yyyy", date), "reject traversal");
    Reject(() => FolderPattern.Format(@"C:\yyyy", date), "reject absolute path");
    Reject(() => FolderPattern.Format("yyyy.", date), "reject trailing dot");
    Reject(() => FolderPattern.Format("CON", date), "reject device name");
    Reject(() => FolderPattern.Format("'unclosed", date), "reject unclosed quote");
    var source = Path.Combine(root, "source"); var output = Path.Combine(source, "out");
    Directory.CreateDirectory(source); Directory.CreateDirectory(output); Directory.CreateDirectory(Path.Combine(source, "nested"));
    await File.WriteAllTextAsync(Path.Combine(source, "a.jpg"), "one");
    await File.WriteAllTextAsync(Path.Combine(source, "nested", "a.jpg"), "two");
    await File.WriteAllTextAsync(Path.Combine(source, "clip.mp4"), "video");
    await File.WriteAllTextAsync(Path.Combine(source, "notes.txt"), "not media");
    await File.WriteAllTextAsync(Path.Combine(output, "ignored.jpg"), "existing output");
    var engine = new SortEngine((_, _) => Task.FromResult(new MediaDate(date, "test", new Dictionary<string, string>())));
    var plan = await engine.PlanAsync(source, output, @"yyyy\MM-dd", true, null, null, default);
    Check(plan.Count == 3, "recursive scan excludes output and non-media");
    Check(plan.Select(x => x.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "duplicate destinations reserved");
    Check(plan.Any(x => x.Destination.EndsWith("a (2).jpg")), "duplicate suffix preserves extension");
    var shallow = await engine.PlanAsync(source, output, "yyyy", false, MediaKind.Photo, null, default);
    Check(shallow.Count == 1, "photo filter and first-level scan");
    var rejected = false;
    try { await engine.PlanAsync(source, source, "yyyy", true, null, null, default); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "reject same source/output");
    var item = plan.First(x => x.Source.EndsWith("clip.mp4"));
    await File.AppendAllTextAsync(item.Source, "changed");
    try { await SortEngine.MoveVerifiedAsync(item, default); } catch (IOException) { }
    Check(File.Exists(item.Source) && !File.Exists(item.Destination), "changed source preserved");
    plan = await engine.PlanAsync(source, output, @"yyyy\MM-dd", true, null, null, default);
    var collision = plan[0]; Directory.CreateDirectory(Path.GetDirectoryName(collision.Destination)!); await File.WriteAllTextAsync(collision.Destination, "do not overwrite");
    try { await SortEngine.MoveVerifiedAsync(collision, default); } catch (IOException) { }
    Check(File.Exists(collision.Source) && await File.ReadAllTextAsync(collision.Destination) == "do not overwrite", "late destination collision never overwrites");
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    var cancelled = await SortEngine.ExecuteAsync(plan, Path.Combine(root, "cancelled.jsonl"), null, cancel.Token);
    Check(cancelled.Count == 0 && plan.All(x => File.Exists(x.Source)), "cancel before execution preserves files");
    plan = await engine.PlanAsync(source, output, @"yyyy\MM-dd", true, null, null, default);
    var results = await SortEngine.ExecuteAsync(plan, Path.Combine(root, "journal.jsonl"), null, default);
    Check(results.All(x => x.Success) && results.Count == 3, "execute all planned moves");
    Check(plan.All(x => !File.Exists(x.Source) && File.Exists(x.Destination)), "successful move removes only source");
    Check(File.ReadAllLines(Path.Combine(root, "journal.jsonl")).Length == 6, "journal records pending and completed moves");
    Check(File.Exists(Path.Combine(source, "notes.txt")) && File.Exists(Path.Combine(output, "ignored.jpg")), "unrelated files preserved");
    var copySource = Path.Combine(root, "copy-source.jpg");
    var copyTarget = Path.Combine(root, "copy-target.jpg");
    var bytes = new byte[1024 * 1024]; Random.Shared.NextBytes(bytes); await File.WriteAllBytesAsync(copySource, bytes);
    var copyInfo = new FileInfo(copySource);
    var copyItem = new PlanItem(copySource, copyTarget, copyInfo.Length, copyInfo.LastWriteTimeUtc, date, "test");
    await SortEngine.MoveVerifiedAsync(copyItem, default, forceVerifiedCopy: true);
    Check(!File.Exists(copySource) && File.ReadAllBytes(copyTarget).SequenceEqual(bytes), "verified-copy branch preserves bytes and deletes source only after commit");
    Check(File.GetLastWriteTimeUtc(copyTarget) == copyItem.ModifiedUtc, "verified-copy preserves modification date");
    Check(!Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any(), "verified-copy leaves no temporary files");
    await File.WriteAllBytesAsync(copySource, bytes); copyInfo.Refresh();
    copyItem = copyItem with { Length = copyInfo.Length, ModifiedUtc = copyInfo.LastWriteTimeUtc };
    try { await SortEngine.MoveVerifiedAsync(copyItem, default, forceVerifiedCopy: true); } catch (IOException) { }
    Check(File.Exists(copySource) && File.ReadAllBytes(copyTarget).SequenceEqual(bytes), "verified-copy collision retains original and destination");
    var transferFolder = Path.Combine(root, "transfer"); Directory.CreateDirectory(transferFolder);
    var snapshot = FileTransferPlan.Snapshot(copySource);
    Check(FileTransferPlan.Create(new[] { snapshot }, root, default).Count == 0, "self-paste does not rename files");
    var transfer = FileTransferPlan.Create(new[] { snapshot, snapshot }, transferFolder, default);
    Check(transfer.Count == 1, "manual transfer deduplicates selected sources");
    await File.WriteAllTextAsync(transfer[0].Destination, "existing");
    transfer = FileTransferPlan.Create(new[] { snapshot }, transferFolder, default);
    Check(transfer[0].Destination.EndsWith("copy-source (2).jpg"), "manual transfer reserves non-overwriting destination");
    var transferred = await SortEngine.ExecuteAsync(transfer, Path.Combine(root, "transfer.jsonl"), null, default);
    Check(transferred.Single().Success && !File.Exists(copySource) && File.ReadAllBytes(transfer[0].Destination).SequenceEqual(bytes), "manual transfer moves original with matching contents");
    Check(File.ReadAllText(Path.Combine(transferFolder, "copy-source.jpg")) == "existing", "manual transfer preserves existing destination");
    var stale = FileTransferPlan.Snapshot(transfer[0].Destination);
    await File.AppendAllTextAsync(stale.Source, "changed after cut");
    var stalePlan = FileTransferPlan.Create(new[] { stale }, Path.Combine(root, "stale"), default);
    var staleResults = await SortEngine.ExecuteAsync(stalePlan, Path.Combine(root, "stale.jsonl"), null, default);
    Check(!staleResults.Single().Success && File.Exists(stale.Source) && !File.Exists(stalePlan[0].Destination), "changes after cut preserve original");
    Console.WriteLine($"All {passed} checks passed.");
}
finally
{
    if (Path.GetFileName(root).StartsWith("MediaSorter-tests-") && Path.GetDirectoryName(root) == Path.TrimEndingDirectorySeparator(Path.GetTempPath())) Directory.Delete(root, true);
}
