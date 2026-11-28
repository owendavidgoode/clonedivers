using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace Clonedivers;

internal static class LinuxForgeBootstrap
{
    internal static string[] MissingTools(string? searchPath = null) => new[] { "ip", "unshare", "flock", "pgrep", "pasta", "python3" }
        .Where(name => !(searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(folder => folder.Length > 0 && File.Exists(Path.Combine(folder, name)))).ToArray();
    internal const string NonAptToolsMessage = "Install iproute2, util-linux, procps, passt and Python3 with your distribution's package manager. " + LinuxForgePlayer.PastaGuidance;
    internal static string InstallToolsFailureMessage(IReadOnlyList<string> missing) =>
        missing.Count > 0 ? LinuxForgePlayer.MissingToolsMessage(missing)
        : "Linux component installation did not finish. " + LinuxForgePlayer.PastaGuidance + " No security setting was disabled.";
    internal static async Task InstallToolsAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (Game.IsRunning()) throw new InvalidOperationException("End HD2 before installing the Linux setup components.");
        if (!File.Exists("/usr/bin/apt-get")) throw new InvalidOperationException(NonAptToolsMessage);
        // The desktop owns approval; neither passwords nor approval dialogs are handled here.
        var start = new ProcessStartInfo("pkexec") { UseShellExecute = false, ArgumentList = { "/usr/bin/apt-get", "install", "-y", "iproute2", "util-linux", "procps", "passt", "python3" } };
        using var process = Process.Start(start) ?? throw new IOException("The desktop package approval could not open.");
        await process.WaitForExitAsync(ct);
        var missing = MissingTools();
        if (process.ExitCode != 0 || missing.Length != 0) throw new IOException(InstallToolsFailureMessage(missing));
    }
    internal static PackFile Installer => new() { Name = "HellForge-Linux-Installer-0.5.0.tar.gz",
        Url = "https://github.com/SkyeShade/HellForge/releases/download/v0.5.0-beta/HellForge-Linux-Installer.tar.gz",
        Size = 451363890, Sha256 = "2688d0382086839f540046cce68c1d16dd44ccf5b476f4b5d3ee80de7cf06532" };
    internal static string? Detect(string? preferred = null, string? home = null)
    {
        if (preferred != null && File.Exists(Path.Combine(preferred, "hellforge-install.json"))) return preferred;
        var drive = Path.Combine(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "hellforge-prefix", "pfx", "drive_c");
        var candidates = new List<string> { Path.Combine(drive, "HellForge") };
        var users = Path.Combine(drive, "users");
        if (Directory.Exists(users)) foreach (var user in Directory.EnumerateDirectories(users).Take(64)) candidates.Add(Path.Combine(user, "Documents", "HellForge"));
        var found = candidates.Where(path => File.Exists(Path.Combine(path, "hellforge-install.json"))).Distinct(StringComparer.Ordinal).ToArray();
        return found.Length == 1 ? found[0] : null;
    }
    /// <summary>A selection counts only when it holds the official marker inside a dedicated prefix; blank or foreign folders never block setup.</summary>
    internal static string? ExistingRoot(string? root)
    {
        root = root?.Trim();
        if (string.IsNullOrEmpty(root) || !File.Exists(Path.Combine(root, "hellforge-install.json"))) return null;
        try { PrefixFor(root); return root; } catch (InvalidDataException) { return null; }
    }
    internal static string? StartupRoot(string? saved, string? home = null) =>
        ExistingRoot(saved) ?? Detect(home: home) ?? (string.IsNullOrWhiteSpace(saved) ? null : saved.Trim()); // a stale saved folder never disables discovery
    /// <summary>Under the setup lease no other run uses earlier staging, so only this run's copy of the 440 MB payload ever stays on disk.</summary>
    internal static void SweepStaging(string cache) { foreach (var folder in Directory.EnumerateDirectories(cache, "unpacked-*")) RemoveStaging(folder); }
    static void RemoveStaging(string folder)
    {
        try { if (Directory.Exists(folder) && new DirectoryInfo(folder).LinkTarget == null) Directory.Delete(folder, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // cleanup never masks the setup result
    }
    internal static async Task<string> ExtractAsync(string archive, string output, CancellationToken ct)
    {
        HellForgeGameplayCatalog.RejectLinks(archive); HellForgeGameplayCatalog.RejectLinks(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Use a fresh installer staging folder.");
        Directory.CreateDirectory(output);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var input = File.OpenRead(archive); using var gzip = new GZipStream(input, CompressionMode.Decompress); using var tar = new TarReader(gzip);
        var names = new HashSet<string>(StringComparer.Ordinal); long bytes = 0; int count = 0;
        while (await tar.GetNextEntryAsync(copyData: false, cancellationToken: ct) is { } entry)
        {
            var name = entry.Name.Replace('\\', '/');
            if (name is "." or "./") continue;
            if (Path.IsPathFullyQualified(name) || name.StartsWith('/') || name.Split('/').Any(p => p == ".." || p.Contains(':')) || !names.Add(name) || ++count > 20000)
                throw new InvalidDataException("Unsafe/duplicate path in the official installer archive.");
            var target = Path.GetFullPath(Path.Combine(output, name));
            if (!target.StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Installer path escapes staging.");
            if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(target); continue; }
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new InvalidDataException("Links/special entries are not accepted in the installer.");
            bytes = checked(bytes + entry.Length); if (bytes > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Expanded installer exceeds the supported size.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                if (entry.DataStream != null) await entry.DataStream.CopyToAsync(file, ct);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | (entry.Mode & UnixFileMode.UserExecute));
        }
        var program = Path.Combine(output, "HellForge.Launcher");
        if (!File.Exists(program) || !File.Exists(Path.Combine(output, "HellForge-Linux.zip"))) throw new InvalidDataException("Official native installer payload is incomplete.");
        return program;
    }
    internal static Task<string?> InstallAsync(string? existing, IProgress<string>? progress, CancellationToken ct) =>
        InstallAsync(existing, progress, null, ct);

    internal static async Task<string?> InstallAsync(string? existing, IProgress<string>? progress, IProgress<OperationProgress>? richProgress, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native Forge setup requires Linux.");
        if (!LinuxForgePlan.SupportsArchitecture(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture)) throw new PlatformNotSupportedException("The native Forge installer requires x86-64 Linux.");
        if (Game.IsRunning()) throw new InvalidOperationException("End HD2 before upgrading its separate Forge application.");
        existing = ExistingRoot(existing);
        if (MissingTools().Length != 0)
        {
            richProgress?.Report(new("Installing Linux prerequisites", "Waiting for desktop package approval…", IsIndeterminate: true, CanCancel: false));
            await InstallToolsAsync(ct);
        }
        var cache = Path.Combine(PlatformPaths.ConfigDirectory, "installers", "forge-0.5.0"); HellForgeGameplayCatalog.RejectLinks(cache); Directory.CreateDirectory(cache);
        using var lease = new FileStream(Path.Combine(cache, "setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        SweepStaging(cache);
        var archive = Path.Combine(cache, Installer.Name);
        progress?.Report("Downloading and verifying official native Forge 0.5.0 setup…");
        var speed = new SpeedTracker();
        richProgress?.Report(new("Downloading HellForge 0.5.0", "Official Linux installer (431 MB)", 0, 0, 0, Installer.Size, CanCancel: true));
        await Pack.DownloadAsync(Installer, archive, new SyncProgress<(long Done, long Total)>(p =>
        {
            var (rate, eta) = speed.Update(p.Done, p.Total);
            richProgress?.Report(new("Downloading HellForge 0.5.0", "Official Linux installer", 0, 0, p.Done, p.Total, rate, eta, CanCancel: true));
        }), ct);
        if (Game.IsRunning()) throw new InvalidOperationException("HD2 started during download. Close it; the verified installer is saved.");
        richProgress?.Report(new("Verifying HellForge installer", "Checking SHA-256 signature…", IsIndeterminate: true, CanCancel: false));
        using var pinned = File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (pinned.Length != Installer.Size || await HellForgePack.HashAsync(archive, ct) != Installer.Sha256) throw new InvalidDataException("Native installer verification failed.");
        byte[]? existingModListBytes = null;
        if (existing != null)
        {
            var existingModListPath = Path.Combine(existing, "mods", "hellforge-mods.json");
            try
            {
                HellForgeGameplayCatalog.RejectLinks(existingModListPath);
                if (File.Exists(existingModListPath) && new FileInfo(existingModListPath).Length <= 4 * 1024 * 1024)
                    existingModListBytes = await File.ReadAllBytesAsync(existingModListPath, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        var staging = Path.Combine(cache, "unpacked-" + Guid.NewGuid().ToString("N"));
        Process? process = null;
        try
        {
            richProgress?.Report(new("Unpacking HellForge setup", "Preparing official native installer…", IsIndeterminate: true, CanCancel: false));
            var program = await ExtractAsync(archive, staging, ct);
            var instruction = existing == null ? "In official setup, choose Proton and Install. Clonedivers will reconnect automatically." :
                "In official setup, keep your existing prefix: " + PrefixFor(existing) + ". The installer keeps your game, account and mods.";
            progress?.Report(instruction);
            richProgress?.Report(new("Official HellForge setup open", instruction, IsIndeterminate: true, CanCancel: false));
            process = Process.Start(new ProcessStartInfo(program) { WorkingDirectory = Path.GetDirectoryName(program)!, UseShellExecute = false })
                ?? throw new IOException("Official native setup did not open.");
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0) throw new IOException("Official native setup ended with exit " + process.ExitCode + ". Existing profiles were not reset.");
        }
        finally
        {
            if (process?.HasExited != false) RemoveStaging(staging); // never pull the payload from under a still-running official setup
            process?.Dispose();
        }
        var root = Detect(existing);
        if (root == null) throw new IOException("Setup did not leave a discoverable installation. Use Locate to select the folder containing hellforge-install.json.");
        try { HellForgeRelease.SyncShippedBundledMods(root); HellForgeRelease.PatchLinuxInnerWrapper(root); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
        if (existing != null && existingModListBytes != null && string.Equals(Path.GetFullPath(root), Path.GetFullPath(existing), StringComparison.Ordinal))
            TryRestoreModListAfter05Upgrade(root, existingModListBytes);
        return root;
    }

    internal static void TryRestoreModListAfter05Upgrade(string root, byte[] savedModListBytes)
    {
        try
        {
            var appBuild = Path.Combine(root, "app", "app-build.json");
            HellForgeGameplayCatalog.RejectLinks(appBuild);
            if (!File.Exists(appBuild)) return;
            using var buildDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appBuild));
            if (!HellForgeRelease.Supports(buildDoc.RootElement.GetProperty("version").GetString())) return;
            HellForgeRelease.SyncShippedBundledMods(root);
            var modListPath = Path.Combine(root, "mods", "hellforge-mods.json");
            var temp = modListPath + ".linux-upgrade.tmp";
            HellForgeGameplayCatalog.RejectLinks(modListPath);
            HellForgeGameplayCatalog.RejectLinks(temp);
            var text = System.Text.Encoding.UTF8.GetString(savedModListBytes).TrimStart('\uFEFF');
            if (System.Text.Json.Nodes.JsonNode.Parse(text) is not System.Text.Json.Nodes.JsonObject savedObj) return;
            savedObj["importDone"] = true;
            Directory.CreateDirectory(Path.GetDirectoryName(modListPath)!);
            File.WriteAllBytes(temp, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(savedObj, HellForgePack.Json));
            File.Move(temp, modListPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException) { }
    }
    internal static string PrefixFor(string root)
    {
        for (var parent = new DirectoryInfo(Path.GetFullPath(root)); parent != null; parent = parent.Parent)
            if (parent.Name == "pfx" && parent.Parent is { } prefix) return prefix.FullName;
        throw new InvalidDataException("Choose an existing dedicated Forge prefix.");
    }
}
