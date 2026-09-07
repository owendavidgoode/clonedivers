// Clonedivers — Helldivers 2 mod on/off switch + Steam launcher.
//
// The whole app lives in this one file on purpose. It does exactly two things:
//   1. Moves every *.patch_* file between <game>\data\ (ON) and <game>\mods_off\ (OFF).
//      Move = rename on the same drive, so gigabyte mod packs flip instantly.
//   2. Opens steam://rungameid/553850 so Steam launches the game the normal way.
// It never injects into, hooks, reads, or otherwise touches the game process.

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;
using static Clonedivers.Palette;

namespace Clonedivers;

/// <summary>Where the mod files currently live. Always derived from disk, never stored.</summary>
public enum ModState { GameNotFound, NoModFiles, On, Off }

/// <summary>Pure file logic. No UI, no processes. Exercised by Clonedivers.Tests.</summary>
public static class ModFiles
{
    public const string DataFolder = "data";
    public const string OffFolder = "mods_off";

    // Helldivers 2 mods are "<archive-hash>.patch_<n>" plus optional companions
    // "<archive-hash>.patch_<n>.gpu_resources" and ".stream". The vanilla game ships
    // no *.patch_* files at all, so this pattern identifies mods exactly.
    // "game.patch" (no underscore+digits) and hand-parked "x.patch_0.bak" / ".disabled" are deliberately NOT matches.
    static readonly Regex PatchName = new(@"\.patch_\d+(\.(gpu_resources|stream))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsPatchFile(string path) => PatchName.IsMatch(Path.GetFileName(path));

    // "<archive>.patch_<n>" + optional companion, split for renumbering and ordering.
    static readonly Regex PatchParts = new(@"^(?<arch>.+?)\.patch_(?<idx>\d+)(?<comp>\.(?:gpu_resources|stream))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParsePatchName(string name, out string archive, out int index, out string companion)
    {
        var m = PatchParts.Match(Path.GetFileName(name));
        archive = m.Success ? m.Groups["arch"].Value : ""; companion = m.Success ? m.Groups["comp"].Value : "";
        index = m.Success && int.TryParse(m.Groups["idx"].Value, out var i) ? i : -1;
        return m.Success && index >= 0;
    }

    /// <summary>A per-file update parks incoming files under this suffix until every one is in place. It never matches
    /// the patch pattern, so the game sees a folder with fewer mods rather than a half-numbered set.</summary>
    public const string StagedSuffix = ".clonedivers-staged";
    public static bool IsStagedFile(string path) => Path.GetFileName(path).EndsWith(StagedSuffix, StringComparison.OrdinalIgnoreCase);
    public static string[] ListStagedFiles(string gameDir)
    {
        var data = Path.Combine(gameDir, DataFolder);
        return Directory.Exists(data) ? Directory.EnumerateFiles(data).Where(IsStagedFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
    }
    /// <summary>True while an interrupted update has staged files waiting: the app then offers "Finish update" and blocks other moves.</summary>
    public static bool HasStagedFiles(string? gameDir) => GameLocator.IsGameDir(gameDir) && ListStagedFiles(gameDir!).Length > 0;

    public static string[] ListPatchFiles(string dir)
    {
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dir)
            .Where(IsPatchFile)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Moves (renames) every patch file in <paramref name="fromDir"/> into <paramref name="toDir"/>,
    /// overwriting any stale copy already there. Returns how many files moved.</summary>
    public static int MoveAllPatchFiles(string fromDir, string toDir)
    {
        var files = ListPatchFiles(fromDir);
        if (files.Length == 0) return 0;
        Directory.CreateDirectory(toDir);

        // Keep going past a locked file (antivirus mid-scan, a download still finishing) so one bad file
        // doesn't strand the rest, then report everything that failed in one message.
        var failed = new List<string>();
        string? firstReason = null;
        foreach (var src in files)
        {
            var dst = Path.Combine(toDir, Path.GetFileName(src));
            try
            {
                // A stale copy at the destination may be read-only; it is being replaced anyway.
                if (File.Exists(dst) && (File.GetAttributes(dst) & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(dst, FileAttributes.Normal);
                File.Move(src, dst, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(Path.GetFileName(src));
                firstReason ??= ex.Message;
            }
        }
        if (failed.Count > 0)
            throw new IOException($"Couldn't move {failed.Count} of {files.Length} file(s): {string.Join(", ", failed)}\n{firstReason}");
        return files.Length;
    }

    public static ModState GetState(string? gameDir)
    {
        if (!GameLocator.IsGameDir(gameDir)) return ModState.GameNotFound;
        if (ListPatchFiles(Path.Combine(gameDir!, DataFolder)).Length > 0) return ModState.On;
        if (ListPatchFiles(Path.Combine(gameDir!, OffFolder)).Length > 0) return ModState.Off;
        return ModState.NoModFiles;
    }

    /// <summary>ON → OFF moves data\ → mods_off\. OFF → ON moves mods_off\ → data\. Returns files moved.</summary>
    public static int Toggle(string gameDir)
    {
        var data = Path.Combine(gameDir, DataFolder);
        var off = Path.Combine(gameDir, OffFolder);
        return GetState(gameDir) switch
        {
            ModState.On => MoveAllPatchFiles(data, off),
            ModState.Off => MoveAllPatchFiles(off, data),
            _ => 0,
        };
    }
}

/// <summary>Finds the Helldivers 2 install through Steam's registry key and library list.</summary>
public static class GameLocator
{
    public const string SteamAppId = "553850";
    public const string GameFolderName = "Helldivers 2";

    /// <summary>True if <paramref name="dir"/> is the game root: has data\ and bin\helldivers2.exe.</summary>
    public static bool IsGameDir(string? dir) =>
        !string.IsNullOrWhiteSpace(dir)
        && Directory.Exists(Path.Combine(dir, ModFiles.DataFolder))
        && File.Exists(Path.Combine(dir, "bin", "helldivers2.exe"));

    /// <summary>Accepts the game folder or its data\ / bin\ child (people click one level too deep)
    /// and returns the game folder, or null if neither is valid.</summary>
    public static string? NormalizeGameDir(string? picked)
    {
        if (string.IsNullOrWhiteSpace(picked)) return null;
        string full;
        try { full = Path.GetFullPath(picked).TrimEnd('\\', '/'); }
        catch { return null; }
        if (IsGameDir(full)) return full;
        var parent = Path.GetDirectoryName(full);
        return IsGameDir(parent) ? parent : null;
    }

    public static string? SteamPath()
    {
        string? p = null;
        try
        {
            p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrWhiteSpace(p))
                p = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        }
        catch { /* no registry access: fall through */ }
        return SafeFullPath(p);
    }

    // Matches   "path"   "C:\\Program Files (x86)\\Steam"   including escaped quotes inside the value.
    static readonly Regex VdfPathKey = new(@"""path""\s+""((?:[^""\\]|\\.)*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Every "path" value in libraryfolders.vdf text, with VDF escapes (\\ and \") undone.</summary>
    public static IEnumerable<string> ParseLibraryPaths(string vdfText)
    {
        foreach (Match m in VdfPathKey.Matches(vdfText))
        {
            var unescaped = Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1");
            if (!string.IsNullOrWhiteSpace(unescaped)) yield return unescaped;
        }
    }

    /// <summary>Candidate game folders: every library in libraryfolders.vdf, then the Steam root itself
    /// (the vdf usually lists the root too, with proper casing; the registry value is often all-lowercase).</summary>
    public static IEnumerable<string> CandidateGameDirs()
    {
        var steam = SteamPath();
        if (steam is null) yield break;

        var libs = new List<string>();
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            string text = "";
            try { text = File.ReadAllText(vdf); } catch { /* unreadable: just use Steam root */ }
            foreach (var lib in ParseLibraryPaths(text))
                if (SafeFullPath(lib) is string full) libs.Add(full);
        }
        libs.Add(steam);

        // Prefer the library whose manifest says the game is installed there (a leftover copy elsewhere loses).
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(l => File.Exists(Path.Combine(l, "steamapps", "appmanifest_" + SteamAppId + ".acf"))))
            yield return Path.Combine(lib, "steamapps", "common", GameFolderName);
    }

    public static string? AutoDetect() => CandidateGameDirs().FirstOrDefault(IsGameDir);

    static string? SafeFullPath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        try { return Path.GetFullPath(p.Replace('/', '\\')).TrimEnd('\\'); }
        catch { return null; }
    }
}

/// <summary>%APPDATA%\Clonedivers\config.json — a manually chosen game path and which pack version was installed.</summary>
public sealed class Settings
{
    public string? GamePath { get; set; }
    public string? InstalledPackVersion { get; set; }
    /// <summary>Hidden test hook: a URL (or file path) that replaces manifest.json. Shown in the footer when set.</summary>
    public string? ManifestUrl { get; set; }
    /// <summary>Optional pack groups the friend switched on or off (option id → enabled); anything absent uses the manifest default.</summary>
    public Dictionary<string, bool> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers");
    public static readonly string FilePath = Path.Combine(Dir, "config.json");
    public static readonly string HashCachePath = Path.Combine(Dir, "hashes.json");      // static init order: after Dir
    public static readonly string PlanPath = Path.Combine(Dir, "apply-plan.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt or unreadable: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only profile: the app still works, it just re-detects next time */ }
    }
}

/// <summary>The only two things we do with the game: notice it is running, and ask Steam to start it.</summary>
public static class Game
{
    /// <summary>Tests swap this out; the app never does.</summary>
    public static Func<bool> IsRunningCheck = IsRunningNow;
    public static bool IsRunning() => IsRunningCheck();

    static bool IsRunningNow()
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName("helldivers2"); }
        catch { return false; }
        var any = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        return any;
    }

    /// <summary>The game needs Steam running (Steamworks + GameGuard), so always go through Steam.</summary>
    public static void LaunchViaSteam() =>
        Process.Start(new ProcessStartInfo("steam://rungameid/" + GameLocator.SteamAppId) { UseShellExecute = true });
}

/// <summary>One file of the pack. Format 2 (manifest.json) lists every *.patch_* file by its final name;
/// format 1 (the frozen pack.json for 1.2.0) lists zip parts without names.</summary>
public sealed class PackFile
{
    public string Name { get; set; } = "";       // final name in data\ (format 2); "" for a format-1 zip part
    public string Url { get; set; } = "";        // "" for a zero-byte file: nothing to download, the app creates it
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";     // lower-case hex
    public string? Option { get; set; }          // optional-group id (PackOption.Id); null = always installed
    public string? UnlessOption { get; set; }    // skipped while this option is ON (the base file a variant replaces)

    /// <summary>SHA-256 of an empty file. 182 of the 604 r3 files are empty companions, so it is special-cased everywhere.</summary>
    public const string EmptySha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public PackFile Clone() => new() { Name = Name, Url = Url, Size = Size, Sha256 = Sha256, Option = Option, UnlessOption = UnlessOption };
}

/// <summary>A group of pack files friends can switch on or off (the Republic Commando squad, say). Files carry the option id;
/// the app renumbers whatever is enabled so switching a group off never leaves a gap in the patch numbers.</summary>
public sealed class PackOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Default { get; set; } = true;
}

/// <summary>The pack block of manifest.json (or the whole of a format-1 pack.json).</summary>
public sealed class PackManifest
{
    public string Version { get; set; } = "";
    public string Name { get; set; } = "Clonedivers pack";
    public string Notes { get; set; } = "";
    public string? GameBuild { get; set; }              // Steam buildid the pack was built and tested on
    public List<string> GameDepots { get; set; } = new();
    public string Status { get; set; } = "ok";          // "ok" | "broken" — flipped from the GitHub web editor when a game patch breaks things
    public string StatusNotes { get; set; } = "";
    public List<PackOption> Options { get; set; } = new();
    public List<PackFile> Files { get; set; } = new();

    public long TotalSize => Files.Sum(f => f.Size);
    public bool IsBroken => string.Equals(Status, "broken", StringComparison.OrdinalIgnoreCase);
    /// <summary>Every file that has bytes has somewhere to get them from. Zero-byte entries need no URL.</summary>
    public bool IsPublished => Files.Count > 0 && Files.All(f => f.Size == 0 || !string.IsNullOrWhiteSpace(f.Url));
    /// <summary>Format 2: files are listed one by one with their final names.</summary>
    public bool IsPerFile => Files.Count > 0 && Files.All(f => f.Name.Length > 0);
}

/// <summary>The newest Clonedivers exe, so the app can update itself.</summary>
public sealed class AppRelease
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";

    /// <summary>The only URL the self-updater accepts: this repo's own release asset for exactly this version.</summary>
    public string PinnedUrl => $"https://github.com/owendavidgoode/clonedivers/releases/download/v{Version}/Clonedivers.exe";
    public bool IsPinned => string.Equals(Url, PinnedUrl, StringComparison.OrdinalIgnoreCase);
    public bool IsNewerThan(string current) =>
        System.Version.TryParse(Version, out var v) && System.Version.TryParse(current, out var c)
        && new System.Version(v.Major, v.Minor, Math.Max(v.Build, 0)) > new System.Version(c.Major, c.Minor, Math.Max(c.Build, 0));
}

/// <summary>manifest.json in the repo: the newest app and the current pack. Format 1 (pack.json) still parses so the
/// zip-based tests and hand-out packs keep working.</summary>
public sealed class Manifest
{
    public int Format { get; set; } = 2;
    public AppRelease? App { get; set; }
    public PackManifest? Pack { get; set; }

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static Manifest Parse(string json)
    {
        Manifest m;
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("manifest is not a JSON object");
            var legacy = root.TryGetProperty("files", out _) && !root.TryGetProperty("pack", out _);
            m = legacy
                ? new Manifest { Format = 1, Pack = JsonSerializer.Deserialize<PackManifest>(json, JsonOpts) }
                : JsonSerializer.Deserialize<Manifest>(json, JsonOpts) ?? new Manifest();
        }
        if (m.Pack is { } p)
        {
            // A name may appear twice only as a variant pair: the base file (unlessOption = X) and its replacement (option = X).
            var seen = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
            var optionIds = new HashSet<string>(p.Options.Select(o => o.Id ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (var f in p.Files)
            {
                f.Sha256 = (f.Sha256 ?? "").Trim().ToLowerInvariant();
                f.Url = (f.Url ?? "").Trim();
                f.Name = (f.Name ?? "").Trim();
                if (f.Size == 0) f.Sha256 = PackFile.EmptySha;
                if (f.Name.Length == 0) continue;   // format-1 zip part
                if (!ModFiles.IsPatchFile(f.Name) || f.Name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
                    throw new InvalidDataException($"manifest.json lists an invalid file name: {f.Name}");
                if (seen.TryGetValue(f.Name, out var other))
                {
                    var pair = (other.UnlessOption is not null && string.Equals(other.UnlessOption, f.Option, StringComparison.OrdinalIgnoreCase))
                            || (f.UnlessOption is not null && string.Equals(f.UnlessOption, other.Option, StringComparison.OrdinalIgnoreCase));
                    if (!pair) throw new InvalidDataException($"manifest.json lists {f.Name} twice without a variant option");
                }
                else seen[f.Name] = f;
                if (f.Size > 0 && f.Sha256.Length != 64) throw new InvalidDataException($"manifest.json has no SHA-256 for {f.Name}");
                if (f.Option is not null && !optionIds.Contains(f.Option)) throw new InvalidDataException($"manifest.json: {f.Name} refers to an unknown option '{f.Option}'");
                if (f.UnlessOption is not null && !optionIds.Contains(f.UnlessOption)) throw new InvalidDataException($"manifest.json: {f.Name} refers to an unknown option '{f.UnlessOption}'");
            }
}
        return m;
    }
}

/// <summary>Where a local mod file was found. Order matters: the planner prefers data\ over mods_off\ over mods_old\ over staged.</summary>
public enum LocalWhere { Data, Off, Old, Staged }

/// <summary>One file found on disk. Sha is null when it was not worth hashing (its size matches nothing in the manifest).</summary>
public sealed record LocalFile(string Path, string Name, long Size, string? Sha, LocalWhere Where);

/// <summary>%APPDATA%\Clonedivers\hashes.json: name|size|mtime → sha256, so the second inventory costs nothing.
/// Keyed by file name, not path: an ON/OFF toggle moves every file and would otherwise throw the whole cache away.</summary>
public sealed class HashCache
{
    public int Format { get; set; } = 1;
    public Dictionary<string, string> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string Key(string name, long size, DateTime lastWriteUtc) => $"{name.ToLowerInvariant()}|{size}|{lastWriteUtc.Ticks}";

    public static HashCache Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var c = JsonSerializer.Deserialize<HashCache>(File.ReadAllText(path));
                if (c is not null) { c.Entries = new Dictionary<string, string>(c.Entries, StringComparer.OrdinalIgnoreCase); return c; }
            }
        }
        catch { /* corrupt cache: rebuild */ }
        return new HashCache();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* the cache is a convenience; losing it costs one re-hash */ }
    }
}

public enum PlanOp { Park, Stage, Copy, Create, Finalize }

/// <summary>One file operation. Park: → mods_old\ (a free ".N" name is picked when the target exists).
/// Stage: rename/move into data\&lt;name&gt;.clonedivers-staged. Copy: duplicate a local file into a staged name.
/// Create: zero-byte file at a staged name. Finalize: staged → final name (never overwrites).</summary>
public sealed record PlanAction(PlanOp Op, string From, string To, string Sha, long Size);

/// <summary>Everything an update will do, computed before anything moves. Downloads are one per unique sha.</summary>
public sealed class UpdatePlan
{
    public List<PlanAction> Actions { get; } = new();
    public List<PackFile> Downloads { get; } = new();
    public long BytesToDownload { get; set; }
    public long BytesToCopy { get; set; }
    public int RenameCount { get; set; }
    public int ParkCount { get; set; }
    public int CopyCount { get; set; }
    public int CreateCount { get; set; }
    public int InPlaceCount { get; set; }
    public bool IsNoOp => Actions.Count == 0 && Downloads.Count == 0;
}

/// <summary>What Apply / Replay did. Interrupted = something could not be moved; the plan file stays and Finish update retries.</summary>
public sealed class ApplyResult
{
    public int Renamed, Parked, Copied, Created;
    public List<string> Failed { get; } = new();
    public string? Reason;
    public bool Interrupted => Failed.Count > 0 || Reason is not null;
}

sealed class PlanFile
{
    public int Format { get; set; } = 1;
    public string PackVersion { get; set; } = "";
    public string GameDir { get; set; } = "";
    public List<PlanFileAction> Actions { get; set; } = new();
}
sealed class PlanFileAction
{
    public string Op { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Sha { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>Pack install. Two paths: the legacy zip path (Install pack from file…) and the per-file path (manifest.json):
/// inventory what is on disk by hash, plan renames/copies/downloads, download only what is missing, then apply.
/// Still just files — a zip reader, a hasher and a downloader, nothing that knows the game exists.</summary>
public static class Pack
{
    // The API view is fresh the moment a push lands; the raw CDN can lag up to five minutes, so it is the fallback.
    public const string ManifestApiUrl = "https://api.github.com/repos/owendavidgoode/clonedivers/contents/manifest.json?ref=main";
    public const string ManifestUrl = "https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/manifest.json";
    public const string OldFolder = "mods_old";            // stale mods parked here; safe to delete
    public const string DownloadFolder = "mods_download";  // <sha> (complete) and <sha>.clonedivers-partial live here until applied
    public const string PartialSuffix = ".clonedivers-partial";   // never matches the patch-file pattern

    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Clonedivers/{AppInfo.Version} (+https://github.com/owendavidgoode/clonedivers)");
        return c;
    }

    /// <summary>Legacy entry point kept for the zip path and its tests: the pack block of any manifest shape.</summary>
    public static PackManifest? ParseManifest(string json) => Manifest.Parse(json).Pack;

    /// <summary>Fetches manifest.json: GitHub API first (fresh, but rate-limited to 60/hour per IP), raw CDN as fallback.
    /// <paramref name="overrideUrl"/> (Settings.ManifestUrl) replaces both — a hidden hook for testing against a local server.</summary>
    public static async Task<Manifest> FetchManifestAsync(string? overrideUrl, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(overrideUrl)) return Manifest.Parse(await GetTextAsync(overrideUrl, ct));
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var req = new HttpRequestMessage(HttpMethod.Get, ManifestApiUrl);
            req.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
            using var resp = await Http.SendAsync(req, cts.Token);
            if (resp.IsSuccessStatusCode)
                return Manifest.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* rate-limited or blocked: use the CDN copy */ }
        return Manifest.Parse(await GetTextAsync(ManifestUrl, ct));
    }

    static async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(12));
        var bust = url + (url.Contains('?') ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return await Http.GetStringAsync(bust, cts.Token);
    }

    // ---------------------------------------------------------------- legacy zip path (Install pack from file…)

    /// <summary>Flattened names of the patch files inside a zip. Throws if the zip is an options package
    /// (same file name under several folders) rather than a deployed pack.</summary>
    public static string[] ListPatchEntries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Where(e => e.Name.Length > 0 && ModFiles.IsPatchFile(e.Name)).Select(e => e.Name).ToArray();
        var dup = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
            throw new InvalidDataException(
                $"'{dup.Key}' appears more than once inside {Path.GetFileName(zipPath)}. That is a mod's options package, not a deployed pack. " +
                "Build the pack from the game's data\\ folder after deploying with Arsenal (tools\\build-pack.ps1).");
        return names;
    }

    /// <summary>Extracted size of the patch files inside a zip, read from the zip directory alone (no decompression).</summary>
    public static long PatchBytes(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Where(e => e.Name.Length > 0 && ModFiles.IsPatchFile(e.Name)).Sum(e => e.Length);
    }

    /// <summary>Extracts every *.patch_* entry to <paramref name="dataDir"/> (flat, folders ignored), overwriting.
    /// Writes to a temp name first so a crash never leaves a half-written file that looks like a mod.</summary>
    public static int ExtractPatchFiles(string zipPath, string dataDir, IProgress<(long done, long total)>? progress, CancellationToken ct)
    {
        ListPatchEntries(zipPath);   // duplicate / empty check up front
        Directory.CreateDirectory(dataDir);
        foreach (var leftover in Directory.EnumerateFiles(dataDir, "*" + PartialSuffix)) File.Delete(leftover);

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => e.Name.Length > 0 && ModFiles.IsPatchFile(e.Name)).ToList();
        if (entries.Count == 0) throw new InvalidDataException($"No *.patch_* files inside {Path.GetFileName(zipPath)}.");

        long total = entries.Sum(e => e.Length), done = 0;
        var buffer = new byte[1 << 20];
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(dataDir, e.Name);
            var tmp = dest + PartialSuffix;
            using (var src = e.Open())
            using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length))
            {
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    dst.Write(buffer, 0, n);
                    done += n;
                    progress?.Report((done, total));
                }
            }
            ClearReadOnly(dest);
            File.Move(tmp, dest, overwrite: true);
        }
        return entries.Count;
    }

    /// <summary>The zip install, file-side: bring parked files back, park anything the pack does not contain
    /// in mods_old\, extract every zip into data\. Ends with the game in the ON state. Returns (installed, parked).
    /// Refused while an interrupted per-file update has staged files in data\ (Finish update first).</summary>
    public static (int installed, int parked) Install(string gameDir, IReadOnlyList<string> zips,
        Func<int, IProgress<(long done, long total)>?>? progressForZip, CancellationToken ct)
    {
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        var off = Path.Combine(gameDir, ModFiles.OffFolder);
        var old = Path.Combine(gameDir, OldFolder);
        if (ModFiles.HasStagedFiles(gameDir))
            throw new InvalidOperationException("A pack update was interrupted. Click Finish update first, then install from file.");

        var incoming = new HashSet<string>(zips.SelectMany(ListPatchEntries), StringComparer.OrdinalIgnoreCase);
        var dupAcross = zips.SelectMany(ListPatchEntries).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dupAcross is not null)
            throw new InvalidDataException($"'{dupAcross.Key}' is in more than one of the selected zips. Pick the parts of one pack only.");

        ModFiles.MoveAllPatchFiles(off, data);
        int parked = 0;
        foreach (var f in ModFiles.ListPatchFiles(data))
        {
            ct.ThrowIfCancellationRequested();
            if (incoming.Contains(Path.GetFileName(f))) continue;
            Directory.CreateDirectory(old);
            var dest = Path.Combine(old, Path.GetFileName(f));
            ClearReadOnly(dest);
            File.Move(f, dest, overwrite: true);
            parked++;
        }

        int installed = 0;
        for (int i = 0; i < zips.Count; i++)
            installed += ExtractPatchFiles(zips[i], data, progressForZip?.Invoke(i), ct);
        return (installed, parked);
    }

    // ---------------------------------------------------------------- downloads

    /// <summary>Downloads one file with HTTP Range resume, then checks size and SHA-256. A failed check deletes the file.</summary>
    public static async Task DownloadAsync(PackFile file, string destPath, IProgress<(long done, long total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        long existing = File.Exists(destPath) ? new FileInfo(destPath).Length : 0;
        if (file.Size > 0 && existing > file.Size) { File.Delete(destPath); existing = 0; }

        if (!(file.Size > 0 && existing == file.Size))   // already complete? then just verify below
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, file.Url);
            if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                File.Delete(destPath);
                await DownloadAsync(file, destPath, progress, ct);   // start over without a Range header
                return;
            }
            resp.EnsureSuccessStatusCode();
            if (string.Equals(resp.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "That link opens a web page instead of the file (Google Drive and OneDrive share links do this). " +
                    "Download it in your browser, then use 'Install pack from file'.");

            var resumed = resp.StatusCode == HttpStatusCode.PartialContent && existing > 0;
            if (!resumed) existing = 0;
            long total = file.Size > 0 ? file.Size : existing + (resp.Content.Headers.ContentLength ?? 0);

            using var net = await resp.Content.ReadAsStreamAsync(ct);
            using var fs = new FileStream(destPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            var buffer = new byte[1 << 20];
            long written = existing;
            int n;
            while ((n = await net.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                written += n;
                progress?.Report((written, total));
            }
        }

        var length = new FileInfo(destPath).Length;
        if (file.Size > 0 && length != file.Size)
        {
            File.Delete(destPath);
            throw new InvalidDataException($"Download ended at {length:N0} bytes but the pack file should be {file.Size:N0}. Deleted it; try again.");
        }
        if (!string.IsNullOrWhiteSpace(file.Sha256))
        {
            var hash = await Sha256Async(destPath, ct);
            if (!hash.Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destPath);
                throw new InvalidDataException("The download failed its integrity check (SHA-256 mismatch). Deleted it; try again.");
            }
        }
    }

    /// <summary>Downloads every missing sha in the plan into mods_download\ (largest first), one bar across all of them.
    /// Progress reports (bytesDone, bytesTotal, fileIndex, fileCount). Cancel keeps partials; a complete file is never re-fetched.</summary>
    public static async Task DownloadPlanAsync(UpdatePlan plan, string dlDir, IProgress<(long done, long total, int file, int files)>? progress, CancellationToken ct)
    {
        var files = plan.Downloads.OrderByDescending(f => f.Size).ToList();
        long total = files.Sum(f => f.Size), before = 0;
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = files[i];
            var complete = Path.Combine(dlDir, f.Sha256);
            if (!(File.Exists(complete) && new FileInfo(complete).Length == f.Size))
            {
                var partial = complete + PartialSuffix;
                long offset = before; int idx = i + 1;
                var reporter = new SyncProgress<(long done, long total)>(p => progress?.Report((offset + p.done, total, idx, files.Count)));
                await DownloadAsync(f, partial, reporter, ct);
                File.Move(partial, complete, overwrite: true);
            }
            before += f.Size;
            progress?.Report((before, total, i + 1, files.Count));
        }
    }

    /// <summary>Deletes everything in mods_download\ that is not a complete or partial download of a sha the manifest still wants
    /// (this is also how 1.2.0's leftover zip partials go away).</summary>
    public static int CleanDownloadFolder(string dlDir, IEnumerable<string> wantedShas)
    {
        if (!Directory.Exists(dlDir)) return 0;
        var keep = new HashSet<string>(wantedShas, StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        foreach (var path in Directory.EnumerateFiles(dlDir))
        {
            var name = Path.GetFileName(path);
            var sha = name.EndsWith(PartialSuffix, StringComparison.OrdinalIgnoreCase) ? name[..^PartialSuffix.Length] : name;
            if (keep.Contains(sha)) continue;
            try { File.Delete(path); removed++; } catch { /* locked: leave it */ }
        }
        return removed;
    }

    // ---------------------------------------------------------------- inventory

    /// <summary>The manifest's files after the friend's option choices, renumbered so the enabled set is gap-free.
    /// With every option on, names are exactly the manifest's.</summary>
    public static List<PackFile> EffectiveFiles(PackManifest pack, Func<string, bool> optionEnabled)
    {
        var kept = pack.Files.Where(f => (f.Option is null || optionEnabled(f.Option)) && (f.UnlessOption is null || !optionEnabled(f.UnlessOption))).Select(f => f.Clone()).ToList();
        var dup = kept.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidDataException($"manifest.json: {dup.Key} is listed twice for the same option choice");
        if (kept.Count == pack.Files.Count || !pack.IsPerFile) return kept;

        var parsed = kept.Select(f => (f, ok: ModFiles.TryParsePatchName(f.Name, out var arch, out var idx, out var comp), arch, idx, comp)).ToList();
        var ranks = parsed.Where(x => x.ok)
            .GroupBy(x => x.arch, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                          g => g.Select(x => x.idx).Distinct().OrderBy(i => i).Select((orig, rank) => (orig, rank)).ToDictionary(t => t.orig, t => t.rank),
                          StringComparer.OrdinalIgnoreCase);
        foreach (var x in parsed)
            if (x.ok) x.f.Name = $"{x.arch}.patch_{ranks[x.arch][x.idx]}{x.comp}";
        return kept;
    }

    /// <summary>Every mod file in data\, mods_off\ (and staged leftovers), hashed where it could possibly match a manifest entry.
    /// mods_old\ is hashed lazily, only for sizes the first two folders could not cover. Progress is in bytes hashed.</summary>
    public static async Task<List<LocalFile>> InventoryAsync(string gameDir, IReadOnlyList<PackFile> wanted, HashCache cache, bool forceRehash,
        IProgress<(long done, long total)>? progress, CancellationToken ct)
    {
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        var off = Path.Combine(gameDir, ModFiles.OffFolder);
        var old = Path.Combine(gameDir, OldFolder);
        var wantedSizes = wanted.Where(f => f.Size > 0).Select(f => f.Size).ToHashSet();
        var need = wanted.GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var found = new List<LocalFile>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        List<(string path, string name, FileInfo fi, LocalWhere where)> Scan(string dir, LocalWhere where)
        {
            var list = new List<(string, string, FileInfo, LocalWhere)>();
            if (!Directory.Exists(dir)) return list;
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                var fn = Path.GetFileName(path);
                if (where == LocalWhere.Staged)
                {
                    if (!fn.EndsWith(ModFiles.StagedSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add((path, fn[..^ModFiles.StagedSuffix.Length], new FileInfo(path), where));
                }
                else if (ModFiles.IsPatchFile(fn)) list.Add((path, fn, new FileInfo(path), where));
            }
            return list;
        }

        async Task HashBatch(List<(string path, string name, FileInfo fi, LocalWhere where)> batch, Func<long, bool> sizeWanted)
        {
            // Decide what needs hashing: size must be wanted; cache hit avoids the read; duplicate cache keys are hashed fresh and never cached.
            var jobs = new List<(string path, string name, FileInfo fi, LocalWhere where, string key, string? sha)>();
            var keyCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in batch)
            {
                var key = HashCache.Key(b.name, b.fi.Length, b.fi.LastWriteTimeUtc);
                keyCount[key] = keyCount.TryGetValue(key, out var n) ? n + 1 : 1;
                jobs.Add((b.path, b.name, b.fi, b.where, key, null));
            }
            long total = 0;
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                if (j.fi.Length == 0) { jobs[i] = j with { sha = PackFile.EmptySha }; continue; }
                if (!sizeWanted(j.fi.Length)) continue;                                   // cannot match anything: not worth reading
                if (!forceRehash && keyCount[j.key] == 1 && cache.Entries.TryGetValue(j.key, out var cached)) { jobs[i] = j with { sha = cached }; seenKeys.Add(j.key); continue; }
                total += j.fi.Length;
            }
            long done = 0;
            progress?.Report((done, total));
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                if (j.sha is not null || j.fi.Length == 0 || !sizeWanted(j.fi.Length)) { found.Add(new LocalFile(j.path, j.name, j.fi.Length, j.sha, j.where)); continue; }
                ct.ThrowIfCancellationRequested();
                long before = done;
                var sha = await Sha256Async(j.path, new SyncProgress<long>(b => progress?.Report((before + b, total))), ct);
                done += j.fi.Length;
                progress?.Report((done, total));
                if (keyCount[j.key] == 1) { cache.Entries[j.key] = sha; seenKeys.Add(j.key); }
                found.Add(new LocalFile(j.path, j.name, j.fi.Length, sha, j.where));
            }
        }

        var first = Scan(data, LocalWhere.Data).Concat(Scan(off, LocalWhere.Off)).Concat(Scan(data, LocalWhere.Staged)).ToList();
        await HashBatch(first, wantedSizes.Contains);

        // Lazy mods_old: only sizes of shas that data\ / mods_off\ could not fully cover.
        var have = found.Where(f => f.Sha is not null).GroupBy(f => f.Sha!).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var shortSizes = wanted.Where(f => f.Size > 0 && need[f.Sha256] > (have.TryGetValue(f.Sha256, out var h) ? h : 0)).Select(f => f.Size).ToHashSet();
        if (shortSizes.Count > 0)
        {
            var oldFiles = Scan(old, LocalWhere.Old).Where(x => shortSizes.Contains(x.fi.Length)).ToList();
            if (oldFiles.Count > 0) await HashBatch(oldFiles, shortSizes.Contains);
        }

        // Prune cache entries for files that no longer exist under any name we saw.
        foreach (var k in cache.Entries.Keys.Where(k => !seenKeys.Contains(k)).ToList()) cache.Entries.Remove(k);
        return found;
    }

    // ---------------------------------------------------------------- plan

    /// <summary>Decides every move without touching disk. Matching is by sha as a multiset: K manifest entries sharing one sha
    /// need K local copies; the shortfall is copied from a local twin when one exists, downloaded once otherwise.
    /// Local files that match nothing are parked. Zero-byte entries are created, never downloaded.</summary>
    public static UpdatePlan Plan(string gameDir, IReadOnlyList<PackFile> wanted, IReadOnlyList<LocalFile> local, IReadOnlySet<string> completeDownloads)
    {
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        var old = Path.Combine(gameDir, OldFolder);
        var dl = Path.Combine(gameDir, DownloadFolder);
        var plan = new UpdatePlan();
        string Staged(string name) => Path.Combine(data, name + ModFiles.StagedSuffix);

        var used = new HashSet<LocalFile>();
        var source = new Dictionary<PackFile, string>();          // entry → path it will be finalized from (in-place path or staged path)
        var inPlace = new HashSet<PackFile>();
        var stages = new List<PlanAction>();
        var copies = new List<PlanAction>();
        var creates = new List<PlanAction>();

        // 1. In place: right name, right bytes, already in data\.
        var byName = local.Where(l => l.Where == LocalWhere.Data).GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var e in wanted)
            if (byName.TryGetValue(e.Name, out var l) && string.Equals(l.Sha, e.Sha256, StringComparison.OrdinalIgnoreCase) && !used.Contains(l))
            { used.Add(l); source[e] = l.Path; inPlace.Add(e); plan.InPlaceCount++; }

        // 2. Keep by rename: any other local copy of the same bytes, preferring data\, then mods_off\, mods_old\, staged.
        var candidates = local.Where(l => l.Sha is not null).OrderBy(l => (int)l.Where)
            .GroupBy(l => l.Sha!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => new Queue<LocalFile>(g), StringComparer.OrdinalIgnoreCase);
        foreach (var e in wanted.Where(e => !inPlace.Contains(e)))
        {
            if (!candidates.TryGetValue(e.Sha256, out var q)) continue;
            LocalFile? pick = null;
            while (q.Count > 0) { var c = q.Dequeue(); if (!used.Contains(c)) { pick = c; break; } }
            if (pick is null) continue;
            used.Add(pick);
            var to = Staged(e.Name);
            if (!string.Equals(pick.Path, to, StringComparison.OrdinalIgnoreCase)) stages.Add(new PlanAction(PlanOp.Stage, pick.Path, to, e.Sha256, e.Size));
            source[e] = to;
            plan.RenameCount++;
        }

        // 3. Everything still without bytes: create (empty), copy from a twin, or download once per sha then copy.
        foreach (var group in wanted.Where(e => !source.ContainsKey(e)).GroupBy(e => e.Sha256, StringComparer.OrdinalIgnoreCase))
        {
            var entries = group.ToList();
            var donor = wanted.FirstOrDefault(e => string.Equals(e.Sha256, group.Key, StringComparison.OrdinalIgnoreCase) && source.ContainsKey(e));
            string? donorPath = donor is null ? null : source[donor];
            int start = 0;
            if (entries[0].Size == 0)
            {
                foreach (var e in entries) { var to = Staged(e.Name); creates.Add(new PlanAction(PlanOp.Create, "", to, e.Sha256, 0)); source[e] = to; plan.CreateCount++; }
                continue;
            }
            if (donorPath is null)
            {
                var first = entries[0];
                var from = Path.Combine(dl, first.Sha256);
                if (!completeDownloads.Contains(first.Sha256)) { plan.Downloads.Add(first.Clone()); plan.BytesToDownload += first.Size; }
                var to = Staged(first.Name);
                stages.Add(new PlanAction(PlanOp.Stage, from, to, first.Sha256, first.Size));
                source[first] = to; donorPath = to; start = 1;
            }
            for (int i = start; i < entries.Count; i++)
            {
                var e = entries[i]; var to = Staged(e.Name);
                copies.Add(new PlanAction(PlanOp.Copy, donorPath, to, e.Sha256, e.Size));
                source[e] = to; plan.CopyCount++; plan.BytesToCopy += e.Size;
            }
        }

        // 4. Park what is left in data\ / mods_off\ (and unmatched staged leftovers). mods_old\ files stay where they are.
        var parks = new List<PlanAction>();
        foreach (var l in local.Where(l => !used.Contains(l) && l.Where != LocalWhere.Old))
        {
            parks.Add(new PlanAction(PlanOp.Park, l.Path, Path.Combine(old, l.Name), l.Sha ?? "", l.Size));
            plan.ParkCount++;
        }

        // 5. Order: parks; stages by descending source patch index (companions with their base); copies/creates; finalizes ascending.
        static int IndexOf(string path) => ModFiles.TryParsePatchName(Path.GetFileName(path).Replace(ModFiles.StagedSuffix, ""), out _, out var i, out _) ? i : -1;
        plan.Actions.AddRange(parks);
        plan.Actions.AddRange(stages.OrderByDescending(s => IndexOf(s.From)).ThenBy(s => s.From, StringComparer.OrdinalIgnoreCase));
        plan.Actions.AddRange(copies);
        plan.Actions.AddRange(creates);
        plan.Actions.AddRange(wanted.Where(e => !inPlace.Contains(e))
            .OrderBy(e => ModFiles.TryParsePatchName(e.Name, out _, out var i, out _) ? i : int.MaxValue).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new PlanAction(PlanOp.Finalize, source[e], Path.Combine(data, e.Name), e.Sha256, e.Size)));
        return plan;
    }

    // ---------------------------------------------------------------- apply

    static readonly JsonSerializerOptions PlanJson = new() { WriteIndented = true };

    /// <summary>Runs the plan. The plan is written to <paramref name="planPath"/> before anything moves, so an interrupted run can be
    /// finished later without network or re-hashing. Never cancellable once started: every step is a same-volume rename or a small copy.</summary>
    public static ApplyResult Apply(string gameDir, UpdatePlan plan, string planPath, string packVersion, HashCache? cache, string? cachePath)
    {
        var pf = new PlanFile { PackVersion = packVersion, GameDir = gameDir, Actions = plan.Actions.Select(a => new PlanFileAction { Op = a.Op.ToString(), From = a.From, To = a.To, Sha = a.Sha, Size = a.Size }).ToList() };
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        File.WriteAllText(planPath, JsonSerializer.Serialize(pf, PlanJson));
        if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 started while installing. Close it and try again.");
        return Execute(gameDir, pf.Actions, planPath, cache, cachePath, replay: false);
    }

    /// <summary>Finishes an interrupted update from its plan file: every action is idempotent (done already → skip).
    /// Returns null when the plan cannot be trusted (file missing, wrong folder, or a stage/finalize whose source and target both exist),
    /// in which case the caller falls back to a fresh plan.</summary>
    public static ApplyResult? Replay(string gameDir, string planPath, HashCache? cache, string? cachePath)
    {
        PlanFile? pf;
        try { pf = File.Exists(planPath) ? JsonSerializer.Deserialize<PlanFile>(File.ReadAllText(planPath)) : null; }
        catch { pf = null; }
        if (pf is null || !string.Equals(Path.GetFullPath(pf.GameDir).TrimEnd('\\'), Path.GetFullPath(gameDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return null;
        if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 is running. Close it, then click Finish update.");
        return Execute(gameDir, pf.Actions, planPath, cache, cachePath, replay: true);
    }

    static ApplyResult Execute(string gameDir, List<PlanFileAction> actions, string planPath, HashCache? cache, string? cachePath, bool replay)
    {
        var r = new ApplyResult();
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        bool abort = false;

        void Try(PlanFileAction a, Action work)
        {
            try { work(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { r.Failed.Add(Path.GetFileName(a.From.Length > 0 ? a.From : a.To) + " (" + ex.Message + ")"); }
        }

        // Parks, stages, copies, creates — in plan order.
        foreach (var a in actions.Where(a => a.Op != nameof(PlanOp.Finalize)))
        {
            if (abort) break;
            switch (a.Op)
            {
                case nameof(PlanOp.Park):
                    if (!File.Exists(a.From)) break;                                   // done already
                    Try(a, () => { Directory.CreateDirectory(Path.GetDirectoryName(a.To)!); File.Move(a.From, FreeName(a.To)); r.Parked++; });
                    break;
                case nameof(PlanOp.Stage):
                    if (!File.Exists(a.From)) { if (File.Exists(a.To)) break; abort = true; r.Reason = $"missing {Path.GetFileName(a.From)}"; break; }
                    if (File.Exists(a.To)) { if (replay) { abort = true; r.Reason = $"both {Path.GetFileName(a.From)} and its staged copy exist"; break; } Try(a, () => File.Delete(a.To)); }
                    Try(a, () => { File.Move(a.From, a.To); r.Renamed++; });
                    break;
                case nameof(PlanOp.Copy):
                    if (File.Exists(a.To) && new FileInfo(a.To).Length == a.Size) break;
                    if (!File.Exists(a.From)) { abort = true; r.Reason = $"missing {Path.GetFileName(a.From)}"; break; }
                    Try(a, () => { File.Copy(a.From, a.To, overwrite: true); r.Copied++; });
                    break;
                case nameof(PlanOp.Create):
                    if (File.Exists(a.To)) break;
                    Try(a, () => { File.Create(a.To).Dispose(); r.Created++; });
                    break;
            }
        }
        if (abort) return r;
        if (Game.IsRunning()) { r.Reason = "Helldivers 2 started during the update"; return r; }

        // Finalizes: a still-occupied target (a locked old file that failed to move) is skipped, never overwritten.
        foreach (var a in actions.Where(a => a.Op == nameof(PlanOp.Finalize)))
        {
            if (!File.Exists(a.From)) { if (File.Exists(a.To)) continue; r.Failed.Add(Path.GetFileName(a.To) + " (staged copy missing)"); continue; }
            if (File.Exists(a.To)) { r.Failed.Add(Path.GetFileName(a.To) + " (an old file with that name could not be moved)"); continue; }
            Try(a, () => File.Move(a.From, a.To));
        }
        if (r.Interrupted) return r;

        // Success: remember the hashes under the final names, forget the plan, tidy the download folder.
        if (cache is not null && cachePath is not null)
        {
            foreach (var a in actions.Where(a => a.Op == nameof(PlanOp.Finalize) && a.Sha.Length == 64))
            {
                try { var fi = new FileInfo(a.To); if (fi.Exists) cache.Entries[HashCache.Key(fi.Name, fi.Length, fi.LastWriteTimeUtc)] = a.Sha; } catch { }
            }
            cache.Save(cachePath);
        }
        try { File.Delete(planPath); } catch { }
        var dl = Path.Combine(gameDir, DownloadFolder);
        try { if (Directory.Exists(dl) && !Directory.EnumerateFileSystemEntries(dl).Any()) Directory.Delete(dl); } catch { }
        return r;
    }

    /// <summary>mods_old\X, then X.1, X.2 … Nothing in mods_old\ is ever overwritten. ".N" never matches the patch pattern.</summary>
    static string FreeName(string target)
    {
        if (!File.Exists(target)) return target;
        for (int i = 1; ; i++) { var t = $"{target}.{i}"; if (!File.Exists(t)) return t; }
    }

    static void ClearReadOnly(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, FileAttributes.Normal);
    }

    // ---------------------------------------------------------------- hashing

    /// <summary>Lower-case SHA-256 of a file. Opened with FileShare.Delete so a rename by someone else never fails because we are reading.</summary>
    public static async Task<string> Sha256Async(string path, CancellationToken ct) => await Sha256Async(path, null, ct);

    public static async Task<string> Sha256Async(string path, IProgress<long>? bytes, CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        long done = 0; int n;
        while ((n = await fs.ReadAsync(buffer, ct)) > 0) { sha.AppendData(buffer, 0, n); done += n; bytes?.Report(done); }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    public static string FormatBytes(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" :
        b >= 1L << 20 ? $"{b / (double)(1L << 20):0} MB" :
        b >= 1L << 10 ? $"{b / (double)(1L << 10):0} KB" : $"{b} B";
}

/// <summary>Progress that reports on the caller's thread (System.Progress posts to a sync context; inside Task.Run there is none).</summary>
public sealed class SyncProgress<T> : IProgress<T>
{
    readonly Action<T> handler;
    public SyncProgress(Action<T> handler) => this.handler = handler;
    public void Report(T value) => handler(value);
}

/// <summary>Steam's appmanifest_553850.acf next to the game: which build is installed and whether an update is queued.</summary>
public static class SteamAcf
{
    public sealed record Info(string? BuildId, string? TargetBuildId, string? StateFlags)
    {
        public bool UpdatePending => TargetBuildId is not null && TargetBuildId != "0" && BuildId is not null && TargetBuildId != BuildId;
    }

    static readonly Regex BuildRx = new(@"^\s*""buildid""\s+""(\d+)""", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    static readonly Regex TargetRx = new(@"^\s*""TargetBuildID""\s+""(\d+)""", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    static readonly Regex StateRx = new(@"^\s*""StateFlags""\s+""(\d+)""", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static string AcfPath(string gameDir) => Path.GetFullPath(Path.Combine(gameDir, "..", "..", "appmanifest_" + GameLocator.SteamAppId + ".acf"));

    public static Info Parse(string text) => new(
        BuildRx.Match(text) is { Success: true } b ? b.Groups[1].Value : null,
        TargetRx.Match(text) is { Success: true } t ? t.Groups[1].Value : null,
        StateRx.Match(text) is { Success: true } s ? s.Groups[1].Value : null);

    static string? cachedPath; static DateTime cachedWrite; static Info? cached;

    /// <summary>Reads the acf, re-parsing only when its timestamp changed (this runs on the 1.5 s refresh tick).</summary>
    public static Info? Read(string? gameDir)
    {
        if (gameDir is null) return null;
        try
        {
            var path = AcfPath(gameDir);
            if (!File.Exists(path)) return null;
            var write = File.GetLastWriteTimeUtc(path);
            if (cached is not null && path == cachedPath && write == cachedWrite) return cached;
            cached = Parse(File.ReadAllText(path)); cachedPath = path; cachedWrite = write;
            return cached;
        }
        catch { return null; }
    }
}

/// <summary>Replacing the running exe with a downloaded one. Windows lets a running exe be renamed, so it is two moves
/// done while the app is still alive and can show errors: exe → .old.exe, .update.exe → exe, then start the new one and exit.</summary>
public static class SelfUpdate
{
    public static (string exe, string update, string old) Paths(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath)!;
        var stem = Path.GetFileNameWithoutExtension(exePath);
        return (exePath, Path.Combine(dir, stem + ".update.exe"), Path.Combine(dir, stem + ".old.exe"));
    }

    /// <summary>Throws if the folder will not let us write the update file (Program Files, controlled folders, read-only media).</summary>
    public static void ProbeWritable(string updatePath)
    {
        if (File.Exists(updatePath)) { using var _ = new FileStream(updatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        else { File.Create(updatePath).Dispose(); File.Delete(updatePath); }
    }

    /// <summary>The swap, retried for <paramref name="retryFor"/> per move (OneDrive and Defender hold fresh files for a few seconds).
    /// If the second move fails the first is rolled back so the app keeps working.</summary>
    public static void Swap(string exe, string update, string old, TimeSpan retryFor)
    {
        Retry(() => { if (File.Exists(old)) File.Delete(old); }, retryFor);
        Retry(() => File.Move(exe, old), retryFor);
        try { Retry(() => File.Move(update, exe), retryFor); }
        catch
        {
            try { Retry(() => File.Move(old, exe), retryFor); } catch { /* the original is still there under .old.exe */ }
            throw;
        }
    }

    static void Retry(Action work, TimeSpan retryFor)
    {
        var until = DateTime.UtcNow + retryFor;
        while (true)
        {
            try { work(); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= until) throw;
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>Best-effort removal of the previous exe left behind by the last swap; retried in the background because Defender may still be scanning it.</summary>
    public static void CleanupOld(string exePath)
    {
        var (_, _, old) = Paths(exePath);
        if (!File.Exists(old)) return;
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                try { File.Delete(old); return; } catch { await Task.Delay(500); }
            }
        });
    }
}

/// <summary>Republic navy, clone-armor white, 501st blue, 212th orange. No green, no red, no gradients.
/// One static class so the custom controls below and MainForm share it (imported with `using static`).</summary>
internal static class Palette
{
    public static readonly Color Bg = Color.FromArgb(11, 16, 32);           // window, DWM caption, LAUNCH text
    public static readonly Color TextMain = Color.FromArgb(240, 243, 250);
    public static readonly Color TextDim = Color.FromArgb(150, 162, 190);   // hints, inert text
    public static readonly Color TextMute = Color.FromArgb(95, 105, 130);   // footer
    public static readonly Color Blue = Color.FromArgb(31, 95, 204);        // ON, progress fill
    public static readonly Color BlueHot = Color.FromArgb(52, 118, 230);
    public static readonly Color Slate = Color.FromArgb(62, 68, 82);        // OFF, secondary buttons, ghost strokes
    public static readonly Color SlateHot = Color.FromArgb(80, 87, 104);
    public static readonly Color Disabled = Color.FromArgb(30, 36, 52);     // inert fill, bar track, tooltip background
    public static readonly Color Orange = Color.FromArgb(226, 118, 28);     // LAUNCH
    public static readonly Color OrangeHot = Color.FromArgb(244, 140, 52);
    public static readonly Color Warn = Color.FromArgb(255, 200, 40);       // running warning, "not found"
    public static readonly Color Border = Color.FromArgb(38, 48, 77);       // DWM border, tooltip border
}

internal static class AppInfo
{
    // AssemblyVersion is derived from <Version> in the csproj. Application.ProductVersion would carry "+<git sha>".
    public static readonly string Version =
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";
}

static class Dwm
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    const int DarkModeOld = 19, DarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;

    /// <summary>Dark caption on Windows 10 1809+; exact caption/text/border colours on Windows 11. HRESULTs are ignored; nothing throws.</summary>
    public static void ApplyDark(IntPtr hwnd, Color caption, Color text, Color border)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, DarkMode, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, DarkModeOld, ref on, 4);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;   // 34/35/36 return E_INVALIDARG on Win10
        int c = ColorTranslator.ToWin32(caption); DwmSetWindowAttribute(hwnd, CaptionColor, ref c, 4);   // COLORREF 0x00BBGGRR
        int t = ColorTranslator.ToWin32(text);    DwmSetWindowAttribute(hwnd, TextColor, ref t, 4);
        int b = ColorTranslator.ToWin32(border);  DwmSetWindowAttribute(hwnd, BorderColor, ref b, 4);
    }
}

static class Power
{
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    /// <summary>Keeps the PC from sleeping through a 9 GB download. Per-thread, so call it from the UI thread both ways.</summary>
    public static void KeepAwake(bool on) => SetThreadExecutionState(on ? 0x80000001u /* ES_CONTINUOUS|ES_SYSTEM_REQUIRED */ : 0x80000000u);
}

/// <summary>Flat Button with anti-aliased rounded corners and hover / press / keyboard-focus states, plus an optional
/// smaller second line (pack buttons), status dot (the toggle) and ghost outline look. It reads the same BackColor /
/// ForeColor / FlatAppearance that Style(), SetArmed() and SetToggle() already set, so MainForm's state code is unchanged.</summary>
sealed class RoundButton : Button
{
    public int Radius { get; set; } = 8;                 // logical px
    public bool Ghost { get; set; }                      // outline only; BackColor is the stroke colour
    // Change-checked so a refresh that alters only the dot or the sub-line repaints (Text/BackColor setters are no-ops when
    // unchanged, so nothing else would), while the 1.5-s timer re-setting the same values costs no paint.
    string subText = ""; Color dotColor = Color.Empty; bool dotFilled = true;
    public string SubText { get => subText; set { value ??= ""; if (subText != value) { subText = value; Invalidate(); } } }   // smaller second line
    public Color DotColor { get => dotColor; set { if (dotColor != value) { dotColor = value; Invalidate(); } } }             // Color.Empty = no dot
    public bool DotFilled { get => dotFilled; set { if (dotFilled != value) { dotFilled = value; Invalidate(); } } }
    bool hover, down; Font? subFont;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnFontChanged(EventArgs e) { subFont?.Dispose(); subFont = null; base.OnFontChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { }   // OnPaint covers every pixel (no square-corner flash)
    Font Sub => subFont ??= new Font(Font.FontFamily, Font.Size * 0.8f, FontStyle.Regular);

    // The base OnPaint is deliberately not called: it would add the dotted focus rectangle and the mnemonic underline.
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;                        // TLP hands out 0-size cells mid-layout
        var outside = Parent?.BackColor ?? BackColor;
        using (var b = new SolidBrush(outside)) g.FillRectangle(b, r);     // corners show the parent's navy
        g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float rad = LogicalToDeviceUnits(Radius);
        var rf = new RectangleF(0.5f, 0.5f, r.Width - 1, r.Height - 1);
        Color fill, fore;
        if (!Enabled) { fill = Disabled; fore = TextDim; }                // we own the disabled look now
        else
        {
            fore = ForeColor;
            fill = Ghost ? (hover ? Color.FromArgb(48, BackColor) : outside)
                 : down  ? Blend(BackColor, Color.Black, 0.12f)
                 : hover ? FlatAppearance.MouseOverBackColor : BackColor;
        }
        using (var p = Rounded(rf, rad))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            if (Ghost) { using var pen = new Pen(Enabled ? BackColor : Disabled, LogicalToDeviceUnits(1)); g.DrawPath(pen, p); }
        }
        if (Focused && ShowFocusCues)                                      // keyboard focus only; mouse clicks show no ring
        {
            var fr = rf; fr.Inflate(-LogicalToDeviceUnits(4), -LogicalToDeviceUnits(4));
            using var fp = Rounded(fr, Math.Max(2, rad - LogicalToDeviceUnits(3)));
            using var pen = new Pen(Color.FromArgb(120, 255, 255, 255), LogicalToDeviceUnits(1));
            g.DrawPath(pen, fp);
        }

        var tr = r; if (down && Enabled) tr.Offset(0, LogicalToDeviceUnits(1));
        if (!string.IsNullOrEmpty(SubText))
        {
            const TextFormatFlags Line = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int h1 = TextRenderer.MeasureText(g, "Xg", Font, Size.Empty, TextFormatFlags.NoPadding).Height;
            int h2 = TextRenderer.MeasureText(g, "Xg", Sub, Size.Empty, TextFormatFlags.NoPadding).Height;
            int top = tr.Top + (tr.Height - h1 - h2) / 2;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tr.Left, top, tr.Width, h1), fore, Line);
            TextRenderer.DrawText(g, SubText, Sub, new Rectangle(tr.Left, top + h1, tr.Width, h2), Blend(fore, fill, 0.25f), Line);  // opaque: GDI text ignores alpha
        }
        else if (DotColor == Color.Empty || !DrawDotAndText(g, tr, fore))
            TextRenderer.DrawText(g, Text, Font, tr, fore,   // TextRenderer centres wrapped text vertically on its own
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>Status dot + label as one centred lockup. False when it would not fit, so the caller draws plain text instead.</summary>
    bool DrawDotAndText(Graphics g, Rectangle tr, Color fore)
    {
        const TextFormatFlags Line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int d = LogicalToDeviceUnits(12), gap = LogicalToDeviceUnits(14);
        int tw = TextRenderer.MeasureText(g, Text, Font, Size.Empty, Line).Width;
        int total = d + gap + tw;
        if (total > tr.Width - LogicalToDeviceUnits(16)) return false;
        int x = tr.Left + (tr.Width - total) / 2, cy = tr.Top + tr.Height / 2;
        var dot = new Rectangle(x, cy - d / 2, d, d);
        if (DotFilled)
        {
            var halo = dot; halo.Inflate(LogicalToDeviceUnits(4), LogicalToDeviceUnits(4));
            using (var hb = new SolidBrush(Color.FromArgb(70, DotColor))) g.FillEllipse(hb, halo);
            using var db = new SolidBrush(DotColor); g.FillEllipse(db, dot);
        }
        else
        {
            float w = LogicalToDeviceUnits(2);
            using var pen = new Pen(DotColor, w);
            g.DrawEllipse(pen, dot.X + w / 2, dot.Y + w / 2, dot.Width - w, dot.Height - w);
        }
        TextRenderer.DrawText(g, Text, Font, Rectangle.FromLTRB(x + d + gap, tr.Top, tr.Right, tr.Bottom), fore, Line);
        return true;
    }

    protected override void Dispose(bool disposing) { if (disposing) subFont?.Dispose(); base.Dispose(disposing); }

    internal static Color Blend(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    internal static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        if (d <= 0 || d > Math.Min(r.Width, r.Height)) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}

sealed class Grid : TableLayoutPanel { public Grid() { DoubleBuffered = true; } }   // DoubleBuffered is protected on TLP

/// <summary>Single-line label that ellipsises the middle of a path (Label.AutoEllipsis only cuts the end).</summary>
sealed class PathLabel : Label
{
    protected override void OnPaint(PaintEventArgs e) =>
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
}

/// <summary>6-px rounded progress bar. Idle it paints nothing but keeps its row, so the window never jumps when a download starts.</summary>
sealed class ThinBar : Control
{
    double fraction; bool marquee, active; float mx; int lastW = -1;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 33 };
    public Color Track { get; set; } = Disabled;
    public Color Fill { get; set; } = Blue;
    // Active / Marquee are set on every progress report; only a real change invalidates, so the lastW guard in Fraction does its job.
    public bool Active { get => active; set { if (active == value) return; active = value; if (!value) Marquee = false; Invalidate(); } }
    public double Fraction { get => fraction; set { fraction = Math.Clamp(value, 0, 1); int w = (int)(Width * fraction); if (w != lastW) { lastW = w; Invalidate(); } } }
    public bool Marquee
    {
        get => marquee;
        set
        {
            if (marquee == value) { if (value && Visible && !anim.Enabled) anim.Start(); return; }
            marquee = value; if (value && Visible) anim.Start(); else anim.Stop(); Invalidate();
        }
    }
    public ThinBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        anim.Tick += (_, _) => { mx = (mx + 0.02f) % 1.25f; Invalidate(); };
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) anim.Stop(); else if (marquee) anim.Start(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var r = ClientRectangle; if (r.Width <= 0 || r.Height <= 0) return;
        using (var b = new SolidBrush(Parent?.BackColor ?? BackColor)) g.FillRectangle(b, r);
        if (!active) return;                                             // idle: invisible but the row keeps its height
        g.SmoothingMode = SmoothingMode.AntiAlias; float rad = r.Height / 2f;
        var rf = new RectangleF(0, 0, r.Width, r.Height);
        using (var p = RoundButton.Rounded(rf, rad)) using (var tb = new SolidBrush(Track)) g.FillPath(tb, p);
        RectangleF f = marquee
            ? RectangleF.Intersect(rf, new RectangleF((mx - 0.25f) * r.Width, 0, r.Width / 4f, r.Height))
            : new RectangleF(0, 0, (float)Math.Max(r.Width * fraction, fraction > 0 ? r.Height : 0), r.Height);
        if (f.Width > 0) using (var p = RoundButton.Rounded(f, rad)) using (var fb = new SolidBrush(Fill)) g.FillPath(fb, p);
    }
    protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
}

/// <summary>The clone helmet from tools\make-icon.ps1, vector-drawn straight onto the navy (no tile) so it is crisp at any DPI.</summary>
sealed class HelmetMark : Control
{
    public HelmetMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var r = ClientRectangle; if (r.Width <= 0 || r.Height <= 0) return;
        using (var b = new SolidBrush(Parent?.BackColor ?? BackColor)) g.FillRectangle(b, r);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Same 256-unit design space as the icon. The helmet itself spans x 50..206, y 34..210; fit that box, not the tile.
        float k = Math.Min(r.Width / 156f, r.Height / 176f);
        g.TranslateTransform((r.Width - 156 * k) / 2 - 50 * k, (r.Height - 176 * k) / 2 - 34 * k);
        g.ScaleTransform(k, k);

        using (var helm = new GraphicsPath())
        {
            helm.AddArc(50, 34, 156, 150, 180, 180);     // dome
            helm.AddLine(206, 109, 206, 176);            // right cheek
            helm.AddArc(172, 176, 34, 34, 0, 90);        // jaw right
            helm.AddLine(189, 210, 67, 210);             // chin
            helm.AddArc(50, 176, 34, 34, 90, 90);        // jaw left
            helm.CloseFigure();
            using var shell = new SolidBrush(TextMain);
            g.FillPath(shell, helm);
        }
        var visor = Color.FromArgb(14, 17, 26); var vent = Color.FromArgb(150, 160, 185);
        Rounded(g, 118, 34, 20, 46, 6, Blue);        // 501st fin down the crown
        Rounded(g, 72, 104, 112, 24, 10, visor);     // T-visor
        Rounded(g, 116, 122, 24, 60, 8, visor);
        Rounded(g, 70, 166, 30, 20, 6, vent);        // breather vents
        Rounded(g, 156, 166, 30, 20, 6, vent);
        Rounded(g, 106, 190, 44, 6, 3, visor);       // mouth grille
    }
    static void Rounded(Graphics g, float x, float y, float w, float h, float rad, Color c)
    {
        using var p = RoundButton.Rounded(new RectangleF(x, y, w, h), rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }
}

public sealed class MainForm : Form
{
    readonly Settings settings;
    string? gameDir;
    DateTime? launchRequested;
    readonly string? updatedTo;   // "--updated 1.3.1": the new exe announcing itself after a self-update

    // Manifest / pack state
    Manifest? manifest;
    string? manifestError;
    bool loadingManifest;
    DateTime lastManifestTry;
    readonly HashCache hashCache = HashCache.Load(Settings.HashCachePath);

    enum BusyKind { None, Inventory, Download, Apply, AppUpdate }
    BusyKind busy;
    bool Busy => busy != BusyKind.None;
    CancellationTokenSource? opCts;
    bool installArmed, downloadArmed, launchArmed = true, offerArmed;
    bool exitingForUpdate;   // the new exe has been started; this instance must close without asking anything
    DateTime lastProgressMessage = DateTime.MinValue;
    readonly Stopwatch rateWatch = new();
    long rateBytes;
    string rateText = "", lastWhat = "";

    readonly Grid root = new();
    readonly RoundButton toggleButton = new();
    readonly RoundButton launchButton = new();
    readonly RoundButton pathButton = new();
    readonly RoundButton openButton = new();
    readonly RoundButton installFileButton = new();
    readonly RoundButton downloadButton = new();
    readonly Grid optionsRow = new();
    readonly List<RoundButton> optionButtons = new();
    readonly ThinBar progress = new();
    readonly Label progressLabel = new();
    readonly Label statusLabel = new();
    readonly Label hintLabel = new();
    readonly Label pathCaption = new();
    readonly PathLabel pathValue = new();
    readonly Label footer = new();
    readonly ToolTip tips = new();
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1500 };

    public MainForm(Settings settings, string? gameDir, string? updatedTo = null)
    {
        this.settings = settings;
        this.gameDir = gameDir;
        this.updatedTo = updatedTo;

        Text = "Clonedivers";
        BackColor = Bg;
        ForeColor = TextMain;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 600);
        MinimumSize = Size;   // never smaller than designed, or the toggle row would collapse to nothing
        MaximizeBox = false;  // a maximised toggle on an ultrawide would be 4000 px across
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { /* no icon resource: fine */ }

        BuildTooltips();
        BuildLayout();

        // Scale every pixel value above for the monitor's DPI (100–150%+); fonts scale on their own.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        refresh.Tick += (_, _) =>
        {
            // Offline at start? Ask GitHub again once a minute until manifest.json answers.
            if (manifest is null && manifestError is not null && !loadingManifest && DateTime.UtcNow - lastManifestTry > TimeSpan.FromSeconds(60))
                _ = LoadManifestAsync();
            RefreshState();
        };
        Activated += (_, _) => RefreshState();
        Shown += (_, _) =>
        {
            if (updatedTo is not null) { ShowProgressDone($"Updated to {updatedTo}."); Activate(); }
            RefreshState();
            refresh.Start();
            _ = LoadManifestAsync();                 // find out whether a pack is published (non-blocking)
            if (this.gameDir is null) OnLocate();   // first-run "pathing wizard": auto-detect failed, so ask.
        };
    }

    // Runs before the first paint (no white caption flash) and again on every handle recreation.
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dwm.ApplyDark(Handle, Bg, TextMain, Border); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Invalidate(true); }

