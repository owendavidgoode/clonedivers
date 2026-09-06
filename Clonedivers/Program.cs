// Clonedivers — Helldivers 2 mod on/off switch + Steam launcher.
//
// The whole app lives in this one file on purpose. It does exactly two things:
//   1. Moves every *.patch_* file between <game>\data\ (ON) and <game>\mods_off\ (OFF).
//      Move = rename on the same drive, so gigabyte mod packs flip instantly.
//   2. Opens steam://rungameid/553850 so Steam launches the game the normal way.
// It never injects into, hooks, reads, or otherwise touches the game process.

using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

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
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Clonedivers/1.1 (+https://github.com/owendavidgoode/clonedivers)");
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

public sealed class MainForm : Form
{
    // Palette: Republic navy, clone-armor white, 501st blue, 212th orange.
    static readonly Color Bg = Color.FromArgb(11, 16, 32);
    static readonly Color TextMain = Color.FromArgb(240, 243, 250);
    static readonly Color TextDim = Color.FromArgb(150, 162, 190);
    static readonly Color Blue = Color.FromArgb(31, 95, 204);
    static readonly Color BlueHot = Color.FromArgb(52, 118, 230);
    static readonly Color Slate = Color.FromArgb(62, 68, 82);
    static readonly Color SlateHot = Color.FromArgb(80, 87, 104);
    static readonly Color Disabled = Color.FromArgb(30, 36, 52);
    static readonly Color Orange = Color.FromArgb(226, 118, 28);
    static readonly Color OrangeHot = Color.FromArgb(244, 140, 52);
    static readonly Color Warn = Color.FromArgb(255, 200, 40);

    readonly Settings settings;
    string? gameDir;
    DateTime? launchRequested;

    // Pack install state
    PackManifest? manifest;
    string? manifestError;
    CancellationTokenSource? opCts;
    bool busy;
    bool installArmed, downloadArmed;
    readonly Stopwatch rateWatch = new();
    long rateBytes;
    string rateText = "", lastWhat = "";

    readonly Button toggleButton = new();
    readonly Button launchButton = new();
    readonly Button pathButton = new();
    readonly Button installFileButton = new();
    readonly Button downloadButton = new();
    readonly ProgressBar progress = new();
    readonly Label progressLabel = new();
    readonly Label detailLabel = new();
    readonly Label pathValue = new();
    readonly LinkLabel openDataLink = new();
    readonly ToolTip tips = new();
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1500 };

    public MainForm(Settings settings, string? gameDir)
    {
        this.settings = settings;
        this.gameDir = gameDir;

        Text = "Clonedivers";
        BackColor = Bg;
        ForeColor = TextMain;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 610);
        MinimumSize = Size;   // never smaller than designed, or the toggle row would collapse to nothing
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { /* no icon resource: fine */ }

        BuildLayout();

        // Scale every pixel value above for the monitor's DPI (100–150%+); fonts scale on their own.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        refresh.Tick += (_, _) => RefreshState();
        Activated += (_, _) => RefreshState();
        Shown += (_, _) =>
        {
            RefreshState();
            refresh.Start();
            _ = LoadManifestAsync();                 // find out whether a pack is published (non-blocking)
            if (this.gameDir is null) OnLocate();   // first-run "pathing wizard": auto-detect failed, so ask.
        };
    }

    void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Bg,
            Padding = new Padding(28, 22, 28, 14),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "CLONEDIVERS",
            Font = new Font("Segoe UI", 26F, FontStyle.Bold),
            ForeColor = TextMain,
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0),
        };
        var subtitle = new Label
        {
            Text = "Helldivers 2  ·  Clone Wars mod switch",
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            ForeColor = TextDim,
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 10),
        };

        Style(toggleButton, Blue, BlueHot, TextMain, 24F);
        toggleButton.Dock = DockStyle.Fill;
        toggleButton.Margin = new Padding(0, 6, 0, 10);
        toggleButton.Click += (_, _) => OnToggle();

        detailLabel.AutoSize = true;
        detailLabel.UseMnemonic = false;   // paths with '&' must not turn into underlines
        detailLabel.Dock = DockStyle.Fill;
        detailLabel.ForeColor = TextDim;
        detailLabel.Font = new Font("Segoe UI", 9.75F);
        detailLabel.TextAlign = ContentAlignment.MiddleCenter;
        detailLabel.Margin = new Padding(0, 0, 0, 4);
        detailLabel.MinimumSize = new Size(0, 48);

        openDataLink.Text = "Open data folder";
        openDataLink.AutoSize = true;
        openDataLink.Dock = DockStyle.Fill;
        openDataLink.TextAlign = ContentAlignment.MiddleCenter;
        openDataLink.Font = new Font("Segoe UI", 9.5F);
        openDataLink.LinkColor = OrangeHot;
        openDataLink.ActiveLinkColor = TextMain;
        openDataLink.VisitedLinkColor = OrangeHot;
        openDataLink.LinkBehavior = LinkBehavior.HoverUnderline;
        openDataLink.Margin = new Padding(0, 0, 0, 10);
        openDataLink.LinkClicked += (_, _) => OpenFolder(Path.Combine(gameDir ?? "", ModFiles.DataFolder));

        Style(launchButton, Orange, OrangeHot, TextMain, 13F);
        launchButton.Text = "LAUNCH HELLDIVERS 2   (via Steam)";
        launchButton.Dock = DockStyle.Fill;
        launchButton.Margin = new Padding(0, 0, 0, 12);
        launchButton.Click += (_, _) => OnLaunch();

        // Pack row: "Install pack from file…"  |  "Download pack v… (5.1 GB)"
        var packRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0),
            BackColor = Bg,
        };
        packRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        packRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Style(installFileButton, Slate, SlateHot, TextMain, 10F);
        installFileButton.Text = "Install pack from file…";
        installFileButton.Dock = DockStyle.Fill;
        installFileButton.MinimumSize = new Size(0, 44);
        installFileButton.Margin = new Padding(0, 0, 6, 0);
        installFileButton.Click += (_, _) => OnInstallFromFile();
        Style(downloadButton, Slate, SlateHot, TextMain, 10F);
        downloadButton.Text = "Checking for pack…";
        downloadButton.Dock = DockStyle.Fill;
        downloadButton.MinimumSize = new Size(0, 44);
        downloadButton.Margin = new Padding(6, 0, 0, 0);
        downloadButton.Click += (_, _) => OnDownloadOrCancel();
        packRow.Controls.Add(installFileButton, 0, 0);
        packRow.Controls.Add(downloadButton, 1, 0);

        progress.Dock = DockStyle.Fill;
        progress.Maximum = 1000;
        progress.MinimumSize = new Size(0, 12);
        progress.Margin = new Padding(0, 10, 0, 2);
        progress.Visible = false;
        progressLabel.AutoSize = true;
        progressLabel.UseMnemonic = false;
        progressLabel.Dock = DockStyle.Fill;
        progressLabel.ForeColor = TextDim;
        progressLabel.Font = new Font("Segoe UI", 9F);
        progressLabel.TextAlign = ContentAlignment.MiddleCenter;
        progressLabel.Margin = new Padding(0, 2, 0, 10);
        progressLabel.Visible = false;

        // Path row: "Game folder:  C:\...\Helldivers 2   [Change…]"
        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0),
            BackColor = Bg,
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var pathCaption = new Label
        {
            Text = "Game folder:",
            AutoSize = true,
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 9.5F),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0),
        };
        pathValue.AutoSize = false;
        pathValue.UseMnemonic = false;
        pathValue.AutoEllipsis = true;
        pathValue.Dock = DockStyle.Fill;
        pathValue.ForeColor = TextMain;
        pathValue.Font = new Font("Segoe UI", 9.5F);
        pathValue.TextAlign = ContentAlignment.MiddleLeft;
        pathValue.Margin = new Padding(0);
        pathValue.MinimumSize = new Size(0, 30);
        Style(pathButton, Slate, SlateHot, TextMain, 9.5F);
        pathButton.Text = "Change…";
        pathButton.AutoSize = true;
        pathButton.Padding = new Padding(10, 4, 10, 4);
        pathButton.Anchor = AnchorStyles.Right;
        pathButton.Margin = new Padding(8, 0, 0, 0);
        pathButton.Click += (_, _) => OnLocate();
        pathRow.Controls.Add(pathCaption, 0, 0);
        pathRow.Controls.Add(pathValue, 1, 0);
        pathRow.Controls.Add(pathButton, 2, 0);

        var footer = new Label
        {
            Text = "v1.1  ·  moves mod files, launches through Steam, touches nothing else  ·  For the Republic.",
            Font = new Font("Segoe UI", 8.25F, FontStyle.Italic),
            ForeColor = Color.FromArgb(95, 105, 130),
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 10, 0, 0),
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // title
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // subtitle
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // toggle (takes all spare height)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // detail
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // open data folder
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));  // launch
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // pack buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progress bar
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progress text
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // path row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // footer
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(toggleButton, 0, 2);
        root.Controls.Add(detailLabel, 0, 3);
        root.Controls.Add(openDataLink, 0, 4);
        root.Controls.Add(launchButton, 0, 5);
        root.Controls.Add(packRow, 0, 6);
        root.Controls.Add(progress, 0, 7);
        root.Controls.Add(progressLabel, 0, 8);
        root.Controls.Add(pathRow, 0, 9);
        root.Controls.Add(footer, 0, 10);
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

    /// <summary>Re-derives everything from disk. Cheap, so it runs on a timer and on focus.</summary>
    void RefreshState()
    {
        var state = ModFiles.GetState(gameDir);
        var running = Game.IsRunning();
        var dataDir = gameDir is null ? "" : Path.Combine(gameDir, ModFiles.DataFolder);
        var active = gameDir is null ? 0 : ModFiles.ListPatchFiles(dataDir).Length;
        var parked = gameDir is null ? 0 : ModFiles.ListPatchFiles(Path.Combine(gameDir, ModFiles.OffFolder)).Length;

        string detail;
        switch (state)
        {
            case ModState.On:
                SetToggle("CLONES: ON", Blue, BlueHot, TextMain, enabled: true);
                detail = $"{active} mod file{(active == 1 ? "" : "s")} active in data\\"
                       + (parked > 0 ? $" ({parked} more still parked in mods_off\\)" : "")
                       + ".\nClick to park them in mods_off\\ and play vanilla."
                       + "\nGame crashing after a Helldivers 2 update? Switch OFF until the mods are updated.";
                break;
            case ModState.Off:
                SetToggle("CLONES: OFF", Slate, SlateHot, TextMain, enabled: true);
                detail = $"{parked} mod file{(parked == 1 ? "" : "s")} parked in mods_off\\.\nClick to move them back into data\\.";
                break;
            case ModState.NoModFiles:
                SetToggle("NO MOD FILES FOUND", Disabled, Disabled, TextDim, enabled: false);
                detail = "No mod files found in data\\ or mods_off\\.\nUnzip the mod you downloaded, then put its files (names ending in .patch_0, .patch_0.gpu_resources, .patch_0.stream)\ndirectly into this folder — not inside a sub-folder:\n" + dataDir;
                break;
            default:
                SetToggle("GAME NOT FOUND", Disabled, Disabled, TextDim, enabled: false);
                detail = "Couldn't find Helldivers 2 through Steam.\nClick \"Locate…\" and pick the game folder (it contains data\\ and bin\\helldivers2.exe).\nNot sure where it is? In Steam: right-click Helldivers 2 → Manage → Browse local files.";
                break;
        }

        if (running && state is ModState.On or ModState.Off)
        {
            detail = "Helldivers 2 is running — close it before toggling. Mods are only read at startup.\n" + detail;
            detailLabel.ForeColor = Warn;
        }
        else detailLabel.ForeColor = TextDim;
        detailLabel.Text = detail;

        openDataLink.Visible = gameDir is not null;

        // Launch feedback: Steam can take a minute to open before the game process exists.
        if (running) launchRequested = null;
        var starting = !running && launchRequested is DateTime t && DateTime.UtcNow - t < TimeSpan.FromSeconds(90);
        launchButton.Enabled = !running && !starting;
        launchButton.Text = running ? "HELLDIVERS 2 IS RUNNING"
                          : starting ? "STARTING VIA STEAM…   (Steam opens first if it was closed)"
                          : "LAUNCH HELLDIVERS 2   (via Steam)";
        pathValue.Text = gameDir ?? "not found";
        tips.SetToolTip(pathValue, gameDir ?? "");
        pathButton.Text = gameDir is null ? "Locate…" : "Change…";

        // Pack buttons
        var canInstall = gameDir is not null && !running && !busy;
        installArmed = canInstall;
        SetArmed(installFileButton, "Install pack from file…", installArmed);
        if (busy)
        {
            downloadArmed = true;
            SetArmed(downloadButton, "Cancel", true);
        }
        else if (manifest is null)
        {
            downloadArmed = false;
            SetArmed(downloadButton, manifestError is null ? "Checking for pack…" : "Pack info unavailable (offline?)", false);
            if (manifestError is not null) tips.SetToolTip(downloadButton, manifestError);
        }
        else if (!manifest.IsPublished)
        {
            downloadArmed = false;
            SetArmed(downloadButton, "Pack not published yet", false);
        }
        else
        {
            downloadArmed = canInstall;
            var installed = settings.InstalledPackVersion;
            var verb = installed == manifest.Version ? "Reinstall pack" : installed is null ? "Download pack" : "Update pack →";
            SetArmed(downloadButton, $"{verb} v{manifest.Version}   ({Pack.FormatBytes(manifest.TotalSize)})", downloadArmed);
            tips.SetToolTip(downloadButton, string.IsNullOrWhiteSpace(manifest.Notes) ? manifest.Name : manifest.Name + "\n" + manifest.Notes);
        }
    }

    async Task LoadManifestAsync()
    {
        try
        {
            manifest = await Pack.FetchManifestAsync(CancellationToken.None);
            manifestError = manifest is null ? "pack.json was empty" : null;
        }
        catch (Exception ex) { manifest = null; manifestError = ex.Message; }
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
        if (!downloadArmed || gameDir is null || manifest is null || !manifest.IsPublished) return;
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
        var root = Path.GetPathRoot(gameDir!) ?? "";
        long free = 0;
        try { free = new DriveInfo(root).AvailableFreeSpace; } catch { /* unknown drive type: skip the warning */ }
        var need = total * 2 + (512L << 20);   // the zip plus the extracted files, plus slack
        var headline = $"Download {m.Name} v{m.Version} ({Pack.FormatBytes(total)}) and install it into data\\?"
                     + (string.IsNullOrWhiteSpace(m.Notes) ? "" : "\n\n" + m.Notes)
                     + (free > 0 && free < need ? $"\n\nWarning: only {Pack.FormatBytes(free)} free on {root} — this needs about {Pack.FormatBytes(need)} while installing." : "");
        if (!ConfirmInstall(headline)) return;

        SetBusy(true);
        var cts = opCts = new CancellationTokenSource();
        var dlDir = Path.Combine(gameDir!, Pack.DownloadFolder);
        try
        {
            var zips = new List<string>();
            for (int i = 0; i < m.Files.Count; i++)
            {
                var part = m.Files.Count > 1 ? $" (part {i + 1} of {m.Files.Count})" : "";
                var dest = Path.Combine(dlDir, SafeFileName(m.Files[i].Url, $"pack-part{i + 1}.zip"));
                var reporter = new Progress<(long done, long total)>(p => ShowProgress("Downloading" + part, p.done, p.total));
                await Pack.DownloadAsync(m.Files[i], dest, reporter, cts.Token);
                zips.Add(dest);
            }
            await InstallCoreAsync(zips, m.Version, cts.Token);
            try { Directory.Delete(dlDir, recursive: true); } catch { /* leave it; harmless */ }
        }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. Partial downloads stay in mods_download\\ and resume next time."); }
        catch (Exception ex) { ShowProgressDone("Install failed."); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    /// <summary>Runs Pack.Install off the UI thread with per-part progress, then records the pack version.</summary>
    async Task InstallCoreAsync(IReadOnlyList<string> zips, string? packVersion, CancellationToken ct)
    {
        // Progress objects are created here so their callbacks land on the UI thread.
        var reporters = zips.Select((_, i) =>
        {
            var part = zips.Count > 1 ? $" (part {i + 1} of {zips.Count})" : "";
            return new Progress<(long done, long total)>(p => ShowProgress("Installing" + part, p.done, p.total));
        }).ToList();

        var result = await Task.Run(() =>
        {
            if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 started while installing. Close it and try again.");
            return Pack.Install(gameDir!, zips, i => reporters[i], ct);
        }, ct);

        settings.InstalledPackVersion = packVersion;
        settings.Save();
        ShowProgressDone($"Pack installed: {result.installed} files in data\\"
            + (result.parked > 0 ? $", {result.parked} old file{(result.parked == 1 ? "" : "s")} parked in mods_old\\" : "")
            + ". Clones are ON.");
    }

    void SetBusy(bool on)
    {
        busy = on;
        if (on) { rateWatch.Reset(); rateText = ""; lastWhat = ""; progress.Value = 0; }
        else { opCts?.Dispose(); opCts = null; }
        RefreshState();
    }

    void ShowProgress(string what, long done, long total)
    {
        progress.Visible = progressLabel.Visible = true;
        if (what != lastWhat) { lastWhat = what; rateWatch.Reset(); rateText = ""; }
        if (total > 0)
        {
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = (int)Math.Clamp(done * 1000 / total, 0, 1000);
        }
        else progress.Style = ProgressBarStyle.Marquee;

        if (!rateWatch.IsRunning) { rateWatch.Start(); rateBytes = done; }
        else if (rateWatch.ElapsedMilliseconds >= 1000)
        {
            var mbps = (done - rateBytes) / 1048576.0 / (rateWatch.ElapsedMilliseconds / 1000.0);
            rateText = $"  ·  {mbps:0.0} MB/s";
            rateWatch.Restart();
            rateBytes = done;
        }
        progressLabel.Text = total > 0
            ? $"{what}  ·  {Pack.FormatBytes(done)} / {Pack.FormatBytes(total)}{rateText}"
            : $"{what}  ·  {Pack.FormatBytes(done)}";
    }

    void ShowProgressDone(string text)
    {
        progress.Visible = false;
        progressLabel.Visible = true;
        progressLabel.Text = text;
    }

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
