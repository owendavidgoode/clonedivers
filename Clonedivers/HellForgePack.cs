using System.Security.Cryptography;
using System.Text.Json;

namespace Clonedivers;

/// <summary>A fixed, build-matched Star Wars selection. Never installs into either game's data folder.</summary>
public static class HellForgePack
{
    public const string SupportedBuild = "25480438";
    public const string ReceiptName = "clonedivers-export.json";
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

    /// <summary>
    /// True for any official or stray/demo/legacy Clonedivers or EmpireDivers Star Wars overhaul pack
    /// (such as local test/demo exports like pack.ef3458f5-f409-4f24-8933-05fdc10a2101) so mode switches
    /// disable competing overhaul packs and launch guards reject overlapping Star Wars overhauls.
    /// Note: reserves HellForge pack.* display-name prefixes "Clonedivers*" and "EmpireDivers*" for full
    /// Star Wars visual overhaul packs (sub-addons or gameplay mods should use distinct names or non-pack IDs).
    /// </summary>
    public static bool IsStarWarsOverhaulMod(HellForgeMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (IsOurPack(mod.Id)) return true;
        if (!mod.Id.StartsWith("pack.", StringComparison.OrdinalIgnoreCase)) return false;
        if (mod.Id.StartsWith("pack.ef3458f5-f409-4f24-8933-", StringComparison.OrdinalIgnoreCase) ||
            mod.Id.StartsWith("pack.b7b312b3-d764-4c7e-a287-", StringComparison.OrdinalIgnoreCase) ||
            mod.Id.StartsWith("pack.e9f12495-c8e7-49f8-89d3-", StringComparison.OrdinalIgnoreCase))
            return true;
        var trimmed = mod.Name?.TrimStart() ?? "";
        return trimmed.StartsWith("Clonedivers", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("EmpireDivers", StringComparison.OrdinalIgnoreCase);
    }

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
        if (options.GetValueOrDefault("mixed-droids") || options.GetValueOrDefault("mixed_droids"))
            options["droids"] = true;
        if (options.GetValueOrDefault("mixed-covenant") || options.GetValueOrDefault("mixed_covenant"))
            options["covenant"] = true;
        var files = Pack.EffectiveFiles(pack, id => options.GetValueOrDefault(id), texture, explicitMode: mode);
        FileSafety.ValidateTargets(files);
        return new(mode, texture, pack.Version, SupportedBuild, manifestSha, options, files);
    }

    sealed record CachedPinnedManifest(Manifest Feed, string Sha256, HashSet<string> SupportedProfiles);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedPinnedManifest> PinnedManifestCache = new(StringComparer.Ordinal);

