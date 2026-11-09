using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clonedivers;

/// <summary>Opens only sealed-account-owned premium Warbonds under the backend's profile lease.</summary>
public sealed class HellForgeWarbonds
{
    const uint FreeWarbond = 1929468580;
    static readonly string[] SnapshotFiles = ["snapshot.json", "real-inventory.json", "progression.json", "seal.json"];
    readonly string root, profile, snapshot;
    readonly IHellForgeCommands commands;
    readonly HellForgeAccount account;
    readonly Func<bool> processesRunning;

    public HellForgeWarbonds(string root) : this(root, new HellForgeCommands(root)) { }
    internal HellForgeWarbonds(string root, IHellForgeCommands commands,
        Func<IReadOnlyList<string>, CancellationToken, Task<string>>? backend = null, Func<bool>? processesRunning = null)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); this.commands = commands;
        for (var directory = new DirectoryInfo(this.root); directory != null; directory = directory.Parent) FileSafety.RejectLink(directory.FullName);
        profile = FileSafety.Child(FileSafety.Child(this.root, "profiles"), HellForgeAccount.Profile);
        snapshot = FileSafety.Child(FileSafety.Child(this.root, "account-snapshot"), HellForgeAccount.Profile);
        account = new HellForgeAccount(this.root, backend);
        this.processesRunning = processesRunning ?? ProcessesRunning;
    }
    static bool ProcessesRunning()
    {
        if (OperatingSystem.IsLinux())
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                    try { if (process.Id != Environment.ProcessId && BlocksLinuxProcess(process.ProcessName)) return true; }
                    catch (InvalidOperationException) { } // exited during enumeration
                    catch (System.ComponentModel.Win32Exception) { return true; } // an unreadable process cannot establish idle
                return false;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        foreach (var name in new[] { "helldivers2", "HellForge" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length != 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }
    // Linux comm may truncate the native launcher's name to 15 characters; Proton keeps the game's .exe suffix.
    internal static bool BlocksLinuxProcess(string name) => name is "helldivers2" or "helldivers2.exe" or "HellForge" or "HellForge.exe" ||
        name.StartsWith("HellForge.Launc", StringComparison.Ordinal);
    async Task RequireIdleAsync(CancellationToken ct)
    {
        if (processesRunning()) throw new InvalidOperationException("Close the game and HellForge window before opening owned Warbonds.");
        var status = await commands.RunAsync(["status"], ct);
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(status.GetProperty("install").GetString()!)).Equals(root, PlatformPaths.Comparison) ||
            status.GetProperty("session").GetProperty("running").ValueKind != JsonValueKind.False)
            throw new InvalidOperationException("HellForge must report an idle session before opening owned Warbonds.");
    }
    static byte[] Read(string path)
    {
        FileSafety.RejectLink(path);
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Warbond input file is oversized.");
        return File.ReadAllBytes(path);
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public async Task<int> OpenAsync(CancellationToken ct = default)
    {
        await RequireIdleAsync(ct);
        using var operation = account.Lease(); // excludes this launcher's imports, reviews and launches
        var path = FileSafety.Child(profile, "profile.json");
        var lockPath = FileSafety.Child(profile, "profile.lock");
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var original = Read(path);
        var captured = SnapshotFiles.ToDictionary(name => name, name => Read(FileSafety.Child(snapshot, name)));
        using var metadata = JsonDocument.Parse(captured["snapshot.json"]);
        using var seal = JsonDocument.Parse(captured["seal.json"]);
        if (metadata.RootElement.GetProperty("progression").ValueKind != JsonValueKind.True ||
            !Hash(captured["real-inventory.json"]).Equals(seal.RootElement.GetProperty("inventorySha256").GetString(), StringComparison.OrdinalIgnoreCase) ||
            !Hash(captured["progression.json"]).Equals(seal.RootElement.GetProperty("progressionSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The account snapshot is incomplete or corrupt. Sync your HD2 account again.");
        // The official verifier checks the machine seal and signed-in Steam account, and validates the saved profile.
        await account.VerifySnapshotAsync(ct);
        var dataPath = FileSafety.Child(FileSafety.Child(root, "app"), "progression-data.json");
        var dataBytes = Read(dataPath);
        using var data = JsonDocument.Parse(dataBytes);
        using var inventory = JsonDocument.Parse(captured["real-inventory.json"]);
        var updated = JsonNode.Parse(original) as JsonObject ?? throw new InvalidDataException("Invalid Forge profile.");
        var added = AddOwned(updated, inventory.RootElement, data.RootElement);
        if (added == 0) return 0;
        await RequireIdleAsync(ct);
        if (!Read(path).AsSpan().SequenceEqual(original) || !Read(dataPath).AsSpan().SequenceEqual(dataBytes) ||
            captured.Any(file => !Read(FileSafety.Child(snapshot, file.Key)).AsSpan().SequenceEqual(file.Value)))
            throw new InvalidDataException("The profile or account snapshot changed during Warbond verification. Retry after sync finishes.");
        ct.ThrowIfCancellationRequested();
        var backups = FileSafety.Child(profile, "backups"); Directory.CreateDirectory(backups);
        var backup = FileSafety.Child(backups, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N") + "-warbonds.json");
        File.WriteAllBytes(backup, original);
        File.WriteAllText(backup + ".sha256", Hash(original) + "  " + Path.GetFileName(backup) + "\n");
        if (Hash(Read(backup)) != Hash(original)) throw new IOException("Warbond backup verification failed.");
        var temporary = FileSafety.Child(profile, "profile.json." + Guid.NewGuid().ToString("N") + ".tmp");
        var fallback = FileSafety.Child(profile, "profile.json.bak");
        var payload = JsonSerializer.SerializeToUtf8Bytes(updated, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(payload); stream.Flush(true); }
            File.Replace(temporary, path, fallback, ignoreMetadataErrors: true);
            if (!Read(path).AsSpan().SequenceEqual(payload) || !JsonNode.DeepEquals(JsonNode.Parse(Read(path)), updated))
                throw new IOException("Saved Warbond profile verification failed. Backup saved at " + backup);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return added;
    }

    static Dictionary<uint, JsonObject> Items(JsonNode? section)
    {
        if (section is not JsonObject obj || obj["items"] is not JsonArray items) throw new InvalidDataException("Inventory has no items array.");
        var result = new Dictionary<uint, JsonObject>();
        foreach (var item in items)
        {
            if (item is not JsonObject entry || entry["isDiscovered"] is not JsonValue discovery || discovery.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Invalid inventory entry.");
            var id = Number(entry["mixId"]); _ = Number(entry["amount"]);
            if (!result.TryAdd(id, entry)) throw new InvalidDataException("Duplicate inventory mixId.");
        }
        return result;
    }
    static uint Number(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number &&
        uint.TryParse(value.ToJsonString(), out var number) ? number : throw new InvalidDataException("Invalid inventory integer.");

    internal static int AddOwned(JsonObject profile, JsonElement inventory, JsonElement data)
    {
        if (Number(profile["schemaVersion"]) != 1) throw new InvalidDataException("Unsupported Forge profile schema.");
        var held = Items(profile["inventory"]);
        var owned = Items(JsonNode.Parse(inventory.GetRawText()));
        var passes = data.GetProperty("seasonPassDetails");
        if (passes.ValueKind != JsonValueKind.Object || !passes.EnumerateObject().Any()) throw new InvalidDataException("Missing Warbond catalogue.");
        int added = 0;
        foreach (var pass in passes.EnumerateObject().Select(p => p.Value))
        {
            if (pass.GetProperty("id32").GetUInt32() == FreeWarbond) continue;
            var tokens = pass.GetProperty("premiumTokenMixIds").EnumerateArray().Select(t => t.GetUInt32()).ToArray();
            if (tokens.Any(t => held.TryGetValue(t, out var item) && Number(item["amount"]) > 0)) continue;
            var ownedTokens = tokens.Where(t => owned.TryGetValue(t, out var item) && Number(item["amount"]) > 0).ToArray();
            if (ownedTokens.Length == 0) continue;
            var token = ownedTokens[0];
            // ProfileStore.AddItem preserves discovery on an existing zero-amount entry.
            if (held.TryGetValue(token, out var existing)) existing["amount"] = 1;
            else
            {
                var entry = new JsonObject { ["mixId"] = token, ["amount"] = 1, ["isDiscovered"] = true };
                ((JsonArray)profile["inventory"]!["items"]!).Add(entry); held.Add(token, entry);
            }
            added++;
        }
        return added;
    }
}
