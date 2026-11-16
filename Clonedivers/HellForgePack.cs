using System.Security.Cryptography;
using System.Text.Json;

namespace Clonedivers;

/// <summary>A fixed, build-matched Star Wars selection. Never installs into either game's data folder.</summary>
public static class HellForgePack
{
    public const string SupportedBuild = "25480438";
    public const string ReceiptName = "clonedivers-export.json";
    public const string PublishedEmpireDescription = "Published Empire appearance · Full textures · fixed enemy models";
    public static string? SelectionProblem(string mode, string texture, bool droids) =>
        mode == "empire" && (texture != "full" || !droids)
            ? "Your saved appearance options are unavailable in this Empire release. Use the published Empire appearance to continue, or return to Clones to keep using them."
            : null;
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public sealed record Selection(string Mode, string Texture, string Version, string GameBuild, string ManifestSha256,
        Dictionary<string, bool> Options, List<PackFile> Files);
    public sealed record Plan(Selection Selection, string Output, Dictionary<string, string> Local)
    {
        public List<PackFile> Missing => Selection.Files.Where(f => f.Size > 0 && !Local.ContainsKey(f.Sha256)).DistinctBy(f => f.Sha256).ToList();
        public long Bytes => Selection.Files.Sum(f => f.Size);
    }
    public sealed class Receipt
    {
        public int Format { get; set; } = 1;
        public Selection? Selection { get; set; }
        public bool RuntimeTested { get; set; }
    }

    public static string PackId(string mode) => "pack." + GuidFor(mode);
    static string GuidFor(string mode) => mode switch {
        "empire" => "ef3458f5-f409-4f24-8933-05fdc10a0061",
        "commandos" => "b7b312b3-d764-4c7e-a287-3ce7e6b65851",
        "clonedivers" => "e9f12495-c8e7-49f8-89d3-53538a9e9939",
        _ => throw new ArgumentException("Unknown Star Wars mode.")
    };
    public static bool IsOurPack(string id) => new[] { "empire", "commandos", "clonedivers" }.Any(m => id == PackId(m));

