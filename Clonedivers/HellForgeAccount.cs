using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clonedivers;

public sealed record HellForgeAccountState(bool Captured, bool Imported, string Message);

/// <summary>One-way account import using the installed engine's sealed-snapshot verifier and profile commands.</summary>
public sealed class HellForgeAccount
{
    public const string Profile = "default";
    const string ReceiptName = "clonedivers-account-import.json";
    const string PendingName = "clonedivers-account-import.pending.json";
    sealed record Receipt(
        [property: JsonPropertyName("policy")] string Policy,
        [property: JsonPropertyName("capture")] string Capture,
        [property: JsonPropertyName("importedUtc")] DateTimeOffset ImportedUtc,
        [property: JsonPropertyName("account")] string? Account = null);
    sealed record Pending(string Phase, Receipt Receipt, string? BeforeProfileHash);
    sealed record VerifiedCapture(string Key, string Account, DateTimeOffset CapturedUtc);
    readonly string root, profile, snapshot;
    readonly Func<IReadOnlyList<string>, CancellationToken, Task<string>> backend;

    public static string? CaptureBlock()
    {
        var captures = OperatingSystem.IsLinux() ? Process.GetProcesses() : Process.GetProcessesByName("HellForge.CaptureProxy");
        try
        {
            foreach (var process in captures)
                try { if (!process.HasExited && (!OperatingSystem.IsLinux() || process.ProcessName.StartsWith("HellForge.Capt", StringComparison.Ordinal)))
                    return "Account sync is still running. Finish the normal HD2 login and let HellForge finish capture cleanup before launching."; }
                catch (InvalidOperationException) { }
        }
        finally { foreach (var process in captures) process.Dispose(); }
        var hosts = OperatingSystem.IsLinux() ? "/etc/hosts" : Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
        if (File.Exists(hosts) && File.ReadLines(hosts).Any(line => line.Contains("# HellForge-CaptureProxy", StringComparison.OrdinalIgnoreCase)))
            return "Account sync cleanup is pending. Open HellForge > Profile > Account sync > Clean up before launching.";
        return null;
    }

