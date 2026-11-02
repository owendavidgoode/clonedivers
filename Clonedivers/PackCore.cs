using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Clonedivers;

/// <summary>Where the mod files currently live. Always derived from disk, never stored.</summary>
public enum ModState { GameNotFound, NoModFiles, On, Off }

/// <summary>Untrusted metadata may name a file, never a path or a Windows device. Managed folders cannot redirect writes.</summary>
public static class FileSafety
{
    public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    public static void ValidateLeaf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || Path.IsPathRooted(name) ||
            Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid file name in pack metadata.");
    }

    public static void ValidatePatchName(string? name)
    {
        ValidateLeaf(name);
        if (!ModFiles.TryParsePatchName(name!, out _, out _, out _)) throw new InvalidDataException("Invalid patch file name in pack metadata.");
    }

    public static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Refusing a linked file or folder: {path}");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    public static string Child(string folder, string name)
    {
        ValidateLeaf(name);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!string.Equals(Path.GetDirectoryName(path), root, PlatformPaths.Comparison))
            throw new InvalidDataException("Pack path escapes its destination folder.");
        RejectLink(root); RejectLink(path);
        return path;
    }

    public static void ValidateFile(PackFile? file, bool named = true)
    {
        if (file is null || file.Size < 0 || !IsSha256(file.Sha256))
            throw new InvalidDataException("Pack files require a nonnegative size and a hexadecimal SHA-256.");
        if (named) ValidatePatchName(file.Name);
        if (file.Size == 0 && !string.Equals(file.Sha256, PackFile.EmptySha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An empty file must have the empty-file SHA-256.");
    }

    public static void ValidateTargets(IReadOnlyList<PackFile>? files)
    {
        if (files is null || files.Count == 0) throw new InvalidDataException("The recovery target contains no files.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            ValidateFile(file);
            if (!seen.Add(file.Name)) throw new InvalidDataException("Duplicate file name in recovery target.");
        }
    }
}

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

    /// <summary>mods_old\ never overwrites: a parked file whose name is already taken there becomes "&lt;name&gt;.1", ".2"…
    /// Those copies still hold pack bytes worth reusing (switching a variant off and on again parks the same names twice),
    /// so the inventory reads them back under their pack name. The suffix keeps them from ever looking like a mod file.</summary>
    static readonly Regex ParkedName = new(@"^(?<base>.+\.patch_\d+(?:\.(?:gpu_resources|stream))?)\.\d+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParseParkedName(string name, out string packName)
    {
        var m = ParkedName.Match(Path.GetFileName(name));
        packName = m.Success ? m.Groups["base"].Value : "";
        return m.Success;
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
        FileSafety.RejectLink(dir);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dir)
            .Where(IsPatchFile).Select(path => FileSafety.Child(dir, Path.GetFileName(path)))
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
                FileSafety.Child(fromDir, Path.GetFileName(src));
                FileSafety.Child(toDir, Path.GetFileName(dst));
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
        if (OperatingSystem.IsLinux()) return LinuxPlatform.SteamRoots().FirstOrDefault(Directory.Exists);
        if (!OperatingSystem.IsWindows()) return null;
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
        if (OperatingSystem.IsLinux())
        {
            foreach (var candidate in LinuxPlatform.CandidateGameDirs()) yield return candidate;
            yield break;
        }
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
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(OperatingSystem.IsWindows() ? p.Replace('/', '\\') : p)); }
        catch { return null; }
    }
}