    static CachedPinnedManifest LoadCachedPinnedManifest(string resourceName) =>
        PinnedManifestCache.GetOrAdd(resourceName, static name =>
        {
            using var stream = typeof(HellForgePack).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidDataException("This launcher has no pinned HellForge pack catalog.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var feed = Manifest.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            var profiles = feed.Pack != null
                ? PackProfiles.Supported(feed.Pack).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return new CachedPinnedManifest(feed, sha, profiles);
        });

    public static Selection PinnedSelection(string mode, string texture, IReadOnlyDictionary<string, bool>? changes = null)
    {
        var resource = mode == "empire" ? "Clonedivers.HellForge.Empire.Manifest.json" : "Clonedivers.HellForge.Manifest.json";
        var primary = LoadCachedPinnedManifest(resource);
        CachedPinnedManifest? fallback = null;
        if (mode == "empire" && texture is "lighter" or "reduced1024" or "reduced512" or "potato256")
        {
            var alt = texture is "reduced512" or "potato256" && typeof(HellForgePack).Assembly.GetManifestResourceInfo("Clonedivers.HellForge.Empire.Potato.Manifest.json") != null
                ? "Clonedivers.HellForge.Empire.Potato.Manifest.json"
                : typeof(HellForgePack).Assembly.GetManifestResourceInfo("Clonedivers.HellForge.Empire.Lighter.Manifest.json") != null
                    ? "Clonedivers.HellForge.Empire.Lighter.Manifest.json" : null;
            if (alt != null) fallback = LoadCachedPinnedManifest(alt);
        }
        return ResolveSelectionFromFeeds(primary.Feed, primary.Sha256, fallback?.Feed, fallback?.Sha256, mode, texture, changes);
    }

    public static Selection ResolveSelection(byte[] bytes, byte[]? lighterFallbackBytes, string mode, string texture, IReadOnlyDictionary<string, bool>? changes = null)
    {
        var primaryFeed = Manifest.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        var primarySha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Manifest? fallbackFeed = null;
        string? fallbackSha = null;
        if (lighterFallbackBytes != null)
        {
            fallbackFeed = Manifest.Parse(System.Text.Encoding.UTF8.GetString(lighterFallbackBytes));
            fallbackSha = Convert.ToHexString(SHA256.HashData(lighterFallbackBytes)).ToLowerInvariant();
        }
        return ResolveSelectionFromFeeds(primaryFeed, primarySha, fallbackFeed, fallbackSha, mode, texture, changes);
    }

    static Selection ResolveSelectionFromFeeds(Manifest primaryFeed, string primarySha, Manifest? fallbackFeed, string? fallbackSha,
        string mode, string texture, IReadOnlyDictionary<string, bool>? changes)
    {
        var requested = changes != null ? new Dictionary<string, bool>(changes, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (mode == "empire")
        {
            var empireChanges = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["droids"] = requested.GetValueOrDefault("droids", true),
                ["covenant"] = requested.GetValueOrDefault("covenant", false)
            };
            foreach (var key in new[] { "mixed-droids", "mixed_droids", "mixed-covenant", "mixed_covenant", "deathtrooper" })
                if (requested.TryGetValue(key, out var value)) empireChanges[key] = value;
            requested = empireChanges;
        }
        else
        {
            requested.Remove("deathtrooper");
        }
        var feed = primaryFeed;
        var activeSha = primarySha;
        static bool HasProfile(PackManifest candidate, string id) =>
            PackProfiles.Supported(candidate).Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (mode == "empire" && texture is "lighter" or "reduced1024" or "reduced512" or "potato256" && fallbackFeed != null && fallbackSha != null &&
            !HasProfile(feed.Pack!, texture) &&
            !(texture == "reduced1024" && HasProfile(feed.Pack!, "lighter")) &&
            !(texture == "potato256" && HasProfile(feed.Pack!, "reduced512")))
        {
            feed = fallbackFeed;
            activeSha = fallbackSha;
        }
        var pack = feed.Pack!;
        var resolvedTexture = texture;
        if (!HasProfile(pack, resolvedTexture))
        {
            if (string.Equals(resolvedTexture, "potato256", StringComparison.OrdinalIgnoreCase))
                resolvedTexture = HasProfile(pack, "reduced512") ? "reduced512" : HasProfile(pack, "lighter") ? "lighter" : HasProfile(pack, "full") ? "full" : resolvedTexture;
            else if (string.Equals(resolvedTexture, "reduced512", StringComparison.OrdinalIgnoreCase))
                resolvedTexture = HasProfile(pack, "lighter") ? "lighter" : HasProfile(pack, "full") ? "full" : resolvedTexture;
            else if (string.Equals(resolvedTexture, "reduced1024", StringComparison.OrdinalIgnoreCase))
                resolvedTexture = HasProfile(pack, "lighter") ? "lighter" : HasProfile(pack, "full") ? "full" : resolvedTexture;
        }
        var optionIds = pack.Options.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var futureKey in new[] { "mixed-droids", "mixed_droids" })
        {
            if (requested.TryGetValue(futureKey, out var mixedOn))
            {
                if (mixedOn) requested["droids"] = true;
                if (!optionIds.Contains(futureKey)) requested.Remove(futureKey);
            }
        }
        foreach (var futureCovKey in new[] { "mixed-covenant", "mixed_covenant" })
        {
            if (requested.TryGetValue(futureCovKey, out var mixedCovOn))
            {
                if (mixedCovOn) requested["covenant"] = true;
                if (!optionIds.Contains(futureCovKey)) requested.Remove(futureCovKey);
            }
        }
        if (requested.ContainsKey("deathtrooper") && (mode != "empire" || !optionIds.Contains("deathtrooper")))
        {
            requested.Remove("deathtrooper");
        }
        if (mode == "empire" && !pack.Files.Any(f => PackProfiles.Includes(f, "empire", resolvedTexture) &&
            (string.Equals(f.Option, "covenant", StringComparison.OrdinalIgnoreCase) || string.Equals(f.UnlessOption, "covenant", StringComparison.OrdinalIgnoreCase))))
        {
            requested["covenant"] = false;
        }
        // Prune Empire's conditional rows on a non-mutating PackManifest copy so cached manifests stay 100% immutable!
        if (mode == "empire")
        {
            bool RawEnabled(string id) => PackExtras.AlwaysOn(pack, id) || requested.GetValueOrDefault(id, pack.Options.FirstOrDefault(o => o.Id == id)?.Default ?? false);
            bool Enabled(string id) => RawEnabled(id)
                || (string.Equals(id, "droids", StringComparison.OrdinalIgnoreCase) && (RawEnabled("mixed-droids") || RawEnabled("mixed_droids")))
                || (string.Equals(id, "covenant", StringComparison.OrdinalIgnoreCase) && (RawEnabled("mixed-covenant") || RawEnabled("mixed_covenant")));
            var prunedFiles = pack.Files.Where(f => PackProfiles.Includes(f, mode, resolvedTexture) &&
                (f.Option == null || Enabled(f.Option)) && (f.UnlessOption == null || !Enabled(f.UnlessOption))).ToList();
            pack = new PackManifest
            {
                Version = pack.Version,
                GameBuild = pack.GameBuild,
                GameDepots = pack.GameDepots,
                Status = pack.Status,
                StatusNotes = pack.StatusNotes,
                Options = pack.Options,
                TextureProfiles = pack.TextureProfiles,
                Files = prunedFiles
            };
            feed = new Manifest { Format = feed.Format, App = feed.App, Pack = pack };
        }
        return Select(feed, activeSha, mode, resolvedTexture, requested);
    }

