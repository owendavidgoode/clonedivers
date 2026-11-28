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
        check(catalog.Id == "build25480438-r17" && catalog.Artifacts.Count == 19 && catalog.Artifacts.Sum(a => a.Bytes) == 8715407 &&
            SquadFingerprint.Compute(catalog) == "r17-a26f",
            "the r17 preset pairs ten optional SDK-20/16 mods with nine required gameplay and author archives and computes match code r17-a26f");
        check(catalog.Artifacts.Count(a => a.Url.Contains("/owendavidgoode/clonedivers/")) == 16 && catalog.Artifacts.Count(a => !a.Url.Contains("/owendavidgoode/clonedivers/")) == 3 &&
            catalog.Artifacts.All(a => !a.Url.Contains("hf-mod.com", StringComparison.OrdinalIgnoreCase)) &&
            catalog.Artifacts.All(a => !a.FileName.Contains("clear-skies", StringComparison.OrdinalIgnoreCase)),
            "r17 eliminates hf-mod.com dependencies via permissive mirrors, permitted SEAF packages, project/MIT code mods, and three direct author GitHub releases while excluding non-redistributable clear-skies");
        check(catalog.Artifacts.All(a => FileSafety.IsSha256(a.ContentHash) && FileSafety.IsSha256(a.Sha256)), "every gameplay dependency has independent archive and official content-hash pins");
        var cleanExit = catalog.Artifacts.Single(a => a.Id == HellForgePreset.CleanExit);
        var leafWriter = catalog.Artifacts.Single(a => a.Id == HellForgePreset.LegoCompactLeafWriter);
        var yoda = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireLegoYoda);
        var atst = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireAtstWriter);
        var tieSwitch = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireTieSwitch);
        var cameraPullback = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireCameraPullback);
        var walkerHud = catalog.Artifacts.Single(a => a.Id == HellForgePreset.TitanfallWalkerHud);
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
        check(yoda.Name == "Empire LEGO Yoda Death & Stormtrooper Salute" && yoda.Version == "0.2.3" && yoda.Bytes == 101380 &&
            yoda.Sha256 == "44d6b8de840b9256cba0cc4c5804f7161c276e734861c640b5d5d58735756c8a" &&
            yoda.ContentHash == "ae37854010066ee31639b8e747fefb9548000a05692586d97968d00a75861ca7" &&
            yoda.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/empire-lego-yoda-0.2.3.zip",
            "Empire LEGO Yoda Death & Stormtrooper Salute is optional with the exact 0.2.3 Salute WAV release pins");
        check(atst.Name == "Empire AT-ST Film-Scale Leaf Writer" && atst.Version == "0.2.7" && atst.Bytes == 53088 &&
            atst.Sha256 == "30b27f5ccc836782f006caf003ffbd745dacfa3a4445914059c39555b60f2703" &&
            atst.ContentHash == "9bf77704f221794a58a46dc463e2511ee2961e004d8e8197bd1f627fffdacba7" &&
            atst.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/empire-atst-writer-0.2.7.zip",
            "Empire AT-ST Film-Scale Leaf Writer is optional with the exact 0.2.7 cab-lift SDK-20 release pins");
        check(tieSwitch.Name == "Star Wars Per-Attack Air Wing (TIE & Republic)" && tieSwitch.Version == "0.4.0" && tieSwitch.Bytes == 33395 &&
            tieSwitch.Sha256 == "1f7a0fc564aadbb550d72e3da9e51d05e35cc14e0ca0dfbb30ebfdc2b8551d46" &&
            tieSwitch.ContentHash == "3ea07d28dbb4944b40f8f2b890b72c438162c5c3cbbb4676da2c7301d38c8816" &&
            tieSwitch.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/empire-tie-switch-0.4.0.zip" &&
            HellForgePreset.RequiredCompanionsForMode("clonedivers").Contains(HellForgePreset.EmpireTieSwitch) &&
            HellForgePreset.RequiredCompanionsForMode("empire").Contains(HellForgePreset.EmpireTieSwitch),
            "Star Wars Per-Attack Air Wing 0.4.0 is optional and enabled across both Empire and Clonedivers mode presets");
        check(cameraPullback.Name == "Empire & Clone Camera Pullback" && cameraPullback.Version == "0.1.6" && cameraPullback.Bytes == 36423 &&
            cameraPullback.Sha256 == "8ed5df776d87fa7a025cab804f12b36b411f85d80569cdd8169a4cefecc975b3" &&
            cameraPullback.ContentHash == "a9769817d7414c5e3ce811e7f05e6f10b3a65990878f2ce0dd59b7d2a5c537e3" &&
            cameraPullback.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/empire-camera-pullback-0.1.6.zip",
            "Empire & Clone Camera Pullback is optional with the exact 0.1.6 SDK-20 1P/3P peer-handoff release pins");
        check(walkerHud.Name == "AT-TE Titanfall Tactical Cockpit HUD" && walkerHud.Version == "0.1.0" && walkerHud.Bytes == 27891 &&
            walkerHud.Sha256 == "a29477da162193ae85eb43b1caa8be95acc4d25031f5ea054090eea1b6ae81ab" &&
            walkerHud.ContentHash == "ca5751c55cec511c3e0b6581afc0b63d490215154de9d4bbe7be50d074c802ab" &&
            walkerHud.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/titanfall-walker-hud-0.1.0.zip" &&
            HellForgePreset.RequiredCompanionsForMode("clonedivers").Contains(HellForgePreset.TitanfallWalkerHud) &&
            HellForgePreset.RequiredCompanionsForMode("empire").Contains(HellForgePreset.TitanfallWalkerHud),
            "AT-TE Titanfall Tactical Cockpit HUD 0.1.0 is registered and enabled across Clonedivers and Empire presets");
        check(dedicatedMelee.Name == "Dedicated Melee Slot" && dedicatedMelee.Version == "1.5.2-clonedivers.5" && dedicatedMelee.Bytes == 60027 &&
            dedicatedMelee.Sha256 == "2162941747f378487146683fbc750eae3c89e0a0ce9822b91901290911149575" &&
            dedicatedMelee.ContentHash == "d50d4c494adf012556f5699584474e4aa77aa513aa6ca31797886d5cfff04bc6" &&
            dedicatedMelee.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/dedicated-melee-slot-1.5.2-clonedivers.5.zip",
            "Dedicated Melee Slot is optional with the exact 1.5.2-clonedivers.5 SDK-20 residency-gated release pins");
        check(swLoc.Name == "Star Wars Localization Bridge" && swLoc.Version == "0.2.0" && swLoc.Bytes == 20693 &&
            swLoc.Sha256 == "01bdedc065c029d6ca0c0b8216dd452b8898239674f4d9ab7413f203b1921cf1" &&
            swLoc.ContentHash == "cae727ce6ecadf901fbbb4f09ac1c74028dd6c1f5851ee4e8697b1b5acd8f198" &&
            swLoc.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/starwars-localization-0.2.0.zip",
            "Star Wars Localization Bridge is optional with the exact 0.2.0 Death Star & Gonk Droid release pins");
        check(chaos.Name == "Chaos Invasions" && chaos.Version == "0.5.2" && chaos.Bytes == 97779 &&
            chaos.Sha256 == "3bc50fe74c9eb4297ec3caed8680bb737a1faf0b7b74bb3900a7fa64a7b075a1" &&
            chaos.ContentHash == "5be47603d166f8fd455849efc6a99d1ca452133bf1c7e54028fce5987d76bbb6" &&
            chaos.Url == "https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-28/chaos-invasions-0.5.2.zip",
            "Chaos Invasions is optional with the exact 0.5.2 SDK-20 air-run & AT-TE support release pins");
        check(!corpseCleanup.Optional && corpseCleanup.Version == "1.0.6-clonedivers.4" && corpseCleanup.Bytes == 81710 &&
            corpseCleanup.Sha256 == "8255f2ad5d25fce4a41db6da938336f5422e0b3d0f5a87b1ee7ced321128c144" &&
            corpseCleanup.ContentHash == "33383f70cc39aa524fd0e34ccf23f481e2107f38924c53256646b79b92f1ea2b" &&
            !heavySeaf.Optional && heavySeaf.Version == "1.2.0-clonedivers.8" && heavySeaf.Bytes == 117878 &&
            heavySeaf.Sha256 == "93a44665b4ab8ad4b0d0a9f475e0ee2519ecb9cd206291e03de565a8826185de" &&
            heavySeaf.ContentHash == "8d74b84423eca6fa068d48322b2c7243ee03bcbade1efdad58691e1aeea2e7e5",
            "Faster Corpse Cleanup (1.0.6-clonedivers.4) and Heavy SEAF Presence (1.2.0-clonedivers.8) are required in r17");
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
        await PackArchiveNullRegressionTests(root, check);
    }

    static async Task PackArchiveNullRegressionTests(string root, Action<bool, string> check)
    {
        var catalog = HellForgeGameplayCatalog.Pinned;
        var originalArtifacts = catalog.Artifacts.ToList();
        var install = Path.Combine(root, "pack-archive-null-050", "HellForge");
        Directory.CreateDirectory(Path.Combine(install, "mods"));
        try
        {
            var cli = new Fake050CliCommands(install);
            var client = new HellForgeClient(install, cli) { RequireCliQuiescent = () => { } };
            Directory.CreateDirectory(client.GameplayCache);

            // Build deterministic fixture .zip archives for all 19 r17 artifacts, matching real 0.5.0-beta structure:
            // - pack.* archives hold manifest.json + option patch subfolders (no hellforge-mod.json)
            // - code/data mods hold hellforge-mod.json + payload
            for (int i = 0; i < catalog.Artifacts.Count; i++)
            {
                var orig = catalog.Artifacts[i];
                var tempPath = Path.Combine(client.GameplayCache, $"fixture-{i}.zip");
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    if (orig.Id.StartsWith("pack.", StringComparison.Ordinal))
                    {
                        var guid = orig.Id["pack.".Length..];
                        object[] opts = orig.Id == HellForgePreset.GuardDogs
                            ? [
                                new { Name = "Smarter Guard Dogs and Sentries", Include = new[] { "core" } },
                                new { Name = "Guard Dogs", Include = new[] { "core" } },
                                new { Name = "Sentries", Include = new[] { "core" } },
                                new { Name = "Intelligence", Include = new[] { "core" } },
                                new { Name = "Safety", Include = new[] { "core" } },
                                new { Name = "Sentry Safety", Include = new[] { "core" } },
                                new { Name = "SEAF Safety", Include = new[] { "core" } },
                                new { Name = "Targeting Laser", Include = new[] { "core" } },
                                new { Name = "Targeting Marker", Include = new[] { "core" } }
                            ]
                            : [new { Name = orig.Name, Include = new[] { "core" } }];
                        using (var w = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                            w.Write(JsonSerializer.Serialize(new { Version = 1, Guid = guid, Name = orig.Name, Options = opts }));
                        using (var w = new StreamWriter(archive.CreateEntry("core/0000000000000001.patch_0").Open()))
                            w.Write("clean-pack-payload:" + orig.Id);
                    }
                    else
                    {
                        using (var w = new StreamWriter(archive.CreateEntry("hellforge-mod.json").Open()))
                            w.Write(JsonSerializer.Serialize(new { id = orig.Id, name = orig.Name, version = orig.Version, gameplay = orig.Id == HellForgePreset.ChaosInvasions }));
                        using (var w = new StreamWriter(archive.CreateEntry("payload.bin").Open()))
                            w.Write("clean-mod-payload:" + orig.Id + ":" + orig.Version);
                    }
                }
                var bytes = File.ReadAllBytes(tempPath);
                var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var contentHashJson = await cli.RunAsync(["mod-hash", "--mod", tempPath]);
                var contentHash = contentHashJson.GetProperty("hash").GetString()!;
                var updated = orig with { Bytes = bytes.Length, Sha256 = sha, ContentHash = contentHash };
                File.Move(tempPath, Path.Combine(client.GameplayCache, updated.FileName), overwrite: true);
                catalog.Artifacts[i] = updated;
            }

            // 1. Fresh InstallGameplayAsync on 0.5.0-beta where pack.* imports unpack to folders with archive: null
            await client.InstallGameplayAsync(client.GameplayCache, activeMode: "empire");
            var installedMods = await client.ModsAsync();
            var packMods = installedMods.Mods.Where(m => m.Id.StartsWith("pack.", StringComparison.Ordinal)).ToList();
            bool directFolderHashThrows = false;
            try { await cli.RunAsync(["mod-hash", "--mod", packMods[0].Folder]); }
            catch (IOException) { directFolderHashThrows = true; }

            var tempHashDir = Path.Combine(client.CacheRoot, "temp");
            bool noTempZipLeak = !Directory.Exists(tempHashDir) || !Directory.EnumerateFiles(tempHashDir, "*.zip").Any();
            var missingAfterInstall = await client.MissingGameplayAsync("empire");
            check(packMods.Count == 3 && packMods.All(m => m.Archive == null) && directFolderHashThrows &&
                missingAfterInstall.Count == 0 && noTempZipLeak,
                "InstallGameplayAsync and MissingGameplayAsync verify all 3 archive:null pack.* folders via ephemeral temp zips with 0 leaked temp files while non-zip pack.* mod-hash throws IOException (#47)");

            // 2. Unpacked pack.* corruption is detected when archive is null and Rollback (RestoreGameplayAsync) succeeds when snapshot.Backups holds pack.* mods!
            var smarterSeafMod = installedMods.Mods.Single(m => m.Id == HellForgePreset.SmarterSeaf);
            var seafPatch = Path.Combine(smarterSeafMod.Folder, "core", "0000000000000001.patch_0");
            File.WriteAllText(seafPatch, "drifted-pack-payload-before-upgrade");
            var missingDrifted = await client.MissingGameplayAsync("empire");
            check(missingDrifted.Count == 1 && missingDrifted[0].Id == HellForgePreset.SmarterSeaf,
                "MissingGameplayAsync detects changed files inside an archive:null pack.* folder");

            cli.FailNextModPack = true;
            bool rolledBackCleanly = false;
            try { await client.InstallGameplayAsync(client.GameplayCache, activeMode: "empire"); }
            catch (IOException ex) when (ex.Message.Contains("previous mod selection was restored", StringComparison.Ordinal)) { rolledBackCleanly = true; }
            check(rolledBackCleanly && client.InterruptedGameplayProblem() == null &&
                File.ReadAllText(seafPatch) == "drifted-pack-payload-before-upgrade",
                "RestoreGameplayAsync rollback succeeds on 0.5.0-beta when snapshot.Backups contains archive:null pack.* mods (#47)");

            // 3. Wedged State = "pending" recovery (RecoverGameplayAsync) when snapshot.Backups holds archive:null pack.* mods!
            cli.LoseNextModAddAck = true;
            bool lostAck = false;
            try { await client.InstallGameplayAsync(client.GameplayCache, activeMode: "empire"); }
            catch (HellForgeCommandOutcomeUnknownException) { lostAck = true; }
            check(lostAck && client.InterruptedGameplayProblem() != null,
                "interrupted pack.* repair leaves State = pending for RecoverGameplayAsync");

            var freshRecoveryClient = new HellForgeClient(install, cli) { RequireCliQuiescent = () => { } };
            await freshRecoveryClient.RecoverGameplayAsync();
            check(freshRecoveryClient.InterruptedGameplayProblem() == null &&
                File.ReadAllText(seafPatch) == "drifted-pack-payload-before-upgrade",
                "RecoverGameplayAsync cleanly recovers an installation wedged in State = pending with archive:null pack.* backups (#47)");

            // 4. Subsequent retry repairs Smarter SEAF back to clean pinned hash with 0 missing artifacts
            await freshRecoveryClient.InstallGameplayAsync(freshRecoveryClient.GameplayCache, activeMode: "empire");
            check((await freshRecoveryClient.MissingGameplayAsync("empire")).Count == 0 &&
                File.ReadAllText(seafPatch) == "clean-pack-payload:" + HellForgePreset.SmarterSeaf,
                "subsequent InstallGameplayAsync after RecoverGameplayAsync repairs archive:null pack.* content cleanly");
        }
        finally
        {
            catalog.Artifacts.Clear();
            catalog.Artifacts.AddRange(originalArtifacts);
        }
    }

    sealed class Fake050CliCommands : IHellForgeCommands
    {
        readonly string root;
        public List<HellForgeMod> Mods { get; } = [];
        public bool FailNextModPack;
        public bool LoseNextModAddAck;

        public Fake050CliCommands(string root)
        {
            this.root = root;
            foreach (var (id, name, ver) in new[]
            {
                ("hellforge.editor", "HellForge Editor", "0.4.2"),
                (HellForgeClient.DifficultyId, "Hell Forge Difficulty", "0.16.1"),
                ("skyeshade.vanilla-plus", "Vanilla Plus", "")
            })
            {
                var dir = Path.Combine(root, "mods", id);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "hellforge-mod.json"), JsonSerializer.Serialize(new { id, name, version = ver }));
                Mods.Add(new(id, name, true, dir, ["bundled", "code"], ver, Archive: null));
            }
        }

        static string ComputeModHashFromFolder(string folder, string id, string version)
        {
            var sb = new StringBuilder();
            sb.Append(id).Append('|').Append(version).Append('\n');
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => (Full: f, Rel: Path.GetRelativePath(folder, f).Replace('\\', '/')))
                .Where(x => !x.Rel.Split('/').Any(p => p.StartsWith('.')))
                .OrderBy(x => x.Rel, StringComparer.Ordinal))
            {
                var fileSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Full)));
                sb.Append(file.Rel).Append('|').Append(fileSha).Append('\n');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }

        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            object result;
            switch (args[0])
            {
                case "status":
                    result = new
                    {
                        install = root,
                        hellforge = HellForgeRelease.Version,
                        engine = HellForgeRelease.Engine,
                        modSdk = HellForgeRelease.Sdk,
                        gameBuild = HellForgePack.SupportedBuild,
                        networkBlock = "wfp",
                        ready = new { gameCopy = true, sessionRunner = true, linuxNamespace = false },
                        session = new { running = false, lastStatus = (string?)null, lastFailure = (string?)null, lastRun = (string?)null },
                        settings = new { multiplayer = true, localPlayFab = true, sandbox = true }
                    };
                    break;
                case "mods":
                    result = new
                    {
                        mods = Mods.Select(m => new
                        {
                            id = m.Id,
                            name = m.Name,
                            enabled = m.Enabled,
                            folder = m.Folder,
                            archive = m.Archive,
                            tags = m.Tags,
                            version = m.Version,
                            options = m.Options?.Select(o => new { name = o.Name, on = o.On })
                        }),
                        problems = Array.Empty<string>(),
                        modlist = "fixture"
                    };
                    break;
                case "mod-hash":
                {
                    var target = args[2];
                    if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        var mf = Path.Combine(target, "hellforge-mod.json");
                        if (!File.Exists(mf)) throw new FileNotFoundException($"HellForge: Could not find file '{mf}'.");
                        using var doc = JsonDocument.Parse(File.ReadAllText(mf));
                        var id = doc.RootElement.GetProperty("id").GetString()!;
                        var ver = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                        result = new { hash = ComputeModHashFromFolder(target, id, ver) };
                        break;
                    }
                    var tempExtract = Path.Combine(Path.GetTempPath(), "fake050-mod-hash-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        ZipFile.ExtractToDirectory(target, tempExtract);
                        var mf = Path.Combine(tempExtract, "hellforge-mod.json");
                        if (File.Exists(mf))
                        {
                            using var doc = JsonDocument.Parse(File.ReadAllText(mf));
                            var id = doc.RootElement.GetProperty("id").GetString()!;
                            var ver = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                            result = new { hash = ComputeModHashFromFolder(tempExtract, id, ver) };
                        }
                        else
                        {
                            var packMf = Path.Combine(tempExtract, "manifest.json");
                            if (!File.Exists(packMf)) throw new InvalidDataException("Archive holds no mod.");
                            using var doc = JsonDocument.Parse(File.ReadAllText(packMf));
                            var id = "pack." + doc.RootElement.GetProperty("Guid").GetString()!;
                            result = new { hash = ComputeModHashFromFolder(tempExtract, id, "") };
                        }
                    }
                    finally { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true); }
                    break;
                }
                case "mod-add":
                {
                    var zipPath = args[2];
                    var tempExtract = Path.Combine(Path.GetTempPath(), "fake050-mod-add-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        ZipFile.ExtractToDirectory(zipPath, tempExtract);
                        var mf = Path.Combine(tempExtract, "hellforge-mod.json");
                        if (File.Exists(mf))
                        {
                            using var doc = JsonDocument.Parse(File.ReadAllText(mf));
                            var id = doc.RootElement.GetProperty("id").GetString()!;
                            var name = doc.RootElement.GetProperty("name").GetString()!;
                            var ver = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                            var dest = Path.Combine(root, "mods", id);
                            if (Directory.Exists(dest)) Directory.Delete(dest, true);
                            ZipFile.ExtractToDirectory(zipPath, dest);
                            var prev = Mods.FirstOrDefault(m => m.Id == id);
                            var enabled = prev?.Enabled ?? true;
                            Mods.RemoveAll(m => m.Id == id);
                            Mods.Add(new(id, name, enabled, dest, ["code"], ver, Archive: null));
                            result = new { id, installed = dest };
                        }
                        else
                        {
                            var packMf = Path.Combine(tempExtract, "manifest.json");
                            using var doc = JsonDocument.Parse(File.ReadAllText(packMf));
                            var id = "pack." + doc.RootElement.GetProperty("Guid").GetString()!;
                            var name = doc.RootElement.GetProperty("Name").GetString()!;
                            var opts = doc.RootElement.TryGetProperty("Options", out var optArr) && optArr.ValueKind == JsonValueKind.Array
                                ? optArr.EnumerateArray().Select(o => new HellForgeOption(o.GetProperty("Name").GetString()!, true)).ToArray()
                                : null;
                            var dest = Path.Combine(root, "mods", id);
                            if (Directory.Exists(dest)) Directory.Delete(dest, true);
                            ZipFile.ExtractToDirectory(zipPath, dest);
                            var prev = Mods.FirstOrDefault(m => m.Id == id);
                            var enabled = prev?.Enabled ?? true;
                            if (prev?.Options != null && opts != null)
                            {
                                var prevMap = prev.Options.ToDictionary(o => o.Name, o => o.On, StringComparer.Ordinal);
                                opts = opts.Select(o => prevMap.TryGetValue(o.Name, out var on) ? o with { On = on } : o).ToArray();
                            }
                            // Real 0.5.0-beta: pack.* zip is extracted to folder and deleted -> archive is null!
                            Mods.RemoveAll(m => m.Id == id);
                            Mods.Add(new(id, name, enabled, dest, ["pack"], "", opts, Archive: null));
                            result = new { id, installed = dest };
                        }
                    }
                    finally { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true); }
                    if (LoseNextModAddAck)
                    {
                        LoseNextModAddAck = false;
                        throw new HellForgeCommandOutcomeUnknownException("Simulated lost acknowledgement after pack.* import.");
                    }
                    break;
                }
                case "mod-remove":
                {
                    var rem = Mods.SingleOrDefault(m => m.Id == args[2]);
                    if (rem != null)
                    {
                        if (Directory.Exists(rem.Folder)) Directory.Delete(rem.Folder, true);
                        Mods.Remove(rem);
                    }
                    result = new { ok = true };
                    break;
                }
                case "mod-switch":
                {
                    var idx = Mods.FindIndex(m => m.Id == args[2]);
                    if (idx >= 0) Mods[idx] = Mods[idx] with { Enabled = args[4] == "yes" };
                    result = new { ok = true };
                    break;
                }
                case "mod-pack":
                {
                    if (FailNextModPack) { FailNextModPack = false; throw new IOException("Simulated mod-pack configuration failure."); }
                    var idx = Mods.FindIndex(m => m.Id == args[2]);
                    if (idx >= 0 && Mods[idx].Options is { } cur)
                    {
                        if (args[3] == "--all")
                            Mods[idx] = Mods[idx] with { Options = cur.Select(o => o with { On = args[4] == "yes" }).ToArray() };
                        else if (args[3] == "--option")
                            Mods[idx] = Mods[idx] with { Options = cur.Select(o => o.Name == args[4] ? o with { On = args[6] == "yes" } : o).ToArray() };
                    }
                    result = new { ok = true };
                    break;
                }
                case "mod-order":
                {
                    var order = args[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var rank = order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
                    Mods.Sort((a, b) => rank.GetValueOrDefault(a.Id, 999).CompareTo(rank.GetValueOrDefault(b.Id, 999)));
                    result = new { ok = true };
                    break;
                }
                default:
                    throw new InvalidOperationException("Unexpected Fake050CliCommands call: " + args[0]);
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
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
        if (!temporary && !(full.Equals(local, StringComparison.OrdinalIgnoreCase)
            || full.Equals(@"D:\CodexScratch\hellforge\forge-upgrade-20261105\probe-0.4.0", StringComparison.OrdinalIgnoreCase)
            || full.Equals(@"D:\ClaudeWork\hellforge-0.5-probe\gameplay-install-rehearsal-v23", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Real recovery trials require an explicitly isolated test installation.");
    }
    internal static async Task RealRecovery(string root)
    {
        RequireIsolated(root);
        Game.IsRunningCheck = () => false;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var official = new HellForgeCommands(full);
        var client = new HellForgeClient(full, official) { RequireCliQuiescent = () => { } };
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
        var resetIntent = client.PackIntentPath + ".gameplay.json";
        if (File.Exists(resetIntent)) File.Delete(resetIntent);
        var clean = await client.ModsAsync();
        if (clean.Mods.Count != 3) throw new InvalidDataException("Clean fixture must contain exactly three official bundled mods.");
        var fault = new FailOnce(official);
        try { await new HellForgeClient(full, fault) { RequireCliQuiescent = () => { } }.InstallGameplayAsync(client.GameplayCache); throw new Exception("Injected failure did not happen."); }
        catch (IOException error) when (error.Message.Contains("previous mod selection was restored", StringComparison.Ordinal)) { }
        var restored = await client.ModsAsync();
        if (restored.Mods.Count != 3 || client.InterruptedGameplayProblem() != null || (await client.MissingGameplayAsync()).Count != catalog.Artifacts.Count)
            throw new InvalidDataException("Fresh-import rollback failed.");
        Console.WriteLine("PASS: actual failed preset installation restored all three bundled mods and cleared recovery");
        try { await new HellForgeClient(full, new LoseAcknowledgement(official)) { RequireCliQuiescent = () => { } }.InstallGameplayAsync(client.GameplayCache); throw new Exception("Lost acknowledgement was not injected."); }
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
        await new HellForgeClient(full, official) { RequireCliQuiescent = () => { } }.RecoverGameplayAsync();
        if ((await client.ModsAsync()).Mods.Count != 3 || client.InterruptedGameplayProblem() != null) throw new InvalidDataException("Restart recovery did not restore the clean fixture.");
        Console.WriteLine("PASS: actual lost import acknowledgement stays pending, rejects damaged backup metadata and recovers through a fresh client");
        await client.InstallGameplayAsync(client.GameplayCache);
        var postInstallMods = await client.ModsAsync();
        var installedPacks = postInstallMods.Mods.Where(m => m.Id.StartsWith("pack.", StringComparison.Ordinal)).ToList();
        if (installedPacks.Count != 3 || installedPacks.Any(m => m.Archive != null))
            throw new InvalidDataException("Expected 3 installed pack.* mods with archive: null on 0.5.0-beta.");
        if ((await client.MissingGameplayAsync()).Count != 0) throw new InvalidDataException("Actual retry left missing dependencies.");
        Console.WriteLine($"PASS: actual retry installed and verified all {catalog.Artifacts.Count} preset mods (including 3 archive:null pack.* folders)");
        var settings = Path.Combine(full, "mods", "hellforge-mods.json"); var originalSettings = File.ReadAllBytes(settings);
        await client.InstallGameplayAsync(client.GameplayCache);
        if (!File.ReadAllBytes(settings).SequenceEqual(originalSettings)) throw new InvalidDataException("An already complete preset was rewritten.");
        Console.WriteLine("PASS: actual repeated setup is a no-op with byte-identical mod settings");

        // Prove #47 exact scenario on real 0.5.0-beta HellForge.Cli.exe:
        // When an existing pack.* mod (Smarter SEAF 1.0.4, archive: null) is backed up into snapshot.Backups,
        // BOTH RestoreGameplayAsync rollback AND wedged State="pending" RecoverGameplayAsync succeed!
        var smarterSeaf = postInstallMods.Mods.Single(m => m.Id == HellForgePreset.SmarterSeaf);
        var seafPayload = Directory.EnumerateFiles(smarterSeaf.Folder, "*", SearchOption.AllDirectories)
            .First(p => Path.GetFileName(p) != "manifest.json" && new FileInfo(p).Length > 0);
        if (!Path.GetFullPath(seafPayload).StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Smarter SEAF payload escaped isolated root.");
        var originalSeafBytes = await File.ReadAllBytesAsync(seafPayload);
        using (var output = new FileStream(seafPayload, FileMode.Append, FileAccess.Write)) output.WriteByte(0xfe);
        var driftedSeafBytes = await File.ReadAllBytesAsync(seafPayload);
        if ((await client.MissingGameplayAsync()).Count != 1) throw new InvalidDataException("Drifted Smarter SEAF pack.* folder was not detected.");
        try { await new HellForgeClient(full, new FailOnce(official)) { RequireCliQuiescent = () => { } }.InstallGameplayAsync(client.GameplayCache); throw new Exception("Pack backup rollback failure did not happen."); }
        catch (IOException error) when (error.Message.Contains("previous mod selection was restored", StringComparison.Ordinal)) { }
        var restoredSeaf = (await client.ModsAsync()).Mods.Single(m => m.Id == HellForgePreset.SmarterSeaf);
        var restoredSeafPayload = Path.Combine(restoredSeaf.Folder, Path.GetRelativePath(smarterSeaf.Folder, seafPayload));
        if (client.InterruptedGameplayProblem() != null || !(await File.ReadAllBytesAsync(restoredSeafPayload)).SequenceEqual(driftedSeafBytes))
            throw new InvalidDataException("RestoreGameplayAsync failed to restore archive:null Smarter SEAF backup.");
        Console.WriteLine("PASS: actual RestoreGameplayAsync rollback restored archive:null Smarter SEAF 1.0.4 backup on 0.5.0-beta");

        try { await new HellForgeClient(full, new LoseAcknowledgement(official)) { RequireCliQuiescent = () => { } }.InstallGameplayAsync(client.GameplayCache); throw new Exception("Pack pending recovery injection did not happen."); }
        catch (HellForgeCommandOutcomeUnknownException) { }
        if (client.InterruptedGameplayProblem() == null) throw new InvalidDataException("Wedged State=pending was not recorded for Smarter SEAF.");
        await new HellForgeClient(full, official) { RequireCliQuiescent = () => { } }.RecoverGameplayAsync();
        var recoveredSeaf = (await client.ModsAsync()).Mods.Single(m => m.Id == HellForgePreset.SmarterSeaf);
        var recoveredSeafPayload = Path.Combine(recoveredSeaf.Folder, Path.GetRelativePath(smarterSeaf.Folder, seafPayload));
        if (client.InterruptedGameplayProblem() != null || !(await File.ReadAllBytesAsync(recoveredSeafPayload)).SequenceEqual(driftedSeafBytes))
            throw new InvalidDataException("RecoverGameplayAsync failed from wedged State=pending with Smarter SEAF in snapshot.Backups.");
        Console.WriteLine("PASS: actual RecoverGameplayAsync recovered wedged State=pending with archive:null Smarter SEAF 1.0.4 in snapshot.Backups");

        await client.InstallGameplayAsync(client.GameplayCache);
        var repairedSeaf = (await client.ModsAsync()).Mods.Single(m => m.Id == HellForgePreset.SmarterSeaf);
        var repairedSeafPayload = Path.Combine(repairedSeaf.Folder, Path.GetRelativePath(smarterSeaf.Folder, seafPayload));
        if ((await client.MissingGameplayAsync()).Count != 0 || !(await File.ReadAllBytesAsync(repairedSeafPayload)).SequenceEqual(originalSeafBytes))
            throw new InvalidDataException("Smarter SEAF repair after RecoverGameplayAsync did not restore clean bytes.");
        Console.WriteLine("PASS: actual InstallGameplayAsync after RecoverGameplayAsync repaired Smarter SEAF 1.0.4 and verified 0 missing mods");

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
