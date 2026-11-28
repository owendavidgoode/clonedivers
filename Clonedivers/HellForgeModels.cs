using System.Text.Json;

namespace Clonedivers;

public static class HellForgeRelease
{
    public const string Version = "0.5.0-beta";
    public const string Engine = "0.1.58";
    public const int Sdk = 20;
    public static bool Supports(string? value) => value != null && System.Text.RegularExpressions.Regex.IsMatch(value, @"^0\.5\.0-beta(?: \([0-9a-f]{7,40}\))?$");
    public static bool IsValidWarMode(string? value) => value is "hellforge" or "open" or "live" or "custom";

    /// <summary>
    /// Mirrors HellForge.Core.BundledMods.Sync for shipped files in &lt;root&gt;/app/bundled-mods/&lt;name&gt; into &lt;root&gt;/mods/&lt;name&gt;
    /// so HellForge.Cli mods queries immediately see the unpacked release's bundled mods without waiting for HellForge.exe or mods-stage.
    /// </summary>
    public static void SyncShippedBundledMods(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        var fullRoot = Path.GetFullPath(root);
        var source = Path.Combine(fullRoot, "app", "bundled-mods");
        if (!Directory.Exists(source)) return;
        HellForgeGameplayCatalog.RejectLinks(source);
        var modsRoot = Path.Combine(fullRoot, "mods");
        HellForgeGameplayCatalog.RejectLinks(modsRoot);
        Directory.CreateDirectory(modsRoot);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var shipped in Directory.GetDirectories(source).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            FileSafety.RejectLink(shipped);
            var name = Path.GetFileName(shipped);
            if (string.IsNullOrEmpty(name)) continue;
            var target = Path.GetFullPath(Path.Combine(modsRoot, name));
            if (!target.StartsWith(modsRoot + Path.DirectorySeparatorChar, pathComparison)) continue;
            HellForgeGameplayCatalog.RejectLinks(target);
            if (Directory.Exists(Path.Combine(target, "src"))) continue;

            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(shipped);
            while (pending.TryPop(out var currentDir))
            {
                FileSafety.RejectLink(currentDir);
                foreach (var entry in Directory.EnumerateFileSystemEntries(currentDir))
                {
                    FileSafety.RejectLink(entry);
                    if (Directory.Exists(entry)) pending.Push(entry);
                    else files.Add(Path.GetRelativePath(shipped, entry).Replace('\\', '/'));
                }
            }
            files.Sort(StringComparer.Ordinal);

            foreach (var relative in files)
            {
                var srcFile = Path.Combine(shipped, relative.Replace('/', Path.DirectorySeparatorChar));
                var dstFile = Path.GetFullPath(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!dstFile.StartsWith(target + Path.DirectorySeparatorChar, pathComparison))
                    throw new InvalidDataException("Bundled mod path escapes target folder.");
                HellForgeGameplayCatalog.RejectLinks(dstFile);
                if (File.Exists(dstFile) && SameShippedBytes(srcFile, dstFile)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                File.Copy(srcFile, dstFile, overwrite: true);
            }

            var listFile = Path.Combine(target, ".hellforge-bundled.txt");
            HellForgeGameplayCatalog.RejectLinks(listFile);
            var before = File.Exists(listFile)
                ? File.ReadAllLines(listFile).Where(l => l.Length > 0).ToList()
                : [];
            foreach (var staleRel in before.Except(files, StringComparer.OrdinalIgnoreCase))
            {
                if (Path.IsPathFullyQualified(staleRel) || staleRel.Split('/', '\\').Any(p => p is ".." || p.Contains(':'))) continue;
                var stalePath = Path.GetFullPath(Path.Combine(target, staleRel.Replace('/', Path.DirectorySeparatorChar)));
                if (!stalePath.StartsWith(target + Path.DirectorySeparatorChar, pathComparison) || !File.Exists(stalePath)) continue;
                HellForgeGameplayCatalog.RejectLinks(stalePath);
                File.Delete(stalePath);
            }
            if (!File.Exists(listFile) || !before.SequenceEqual(files, StringComparer.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(target);
                File.WriteAllLines(listFile, files);
            }
        }
        PatchLinuxInnerWrapper(fullRoot);
    }

    const string UnpatchedLinuxInnerAnchor =
        "while [[ \"$parent\" =~ ^[0-9]+$ ]] && [ \"$parent\" -gt 1 ]; do\n" +
        "    wrapper_pids+=\" $parent\"\n" +
        "    parent=\"$(awk '/^PPid:/ { print $2 }' \"/proc/$parent/status\" 2>/dev/null || true)\"\n" +
        "done\n\n" +
        "of_this_install() {\n" +
        "    local pid=\"$1\" text exe cwd\n" +
        "    for w in $wrapper_pids; do [ \"$pid\" = \"$w\" ] && return 1; done\n" +
        "    text=\"$({ tr '\\0' '\\n' < \"/proc/$pid/cmdline\"; } 2>/dev/null || true)\"";

    const string PatchedLinuxInnerReplacement =
        "while [[ \"$parent\" =~ ^[0-9]+$ ]] && [ \"$parent\" -gt 1 ]; do\n" +
        "    wrapper_pids+=\" $parent\"\n" +
        "    parent=\"$(awk '/^PPid:/ { print $2 }' \"/proc/$parent/status\" 2>/dev/null || true)\"\n" +
        "done\n" +
        "self_cmdline=\"$({ tr '\\0' '\\n' < \"/proc/$$/cmdline\"; } 2>/dev/null || true)\"\n\n" +
        "of_this_install() {\n" +
        "    local pid=\"$1\" text exe cwd\n" +
        "    for w in $wrapper_pids; do [ \"$pid\" = \"$w\" ] && return 1; done\n" +
        "    text=\"$({ tr '\\0' '\\n' < \"/proc/$pid/cmdline\"; } 2>/dev/null || true)\"\n" +
        "    [ -n \"$self_cmdline\" ] && [ \"$text\" = \"$self_cmdline\" ] && return 1";

    /// <summary>
    /// Idempotently patches deployed &lt;root&gt;/app/linux/hf-inner.sh (and &lt;root&gt;/hf-inner.sh if present)
    /// with the Issue #44 self_cmdline command-substitution subshell exclusion guard so post-crash cleanup
    /// never deadlocks matching its own $(all_ gone) subshell. Leaves future upstream fixes untouched.
    /// </summary>
    public static int PatchLinuxInnerWrapper(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return 0;
        var fullRoot = Path.GetFullPath(root);
        int patched = 0;
        foreach (var relative in new[] { Path.Combine("app", "linux", "hf-inner.sh"), "hf-inner.sh" })
        {
            var candidate = Path.Combine(fullRoot, relative);
            if (PatchLinuxInnerScriptFile(candidate))
                patched++;
        }
        return patched;
    }

    public static bool PatchLinuxInnerScriptFile(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath)) return false;
        var full = Path.GetFullPath(scriptPath);
        HellForgeGameplayCatalog.RejectLinks(full);
        if (!File.Exists(full)) return false;
        var info = new FileInfo(full);
        if (info.Length <= 0 || info.Length > 512 * 1024) return false;