    public static async Task<Plan> PlanAsync(Selection selection, string output, IEnumerable<string> sources,
        IProgress<string>? progress = null, CancellationToken ct = default, IProgress<OperationProgress>? richProgress = null)
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
        richProgress?.Report(new("Scanning local cache", "Checking existing folders for reusable files…", IsIndeterminate: true, CanCancel: true));
        int scannedFiles = 0;
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
                scannedFiles++;
                if (richProgress != null && scannedFiles % 64 == 0)
                    richProgress.Report(new("Scanning local cache", $"Checked {scannedFiles:N0} local files…", IsIndeterminate: true, CanCancel: true));
                if (!sizes.Contains(file.Length)) continue;
                if (!bySize.TryGetValue(file.Length, out var list)) bySize[file.Length] = list = [];
                list.Add(file.FullName);
            }
        }
        var local = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var groups = selection.Files.Where(f => f.Size > 0).DistinctBy(f => f.Sha256).GroupBy(f => f.Size).ToList();
        var totalCheckFiles = groups.Sum(g => g.Count());
        long totalCheckBytes = groups.Sum(g => g.Key * g.Count());
        int checkedFiles = 0;
        long checkedBytes = 0;
        var speed = new SpeedTracker();
        var progressGate = new object();
        string? hashCacheFile = string.Equals(exports.Name, "exports", PlatformPaths.Comparison) && exports.Parent != null
            ? Path.Combine(exports.Parent.FullName, PackArtifactEngine.HashCacheFileName)
            : null;
        var hashCache = PackArtifactEngine.LoadHashCache(hashCacheFile);

        await Parallel.ForEachAsync(
            groups,
            new ParallelOptions { MaxDegreeOfParallelism = PackArtifactEngine.DefaultParallelism, CancellationToken = ct },
            async (group, token) =>
            {
                var hashes = group.Select(f => f.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var path in bySize.GetValueOrDefault(group.Key) ?? [])
                {
                    var hash = await PackArtifactEngine.HashWithMetadataCacheAsync(path, hashCache, token);
                    if (hashes.Contains(hash) && local.TryAdd(hash, path))
                    {
                        var nowFiles = Interlocked.Increment(ref checkedFiles);
                        var nowBytes = Interlocked.Add(ref checkedBytes, group.Key);
                        if (richProgress != null)
                        {
                            lock (progressGate)
                            {
                                var (rate, eta) = speed.Update(nowBytes, totalCheckBytes);
                                richProgress.Report(new("Verifying cached files", $"Reusing {local.Count} local files", nowFiles, totalCheckFiles, nowBytes, totalCheckBytes, rate, eta, CanCancel: true));
                            }
                        }
                    }
                    if (hashes.All(local.ContainsKey)) break;
                }
                // Account for uncached files in this size group so the bar advances smoothly to 100%.
                var missed = hashes.Count(h => !local.ContainsKey(h));
                if (missed > 0)
                {
                    var nowFiles = Interlocked.Add(ref checkedFiles, missed);
                    var nowBytes = Interlocked.Add(ref checkedBytes, group.Key * missed);
                    if (richProgress != null)
                    {
                        lock (progressGate)
                        {
                            var (rate, eta) = speed.Update(nowBytes, totalCheckBytes);
                            richProgress.Report(new("Verifying cached files", $"Checked {nowFiles}/{totalCheckFiles} files ({local.Count} cached)", nowFiles, totalCheckFiles, nowBytes, totalCheckBytes, rate, eta, CanCancel: true));
                        }
                    }
                }
            });

        PackArtifactEngine.SaveHashCache(hashCacheFile, hashCache);
        return new(selection, output, new Dictionary<string, string>(local, StringComparer.OrdinalIgnoreCase));
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
    internal static string VolumeKey(string output)
    {
        try
        {
            var full = Path.GetFullPath(output);
            var volume = ContainingVolume(full, DriveInfo.GetDrives().Select(d => d.Name), OperatingSystem.IsWindows());
            if (volume != null)
                return OperatingSystem.IsWindows() ? volume.ToUpperInvariant() : volume;
            var root = Path.GetPathRoot(full) ?? full;
            return OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return output;
        }
    }

    /// <summary>
    /// Reclaims a freshly created export folder if the subsequent <see cref="HellForgeClient.InstallPackAsync"/> step fails
    /// due to low disk space, preserving any newly downloaded network files inside <c>appearance-downloads</c> so zero network
    /// bytes are lost while reclaiming all ~12 GiB of duplicated local copies immediately.
    /// </summary>
    internal static void ReclaimFailedInstallExport(Plan plan, Exception error)
    {
        if (plan == null || error is OperationCanceledException) return;
        if (error is not IOException io || !io.Message.Contains("Not enough", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Output));
            if (!Directory.Exists(full)) return;
            var parent = Path.GetDirectoryName(full);
            if (parent == null || !string.Equals(Path.GetFileName(parent), "exports", StringComparison.OrdinalIgnoreCase)) return;
            FileSafety.RejectLink(parent);
            FileSafety.RejectLink(full);

            if (plan.Missing.Count > 0)
            {
                var cache = DownloadCache(full);
                FileSafety.RejectLink(cache);
                Directory.CreateDirectory(cache);
                foreach (var dl in plan.Missing.Where(f => f.Size > 0))
                {
                    var exported = FileSafety.Child(full, dl.Name);
                    var cached = CachedAsset(cache, dl);
                    if (File.Exists(exported) && !File.Exists(cached))
                    {
                        try { FileSafety.RejectLink(exported); File.Move(exported, cached, overwrite: false); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                }
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories))
                FileSafety.RejectLink(entry);
            Directory.Delete(full, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
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
        IProgress<(long Done, long Total)>? bytes = null, IProgress<OperationProgress>? richProgress = null) =>
        ExportAsync(plan, download, progress, ct, AvailableExportBytes, bytes: bytes, richProgress: richProgress);

    internal static async Task ExportAsync(Plan plan, bool download, IProgress<string>? progress, CancellationToken ct, Func<string, long?> availableBytes,
        Func<PackFile, string, CancellationToken, Task>? downloadFile = null, IProgress<(long Done, long Total)>? bytes = null,
        Func<PackFile, string, IProgress<(long Done, long Total)>, CancellationToken, Task>? downloadWithProgress = null,
        IProgress<OperationProgress>? richProgress = null)
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
            await ExportFilesAsync(plan, download, cache, progress, ct, target => unverified = target, availableBytes, downloadFile, bytes, downloadWithProgress, richProgress);
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
    static async Task ExportFilesAsync(Plan plan, bool download, string cache, IProgress<string>? progress, CancellationToken ct, Action<string?> writing,
        Func<string, long?> availableBytes, Func<PackFile, string, CancellationToken, Task>? downloadFile, IProgress<(long Done, long Total)>? bytes,
        Func<PackFile, string, IProgress<(long Done, long Total)>, CancellationToken, Task>? downloadWithProgress,
        IProgress<OperationProgress>? richProgress = null)
    {
        var exported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int done = 0; long completed = 0;
        int totalFiles = plan.Selection.Files.Count;
        var speed = new SpeedTracker();
        bytes?.Report((0, plan.Bytes));
        richProgress?.Report(new(plan.Missing.Count > 0 ? "Downloading Star Wars pack" : "Copying cached pack files",
            null, 0, totalFiles, 0, plan.Bytes, CanCancel: true));
        foreach (var file in plan.Selection.Files)
        {
            ct.ThrowIfCancellationRequested();
            var target = FileSafety.Child(plan.Output, file.Name); writing(target); string? cached = null;
            long reported = 0;
            bool hasLocal = plan.Local.TryGetValue(file.Sha256, out var source) && File.Exists(source);
            bool isDownload = file.Size > 0 && !exported.ContainsKey(file.Sha256) && !hasLocal;
            string stage = isDownload ? "Downloading Star Wars pack" : "Preparing cached pack files";
            string subStep = isDownload ? $"Downloading {file.Name}" : $"Copying {file.Name}";
            void Report(long value)
            {
                reported = Math.Max(reported, Math.Clamp(value, 0, file.Size));
                long totalDone = completed + reported;
                bytes?.Report((totalDone, plan.Bytes));
                if (richProgress != null)
                {
                    var (rate, eta) = speed.Update(totalDone, plan.Bytes);
                    richProgress.Report(new(stage, subStep, done + 1, totalFiles, totalDone, plan.Bytes, rate, eta, CanCancel: true));
                }
            }
            string actualSha;
            if (file.Size == 0)
            {
                await File.WriteAllBytesAsync(target, [], ct);
                actualSha = PackFile.EmptySha;
            }
            else if (exported.TryGetValue(file.Sha256, out var prior))
            {
                actualSha = await PackArtifactEngine.CopyAndHashAsync(prior, target, file.Size, ct, Report);
            }
            else if (hasLocal && source != null)
            {
                actualSha = await PackArtifactEngine.CopyAndHashAsync(source, target, file.Size, ct, Report);
            }
            else
            {
                if (!download)
                    throw new IOException($"Cached Star Wars pack source is missing: {file.Name}. Add source folders or allow verified downloads.");
                long remainingExport = Math.Max(0L, plan.Bytes - completed);
                long fallbackNeeded = checked(remainingExport + ExportReserve + file.Size);
                if (availableBytes(plan.Output) is long fallbackFree && fallbackFree < fallbackNeeded)
                    throw new IOException($"Not enough space to download missing pack file {file.Name}: {Pack.FormatBytes(fallbackNeeded)} needed, {Pack.FormatBytes(Math.Max(0L, fallbackFree))} available.");
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
                if (new FileInfo(cached).Length != file.Size)
                    throw new InvalidDataException($"Download hash mismatch: {file.Name}");
                actualSha = await PackArtifactEngine.CopyAndHashAsync(cached, target, file.Size, ct);
                if (!string.Equals(actualSha, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Download hash mismatch: {file.Name}");
            }
            if (new FileInfo(target).Length != file.Size || !string.Equals(actualSha, file.Sha256, StringComparison.OrdinalIgnoreCase))
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
            Description = "Star Wars pack for HellForge. Choose difficulties 1–12 in game. Gameplay and multiplayer validation pending.", Options = Array.Empty<object>()
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
        _ = await PackArtifactEngine.CopyAndHashAsync(source, target, expected, ct, bytes);
    }
    internal static Task CopyExactAsync(Stream input, Stream output, long expected, CancellationToken ct, Action<long>? bytes = null) =>
        PackArtifactEngine.CopyExactStreamAsync(input, output, expected, ct, bytes);

    sealed record VerifyStamp(string Folder, string Identity, long VerifiedUtcTicks, long ReceiptSize, long ReceiptTicks, long ReceiptCreationTicks,
        long ManifestSize, long ManifestTicks, long ManifestCreationTicks, Dictionary<string, VerifyFileStamp> Files);
    sealed record VerifyFileStamp(long Size, long LastWriteTicks, long CreationTicks);

    public static Task VerifyAsync(string folder, Selection expected, CancellationToken ct = default) =>
        VerifyAsync(folder, expected, null, ct);

    public static async Task VerifyAsync(string folder, Selection expected, IProgress<(long Done, long Total)>? bytes, CancellationToken ct = default)
    {
        await VerifyMetadataAsync(folder, expected, ct);
        await VerifyContentAsync(folder, expected, bytes, ct);
    }

    /// <summary>Verifies pack integrity for Launch, skipping redundant multi-GB hashing when a verified layout stamp matches size and mtime.</summary>
    public static async Task<bool> VerifyForLaunchAsync(string folder, Selection expected, string? verifyCachePath,
        IProgress<(long Done, long Total)>? bytes = null, CancellationToken ct = default)
    {
        await VerifyMetadataAsync(folder, expected, ct);
        var total = expected.Files.Sum(f => f.Size);
        if (verifyCachePath != null && TryMatchVerifiedLayout(folder, expected, verifyCachePath))
        {
            await VerifyFolderMetadataAsync(folder, expected, ct);
            bytes?.Report((total, total));
            return true;
        }
        await VerifyContentAsync(folder, expected, bytes, ct);
        if (verifyCachePath != null) SaveVerifiedLayout(folder, expected, verifyCachePath);
        return false;
    }

    // Fast readiness only. Every launch runs VerifyForLaunchAsync.
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
    static async Task VerifyContentAsync(string folder, Selection expected, IProgress<(long Done, long Total)>? bytes, CancellationToken ct)
    {
        // Compare to the trusted catalog, not only the mutable receipt, using bounded-parallel 1 MiB SequentialScan reads.
        await PackArtifactEngine.VerifyPackFilesAsync(folder, expected.Files, bytes, ct);
        await VerifyFolderMetadataAsync(folder, expected, ct);
    }

    static async Task VerifyFolderMetadataAsync(string folder, Selection expected, CancellationToken ct)
    {
        var present = Directory.EnumerateFiles(folder).Select(p => Path.GetFileName(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowed = expected.Files.Select(f => f.Name).Append("manifest.json").Append(ReceiptName);
        if (!present.SetEquals(allowed)) throw new InvalidDataException("The pack contains unexpected files; prepare it again.");
        var manifestPath = FileSafety.Child(folder, "manifest.json");
        FileSafety.RejectLink(manifestPath);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        if (manifest.RootElement.GetProperty("Guid").GetString() != GuidFor(expected.Mode) ||
            manifest.RootElement.GetProperty("Options").GetArrayLength() != 0 || manifest.RootElement.TryGetProperty("Include", out _))
            throw new InvalidDataException("The cosmetic pack manifest changed; prepare it again.");
        if (Directory.EnumerateDirectories(folder).Any()) throw new InvalidDataException("Unexpected folders in the Star Wars pack.");
    }

    static bool TryMatchVerifiedLayout(string folder, Selection expected, string verifyCachePath)
    {
        try
        {
            for (var parent = new DirectoryInfo(Path.GetFullPath(verifyCachePath)); parent != null; parent = parent.Parent)
                FileSafety.RejectLink(parent.FullName);
            if (!File.Exists(verifyCachePath) || new FileInfo(verifyCachePath).Length > 4 * 1024 * 1024) return false;
            var stamp = JsonSerializer.Deserialize<VerifyStamp>(File.ReadAllText(verifyCachePath), Json);
            if (stamp?.Files == null) return false;
            var normalizedFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (!string.Equals(stamp.Folder, normalizedFolder, PlatformPaths.Comparison) ||
                !string.Equals(stamp.Identity, SelectionIdentity(expected), StringComparison.Ordinal) ||
                stamp.Files.Count != expected.Files.Count) return false;
            var receiptInfo = new FileInfo(FileSafety.Child(folder, ReceiptName));
            var manifestInfo = new FileInfo(FileSafety.Child(folder, "manifest.json"));
            if (!receiptInfo.Exists || !manifestInfo.Exists ||
                receiptInfo.Length != stamp.ReceiptSize || receiptInfo.LastWriteTimeUtc.Ticks != stamp.ReceiptTicks || receiptInfo.CreationTimeUtc.Ticks != stamp.ReceiptCreationTicks ||
                manifestInfo.Length != stamp.ManifestSize || manifestInfo.LastWriteTimeUtc.Ticks != stamp.ManifestTicks || manifestInfo.CreationTimeUtc.Ticks != stamp.ManifestCreationTicks)
                return false;
            // Racy-clean guard: files modified or recreated within 2 seconds of the cached verification are always re-hashed.
            var racyCutoffTicks = stamp.VerifiedUtcTicks - TimeSpan.FromSeconds(2).Ticks;
            foreach (var file in expected.Files)
            {
                var path = FileSafety.Child(folder, file.Name);
                FileSafety.RejectLink(path);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != file.Size || info.LastWriteTimeUtc.Ticks > racyCutoffTicks || info.CreationTimeUtc.Ticks > racyCutoffTicks ||
                    !stamp.Files.TryGetValue(file.Name, out var cached) ||
                    cached.Size != file.Size || cached.LastWriteTicks != info.LastWriteTimeUtc.Ticks || cached.CreationTicks != info.CreationTimeUtc.Ticks)
                    return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static void SaveVerifiedLayout(string folder, Selection expected, string verifyCachePath)
    {
        try
        {
            for (var parent = new DirectoryInfo(Path.GetFullPath(verifyCachePath)); parent != null; parent = parent.Parent)
                FileSafety.RejectLink(parent.FullName);
            var receiptInfo = new FileInfo(FileSafety.Child(folder, ReceiptName));
            var manifestInfo = new FileInfo(FileSafety.Child(folder, "manifest.json"));
            if (!receiptInfo.Exists || !manifestInfo.Exists) return;
            var files = new Dictionary<string, VerifyFileStamp>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in expected.Files)
            {
                var info = new FileInfo(FileSafety.Child(folder, file.Name));
                if (!info.Exists) return;
                files[file.Name] = new(info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);
            }
            var stamp = new VerifyStamp(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), SelectionIdentity(expected),
                DateTime.UtcNow.Ticks, receiptInfo.Length, receiptInfo.LastWriteTimeUtc.Ticks, receiptInfo.CreationTimeUtc.Ticks,
                manifestInfo.Length, manifestInfo.LastWriteTimeUtc.Ticks, manifestInfo.CreationTimeUtc.Ticks, files);
            var directory = Path.GetDirectoryName(Path.GetFullPath(verifyCachePath))!;
            Directory.CreateDirectory(directory);
            var temp = verifyCachePath + ".tmp";
            FileSafety.RejectLink(temp);
            File.WriteAllText(temp, JsonSerializer.Serialize(stamp, Json));
            File.Move(temp, verifyCachePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException) { }
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
    public static Task<string> HashAsync(string path, CancellationToken ct = default) =>
        PackArtifactEngine.HashFileAsync(path, ct);
}