    void BuildTooltips()
    {
        tips.InitialDelay = 350; tips.AutoPopDelay = 30000; tips.ReshowDelay = 100;
        // Dark tooltip; the system one is a white box on our navy window.
        tips.OwnerDraw = true; tips.BackColor = Disabled; tips.ForeColor = TextMain;
        // Measure with word-wrap at a 460-px cap: WinForms lets a native tip grow to the screen width, so the pack notes
        // (~380 characters) would otherwise run off a 1920-px monitor as one line. Draw wraps with the same flags.
        tips.Popup += (_, e) =>
        {
            using var f = SystemFonts.StatusFont;   // the tooltip window's own font (Segoe UI 9); each call returns a new Font
            var s = TextRenderer.MeasureText(tips.GetToolTip(e.AssociatedControl), f, new Size(LogicalToDeviceUnits(460), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(s.Width + LogicalToDeviceUnits(10), s.Height + LogicalToDeviceUnits(6));
        };
        tips.Draw += (_, e) =>
        {
            e.Graphics.Clear(Disabled);
            using var pen = new Pen(Border); e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, Rectangle.Inflate(e.Bounds, -LogicalToDeviceUnits(5), -LogicalToDeviceUnits(3)), TextMain,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        };
    }

    void BuildLayout()
    {
        root.Dock = DockStyle.Fill;
        root.ColumnCount = 1;
        root.BackColor = Bg;
        root.Padding = new Padding(24, 20, 24, 16);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Header lockup: helmet mark + wordmark, centred as one block (AutoSize grid anchored None in a full-width cell).
        var header = new Grid { AutoSize = true, ColumnCount = 2, RowCount = 2, Anchor = AnchorStyles.None, BackColor = Bg, Margin = new Padding(0, 0, 0, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var mark = new HelmetMark { Width = 44, Height = 44, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 14, 0) };
        var title = new Label
        {
            Text = "CLONEDIVERS",
            Font = new Font("Segoe UI", 26F, FontStyle.Bold),
            ForeColor = TextMain,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        };
        var subtitle = new Label
        {
            Text = "Helldivers 2  ·  Clone Wars mod switch",
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            ForeColor = TextDim,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2, 0, 0, 0),
        };
        header.Controls.Add(mark, 0, 0);
        header.SetRowSpan(mark, 2);
        header.Controls.Add(title, 1, 0);
        header.Controls.Add(subtitle, 1, 1);

        Style(toggleButton, Blue, BlueHot, TextMain, 24F);
        toggleButton.Radius = 12;
        toggleButton.Dock = DockStyle.Fill;
        toggleButton.Margin = new Padding(0, 8, 0, 12);
        toggleButton.Click += (_, _) => OnToggle();

        // Under the switch: one bright status line, one dim hint. The hint reserves three lines so ON / OFF / running never move the toggle.
        statusLabel.AutoSize = true;
        statusLabel.UseMnemonic = false;   // paths with '&' must not turn into underlines
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = TextMain;
        statusLabel.Font = new Font("Segoe UI", 10F);   // 10.5 wraps the running warning at 125% / 175%, which would shrink the toggle only while the game runs
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.Margin = new Padding(0, 0, 0, 2);
        hintLabel.AutoSize = true;
        hintLabel.UseMnemonic = false;
        hintLabel.Dock = DockStyle.Fill;
        hintLabel.ForeColor = TextDim;
        hintLabel.Font = new Font("Segoe UI", 9F);
        hintLabel.TextAlign = ContentAlignment.MiddleCenter;
        hintLabel.Margin = new Padding(0, 0, 0, 12);
        hintLabel.MinimumSize = new Size(0, 56);

        Style(launchButton, Orange, OrangeHot, Bg, 13F);   // navy on orange reads at 6.1:1; the old white was 2.8:1
        launchButton.Radius = 10;
        launchButton.Text = "LAUNCH HELLDIVERS 2";
        launchButton.Dock = DockStyle.Fill;
        launchButton.Margin = new Padding(0, 0, 0, 12);
        launchButton.Click += (_, _) => OnLaunch();

        // Pack row: ghost "Install pack from file…"  |  filled "Download pack" with "version · size" under it
        var packRow = new Grid { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0), BackColor = Bg };
        packRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        packRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Style(installFileButton, Slate, SlateHot, TextMain, 10F);
        installFileButton.Radius = 8;
        installFileButton.Ghost = true;
        installFileButton.Text = "Install pack from file…";
        installFileButton.Dock = DockStyle.Fill;
        installFileButton.MinimumSize = new Size(0, 50);
        installFileButton.Margin = new Padding(0, 0, 6, 0);
        installFileButton.Click += (_, _) => OnInstallFromFile();
        Style(downloadButton, Slate, SlateHot, TextMain, 10F);
        downloadButton.Radius = 8;
        downloadButton.Text = "Checking for pack…";
        downloadButton.Dock = DockStyle.Fill;
        downloadButton.MinimumSize = new Size(0, 50);
        downloadButton.Margin = new Padding(6, 0, 0, 0);
        downloadButton.Click += (_, _) => OnDownloadOrCancel();
        packRow.Controls.Add(installFileButton, 0, 0);
        packRow.Controls.Add(downloadButton, 1, 0);

        // Options row: one ghost toggle per optional group in the manifest (empty and flat until a manifest with options arrives).
        optionsRow.Dock = DockStyle.Fill;
        optionsRow.AutoSize = true;
        optionsRow.Margin = new Padding(0);
        optionsRow.BackColor = Bg;

        // Progress: label above a thin bar. Both stay Visible for life — hiding a child collapses its AutoSize row and the window jumps.
        progressLabel.AutoSize = true;
        progressLabel.UseMnemonic = false;
        progressLabel.Dock = DockStyle.Fill;
        progressLabel.ForeColor = TextDim;
        progressLabel.Font = new Font("Segoe UI", 9F);
        progressLabel.TextAlign = ContentAlignment.MiddleLeft;
        progressLabel.Margin = new Padding(0, 12, 0, 4);
        progressLabel.MinimumSize = new Size(0, 36);   // two 9-pt lines at every scale: the "Pack updated … parked in mods_old\" message wraps, and this row must never move
        progressLabel.Text = "";
        progressLabel.Click += (_, _) => { if (offerArmed) OnAppUpdate(); };
        progress.Height = 6;   // a plain Control has no preferred size; without this the AutoSize row collapses
        progress.MinimumSize = new Size(0, 6);
        progress.Dock = DockStyle.Fill;
        progress.Margin = new Padding(0, 0, 0, 2);

        // Game-folder row: "Found via Steam:  C:\…\Helldivers 2   [Open] [Change…]" — one 30-px line, path middle-ellipsised.
        var pathRow = new Grid { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 6, 0, 0), BackColor = Bg };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathCaption.Text = "Game folder:";
        pathCaption.AutoSize = true;
        pathCaption.ForeColor = TextDim;
        pathCaption.Font = new Font("Segoe UI", 9.5F);
        pathCaption.Anchor = AnchorStyles.Left;
        pathCaption.Margin = new Padding(0, 0, 8, 0);
        pathValue.AutoSize = false;
        pathValue.UseMnemonic = false;
        pathValue.Anchor = AnchorStyles.Left | AnchorStyles.Right;   // stretched across the cell and centred on the buttons; Dock=Fill pins a height-capped label to the top
        pathValue.ForeColor = TextMain;
        pathValue.Font = new Font("Segoe UI", 9.5F);
        pathValue.Margin = new Padding(0);
        pathValue.MinimumSize = new Size(0, 30);
        pathValue.MaximumSize = new Size(0, 30);   // 0 = width unconstrained; the height cap keeps the row at one line
        Style(openButton, Slate, SlateHot, TextMain, 9.5F);
        openButton.Radius = 6;
        openButton.Ghost = true;
        openButton.Text = "Open";
        openButton.AutoSize = true;
        openButton.Padding = new Padding(10, 0, 10, 0);
        openButton.MinimumSize = new Size(0, 30);
        openButton.Anchor = AnchorStyles.Right;
        openButton.Margin = new Padding(8, 0, 0, 0);
        openButton.Click += (_, _) => OpenFolder(Path.Combine(gameDir ?? "", ModFiles.DataFolder));
        Style(pathButton, Slate, SlateHot, TextMain, 9.5F);
        pathButton.Radius = 6;
        pathButton.Ghost = true;
        pathButton.Text = "Change…";
        pathButton.AutoSize = true;
        pathButton.Padding = new Padding(10, 0, 10, 0);
        pathButton.MinimumSize = new Size(0, 30);
        pathButton.Anchor = AnchorStyles.Right;
        pathButton.Margin = new Padding(8, 0, 0, 0);
        pathButton.Click += (_, _) => OnLocate();
        pathRow.Controls.Add(pathCaption, 0, 0);
        pathRow.Controls.Add(pathValue, 1, 0);
        pathRow.Controls.Add(openButton, 2, 0);
        pathRow.Controls.Add(pathButton, 3, 0);

        var testNote = string.IsNullOrWhiteSpace(settings.ManifestUrl) ? "" : $"  ·  test manifest: {HostOf(settings.ManifestUrl)}";
        footer.Text = $"v{AppInfo.Version}{testNote}  ·  moves mod files, launches through Steam, touches nothing else  ·  For the Republic.";
        footer.Font = new Font("Segoe UI", 8.25F, FontStyle.Italic);
        footer.ForeColor = string.IsNullOrWhiteSpace(settings.ManifestUrl) ? TextMute : Warn;
        footer.AutoSize = true;
        footer.Dock = DockStyle.Fill;
        footer.TextAlign = ContentAlignment.MiddleCenter;
        footer.Margin = new Padding(0, 10, 0, 0);

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header lockup
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // toggle (takes all spare height)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // hint
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));  // launch: 50-px button + 12 margin, level with the pack buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // pack buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // options (empty until the manifest has some)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progress text
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progress bar
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // path row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // footer
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(toggleButton, 0, 1);
        root.Controls.Add(statusLabel, 0, 2);
        root.Controls.Add(hintLabel, 0, 3);
        root.Controls.Add(launchButton, 0, 4);
        root.Controls.Add(packRow, 0, 5);
        root.Controls.Add(optionsRow, 0, 6);
        root.Controls.Add(progressLabel, 0, 7);
        root.Controls.Add(progress, 0, 8);
        root.Controls.Add(pathRow, 0, 9);
        root.Controls.Add(footer, 0, 10);
        Controls.Add(root);
    }

    static string HostOf(string url)
    {
        try { return Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.IsFile ? "local file" : u.Host) : url; } catch { return url; }
    }

    /// <summary>One ghost toggle per optional group. Rebuilt when a manifest arrives; the row stays empty otherwise.</summary>
    void RebuildOptions()
    {
        var pack = manifest?.Pack;
        var options = pack?.Options ?? new List<PackOption>();
        if (optionButtons.Count == options.Count && optionButtons.Select(b => (string)b.Tag!).SequenceEqual(options.Select(o => o.Id))) return;
        optionsRow.SuspendLayout();
        optionsRow.Controls.Clear();
        optionsRow.ColumnStyles.Clear();
        foreach (var b in optionButtons) b.Dispose();
        optionButtons.Clear();
        optionsRow.ColumnCount = Math.Max(1, options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / options.Count));
            var b = new RoundButton { Radius = 8, Ghost = true, Dock = DockStyle.Fill, Tag = o.Id, MinimumSize = new Size(0, 36),
                                      Margin = new Padding(i == 0 ? 0 : 6, 8, i == options.Count - 1 ? 0 : 6, 0) };
            Style(b, Slate, SlateHot, TextMain, 9.5F);
            b.Click += (_, _) => OnOptionToggle(o);
            optionsRow.Controls.Add(b, i, 0);
            optionButtons.Add(b);
        }
        optionsRow.ResumeLayout(true);
    }

    bool OptionEnabled(PackOption o) => settings.Options.TryGetValue(o.Id, out var v) ? v : o.Default;
    bool OptionEnabledById(string id)
    {
        if (settings.Options.TryGetValue(id, out var v)) return v;
        return manifest?.Pack?.Options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase))?.Default ?? true;
    }
    List<PackFile> WantedFiles(PackManifest pack) => Pack.EffectiveFiles(pack, OptionEnabledById);

    /// <summary>Secondary buttons that may be inert: styled dim when not armed (a truly disabled flat button is unreadable here).</summary>
    static void SetArmed(Button b, string text, bool armed)
    {
        b.Text = text;
        b.BackColor = armed ? Slate : Disabled;
        b.ForeColor = armed ? TextMain : TextDim;
        b.FlatAppearance.MouseOverBackColor = armed ? SlateHot : Disabled;
        b.FlatAppearance.MouseDownBackColor = armed ? Slate : Disabled;
        b.Cursor = armed ? Cursors.Hand : Cursors.Default;
    }

    /// <summary>Inert colours but still clickable ("Pack up to date", "Can't reach GitHub"): hand cursor and a hover so it reads as a button.</summary>
    static void SetQuiet(Button b, string text) { SetArmed(b, text, armed: false); b.FlatAppearance.MouseOverBackColor = SlateHot; b.Cursor = Cursors.Hand; }

    static void Style(Button b, Color back, Color hot, Color fore, float pt)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hot;
        b.FlatAppearance.MouseDownBackColor = back;
        b.BackColor = back;
        b.ForeColor = fore;
        b.Font = new Font("Segoe UI", pt, FontStyle.Bold);
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
    }

    void Tip(Control c, string text) { if (tips.GetToolTip(c) != text) tips.SetToolTip(c, text); }   // SetToolTip on a showing tip resets it

    // "Disabled" states stay technically clickable: a truly disabled flat button paints its text nearly
    // black on our dark background, and a click that explains what to do beats a dead button anyway.
    bool toggleArmed = true;

    void SetToggle(string text, Color back, Color hot, Color fore, bool enabled)
    {
        toggleButton.Text = text;
        toggleButton.BackColor = back;
        toggleButton.FlatAppearance.MouseOverBackColor = hot;
        toggleButton.FlatAppearance.MouseDownBackColor = back;
        toggleButton.ForeColor = fore;
        toggleButton.Cursor = enabled ? Cursors.Hand : Cursors.Default;
        toggleArmed = enabled;
    }

    /// <summary>Re-derives everything from disk. Cheap, so it runs on a timer and on focus. One layout pass per call.</summary>
    void RefreshState()
    {
        root.SuspendLayout();
        try { ApplyState(); }
        finally { root.ResumeLayout(true); }
    }

    // The three "something changed under the pack" signals, in the order they are shown.
    enum Signal { None, Broken, Pending, BuildChanged }

    Signal CurrentSignal(ModState state, PackManifest? pack, SteamAcf.Info? acf)
    {
        if (pack is null) return Signal.None;
        if (pack.IsBroken) return Signal.Broken;
        if (state != ModState.On || acf is null) return Signal.None;
        if (acf.UpdatePending) return Signal.Pending;
        if (!string.IsNullOrWhiteSpace(pack.GameBuild) && acf.BuildId is not null && acf.BuildId != pack.GameBuild.Trim()) return Signal.BuildChanged;
        return Signal.None;
    }

    static string SignalSentence(Signal s, PackManifest? pack) => s switch
    {
        Signal.Broken => string.IsNullOrWhiteSpace(pack?.StatusNotes) ? "Owen marked this pack broken. Switch OFF until an updated pack ships." : "Owen marked this pack broken: " + pack!.StatusNotes.Trim(),
        Signal.Pending => "Steam has a Helldivers 2 update waiting; it installs when you launch.",
        Signal.BuildChanged => "Helldivers 2 has updated since this pack was built.",
        _ => "",
    };

    void ApplyState()
    {
        var state = ModFiles.GetState(gameDir);
        var running = Game.IsRunning();
        var staged = !Busy && ModFiles.HasStagedFiles(gameDir);
        var pack = manifest?.Pack;
        var acf = SteamAcf.Read(gameDir);
        var dataDir = gameDir is null ? "" : Path.Combine(gameDir, ModFiles.DataFolder);
        var active = gameDir is null ? 0 : ModFiles.ListPatchFiles(dataDir).Length;
        var parked = gameDir is null ? 0 : ModFiles.ListPatchFiles(Path.Combine(gameDir, ModFiles.OffFolder)).Length;
        var usable = pack is not null && pack.IsPublished && pack.IsPerFile;

        string detail;
        var dot = Color.Empty;
        switch (state)
        {
            case ModState.On:
                SetToggle("CLONES: ON", Blue, BlueHot, TextMain, enabled: true);
                dot = running ? Warn : Color.FromArgb(150, 205, 255);
                detail = $"{active:N0} mod file{(active == 1 ? "" : "s")} active in data\\"
                       + (parked > 0 ? $" ({parked:N0} more still parked in mods_off\\)" : "")
                       + ".\nClick to park them in mods_off\\ and play vanilla."
                       + "\nGame crashing after a Helldivers 2 update? Switch OFF until the mods are updated.";
                break;
            case ModState.Off:
                SetToggle("CLONES: OFF", Slate, SlateHot, TextMain, enabled: true);
                dot = running ? Warn : TextDim;
                detail = $"{parked:N0} mod file{(parked == 1 ? "" : "s")} parked in mods_off\\.\nClick to move them back into data\\.";
                break;
            case ModState.NoModFiles:
                SetToggle("NO MOD FILES FOUND", Disabled, Disabled, TextDim, enabled: false);
                detail = usable
                    ? "No mod pack installed.\nClick Download pack below to fetch the Clone Wars pack. Got the zips from Owen instead? Install pack from file… and select all the parts at once."
                    : "No mod files found in data\\ or mods_off\\.\nUnzip the mod you downloaded, then put its files (names ending in .patch_0, .patch_0.gpu_resources, .patch_0.stream)\ndirectly into this folder — not inside a sub-folder:\n" + dataDir;
                break;
            default:
                SetToggle("GAME NOT FOUND", Disabled, Disabled, TextDim, enabled: false);
                detail = "Couldn't find Helldivers 2 through Steam.\nClick \"Locate…\" and pick the game folder (it contains data\\ and bin\\helldivers2.exe).\nNot sure where it is? In Steam: right-click Helldivers 2 → Manage → Browse local files.";
                break;
        }

        // Warnings under the switch, in precedence order: game running > pack broken > Steam update pending > game build changed.
        var warn = running && state is ModState.On or ModState.Off;
        var signal = CurrentSignal(state, pack, acf);
        if (warn) detail = "Helldivers 2 is running — close it before toggling. Mods are only read at startup.\n" + detail;
        else if (signal != Signal.None && !staged)
        {
            var lines = detail.Split('\n');
            var keep = string.Join("\n", lines.Skip(1).Take(2));
            var advice = signal == Signal.Broken ? "" : "If the game crashes or looks wrong, switch OFF until Owen confirms the pack.\n";
            detail = SignalSentence(signal, pack) + "\n" + advice + keep;
            warn = true;
        }
        Tip(toggleButton, acf?.BuildId is null ? "" : $"Game build {acf.BuildId}" + (string.IsNullOrWhiteSpace(pack?.GameBuild) ? "" : $"  ·  pack built for {pack!.GameBuild}") + (acf.UpdatePending ? $"  ·  Steam update to {acf.TargetBuildId} pending" : ""));

        if (staged)
        {
            SetToggle("UPDATE INTERRUPTED", Disabled, Disabled, TextDim, enabled: false);
            detail = "A pack update was interrupted.\nClick Finish update below — it takes a few seconds and needs no internet.\nMods are off until then.";
            warn = false; dot = Color.Empty;
        }
        if (Busy)
        {
            var (t, d) = busy switch
            {
                BusyKind.Inventory => ("CHECKING FILES…", "Checking which installed files can be kept.\nThe first check reads every file once (a few minutes on a hard drive). Nothing moves yet."),
                BusyKind.Download => ("DOWNLOADING PACK…", "Downloading only what changed — leave this window open.\nThe switch reads CLONES: ON when it finishes. Cancel keeps whatever has downloaded so far."),
                BusyKind.Apply => ("FINISHING UPDATE…", "Putting the files in place — a few seconds.\nThis step cannot be cancelled."),
                _ => ("UPDATING CLONEDIVERS…", "Downloading the new Clonedivers — leave this window open.\nIt restarts by itself when done."),
            };
            SetToggle(t, Disabled, Disabled, TextDim, enabled: false);
            detail = d; warn = false; dot = Color.Empty;
        }
        toggleButton.DotColor = dot;
        toggleButton.DotFilled = state == ModState.On;
        int nl = detail.IndexOf('\n');
        statusLabel.Text = nl < 0 ? detail : detail[..nl];
        hintLabel.Text = nl < 0 ? "" : detail[(nl + 1)..];
        statusLabel.ForeColor = warn ? Warn : TextMain;

        // Launch feedback: Steam can take a minute to open before the game process exists.
        if (running) launchRequested = null;
        var starting = !running && launchRequested is DateTime lt && DateTime.UtcNow - lt < TimeSpan.FromSeconds(90);
        launchArmed = !running && !starting && busy != BusyKind.Apply;
        launchButton.Enabled = true;
        launchButton.Text = running ? "HELLDIVERS 2 IS RUNNING" : starting ? "STARTING VIA STEAM…" : "LAUNCH HELLDIVERS 2";
        launchButton.BackColor = launchArmed ? Orange : Disabled;
        launchButton.ForeColor = launchArmed ? Bg : TextDim;
        launchButton.FlatAppearance.MouseOverBackColor = launchArmed ? OrangeHot : Disabled;
        launchButton.FlatAppearance.MouseDownBackColor = launchArmed ? Orange : Disabled;
        launchButton.Cursor = launchArmed || running ? Cursors.Hand : Cursors.Default;
        Tip(launchButton, running ? "Helldivers 2 is already running."
                        : starting ? "Steam is starting the game. If Steam was closed it opens first; give it a minute."
                        : "Starts the game through Steam, the same as pressing Play. If Steam is closed it opens first, then the game.");

        // Game-folder row. Only manual picks are saved to settings.GamePath, so the caption check is exact.
        var manual = gameDir is not null && string.Equals(settings.GamePath, gameDir, StringComparison.OrdinalIgnoreCase);
        pathCaption.Text = gameDir is not null && !manual ? "Found via Steam:" : "Game folder:";
        pathValue.Text = gameDir ?? "not found";
        pathValue.ForeColor = gameDir is null ? Warn : TextMain;
        Tip(pathValue, gameDir ?? "");
        openButton.Visible = gameDir is not null;
        Tip(openButton, "Opens Helldivers 2\\data\\, where the game reads mod files.");
        pathButton.Text = gameDir is null ? "Locate…" : "Change…";
        Tip(pathButton, gameDir is null ? "Pick the Helldivers 2 folder (it contains data\\ and bin\\helldivers2.exe)." : "Pick a different Helldivers 2 folder.");

        // Pack buttons
        var canInstall = gameDir is not null && !running && !Busy;
        installArmed = canInstall && !staged;
        SetArmed(installFileButton, "Install pack from file…", installArmed);
        Tip(installFileButton, staged ? "Finish the interrupted update first." : "Have the pack as zip files from Owen? Pick all the parts at once. Needs the game closed.");
        downloadButton.SubText = "";
        if (Busy)
        {
            downloadArmed = busy != BusyKind.Apply;
            if (busy == BusyKind.Apply) { SetArmed(downloadButton, "Finishing…", false); Tip(downloadButton, "A few seconds; this step cannot be cancelled."); }
            else { SetArmed(downloadButton, "Cancel", true); Tip(downloadButton, "Stops the check or download. Downloaded files are kept and resume next time."); }
        }
        else if (staged)
        {
            downloadArmed = canInstall;
            SetArmed(downloadButton, "Finish update", downloadArmed);
            downloadButton.SubText = "a few seconds";
            Tip(downloadButton, "Puts the files from the interrupted update in place. Needs no internet.");
        }
        else if (manifest is null && manifestError is null)
        {
            downloadArmed = false;
            SetArmed(downloadButton, "Checking for pack…", false);
            Tip(downloadButton, "Reading manifest.json from GitHub.");
        }
        else if (manifest is null)
        {
            downloadArmed = false;
            SetQuiet(downloadButton, manifestError!.StartsWith("manifest.json", StringComparison.OrdinalIgnoreCase) ? "Can't read manifest" : "Can't reach GitHub");
            downloadButton.SubText = "click to retry";
            Tip(downloadButton, manifestError!);
        }
        else if (!usable)
        {
            downloadArmed = false;
            SetArmed(downloadButton, "Pack not published yet", false);
            Tip(downloadButton, pack is null ? "" : "The manifest lists no per-file pack yet.");
        }
        else
        {
            downloadArmed = canInstall;
            var installed = settings.InstalledPackVersion;
            var size = Pack.FormatBytes(WantedFiles(pack!).Sum(f => f.Size));   // what THIS friend would install with their toggles, not every variant in the manifest
            var notes = string.IsNullOrWhiteSpace(pack.Notes) ? pack.Name : pack.Name + "\n" + pack.Notes;
            var anyFiles = active + parked > 0;
            if (!anyFiles)
            {
                SetArmed(downloadButton, "Download pack", downloadArmed);
                downloadButton.SubText = $"{pack.Version}  ·  {size}";
                Tip(downloadButton, notes + $"\n\nAbout {size} the first time; later updates fetch only what changed. Resumes if interrupted.");
            }
            else if (installed == pack.Version)
            {
                SetQuiet(downloadButton, "Pack up to date");
                downloadButton.Cursor = canInstall ? Cursors.Hand : Cursors.Default;
                downloadButton.SubText = $"{pack.Version}  ·  click to verify";
                Tip(downloadButton, $"You have pack {pack.Version}. Click to check every installed file and re-download only what is missing or damaged — usually nothing. The first check reads every file once (a few minutes on a hard drive).\n\n{notes}");
            }
            else
            {
                SetArmed(downloadButton, "Update pack", downloadArmed);
                downloadButton.SubText = $"{pack.Version}  ·  downloads only what changed";
                Tip(downloadButton, $"Installed: {installed ?? "unknown — installed from zips"}\nAvailable: {pack.Version}\n\n{notes}\n\nOnly changed files are downloaded; everything else is renamed in place. Resumes if interrupted.");
            }
        }

        // Option toggles
        RebuildOptions();
        foreach (var b in optionButtons)
        {
            var o = pack?.Options.FirstOrDefault(x => string.Equals(x.Id, (string)b.Tag!, StringComparison.OrdinalIgnoreCase));
            if (o is null) continue;
            var on = OptionEnabled(o);
            var armed = canInstall && !staged && usable;
            b.Text = $"{o.Name}: {(on ? "ON" : "OFF")}";
            b.BackColor = armed ? (on ? Blue : Slate) : Disabled;      // ghost: BackColor is the outline colour
            b.ForeColor = armed ? TextMain : TextDim;
            b.Cursor = armed ? Cursors.Hand : Cursors.Default;
            Tip(b, (string.IsNullOrWhiteSpace(o.Description) ? o.Name : o.Description) + (on ? "\n\nClick to switch it off; its files are parked in mods_old\\ and the rest is renumbered (no download)." : "\n\nClick to switch it on; only its files are downloaded."));
        }

        // Self-update offer lives in the idle progress row.
        var app = manifest?.App;
        var offer = !Busy && !staged && app is not null && app.IsNewerThan(AppInfo.Version) && (app.IsPinned || !string.IsNullOrWhiteSpace(settings.ManifestUrl))
                    && DateTime.UtcNow - lastProgressMessage > TimeSpan.FromSeconds(5);
        if (offer)
        {
            offerArmed = true;
            progressLabel.Text = $"Clonedivers {app!.Version} is available  ·  click to update";
            progressLabel.ForeColor = OrangeHot;
            progressLabel.Cursor = Cursors.Hand;
            Tip(progressLabel, $"Downloads Clonedivers {app.Version} ({Pack.FormatBytes(app.Size)}) and restarts by itself. Your game folder, pack and settings carry over.");
        }
        else if (offerArmed)
        {
            offerArmed = false;
            if (progressLabel.ForeColor == OrangeHot) { progressLabel.Text = ""; progressLabel.ForeColor = TextDim; }
            progressLabel.Cursor = Cursors.Default;
            Tip(progressLabel, "");
        }
    }

    async Task LoadManifestAsync()
    {
        if (loadingManifest) return;
        loadingManifest = true; lastManifestTry = DateTime.UtcNow;
        try
        {
            manifest = await Pack.FetchManifestAsync(settings.ManifestUrl, CancellationToken.None);
            manifestError = null;
        }
        catch (Exception ex) { manifest = null; manifestError = ex.Message; }
        finally { loadingManifest = false; }
        if (!IsDisposed) RefreshState();
    }

    void ShowRunningWarning() => MessageBox.Show(this,
        "Helldivers 2 is running.\n\nClose the game first. The engine only reads mod files at startup, " +
        "and moving them out from under a running game is asking for trouble.\n\n" +
        "No game window open? It may have crashed and left helldivers2.exe behind — end it in Task Manager (Ctrl+Shift+Esc).",
        "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    void OnInstallFromFile()
    {
        if (!installArmed || gameDir is null) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        using var dlg = new OpenFileDialog
        {
            Title = "Pick the Clonedivers pack zip (select all parts if there are several)",
            Filter = "Pack zip files (*.zip)|*.zip|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.FileNames.Length == 0) return;
        _ = InstallFromFilesAsync(dlg.FileNames);
    }

    void OnDownloadOrCancel()
    {
        if (Busy) { if (busy != BusyKind.Apply) opCts?.Cancel(); return; }
        if (gameDir is null) return;
        if (ModFiles.HasStagedFiles(gameDir))
        {
            if (!downloadArmed) return;
            if (Game.IsRunning()) { ShowRunningWarning(); return; }
            _ = FinishUpdateAsync();
            return;
        }
        if (manifest is null)
        {
            // "Can't reach GitHub · click to retry"
            if (manifestError is not null && !loadingManifest) { manifestError = null; _ = LoadManifestAsync(); RefreshState(); }
            return;
        }
        var pack = manifest.Pack;
        if (!downloadArmed || pack is null || !pack.IsPublished || !pack.IsPerFile) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        var verify = settings.InstalledPackVersion == pack.Version;
        _ = RunPackAsync(pack, verify, optionChange: null);
    }

    void OnOptionToggle(PackOption o)
    {
        var pack = manifest?.Pack;
        if (Busy || gameDir is null || pack is null || !pack.IsPublished || !pack.IsPerFile || ModFiles.HasStagedFiles(gameDir)) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        var turnOn = !OptionEnabled(o);
        settings.Options[o.Id] = turnOn;
        settings.Save();
        RefreshState();
        _ = RunPackAsync(pack, verify: false, optionChange: (o, turnOn));
    }

    bool ConfirmInstall(string headline)
    {
        var text = headline + "\n\nMod files already in data\\ that are not part of the pack are moved to mods_old\\ " +
                   "(delete that folder later to free space). Nothing is deleted.";
        return MessageBox.Show(this, text, "Clonedivers", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    async Task InstallFromFilesAsync(IReadOnlyList<string> zips)
    {
        var names = string.Join(", ", zips.Select(Path.GetFileName));
        if (!ConfirmInstall($"Install {names} into data\\?")) return;
        SetBusy(BusyKind.Download);
        var cts = opCts = new CancellationTokenSource();
        try
        {
            // Patch bytes per zip come from the zip directories (no decompression), so the bar can span all parts instead of restarting per zip.
            var sizes = await Task.Run(() => zips.Select(Pack.PatchBytes).ToArray(), cts.Token);
            long grand = sizes.Sum();
            var starts = new long[sizes.Length];
            for (int i = 1; i < starts.Length; i++) starts[i] = starts[i - 1] + sizes[i - 1];
            var reporters = zips.Select((_, i) => new Progress<(long done, long total)>(p => ShowProgress("Installing pack", starts[i] + p.done, grand))).ToList();
            var result = await Task.Run(() =>
            {
                if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 started while installing. Close it and try again.");
                return Pack.Install(gameDir!, zips, i => reporters[i], cts.Token);
            }, cts.Token);
            settings.InstalledPackVersion = null;   // a zip install is of unknown version; Update pack will verify and record it
            settings.Save();
            ShowProgressDone($"Pack installed: {result.installed:N0} files in data\\"
                + (result.parked > 0 ? $", {result.parked:N0} old file{(result.parked == 1 ? "" : "s")} parked in mods_old\\" : "")
                + ". Clones are ON — hit LAUNCH.");
        }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. The big button shows what is in data\\ now."); }
        catch (Exception ex) { ShowProgressDone("Install failed."); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(BusyKind.None); }
    }

    /// <summary>The per-file flow: inventory → plan → confirm → download what is missing → apply. <paramref name="verify"/> re-hashes
    /// everything (the "Pack up to date" click); <paramref name="optionChange"/> names the toggle that triggered it, for the dialog.</summary>
    async Task RunPackAsync(PackManifest pack, bool verify, (PackOption option, bool on)? optionChange)
    {
        var dlDir = Path.Combine(gameDir!, Pack.DownloadFolder);
        var wanted = WantedFiles(pack);
        var planPath = Settings.PlanPath;
        SetBusy(BusyKind.Inventory);
        var cts = opCts = new CancellationTokenSource();
        UpdatePlan plan;
        try
        {
            Pack.CleanDownloadFolder(dlDir, wanted.Select(f => f.Sha256));
            var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dlDir))
                foreach (var f in wanted.Where(f => f.Size > 0))
                    if (File.Exists(Path.Combine(dlDir, f.Sha256)) && new FileInfo(Path.Combine(dlDir, f.Sha256)).Length == f.Size) complete.Add(f.Sha256);
            var reporter = new Progress<(long done, long total)>(p => ShowProgress("Checking installed files", p.done, p.total));
            var local = await Task.Run(() => Pack.InventoryAsync(gameDir!, wanted, hashCache, verify, reporter, cts.Token), cts.Token);
            hashCache.Save(Settings.HashCachePath);
            plan = Pack.Plan(gameDir!, wanted, local, complete);
        }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. Nothing was changed."); SetBusy(BusyKind.None); return; }
        catch (Exception ex) { ShowProgressDone("Update failed."); SetBusy(BusyKind.None); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        SetBusy(BusyKind.None);

        if (plan.IsNoOp)
        {
            settings.InstalledPackVersion = pack.Version;
            settings.Save();
            ShowProgressDone(verify ? $"Verified: all {wanted.Count:N0} files match {pack.Version}." : optionChange is { } oc ? $"{oc.option.Name} is now {(oc.on ? "on" : "off")}; nothing to change." : "Already up to date — nothing to download.");
            RefreshState();
            return;
        }

        // Confirm, with the real numbers.
        var fresh = !ModFiles.ListPatchFiles(Path.Combine(gameDir!, ModFiles.DataFolder)).Any() && !ModFiles.ListPatchFiles(Path.Combine(gameDir!, ModFiles.OffFolder)).Any();
        var dlText = plan.BytesToDownload > 0 ? $"Download {Pack.FormatBytes(plan.BytesToDownload)} ({plan.Downloads.Count:N0} file{(plan.Downloads.Count == 1 ? "" : "s")})." : "Nothing to download.";
        string headline;
        if (optionChange is { } o)
            headline = $"Turn {o.option.Name} {(o.on ? "on" : "off")}?\n\n{dlText}" + (o.on ? "" : " The rest is renumbered in place.");
        else if (fresh)
            headline = $"Download the Clone Wars pack {pack.Version}?\n\n{Pack.FormatBytes(plan.BytesToDownload)} ({plan.Downloads.Count:N0} files) — start it before dinner. Leave this window open; it resumes if interrupted.";
        else if (verify)
            headline = $"Repair pack {pack.Version}?\n\n{dlText} Those files are missing or damaged; everything else checks out.";
        else
            headline = $"Update pack to {pack.Version}?\n\n{dlText} Everything else you already have.";
        if (plan.ParkCount > 0 && !fresh) headline += $"\n{plan.ParkCount:N0} old file{(plan.ParkCount == 1 ? "" : "s")} go to mods_old\\ (nothing is deleted).";
        if (!fresh) headline += "\nLeave this window open and keep the game closed.";
        if (!string.IsNullOrWhiteSpace(pack.Notes)) headline += "\n\n" + pack.Notes;
        var drive = Path.GetPathRoot(gameDir!) ?? "";
        long free = 0;
        try { free = new DriveInfo(drive).AvailableFreeSpace; } catch { /* unknown drive type: skip the warning */ }
        var need = plan.BytesToDownload + plan.BytesToCopy + (256L << 20);
        if (free > 0 && free < need) headline += $"\n\nWarning: only {Pack.FormatBytes(free)} free on {drive} — this needs about {Pack.FormatBytes(need)}.";
        if (Form.ActiveForm != this) FlashTaskbar();
        if (MessageBox.Show(this, headline, "Clonedivers", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            if (optionChange is { } undo) { settings.Options[undo.option.Id] = !undo.on; settings.Save(); }
            RefreshState();
            return;
        }

        try
        {
            if (plan.Downloads.Count > 0)
            {
                SetBusy(BusyKind.Download);
                cts = opCts = new CancellationTokenSource();
                var dl = new Progress<(long done, long total, int file, int files)>(p => ShowProgress($"Downloading pack ({p.file} of {p.files})", p.done, p.total));
                await Pack.DownloadPlanAsync(plan, dlDir, dl, cts.Token);
            }
            SetBusy(BusyKind.Apply);
            var result = await Task.Run(() => Pack.Apply(gameDir!, plan, planPath, pack.Version, hashCache, Settings.HashCachePath));
            ReportApply(result, pack.Version, plan.Downloads.Count);
        }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. Downloaded files stay in mods_download\\ and resume next time."); }
        catch (Exception ex) { ShowProgressDone("Update failed."); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(BusyKind.None); }
    }

    void ReportApply(ApplyResult result, string version, int downloaded)
    {
        if (result.Interrupted)
        {
            var names = string.Join(", ", result.Failed.Take(3)) + (result.Failed.Count > 3 ? ", …" : "");
            ShowProgressDone(result.Reason is not null && result.Failed.Count == 0
                ? $"Update interrupted: {result.Reason}. Click Finish update to continue."
                : $"Couldn't move {result.Failed.Count} file(s) (antivirus?): {names}. Close whatever has them open and click Finish update.");
            return;
        }
        settings.InstalledPackVersion = version;
        settings.Save();
        var parts = new List<string>();
        if (downloaded > 0) parts.Add($"{downloaded:N0} downloaded");
        if (result.Renamed > 0) parts.Add($"{result.Renamed:N0} renamed");
        if (result.Copied + result.Created > 0) parts.Add($"{result.Copied + result.Created:N0} created");
        if (result.Parked > 0) parts.Add($"{result.Parked:N0} parked in mods_old\\");
        ShowProgressDone($"Pack updated to {version}" + (parts.Count > 0 ? ": " + string.Join(", ", parts) : "") + ". Clones are ON — hit LAUNCH.");
    }

    /// <summary>Finish update: replay the saved plan (no network, no hashing); fall back to a fresh plan when the file is unusable.</summary>
    async Task FinishUpdateAsync()
    {
        SetBusy(BusyKind.Apply);
        try
        {
            var result = await Task.Run(() => Pack.Replay(gameDir!, Settings.PlanPath, hashCache, Settings.HashCachePath));
            if (result is not null) { ReportApply(result, settings.InstalledPackVersion ?? manifest?.Pack?.Version ?? "", 0); return; }
        }
        catch (Exception ex) { ShowProgressDone("Update failed."); SetBusy(BusyKind.None); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        finally { if (busy == BusyKind.Apply) SetBusy(BusyKind.None); }
        var pack = manifest?.Pack;
        if (pack is null || !pack.IsPublished || !pack.IsPerFile)
        {
            ShowProgressDone("Can't finish offline: the saved plan is unusable and the manifest is not available. Connect and click Finish update again.");
            return;
        }
        await RunPackAsync(pack, verify: false, optionChange: null);
    }

    // ---- self-update

    void OnAppUpdate()
    {
        var app = manifest?.App;
        if (Busy || app is null) return;
        var exePath = Environment.ProcessPath;
        if (exePath is null) return;
        var (exe, update, old) = SelfUpdate.Paths(exePath);
        if (MessageBox.Show(this, $"Update Clonedivers to {app.Version}?\n\nIt downloads {Pack.FormatBytes(app.Size)} and restarts by itself.",
                "Clonedivers", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { SelfUpdate.ProbeWritable(update); }
        catch (Exception ex) { BrowserFallback(Path.GetDirectoryName(exe)!, ex.Message); return; }
        _ = AppUpdateAsync(app, exe, update, old);
    }

    void BrowserFallback(string dir, string reason)
    {
        MessageBox.Show(this, $"Clonedivers can't replace itself in this folder:\n{dir}\n{reason}\n\nThe release page opens in your browser — download Clonedivers.exe there and replace this file.",
            "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        try { Process.Start(new ProcessStartInfo("https://github.com/owendavidgoode/clonedivers/releases/latest") { UseShellExecute = true }); } catch { }
    }

    async Task AppUpdateAsync(AppRelease app, string exe, string update, string old)
    {
        SetBusy(BusyKind.AppUpdate);
        var cts = opCts = new CancellationTokenSource();
        try
        {
            var file = new PackFile { Url = app.Url, Size = app.Size, Sha256 = app.Sha256, Name = "" };
            var reporter = new Progress<(long done, long total)>(p => ShowProgress($"Downloading Clonedivers {app.Version}", p.done, p.total));
            await Pack.DownloadAsync(file, update, reporter, cts.Token);
            // It sat on disk for a moment (OneDrive / Defender may have touched it): check again before trusting it.
            var sha = await Pack.Sha256Async(update, cts.Token);
            if (!sha.Equals(app.Sha256.Trim(), StringComparison.OrdinalIgnoreCase)) { File.Delete(update); throw new InvalidDataException("The downloaded Clonedivers failed its integrity check. Deleted it; try again."); }
            ShowProgressDone("Restarting…");
            await Task.Run(() => SelfUpdate.Swap(exe, update, old, TimeSpan.FromSeconds(30)));
            Program.ReleaseSingleInstance();
            Process.Start(new ProcessStartInfo(exe, "--updated " + app.Version) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
            exitingForUpdate = true;
            busy = BusyKind.None;   // OnFormClosing must not ask "cancel the update and quit?" — the update is done
            Close();
        }
        catch (OperationCanceledException) { ShowProgressDone("Update cancelled. The partial download resumes next time."); }
        catch (UnauthorizedAccessException ex) { ShowProgressDone("Update failed."); BrowserFallback(Path.GetDirectoryName(exe)!, ex.Message); }
        catch (Exception ex)
        {
            ShowProgressDone("Update failed.");
            MessageBox.Show(this, $"Clonedivers couldn't replace itself: {ex.Message}\n\nIf the new version is saved as {Path.GetFileName(update)} next to it, try again later or rename it by hand.",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
            OpenFolder(Path.GetDirectoryName(exe)!);
        }
        finally { SetBusy(BusyKind.None); }
    }

    // ---- progress plumbing

    void SetBusy(BusyKind kind)
    {
        busy = kind;
        if (kind != BusyKind.None) { rateWatch.Reset(); rateText = ""; lastWhat = ""; progress.Fraction = 0; }
        else { opCts?.Dispose(); opCts = null; Text = "Clonedivers"; }
        Power.KeepAwake(kind != BusyKind.None);
        RefreshState();
    }

    void ShowProgress(string what, long done, long total)
    {
        progress.Active = true;
        if (what != lastWhat) { lastWhat = what; rateWatch.Reset(); rateText = ""; }
        if (total > 0) { progress.Marquee = false; progress.Fraction = done / (double)total; }
        else progress.Marquee = true;

        if (!rateWatch.IsRunning) { rateWatch.Start(); rateBytes = done; }
        else if (rateWatch.ElapsedMilliseconds >= 1000)
        {
            var mbps = (done - rateBytes) / 1048576.0 / (rateWatch.ElapsedMilliseconds / 1000.0);
            rateText = $"  ·  {mbps:0.0} MB/s";
            rateWatch.Restart();
            rateBytes = done;
        }
        var pct = total > 0 ? (int)Math.Clamp(done * 100 / total, 0, 100) : -1;
        progressLabel.ForeColor = TextDim;
        progressLabel.Text = total > 0
            ? $"{what}  ·  {Pack.FormatBytes(done)} / {Pack.FormatBytes(total)}{rateText}  ·  {pct}%"
            : $"{what}  ·  {Pack.FormatBytes(done)}";
        Text = pct >= 0 ? $"Clonedivers  ·  {pct}%" : "Clonedivers";   // the taskbar shows progress while you alt-tab
        lastProgressMessage = DateTime.UtcNow;
    }

    void ShowProgressDone(string text)
    {
        progress.Active = false;
        progressLabel.ForeColor = TextDim;
        progressLabel.Text = text;
        lastProgressMessage = DateTime.UtcNow;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    /// <summary>Flashes the taskbar button until the window is focused: the inventory takes a while and friends alt-tab away.</summary>
    void FlashTaskbar()
    {
        try
        {
            var f = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = Handle, dwFlags = 2 | 12 /* FLASHW_TRAY | FLASHW_TIMERNOFG */, uCount = 0, dwTimeout = 0 };
            FlashWindowEx(ref f);
        }
        catch { }
    }

    // ---- toggle / launch / locate

    bool TryToggle()
    {
        try
        {
            ModFiles.Toggle(gameDir!);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this,
                "Windows refused to move files inside the game folder:\n" + ex.Message +
                "\n\nTry running Clonedivers as administrator (right-click → Run as administrator).",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this,
                ex.Message +
                "\n\nUsually another program has that file open — an antivirus scan, a download still finishing, or the game itself. " +
                "The other files moved. Once it's free, flip the switch off and on again and it will catch up. " +
                "The big button always shows where the files actually are.",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return false;
    }

    void OnToggle()
    {
        if (gameDir is null || Busy) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        if (!toggleArmed) { RefreshState(); return; }   // "no mod files" / "game not found" / interrupted: the text below says what to do
        TryToggle();
        RefreshState();
    }

    void OnLaunch()
    {
        if (busy == BusyKind.Apply) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }   // the dim button still explains itself when clicked
        if (!launchArmed) return;
        var state = ModFiles.GetState(gameDir);
        var signal = CurrentSignal(state, manifest?.Pack, SteamAcf.Read(gameDir));
        if (state == ModState.On && signal != Signal.None && !Busy)
        {
            var answer = MessageBox.Show(this,
                SignalSentence(signal, manifest?.Pack) + "\n\nSwitch the clones OFF before launching?\n\n" +
                "Yes — switch OFF, then launch (safe)\nNo — launch with the clones ON\nCancel — don't launch",
                "Clonedivers", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes && !TryToggle()) { RefreshState(); return; }
        }
        try
        {
            Game.LaunchViaSteam();
            launchRequested = DateTime.UtcNow;
            RefreshState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't hand off to Steam:\n" + ex.Message + "\n\nIs Steam installed?",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void OnLocate()
    {
        if (Busy) return;
        var picked = PromptForGameDir();
        if (picked is null) return;
        gameDir = picked;
        settings.GamePath = picked;
        settings.Save();
        RefreshState();
    }

    string? PromptForGameDir()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Where is Helldivers 2?  Pick the game folder (it contains data and bin)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        var steam = GameLocator.SteamPath();
        var common = steam is null ? null : Path.Combine(steam, "steamapps", "common");
        if (common is not null && Directory.Exists(common)) dlg.InitialDirectory = common;

        while (true)
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return null;
            var normalized = GameLocator.NormalizeGameDir(dlg.SelectedPath);
            if (normalized is not null) return normalized;
            MessageBox.Show(this,
                "That doesn't look like the Helldivers 2 folder:\n" + dlg.SelectedPath +
                "\n\nPick the folder that contains data\\ and bin\\helldivers2.exe — usually\n" +
                @"C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2" +
                "\n\nNot sure? In Steam: right-click Helldivers 2 → Manage → Browse local files. That window is the folder to pick.",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    static void OpenFolder(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); }
        catch { /* explorer missing? nothing useful to do */ }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (exitingForUpdate) { base.OnFormClosing(e); return; }
        if (busy == BusyKind.Apply)
        {
            MessageBox.Show(this, "Finishing the update — a few seconds. Try again in a moment.", "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }
        if (Busy && MessageBox.Show(this, "A pack update is in progress. Cancel it and quit?", "Clonedivers",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        opCts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { refresh.Dispose(); tips.Dispose(); opCts?.Dispose(); }
        base.Dispose(disposing);
    }
}

static class Program
{
    static Mutex? single;

    /// <summary>Lets the freshly swapped exe start while this one is still shutting down (the mutex is held for the process lifetime otherwise).</summary>
    public static void ReleaseSingleInstance()
    {
        try { single?.ReleaseMutex(); single?.Dispose(); } catch { }
        single = null;
    }

    [STAThread]
    static void Main(string[] args)
    {
        // The 60 MB self-contained exe takes a few seconds to unpack on first launch; a second double-click
        // during that pause should not open a second window.
        single = new Mutex(true, @"Local\Clonedivers", out bool firstInstance);
        if (!firstInstance) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 10F));

        string? updatedTo = null;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--updated") updatedTo = args[i + 1];
        if (Environment.ProcessPath is { } exe) SelfUpdate.CleanupOld(exe);

        var settings = Settings.Load();
        // A remembered manual pick wins if it is still valid; otherwise ask Steam where the game is.
        var gameDir = GameLocator.IsGameDir(settings.GamePath) ? settings.GamePath : GameLocator.AutoDetect();
        Application.Run(new MainForm(settings, gameDir, updatedTo));
        ReleaseSingleInstance();
    }
}
