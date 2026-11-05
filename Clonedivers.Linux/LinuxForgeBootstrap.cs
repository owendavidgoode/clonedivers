using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace Clonedivers;

internal static class LinuxForgeBootstrap
{
    internal static string[] MissingTools(string? searchPath = null) => new[] { "ip", "unshare", "flock", "pgrep", "pasta", "python3" }
        .Where(name => !(searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(folder => folder.Length > 0 && File.Exists(Path.Combine(folder, name)))).ToArray();
    internal static async Task InstallToolsAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (Game.IsRunning()) throw new InvalidOperationException("End HD2 before installing the Linux setup components.");
        if (!File.Exists("/usr/bin/apt-get")) throw new InvalidOperationException("Install iproute2, util-linux, procps, passt and Python3 with your distribution's package manager.");
        // The desktop owns approval; neither passwords nor approval dialogs are handled here.
        var start = new ProcessStartInfo("pkexec") { UseShellExecute = false, ArgumentList = { "/usr/bin/apt-get", "install", "-y", "iproute2", "util-linux", "procps", "passt", "python3" } };
        using var process = Process.Start(start) ?? throw new IOException("The desktop package approval could not open.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0 || MissingTools().Length != 0) throw new IOException("Linux component installation did not finish. Co-op requires pasta (passt) and Python3; no security setting was disabled.");
    }
    internal static PackFile Installer => new() { Name = "HellForge-Linux-Installer-0.4.0.tar.gz",
        Url = "https://github.com/SkyeShade/HellForge/releases/download/v0.4.0-beta/HellForge-Linux-Installer.tar.gz",
        Size = 439876336, Sha256 = "62e599c39cf9f102c4efecfc73311e8bf02dc74d1d559cfe6506ed023ac5b5d5" };
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
    internal static async Task<string?> InstallAsync(string? existing, IProgress<string>? progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native Forge setup requires Linux.");
        if (!LinuxForgePlan.SupportsArchitecture(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture)) throw new PlatformNotSupportedException("The native Forge installer requires x86-64 Linux.");
        if (Game.IsRunning()) throw new InvalidOperationException("End HD2 before upgrading its separate Forge application.");
        if (MissingTools().Length != 0) await InstallToolsAsync(ct);
        var cache = Path.Combine(PlatformPaths.ConfigDirectory, "installers", "forge-0.4.0"); HellForgeGameplayCatalog.RejectLinks(cache); Directory.CreateDirectory(cache);
        using var lease = new FileStream(Path.Combine(cache, "setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var archive = Path.Combine(cache, Installer.Name);
        progress?.Report("Downloading and verifying official native Forge 0.4.0 setup…");
        await Pack.DownloadAsync(Installer, archive, null, ct);
        if (Game.IsRunning()) throw new InvalidOperationException("HD2 started during download. Close it; the verified installer is saved.");
        using var pinned = File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (pinned.Length != Installer.Size || await HellForgePack.HashAsync(archive, ct) != Installer.Sha256) throw new InvalidDataException("Native installer verification failed.");
        var program = await ExtractAsync(archive, Path.Combine(cache, "unpacked-" + Guid.NewGuid().ToString("N")), ct);
        progress?.Report(existing == null ? "In official setup, choose Proton and Install. Clonedivers will reconnect automatically." :
            "In official setup, keep your existing prefix: " + PrefixFor(existing) + ". The installer keeps your game, account and mods.");
        using var process = Process.Start(new ProcessStartInfo(program) { WorkingDirectory = Path.GetDirectoryName(program)!, UseShellExecute = false })
            ?? throw new IOException("Official native setup did not open.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new IOException("Official native setup ended with exit " + process.ExitCode + ". Existing profiles were not reset.");
        var root = Detect(existing);
        if (root == null) throw new IOException("Setup did not leave a discoverable installation. Use Locate to select the folder containing hellforge-install.json.");
        return root;
    }
    internal static string PrefixFor(string root)
    {
        for (var parent = new DirectoryInfo(Path.GetFullPath(root)); parent != null; parent = parent.Parent)
            if (parent.Name == "pfx" && parent.Parent is { } prefix) return prefix.FullName;
        throw new InvalidDataException("Choose an existing dedicated Forge prefix.");
    }
}