/// <summary>%APPDATA%\Clonedivers\config.json — a manually chosen game path and which pack version was installed.</summary>
public sealed class Settings
{
    public string? GamePath { get; set; }
    public string? InstalledPackVersion { get; set; }
    public string? InstalledGameBuild { get; set; }
    public string? TextureProfile { get; set; }
    public string? InstalledTextureProfile { get; set; }
    public string? InstalledGamePath { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    /// <summary>Hidden test hook: a URL (or file path) that replaces manifest.json. Shown in the footer when set.</summary>
    public string? ManifestUrl { get; set; }
    public TelemetrySettings Telemetry { get; set; } = new();
    /// <summary>Optional pack groups the friend switched on or off (option id → enabled); anything absent uses the manifest default.</summary>
    public Dictionary<string, bool> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> OptionsFor(PackManifest pack, string? changedId = null, bool on = false)
    {
        var result = new Dictionary<string, bool>(Options, StringComparer.OrdinalIgnoreCase);
        foreach (var o in pack.Options) result.TryAdd(o.Id, o.Default);
        if (changedId is not null) result[changedId] = on;
        foreach (var o in pack.Options.Where(o => PackExtras.AlwaysOn(pack, o.Id))) result[o.Id] = true;
        return result;
    }

    public string ProfileFor(PackManifest pack) => PackProfiles.Resolve(pack,
        id => Options.TryGetValue(id, out var on) ? on : pack.Options.FirstOrDefault(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Default ?? false,
        TextureProfile);

    public void RecordInstall(ApplyResult result, bool preserveTexturePreference = false)
    {
        if (result.Interrupted) return;
        if (preserveTexturePreference && TextureProfile is null)
            TextureProfile = Options.TryGetValue("skinny", out var lighter) && lighter ? "lighter" : "full";
        InstalledPackVersion = result.PackVersion;
        InstalledGameBuild = result.GameBuild;
        InstalledTextureProfile = result.TextureProfile;
        if (!preserveTexturePreference && result.TextureProfile is not null) TextureProfile = result.TextureProfile;
        InstalledGamePath = result.GameDir;
        LastVerifiedUtc = result.VerifiedUtc;
        if (result.Options is not null) Options = new(result.Options, StringComparer.OrdinalIgnoreCase);
    }

    public bool ForgetInstallationForDifferentGame(string? gameDir)
    {
        if (gameDir is null || InstalledGamePath is null || RecoveryTarget.SameGame(gameDir, InstalledGamePath)) return false;
        InstalledPackVersion = null;
        InstalledGameBuild = null;
        InstalledTextureProfile = null;
        InstalledGamePath = null;
        LastVerifiedUtc = null;
        return true;
    }

    public bool BuildChanged(PackManifest? available, SteamAcf.Info? game)
    {
        if (game?.BuildId is null) return false;
        var tested = InstalledGameBuild;
        if (string.IsNullOrWhiteSpace(tested) && InstalledPackVersion == available?.Version) tested = available?.GameBuild;
        if (!string.IsNullOrWhiteSpace(tested)) return tested.Trim() != game.BuildId;
        // Older clients did not persist the build. Do not assume a newer remote pack describes their installed files.
        return InstalledPackVersion is not null && available is not null && InstalledPackVersion != available.Version;
    }

    static readonly string Dir = PlatformPaths.ConfigDirectory;
    public static readonly string FilePath = Path.Combine(Dir, "config.json");
    public static readonly string HashCachePath = Path.Combine(Dir, "hashes.json");      // static init order: after Dir
    public static readonly string PlanPath = Path.Combine(Dir, "apply-plan.json");
    public static string ReceiptPathFor(string gameDir) => Path.Combine(Dir, "receipts",
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            PlatformPaths.Identity(gameDir)))).ToLowerInvariant() + ".json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                loaded.Options = new(loaded.Options ?? new(), StringComparer.OrdinalIgnoreCase);
                loaded.Telemetry ??= new();
                return loaded;
            }
        }
        catch { /* corrupt or unreadable: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(FilePath + ".tmp", FilePath, overwrite: true);
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
        if (OperatingSystem.IsLinux()) return LinuxPlatform.IsGameRunning();
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
    public List<string>? Modes { get; set; }     // format 3: allowed visual modes; null means both modded modes
    public List<string>? TextureProfiles { get; set; } // format 3: allowed texture profile IDs; null means all

    /// <summary>SHA-256 of an empty file. 182 of the 604 r3 files are empty companions, so it is special-cased everywhere.</summary>
    public const string EmptySha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public PackFile Clone() => new() { Name = Name, Url = Url, Size = Size, Sha256 = Sha256, Option = Option, UnlessOption = UnlessOption,
        Modes = Modes?.ToList(), TextureProfiles = TextureProfiles?.ToList() };
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
    public bool CombinedRoster { get; set; }
    public string Version { get; set; } = "";
    public string Name { get; set; } = "Clonedivers pack";
    public string Notes { get; set; } = "";
    public string? GameBuild { get; set; }              // Steam buildid the pack was built and tested on
    public List<string> GameDepots { get; set; } = new();
    public string Status { get; set; } = "ok";          // "ok" | "broken" — flipped from the GitHub web editor when a game patch breaks things
    public string StatusNotes { get; set; } = "";
    public List<PackOption> Options { get; set; } = new();
    public List<PackTextureProfile> TextureProfiles { get; set; } = new();
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
    public bool CanSelfUpdate(string? manifestOverride, string currentVersion) =>
        string.IsNullOrWhiteSpace(manifestOverride) && IsPinned && IsNewerThan(currentVersion) && Size > 0 && FileSafety.IsSha256(Sha256);
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
        if (m.Format is not (1 or 2 or 3)) throw new InvalidDataException($"Unsupported manifest format: {m.Format}");
        if (m.Pack is { } p)
        {
            if (p.Options is null || p.Files is null || p.Options.Any(o => o is null))
                throw new InvalidDataException("manifest.json has invalid options or files.");
            // A name may appear twice only as a variant pair: the base file (unlessOption = X) and its replacement (option = X).
            var seen = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
            var optionIds = new HashSet<string>(p.Options.Select(o => o.Id ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (var f in p.Files)
            {
                if (f is null) throw new InvalidDataException("manifest.json contains a null file.");
                f.Sha256 = (f.Sha256 ?? "").Trim().ToLowerInvariant();
                f.Url = (f.Url ?? "").Trim();
                f.Name ??= "";
                if (f.Size == 0) f.Sha256 = PackFile.EmptySha;
                FileSafety.ValidateFile(f, named: f.Name.Length > 0);
                if (f.Name.Length == 0) { if (m.Format == 3) throw new InvalidDataException("Format 3 requires named patch files."); continue; }   // format-1 zip part
                if (!ModFiles.IsPatchFile(f.Name) || f.Name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
                    throw new InvalidDataException($"manifest.json lists an invalid file name: {f.Name}");
                if (m.Format < 3 && seen.TryGetValue(f.Name, out var other))
                {
                    var pair = (other.UnlessOption is not null && string.Equals(other.UnlessOption, f.Option, StringComparison.OrdinalIgnoreCase))
                            || (f.UnlessOption is not null && string.Equals(f.UnlessOption, other.Option, StringComparison.OrdinalIgnoreCase));
                    if (!pair) throw new InvalidDataException($"manifest.json lists {f.Name} twice without a variant option");
                }
                else seen[f.Name] = f;
                if (f.Option is not null && !optionIds.Contains(f.Option)) throw new InvalidDataException($"manifest.json: {f.Name} refers to an unknown option '{f.Option}'");
                if (f.UnlessOption is not null && !optionIds.Contains(f.UnlessOption)) throw new InvalidDataException($"manifest.json: {f.Name} refers to an unknown option '{f.UnlessOption}'");
            }
            if (m.Format < 3 && (p.TextureProfiles is null || p.TextureProfiles.Count > 0 || p.Files.Any(f => f.Modes is not null || f.TextureProfiles is not null)))
                throw new InvalidDataException("Texture profiles and mode conditions require manifest format 3.");
            if (m.Format == 3) PackProfiles.Validate(p);
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
    public bool TargetActive { get; set; } = true;
    public List<PackFile> TargetFiles { get; } = new();
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
    public string PackVersion { get; set; } = "";
    public string? GameBuild { get; set; }
    public Dictionary<string, bool>? Options { get; set; }
    public bool TargetActive { get; set; } = true;
    public string? TextureProfile { get; set; }
    public string? GameDir { get; set; }
    public DateTimeOffset? VerifiedUtc { get; set; }
    public int Renamed, Parked, Copied, Created;
    public List<string> Failed { get; } = new();
    public string? Reason;
    public bool Interrupted => Failed.Count > 0 || Reason is not null;
}

sealed class PlanFile
{
    public int Format { get; set; } = 2;
    public string PackVersion { get; set; } = "";
    public string? GameBuild { get; set; }
    public Dictionary<string, bool>? Options { get; set; }
    public bool TargetActive { get; set; } = true;
    public string? TextureProfile { get; set; }
    public bool Verified { get; set; }
    public List<PackFile> TargetFiles { get; set; } = new();
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
        if (names.Length == 0) throw new InvalidDataException($"No *.patch_* files inside {Path.GetFileName(zipPath)}.");
        foreach (var name in names) FileSafety.ValidatePatchName(name);
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
        FileSafety.RejectLink(dataDir);
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
            var dest = FileSafety.Child(dataDir, e.Name);
            var tmp = FileSafety.Child(dataDir, e.Name + PartialSuffix);
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
        FileSafety.RejectLink(old);
        if (ModFiles.HasStagedFiles(gameDir))
            throw new InvalidOperationException("A pack update was interrupted. Click Finish update first, then install from file.");

        var incoming = new HashSet<string>(zips.SelectMany(ListPatchEntries), StringComparer.OrdinalIgnoreCase);
        var dupAcross = zips.SelectMany(ListPatchEntries).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dupAcross is not null)
            throw new InvalidDataException($"'{dupAcross.Key}' is in more than one of the selected zips. Pick the parts of one pack only.");

        ct.ThrowIfCancellationRequested();
        ModFiles.MoveAllPatchFiles(off, data);
        int parked = 0;
        foreach (var f in ModFiles.ListPatchFiles(data))
        {
            ct.ThrowIfCancellationRequested();
            if (incoming.Contains(Path.GetFileName(f))) continue;
            Directory.CreateDirectory(old);
            var dest = Path.Combine(old, Path.GetFileName(f));
            File.Move(f, FreeName(dest));
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
        for (var attempt = 0; ; attempt++)
        {
            try { await DownloadAttemptAsync(file, destPath, progress, ct); return; }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 2 &&
                (ex is DownloadInterruptedException || ex is HttpRequestException h &&
                    (h.StatusCode is null || h.StatusCode == HttpStatusCode.RequestTimeout || (int)h.StatusCode == 429 || (int)h.StatusCode >= 500)))
            {
                await Task.Delay(DownloadRetryDelay * (attempt + 1), ct);
            }
        }
    }

    internal static TimeSpan DownloadInactivityTimeout { get; set; } = TimeSpan.FromSeconds(30);
    internal static TimeSpan DownloadRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    sealed class DownloadInterruptedException(string message, Exception? inner = null) : IOException(message, inner);

    static async Task DownloadAttemptAsync(PackFile file, string destPath, IProgress<(long done, long total)>? progress, CancellationToken ct)
    {
        FileSafety.RejectLink(destPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        long existing = File.Exists(destPath) ? new FileInfo(destPath).Length : 0;
        if (file.Size > 0 && existing > file.Size) { File.Delete(destPath); existing = 0; }

        if (!(file.Size > 0 && existing == file.Size))   // already complete? then just verify below
        {
            using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(ct);
            inactivity.CancelAfter(DownloadInactivityTimeout);
            try
            {
            using var req = new HttpRequestMessage(HttpMethod.Get, file.Url);
            if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, inactivity.Token);

            if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (existing == 0) throw new InvalidDataException("The server cannot provide this file. Retry the download later.");
                File.Delete(destPath);
                await DownloadAttemptAsync(file, destPath, progress, ct);   // one retry without a Range header
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

            using var net = await resp.Content.ReadAsStreamAsync(inactivity.Token);
            using var fs = new FileStream(destPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            var buffer = new byte[1 << 20];
            long written = existing;
            int n;
            while ((n = await net.ReadAsync(buffer, inactivity.Token)) > 0)
            {
                inactivity.CancelAfter(DownloadInactivityTimeout);
                await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                written += n;
                progress?.Report((written, total));
            }
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            { throw new DownloadInterruptedException("The download stopped responding. Progress is saved; click Retry to resume.", ex); }
            catch (HttpIOException ex)
            { throw new DownloadInterruptedException("The connection ended early. Progress is saved; click Retry to resume.", ex); }
        }

        var length = new FileInfo(destPath).Length;
        if (file.Size > 0 && length != file.Size)
        {
            if (length < file.Size) throw new DownloadInterruptedException($"The download stopped at {length:N0} of {file.Size:N0} bytes. Progress is saved; click Retry to resume.");
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
        foreach (var f in plan.Downloads) FileSafety.ValidateFile(f);
        FileSafety.RejectLink(dlDir);
        var files = plan.Downloads.OrderByDescending(f => f.Size).ToList();
        long total = files.Sum(f => f.Size), before = 0;
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = files[i];
            var complete = FileSafety.Child(dlDir, f.Sha256);
            if (!await MatchesAsync(complete, f, ct))
            {
                var partial = FileSafety.Child(dlDir, f.Sha256 + PartialSuffix);
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
        FileSafety.RejectLink(dlDir);
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

    static async Task<bool> MatchesAsync(string path, PackFile file, CancellationToken ct) =>
        File.Exists(path) && new FileInfo(path).Length == file.Size &&
        string.Equals(await Sha256Async(path, ct), file.Sha256, StringComparison.OrdinalIgnoreCase);

    public static async Task<HashSet<string>> VerifiedDownloadsAsync(string dlDir, IEnumerable<PackFile> wanted, CancellationToken ct)
    {
        FileSafety.RejectLink(dlDir);
        var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in wanted.Where(f => f.Size > 0).DistinctBy(f => f.Sha256))
        {
            ct.ThrowIfCancellationRequested();
            FileSafety.ValidateFile(f);
            if (await MatchesAsync(FileSafety.Child(dlDir, f.Sha256), f, ct)) complete.Add(f.Sha256);
        }
        return complete;
    }

    /// <summary>The manifest's files after the friend's option choices, renumbered so the enabled set is gap-free.
    /// With every option on, names are exactly the manifest's.</summary>
    public static List<PackFile> EffectiveFiles(PackManifest pack, Func<string, bool> optionEnabled, string? textureProfile = null)
    {
        var profile = PackProfiles.Resolve(pack, optionEnabled, textureProfile);
        bool Enabled(string id) => PackExtras.AlwaysOn(pack, id) || (string.Equals(id, "skinny", StringComparison.OrdinalIgnoreCase) ? profile != "full" : optionEnabled(id));
        var mode = LauncherModes.SupportsEmpire(pack) && Enabled(LauncherModes.EmpireOption) ? "empire" :
            (pack.TextureProfiles.Count > 0 || pack.Options.Any(o => string.Equals(o.Id, "commandos", StringComparison.OrdinalIgnoreCase))) && Enabled("commandos") ? "commandos" : "clonedivers";
        var kept = pack.Files.Where(f => PackProfiles.Includes(f, mode, profile) && (f.Option is null || Enabled(f.Option)) && (f.UnlessOption is null || !Enabled(f.UnlessOption))).Select(f => f.Clone()).ToList();
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
        FileSafety.ValidateTargets(wanted);
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        var off = Path.Combine(gameDir, ModFiles.OffFolder);
        var old = Path.Combine(gameDir, OldFolder);
        var wantedSizes = wanted.Where(f => f.Size > 0).Select(f => f.Size).ToHashSet();
        var need = wanted.GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var found = new List<LocalFile>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Retaining inactive-profile hashes requires considering collisions across all folders,
        // even when mods_old will not be content-scanned for this particular choice.
        var existingKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { data, off, old })
        {
            FileSafety.RejectLink(dir);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                var fi = new FileInfo(path);
                if (!ModFiles.IsPatchFile(fi.Name) && !ModFiles.TryParseParkedName(fi.Name, out _) && !ModFiles.IsStagedFile(fi.Name)) continue;
                var key = HashCache.Key(fi.Name, fi.Length, fi.LastWriteTimeUtc);
                existingKeys[key] = existingKeys.GetValueOrDefault(key) + 1;
            }
        }
        var ambiguousKeys = existingKeys.Where(k => k.Value > 1).Select(k => k.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in ambiguousKeys) cache.Entries.Remove(key);

        List<(string path, string name, FileInfo fi, LocalWhere where)> Scan(string dir, LocalWhere where)
        {
            FileSafety.RejectLink(dir);
            var list = new List<(string, string, FileInfo, LocalWhere)>();
            if (!Directory.Exists(dir)) return list;
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                var fn = Path.GetFileName(path);
                if (ModFiles.IsPatchFile(fn) || ModFiles.IsStagedFile(fn) || ModFiles.TryParseParkedName(fn, out _)) FileSafety.Child(dir, fn);
                if (where == LocalWhere.Staged)
                {
                    if (!fn.EndsWith(ModFiles.StagedSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add((path, fn[..^ModFiles.StagedSuffix.Length], new FileInfo(path), where));
                }
                else if (ModFiles.IsPatchFile(fn)) list.Add((path, fn, new FileInfo(path), where));
                // mods_old\ only: "<name>.N" copies parked when the name was taken (see FreeName) are the same pack bytes.
                else if (where == LocalWhere.Old && ModFiles.TryParseParkedName(fn, out var packName)) list.Add((path, packName, new FileInfo(path), where));
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
                var key = HashCache.Key(b.fi.Name, b.fi.Length, b.fi.LastWriteTimeUtc);   // the file's own name: a ".1" twin and the file it shadows must not share a key
                keyCount[key] = keyCount.TryGetValue(key, out var n) ? n + 1 : 1;
                jobs.Add((b.path, b.name, b.fi, b.where, key, null));
            }
            long total = 0;
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                if (j.fi.Length == 0) { jobs[i] = j with { sha = PackFile.EmptySha }; continue; }
                if (!sizeWanted(j.fi.Length)) continue;                                   // cannot match anything: not worth reading
                if (!forceRehash && keyCount[j.key] == 1 && !ambiguousKeys.Contains(j.key) && cache.Entries.TryGetValue(j.key, out var cached)) { jobs[i] = j with { sha = cached }; seenKeys.Add(j.key); continue; }
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
                if (keyCount[j.key] == 1 && !ambiguousKeys.Contains(j.key)) { cache.Entries[j.key] = sha; seenKeys.Add(j.key); }
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

        // A profile can leave most of mods_old unscanned. Preserve hashes for existing parked
        // files without reading their contents; prune only stale file identities.
        seenKeys.UnionWith(existingKeys.Keys);
        foreach (var k in cache.Entries.Keys.Where(k => !seenKeys.Contains(k)).ToList()) cache.Entries.Remove(k);
        return found;
    }

    // ---------------------------------------------------------------- plan

    /// <summary>Decides every move without touching disk. Matching is by sha as a multiset: K manifest entries sharing one sha
    /// need K local copies; the shortfall is copied from a local twin when one exists, downloaded once otherwise.
    /// Local files that match nothing are parked. Zero-byte entries are created, never downloaded.</summary>
    public static UpdatePlan Plan(string gameDir, IReadOnlyList<PackFile> wanted, IReadOnlyList<LocalFile> local, IReadOnlySet<string> completeDownloads, bool targetActive = true)
    {
        FileSafety.ValidateTargets(wanted);
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        var destination = Path.Combine(gameDir, targetActive ? ModFiles.DataFolder : ModFiles.OffFolder);
        var old = Path.Combine(gameDir, OldFolder);
        var dl = Path.Combine(gameDir, DownloadFolder);
        var plan = new UpdatePlan { TargetActive = targetActive };
        plan.TargetFiles.AddRange(wanted.Select(f => f.Clone()));
        // Fresh names never overwrite leftovers from an interrupted plan, including a renumbering cycle.
        var stageId = Guid.NewGuid().ToString("N") + "-";
        string Staged(string name) => Path.Combine(data, stageId + name + ModFiles.StagedSuffix);

        var used = new HashSet<LocalFile>();
        var source = new Dictionary<PackFile, string>();          // entry → path it will be finalized from (in-place path or staged path)
        var inPlace = new HashSet<PackFile>();
        var stages = new List<PlanAction>();
        var copies = new List<PlanAction>();
        var creates = new List<PlanAction>();

        // 1. In place: right name, right bytes, already in data\.
        var byName = local.Where(l => l.Where == (targetActive ? LocalWhere.Data : LocalWhere.Off)).GroupBy(l => l.Name, PlatformPaths.Comparer).ToDictionary(g => g.Key, g => g.First(), PlatformPaths.Comparer);
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
            if (!string.Equals(pick.Path, to, PlatformPaths.Comparison)) stages.Add(new PlanAction(PlanOp.Stage, pick.Path, to, e.Sha256, e.Size));
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
            .Select(e => new PlanAction(PlanOp.Finalize, source[e], Path.Combine(destination, e.Name), e.Sha256, e.Size)));
        return plan;
    }

    // ---------------------------------------------------------------- apply

    static readonly JsonSerializerOptions PlanJson = new() { WriteIndented = true };

    /// <summary>Runs the plan. The plan is written to <paramref name="planPath"/> before anything moves, so an interrupted run can be
    /// reconciled after interruption. Never cancellable once started: every step is a same-volume rename or a copy.</summary>
    public static ApplyResult Apply(string gameDir, UpdatePlan plan, string planPath, string packVersion, HashCache? cache, string? cachePath,
        string? gameBuild = null, Dictionary<string, bool>? options = null, string? textureProfile = null, bool verified = false, string? receiptPath = null)
    {
        FileSafety.ValidateTargets(plan.TargetFiles);
        foreach (var action in plan.Actions) ValidateActionPaths(gameDir, action.Op, action.From, action.To);
        FileSafety.RejectLink(planPath); FileSafety.RejectLink(planPath + ".tmp");
        var pf = new PlanFile { PackVersion = packVersion, GameBuild = gameBuild, Options = options, TargetFiles = plan.TargetFiles,
            TargetActive = plan.TargetActive, TextureProfile = textureProfile, Verified = verified,
            GameDir = gameDir, Actions = plan.Actions.Select(a => new PlanFileAction { Op = a.Op.ToString(), From = a.From, To = a.To, Sha = a.Sha, Size = a.Size }).ToList() };
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        File.WriteAllText(planPath + ".tmp", JsonSerializer.Serialize(pf, PlanJson));
        File.Move(planPath + ".tmp", planPath, overwrite: true);
        if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 started while installing. Close it and try again.");
        var result = Execute(gameDir, pf.Actions, planPath, cache, cachePath);
        result.PackVersion = packVersion; result.GameBuild = gameBuild; result.Options = options;
        result.TargetActive = plan.TargetActive; result.TextureProfile = textureProfile; result.GameDir = gameDir;
        result.VerifiedUtc = verified ? DateTimeOffset.UtcNow : null;
        if (!result.Interrupted)
        {
            // Commit durable installed metadata before dropping the recovery target. A crash
            // before this point is repaired from the plan; a crash afterward is read from the receipt.
            if (receiptPath is not null) InstallReceipt.Write(receiptPath, gameDir, plan.TargetFiles, result);
            try { File.Delete(planPath); } catch { }
        }
        return result;
    }

    public static bool HasPendingUpdate(string? gameDir, string planPath)
    {
        if (ModFiles.HasStagedFiles(gameDir)) return true;
        if (!File.Exists(planPath)) return false;
        try
        {
            var pf = JsonSerializer.Deserialize<PlanFile>(File.ReadAllText(planPath));
            return pf is not null && gameDir is not null && SameGame(pf.GameDir, gameDir);
        }
        catch { return false; }
    }

    static bool SameGame(string a, string b) => string.Equals(PlatformPaths.Identity(a), PlatformPaths.Identity(b), StringComparison.Ordinal);

    /// <summary>Reconcile the saved target against actual bytes, including already finalized and parked files.
    /// Old action-only plans and missing bytes return null for a fresh manifest-based repair; no old action is replayed blindly.</summary>
    public static ApplyResult? Replay(string gameDir, string planPath, HashCache? cache, string? cachePath, string? receiptPath = null)
    {
        PlanFile? pf;
        try { pf = File.Exists(planPath) ? JsonSerializer.Deserialize<PlanFile>(File.ReadAllText(planPath)) : null; }
        catch { pf = null; }
        if (pf is null || pf.Format != 2) return null;
        try
        {
            if (!SameGame(pf.GameDir, gameDir)) return null;
            FileSafety.ValidateTargets(pf.TargetFiles);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException) { return null; }
        if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 is running. Close it, then click Finish update.");
        var local = InventoryAsync(gameDir, pf.TargetFiles, cache ?? new HashCache(), true, null, CancellationToken.None).GetAwaiter().GetResult();
        var complete = VerifiedDownloadsAsync(Path.Combine(gameDir, DownloadFolder), pf.TargetFiles, CancellationToken.None).GetAwaiter().GetResult();
        var plan = Plan(gameDir, pf.TargetFiles, local, complete, pf.TargetActive);
        if (plan.Downloads.Count > 0) return null;
        return Apply(gameDir, plan, planPath, pf.PackVersion, cache, cachePath, pf.GameBuild, pf.Options, pf.TextureProfile, true, receiptPath);
    }

    static void ValidateActionPaths(string gameDir, PlanOp op, string from, string to)
    {
        void InFolder(string path, params string[] folders)
        {
            var full = Path.GetFullPath(path);
            foreach (var folder in folders)
            {
                var root = Path.GetFullPath(Path.Combine(gameDir, folder));
                if (string.Equals(Path.GetDirectoryName(full), root, PlatformPaths.Comparison))
                {
                    FileSafety.Child(root, Path.GetFileName(full));
                    return;
                }
            }
            throw new InvalidDataException("An update action escapes its managed game folder.");
        }
        if (op == PlanOp.Finalize) InFolder(to, ModFiles.DataFolder, ModFiles.OffFolder);
        else InFolder(to, op == PlanOp.Park ? OldFolder : ModFiles.DataFolder);
        if (op == PlanOp.Finalize) FileSafety.ValidatePatchName(Path.GetFileName(to));
        else if (op != PlanOp.Park && !ModFiles.IsStagedFile(to)) throw new InvalidDataException("Update writes must use staged file names.");
        if (op != PlanOp.Create) InFolder(from, ModFiles.DataFolder, ModFiles.OffFolder, OldFolder, DownloadFolder);
    }

    static ApplyResult Execute(string gameDir, List<PlanFileAction> actions, string planPath, HashCache? cache, string? cachePath)
    {
        var r = new ApplyResult();
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        bool abort = false;

        void Try(PlanFileAction a, Action work)
        {
            try { ValidateActionPaths(gameDir, Enum.Parse<PlanOp>(a.Op), a.From, a.To); work(); }
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
                    if (File.Exists(a.To)) { abort = true; r.Reason = $"both {Path.GetFileName(a.From)} and its staged copy exist"; break; }
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
            Try(a, () => { Directory.CreateDirectory(Path.GetDirectoryName(a.To)!); File.Move(a.From, a.To); });
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
        var dl = Path.Combine(gameDir, DownloadFolder);
        try { if (Directory.Exists(dl) && !Directory.EnumerateFileSystemEntries(dl).Any()) Directory.Delete(dl); } catch { }
        return r;
    }

    /// <summary>mods_old\X, then X.1, X.2 … Nothing in mods_old\ is ever overwritten. ".N" never matches the patch pattern.</summary>
    static string FreeName(string target)
    {
        FileSafety.Child(Path.GetDirectoryName(target)!, Path.GetFileName(target));
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

internal static class AppInfo
{
    // AssemblyVersion is derived from <Version> in the csproj. Application.ProductVersion would carry "+<git sha>".
    public static readonly string Version =
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";
}