    public static Selection Select(Manifest feed, string manifestSha, string mode, string texture, IReadOnlyDictionary<string, bool>? changes = null)
    {
        _ = GuidFor(mode);
        var pack = feed.Pack ?? throw new InvalidDataException("No pack in feed.");
        if (feed.Format != 3 || pack.GameBuild != SupportedBuild || pack.IsBroken || !FileSafety.IsSha256(manifestSha))
            throw new InvalidDataException($"Export requires a working format-3 pack targeting HellForge build {SupportedBuild}.");
        var options = pack.Options.ToDictionary(o => o.Id, o => o.Default, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, on) in changes ?? new Dictionary<string, bool>())
        {
            if (!options.ContainsKey(id)) throw new ArgumentException($"Unknown pack option: {id}");
            options[id] = on;
        }
        options["empire"] = mode == "empire";
        options["commandos"] = mode == "commandos";
        var files = Pack.EffectiveFiles(pack, id => options.GetValueOrDefault(id), texture);
        FileSafety.ValidateTargets(files);
        return new(mode, texture, pack.Version, SupportedBuild, manifestSha, options, files);
    }

    public static Selection PinnedSelection(string mode, string texture, IReadOnlyDictionary<string, bool>? changes = null)
    {
        if (mode == "empire")
        {
            if (SelectionProblem(mode, texture, changes?.GetValueOrDefault("droids", true) ?? true) is { } problem)
                throw new InvalidOperationException(problem);
            changes = new Dictionary<string, bool> { ["droids"] = true, ["covenant"] = false };
        }
        var resource = mode == "empire" ? "Clonedivers.HellForge.Empire.Manifest.json" : "Clonedivers.HellForge.Manifest.json";
        using var stream = typeof(HellForgePack).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("This launcher has no pinned HellForge pack catalog.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var feed = Manifest.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        // Prune Empire's conditional rows before selection so its fixed layers stay at 1000+.
        if (mode == "empire")
        {
            var pack = feed.Pack!;
            bool Enabled(string id) => PackExtras.AlwaysOn(pack, id) || changes!.GetValueOrDefault(id, pack.Options.FirstOrDefault(o => o.Id == id)?.Default ?? false);
            pack.Files = pack.Files.Where(f => PackProfiles.Includes(f, mode, texture) &&
                (f.Option == null || Enabled(f.Option)) && (f.UnlessOption == null || !Enabled(f.UnlessOption))).ToList();
        }
        return Select(feed, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), mode, texture, changes);
    }

    public static async Task<Plan> PlanAsync(Selection selection, string output, IEnumerable<string> sources,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; choose a new folder.");
        RejectInstallationOutput(output);
        // An interrupted export keeps its hash-verified files; completed pack backups keep
        // previous-pack bytes so texture/option toggles can reuse them without re-downloading.
        var exports = new DirectoryInfo(Path.GetDirectoryName(output)!);
        if (string.Equals(exports.Name, "exports", PlatformPaths.Comparison))
        {
            var extra = new List<string>();
            if (exports.Exists && !exports.Attributes.HasFlag(FileAttributes.ReparsePoint))
                extra.AddRange(exports.EnumerateDirectories()
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint) && !d.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName));
            if (exports.Parent is { Exists: true } cache && !cache.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var backups = new DirectoryInfo(Path.Combine(cache.FullName, "backups"));
                if (backups.Exists && !backups.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    extra.AddRange(CompletedBackupPacks(backups));
            }
            if (extra.Count > 0) sources = sources.Concat(extra).ToList();
        }
        var sizes = selection.Files.Where(f => f.Size > 0).Select(f => f.Size).ToHashSet();
        var bySize = new Dictionary<long, List<string>>();
        progress?.Report("Finding reusable pack files…");
        foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var root = Path.GetFullPath(source);
            FileSafety.RejectLink(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            if (Within(output, root)) throw new IOException("Output must be outside every source folder.");
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions {
                RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
            {
                ct.ThrowIfCancellationRequested();
                if (!sizes.Contains(file.Length)) continue;
                if (!bySize.TryGetValue(file.Length, out var list)) bySize[file.Length] = list = [];
                list.Add(file.FullName);
            }
        }
        var local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in selection.Files.Where(f => f.Size > 0).DistinctBy(f => f.Sha256).GroupBy(f => f.Size))
        {
            var hashes = group.Select(f => f.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in bySize.GetValueOrDefault(group.Key) ?? [])
            {
                var hash = await HashAsync(path, ct);
                if (hashes.Contains(hash)) local.TryAdd(hash, path);
                if (hashes.All(local.ContainsKey)) break;
            }
        }
        return new(selection, output, local);
    }

    static IEnumerable<string> CompletedBackupPacks(DirectoryInfo backups)
    {
        var packs = new List<string>();
        foreach (var folder in backups.EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc))
        {
            if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint) || folder.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var outcome = Path.Combine(folder.FullName, "outcome.json");
                FileSafety.RejectLink(outcome);
                if (!File.Exists(outcome) || new FileInfo(outcome).Length > 16384) continue;
                using var record = JsonDocument.Parse(File.ReadAllText(outcome));
                if (record.RootElement.ValueKind != JsonValueKind.Object ||
                    !record.RootElement.TryGetProperty("outcome", out var state) ||
                    state.ValueKind != JsonValueKind.String ||
                    state.GetString() is not ("applied" or "restored")) continue;
                var previous = new DirectoryInfo(Path.Combine(folder.FullName, "previous-pack"));
                if (!previous.Exists || previous.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                packs.Add(previous.FullName);
                if (packs.Count >= 32) break;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
            {
                // Ignore damaged or unreadable backup metadata during reuse discovery.
            }
        }
        return packs;
    }

    internal const long ExportReserve = 64L * 1024 * 1024;
    internal static string? ContainingVolume(string output, IEnumerable<string> volumes, bool windows)
    {
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var separator = windows ? '\\' : '/';
        return volumes.OrderByDescending(v => v.Length).FirstOrDefault(volume => {
            var prefix = volume.TrimEnd('\\', '/');
            return output.Equals(prefix, comparison) || output.StartsWith(prefix + separator, comparison);
        });
    }
    internal static long? AvailableExportBytes(string output)
    {
        try
        {
            var volume = ContainingVolume(Path.GetFullPath(output), DriveInfo.GetDrives().Select(d => d.Name), OperatingSystem.IsWindows());
            return volume == null ? null : new DriveInfo(volume).AvailableFreeSpace;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { return null; } // unavailable capacity is not evidence of a full disk; actual writes remain checked
    }
    public static Task ExportAsync(Plan plan, bool download, IProgress<string>? progress = null, CancellationToken ct = default,
        IProgress<(long Done, long Total)>? bytes = null) =>
        ExportAsync(plan, download, progress, ct, AvailableExportBytes, bytes: bytes);

    internal static async Task ExportAsync(Plan plan, bool download, IProgress<string>? progress, CancellationToken ct, Func<string, long?> availableBytes,
        Func<PackFile, string, CancellationToken, Task>? downloadFile = null, IProgress<(long Done, long Total)>? bytes = null,
        Func<PackFile, string, IProgress<(long Done, long Total)>, CancellationToken, Task>? downloadWithProgress = null)
    {
        ct.ThrowIfCancellationRequested();
        FileSafety.ValidateTargets(plan.Selection.Files);
        if (plan.Missing.Count > 0 && !download) throw new IOException("Missing content. Add source folders or allow verified downloads.");
        if (Directory.Exists(plan.Output) || File.Exists(plan.Output)) throw new IOException("Output already exists; choose a new folder.");
        RejectInstallationOutput(plan.Output);
        var cache = DownloadCache(plan.Output);
        if (Within(cache, Path.GetFullPath(plan.Output))) throw new IOException("Choose an export folder outside the appearance download cache.");
        var needed = checked(plan.Bytes + ExportReserve + plan.Missing.Select(f => f.Size).DefaultIfEmpty().Max()); // export copies plus one cached download
        if (availableBytes(plan.Output) is long free && free < needed)
            throw new IOException($"Not enough space to prepare the pack files: {Pack.FormatBytes(needed)} needed for this copy including working space, {Pack.FormatBytes(Math.Max(0, free))} available. Free space on this drive, then try again.");
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(plan.Output);
        var started = DateTime.UtcNow; string? unverified = null;
        try
        {
            RejectInstallationOutput(cache); FileSafety.RejectLink(cache); Directory.CreateDirectory(cache);
            using var lease = new FileStream(FileSafety.Child(cache, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            PruneDownloadCache(cache, plan.Missing);
            await ExportFilesAsync(plan, cache, progress, ct, target => unverified = target, downloadFile, bytes, downloadWithProgress);
        }
        catch
        {
            // Keep verified files for the next plan to reuse; drop anything
            // unverified, and the folder itself when nothing was verified.
            try
            {
                foreach (var path in new[] { unverified, Path.Combine(plan.Output, ReceiptName), Path.Combine(plan.Output, ReceiptName + ".tmp"), Path.Combine(plan.Output, "manifest.json") })
                    if (path != null) File.Delete(path);
                if (!Directory.EnumerateFileSystemEntries(plan.Output).Any()) Directory.Delete(plan.Output);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
        PruneAbandonedExports(plan.Output, started);
    }
    internal static string DownloadCache(string output)
    {
        var parent = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(output))!);
        // Both frontends choose a new GUID under exports for every attempt.
        return Path.Combine(parent.Name == "exports" ? parent.Parent!.FullName : parent.FullName, "appearance-downloads");
    }
    static string CachedAsset(string cache, PackFile file) => FileSafety.Child(cache, file.Sha256.ToLowerInvariant() + ".partial");
    internal static void PruneDownloadCache(string cache, IEnumerable<PackFile> files)
    {
        var wanted = files.Where(f => f.Size > 0).Select(f => f.Sha256 + ".partial").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(cache, "*.partial"))
        {
            var name = Path.GetFileName(path);
            if (!FileSafety.IsSha256(name[..^8]) || wanted.Contains(name)) continue;
            try { FileSafety.RejectLink(path); File.Delete(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    static async Task ExportFilesAsync(Plan plan, string cache, IProgress<string>? progress, CancellationToken ct, Action<string?> writing,
        Func<PackFile, string, CancellationToken, Task>? downloadFile, IProgress<(long Done, long Total)>? bytes,
        Func<PackFile, string, IProgress<(long Done, long Total)>, CancellationToken, Task>? downloadWithProgress)
    {
        var exported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int done = 0; long completed = 0;
        bytes?.Report((0, plan.Bytes));
        foreach (var file in plan.Selection.Files)
        {
            ct.ThrowIfCancellationRequested();
            var target = FileSafety.Child(plan.Output, file.Name); writing(target); string? cached = null;
            long reported = 0;
            void Report(long value) { reported = Math.Max(reported, Math.Clamp(value, 0, file.Size)); bytes?.Report((completed + reported, plan.Bytes)); }
            if (file.Size == 0) await File.WriteAllBytesAsync(target, [], ct);
            else if (exported.TryGetValue(file.Sha256, out var prior)) await CopyLocalAsync(prior, target, file.Size, ct, Report);
            else if (plan.Local.TryGetValue(file.Sha256, out var source)) await CopyLocalAsync(source, target, file.Size, ct, Report);
            else
            {
                progress?.Report($"Downloading {done + 1}/{plan.Selection.Files.Count}: {Pack.FormatBytes(file.Size)}");
                if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
                    !uri.AbsolutePath.StartsWith("/owendavidgoode/clonedivers/releases/download/", StringComparison.Ordinal))
                    throw new InvalidDataException("Only Clonedivers GitHub release assets may be downloaded.");
                cached = CachedAsset(cache, file);
                FileSafety.RejectLink(cached);
                if (File.Exists(cached) && new FileInfo(cached).Length <= file.Size) Report(new FileInfo(cached).Length);
                if (downloadWithProgress != null) await downloadWithProgress(file, cached, new ByteProgress(p => Report(p.Done)), ct);
                else if (downloadFile == null) await Pack.DownloadAsync(file, cached, new ByteProgress(p => Report(p.Done)), ct);
                else await downloadFile(file, cached, ct);
                if (new FileInfo(cached).Length != file.Size || !string.Equals(await HashAsync(cached, ct), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Download hash mismatch: {file.Name}");
                await CopyLocalAsync(cached, target, file.Size, ct);
            }
            if (new FileInfo(target).Length != file.Size || !string.Equals(await HashAsync(target, ct), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Export hash mismatch: {file.Name}");
            writing(null); exported.TryAdd(file.Sha256, target);
            Report(file.Size); completed += file.Size;
            // The verified export is now the reusable copy. Keep only unfinished downloads.
            if (cached != null) File.Delete(cached);
            if (++done % 100 == 0) progress?.Report($"Verified {Math.Min(done, plan.Selection.Files.Count)}/{plan.Selection.Files.Count} files…");
        }
        var name = plan.Selection.Mode == "empire" ? "EmpireDivers" : "Clonedivers";
        await File.WriteAllTextAsync(Path.Combine(plan.Output, "manifest.json"), JsonSerializer.Serialize(new {
            Version = 1, Guid = GuidFor(plan.Selection.Mode), Name = $"{name} {plan.Selection.Version} ({plan.Selection.Texture})",
            Description = "Star Wars pack for HellForge. Choose difficulties 1–11 in game. Gameplay and multiplayer validation pending.", Options = Array.Empty<object>()
        }, Json), ct);
        var receiptPath = FileSafety.Child(plan.Output, ReceiptName);
        var receiptTemp = receiptPath + ".tmp";
        FileSafety.RejectLink(receiptTemp);
        try
        {
            await using (var receiptFile = new FileStream(receiptTemp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(receiptFile, new Receipt { Selection = plan.Selection }, Json, ct);
                await receiptFile.FlushAsync(ct);
                receiptFile.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(receiptTemp, receiptPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(receiptTemp)) File.Delete(receiptTemp); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    internal sealed class ByteProgress(Action<(long Done, long Total)> report) : IProgress<(long Done, long Total)>
    { public void Report((long Done, long Total) value) => report(value); }
    // After a complete export, earlier interrupted ones (launcher-named, no
    // receipt, untouched since this one began) were reuse sources; drop them.
    internal static void PruneAbandonedExports(string output, DateTime before)
    {
        var exports = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(output))!);
        if (exports.Name != "exports" || !exports.Exists || exports.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
        foreach (var folder in exports.EnumerateDirectories())
            try
            {
                if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint) || folder.LastWriteTimeUtc >= before || File.Exists(Path.Combine(folder.FullName, ReceiptName)) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(folder.Name, "^[a-z]{1,16}-[0-9a-f]{32}$")) continue;
                folder.Delete(true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal static Selection? ReadStorageReceipt(string folder)
    {
        var path = Path.Combine(folder, ReceiptName);
        for (var parent = new DirectoryInfo(folder); parent != null; parent = parent.Parent) FileSafety.RejectLink(parent.FullName);
        FileSafety.RejectLink(path);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Oversized export receipt.");
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path), Json);
        if (receipt?.Format != 1 || receipt.Selection == null || receipt.Selection.Options == null ||
            receipt.Selection.Version == null || receipt.Selection.Texture == null ||
            receipt.Selection.GameBuild == null || receipt.Selection.ManifestSha256 == null)
            throw new InvalidDataException("Invalid export receipt.");
        _ = GuidFor(receipt.Selection.Mode); FileSafety.ValidateTargets(receipt.Selection.Files);
        return receipt.Selection;
    }
    internal static string SelectionIdentity(Selection s) => JsonSerializer.Serialize(new {
        s.Mode, s.Texture, s.Version, s.GameBuild, s.ManifestSha256,
        Options = s.Options.OrderBy(p => p.Key, StringComparer.Ordinal),
        Files = s.Files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => new { f.Name, f.Size, f.Sha256 })
    });
    internal static List<string> SupersededExports(string exports, HashSet<string> selections)
    {
        FileSafety.RejectLink(exports);
        if (!Directory.Exists(exports)) return [];
        var receipts = new List<(DirectoryInfo Folder, Selection Selection)>();
        foreach (var folder in new DirectoryInfo(exports).EnumerateDirectories())
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(folder.Name, "^[a-z]{1,16}-[0-9a-f]{32}$")) continue;
            try
            {
                if (ReadStorageReceipt(folder.FullName) is { } selection) receipts.Add((folder, selection));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException or NullReferenceException or NotSupportedException)
            {
                // Damaged or unreadable export receipts stay kept as non-candidates without aborting cleanup.
            }
        }
        return receipts.GroupBy(r => r.Selection.Mode).SelectMany(group => group.OrderByDescending(r => r.Folder.LastWriteTimeUtc)
            .ThenBy(r => r.Folder.Name, StringComparer.Ordinal).Skip(1))
            .Where(r => !selections.Contains(SelectionIdentity(r.Selection))).Select(r => r.Folder.FullName).ToList();
    }
    internal static void DeleteStorageFolder(string cache, string folder)
    {
        cache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cache));
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (folder.Equals(cache, StringComparison.OrdinalIgnoreCase) || !Within(folder, cache)) throw new IOException("Storage cleanup escaped its cache.");
        for (var parent = new DirectoryInfo(folder); parent != null; parent = parent.Parent) FileSafety.RejectLink(parent.FullName);
        if (!Directory.Exists(folder)) return;
        RejectLinksInTree(folder);

        var active = folder;
        var name = Path.GetFileName(folder);
        if (!name.StartsWith("deleting-", StringComparison.Ordinal))
        {
            var parent = Path.GetDirectoryName(folder)!;
            var renamed = Path.Combine(parent, "deleting-" + name);
            if (Directory.Exists(renamed) || File.Exists(renamed))
                renamed = Path.Combine(parent, "deleting-" + Guid.NewGuid().ToString("N") + "-" + name);
            FileSafety.RejectLink(renamed);
            try
            {
                Directory.Move(folder, renamed);
                active = renamed;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // If an open handle blocks renaming now, delete contents before markers so cleanup remains retryable.
            }
        }

        var (directories, files) = CollectStorageTree(active);
        Exception? failure = null;
        // Delete previous-pack and other content before marker files so an interrupted or locked
        // delete keeps its identifying marker (or deleting-* name) for subsequent retry.
        foreach (var file in files.Where(f => !IsStorageMarker(active, f))
                     .OrderBy(f => string.Equals(Path.GetDirectoryName(f), active, PlatformPaths.Comparison) ? 1 : 0))
        {
            try { File.Delete(file); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure ??= error; }
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();

        for (int i = directories.Count - 1; i >= 1; i--)
            Directory.Delete(directories[i], false);

        foreach (var file in files.Where(f => IsStorageMarker(active, f)))
            File.Delete(file);

        Directory.Delete(active, false);
    }

    static void RejectLinksInTree(string folder)
    {
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.TryPop(out var directory))
        {
            FileSafety.RejectLink(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileSafety.RejectLink(path);
                if (Directory.Exists(path)) pending.Push(path);
            }
        }
    }

    static (List<string> Directories, List<string> Files) CollectStorageTree(string folder)
    {
        var directories = new List<string>();
        var files = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(folder);
        while (queue.TryDequeue(out var directory))
        {
            FileSafety.RejectLink(directory);
            directories.Add(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileSafety.RejectLink(path);
                if (Directory.Exists(path)) queue.Enqueue(path);
                else files.Add(path);
            }
        }
        return (directories, files);
    }

    static bool IsStorageMarker(string root, string file) =>
        string.Equals(Path.GetDirectoryName(file), root, PlatformPaths.Comparison) &&
        (Path.GetFileName(file).Equals("outcome.json", StringComparison.OrdinalIgnoreCase) ||
         Path.GetFileName(file).Equals(ReceiptName, StringComparison.OrdinalIgnoreCase));

    internal static async Task CopyLocalAsync(string source, string target, long expected, CancellationToken ct, Action<long>? bytes = null)
    {
        FileSafety.RejectLink(source);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != expected) throw new InvalidDataException("Reusable pack content changed size; prepare again.");
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await CopyExactAsync(input, output, expected, ct, bytes);
    }
    internal static async Task CopyExactAsync(Stream input, Stream output, long expected, CancellationToken ct, Action<long>? bytes = null)
    {
        var buffer = new byte[128 * 1024]; long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (count > expected - total) throw new InvalidDataException("Pack content exceeds catalog size.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
            total += count;
            bytes?.Invoke(total);
        }
        if (total != expected) throw new InvalidDataException("Pack content is shorter than its catalog size.");
    }

    public static async Task VerifyAsync(string folder, Selection expected, CancellationToken ct = default)
    {
        await VerifyMetadataAsync(folder, expected, ct);
        await VerifyContentAsync(folder, expected, ct);
    }
    // Fast readiness only. Every launch still runs full content verification.
    public static async Task VerifyMetadataAsync(string folder, Selection expected, CancellationToken ct = default)
    {
        FileSafety.RejectLink(folder);
        var receiptPath = FileSafety.Child(folder, ReceiptName);
        if (!File.Exists(receiptPath) || new FileInfo(receiptPath).Length > 4 * 1024 * 1024) throw new InvalidDataException("Pack receipt missing or oversized; prepare the pack again.");
        var receipt = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receiptPath, ct), Json);
        if (receipt?.Format != 1 || receipt.Selection is not { } actual || actual.GameBuild != expected.GameBuild ||
            actual.ManifestSha256 != expected.ManifestSha256 || actual.Mode != expected.Mode || actual.Texture != expected.Texture ||
            actual.Version != expected.Version || actual.Files == null || !SameFiles(actual.Files, expected.Files) || actual.Options == null ||
            actual.Options.Count != expected.Options.Count || expected.Options.Any(pair => !actual.Options.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidDataException("This pack does not match the selected mode/options; prepare it again.");
    }
    static async Task VerifyContentAsync(string folder, Selection expected, CancellationToken ct)
    {
        // Compare to the trusted catalog, not only the mutable receipt.
        foreach (var file in expected.Files)
        {
            var path = FileSafety.Child(folder, file.Name);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size || !string.Equals(await HashAsync(path, ct), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Pack file changed: {file.Name}. Prepare the pack again.");
        }
        var present = Directory.EnumerateFiles(folder).Select(p => Path.GetFileName(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowed = expected.Files.Select(f => f.Name).Append("manifest.json").Append(ReceiptName);
        if (!present.SetEquals(allowed)) throw new InvalidDataException("The pack contains unexpected files; prepare it again.");
        var manifestPath = FileSafety.Child(folder, "manifest.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        if (manifest.RootElement.GetProperty("Guid").GetString() != GuidFor(expected.Mode) ||
            manifest.RootElement.GetProperty("Options").GetArrayLength() != 0 || manifest.RootElement.TryGetProperty("Include", out _))
            throw new InvalidDataException("The cosmetic pack manifest changed; prepare it again.");
        if (Directory.EnumerateDirectories(folder).Any()) throw new InvalidDataException("Unexpected folders in the Star Wars pack.");
    }

    static bool SameFiles(List<PackFile> a, List<PackFile> b) => a.Count == b.Count &&
        a.OrderBy(f => f.Name, StringComparer.Ordinal).Zip(b.OrderBy(f => f.Name, StringComparer.Ordinal)).All(p =>
            p.First.Name == p.Second.Name && p.First.Size == p.Second.Size && p.First.Sha256 == p.Second.Sha256);

    static void RejectInstallationOutput(string output)
    {
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(output)!); parent != null; parent = parent.Parent)
        {
            FileSafety.RejectLink(parent.FullName);
            if (parent.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(parent.FullName, "hellforge-install.json")))
                throw new IOException("Export outside Steam and HellForge installations, then import through HellForge.");
        }
    }
    static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static async Task<string> HashAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }
}
