using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clonedivers;

namespace Clonedivers.Tests;

internal static class GameplayInstallTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var catalog = HellForgeGameplayCatalog.Pinned;
        catalog.Validate();
        check(catalog.Id == "build25480438-r8" && catalog.Artifacts.Count == 17 && catalog.Artifacts.Sum(a => a.Bytes) == 8494544,
            "the r8 preset pairs eight optional SDK-16 mods with nine required gameplay and author archives");
        check(catalog.Artifacts.Count(a => a.Url.Contains("/owendavidgoode/clonedivers/")) == 14 && catalog.Artifacts.Count(a => !a.Url.Contains("/owendavidgoode/clonedivers/")) == 3 &&
            catalog.Artifacts.All(a => !a.Url.Contains("hf-mod.com", StringComparison.OrdinalIgnoreCase)) &&
            catalog.Artifacts.All(a => !a.FileName.Contains("clear-skies", StringComparison.OrdinalIgnoreCase)),
            "r8 eliminates hf-mod.com dependencies via permissive mirrors, permitted SEAF packages, project/MIT code mods, and three direct author GitHub releases while excluding non-redistributable clear-skies");
        check(catalog.Artifacts.All(a => FileSafety.IsSha256(a.ContentHash) && FileSafety.IsSha256(a.Sha256)), "every gameplay dependency has independent archive and official content-hash pins");
        var cleanExit = catalog.Artifacts.Single(a => a.Id == HellForgePreset.CleanExit);
        var leafWriter = catalog.Artifacts.Single(a => a.Id == HellForgePreset.LegoCompactLeafWriter);
        var yoda = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireLegoYoda);
        var atst = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireAtstWriter);
        var tieSwitch = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireTieSwitch);
        var cameraPullback = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireCameraPullback);
        var dedicatedMelee = catalog.Artifacts.Single(a => a.Id == HellForgePreset.DedicatedMeleeSlot);
        var chaos = catalog.Artifacts.Single(a => a.Id == HellForgePreset.ChaosInvasions);
        var corpseCleanup = catalog.Artifacts.Single(a => a.Id == HellForgePreset.FasterCorpseCleanup);
        var heavySeaf = catalog.Artifacts.Single(a => a.Id == "local.heavy-seaf-presence");
        check(catalog.Artifacts.Where(a => a.Optional).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(HellForgePreset.Optional) &&
            HellForgePreset.Required.All(r => !HellForgePreset.Optional.Contains(r.Id)) &&
            cleanExit.Version == "0.2.0" && cleanExit.Bytes == 8192 && cleanExit.Sha256 == "6835354fff8264c6f29c33dfac71a0b6e4c4da7a401920bb76e1b604d2435932" &&
            cleanExit.ContentHash == "0006a02101583cf05e3bd9fa4e431409c18ff4ac72f59ae1ece8c7d9831a888a" &&
            cleanExit.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-08/clean-exit-0.2.0.zip",
            "Clean Exit stays optional with the exact independently verified 0.2.0 release pins");
        check(leafWriter.Name == "LEGO Compact Leaf Writer" && leafWriter.Version == "0.2.1" && leafWriter.Bytes == 254245 &&
            leafWriter.Sha256 == "f41439f351f5c9230028e4e7d74335a5b68be8f392ca9c9010f449fb73bf552e" &&
            leafWriter.ContentHash == "1897c694632418df33a5ceb66706d4466f3acf0bf426ff0061f6f948fec86fef" &&
            leafWriter.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-21/lego-compact-leaf-writer-0.2.1.zip",
            "LEGO Compact Leaf Writer is optional with the exact 0.2.1 D11 tuned release pins");
        check(yoda.Name == "Empire LEGO Yoda Death" && yoda.Version == "0.2.0" && yoda.Bytes == 53078 &&
            yoda.Sha256 == "7ecf857d719e190574fc9fe13cfe7e6c0f11126f4465c8bb2796167d4b60056c" &&
            yoda.ContentHash == "d896384245eae48b4a883a8cb561daae75cd61a739c9ac20f85ea54be474b043" &&
            yoda.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-19/empire-lego-yoda-0.2.0.zip",
            "Empire LEGO Yoda Death is optional with the exact 0.2.0 co-op release pins");
        check(atst.Name == "Empire AT-ST Film-Scale Leaf Writer" && atst.Version == "0.2.2" && atst.Bytes == 37771 &&
            atst.Sha256 == "d807baff203a0f82238600d6b10ad1890e46d95804c2bc75fbc15046abe87401" &&
            atst.ContentHash == "1040b25ecf28f875e5df0ced10a490d953813869308422c5d5a1fa8a4bc45751" &&
            atst.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-22/empire-atst-writer-0.2.2.zip",
            "Empire AT-ST Film-Scale Leaf Writer is optional with the exact 0.2.2 multi-exosuit release pins");
        check(tieSwitch.Name == "Empire per-attack TIE (lab candidate)" && tieSwitch.Version == "0.3.0" && tieSwitch.Bytes == 22644 &&
            tieSwitch.Sha256 == "491f437bbed022aaf6899b259784156341e6acec80bf1ff5a6116f106da06f5b" &&
            tieSwitch.ContentHash == "fe2c081780faecf75bc6f4d4a4e07df7ca9ddc332f32e4a148af4e460d2fbe7b" &&
            tieSwitch.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-19/empire-tie-switch-0.3.0.zip",
            "Empire per-attack TIE switch is optional with the exact 0.3.0 release pins");
        check(cameraPullback.Name == "Empire & Clone Camera Pullback" && cameraPullback.Version == "0.1.3" && cameraPullback.Bytes == 31555 &&
            cameraPullback.Sha256 == "b9acf9a83ef7b648e141f8ddceb447e5e9a69e54d155c8aeee6631af13c88c57" &&
            cameraPullback.ContentHash == "ae866e1ee48c921160fbdfa98a6e660dba3c99d40d303df23e282a778c0d9b13" &&
            cameraPullback.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-22/empire-camera-pullback-0.1.3.zip",
            "Empire & Clone Camera Pullback is optional with the exact 0.1.3 O(1) hot-prefix release pins");
        check(dedicatedMelee.Name == "Dedicated Melee Slot" && dedicatedMelee.Version == "1.5.2-clonedivers.2" && dedicatedMelee.Bytes == 53146 &&
            dedicatedMelee.Sha256 == "3dfe28c02ae4db9f344e8368c1f8bd2b717a723c3ac871c608fb027ea4c00e07" &&
            dedicatedMelee.ContentHash == "814a5d993dbf5f9f7bb09c7e68636e1c80ba06a57c8b1d4efdc4815df7b1c9a4" &&
            dedicatedMelee.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-22/dedicated-melee-slot-1.5.2-clonedivers.2.zip",
            "Dedicated Melee Slot is optional with the exact 1.5.2-clonedivers.2 co-op release pins");
        check(chaos.Name == "Chaos Invasions" && chaos.Version == "0.3.3" && chaos.Bytes == 42092 &&
            chaos.Sha256 == "1a4c3f992f270d40aafe7305947a84a6e326151847d27ece774165f24543e67a" &&
            chaos.ContentHash == "be9dc1cf956c391c8fdf4c307d4177a44fe25a30981dd3c4d49182d7a81215c7" &&
            chaos.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-22/chaos-invasions-0.3.3.zip",
            "Chaos Invasions is optional with the exact 0.3.3 D11 OOM-safe release pins");
        check(!corpseCleanup.Optional && corpseCleanup.Version == "1.0.6-clonedivers.1" && corpseCleanup.Bytes == 75523 &&
            !heavySeaf.Optional && heavySeaf.Version == "1.2.0-clonedivers.5" && heavySeaf.Bytes == 106285,
            "Faster Corpse Cleanup (1.0.6-clonedivers.1) and Heavy SEAF Presence (1.2.0-clonedivers.5) are required in r8");
        using (var stream = typeof(GameplayInstallTests).Assembly.GetManifestResourceStream("Clonedivers.Tests.CleanExit.json")!)
        using (var manifest = JsonDocument.Parse(stream))
            check(manifest.RootElement.GetProperty("id").GetString() == cleanExit.Id && manifest.RootElement.GetProperty("version").GetString() == cleanExit.Version &&
                !manifest.RootElement.GetProperty("gameplay").GetBoolean(), "Clean Exit declares gameplay false so the official runtime excludes it from co-op matching");
        using (var stream = typeof(GameplayInstallTests).Assembly.GetManifestResourceStream("Clonedivers.Tests.LegoCompactLeafWriter.json")!)
        using (var manifest = JsonDocument.Parse(stream))
            check(manifest.RootElement.GetProperty("id").GetString() == leafWriter.Id && manifest.RootElement.GetProperty("version").GetString() == leafWriter.Version &&
                !manifest.RootElement.GetProperty("gameplay").GetBoolean() && manifest.RootElement.GetProperty("coop").GetString() == "per-player",
                "LEGO Compact Leaf Writer declares 0.2.1, gameplay false and per-player co-op scope");
        using (var stream = typeof(GameplayInstallTests).Assembly.GetManifestResourceStream("Clonedivers.Tests.EmpireLegoYoda.json")!)
        using (var manifest = JsonDocument.Parse(stream))
            check(manifest.RootElement.GetProperty("id").GetString() == yoda.Id && manifest.RootElement.GetProperty("version").GetString() == yoda.Version &&
                !manifest.RootElement.GetProperty("gameplay").GetBoolean() && manifest.RootElement.GetProperty("coop").GetString() == "per-player",
                "Empire LEGO Yoda Death declares 0.2.0, gameplay false and per-player co-op scope");
        using (var stream = typeof(GameplayInstallTests).Assembly.GetManifestResourceStream("Clonedivers.Tests.EmpireTieSwitch.json")!)
        using (var manifest = JsonDocument.Parse(stream))
            check(manifest.RootElement.GetProperty("id").GetString() == tieSwitch.Id && manifest.RootElement.GetProperty("version").GetString() == tieSwitch.Version &&
                !manifest.RootElement.GetProperty("gameplay").GetBoolean() && manifest.RootElement.GetProperty("coop").GetString() == "per-player",
                "Empire per-attack TIE switch declares 0.3.0, gameplay false and per-player co-op scope");
        using (var r8Stream = typeof(GameplayInstallTests).Assembly.GetManifestResourceStream("Clonedivers.Forge.Gameplay.json")!)
        {
            var r8Catalog = JsonSerializer.Deserialize<HellForgeGameplayCatalog>(r8Stream, HellForgePack.Json)!;
            var r8Atst = r8Catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireAtstWriter);
            using (var atstStream = typeof(GameplayInstallTests).Assembly.GetManifestResourceStream("Clonedivers.Tests.EmpireAtstWriter.json")!)
            using (var atstManifest = JsonDocument.Parse(atstStream))
            {
                check(atstManifest.RootElement.GetProperty("id").GetString() == atst.Id &&
                    atstManifest.RootElement.GetProperty("version").GetString() == r8Atst.Version &&
                    !atstManifest.RootElement.GetProperty("gameplay").GetBoolean() &&
                    atstManifest.RootElement.GetProperty("coop").GetString() == "per-player",
                    "Empire AT-ST Writer source manifest matches staged r8 version (0.2.2), gameplay false and per-player co-op scope");
            }
            var r8Chaos = r8Catalog.Artifacts.Single(a => a.Id == HellForgePreset.ChaosInvasions);
            using var stream = typeof(GameplayInstallTests).Assembly.GetManifestResourceStream("Clonedivers.Tests.ChaosInvasions.json")!;
            using var manifest = JsonDocument.Parse(stream);
            check(manifest.RootElement.GetProperty("id").GetString() == chaos.Id &&
                manifest.RootElement.GetProperty("version").GetString() == r8Chaos.Version &&
                manifest.RootElement.GetProperty("gameplay").GetBoolean(),
                "Chaos Invasions source manifest matches staged r8 version and declares gameplay true as a host-driven optional gameplay mod");
        }
        var clone = JsonSerializer.Deserialize<HellForgeGameplayCatalog>(JsonSerializer.Serialize(catalog, HellForgePack.Json), HellForgePack.Json)!;
        clone.Artifacts[clone.Artifacts.FindIndex(a => a.Id == yoda.Id)] = yoda with { Optional = false };
        try { clone.Validate(); check(false, "catalog validation must reject required-flagged optional mod"); }
        catch (InvalidDataException) { check(true, "catalog validation enforces HellForgePreset.Optional membership"); }
        foreach (var entry in new[] { "../escape.dll", "C:/escape.dll", "safe/../../escape.dll", "safe/entry.dll" })
        {
            var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("fixture"); }
            var artifact = new GameplayArtifact("fixture", "fixture", "1", "https://example.invalid/fixture", new FileInfo(file).Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(), new string('a', 64), "fixture");
            var unsafePath = entry != "safe/entry.dll";
            try { await HellForgeGameplayCatalog.VerifyArchiveAsync(file, artifact); check(!unsafePath, "archive path is confined: " + entry); }
            catch (InvalidDataException) { check(unsafePath, "unsafe archive cannot reach the official importer: " + entry); }
            if (!unsafePath)
            {
                try { await HellForgeGameplayCatalog.VerifyArchiveAsync(file, artifact with { Sha256 = new string('b', 64) }); check(false, "wrong gameplay hash must fail"); }
                catch (InvalidDataException) { check(true, "size-correct wrong-hash gameplay artifacts fail before import"); }
            }
        }
        await RecordTests(root, check);
    }

    static async Task RecordTests(string root, Action<bool, string> check)
    {
        var install = Path.Combine(root, "gameplay-records", "HellForge"); Directory.CreateDirectory(Path.Combine(install, "mods"));
        var commands = new RecordingCommands(install);
        var client = new HellForgeClient(install, commands) { RequireCliQuiescent = () => { } };
        var record = client.PackIntentPath + ".gameplay.json"; Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        var backup = Path.Combine(client.CacheRoot, "backups", "gameplay-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
        void Write(string catalog, string state, string? owner = null) => File.WriteAllText(record, JsonSerializer.Serialize(new {
            Root = owner ?? client.Root, Catalog = catalog, Backup = backup, SnapshotSha256 = new string('a', 64), State = state }));
        foreach (var revision in new[] { "build25480438-r1", "build25480438-r2", "build25480438-r3", "build25480438-r4", "build25480438-r5", "build25480438-r6", "build25480438-r7" })
        foreach (var state in new[] { "applied", "restored" })
        {
            Write(revision, state);
            var problem = client.InterruptedGameplayProblem(); await client.RecoverGameplayAsync();
            check(problem == null && File.Exists(record), "a completed " + state + " record from " + revision + " stays readable without blocking setup or recovery");
        }
        Write(HellForgeGameplayCatalog.Pinned.Id, "pending");
        check(client.InterruptedGameplayProblem()?.StartsWith("Gameplay setup was interrupted", StringComparison.Ordinal) == true, "a pending record from the pinned catalog still requires recovery");
        Write(HellForgeGameplayCatalog.Pinned.Id, "applied", Path.Combine(root, "other"));
        check(client.InterruptedGameplayProblem()?.Contains("needs review") == true, "a record for another installation still needs review");
        foreach (var revision in new[] { "build25480438-r1", "build25480438-r2", "build25480438-r3", "build25480438-r4", "build25480438-r5", "build25480438-r6", "build25480438-r7" })
        {
            Write(revision, "pending");
            check(client.InterruptedGameplayProblem()?.StartsWith("Gameplay setup from an earlier launcher", StringComparison.Ordinal) == true, "a pending " + revision + " record names its own recovery path");
            try { await client.RecoverGameplayAsync(); check(false, "an earlier catalog's pending record must not report a restore"); }
            catch (InvalidOperationException error)
            {
                check(error.Message.Contains(backup) && !File.Exists(record) && Directory.Exists(backup) && Directory.EnumerateFiles(Path.GetDirectoryName(record)!, "*.gameplay.json.retired-*").Any() &&
                    client.InterruptedGameplayProblem() == null && commands.Calls.Count == 0, "recovering an earlier catalog's pending record retires it, keeps its backup and unblocks setup");
            }
        }
        var zip = Path.Combine(backup, new string('b', 64) + ".zip");
        check(HellForgeClient.BackupHashSource(new(HellForgePreset.GuardDogs, "Smarter Guard Dogs", true, Path.Combine(install, "mods", "guard-dogs"), ["pack"]), zip) == zip &&
            HellForgeClient.BackupHashSource(new("local.operation-reroller", "Operation Reroller", true, Path.Combine(install, "mods", "reroller"), ["code"]), zip) == Path.Combine(install, "mods", "reroller"),
            "a folder-installed cosmetic pack's backup is attested by its ZIP, not by the CLI's unhashable pack folder");
    }

    sealed class RecordingCommands(string root) : IHellForgeCommands
    {
        public List<string[]> Calls { get; } = [];
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls.Add(args.ToArray());
            throw new IOException("Fixture CLI cannot run " + args[0] + " for " + root);
        }
    }

    internal static void RequireIsolated(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var local = @"D:\CodexScratch\hellforge\windows-onboarding-20261104\real-bootstrap";
        var temporary = full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetTempPath()) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).StartsWith("forge-gameplay-install-test-", StringComparison.Ordinal);
        if (!temporary && !(full.Equals(local, StringComparison.OrdinalIgnoreCase) || full.Equals(@"D:\CodexScratch\hellforge\forge-upgrade-20261105\probe-0.4.0", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Real recovery trials require an explicitly isolated test installation.");
    }
    internal static async Task RealRecovery(string root)
    {
        RequireIsolated(root);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var official = new HellForgeCommands(full);
        var client = new HellForgeClient(full, official);
        if (client.InterruptedGameplayProblem() != null) await client.RecoverGameplayAsync();
        var catalog = HellForgeGameplayCatalog.Pinned;
        var before = await client.ModsAsync();
        if (before.Mods.Any(m => !catalog.Order.Contains(m.Id))) throw new InvalidOperationException("Test root contains unrelated mods; no trial changes were made.");
        foreach (var artifact in catalog.Artifacts)
            await HellForgeGameplayCatalog.VerifyArchiveAsync(Path.Combine(client.GameplayCache, artifact.FileName), artifact);
        var profiles = Path.Combine(full, "profiles");
        if (!Directory.EnumerateFiles(profiles, "*", SearchOption.AllDirectories).Any())
            await File.WriteAllTextAsync(Path.Combine(profiles, "preset-test-sentinel.txt"), "Isolated preservation fixture; no account or rewards.");
        var profileFiles = Directory.EnumerateFiles(profiles, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        // Only the known throwaway installation's catalog artifacts are removed.
        foreach (var mod in before.Mods.Where(m => catalog.Artifacts.Any(a => a.Id == m.Id)))
            await official.RunAsync(["mod-remove", "--id", mod.Id]);
        // Reset the isolated fixture's one-time defaults before testing a fresh install.
        File.Delete(client.PackIntentPath + ".gameplay.json");
        var clean = await client.ModsAsync();
        if (clean.Mods.Count != 3) throw new InvalidDataException("Clean fixture must contain exactly three official bundled mods.");
        var fault = new FailOnce(official);
        try { await new HellForgeClient(full, fault).InstallGameplayAsync(client.GameplayCache); throw new Exception("Injected failure did not happen."); }
        catch (IOException error) when (error.Message.Contains("previous mod selection was restored", StringComparison.Ordinal)) { }
        var restored = await client.ModsAsync();
        if (restored.Mods.Count != 3 || client.InterruptedGameplayProblem() != null || (await client.MissingGameplayAsync()).Count != catalog.Artifacts.Count)
            throw new InvalidDataException("Fresh-import rollback failed.");
        Console.WriteLine("PASS: actual failed preset installation restored all three bundled mods and cleared recovery");
        try { await new HellForgeClient(full, new LoseAcknowledgement(official)).InstallGameplayAsync(client.GameplayCache); throw new Exception("Lost acknowledgement was not injected."); }
        catch (HellForgeCommandOutcomeUnknownException) { }
        if (client.InterruptedGameplayProblem() == null) throw new InvalidDataException("An unknown import outcome did not preserve recovery intent.");
        var intentFile = client.PackIntentPath + ".gameplay.json";
        using (var intent = JsonDocument.Parse(File.ReadAllText(intentFile)))
        {
            var snapshotFile = Path.Combine(intent.RootElement.GetProperty("Backup").GetString()!, "before.json");
            var snapshotBytes = File.ReadAllBytes(snapshotFile);
            await File.WriteAllTextAsync(snapshotFile, "corrupted snapshot");
            try { await client.RecoverGameplayAsync(); throw new Exception("Corrupt recovery metadata was accepted."); }
            catch (InvalidDataException) { }
            if (client.InterruptedGameplayProblem() == null) throw new InvalidDataException("Invalid recovery metadata lost its pending state.");
            await File.WriteAllBytesAsync(snapshotFile, snapshotBytes);
        }
        await new HellForgeClient(full, official).RecoverGameplayAsync();
        if ((await client.ModsAsync()).Mods.Count != 3 || client.InterruptedGameplayProblem() != null) throw new InvalidDataException("Restart recovery did not restore the clean fixture.");
        Console.WriteLine("PASS: actual lost import acknowledgement stays pending, rejects damaged backup metadata and recovers through a fresh client");
        await client.InstallGameplayAsync(client.GameplayCache);
        if ((await client.MissingGameplayAsync()).Count != 0) throw new InvalidDataException("Actual retry left missing dependencies.");
        Console.WriteLine("PASS: actual retry installed and verified all eleven preset mods");
        var settings = Path.Combine(full, "mods", "hellforge-mods.json"); var originalSettings = File.ReadAllBytes(settings);
        await client.InstallGameplayAsync(client.GameplayCache);
        if (!File.ReadAllBytes(settings).SequenceEqual(originalSettings)) throw new InvalidDataException("An already complete preset was rewritten.");
        Console.WriteLine("PASS: actual repeated setup is a no-op with byte-identical mod settings");
        var installed = (await client.ModsAsync()).Mods.Single(m => m.Id == "pack.612eaf70-d682-43c7-9efd-16dcc695f977");
        var payload = Directory.EnumerateFiles(installed.Folder, "*", SearchOption.AllDirectories).First(p => Path.GetFileName(p) != "manifest.json" && new FileInfo(p).Length > 0);
        if (!Path.GetFullPath(payload).StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Test payload escaped isolated root.");
        using (var output = new FileStream(payload, FileMode.Append, FileAccess.Write)) output.WriteByte(0xff);
        if ((await client.MissingGameplayAsync()).Count != 1) throw new InvalidDataException("Changed unpacked data was not detected.");
        await client.InstallGameplayAsync(client.GameplayCache);
        if ((await client.MissingGameplayAsync()).Count != 0) throw new InvalidDataException("Official reimport reused the corrupt data cache.");
        Console.WriteLine("PASS: actual data-cache corruption is detected and repaired through official import into a fresh cache");
        foreach (var file in profileFiles)
            if (!File.Exists(file.Key) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Key))) != file.Value) throw new InvalidDataException("Gameplay setup changed a profile file.");
        Console.WriteLine("PASS: actual profile file bytes survived failure, retry and repair");
    }

    sealed class FailOnce(IHellForgeCommands inner) : IHellForgeCommands
    {
        bool failed;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (!failed && args[0] == "mod-pack") { failed = true; throw new IOException("Deliberate fixture configuration failure after imports."); }
            return inner.RunAsync(args, ct);
        }
    }
    sealed class LoseAcknowledgement(IHellForgeCommands inner) : IHellForgeCommands
    {
        bool failed;
        public async Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            var response = await inner.RunAsync(args, ct);
            if (!failed && args[0] == "mod-add") { failed = true; throw new HellForgeCommandOutcomeUnknownException("Fixture lost the successful import acknowledgement."); }
            return response;
        }
    }
}
