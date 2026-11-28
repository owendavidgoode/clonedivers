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
        check(catalog.Id == "build25480438-r16" && catalog.Artifacts.Count == 18 && catalog.Artifacts.Sum(a => a.Bytes) == 8588323 &&
            SquadFingerprint.Compute(catalog) == "r16-a26f",
            "the r16 preset pairs nine optional SDK-20/16 mods with nine required gameplay and author archives and computes match code r16-a26f");
        check(catalog.Artifacts.Count(a => a.Url.Contains("/owendavidgoode/clonedivers/")) == 15 && catalog.Artifacts.Count(a => !a.Url.Contains("/owendavidgoode/clonedivers/")) == 3 &&
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
        var swLoc = catalog.Artifacts.Single(a => a.Id == HellForgePreset.StarWarsLocalization);
        var dedicatedMelee = catalog.Artifacts.Single(a => a.Id == HellForgePreset.DedicatedMeleeSlot);
        var chaos = catalog.Artifacts.Single(a => a.Id == HellForgePreset.ChaosInvasions);
        var corpseCleanup = catalog.Artifacts.Single(a => a.Id == HellForgePreset.FasterCorpseCleanup);
        var heavySeaf = catalog.Artifacts.Single(a => a.Id == "local.heavy-seaf-presence");
        check(catalog.Artifacts.Where(a => a.Optional).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(HellForgePreset.Optional) &&
            HellForgePreset.Required.All(r => !HellForgePreset.Optional.Contains(r.Id)) &&
            cleanExit.Version == "0.2.1" && cleanExit.Bytes == 8401 && cleanExit.Sha256 == "f74153f38726cd440368daf1574c11d2ad4616518a47cf9fb23001d34dd792df" &&
            cleanExit.ContentHash == "0eadc6cc748a4b7d1ef87fbf704fb0bf1c62fd0fdbfe146a09cf161ec3c9b8f4" &&
            cleanExit.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-25/clean-exit-0.2.1.zip",
            "Clean Exit stays optional with the exact independently verified 0.2.1 In-Game Esc menu release pins");
        check(leafWriter.Name == "LEGO Compact Leaf Writer" && leafWriter.Version == "0.2.4" && leafWriter.Bytes == 266729 &&
            leafWriter.Sha256 == "49cda096ac5ae858fcb4fb1c09d4fda6ef0157e03078cdf90a502ac898cd7401" &&
            leafWriter.ContentHash == "4287eff52df086c4b0493723e63edb26a9f4c9d1284df43e93c6bdfbe0eeabd2" &&
            leafWriter.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/lego-compact-leaf-writer-0.2.4.zip",
            "LEGO Compact Leaf Writer is optional with the exact 0.2.4 SDK-20 relocated-EM release pins");
        check(yoda.Name == "Empire LEGO Yoda Death" && yoda.Version == "0.2.2" && yoda.Bytes == 54747 &&
            yoda.Sha256 == "f97b33e109bffc9f9c9f77d48a83c7d2b094f30e543f1cf8a1f42b0e7cfbdd68" &&
            yoda.ContentHash == "25b9df01a07e94fc79cfb2b820f13f013ccb05a5399048db2fa31e893b37d5af" &&
            yoda.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-26/empire-lego-yoda-0.2.2.zip",
            "Empire LEGO Yoda Death is optional with the exact 0.2.2 O(1) hash-indexed release pins");
        check(atst.Name == "Empire AT-ST Film-Scale Leaf Writer" && atst.Version == "0.2.6" && atst.Bytes == 40306 &&
            atst.Sha256 == "22179f06145c135e695c3042853553f4318dfac2e69f40d827eb51611924766d" &&
            atst.ContentHash == "b0b7837d5c64a76e1a9e8c983076788feff448d3c1bab66d62abdd49b0c83c66" &&
            atst.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/empire-atst-writer-0.2.6.zip",
            "Empire AT-ST Film-Scale Leaf Writer is optional with the exact 0.2.6 SDK-20 relocated-EM release pins");
        check(tieSwitch.Name == "Empire per-attack TIE (lab candidate)" && tieSwitch.Version == "0.3.2" && tieSwitch.Bytes == 25501 &&
            tieSwitch.Sha256 == "2269fb9c913f0f3da03fb4ab24be9e1af66599594f49a83d035f167cf53cbefd" &&
            tieSwitch.ContentHash == "ef62a3fc3e65ce392ea55d14495eba57e1543b5db28940cac519bf3a4c5eb6df" &&
            tieSwitch.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-26/empire-tie-switch-0.3.2.zip",
            "Empire per-attack TIE switch is optional with the exact 0.3.2 zero-CreateFileW mission guard release pins");
        check(cameraPullback.Name == "Empire & Clone Camera Pullback" && cameraPullback.Version == "0.1.5" && cameraPullback.Bytes == 35230 &&
            cameraPullback.Sha256 == "ca58387566fc31e64cfabfa8380f0c9439c6ceb6b871fa5007926c786c582bf2" &&
            cameraPullback.ContentHash == "d973fb81e72618642e5d4fa2670f1d85f75eacf8fcdd36758214b4d2776a9d66" &&
            cameraPullback.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/empire-camera-pullback-0.1.5.zip",
            "Empire & Clone Camera Pullback is optional with the exact 0.1.5 SDK-20 session-cached relocated-EM release pins");
        check(dedicatedMelee.Name == "Dedicated Melee Slot" && dedicatedMelee.Version == "1.5.2-clonedivers.4" && dedicatedMelee.Bytes == 55994 &&
            dedicatedMelee.Sha256 == "2523782a17941ce2ebf3ac0df8381171c05fb40b58b8c295e38d13a38362d676" &&
            dedicatedMelee.ContentHash == "42c7235aee004ea6c7a348e80a855606406b3f00acd8a21cf0021cbdaa29b997" &&
            dedicatedMelee.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/dedicated-melee-slot-1.5.2-clonedivers.4.zip",
            "Dedicated Melee Slot is optional with the exact 1.5.2-clonedivers.4 SDK-20 relocated-EM release pins");
        check(swLoc.Name == "Star Wars Localization Bridge" && swLoc.Version == "0.1.1" && swLoc.Bytes == 10577 &&
            swLoc.Sha256 == "b0ff55d0464ee6fa3b72693bd04b0bc28384773af04d21740e4e1f5a5ee9ddba" &&
            swLoc.ContentHash == "1590c83d7907fbc033d62e3a896d2dda2bddc2c935d3e6acd7715d9bf0dc3362" &&
            swLoc.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-25/starwars-localization-0.1.1.zip",
            "Star Wars Localization Bridge is optional with the exact 0.1.1 SEAF-free release pins");
        check(chaos.Name == "Chaos Invasions" && chaos.Version == "0.5.2" && chaos.Bytes == 84638 &&
            chaos.Sha256 == "e57017ca9f42f67f0a09d9e5659c048ebd3efc135fdd0e31e170db6b49bdfd68" &&
            chaos.ContentHash == "3d0975b9fe222b32452d784e7fb7c2b126319742d352fcd6b564b17374784b98" &&
            chaos.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/chaos-invasions-0.5.2.zip",
            "Chaos Invasions is optional with the exact 0.5.2 SDK-20 relocated-EM & Stormtrooper Difficulty 12 release pins");
        check(!corpseCleanup.Optional && corpseCleanup.Version == "1.0.6-clonedivers.4" && corpseCleanup.Bytes == 81988 &&
            corpseCleanup.Sha256 == "6c6d59f62d222fa393697e8bdff4436bf2b4a8833d487c948bb8f0eebe957284" &&
            corpseCleanup.ContentHash == "f09456ccff0da17fd87dcf44dd0ea3f261f79d09eb48812e2d6bcc967c71de5b" &&
            !heavySeaf.Optional && heavySeaf.Version == "1.2.0-clonedivers.8" && heavySeaf.Bytes == 114199 &&
            heavySeaf.Sha256 == "3aa65e16f59dc8c8c8bd0125ff5a3035ca29e4a43536a1a0565342a776022e63" &&
            heavySeaf.ContentHash == "52db31f447286d6687ab9e27cb3790b7a780a97c8599a9a6ee7c57618f0422bf",
            "Faster Corpse Cleanup (1.0.6-clonedivers.4) and Heavy SEAF Presence (1.2.0-clonedivers.8) are required in r16");
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
                "Empire per-attack TIE switch declares 0.3.1, gameplay false and per-player co-op scope");
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
