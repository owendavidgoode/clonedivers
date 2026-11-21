using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Clonedivers;

public sealed record SelfInstallResult(
    string InstalledExePath,
    string? DesktopShortcutPath,
    string? StartMenuShortcutPath,
    bool ShortcutsCreated);

/// <summary>
/// Detects when Clonedivers.exe is launched directly from %USERPROFILE%\Downloads on Windows
/// and installs it cleanly to %LOCALAPPDATA%\Clonedivers\Clonedivers.exe with Desktop &amp; Start Menu .lnk shortcuts.
/// </summary>
public static class SelfInstallHelper
{
    static readonly Guid DownloadsKnownFolderGuid = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string GetTargetExePath(string? localAppData = null)
    {
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
        return Path.Combine(Path.GetFullPath(root), "Clonedivers", "Clonedivers.exe");
    }

    public static bool IsRunningFromDownloads(
        string? exePath = null,
        string? userProfile = null,
        string? localAppData = null,
        bool? isWindowsOverride = null)
    {
        bool isWindows = isWindowsOverride ?? OperatingSystem.IsWindows();
        if (!isWindows) return false;

        var candidateExe = string.IsNullOrWhiteSpace(exePath) ? Environment.ProcessPath : exePath;
        if (string.IsNullOrWhiteSpace(candidateExe)) return false;

        try
        {
            var fullExe = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateExe));
            var targetDir = Path.GetDirectoryName(GetTargetExePath(localAppData));
            if (!string.IsNullOrWhiteSpace(targetDir) && IsUnderDirectory(fullExe, targetDir))
                return false;

            foreach (var downloads in CandidateDownloadsFolders(userProfile))
            {
                if (IsUnderDirectory(fullExe, downloads))
                    return true;
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
        }

