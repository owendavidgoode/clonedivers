using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clonedivers;

public sealed partial class HellForgeClient
{
    sealed record GameplayBackup(string Id, string Archive, long Bytes, string Sha256, string ContentHash);
    sealed record GameplaySnapshot(string Root, HellForgeMods Mods, List<GameplayBackup> Backups, bool HadShare = false, bool Share = true);
    sealed record GameplayIntent(string Root, string Catalog, string Backup, string SnapshotSha256, string State, bool OptionalDefaultsApplied = false);
    public string GameplayCache => Path.Combine(CacheRoot, "gameplay", HellForgeGameplayCatalog.Pinned.Id);
    string GameplayIntentPath => PackIntentPath + ".gameplay.json";
    bool GameplayDefaultsApplied => ReadGameplayIntent() is { Catalog: var catalog } intent && catalog == HellForgeGameplayCatalog.Pinned.Id &&
        (intent.State == "applied" || intent.OptionalDefaultsApplied);

    async Task RequireGameplayIdleAsync(CancellationToken ct)
    {
        if (Game.IsRunning()) throw new InvalidOperationException("Close HD2 before installing or recovering its gameplay preset.");
        var json = await commands.RunAsync(["status"], ct);
        if (json.GetProperty("session").GetProperty("running").GetBoolean()) throw new InvalidOperationException("End the active Forge session before changing gameplay mods.");
        if (Text(json, "gameBuild") != HellForgePack.SupportedBuild || Text(json, "engine") != HellForgeRelease.Engine ||
            json.GetProperty("modSdk").GetInt32() != HellForgeRelease.Sdk || !HellForgeRelease.Supports(Text(json, "hellforge")))
            throw new InvalidDataException("The gameplay installer requires the pinned Forge, engine, SDK and game build.");
        // Importing archives is supported before a game copy exists. Launch still
        // requires the full official game/ownership/isolation/sandbox readiness.
    }

