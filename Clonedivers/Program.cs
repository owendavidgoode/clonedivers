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

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers");
    public static readonly string FilePath = Path.Combine(Dir, "config.json");

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
    public static bool IsRunning()
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

/// <summary>One downloadable piece of the pack. Size and SHA-256 come from tools\build-pack.ps1.</summary>
public sealed class PackFile
{
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>pack.json in the repo: where the current pack lives and how to verify it.</summary>
public sealed class PackManifest
{
    public string Version { get; set; } = "";
    public string Name { get; set; } = "Clonedivers pack";
    public string Notes { get; set; } = "";
    public List<PackFile> Files { get; set; } = new();

    public long TotalSize => Files.Sum(f => f.Size);
    public bool IsPublished => Files.Count > 0 && Files.All(f => !string.IsNullOrWhiteSpace(f.Url));
}

/// <summary>Pack install: download zips (resumable, hash-checked) and extract their *.patch_* files flat into data\.
/// Still just files — a zip reader and a downloader, nothing that knows the game exists.</summary>
public static class Pack
{
    // The API view is fresh the moment a push lands; the raw CDN can lag up to five minutes, so it is the fallback.
    public const string ManifestApiUrl = "https://api.github.com/repos/owendavidgoode/clonedivers/contents/pack.json?ref=main";
    public const string ManifestUrl = "https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/pack.json";
    public const string OldFolder = "mods_old";            // stale mods parked here on install; safe to delete
    public const string DownloadFolder = "mods_download";  // zips live here until extracted, then it is removed
    const string PartialSuffix = ".clonedivers-partial";   // never matches the patch-file pattern

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Clonedivers/{AppInfo.Version} (+https://github.com/owendavidgoode/clonedivers)");
        return c;
    }

    public static PackManifest? ParseManifest(string json) => JsonSerializer.Deserialize<PackManifest>(json, JsonOpts);