        return false;
    }

    public static SelfInstallResult InstallToLocalAppDataAndCreateShortcuts(
        string currentExePath,
        string? localAppData = null,
        string? desktopDir = null,
        string? startMenuProgramsDir = null,
        bool createShortcuts = true)
    {
        if (string.IsNullOrWhiteSpace(currentExePath))
            throw new ArgumentException("Current executable path is required.", nameof(currentExePath));

        var sourceExe = Path.GetFullPath(currentExePath);
        FileSafety.RejectLink(sourceExe);
        if (!File.Exists(sourceExe))
            throw new FileNotFoundException("Launcher executable was not found.", sourceExe);

        var targetExe = GetTargetExePath(localAppData);
        var targetDir = Path.GetDirectoryName(targetExe)!;
        for (var dir = new DirectoryInfo(targetDir); dir != null; dir = dir.Parent)
            FileSafety.RejectLink(dir.FullName);

        Directory.CreateDirectory(targetDir);
        FileSafety.RejectLink(targetExe);

        if (!string.Equals(sourceExe, targetExe, StringComparison.OrdinalIgnoreCase))
        {
            var expectedSize = new FileInfo(sourceExe).Length;
            if (expectedSize <= 0 || expectedSize > 512L * 1024 * 1024)
                throw new InvalidDataException("Unexpected launcher executable size.");

            string expectedSha;
            using (var srcStream = new FileStream(sourceExe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                expectedSha = Convert.ToHexString(SHA256.HashData(srcStream)).ToLowerInvariant();

            var tempTarget = FileSafety.Child(targetDir, $"Clonedivers.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var input = new FileStream(sourceExe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var output = new FileStream(tempTarget, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }

                string copiedSha;
                using (var checkStream = File.OpenRead(tempTarget))
                    copiedSha = Convert.ToHexString(SHA256.HashData(checkStream)).ToLowerInvariant();

                if (new FileInfo(tempTarget).Length != expectedSize || !string.Equals(copiedSha, expectedSha, StringComparison.Ordinal))
                    throw new IOException("Installed launcher copy failed SHA-256 verification.");

                FileSafety.RejectLink(targetExe);
                File.Move(tempTarget, targetExe, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempTarget)) File.Delete(tempTarget); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }

        string? desktopLnk = null;
        string? startMenuLnk = null;
        bool shortcutsOk = false;

        if (createShortcuts && OperatingSystem.IsWindows())
        {
            var resolvedDesktop = string.IsNullOrWhiteSpace(desktopDir)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : Path.GetFullPath(desktopDir);
            var resolvedPrograms = string.IsNullOrWhiteSpace(startMenuProgramsDir)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Programs)
                : Path.GetFullPath(startMenuProgramsDir);

            bool dOk = false, sOk = false;
            if (!string.IsNullOrWhiteSpace(resolvedDesktop))
            {
                Directory.CreateDirectory(resolvedDesktop);
                desktopLnk = FileSafety.Child(resolvedDesktop, "Clonedivers.lnk");
                dOk = TryCreateWindowsShortcut(desktopLnk, targetExe, targetDir, "Regular, Clones and Empire modes for HellForge");
            }

            if (!string.IsNullOrWhiteSpace(resolvedPrograms))
            {
                Directory.CreateDirectory(resolvedPrograms);
                startMenuLnk = FileSafety.Child(resolvedPrograms, "Clonedivers.lnk");
                sOk = TryCreateWindowsShortcut(startMenuLnk, targetExe, targetDir, "Regular, Clones and Empire modes for HellForge");
            }

            shortcutsOk = dOk && sOk;
        }

        return new SelfInstallResult(targetExe, desktopLnk, startMenuLnk, shortcutsOk);
    }

    public static bool TryCreateWindowsShortcut(string shortcutPath, string targetExePath, string workingDirectory, string description)
    {
        if (!OperatingSystem.IsWindows()) return false;
        FileSafety.RejectLink(shortcutPath);

        if (TryCreateShortcutViaWScriptCom(shortcutPath, targetExePath, workingDirectory, description) && File.Exists(shortcutPath))
            return true;

        return TryCreateShortcutViaPowerShell(shortcutPath, targetExePath, workingDirectory, description) && File.Exists(shortcutPath);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static bool TryCreateShortcutViaWScriptCom(string shortcutPath, string targetExePath, string workingDirectory, string description)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return false;

            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
            if (shortcut == null) return false;

            var scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetExePath]);
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [workingDirectory]);
            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [$"{targetExePath},0"]);
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, [description]);
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    static bool TryCreateShortcutViaPowerShell(string shortcutPath, string targetExePath, string workingDirectory, string description)
    {
        try
        {
            // Pass paths via environment variables — never interpolate paths into script text!
            const string script =
                "$s = (New-Object -ComObject WScript.Shell).CreateShortcut($env:CD_LNK_PATH); " +
                "$s.TargetPath = $env:CD_TARGET_EXE; " +
                "$s.WorkingDirectory = $env:CD_WORK_DIR; " +
                "$s.IconLocation = ($env:CD_TARGET_EXE + ',0'); " +
                "$s.Description = $env:CD_DESC; " +
                "$s.Save()";

            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            psi.Environment["CD_LNK_PATH"] = shortcutPath;
            psi.Environment["CD_TARGET_EXE"] = targetExePath;
            psi.Environment["CD_WORK_DIR"] = workingDirectory;
            psi.Environment["CD_DESC"] = description;

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            if (!proc.WaitForExit(8000))
            {
                try { proc.Kill(); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    static IEnumerable<string> CandidateDownloadsFolders(string? userProfileOverride)
    {
        var profile = string.IsNullOrWhiteSpace(userProfileOverride)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfileOverride;
        if (!string.IsNullOrWhiteSpace(profile))
            yield return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(profile, "Downloads")));

        if (userProfileOverride == null && OperatingSystem.IsWindows())
        {
            var known = TryGetKnownDownloadsFolder();
            if (!string.IsNullOrWhiteSpace(known))
                yield return Path.TrimEndingDirectorySeparator(Path.GetFullPath(known));
        }
    }

    static bool IsUnderDirectory(string filePath, string directoryPath)
    {
        var normDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var normFile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(filePath));
        return normFile.StartsWith(normDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normFile.StartsWith(normDir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static string? TryGetKnownDownloadsFolder()
    {
        if (!OperatingSystem.IsWindows()) return null;
        IntPtr ptr = IntPtr.Zero;
        try
        {
            var guid = DownloadsKnownFolderGuid;
            if (SHGetKnownFolderPath(ref guid, 0, IntPtr.Zero, out ptr) == 0 && ptr != IntPtr.Zero)
                return Marshal.PtrToStringUni(ptr);
        }
        catch { }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
        }
        return null;
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