    async Task<string> GameplayHashAsync(string path, CancellationToken ct)
    {
        HellForgeGameplayCatalog.RejectLinks(path);
        var json = await commands.RunAsync(["mod-hash", "--mod", path], ct);
        var hash = Text(json, "hash");
        if (!FileSafety.IsSha256(hash)) throw new InvalidDataException("Official mod content hash was invalid.");
        return hash;
    }
    async Task<string> InstalledGameplayHashAsync(HellForgeMod mod, GameplayArtifact artifact, CancellationToken ct)
    {
        ValidateModFolder(mod.Folder);
        if (!mod.Id.StartsWith("pack.", StringComparison.Ordinal) || mod.Archive == null) return await GameplayHashAsync(mod.Folder, ct);
        ValidateModFolder(mod.Archive);
        await HellForgeGameplayCatalog.VerifyArchiveAsync(mod.Archive, artifact, ct);
        // The official hash command takes cosmetic ZIPs, not their unpacked
        // folders. Also attest the cache the runner will actually consume.
        using var zip = ZipFile.OpenRead(mod.Archive);
        foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
        {
            var target = Path.GetFullPath(Path.Combine(mod.Folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(mod.Folder + Path.DirectorySeparatorChar, RecoveryPathComparison)) throw new InvalidDataException("Data mod entry escapes its installed folder.");
            HellForgeGameplayCatalog.RejectLinks(target);
            if (!File.Exists(target) || new FileInfo(target).Length != entry.Length) throw new InvalidDataException("Unpacked data mod content changed: " + mod.Name);
            using var expected = entry.Open(); using var actual = File.OpenRead(target);
            var expectedHash = await SHA256.HashDataAsync(expected, ct); var actualHash = await SHA256.HashDataAsync(actual, ct);
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                throw new InvalidDataException("Unpacked data mod content changed: " + mod.Name);
        }
        return await GameplayHashAsync(mod.Archive, ct);
    }

    public async Task<List<GameplayArtifact>> MissingGameplayAsync(CancellationToken ct = default)
    {
        var catalog = await ModsAsync(ct);
        if (catalog.Problems.Length != 0) throw new InvalidDataException("Resolve the mod catalog errors first: " + string.Join("; ", catalog.Problems));
        var missing = new List<GameplayArtifact>();
        var defaultsApplied = GameplayDefaultsApplied;
        foreach (var artifact in HellForgeGameplayCatalog.Pinned.Artifacts)
        {
            var found = catalog.Mods.SingleOrDefault(m => m.Id == artifact.Id);
            // Offer optional defaults once. Later removal, disabling or replacement
            // belongs to the player, including while required mods are repaired.
            if (artifact.Optional && (defaultsApplied || found is { Enabled: false })) continue;
            if (found == null || artifact.Version.Length > 0 && found.Version != artifact.Version || artifact.Version.Length == 0 && found.Name != artifact.Name)
            { missing.Add(artifact); continue; }
            ValidateModFolder(found.Folder);
            try { if (await InstalledGameplayHashAsync(found, artifact, ct) != artifact.ContentHash) missing.Add(artifact); }
            catch (Exception error) when (error is IOException or InvalidDataException) { missing.Add(artifact); }
        }
        return missing;
    }

    GameplayIntent? ReadGameplayIntent()
    {
        RejectAncestors(GameplayIntentPath);
        if (!File.Exists(GameplayIntentPath)) return null;
        if (new FileInfo(GameplayIntentPath).Length > 16384) throw new InvalidDataException("Gameplay recovery record is oversized.");
        var value = JsonSerializer.Deserialize<GameplayIntent>(File.ReadAllText(GameplayIntentPath), HellForgePack.Json) ?? throw new InvalidDataException("Empty gameplay recovery record.");
        // Completed records from an earlier launcher's catalog stay inert; only
        // pending ones matter, and RecoverGameplayAsync handles their catalog.
        if (!Root.Equals(value.Root, RecoveryPathComparison) || value.Catalog is not { Length: > 0 and <= 64 } || !FileSafety.IsSha256(value.SnapshotSha256) ||
            value.State is not ("pending" or "applied" or "restored") || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(value.Backup)), Path.Combine(CacheRoot, "backups"), RecoveryPathComparison) || !Path.GetFileName(value.Backup).StartsWith("gameplay-", StringComparison.Ordinal))
            throw new InvalidDataException("Gameplay recovery record does not match this installation.");
        RejectAncestors(value.Backup); return value;
    }
    void WriteGameplayIntent(GameplayIntent value)
    {
        RejectAncestors(GameplayIntentPath); RejectAncestors(GameplayIntentPath + ".tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(GameplayIntentPath)!);
        using (var file = new FileStream(GameplayIntentPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, value, HellForgePack.Json); file.Flush(flushToDisk: true); }
        File.Move(GameplayIntentPath + ".tmp", GameplayIntentPath, true);
    }
    public string? InterruptedGameplayProblem()
    {
        try
        {
            return ReadGameplayIntent() switch
            {
                not { State: "pending" } => null,
                { Catalog: var catalog } when catalog != HellForgeGameplayCatalog.Pinned.Id => "Gameplay setup from an earlier launcher was interrupted. Recover to keep its backup for review and continue with the current preset.",
                _ => "Gameplay setup was interrupted. Recover the previous gameplay preset before continuing."
            };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
        { return "Gameplay recovery needs review: " + error.Message; }
    }

    public async Task InstallGameplayAsync(string staged, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromMinutes(20)); ct = budget.Token;
        if (!Path.GetFullPath(staged).Equals(Path.GetFullPath(GameplayCache), RecoveryPathComparison)) throw new InvalidDataException("Use this installation's verified gameplay staging cache.");
        using var operation = LockPackChange(); RequireCliQuiescent(); RequireNoInterruptedPack();
        await RequireGameplayIdleAsync(ct);
        var before = await ModsAsync(ct);
        var missing = await MissingGameplayAsync(ct);
        if (missing.Count == 0 && GameplayDefaultsApplied && HellForgePreset.Problem(before, checkOrder: true) == null) { progress?.Report("Required gameplay preset already verified."); return; }
        foreach (var artifact in missing)
        {
            var path = FileSafety.Child(staged, artifact.FileName);
            await HellForgeGameplayCatalog.VerifyArchiveAsync(path, artifact, ct);
            if (await GameplayHashAsync(path, ct) != artifact.ContentHash) throw new InvalidDataException("Unexpected native content in " + artifact.Name);
        }
        var builtins = new[] { "hellforge.editor", DifficultyId, "skyeshade.vanilla-plus" };
        if (builtins.Any(id => before.Mods.All(m => m.Id != id))) throw new InvalidDataException("Finish official Forge setup to obtain its bundled Editor, Difficulty and Vanilla Plus mods.");
        var backup = Path.Combine(CacheRoot, "backups", "gameplay-" + Guid.NewGuid().ToString("N"));
        RejectAncestors(backup); Directory.CreateDirectory(backup);
        var copies = new List<GameplayBackup>();
        foreach (var artifact in missing)
        {
            var old = before.Mods.SingleOrDefault(m => m.Id == artifact.Id);
            if (old == null) continue;
            ValidateModFolder(old.Folder);
            var name = artifact.Sha256 + ".zip"; var path = FileSafety.Child(backup, name);
            string content;
            if (old.Id.StartsWith("pack.", StringComparison.Ordinal) && old.Archive != null)
            {
                ValidateModFolder(old.Archive);
                var size = new FileInfo(old.Archive).Length;
                if (size > 256L * 1024 * 1024) throw new InvalidDataException("Previous gameplay archive exceeds the backup limit.");
                await HellForgePack.CopyLocalAsync(old.Archive, path, size, ct);
                content = await GameplayHashAsync(old.Archive, ct);
            }
            else { await SnapshotGameplayAsync(old.Folder, path, ct); content = await GameplayHashAsync(BackupHashSource(old, path), ct); }
            if (await GameplayHashAsync(path, ct) != content) throw new InvalidDataException("Could not produce a restorable backup of " + old.Name);
            copies.Add(new(old.Id, name, new FileInfo(path).Length, await HellForgePack.HashAsync(path, ct), content));
        }
        var share = ReadSharing();
        var snapshot = new GameplaySnapshot(Root, before, copies, share.HadShare, share.Share);
        var snapshotPath = FileSafety.Child(backup, "before.json");
        using (var file = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(file, snapshot, HellForgePack.Json, ct); file.Flush(flushToDisk: true); }
        var intent = new GameplayIntent(Root, HellForgeGameplayCatalog.Pinned.Id, backup, await HellForgePack.HashAsync(snapshotPath, ct), "pending", GameplayDefaultsApplied);
        await RequireGameplayIdleAsync(ct); ct.ThrowIfCancellationRequested(); WriteGameplayIntent(intent);
        try
        {
            foreach (var artifact in missing)
            {
                await RequireGameplayIdleAsync(ct);
                progress?.Report("Installing " + artifact.Name + "…");
                await ImportGameplayArchiveAsync(FileSafety.Child(staged, artifact.FileName), ct);
            }
            var imported = await ModsAsync(ct);
            foreach (var artifact in HellForgeGameplayCatalog.Pinned.Artifacts.Where(a => !a.Optional || missing.Contains(a)))
            {
                var mod = imported.Mods.SingleOrDefault(m => m.Id == artifact.Id) ?? throw new InvalidDataException("Forge did not import " + artifact.Name);
                if (await InstalledGameplayHashAsync(mod, artifact, ct) != artifact.ContentHash) throw new InvalidDataException("Installed content differs for " + artifact.Name);
            }
            await ConfigureGameplayAsync(missing, ct);
            var after = await ModsAsync(ct);
            if (HellForgePreset.Problem(after with { HostingProblem = null }, checkOrder: true) is { } issue) throw new InvalidDataException(issue);
            WriteGameplayIntent(intent with { State = "applied" });
            progress?.Report(after.HostingProblem ?? "Required gameplay mods installed and configured.");
        }
        catch (Exception error)
        {
            if (error is HellForgeCommandOutcomeUnknownException) throw;
            try { using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(10)); await RestoreGameplayAsync(intent, recovery.Token); }
            catch (Exception restore) { throw new IOException("Gameplay setup stopped; recovery is pending. " + restore.Message, new AggregateException(error, restore)); }
            throw new IOException("Gameplay setup failed; the previous mod selection was restored. " + error.Message, error);
        }
    }

    async Task ConfigureGameplayAsync(List<GameplayArtifact> installed, CancellationToken ct)
    {
        var catalog = await ModsAsync(ct);
        var wanted = HellForgeGameplayCatalog.Pinned.Order.ToHashSet(StringComparer.Ordinal);
        foreach (var mod in catalog.Mods)
        {
            if (HellForgeGameplayCatalog.Pinned.Artifacts.Any(a => a.Id == mod.Id && a.Optional) && installed.All(a => a.Id != mod.Id)) continue;
            var conflict = HellForgePreset.IsConflictingAlternative(mod);
            if (!wanted.Contains(mod.Id) && !conflict) continue;
            var on = wanted.Contains(mod.Id);
            if (mod.Enabled == on) continue;
            await RequireGameplayIdleAsync(ct);
            await commands.RunAsync(["mod-switch", "--id", mod.Id, "--on", on ? "yes" : "no"], ct);
        }
        foreach (var id in new[] { "pack.612eaf70-d682-43c7-9efd-16dcc695f977", "pack.95ef276a-6287-465f-ac5b-8512d2227b74" })
        {
            await RequireGameplayIdleAsync(ct); await commands.RunAsync(["mod-pack", "--id", id, "--all", "yes"], ct);
        }
        foreach (var option in new[] { "Smarter Guard Dogs and Sentries", "Guard Dogs", "Sentries", "Intelligence", "Safety", "Sentry Safety", "SEAF Safety", "Targeting Marker" })
        {
            await RequireGameplayIdleAsync(ct);
            await commands.RunAsync(["mod-pack", "--id", HellForgePreset.GuardDogs, "--option", option, "--on", option == "Targeting Marker" ? "no" : "yes"], ct);
        }
        var ordered = HellForgeGameplayCatalog.Pinned.Order.Where(id => catalog.Mods.Any(m => m.Id == id)).Concat(catalog.Mods.Select(m => m.Id).Where(id => !wanted.Contains(id))).ToArray();
        await RequireGameplayIdleAsync(ct); await commands.RunAsync(["mod-order", "--ids", string.Join(",", ordered)], ct);
        await WriteSharingAsync(true, false, ct);
    }

    // Official 0.4 external-launcher file contract. Change only the sharing
    // preference; preserve every other field and use atomic replacement.
    (bool HadShare, bool Share) ReadSharing()
    {
        var path = FileSafety.Child(Path.Combine(Root, "mods"), "hellforge-mods.json"); RejectAncestors(path);
        if (!File.Exists(path)) return (false, true);
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Mod preferences are oversized.");
        var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Invalid mod preferences.");
        return json.TryGetPropertyValue("share", out var value) ? (true, value?.GetValue<bool>() ?? throw new InvalidDataException("Invalid sharing preference.")) : (false, true);
    }
    async Task WriteSharingAsync(bool present, bool value, CancellationToken ct)
    {
        await RequireGameplayIdleAsync(ct);
        var path = FileSafety.Child(Path.Combine(Root, "mods"), "hellforge-mods.json"); RejectAncestors(path);
        if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Mod preferences are oversized.");
        var before = File.Exists(path) ? File.ReadAllText(path) : null;
        var json = before == null ? new JsonObject { ["format"] = 1 } : JsonNode.Parse(before) as JsonObject ?? throw new InvalidDataException("Invalid mod preferences.");
        if (present) json["share"] = value; else json.Remove("share");
        var temp = path + ".clonedivers-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, json, HellForgePack.Json); file.Flush(flushToDisk: true); }
            if ((File.Exists(path) ? File.ReadAllText(path) : null) != before) throw new IOException("Forge mod preferences changed during setup; retry after closing its Mods page.");
            ct.ThrowIfCancellationRequested(); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        if (ReadSharing() != (present, present ? value : true)) throw new IOException("Forge mod sharing could not be confirmed.");
    }

    async Task ImportGameplayArchiveAsync(string source, CancellationToken ct)
    {
        // A unique author-archive filename makes the official importer create a
        // fresh unpacked cache even when repairing identical archive bytes.
        var imports = Path.Combine(GameplayCache, "imports"); RejectAncestors(imports); Directory.CreateDirectory(imports);
        var copy = FileSafety.Child(imports, Guid.NewGuid().ToString("N") + ".zip");
        var expected = await HellForgePack.HashAsync(source, ct);
        await HellForgePack.CopyLocalAsync(source, copy, new FileInfo(source).Length, ct);
        if (await HellForgePack.HashAsync(copy, ct) != expected) throw new InvalidDataException("Gameplay import copy changed during preparation.");
        using var locked = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read);
        await RequireGameplayIdleAsync(ct); await commands.RunAsync(["mod-add", "--file", copy], ct);
    }

    // The official hash command cannot read a cosmetic pack folder (it wants
    // hellforge-mod.json); its backup ZIP is what recovery imports, so attest that.
    internal static string BackupHashSource(HellForgeMod old, string backupZip) => old.Id.StartsWith("pack.", StringComparison.Ordinal) ? backupZip : old.Folder;
    static async Task SnapshotGameplayAsync(string folder, string archive, CancellationToken ct)
    {
        using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);
        var queue = new Queue<string>(); queue.Enqueue(folder); long bytes = 0; int count = 0;
        while (queue.TryDequeue(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested(); FileSafety.RejectLink(path);
                if (Directory.Exists(path)) { queue.Enqueue(path); continue; }
                bytes = checked(bytes + new FileInfo(path).Length);
                if (++count > 20000 || bytes > 256L * 1024 * 1024) throw new InvalidDataException("Gameplay backup exceeds the supported inventory size.");
                var entry = zip.CreateEntry(Path.GetRelativePath(folder, path).Replace('\\', '/'), CompressionLevel.Optimal);
                using var input = File.OpenRead(path); using var stream = entry.Open(); await input.CopyToAsync(stream, ct);
            }
    }

    public async Task RecoverGameplayAsync(CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromMinutes(10)); ct = budget.Token;
        using var operation = LockPackChange(); RequireCliQuiescent();
        var intent = ReadGameplayIntent() ?? throw new InvalidOperationException("No interrupted gameplay setup was found.");
        if (intent.State != "pending") return;
        if (intent.Catalog != HellForgeGameplayCatalog.Pinned.Id)
        {
            // This launcher cannot restore another catalog's preset. Keep the
            // record and backup for review rather than blocking setup forever.
            var retired = GameplayIntentPath + ".retired-" + Guid.NewGuid().ToString("N"); RejectAncestors(retired);
            File.Move(GameplayIntentPath, retired);
            throw new InvalidOperationException("Gameplay setup from an earlier launcher cannot be restored automatically. Its backup was kept at " + intent.Backup + ". Prepare again to install the current preset.");
        }
        await RestoreGameplayAsync(intent, ct);
    }
    async Task RestoreGameplayAsync(GameplayIntent intent, CancellationToken ct)
    {
        await RequireGameplayIdleAsync(ct);
        var path = FileSafety.Child(intent.Backup, "before.json");
        if (new FileInfo(path).Length > 4 * 1024 * 1024 || await HellForgePack.HashAsync(path, ct) != intent.SnapshotSha256) throw new InvalidDataException("Gameplay recovery metadata changed.");
        var snapshot = JsonSerializer.Deserialize<GameplaySnapshot>(await File.ReadAllTextAsync(path, ct), HellForgePack.Json) ?? throw new InvalidDataException("Empty gameplay snapshot.");
        var artifacts = HellForgeGameplayCatalog.Pinned.Artifacts;
        if (!Root.Equals(snapshot.Root, RecoveryPathComparison) || snapshot.Backups.Any(b => artifacts.All(a => a.Id != b.Id)) || snapshot.Mods.Mods.Select(m => m.Id).Distinct().Count() != snapshot.Mods.Mods.Count)
            throw new InvalidDataException("Gameplay snapshot is invalid for this installation.");
        foreach (var copy in snapshot.Backups)
        {
            FileSafety.ValidateLeaf(copy.Archive); var file = FileSafety.Child(intent.Backup, copy.Archive);
            if (new FileInfo(file).Length != copy.Bytes || await HellForgePack.HashAsync(file, ct) != copy.Sha256 || await GameplayHashAsync(file, ct) != copy.ContentHash)
                throw new InvalidDataException("Gameplay backup content changed.");
        }
        var current = await ModsAsync(ct);
        foreach (var artifact in artifacts)
        {
            var old = snapshot.Mods.Mods.SingleOrDefault(m => m.Id == artifact.Id);
            var copy = snapshot.Backups.SingleOrDefault(b => b.Id == artifact.Id);
            if (copy != null) { await RequireGameplayIdleAsync(ct); await ImportGameplayArchiveAsync(FileSafety.Child(intent.Backup, copy.Archive), ct); }
            else if (old == null && current.Mods.Any(m => m.Id == artifact.Id)) { await RequireGameplayIdleAsync(ct); await commands.RunAsync(["mod-remove", "--id", artifact.Id], ct); }
        }
        var restored = await ModsAsync(ct);
        foreach (var old in snapshot.Mods.Mods)
        {
            var found = restored.Mods.SingleOrDefault(m => m.Id == old.Id) ?? throw new InvalidDataException("A previous mod is missing during recovery: " + old.Name);
            if (found.Enabled != old.Enabled) { await RequireGameplayIdleAsync(ct); await commands.RunAsync(["mod-switch", "--id", old.Id, "--on", old.Enabled ? "yes" : "no"], ct); }
            if (old.Options == null) continue;
            foreach (var option in old.Options)
                if (found.Options?.SingleOrDefault(o => o.Name == option.Name)?.On != option.On)
                { await RequireGameplayIdleAsync(ct); await commands.RunAsync(["mod-pack", "--id", old.Id, "--option", option.Name, "--on", option.On ? "yes" : "no"], ct); }
        }
        await RequireGameplayIdleAsync(ct); await commands.RunAsync(["mod-order", "--ids", string.Join(",", snapshot.Mods.Mods.Select(m => m.Id))], ct);
        await WriteSharingAsync(snapshot.HadShare, snapshot.Share, ct);
        var verified = await ModsAsync(ct);
        if (!verified.Mods.Select(m => m.Id).SequenceEqual(snapshot.Mods.Mods.Select(m => m.Id)) || verified.Problems.Length != 0)
            throw new InvalidDataException("The previous gameplay mod order could not be confirmed restored.");
        foreach (var old in snapshot.Mods.Mods)
        {
            var actual = verified.Mods.Single(m => m.Id == old.Id);
            if (actual.Enabled != old.Enabled || (old.Options ?? []).Any(option => actual.Options?.SingleOrDefault(o => o.Name == option.Name)?.On != option.On))
                throw new InvalidDataException("Previous gameplay options could not be confirmed restored: " + old.Name);
        }
        foreach (var copy in snapshot.Backups)
        {
            var actual = verified.Mods.Single(m => m.Id == copy.Id);
            var restoredPath = !actual.Id.StartsWith("pack.", StringComparison.Ordinal) ? actual.Folder
                : actual.Archive ?? throw new InvalidDataException("Previous gameplay content could not be confirmed restored: " + actual.Name);
            ValidateModFolder(restoredPath);
            if (await GameplayHashAsync(restoredPath, ct) != copy.ContentHash)
                throw new InvalidDataException("Previous gameplay content could not be confirmed restored: " + actual.Name);
        }
        WriteGameplayIntent(intent with { State = "restored" });
    }
}