    public HellForgeAccount(string root, Func<IReadOnlyList<string>, CancellationToken, Task<string>>? backend = null)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        profile = FileSafety.Child(FileSafety.Child(this.root, "profiles"), Profile);
        snapshot = FileSafety.Child(FileSafety.Child(this.root, "account-snapshot"), Profile);
        this.backend = backend ?? RunBackendAsync;
    }

    internal string LeasePath
    {
        get
        {
            var parent = Path.GetDirectoryName(root) ?? throw new InvalidDataException("Choose a separate HellForge folder, not a drive root.");
            var identity = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + ":" + Profile))).ToLowerInvariant();
            return Path.Combine(parent, "Clonedivers-HellForge-cache", "accounts", key + ".lock");
        }
    }
    internal FileStream Lease(bool review = false)
    {
        var path = LeasePath;
        FileSafety.RejectLink(path);
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory != null; directory = directory.Parent) FileSafety.RejectLink(directory.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, review ? FileAccess.Read : FileAccess.ReadWrite, review ? FileShare.Read : FileShare.None); }
        catch (IOException error) { throw new IOException("Another launcher is reviewing, importing or launching this Forge account. Wait for it to finish; no account changes were started here.", error); }
    }

    string Checked(string folder, string name, long limit = 64 * 1024 * 1024)
    {
        var path = FileSafety.Child(folder, name);
        if (File.Exists(path) && new FileInfo(path).Length > limit) throw new InvalidDataException("Account import file is oversized.");
        return path;
    }

    string? CaptureKey()
    {
        var metadata = Checked(snapshot, "snapshot.json", 64 * 1024);
        var inventory = Checked(snapshot, "real-inventory.json");
        var progression = Checked(snapshot, "progression.json");
        var seal = Checked(snapshot, "seal.json", 64 * 1024);
        if (!new[] { metadata, inventory, progression, seal }.All(File.Exists)) return null;
        using var info = JsonDocument.Parse(File.ReadAllText(metadata));
        if (!info.RootElement.TryGetProperty("progression", out var flag) || flag.ValueKind != JsonValueKind.True) return null;
        // This identifies a capture; it never substitutes for the official backend's account/PC seal verification.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in new[] { inventory, progression, seal })
        {
            using var stream = File.OpenRead(path);
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    async Task<VerifiedCapture?> InspectAsync(CancellationToken ct)
    {
        Checked(profile, "profile.json");
        var key = CaptureKey();
        if (key == null) return null;
        // Verifies the seal against this PC and the currently signed-in Steam account. No local entitlement inference.
        using var preview = JsonDocument.Parse(await backend(["profile", "import-preview", profile, "starter"], ct));
        if (!preview.RootElement.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Object ||
            !categories.TryGetProperty("level", out var level) || !level.TryGetProperty("vanilla", out var experience) ||
            experience.ValueKind != JsonValueKind.String || experience.GetString() is null or "not captured")
            throw new InvalidDataException("HellForge returned an incomplete account review. Sync again to capture progression.");
        // Read stable identity only AFTER the official verifier accepted this PC/account's seal.
        using var seal = JsonDocument.Parse(File.ReadAllText(Checked(snapshot, "seal.json", 64 * 1024)));
        if (!seal.RootElement.TryGetProperty("steamAccount", out var owner) || owner.ValueKind != JsonValueKind.String ||
            !ulong.TryParse(owner.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0 ||
            !seal.RootElement.TryGetProperty("capturedUtc", out var captured) || !captured.TryGetDateTimeOffset(out var utc))
            throw new InvalidDataException("The verified account capture has incomplete identity metadata. Sync again.");
        if (CaptureKey() != key) throw new InvalidDataException("The account capture changed during verification. Retry after sync finishes.");
        var account = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Clonedivers account v1:" + id.ToString(CultureInfo.InvariantCulture)))).ToLowerInvariant();
        return new(key, account, utc);
    }

    Receipt? ReadReceipt()
    {
        var path = Checked(profile, ReceiptName, 64 * 1024);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path), HellForgePack.Json);
        if (receipt == null || !FileSafety.IsSha256(receipt.Capture) || receipt.ImportedUtc == default ||
            receipt.Policy is not ("one-way-v1" or "one-way-v2") ||
            receipt.Account != null && !FileSafety.IsSha256(receipt.Account) || receipt.Policy == "one-way-v2" && receipt.Account == null)
            throw new InvalidDataException("The account import receipt is incomplete. Restore its backup before continuing.");
        return receipt;
    }

    Pending? ReadPending()
    {
        var path = Checked(profile, PendingName, 64 * 1024);
        if (!File.Exists(path)) return null;
        var pending = JsonSerializer.Deserialize<Pending>(File.ReadAllText(path), HellForgePack.Json);
        if (pending?.Receipt == null || pending.Phase is not ("started" or "committed") ||
            !FileSafety.IsSha256(pending.Receipt.Capture) || !FileSafety.IsSha256(pending.Receipt.Account) || pending.Receipt.ImportedUtc == default)
            throw new InvalidDataException("The pending account import record is incomplete. Review HellForge's profile backups before continuing.");
        return pending;
    }

    DateTimeOffset? LatestRestore()
    {
        var folder = FileSafety.Child(profile, "backups");
        if (!Directory.Exists(folder)) return null;
        DateTimeOffset? latest = null;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*-restore.json"))
        {
            if (++count > 1024) throw new InvalidDataException("Too many profile restore records to review safely.");
            FileSafety.RejectLink(file);
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length == 27 && DateTimeOffset.TryParseExact(name[..19], "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc) && (latest == null || utc > latest)) latest = utc;
        }
        return latest;
    }
    DateTimeOffset? LatestNativeImport()
    {
        var folder = FileSafety.Child(profile, "backups"); if (!Directory.Exists(folder)) return null;
        DateTimeOffset? latest = null; int count = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*-import.json"))
        {
            if (++count > 1024) throw new InvalidDataException("Too many native import records to review safely.");
            FileSafety.RejectLink(file); var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length == 26 && DateTimeOffset.TryParseExact(name[..19], "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc) && (latest == null || utc > latest)) latest = utc;
        }
        return latest;
    }

    void RequireOwner(Receipt receipt, VerifiedCapture capture)
    {
        if (receipt.Account != null && receipt.Account != capture.Account)
            throw new InvalidDataException("This Forge character belongs to another Steam account. Switch back to its owner; no progress will be merged or replaced.");
        if (receipt.Account == null && receipt.Capture != capture.Key)
            throw new InvalidDataException("This older import receipt must be linked using its original capture before a new capture can be merged. Restore the matching receipt/snapshot backup; no profile was changed.");
        if (!File.Exists(Checked(profile, "profile.json")))
            throw new InvalidDataException("Your imported Forge character is missing. Restore its profile backup; a starter character will not be created over it.");
        if (LatestRestore() is { } restored && restored > receipt.ImportedUtc && capture.CapturedUtc <= restored)
            throw new InvalidDataException("The profile was restored after this account import. Sync your HD2 account again before applying an import; the old capture will not be replayed.");
    }

    bool RecoveredAfterRestore(Pending pending, VerifiedCapture capture) => LatestRestore() is { } restored &&
        restored > pending.Receipt.ImportedUtc && capture.CapturedUtc > restored;

    HellForgeAccountState State(VerifiedCapture capture, Receipt? receipt, Pending? pending)
    {
        if (receipt == null && pending == null && LatestNativeImport() is { } native && capture.CapturedUtc <= native)
            return new(false, false, "This character already has an official Forge import. Sync a fresh HD2 capture before linking it to Clonedivers; its earned Forge progress will be kept and an old capture will not be replayed.");
        if (receipt != null) RequireOwner(receipt, capture);
        if (pending != null)
        {
            RequireOwner(pending.Receipt, capture);
            if (!RecoveredAfterRestore(pending, capture))
            {
                if (pending.Receipt.Capture != capture.Key) throw new InvalidDataException("An unfinished account import refers to another capture. Restore a reviewed profile backup in HellForge, then capture your account again before importing.");
                if (pending.Phase == "started") throw new InvalidDataException("An earlier account import was interrupted. Restore a reviewed profile backup in HellForge, then sync your account again; the unfinished import will not be replayed automatically.");
                if (receipt != pending.Receipt)
                    return new(true, false, "Your account import committed, but its receipt was not saved. Apply HD2 import to finish bookkeeping without importing the account again.");
            }
        }
        if (receipt?.Capture == capture.Key)
            return new(true, true, "HD2 account imported. All three modes share your Forge character. Forge progress saves here; later HD2 syncs merge forward. Super Credits stay in HD2.");
        return new(true, false, "Your HD2 capture is ready. Apply the import to bring over your account; HellForge backs up the local character first.");
    }

    public async Task<HellForgeAccountState> ReadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var review = Lease(review: true);
        return await InspectAsync(ct) is { } capture ? State(capture, ReadReceipt(), ReadPending())
            : new(false, false, MissingCaptureMessage());
    }
    string MissingCaptureMessage()
    {
        var missing = new[] { "snapshot.json", "real-inventory.json", "progression.json", "seal.json" }
            .Where(name => !File.Exists(Checked(snapshot, name))).ToArray();
        return "No complete HD2 capture for profile default in " + root + ". " +
            (missing.Length == 0 ? "The capture does not include verified progression. " : "Missing capture files: " + string.Join(", ", missing) + ". ") +
            "Use Sync HD2 account, then HellForge > Profile > Account sync > Sync Inventory. Pass the HD2 title screen and close the game; Clonedivers will finish the requested import after cleanup.";
    }

    void WriteJson(string name, object value)
    {
        var path = Checked(profile, name, 64 * 1024);
        var temporary = Checked(profile, name + ".tmp", 64 * 1024);
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, HellForgePack.Json));
        File.Move(temporary, path, true);
    }

    public async Task BindLegacyReceiptAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var operation = Lease();
        if (await InspectAsync(ct) is not { } capture) return;
        var receipt = ReadReceipt();
        if (!State(capture, receipt, ReadPending()).Imported || receipt == null || receipt.Account != null) return;
        BindLegacy(receipt, capture);
    }
    void BindLegacy(Receipt receipt, VerifiedCapture capture)
    {
        var backup = Checked(profile, ReceiptName + ".v1.bak", 64 * 1024);
        if (!File.Exists(backup)) File.Copy(Checked(profile, ReceiptName, 64 * 1024), backup);
        // Extend the receipt without changing its import-policy tag, so the retained1.8.7 fallback still reads it.
        WriteJson(ReceiptName, receipt with { Account = capture.Account });
    }

    string? ProfileHash()
    {
        var path = Checked(profile, "profile.json");
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public async Task ImportAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var operation = Lease();
        var capture = await InspectAsync(ct) ?? throw new InvalidOperationException("Sync your complete HD2 account before importing.");
        var receiptBefore = ReadReceipt();
        var pendingBefore = ReadPending();
        var state = State(capture, receiptBefore, pendingBefore);
        if (!state.Captured) throw new InvalidOperationException(state.Message);
        if (state.Imported)
        {
            if (receiptBefore?.Account == null && receiptBefore != null) BindLegacy(receiptBefore, capture);
            return; // Never reapply a stale capture over currencies spent in Forge.
        }
        if (pendingBefore?.Phase == "committed" && !RecoveredAfterRestore(pendingBefore, capture))
        {
            WriteJson(ReceiptName, pendingBefore.Receipt);
            File.Delete(Checked(profile, PendingName, 64 * 1024));
            return;
        }
        var key = capture.Key;
        var settings = Checked(profile, "account-import.json", 64 * 1024);
        var first = receiptBefore == null;
        var adoptingNative = first && LatestNativeImport() != null;
        // Initial import takes account values; later captures preserve earned Forge progress. No unlock-all choice.
        var categories = new Dictionary<string, object>();
        foreach (var (id, rule) in new[] {
            ("level", first && !adoptingNative ? "Vanilla" : "Higher"), ("warbondItems", "Merge"),
            ("armory", first && !adoptingNative ? "Vanilla" : "Merge"), ("packages", first && !adoptingNative ? "Vanilla" : "Higher"),
            ("currencies", first && !adoptingNative ? "Vanilla" : "Higher"), ("ship", "Vanilla"), ("onboarding", "Vanilla") })
            categories[id] = new { include = first && !adoptingNative || id is not ("ship" or "onboarding"), rule };
        var temporary = Checked(profile, "account-import.json.tmp", 64 * 1024);
        var backups = FileSafety.Child(profile, "backups");
        if (Directory.Exists(backups)) foreach (var file in Directory.EnumerateFileSystemEntries(backups)) FileSafety.RejectLink(file);
        var prior = File.Exists(settings) ? File.ReadAllText(settings) : null;
        var beforeHash = ProfileHash();
        var nextReceipt = new Receipt("one-way-v1", key, DateTimeOffset.UtcNow, capture.Account);
        var pending = new Pending("started", nextReceipt, beforeHash);
        bool backendSucceeded = false, journalStarted = false;
        try
        {
            WriteJson(PendingName, pending);
            journalStarted = true;
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { autoApply = false, categories }, HellForgePack.Json));
            File.Move(temporary, settings, true);
            ct.ThrowIfCancellationRequested();
            if (CaptureKey() != key) throw new InvalidDataException("The capture changed before import. Retry after sync finishes.");
            await backend(["profile", "import", profile, "starter"], ct);
            backendSucceeded = true;
            WriteJson(PendingName, pending with { Phase = "committed" });
            if (CaptureKey() != key) throw new InvalidDataException("The account capture changed during import. Review it before applying again.");
            WriteJson(ReceiptName, nextReceipt);
            File.Delete(Checked(profile, PendingName, 64 * 1024));
        }
        catch
        {
            // Do not undo policy or replay a capture after a commit succeeded, even if bookkeeping failed.
            if (!backendSucceeded && ProfileHash() == beforeHash)
            {
                if (prior != null) { File.WriteAllText(temporary, prior); File.Move(temporary, settings, true); }
                else if (File.Exists(settings)) File.Delete(settings);
                if (journalStarted) File.Delete(Checked(profile, PendingName, 64 * 1024));
            }
            throw;
        }
    }

    async Task<string> RunBackendAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var app = FileSafety.Child(root, "app");
        var folder = FileSafety.Child(app, OperatingSystem.IsLinux() ? "backend-linux" : "backend");
        var exe = FileSafety.Child(folder, OperatingSystem.IsLinux() ? "HellForge.LocalBackend" : "HellForge.LocalBackend.exe");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return await RunBackendProcessAsync(start, args.Count > 1 && args[1] == "import", ct);
    }

    internal static async Task<string> RunBackendProcessAsync(ProcessStartInfo start, bool committing, CancellationToken ct,
        TimeSpan? previewTimeout = null)
    {
        ct.ThrowIfCancellationRequested();
        using var preview = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!committing) preview.CancelAfter(previewTimeout ?? TimeSpan.FromSeconds(30));
        using var process = Process.Start(start) ?? throw new IOException("HellForge's account importer did not start.");
        using var pipes = new CancellationTokenSource(); // import cancellation must never cancel the committing backend's readers
        var incomplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = CollectBackendTextAsync(process.StandardOutput, 1024 * 1024, incomplete, pipes.Token);
        var errors = CollectBackendTextAsync(process.StandardError, 64 * 1024, incomplete, pipes.Token);
        // Once an import starts, allow its atomic commit/backup to finish even if the window is closing.
        try
        {
            var exited = process.WaitForExitAsync(committing ? CancellationToken.None : preview.Token);
            if (!committing && await Task.WhenAny(exited, incomplete.Task) == incomplete.Task)
                throw new InvalidDataException("HellForge's account review is oversized or incomplete. No account import was started.");
            await exited;
            // Exit establishes the import outcome; inherited diagnostic pipes must
            // not make a successful commit wait for another process's lifetime.
            pipes.CancelAfter(TimeSpan.FromSeconds(2));
            var text = await output; var error = await errors;
            if (process.ExitCode != 0) throw new InvalidDataException(error.Text.Trim().Length > 0 ? error.Text.Trim() : "HellForge refused the account snapshot.");
            if (!committing && (text.Incomplete || error.Incomplete))
                throw new InvalidDataException("HellForge's account review is oversized or incomplete. No account import was started.");
            // ImportAsync uses the verified process exit, not this diagnostic body.
            // Do not turn an exit0 commit into an unknown import just for noisy logs.
            return text.Text + (text.Incomplete || error.Incomplete ? "\n[Backend diagnostics truncated after successful import.]" : "");
        }
        catch (OperationCanceledException) when (!committing)
        {
            // This process performs only a read-only preview, never a capture, game session or profile commit.
            await HellForgeCommands.StopOwnedCliAsync(process);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("HellForge's account review timed out. No account import was started. Retry or open Setup & mods to inspect HellForge.");
        }
        catch when (!committing) { await HellForgeCommands.StopOwnedCliAsync(process); throw; }
        finally
        {
            pipes.Cancel();
            await Task.WhenAll(output, errors);
        }
    }
    sealed record BackendText(string Text, bool Incomplete);
    static async Task<BackendText> CollectBackendTextAsync(TextReader reader, int limit, TaskCompletionSource incomplete, CancellationToken ct)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; bool truncated = false;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
            {
                var take = Math.Min(count, limit - text.Length);
                text.Append(buffer, 0, take);
                if (take != count) { truncated = true; incomplete.TrySetResult(); }
                // Continue draining excess output so a committing importer cannot
                // block on a full pipe, and never retain the discarded suffix.
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException || error is OperationCanceledException && ct.IsCancellationRequested)
        { truncated = true; incomplete.TrySetResult(); }
        return new(text.ToString(), truncated);
    }
}