        var raw = File.ReadAllText(full, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        // Already patched by Clonedivers or fixed upstream (e.g. self_cmdline or BASHPID).
        if (raw.Contains("self_cmdline", StringComparison.Ordinal) || raw.Contains("BASHPID", StringComparison.Ordinal))
            return false;

        var lf = raw.Replace("\r\n", "\n", StringComparison.Ordinal);
        int idx = lf.IndexOf(UnpatchedLinuxInnerAnchor, StringComparison.Ordinal);
        if (idx < 0)
            return false;

        var updated = string.Concat(lf.AsSpan(0, idx), PatchedLinuxInnerReplacement, lf.AsSpan(idx + UnpatchedLinuxInnerAnchor.Length));
        var temp = full + ".clonedivers-" + Guid.NewGuid().ToString("N") + ".tmp";
        HellForgeGameplayCatalog.RejectLinks(temp);
        UnixFileMode? existingMode = null;
        if (OperatingSystem.IsLinux())
        {
            try { existingMode = File.GetUnixFileMode(full); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        try
        {
            File.WriteAllBytes(temp, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(updated));
            if (OperatingSystem.IsLinux())
            {
                var mode = existingMode ?? (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                File.SetUnixFileMode(temp, mode | UnixFileMode.UserExecute);
            }
            File.Move(temp, full, overwrite: true);
            return true;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    static bool SameShippedBytes(string a, string b)
    {
        var x = new FileInfo(a);
        var y = new FileInfo(b);
        if (x.Length != y.Length) return false;
        using var first = x.OpenRead();
        using var second = y.OpenRead();
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Security.Cryptography.SHA256.HashData(first),
            System.Security.Cryptography.SHA256.HashData(second));
    }
}

public sealed record HellForgeStatus(string Root, string Version, string Build, bool GameCopyReady, bool RunnerReady,
    bool Running, bool Multiplayer, bool LocalBackend, string? LastStatus, string? LastFailure, string? LastNotice = null, string? RecoveryProblem = null, string? CompatibilityProblem = null,
    string? Engine = null, int? ModSdk = null, bool HasRun = true, bool? GameLaunched = null, string? GalacticWar = null);
public sealed record HellForgeOption(string Name, bool On);
public sealed record HellForgeMod(string Id, string Name, bool Enabled, string Folder, string[] Tags, string Version = "", HellForgeOption[]? Options = null, string? Archive = null);
public sealed record HellForgeMods(List<HellForgeMod> Mods, string[] Problems, string? Code, string? HostingProblem = null);
public sealed class HellForgeCommandOutcomeUnknownException(string message, Exception? inner = null) : IOException(message, inner);
