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
            texture = "full";
            changes = new Dictionary<string, bool> { ["droids"] = true, ["covenant"] = false };
        }
        var resource = mode == "empire" ? "Clonedivers.HellForge.Empire.Manifest.json" : "Clonedivers.HellForge.Manifest.json";
        using var stream = typeof(HellForgePack).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("This launcher has no pinned HellForge pack catalog.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        return Select(Manifest.Parse(System.Text.Encoding.UTF8.GetString(bytes)), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), mode, texture, changes);
    }

    public static async Task<Plan> PlanAsync(Selection selection, string output, IEnumerable<string> sources,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; choose a new folder.");
        RejectInstallationOutput(output);
        // An interrupted export keeps its hash-verified files; reuse them rather
        // than downloading the whole pack again.
        var exports = new DirectoryInfo(Path.GetDirectoryName(output)!);
        if (exports.Name == "exports" && exports.Exists && !exports.Attributes.HasFlag(FileAttributes.ReparsePoint))
            sources = sources.Concat(exports.EnumerateDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName)).ToList();
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
    public static Task ExportAsync(Plan plan, bool download, IProgress<string>? progress = null, CancellationToken ct = default) =>
        ExportAsync(plan, download, progress, ct, AvailableExportBytes);

    internal static async Task ExportAsync(Plan plan, bool download, IProgress<string>? progress, CancellationToken ct, Func<string, long?> availableBytes)
    {
        ct.ThrowIfCancellationRequested();
        FileSafety.ValidateTargets(plan.Selection.Files);
        if (plan.Missing.Count > 0 && !download) throw new IOException("Missing content. Add source folders or allow verified downloads.");
        if (Directory.Exists(plan.Output) || File.Exists(plan.Output)) throw new IOException("Output already exists; choose a new folder.");
        RejectInstallationOutput(plan.Output);
        var needed = checked(plan.Bytes + ExportReserve); // local reuse still makes independent copies of every target
        if (availableBytes(plan.Output) is long free && free < needed)
            throw new IOException($"Not enough space to prepare the pack files: {Pack.FormatBytes(needed)} needed for this copy including working space, {Pack.FormatBytes(Math.Max(0, free))} available. Free space on this drive, then try again.");
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(plan.Output);
        var started = DateTime.UtcNow; string? unverified = null;
        try { await ExportFilesAsync(plan, progress, ct, target => unverified = target); }
        catch
        {
            // Keep verified files for the next plan to reuse; drop anything
            // unverified, and the folder itself when nothing was verified.
            try
            {
                foreach (var path in new[] { unverified, Path.Combine(plan.Output, ReceiptName), Path.Combine(plan.Output, "manifest.json") })
                    if (path != null) File.Delete(path);
                if (!Directory.EnumerateFileSystemEntries(plan.Output).Any()) Directory.Delete(plan.Output);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
        PruneAbandonedExports(plan.Output, started);
    }
    static async Task ExportFilesAsync(Plan plan, IProgress<string>? progress, CancellationToken ct, Action<string?> writing)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var exported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int done = 0;
        foreach (var file in plan.Selection.Files)
        {
            ct.ThrowIfCancellationRequested();
            var target = FileSafety.Child(plan.Output, file.Name); writing(target);
            if (file.Size == 0) await File.WriteAllBytesAsync(target, [], ct);
            else if (exported.TryGetValue(file.Sha256, out var prior)) await CopyLocalAsync(prior, target, file.Size, ct);
            else if (plan.Local.TryGetValue(file.Sha256, out var source)) await CopyLocalAsync(source, target, file.Size, ct);
            else
            {
                progress?.Report($"Downloading {done + 1}/{plan.Selection.Files.Count}: {Pack.FormatBytes(file.Size)}");
                if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
                    !uri.AbsolutePath.StartsWith("/owendavidgoode/clonedivers/releases/download/", StringComparison.Ordinal))
                    throw new InvalidDataException("Only Clonedivers GitHub release assets may be downloaded.");
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length != file.Size) throw new InvalidDataException("Download size differs from catalog.");
                await using var content = await response.Content.ReadAsStreamAsync(ct);
                await using var disk = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await CopyExactAsync(content, disk, file.Size, ct);
            }
            if (new FileInfo(target).Length != file.Size || !string.Equals(await HashAsync(target, ct), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Export hash mismatch: {file.Name}");
            writing(null); exported.TryAdd(file.Sha256, target);
            if (++done % 100 == 0) progress?.Report($"Verified {Math.Min(done, plan.Selection.Files.Count)}/{plan.Selection.Files.Count} files…");
        }
        await File.WriteAllTextAsync(Path.Combine(plan.Output, ReceiptName), JsonSerializer.Serialize(new Receipt { Selection = plan.Selection }, Json), ct);
        var name = plan.Selection.Mode == "empire" ? "EmpireDivers" : "Clonedivers";
        await File.WriteAllTextAsync(Path.Combine(plan.Output, "manifest.json"), JsonSerializer.Serialize(new {
            Version = 1, Guid = GuidFor(plan.Selection.Mode), Name = $"{name} {plan.Selection.Version} ({plan.Selection.Texture})",
            Description = "Star Wars pack for HellForge. Choose difficulties 1–11 in game. Gameplay and multiplayer validation pending.", Options = Array.Empty<object>()
        }, Json), ct);
    }
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

    internal static async Task CopyLocalAsync(string source, string target, long expected, CancellationToken ct)
    {
        FileSafety.RejectLink(source);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != expected) throw new InvalidDataException("Reusable pack content changed size; prepare again.");
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await CopyExactAsync(input, output, expected, ct);
    }
    internal static async Task CopyExactAsync(Stream input, Stream output, long expected, CancellationToken ct)
    {
        var buffer = new byte[128 * 1024]; long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (count > expected - total) throw new InvalidDataException("Pack content exceeds catalog size.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
            total += count;
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
