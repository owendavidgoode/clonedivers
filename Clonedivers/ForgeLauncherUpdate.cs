using System.Security.Cryptography;

namespace Clonedivers;

/// <summary>Only the explicitly identified Forge launcher may replace this edition.</summary>
internal static class ForgeLauncherUpdate
{
    static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    internal static bool IsOffer(AppRelease? app, string current) => app is { Channel: "forge" }
        && System.Version.TryParse(app.Version, out var version) && version >= new System.Version(1, 10, 0)
        && app.CanSelfUpdate(null, current);

    internal static async Task<AppRelease?> CheckAsync(string current, CancellationToken ct,
        Func<CancellationToken, Task<Manifest>>? fetch = null)
    {
        var manifest = await (fetch ?? (token => Pack.FetchManifestAsync(null, token)))(ct);
        return IsOffer(manifest.App, current) ? manifest.App : null;
    }

    internal static bool IsForgeBuild(string? fileVersion) => System.Version.TryParse(fileVersion, out var version) && version >= new System.Version(1, 10, 0);

    // The old updater leaves .old.exe beside the new executable. Preserve its
    // exact bytes before the usual cleanup runs. Failure leaves .old.exe intact.
    // A Forge build replacing itself is not a migration: ordinary cleanup, no backup.
    internal static string? PreservePrevious(string executable, string? directory = null)
    {
        var previous = SelfUpdate.Paths(Path.GetFullPath(executable)).old;
        FileSafety.RejectLink(previous);
        if (!File.Exists(previous) || IsForgeBuild(System.Diagnostics.FileVersionInfo.GetVersionInfo(previous).FileVersion)) return null;
        if (new FileInfo(previous).Length > 512L * 1024 * 1024) throw new IOException("Previous launcher is unexpectedly large; its backup was left in place.");
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clonedivers", "launcher-backups");
        directory = Path.GetFullPath(directory);
        for (var parent = new DirectoryInfo(directory); parent != null; parent = parent.Parent) FileSafety.RejectLink(parent.FullName);
        Directory.CreateDirectory(directory);
        var hash = Hash(previous);
        var target = FileSafety.Child(directory, hash + ".exe");
        if (File.Exists(target))
        {
            if (Hash(target) != hash)
                throw new IOException("The retained launcher backup changed. The adjacent old executable was preserved.");
            return target;
        }
        var temporary = FileSafety.Child(directory, hash + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(previous, temporary, false);
            if (Hash(temporary) != hash)
                throw new IOException("Previous launcher changed during backup. Its adjacent copy was preserved.");
            File.Move(temporary, target, false);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
