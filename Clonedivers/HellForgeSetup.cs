using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Clonedivers;

internal sealed record ForgeSetupProgress(string Message, long Done = 0, long Total = 0, bool CanCancel = false);

/// <summary>Bootstrap the pinned author installer. Game copying, ownership and isolation remain official Forge setup.</summary>
internal static partial class HellForgeSetup
{
    internal const string InstallerUrl = "https://github.com/SkyeShade/HellForge/releases/download/v0.4.0-beta/HellForge-Setup.exe";
    internal const string InstallerSha256 = "97dbdd07638e5198b98bf2ad74a1ba907c53f569fcdcdcff330e6c74359f04d7";
    internal const long InstallerBytes = 526467508;
    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "HellForge");
    internal static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clonedivers", "installers");
    internal static PackFile Installer => new() { Name = "HellForge-Setup.exe", Url = InstallerUrl, Size = InstallerBytes, Sha256 = InstallerSha256 };
    internal static bool SupportsVersion(string version) => HellForgeRelease.Supports(version);

    internal static string ValidateTarget(string value, IEnumerable<string>? cloudRoots = null)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) throw new InvalidDataException("Choose a full local folder path for Forge.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || Path.GetDirectoryName(root) is null)
            throw new InvalidDataException("Choose a folder on a local drive, outside Steam and OneDrive.");
        for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent)
        {
            FileSafety.RejectLink(directory.FullName);
            if (directory.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) || directory.Name.Equals("OneDrive", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Forge needs its own folder outside Steam and OneDrive.");
            if (File.Exists(Path.Combine(directory.FullName, "hellforge-install.json")))
                throw new InvalidDataException("Forge is already installed at or above this folder. Use existing Forge instead of installing another copy inside it.");
        }
        cloudRoots ??= new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }.Select(Environment.GetEnvironmentVariable).OfType<string>();
        if (cloudRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Any(r => Within(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))))
            throw new InvalidDataException("Choose a Forge folder outside your cloud-synced folders.");
        if (File.Exists(root)) throw new InvalidDataException("That location is a file. Choose a folder for Forge.");
        return root;
    }

    static bool Within(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static void RequireNewTarget(string root)
    {
        ValidateTarget(root);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException("That folder already contains files. Use the existing Forge installation or choose an empty folder; setup will not replace it.");
    }

    internal static void RequireSpace(string root)
    {
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.IsReady && drive.AvailableFreeSpace < 50L * 1024 * 1024 * 1024)
            throw new IOException($"Drive {drive.Name} has {Pack.FormatBytes(drive.AvailableFreeSpace)} free. Allow at least 50 GB for Forge setup, plus space for appearance packs. Choose another drive or free space first.");
    }

    internal static async Task<string> DownloadAsync(string cache, IProgress<ForgeSetupProgress>? progress, CancellationToken ct, PackFile? artifact = null)
    {
        artifact ??= Installer;
        FileSafety.RejectLink(cache); Directory.CreateDirectory(cache);
        var path = FileSafety.Child(cache, "HellForge-Setup-" + artifact.Sha256 + ".exe");
        progress?.Report(new("Downloading Forge…", CanCancel: true));
        await Pack.DownloadAsync(artifact, path, new DownloadProgress(progress), ct);
        return path;
    }

    sealed class DownloadProgress(IProgress<ForgeSetupProgress>? progress) : IProgress<(long done, long total)>
    {
        public void Report((long done, long total) p) => progress?.Report(new("Downloading Forge…", p.done, p.total, true));
    }

    internal static async Task<string> InstallAsync(string target, IProgress<ForgeSetupProgress>? progress, CancellationToken ct,
        string? cache = null, PackFile? artifact = null, Func<string, string, Task>? unpack = null, Action<string>? spaceCheck = null)
    {
        target = ValidateTarget(target); RequireNewTarget(target);
        if (Game.IsRunning()) throw new InvalidOperationException("Close HD2 before installing Forge. Your current session has not been changed.");
        (spaceCheck ?? RequireSpace)(target);
        cache ??= CacheRoot; artifact ??= Installer;
        FileSafety.RejectLink(cache); Directory.CreateDirectory(cache);
        using var setupLock = new FileStream(FileSafety.Child(cache, "forge-setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var installer = await DownloadAsync(cache, progress, ct, artifact);
        ct.ThrowIfCancellationRequested();
        if (Game.IsRunning()) throw new InvalidOperationException("HD2 started while downloading. Close it, then retry; the verified installer is saved.");
        RequireNewTarget(target);
        // Lock verified executable bytes against replacement through process exit.
        using var pinned = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (pinned.Length != artifact.Size || !Convert.ToHexString(await SHA256.HashDataAsync(pinned, ct)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Forge installer verification failed. It was not started.");
        ct.ThrowIfCancellationRequested();
        var parent = Path.GetDirectoryName(target)!;
        FileSafety.RejectLink(parent); Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, Path.GetFileName(target) + ".clonedivers-install-" + Guid.NewGuid().ToString("N"));
        progress?.Report(new("Installing Forge… Please wait for this step to finish."));
        try
        {
            // Official --unpack does not open a window, copy a game, change networking or authenticate.
            await (unpack ?? UnpackAsync)(installer, staging);
            HellForgeCommands.ValidateRoot(staging);
            using (var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(staging, "hellforge-install.json"))))
                if (marker.RootElement.GetProperty("buildId").GetString() != HellForgePack.SupportedBuild)
                    throw new InvalidDataException("Installer produced a different game build. Existing installations were not changed.");
            RequireNewTarget(target);
            if (Directory.Exists(target)) Directory.Delete(target); // only a rechecked empty chosen folder; never recursive
            Directory.Move(staging, target);
        }
        catch (Exception error)
        {
            throw new IOException("Forge installation did not finish. Your existing installations were not replaced. Retry setup; any unfinished files are retained at " + staging + ". " + error.Message, error);
        }
        progress?.Report(new("Forge installed. Continue with its game setup."));
        return HellForgeCommands.ValidateRoot(target);
    }

    internal static ProcessStartInfo UnpackStartInfo(string installer, string folder) => new(installer)
    {
        UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(installer)!,
        RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "--unpack", folder }
    };

    static async Task UnpackAsync(string installer, string folder)
    {
        using var process = Process.Start(UnpackStartInfo(installer, folder)) ?? throw new IOException("Official Forge installer did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var output = HellForgeCommands.ReadBoundedOutputAsync(process.StandardOutput, 16384, false, timeout.Token);
        var error = HellForgeCommands.ReadBoundedOutputAsync(process.StandardError, 16384, false, timeout.Token);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Observe(Task task) { try { await task; } catch (Exception e) { failed.TrySetResult(e); } }
        var readOut = Observe(output); var readError = Observe(error);
        try
        {
            var exited = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(exited, failed.Task) == failed.Task) throw await failed.Task;
            await exited; await output; var why = await error;
            if (process.ExitCode != 0) throw new IOException("Official installer failed (exit " + process.ExitCode + "). " + why.Trim());
        }
        catch { await HellForgeCommands.StopOwnedCliAsync(process); throw; }
        finally { timeout.Cancel(); await Task.WhenAll(readOut, readError); }
    }
}
