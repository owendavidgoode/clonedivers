using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clonedivers;

public sealed partial class HellForgeClient
{
    sealed record PackIntent(int Format, string Root, string Backup, string Mode, string BeforeSha256, string State);
    sealed record PackSnapshot(HellForgeMods Mods, HellForgeStatus Status, List<PackBackupFile> Files);
    static StringComparison RecoveryPathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal string PackIntentPath => Path.Combine(CacheRoot, "transactions", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        OperatingSystem.IsWindows() ? Root.ToUpperInvariant() : Root))).ToLowerInvariant() + ".json");

    static void RejectAncestors(string path)
    {
        FileSafety.RejectLink(path);
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)!); parent != null; parent = parent.Parent) FileSafety.RejectLink(parent.FullName);
    }
    FileStream LockPackChange()
    {
        var path = PackIntentPath + ".lock";
        RejectAncestors(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new IOException("Another launcher is changing or recovering this appearance. Wait for it to finish before retrying.", error); }
    }
    PackIntent? ReadPackIntent()
    {
        var path = PackIntentPath;
        RejectAncestors(path);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Appearance recovery record is oversized.");
        var intent = JsonSerializer.Deserialize<PackIntent>(File.ReadAllText(path), HellForgePack.Json) ?? throw new InvalidDataException("Appearance recovery record is empty.");
        if (intent.Format != 1 || !Root.Equals(intent.Root, RecoveryPathComparison) || intent.Mode is not ("regular" or "clonedivers" or "commandos" or "empire") ||
            intent.State is not ("pending" or "applied" or "restored") || intent.BeforeSha256.Length != 64 || !intent.BeforeSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Appearance recovery record does not match this installation or format.");
        var backup = Path.GetFullPath(intent.Backup);
        if (!backup.Equals(intent.Backup, RecoveryPathComparison) || !Path.Combine(CacheRoot, "backups").Equals(Path.GetDirectoryName(backup), RecoveryPathComparison))
            throw new InvalidDataException("Appearance recovery backup is outside this installation's cache.");
        RejectAncestors(Path.Combine(backup, "before.json"));
        return intent;
    }
    void WritePackIntent(PackIntent intent)
    {
        var path = PackIntentPath; var temp = path + ".tmp";
        RejectAncestors(path); RejectAncestors(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, intent, HellForgePack.Json);
            file.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }
    async Task<PackIntent> BeginPackIntentAsync(string backup, string mode, CancellationToken ct)
    {
        var intent = new PackIntent(1, Root, backup, mode, await HellForgePack.HashAsync(Path.Combine(backup, "before.json"), ct), "pending");
        ct.ThrowIfCancellationRequested();
        WritePackIntent(intent); // durable intent precedes the first official mutation
        return intent;
    }
    public string? InterruptedPackProblem()
    {
        try
        {
            return ReadPackIntent() is { State: "pending" } intent
                ? "An appearance change was interrupted. Use Recover previous appearance before preparing or launching. Backup: " + intent.Backup : null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
        { return "Appearance recovery needs review before preparing or launching: " + error.Message + " Record: " + PackIntentPath; }
    }
    void RequireNoInterruptedPack()
    {
        if (InterruptedGameplayProblem() is { } gameplay) throw new InvalidOperationException(gameplay);
        if (InterruptedPackProblem() is { } problem) throw new InvalidOperationException(problem);
    }

    internal Action RequireCliQuiescent { get; set; } = () =>
    {
        // A CLI orphaned by a crashed frontend can still be copying files. Never
        // recover over it or kill it blindly; all matching CLIs must finish first.
        foreach (var process in Process.GetProcessesByName("HellForge.Cli"))
        {
            using (process)
            {
                if (process.HasExited) continue;
                // Wine drive aliases and Windows short paths are not reliable installation
                // identities. Conservatively wait for every CLI rather than missing an orphan.
                throw new InvalidOperationException("A Forge CLI is still running. Wait for it to finish before changing or recovering mods.");
            }
        }
    };
    public async Task RecoverInterruptedPackAsync(CancellationToken ct = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromMinutes(10));
        ct = limit.Token;
        await RequireIdleAsync(ct);
        using var operation = LockPackChange();
        RequireCliQuiescent();
        var intent = ReadPackIntent() ?? throw new InvalidOperationException("There is no interrupted appearance change to recover.");
        if (intent.State != "pending") throw new InvalidOperationException("This appearance change already completed; refresh the launcher.");
        var beforePath = Path.Combine(intent.Backup, "before.json");
        if (!File.Exists(beforePath) || new FileInfo(beforePath).Length > 4 * 1024 * 1024 || await HellForgePack.HashAsync(beforePath, ct) != intent.BeforeSha256)
            throw new InvalidDataException("The saved appearance recovery metadata changed. No recovery was attempted.");
        var snapshot = JsonSerializer.Deserialize<PackSnapshot>(await File.ReadAllTextAsync(beforePath, ct), HellForgePack.Json)
            ?? throw new InvalidDataException("The saved appearance recovery metadata is empty.");
        if (!Root.Equals(snapshot.Status.Root, RecoveryPathComparison) || snapshot.Status.Build != HellForgePack.SupportedBuild || snapshot.Status.Running ||
            snapshot.Mods.Problems.Length != 0 || snapshot.Mods.Mods.Select(m => m.Id).Distinct().Count() != snapshot.Mods.Mods.Count || snapshot.Files.Count > 4096)
            throw new InvalidDataException("The saved appearance selection is not a valid idle snapshot of this installation.");
        if (snapshot.Files.Any(f => string.IsNullOrWhiteSpace(f.Relative) || Path.GetFileName(f.Relative) != f.Relative || f.Relative is "." or ".." ||
            f.Bytes < 0 || f.Sha256.Length != 64 || !f.Sha256.All(Uri.IsHexDigit)) || snapshot.Files.Select(f => f.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Files.Count)
            throw new InvalidDataException("The saved appearance file inventory is invalid.");
        var old = intent.Mode == "regular" ? null : snapshot.Mods.Mods.SingleOrDefault(m => m.Id == HellForgePack.PackId(intent.Mode));
        if (old != null)
        {
            ValidateModFolder(old.Folder);
            var source = Path.Combine(intent.Backup, "previous-pack");
            ValidateCosmeticBackup(source, old.Id);
            if (!await MatchesPackBackupAsync(source, snapshot.Files, ct)) throw new InvalidDataException("The saved appearance backup changed. No recovery was attempted.");
        }
        else if (snapshot.Files.Count != 0) throw new InvalidDataException("The saved appearance backup does not match its selection.");
        var current = await ModsAsync(ct);
        HellForgeMods Unrelated(HellForgeMods catalog) => catalog with { Mods = catalog.Mods.Where(m => !HellForgePack.IsOurPack(m.Id) && m.Id != DifficultyId).ToList() };
        if (current.Problems.Length != 0 || PackState(Unrelated(current)) != PackState(Unrelated(snapshot.Mods)))
            throw new InvalidDataException("Other gameplay mods changed after the interrupted appearance operation. Review the saved snapshot before recovering; those choices were not overwritten.");
        await RequireIdleAsync(ct);
        RequireCliQuiescent();
        await RestorePackChangeAsync(snapshot.Mods, snapshot.Status, intent.Mode, old, intent.Backup, snapshot.Files, ct);
        SavePackOutcome(intent.Backup, "restored");
        WritePackIntent(intent with { State = "restored" });
    }
}