    /// <summary>Fetches pack.json: GitHub API first (fresh, but rate-limited to 60/hour per IP), raw CDN as fallback.</summary>
    public static async Task<PackManifest?> FetchManifestAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var req = new HttpRequestMessage(HttpMethod.Get, ManifestApiUrl);
            req.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
            using var resp = await Http.SendAsync(req, cts.Token);
            if (resp.IsSuccessStatusCode)
                return ParseManifest(await resp.Content.ReadAsStringAsync(cts.Token));
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* rate-limited or blocked: use the CDN copy */ }
        return await FetchManifestAsync(ManifestUrl, ct);
    }

    public static async Task<PackManifest?> FetchManifestAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(12));
        var bust = url + (url.Contains('?') ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return ParseManifest(await Http.GetStringAsync(bust, cts.Token));
    }

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


    /// <summary>Extracted size of the patch files inside a zip, read from the zip directory alone (no decompression).
    /// Lets the install bar span every part instead of restarting per zip.</summary>
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
            if (File.Exists(dest) && (File.GetAttributes(dest) & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(dest, FileAttributes.Normal);
            File.Move(tmp, dest, overwrite: true);
        }
        return entries.Count;
    }

    /// <summary>Downloads one pack file with HTTP Range resume, then checks size and SHA-256. A failed check deletes the file.</summary>
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

    /// <summary>The whole install, file-side: bring parked files back, park anything the pack does not contain
    /// in mods_old\, extract every zip into data\. Ends with the game in the ON state. Returns (installed, parked).</summary>
    public static (int installed, int parked) Install(string gameDir, IReadOnlyList<string> zips,
        Func<int, IProgress<(long done, long total)>?>? progressForZip, CancellationToken ct)
    {
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        var off = Path.Combine(gameDir, ModFiles.OffFolder);
        var old = Path.Combine(gameDir, OldFolder);

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
            if (File.Exists(dest) && (File.GetAttributes(dest) & FileAttributes.ReadOnly) != 0) File.SetAttributes(dest, FileAttributes.Normal);
            File.Move(f, dest, overwrite: true);
            parked++;
        }

        int installed = 0;
        for (int i = 0; i < zips.Count; i++)
            installed += ExtractPatchFiles(zips[i], data, progressForZip?.Invoke(i), ct);
        return (installed, parked);
    }

    public static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash);
    }

    public static string FormatBytes(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" :
        b >= 1L << 20 ? $"{b / (double)(1L << 20):0} MB" :
        b >= 1L << 10 ? $"{b / (double)(1L << 10):0} KB" : $"{b} B";
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

    // Pack install state
    PackManifest? manifest;
    string? manifestError;
    bool loadingManifest;
    DateTime lastManifestTry;
    CancellationTokenSource? opCts;
    bool busy;
    bool installArmed, downloadArmed, launchArmed = true;
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
    readonly ThinBar progress = new();
    readonly Label progressLabel = new();
    readonly Label statusLabel = new();
    readonly Label hintLabel = new();
    readonly Label pathCaption = new();
    readonly PathLabel pathValue = new();
    readonly ToolTip tips = new();
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1500 };

    public MainForm(Settings settings, string? gameDir)
    {
        this.settings = settings;
        this.gameDir = gameDir;

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
            // Offline at start? Ask GitHub again once a minute until pack.json answers.
            if (manifest is null && manifestError is not null && !loadingManifest && DateTime.UtcNow - lastManifestTry > TimeSpan.FromSeconds(60))
                _ = LoadManifestAsync();
            RefreshState();
        };
        Activated += (_, _) => RefreshState();
        Shown += (_, _) =>
        {
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

        // Progress: label above a thin bar. Both stay Visible for life — hiding a child collapses its AutoSize row and the window jumps.
        progressLabel.AutoSize = true;
        progressLabel.UseMnemonic = false;
        progressLabel.Dock = DockStyle.Fill;
        progressLabel.ForeColor = TextDim;
        progressLabel.Font = new Font("Segoe UI", 9F);
        progressLabel.TextAlign = ContentAlignment.MiddleLeft;
        progressLabel.Margin = new Padding(0, 12, 0, 4);
        progressLabel.MinimumSize = new Size(0, 36);   // two 9-pt lines at every scale: the "Pack installed … parked in mods_old\" message wraps, and this row must never move
        progressLabel.Text = "";
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

        var footer = new Label
        {
            Text = $"v{AppInfo.Version}  ·  moves mod files, launches through Steam, touches nothing else  ·  For the Republic.",
            Font = new Font("Segoe UI", 8.25F, FontStyle.Italic),
            ForeColor = TextMute,
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 10, 0, 0),
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header lockup
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // toggle (takes all spare height)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // hint
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));  // launch: 50-px button + 12 margin, level with the pack buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // pack buttons
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
        root.Controls.Add(progressLabel, 0, 6);
        root.Controls.Add(progress, 0, 7);
        root.Controls.Add(pathRow, 0, 8);
        root.Controls.Add(footer, 0, 9);
        Controls.Add(root);
    }

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

    void ApplyState()
    {
        var state = ModFiles.GetState(gameDir);
        var running = Game.IsRunning();
        var dataDir = gameDir is null ? "" : Path.Combine(gameDir, ModFiles.DataFolder);
        var active = gameDir is null ? 0 : ModFiles.ListPatchFiles(dataDir).Length;
        var parked = gameDir is null ? 0 : ModFiles.ListPatchFiles(Path.Combine(gameDir, ModFiles.OffFolder)).Length;

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
                detail = manifest?.IsPublished == true
                    ? "No mod pack installed.\nClick Download pack below to fetch the Clone Wars pack. Got the zips from Owen instead? Install pack from file… and select all the parts at once."
                    : "No mod files found in data\\ or mods_off\\.\nUnzip the mod you downloaded, then put its files (names ending in .patch_0, .patch_0.gpu_resources, .patch_0.stream)\ndirectly into this folder — not inside a sub-folder:\n" + dataDir;
                break;
            default:
                SetToggle("GAME NOT FOUND", Disabled, Disabled, TextDim, enabled: false);
                detail = "Couldn't find Helldivers 2 through Steam.\nClick \"Locate…\" and pick the game folder (it contains data\\ and bin\\helldivers2.exe).\nNot sure where it is? In Steam: right-click Helldivers 2 → Manage → Browse local files.";
                break;
        }

        var warn = running && state is ModState.On or ModState.Off;
        if (warn) detail = "Helldivers 2 is running — close it before toggling. Mods are only read at startup.\n" + detail;
        if (busy)
        {
            // Pack.Install moves mods_off → data first, so without this the switch would flip ON half-way through.
            SetToggle("INSTALLING PACK…", Disabled, Disabled, TextDim, enabled: false);
            detail = "Installing the pack — leave this window open.\nThe switch reads CLONES: ON when it finishes. Cancel keeps whatever has downloaded so far.";
            warn = false;
            dot = Color.Empty;
        }
        toggleButton.DotColor = dot;
        toggleButton.DotFilled = state == ModState.On;
        int nl = detail.IndexOf('\n');
        statusLabel.Text = nl < 0 ? detail : detail[..nl];
        hintLabel.Text = nl < 0 ? "" : detail[(nl + 1)..];
        statusLabel.ForeColor = warn ? Warn : TextMain;

        // Launch feedback: Steam can take a minute to open before the game process exists.
        if (running) launchRequested = null;
        var starting = !running && launchRequested is DateTime t && DateTime.UtcNow - t < TimeSpan.FromSeconds(90);
        launchArmed = !running && !starting;
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
        var canInstall = gameDir is not null && !running && !busy;
        installArmed = canInstall;
        SetArmed(installFileButton, "Install pack from file…", installArmed);
        Tip(installFileButton, "Have the pack as zip files from Owen? Pick all the parts at once. Needs the game closed.");
        downloadButton.SubText = "";
        if (busy)
        {
            downloadArmed = true;
            SetArmed(downloadButton, "Cancel", true);
            Tip(downloadButton, "Stops the download or install. Downloaded parts are kept and resume next time.");
        }
        else if (manifest is null && manifestError is null)
        {
            downloadArmed = false;
            SetArmed(downloadButton, "Checking for pack…", false);
            Tip(downloadButton, "Reading pack.json from GitHub.");
        }
        else if (manifest is null)
        {
            downloadArmed = false;
            SetQuiet(downloadButton, "Can't reach GitHub");
            downloadButton.SubText = "click to retry";
            Tip(downloadButton, manifestError!);
        }
        else if (!manifest.IsPublished)
        {
            downloadArmed = false;
            SetArmed(downloadButton, "Pack not published yet", false);
            Tip(downloadButton, "");
        }
        else
        {
            downloadArmed = canInstall;
            var installed = settings.InstalledPackVersion;
            var size = Pack.FormatBytes(manifest.TotalSize);
            var notes = string.IsNullOrWhiteSpace(manifest.Notes) ? manifest.Name : manifest.Name + "\n" + manifest.Notes;
            if (installed == manifest.Version)
            {
                SetQuiet(downloadButton, "Pack up to date");
                downloadButton.Cursor = canInstall ? Cursors.Hand : Cursors.Default;
                downloadButton.SubText = $"{manifest.Version}  ·  click to reinstall";
                Tip(downloadButton, $"You have pack {manifest.Version}. Click to download it again ({size}) and repair the install.\n\n{notes}");
            }
            else
            {
                SetArmed(downloadButton, installed is null ? "Download pack" : "Update pack", downloadArmed);
                downloadButton.SubText = $"{manifest.Version}  ·  {size}";
                Tip(downloadButton, (installed is null ? "" : $"Installed: {installed}\nAvailable: {manifest.Version}\n\n") + notes
                    + $"\n\nAbout {size}; needs roughly twice that free on the game drive while it installs. Resumes if interrupted.");
            }
        }
    }

    async Task LoadManifestAsync()
    {
        if (loadingManifest) return;
        loadingManifest = true; lastManifestTry = DateTime.UtcNow;
        try
        {
            manifest = await Pack.FetchManifestAsync(CancellationToken.None);
            manifestError = manifest is null ? "pack.json was empty" : null;
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
        if (busy) { opCts?.Cancel(); return; }
        if (manifest is null)
        {
            // "Can't reach GitHub · click to retry"
            if (manifestError is not null && !loadingManifest) { manifestError = null; _ = LoadManifestAsync(); RefreshState(); }
            return;
        }
        if (!downloadArmed || gameDir is null || !manifest.IsPublished) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        _ = DownloadAndInstallAsync(manifest);
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
        SetBusy(true);
        var cts = opCts = new CancellationTokenSource();
        try { await InstallCoreAsync(zips, packVersion: null, cts.Token); }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. The big button shows what is in data\\ now."); }
        catch (Exception ex) { ShowProgressDone("Install failed."); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task DownloadAndInstallAsync(PackManifest m)
    {
        var total = m.TotalSize;
        var drive = Path.GetPathRoot(gameDir!) ?? "";
        long free = 0;
        try { free = new DriveInfo(drive).AvailableFreeSpace; } catch { /* unknown drive type: skip the warning */ }
        var need = total * 2 + (512L << 20);   // the zip plus the extracted files, plus slack
        var headline = $"Download {m.Name} {m.Version} ({Pack.FormatBytes(total)}) and install it into data\\?"
                     + "\n\nLeave this window open while it runs. If it is interrupted, click Download pack again and it resumes."
                     + (string.IsNullOrWhiteSpace(m.Notes) ? "" : "\n\n" + m.Notes)
                     + (free > 0 && free < need ? $"\n\nWarning: only {Pack.FormatBytes(free)} free on {drive} — this needs about {Pack.FormatBytes(need)} while installing." : "");
        if (!ConfirmInstall(headline)) return;

        SetBusy(true);
        var cts = opCts = new CancellationTokenSource();
        var dlDir = Path.Combine(gameDir!, Pack.DownloadFolder);
        try
        {
            // One bar across every part: each part's bytes are offset by the parts before it.
            var zips = new List<string>();
            long before = 0;
            for (int i = 0; i < m.Files.Count; i++)
            {
                long offset = before;
                var dest = Path.Combine(dlDir, SafeFileName(m.Files[i].Url, $"pack-part{i + 1}.zip"));
                var reporter = new Progress<(long done, long total)>(p => ShowProgress("Downloading pack", offset + p.done, total));
                await Pack.DownloadAsync(m.Files[i], dest, reporter, cts.Token);
                before += m.Files[i].Size;
                ShowProgress("Downloading pack", before, total);   // a part that was already complete (resume) reports nothing
                zips.Add(dest);
            }
            await InstallCoreAsync(zips, m.Version, cts.Token);
            try { Directory.Delete(dlDir, recursive: true); } catch { /* leave it; harmless */ }
        }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. Partial downloads stay in mods_download\\ and resume next time."); }
        catch (Exception ex) { ShowProgressDone("Install failed."); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    /// <summary>Runs Pack.Install off the UI thread with one bar across every zip, then records the pack version.</summary>
    async Task InstallCoreAsync(IReadOnlyList<string> zips, string? packVersion, CancellationToken ct)
    {
        // Patch bytes per zip come from the zip directories (no decompression), so the bar can span all parts instead of restarting per zip.
        var sizes = await Task.Run(() => zips.Select(Pack.PatchBytes).ToArray(), ct);
        long grand = sizes.Sum();
        var starts = new long[sizes.Length];
        for (int i = 1; i < starts.Length; i++) starts[i] = starts[i - 1] + sizes[i - 1];

        // Progress objects are created here so their callbacks land on the UI thread.
        var reporters = zips.Select((_, i) => new Progress<(long done, long total)>(p => ShowProgress("Installing pack", starts[i] + p.done, grand))).ToList();

        var result = await Task.Run(() =>
        {
            if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 started while installing. Close it and try again.");
            return Pack.Install(gameDir!, zips, i => reporters[i], ct);
        }, ct);

        settings.InstalledPackVersion = packVersion;
        settings.Save();
        ShowProgressDone($"Pack installed: {result.installed:N0} files in data\\"
            + (result.parked > 0 ? $", {result.parked:N0} old file{(result.parked == 1 ? "" : "s")} parked in mods_old\\" : "")
            + ". Clones are ON — hit LAUNCH.");
    }

    void SetBusy(bool on)
    {
        busy = on;
        if (on) { rateWatch.Reset(); rateText = ""; lastWhat = ""; progress.Fraction = 0; }
        else { opCts?.Dispose(); opCts = null; Text = "Clonedivers"; }
        Power.KeepAwake(on);
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
        progressLabel.Text = total > 0
            ? $"{what}  ·  {Pack.FormatBytes(done)} / {Pack.FormatBytes(total)}{rateText}  ·  {pct}%"
            : $"{what}  ·  {Pack.FormatBytes(done)}";
        Text = pct >= 0 ? $"Clonedivers  ·  {pct}%" : "Clonedivers";   // the taskbar shows progress while you alt-tab
    }

    void ShowProgressDone(string text) { progress.Active = false; progressLabel.Text = text; }

    static string SafeFileName(string url, string fallback)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            name = Uri.UnescapeDataString(name);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : fallback;
        }
        catch { return fallback; }
    }

    void OnToggle()
    {
        if (gameDir is null || busy) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        if (!toggleArmed) { RefreshState(); return; }   // "no mod files" / "game not found": the text below says what to do
        try
        {
            ModFiles.Toggle(gameDir);
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
        RefreshState();
    }

    void OnLaunch()
    {
        if (busy) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }   // the dim button still explains itself when clicked
        if (!launchArmed) return;
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
        if (busy) return;
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
        if (busy && MessageBox.Show(this, "A pack install is in progress. Cancel it and quit?", "Clonedivers",
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
    [STAThread]
    static void Main()
    {
        // The 60 MB self-contained exe takes a few seconds to unpack on first launch; a second double-click
        // during that pause should not open a second window.
        using var single = new Mutex(true, @"Local\Clonedivers", out bool firstInstance);
        if (!firstInstance) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 10F));

        var settings = Settings.Load();
        // A remembered manual pick wins if it is still valid; otherwise ask Steam where the game is.
        var gameDir = GameLocator.IsGameDir(settings.GamePath) ? settings.GamePath : GameLocator.AutoDetect();
        Application.Run(new MainForm(settings, gameDir));
    }
}
