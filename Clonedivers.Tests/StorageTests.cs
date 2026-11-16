using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clonedivers;

namespace Clonedivers.Tests;

internal static class StorageTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var area = Path.Combine(root, "storage"); Directory.CreateDirectory(area);
        Space(check);
        await Installers(Path.Combine(area, "installers"), check);
        await Appearance(Path.Combine(area, "appearance"), check);
        await ToggleReuse(Path.Combine(area, "toggle"), check);
    }
    static void Space(Action<bool, string> check)
    {
        foreach (var free in new[] { 50L << 30, (70L << 30) - 1 })
        {
            bool install = false, game = false;
            try { HellForgeSetup.CheckSetupSpace(free, @"Q:\"); }
            catch (IOException e) { install = e.Message.Contains(@"Q:\") && e.Message.Contains("70 GB"); }
            try { HellForgeSetup.CheckGameSpace(free, 0, @"Q:\"); }
            catch (IOException e) { game = e.Message.Contains(@"Q:\") && e.Message.Contains("70 GB"); }
            check(install && game, "both setup checks reject " + free + " bytes and name the drive and 70 GB budget");
        }
        HellForgeSetup.CheckSetupSpace(70L << 30, @"Q:\"); HellForgeSetup.CheckGameSpace(70L << 30, 0, @"Q:\");
        HellForgeSetup.CheckGameSpace(22L << 30, 48L << 30, @"Q:\");
        check(true, "70 GB is sufficient and resumed game bytes count towards the same budget");
    }
    static void FakeInstall(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "app", "cli")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "hellforge-install.json"), "{\"schemaVersion\":1,\"buildId\":\"25480438\"}");
        File.WriteAllText(Path.Combine(root, "HellForge.exe"), "fixture"); File.WriteAllText(Path.Combine(root, "app", "cli", "HellForge.Cli.exe"), "fixture");
    }
    static async Task Installers(string area, Action<bool, string> check)
    {
        var body = new byte[] { 1, 2, 3, 4, 5, 6 };
        var artifact = new PackFile { Name = "fixture.exe", Size = body.Length, Sha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(), Url = "https://invalid.example/never" };
        var legacy = Path.Combine(area, "legacy"); Directory.CreateDirectory(legacy);
        var target = Path.Combine(area, "new", "Forge"); var cache = HellForgeSetup.InstallCache(target);
        var old = HellForgeSetup.InstallerPath(legacy, artifact); File.WriteAllBytes(old, body);
        bool unpacked = false;
        var installed = await HellForgeSetup.InstallAsync(target, null, default, artifact: artifact,
            unpack: (installer, staging) => { unpacked = installer == HellForgeSetup.InstallerPath(cache, artifact); FakeInstall(staging); return Task.CompletedTask; },
            spaceCheck: _ => { }, cacheAvailableBytes: _ => long.MaxValue, legacyCache: legacy);
        check(installed == target && unpacked && !File.Exists(old) && File.ReadAllBytes(HellForgeSetup.InstallerPath(cache, artifact)).SequenceEqual(body),
            "default-cache install moves and runs the verified legacy installer without a network request or duplicate copy");
        File.Delete(HellForgeSetup.InstallerPath(cache, artifact)); File.WriteAllBytes(old, body[..3]);
        bool stopped = false;
        try { await HellForgeSetup.InstallAsync(Path.Combine(area, "new", "OtherForge"), null, default, artifact: artifact,
            spaceCheck: _ => { }, cacheAvailableBytes: _ => 0, legacyCache: legacy); }
        catch (IOException) { stopped = true; }
        check(stopped && !File.Exists(old) && File.ReadAllBytes(HellForgeSetup.InstallerPath(cache, artifact)).SequenceEqual(body[..3]),
            "default-cache install moves a legacy partial before capacity preflight so its exact bytes remain resumable");
        File.WriteAllBytes(old, body[..2]);
        await HellForgeSetup.ReuseInstallerAsync(legacy, cache, artifact, default);
        check(!File.Exists(old) && new FileInfo(HellForgeSetup.InstallerPath(cache, artifact)).Length == 3,
            "installer migration keeps the longer target partial and removes the redundant legacy copy");
        File.WriteAllBytes(old, new byte[body.Length]);
        await HellForgeSetup.ReuseInstallerAsync(legacy, cache, artifact, default);
        check(!File.Exists(old) && new FileInfo(HellForgeSetup.InstallerPath(cache, artifact)).Length == 3,
            "a corrupt full legacy installer cannot replace a useful target partial");
        File.WriteAllBytes(old, body);
        await HellForgeSetup.ReuseInstallerAsync(legacy, cache, artifact, default);
        check(!File.Exists(old) && File.ReadAllBytes(HellForgeSetup.InstallerPath(cache, artifact)).SequenceEqual(body),
            "a verified legacy installer replaces a shorter partial without retaining two copies");

        // #4: DefaultRoot cache and remembered last install cache reuse when switching targets.
        var defaultRootCache = HellForgeSetup.InstallCache(Path.Combine(area, "default-root", "HellForge"));
        var emptyLegacy = Path.Combine(area, "empty-legacy"); Directory.CreateDirectory(emptyLegacy);
        var defaultInstaller = HellForgeSetup.InstallerPath(defaultRootCache, artifact);
        Directory.CreateDirectory(defaultRootCache); File.WriteAllBytes(defaultInstaller, body);
        var movedTarget = Path.Combine(area, "moved-drive", "HellForge");
        bool movedUnpacked = false;
        var movedInstalled = await HellForgeSetup.InstallAsync(movedTarget, null, default, artifact: artifact,
            unpack: (installer, staging) => { movedUnpacked = installer == HellForgeSetup.InstallerPath(HellForgeSetup.InstallCache(movedTarget), artifact); FakeInstall(staging); return Task.CompletedTask; },
            spaceCheck: _ => { }, cacheAvailableBytes: _ => long.MaxValue, legacyCache: emptyLegacy, defaultRootCache: defaultRootCache);
        check(movedInstalled == movedTarget && movedUnpacked && !File.Exists(defaultInstaller) &&
            File.ReadAllBytes(HellForgeSetup.InstallerPath(HellForgeSetup.InstallCache(movedTarget), artifact)).SequenceEqual(body),
            "default-cache install reuses an installer from InstallCache(DefaultRoot) when switching to another target");

        var rememberedLegacy = Path.Combine(area, "remembered-legacy"); Directory.CreateDirectory(rememberedLegacy);
        var emptyDefault = Path.Combine(area, "empty-default");
        var firstCustom = Path.Combine(area, "first-custom", "Forge");
        var firstCache = HellForgeSetup.InstallCache(firstCustom);
        Directory.CreateDirectory(firstCache); File.WriteAllBytes(HellForgeSetup.InstallerPath(firstCache, artifact), body);
        try
        {
            await HellForgeSetup.InstallAsync(firstCustom, null, default, artifact: artifact,
                unpack: (_, _) => throw new IOException("simulated stop after cache selection"),
                spaceCheck: _ => { }, cacheAvailableBytes: _ => long.MaxValue, legacyCache: rememberedLegacy, defaultRootCache: emptyDefault);
        }
        catch (IOException) { }
        var firstPartial = HellForgeSetup.InstallerPath(firstCache, artifact);
        File.WriteAllBytes(firstPartial, body[..4]);
        var secondCustom = Path.Combine(area, "second-custom", "Forge");
        bool rememberedStopped = false;
        try
        {
            await HellForgeSetup.InstallAsync(secondCustom, null, default, artifact: artifact,
                spaceCheck: _ => { }, cacheAvailableBytes: _ => 0, legacyCache: rememberedLegacy, defaultRootCache: emptyDefault);
        }
        catch (IOException) { rememberedStopped = true; }
        check(rememberedStopped && !File.Exists(firstPartial) &&
            File.ReadAllBytes(HellForgeSetup.InstallerPath(HellForgeSetup.InstallCache(secondCustom), artifact)).SequenceEqual(body[..4]),
            "default-cache install moves a partial from the remembered last install cache before capacity preflight");

        // #5: Injectable cross-volume copy migration branch (low-space refusal, .migrating cleanup, copy & source removal).
        var crossOld = Path.Combine(area, "cross-old"); Directory.CreateDirectory(crossOld);
        var crossNew = Path.Combine(area, "cross-new"); Directory.CreateDirectory(crossNew);
        var crossSource = HellForgeSetup.InstallerPath(crossOld, artifact);
        var crossTarget = HellForgeSetup.InstallerPath(crossNew, artifact);
        var crossTemp = crossTarget + ".migrating";
        File.WriteAllBytes(crossSource, body);
        bool lowSpaceRejected = false;
        try
        {
            await HellForgeSetup.ReuseInstallerAsync(crossOld, crossNew, artifact, default,
                _ => body.Length + HellForgeSetup.CacheReserve - 1, sameVolume: false);
        }
        catch (IOException) { lowSpaceRejected = true; }
        check(lowSpaceRejected && File.Exists(crossSource) && !File.Exists(crossTarget) && !File.Exists(crossTemp),
            "cross-volume installer migration refuses low space without touching source or leaving .migrating");

        bool copyFailed = false;
        try
        {
            await HellForgeSetup.ReuseInstallerAsync(crossOld, crossNew, artifact, default,
                _ => long.MaxValue, sameVolume: false,
                copyFile: async (_, temp, _, _) => { await File.WriteAllBytesAsync(temp, body[..2]); throw new IOException("simulated cross-volume write failure"); });
        }
        catch (IOException) { copyFailed = true; }
        check(copyFailed && File.Exists(crossSource) && !File.Exists(crossTarget) && !File.Exists(crossTemp),
            "failed cross-volume migration cleans up .migrating and preserves the source file");

        File.WriteAllBytes(crossTemp, [9, 9]);
        await HellForgeSetup.ReuseInstallerAsync(crossOld, crossNew, artifact, default,
            _ => body.Length + HellForgeSetup.CacheReserve, sameVolume: false);
        check(!File.Exists(crossSource) && !File.Exists(crossTemp) && File.ReadAllBytes(crossTarget).SequenceEqual(body),
            "cross-volume installer migration copies verified bytes, cleans up stale .migrating, and removes source");
    }
    static async Task Appearance(string area, Action<bool, string> check)
    {
        var root = Path.Combine(area, "Forge"); FakeInstall(root);
        var commands = new Commands(root); var client = new HellForgeClient(root, commands) { AvailablePackBytes = _ => long.MaxValue, RequireCliQuiescent = () => { } };
        var body = new byte[300000]; body[0] = 7;
        var source = Path.Combine(area, "source"); Directory.CreateDirectory(source); var local = Path.Combine(source, "asset"); File.WriteAllBytes(local, body);
        var hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var file = new PackFile { Name = "appearance.patch_0", Size = body.Length, Sha256 = hash };
        var selection = new HellForgePack.Selection("clonedivers", "full", "current", HellForgePack.SupportedBuild, hash, new(), [file]);
        async Task<string> Export(string mode, string version, int age)
        {
            var output = Path.Combine(client.CacheRoot, "exports", mode + "-" + Guid.NewGuid().ToString("N"));
            var selected = selection with { Mode = mode, Version = version };
            await HellForgePack.ExportAsync(new(selected, output, new() { [hash] = local }), false);
            Directory.SetLastWriteTimeUtc(output, DateTime.UtcNow.AddMinutes(age)); return output;
        }
        var obsolete = await Export("clonedivers", "obsolete", -10);
        var current = await Export("clonedivers", "current", -9);
        var newest = await Export("clonedivers", "newest", -8);
        var empireOld = await Export("empire", "old", -7); var empireNewest = await Export("empire", "newest", -6);
        var manual = Path.Combine(client.CacheRoot, "exports", "manual-export"); Directory.CreateDirectory(manual); File.WriteAllText(Path.Combine(manual, "sentinel"), "keep");
        var unfinished = Path.Combine(client.CacheRoot, "exports", "empire-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(unfinished);
        string Backup(string state)
        {
            var folder = Path.Combine(client.CacheRoot, "backups", "20260101-000000-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            if (state.Length > 0) File.WriteAllText(Path.Combine(folder, "outcome.json"), JsonSerializer.Serialize(new { outcome = state }));
            return folder;
        }
        var oldApplied = Backup("applied"); var oldRestored = Backup("restored"); var incomplete = Backup("");
        await client.InstallPackAsync(current, selection);
        using var firstIntent = JsonDocument.Parse(File.ReadAllText(client.PackIntentPath)); var firstBackup = firstIntent.RootElement.GetProperty("Backup").GetString()!;
        check(!Directory.Exists(oldApplied) && !Directory.Exists(oldRestored) && Directory.Exists(firstBackup) && Directory.Exists(incomplete),
            "successful apply prunes older completed backups and keeps the current and incomplete backups");
        check(!Directory.Exists(obsolete) && !Directory.Exists(empireOld) && Directory.Exists(current) && Directory.Exists(newest) && Directory.Exists(empireNewest),
            "successful apply keeps installed selections and the newest export per mode while pruning superseded receipts");
        check(File.Exists(Path.Combine(manual, "sentinel")) && Directory.Exists(unfinished), "storage pruning preserves manual folders and unreceipted exports");
        var pendingText = File.ReadAllText(client.PackIntentPath).Replace("\"applied\"", "\"pending\""); File.WriteAllText(client.PackIntentPath, pendingText);
        var pendingBackup = Backup("applied"); var pendingExport = await Export("empire", "superseded", -20);
        client.PrunePackStorage(firstBackup, new(commands.Mods, [], null));
        check(Directory.Exists(pendingBackup) && Directory.Exists(pendingExport), "pending appearance intent prevents all completed-storage pruning");
        File.WriteAllText(client.PackIntentPath, pendingText.Replace("\"pending\"", "\"applied\""));
        commands.FailSwitch = true;
        bool restored = false;
        try { await client.InstallPackAsync(newest, selection with { Version = "newest" }); }
        catch (IOException e) { restored = e.Message.Contains("were restored"); }
        using var restoredIntent = JsonDocument.Parse(File.ReadAllText(client.PackIntentPath)); var restoredBackup = restoredIntent.RootElement.GetProperty("Backup").GetString()!;
        check(restored && restoredIntent.RootElement.GetProperty("State").GetString() == "restored" && Directory.Exists(restoredBackup) && !Directory.Exists(firstBackup) && !Directory.Exists(pendingBackup) && !Directory.Exists(pendingExport),
            "successful automatic restore prunes completed history only after its terminal recovery record and keeps its own backup");
        var blocked = Backup("recovery-needed"); var retained = Backup("applied");
        client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
        check(!Directory.Exists(retained) && Directory.Exists(blocked),
            "recovery-needed outcome stays kept without preventing cleanup of older completed backups");

        var unknownOutcome = Backup("outcome-unknown");
        var corruptOutcome = Backup(""); File.WriteAllText(Path.Combine(corruptOutcome, "outcome.json"), "{broken");
        var futureBackup = Path.Combine(client.CacheRoot, "backups", "20990101-000000-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(futureBackup);
        File.WriteAllText(Path.Combine(futureBackup, "outcome.json"), JsonSerializer.Serialize(new { outcome = "applied" }));
        var olderCompleted = Backup("applied");
        client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
        check(Directory.Exists(unknownOutcome) && Directory.Exists(corruptOutcome) && Directory.Exists(futureBackup) && !Directory.Exists(olderCompleted),
            "outcome-unknown, unreadable outcome, and future-timestamped backups stay kept while older completed backups prune");

        // Pending .gameplay.json for this root aborts all pruning globally.
        var gameplayBackup = Path.Combine(client.CacheRoot, "backups", "gameplay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gameplayBackup);
        var gameplayIntentPath = client.PackIntentPath + ".gameplay.json";
        File.WriteAllText(gameplayIntentPath, JsonSerializer.Serialize(new {
            Root = client.Root, Catalog = HellForgeGameplayCatalog.Pinned.Id, Backup = gameplayBackup,
            SnapshotSha256 = new string('a', 64), State = "pending", OptionalDefaultsApplied = false
        }));
        var gameplayWaitBackup = Backup("applied");
        var gameplayWaitExport = await Export("empire", "wait-gameplay", -30);
        client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
        check(client.InterruptedGameplayProblem() != null && Directory.Exists(gameplayWaitBackup) && Directory.Exists(gameplayWaitExport),
            "pending .gameplay.json intent for this root prevents all completed-storage pruning");
        File.Delete(gameplayIntentPath);

        // Second root's pending <hash>.json protects only its own Backup; unreadable/oversized transaction records are skipped.
        var transactionsDir = Path.GetDirectoryName(client.PackIntentPath)!;
        var otherRoot = Path.Combine(area, "OtherForge");
        var otherIdentity = OperatingSystem.IsWindows() ? otherRoot.ToUpperInvariant() : otherRoot;
        var otherHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otherIdentity))).ToLowerInvariant();
        var otherBackup = Backup("applied");
        File.WriteAllText(Path.Combine(transactionsDir, otherHash + ".json"), JsonSerializer.Serialize(new {
            Format = 1, Root = otherRoot, Backup = otherBackup, Mode = "clonedivers",
            BeforeSha256 = new string('b', 64), State = "pending"
        }));
        var corruptTx = Path.Combine(transactionsDir, "corrupt-other.json"); File.WriteAllText(corruptTx, "{broken");
        var oversizedTx = Path.Combine(transactionsDir, "oversized-other.json"); File.WriteAllText(oversizedTx, new string(' ', 20000));
        client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
        check(Directory.Exists(otherBackup) && !Directory.Exists(gameplayWaitBackup) && !Directory.Exists(gameplayWaitExport),
            "second root pending transaction and corrupt records protect that backup without aborting cleanup for this root");
        File.Delete(corruptTx); File.Delete(oversizedTx);

        // Non-launcher receipted export folder (empire-copy) and corrupt receipt export stay kept while superseded launcher exports prune.
        var empireCopy = Path.Combine(client.CacheRoot, "exports", "empire-copy");
        await HellForgePack.ExportAsync(new(selection with { Mode = "empire", Version = "manual-copy" }, empireCopy, new() { [hash] = local }), false);
        Directory.SetLastWriteTimeUtc(empireCopy, DateTime.UtcNow.AddMinutes(10));
        var corruptExport = Path.Combine(client.CacheRoot, "exports", "empire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(corruptExport);
        File.WriteAllText(Path.Combine(corruptExport, HellForgePack.ReceiptName), "{broken");
        var supersededEmpire = await Export("empire", "superseded-again", -40);
        client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
        check(Directory.Exists(empireCopy) && Directory.Exists(empireNewest) && Directory.Exists(corruptExport) && !Directory.Exists(supersededEmpire),
            "non-launcher receipted folder and damaged receipts stay kept while superseded launcher exports prune");

        // Resumable deletion: locked file deletes previous-pack first, preserves outcome.json, and retries cleanly when unlocked; deleting-* retries too.
        var lockedBackup = Backup("applied");
        var lockedPrev = Path.Combine(lockedBackup, "previous-pack"); Directory.CreateDirectory(lockedPrev);
        var lockedPrevFile = Path.Combine(lockedPrev, "pack.patch_0"); File.WriteAllBytes(lockedPrevFile, [1, 2, 3]);
        var lockedBefore = Path.Combine(lockedBackup, "before.json"); File.WriteAllText(lockedBefore, "{}");
        using (new FileStream(lockedBefore, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
            check(!File.Exists(lockedPrevFile) && File.Exists(Path.Combine(lockedBackup, "outcome.json")),
                "locked backup cleanup deletes previous-pack first while preserving outcome.json for retry");
        }
        client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
        check(!Directory.Exists(lockedBackup), "subsequent prune finishes deleting previously locked backup");

        var deletingBackup = Path.Combine(client.CacheRoot, "backups", "deleting-20260101-000000-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(deletingBackup, "previous-pack"));
        File.WriteAllBytes(Path.Combine(deletingBackup, "previous-pack", "old.patch_0"), [4, 5]);
        var deletingExport = Path.Combine(client.CacheRoot, "exports", "deleting-empire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(deletingExport);
        File.WriteAllBytes(Path.Combine(deletingExport, "old.patch_0"), [6, 7]);
        var outsideSentinelDir = Path.Combine(area, "junction-target"); Directory.CreateDirectory(outsideSentinelDir);
        var outsideSentinel = Path.Combine(outsideSentinelDir, "keep.txt"); File.WriteAllText(outsideSentinel, "untouched");
        var deletingJunctionBackup = Path.Combine(client.CacheRoot, "backups", "deleting-junction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(deletingJunctionBackup);
        var junctionPath = Path.Combine(deletingJunctionBackup, "previous-pack");
        bool junctionCreated = TryCreateJunction(junctionPath, outsideSentinelDir);
        client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
        check(!Directory.Exists(deletingBackup) && !Directory.Exists(deletingExport) && File.Exists(outsideSentinel) &&
            (!OperatingSystem.IsWindows() || junctionCreated && Directory.Exists(deletingJunctionBackup)),
            "PrunePackStorage retries unlinked deleting-* folders and refuses linked deleting-* folders without touching targets");
        if (junctionCreated) Directory.Delete(junctionPath);
        if (Directory.Exists(deletingJunctionBackup)) Directory.Delete(deletingJunctionBackup, true);
        var progress = new List<(long Done, long Total)>();
        var duplicate = file.Clone(); duplicate.Name = "duplicate.patch_0";
        var plan = new HellForgePack.Plan(selection with { Files = [file, duplicate] }, Path.Combine(area, "progress-export"), new() { [hash] = local });
        await HellForgePack.ExportAsync(plan, false, bytes: new Bytes(progress.Add));
        check(progress.Count > 4 && progress.Any(p => p.Done > 0 && p.Done < body.Length) && progress.Any(p => p.Done > body.Length && p.Done < body.Length * 2) &&
            progress.Last() == ((long)body.Length * 2, (long)body.Length * 2) && progress.All(p => p.Total == body.Length * 2) && progress.Zip(progress.Skip(1)).All(p => p.First.Done <= p.Second.Done),
            "optional public byte callback tracks cumulative local and duplicate assets in chunks through the full export");
    }
    static async Task ToggleReuse(string area, Action<bool, string> check)
    {
        var root = Path.Combine(area, "Forge"); FakeInstall(root);
        var commands = new Commands(root); var client = new HellForgeClient(root, commands) { AvailablePackBytes = _ => long.MaxValue, RequireCliQuiescent = () => { } };
        var source = Path.Combine(area, "source"); Directory.CreateDirectory(source);
        (PackFile File, string Path) MakeAsset(string name, int size, byte seed)
        {
            var data = new byte[size]; data[0] = seed;
            var path = Path.Combine(source, $"{name}.{seed}"); File.WriteAllBytes(path, data);
            var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            return (new PackFile { Name = name, Size = data.Length, Sha256 = sha }, path);
        }
        var shared = MakeAsset("shared.patch_0", 1024, 1);
        var fullOnly = MakeAsset("variant.patch_1", 2048, 2);
        var lighterOnly = MakeAsset("variant.patch_1", 1536, 3);
        var fullSelection = new HellForgePack.Selection("clonedivers", "full", "r19", HellForgePack.SupportedBuild, shared.File.Sha256,
            new() { ["droids"] = true, ["covenant"] = false }, [shared.File, fullOnly.File]);
        var lighterSelection = new HellForgePack.Selection("clonedivers", "lighter", "r19", HellForgePack.SupportedBuild, lighterOnly.File.Sha256,
            new() { ["droids"] = true, ["covenant"] = false }, [shared.File, lighterOnly.File]);

        var fullExport = Path.Combine(client.CacheRoot, "exports", "clonedivers-" + Guid.NewGuid().ToString("N"));
        await HellForgePack.ExportAsync(new(fullSelection, fullExport, new() { [shared.File.Sha256] = shared.Path, [fullOnly.File.Sha256] = fullOnly.Path }), false);
        await client.InstallPackAsync(fullExport, fullSelection);

        var lighterExport = Path.Combine(client.CacheRoot, "exports", "clonedivers-" + Guid.NewGuid().ToString("N"));
        await HellForgePack.ExportAsync(new(lighterSelection, lighterExport, new() { [shared.File.Sha256] = shared.Path, [lighterOnly.File.Sha256] = lighterOnly.Path }), false);
        Directory.SetLastWriteTimeUtc(lighterExport, DateTime.UtcNow.AddMinutes(1));
        await client.InstallPackAsync(lighterExport, lighterSelection);

        var replan = await HellForgePack.PlanAsync(fullSelection, Path.Combine(client.CacheRoot, "exports", "clonedivers-" + Guid.NewGuid().ToString("N")),
            [Path.Combine(root, "mods")]);
        check(!Directory.Exists(fullExport) && replan.Missing.Count == 0 &&
            replan.Local.TryGetValue(fullOnly.File.Sha256, out var backupReuse) &&
            backupReuse.StartsWith(Path.Combine(client.CacheRoot, "backups"), StringComparison.OrdinalIgnoreCase),
            "switching Full to Lighter prunes the older Full export yet re-planning Full reuses backup previous-pack with zero missing bytes");
        await HellForgePack.ExportAsync(replan, false);
        await HellForgePack.VerifyAsync(replan.Output, fullSelection);
        check(true, "re-planned Full export completes offline and verifies from backup previous-pack");

        // Completed "restored" backups also contribute previous-pack reuse, while outcome-unknown backups do not.
        var restoredOnly = MakeAsset("restored.patch_2", 1792, 4);
        var unknownOnly = MakeAsset("unknown.patch_3", 1920, 5);
        var restoredBackup = Path.Combine(client.CacheRoot, "backups", "20260101-000001-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(restoredBackup, "previous-pack"));
        File.Copy(restoredOnly.Path, Path.Combine(restoredBackup, "previous-pack", restoredOnly.File.Name));
        File.WriteAllText(Path.Combine(restoredBackup, "outcome.json"), JsonSerializer.Serialize(new { outcome = "restored" }));
        var unknownBackup = Path.Combine(client.CacheRoot, "backups", "20260101-000002-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(unknownBackup, "previous-pack"));
        File.Copy(unknownOnly.Path, Path.Combine(unknownBackup, "previous-pack", unknownOnly.File.Name));
        File.WriteAllText(Path.Combine(unknownBackup, "outcome.json"), JsonSerializer.Serialize(new { outcome = "outcome-unknown" }));
        var probeSelection = new HellForgePack.Selection("clonedivers", "full", "r19", HellForgePack.SupportedBuild, restoredOnly.File.Sha256,
            new(), [restoredOnly.File, unknownOnly.File]);
        var probePlan = await HellForgePack.PlanAsync(probeSelection, Path.Combine(client.CacheRoot, "exports", "clonedivers-" + Guid.NewGuid().ToString("N")), []);
        check(probePlan.Local.ContainsKey(restoredOnly.File.Sha256) && probePlan.Missing.Select(m => m.Sha256).SequenceEqual([unknownOnly.File.Sha256]),
            "PlanAsync reuses previous-pack from restored backups and excludes incomplete or outcome-unknown backups");
    }
    static bool TryCreateJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var script = "$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start);
        process?.WaitForExit();
        return process?.ExitCode == 0;
    }
    sealed class Bytes(Action<(long Done, long Total)> report) : IProgress<(long Done, long Total)>
    { public void Report((long Done, long Total) value) => report(value); }
    sealed class Commands(string root) : IHellForgeCommands
    {
        internal List<HellForgeMod> Mods = [new(HellForgeClient.DifficultyId, "Difficulty", true, Path.Combine(root, "mods", "difficulty"), [])];
        internal bool FailSwitch;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            object result;
            switch (args[0])
            {
                case "status": result = new { install = root, hellforge = HellForgeRelease.Version, engine = HellForgeRelease.Engine, modSdk = HellForgeRelease.Sdk, gameBuild = HellForgePack.SupportedBuild, networkBlock = "wfp",
                    ready = new { gameCopy = true, sessionRunner = true }, session = new { running = false }, settings = new { multiplayer = true, localPlayFab = true, sandbox = true } }; break;
                case "mods": result = new { mods = Mods.Select(m => new { id = m.Id, name = m.Name, enabled = m.Enabled, folder = m.Folder, tags = m.Tags, version = m.Version }), problems = Array.Empty<string>() }; break;
                case "mod-add":
                    using (var manifest = JsonDocument.Parse(File.ReadAllText(args[2])))
                    {
                        var id = "pack." + manifest.RootElement.GetProperty("Guid").GetString(); var folder = Path.Combine(root, "mods", "selected"); Directory.CreateDirectory(folder);
                        foreach (var path in Directory.GetFiles(folder)) File.Delete(path);
                        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(args[2])!)) File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
                        Mods.RemoveAll(m => m.Id == id); Mods.Add(new(id, "Selected", true, folder, [])); result = new { id };
                    }
                    break;
                case "mod-switch":
                    if (FailSwitch) { FailSwitch = false; throw new IOException("fixture appearance switch failure"); }
                    var index = Mods.FindIndex(m => m.Id == args[2]); Mods[index] = Mods[index] with { Enabled = args[4] == "yes" }; result = new { ok = true }; break;
                case "settings": result = new { ok = true }; break;
                default: throw new InvalidOperationException("Unexpected storage fixture command: " + args[0]);
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }
}
