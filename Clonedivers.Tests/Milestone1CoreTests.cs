using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clonedivers.Ui;

namespace Clonedivers.Tests;

internal static class Milestone1CoreTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        Console.WriteLine("Milestone 1 core services: preferences, 5-tier advisor, disk manager, squad hash, crash triage, verifier, host mutators, Discord RPC, self-install, telemetry, What's New, UI SFX, Discord report");

        var area = Path.Combine(root, "m1-core");
        Directory.CreateDirectory(area);

        TestPreferencesDefaultsAndMigration(Path.Combine(area, "prefs"), check);
        TestPerformanceTierAdvisor(Path.Combine(area, "tiers"), check);
        await TestForgeDiskManagerAsync(Path.Combine(area, "disk"), check);
        TestSquadFingerprint(Path.Combine(area, "fingerprint"), check);
        TestCrashTriage(Path.Combine(area, "triage"), check);
        await TestForgeVerifierAsync(Path.Combine(area, "verifier"), check);
        TestHostMutatorConfig(Path.Combine(area, "mutators"), check);
        await TestDiscordRpcClientAsync(check);
        TestSelfInstallHelper(Path.Combine(area, "self-install"), check);
        await TestSquadReportClientAsync(check);
        await TestWhatsNewFetcherAsync(check);
        TestUiSoundEffects(check);
        TestDiscordReportFormatter(check);
    }

    static void TestPreferencesDefaultsAndMigration(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);

        // 1. Brand-new object & missing hellforge.json (without legacy config.json) default to craziest full-variety mix + auto tier
        var freshObj = new HellForgePreferences();
        var freshLoaded = HellForgePreferences.Load(Path.Combine(dir, "brand-new", "hellforge.json"));
        foreach (var (label, p) in new[] { ("new HellForgePreferences()", freshObj), ("Load(missing hellforge.json)", freshLoaded) })
        {
            check(p.Mode == "clonedivers" &&
                  p.EnemySkin == "mixed-droids" &&
                  p.Droids &&
                  p.Covenant &&
                  p.DeltaSquad &&
                  p.AutoPerformanceTier &&
                  p.PerformanceTier == "auto" &&
                  p.SoundEffectsEnabled &&
                  p.DiscordRpcEnabled &&
                  !p.MinimizeToTray &&
                  p.LastStepDownSuggestedTier == null &&
                  p.PendingStepDownReason == null &&
                  p.HostChaosIntensity == "standard" &&
                  p.HostHeavySeaf == "standard" &&
                  p.HostFasterCorpseCleanup &&
                  p.PlayerLabel == null,
                  $"{label} defaults to full-variety mix (mixed-droids, Covenant=true, DeltaSquad=true) and AutoPerformanceTier=true");
        }

        // 2. Existing pre-v2.0 hellforge.json preserves legacy defaults without turning on Covenant, mixed-droids, or AutoPerformanceTier
        var v1Path = Path.Combine(dir, "existing-v1.json");
        const string v1Json = """{"Format":1,"Mode":"clonedivers","Texture":"lighter"}""";
        File.WriteAllText(v1Path, v1Json);
        var loadedV1 = HellForgePreferences.Load(v1Path);
        check(loadedV1.EnemySkin == "all-droids" && !loadedV1.Covenant && !loadedV1.AutoPerformanceTier &&
              loadedV1.PerformanceTier == "high" && loadedV1.Texture == "lighter" &&
              File.ReadAllText(v1Path) == v1Json,
              "existing v1 hellforge.json preserves saved choices cleanly without mutating disk file");

        // 3. Existing v2.0.2 hellforge.json preserves explicit saved choices and AutoPerformanceTier=false
        var v202Path = Path.Combine(dir, "existing-v202.json");
        File.WriteAllText(v202Path, """{"Format":1,"Mode":"empire","Texture":"reduced512","Droids":false,"EnemySkin":"native-automatons","Covenant":false}""");
        var loadedV202 = HellForgePreferences.Load(v202Path);
        check(loadedV202.Mode == "empire" && loadedV202.EnemySkin == "native-automatons" && !loadedV202.Droids &&
              !loadedV202.Covenant && !loadedV202.AutoPerformanceTier && loadedV202.PerformanceTier == "low-512",
              "existing v2.0.2 hellforge.json preserves native-automatons, Covenant=false, and maps reduced512 to low-512");

        // 4. Full round-trip of all new Milestone 1 fields
        var m1Path = Path.Combine(dir, "m1-full.json");
        var custom = new HellForgePreferences
        {
            Mode = "empire",
            EnemySkin = "mixed-droids",
            Covenant = true,
            DeltaSquad = false,
            AutoPerformanceTier = false,
            PerformanceTier = "ultra-potato",
            Texture = "potato256",
            SoundEffectsEnabled = false,
            DiscordRpcEnabled = false,
            MinimizeToTray = true,
            LastStepDownSuggestedTier = "ultra-potato",
            PendingStepDownReason = "Average frame rate was 27.4 FPS (< 35 FPS).",
            HostChaosIntensity = "total-war",
            HostHeavySeaf = "constant",
            HostFasterCorpseCleanup = false,
            PlayerLabel = "Commander Cody"
        };
        custom.Save(m1Path);
        var reloaded = HellForgePreferences.Load(m1Path);
        check(!reloaded.AutoPerformanceTier && reloaded.PerformanceTier == "ultra-potato" &&
              !reloaded.SoundEffectsEnabled && !reloaded.DiscordRpcEnabled && reloaded.MinimizeToTray &&
              reloaded.LastStepDownSuggestedTier == "ultra-potato" &&
              reloaded.PendingStepDownReason == "Average frame rate was 27.4 FPS (< 35 FPS)." &&
              reloaded.HostChaosIntensity == "total-war" && reloaded.HostHeavySeaf == "constant" &&
              !reloaded.HostFasterCorpseCleanup && reloaded.PlayerLabel == "Commander Cody",
              "all Milestone 1 HellForgePreferences properties round-trip cleanly");

        // 5. Validation rejects invalid enum values
        bool rejectedTier = false, rejectedChaos = false, rejectedSeaf = false, rejectedLabel = false;
        try { new HellForgePreferences { PerformanceTier = "extreme-8k" }.Validate(); } catch (InvalidDataException) { rejectedTier = true; }
        try { new HellForgePreferences { HostChaosIntensity = "insane" }.Validate(); } catch (InvalidDataException) { rejectedChaos = true; }
        try { new HellForgePreferences { HostHeavySeaf = "infinite" }.Validate(); } catch (InvalidDataException) { rejectedSeaf = true; }
        try { new HellForgePreferences { PlayerLabel = new string('x', 41) }.Validate(); } catch (InvalidDataException) { rejectedLabel = true; }
        check(rejectedTier && rejectedChaos && rejectedSeaf && rejectedLabel,
              "HellForgePreferences.Validate rejects unsupported tier, mutator enums, and oversized player labels");

        // 6. L8: Legacy config.json with TextureProfile="reduced512" migrates cleanly to Texture="reduced512" / PerformanceTier="low-512"
        var legacy512Dir = Path.Combine(dir, "legacy-reduced512");
        Directory.CreateDirectory(legacy512Dir);
        File.WriteAllText(Path.Combine(legacy512Dir, "config.json"),
            """{"TextureProfile":"reduced512","Options":{"empire":true,"droids":true,"covenant":false}}""");
        var migrated512 = HellForgePreferences.Load(Path.Combine(legacy512Dir, "hellforge.json"));
        check(migrated512.Mode == "empire" &&
              migrated512.Texture == "reduced512" &&
              !migrated512.AutoPerformanceTier &&
              migrated512.PerformanceTier == PerformanceTierAdvisor.Low512 &&
              !migrated512.Covenant,
              "legacy config.json TextureProfile='reduced512' migrates to Texture='reduced512' and PerformanceTier='low-512' (L8)");
    }

    static void TestPerformanceTierAdvisor(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);

        var tiers = PerformanceTierAdvisor.Tiers;
        check(tiers.Count == 5 &&
              tiers[0].Id == "ultra" && tiers[0].MappedPackTexture == "full" &&
              tiers[1].Id == "high" && tiers[1].MappedPackTexture == "lighter" &&
              tiers[2].Id == "balanced" && tiers[2].MappedPackTexture == "reduced1024" &&
              tiers[3].Id == "low-512" && tiers[3].MappedPackTexture == "reduced512" &&
              tiers[4].Id == "ultra-potato" && tiers[4].MappedPackTexture == "potato256",
              "PerformanceTierAdvisor defines the 5-tier ladder mapped 1:1 onto full/lighter/reduced1024/reduced512/potato256");

        check(PerformanceTierAdvisor.NextLowerTier("ultra") == "high" &&
              PerformanceTierAdvisor.NextLowerTier("high") == "balanced" &&
              PerformanceTierAdvisor.NextLowerTier("balanced") == "low-512" &&
              PerformanceTierAdvisor.NextLowerTier("low-512") == "ultra-potato" &&
              PerformanceTierAdvisor.NextLowerTier("ultra-potato") == null,
              "PerformanceTierAdvisor.NextLowerTier steps down one notch at a time and stops at ultra-potato");

        check(PerformanceTierAdvisor.Recommend("NVIDIA GeForce RTX 4080", 15.8, 32.0).RecommendedTierId == "ultra" &&
              PerformanceTierAdvisor.Recommend("NVIDIA GeForce RTX 3070", 7.85, 16.0).RecommendedTierId == "high" &&
              PerformanceTierAdvisor.Recommend("NVIDIA GeForce RTX 2060", 5.85, 16.0).RecommendedTierId == "balanced" &&
              PerformanceTierAdvisor.Recommend("NVIDIA GeForce GTX 1650", 3.85, 16.0).RecommendedTierId == "low-512" &&
              PerformanceTierAdvisor.Recommend("NVIDIA GeForce MX150", 1.95, 8.0).RecommendedTierId == "ultra-potato" &&
              PerformanceTierAdvisor.Recommend("Intel(R) UHD Graphics 620", 0.12, 8.0, true).RecommendedTierId == "ultra-potato" &&
              PerformanceTierAdvisor.Recommend("Unknown GPU", 0, 16.0).RecommendedTierId == "balanced",
              "PerformanceTierAdvisor.Recommend classifies 16GB/8GB/6GB/4GB/2GB/iGPU/unknown-VRAM hardware accurately");

        // Linux /proc/meminfo + /sys/class/drm fixture test
        var memInfo = Path.Combine(dir, "meminfo");
        var drm = Path.Combine(dir, "drm");
        var card0 = Path.Combine(drm, "card0", "device");
        Directory.CreateDirectory(card0);
        File.WriteAllText(memInfo, "MemTotal:       16384000 kB\nMemFree:         4000000 kB\n");
        File.WriteAllText(Path.Combine(card0, "vendor"), "0x1002\n");
        File.WriteAllText(Path.Combine(card0, "mem_info_vram_total"), (8L * 1024 * 1024 * 1024).ToString());
        var linuxRec = PerformanceTierAdvisor.ScanLinuxHardware(memInfo, drm, Path.Combine(dir, "no-nvidia"));
        check(linuxRec.GpuName == "AMD Radeon GPU" && linuxRec.VramGb == 8.0 && linuxRec.RamGb is > 15.5 and < 16.0 && linuxRec.RecommendedTierId == "high",
              "ScanLinuxHardware parses /proc/meminfo and sysfs mem_info_vram_total cleanly");

        var live = PerformanceTierAdvisor.ScanHardware();
        check(!string.IsNullOrWhiteSpace(live.GpuName) && PerformanceTierAdvisor.IsConcreteTier(live.RecommendedTierId),
              "ScanHardware executes safely on the current machine");
    }

    static async Task TestForgeDiskManagerAsync(string dir, Action<bool, string> check)
    {
        var install = Path.Combine(dir, "HellForge");
        var cache = Path.Combine(dir, "Clonedivers-HellForge-cache");

        Directory.CreateDirectory(Path.Combine(install, "game"));
        Directory.CreateDirectory(Path.Combine(install, "mods", "pack.active"));
        Directory.CreateDirectory(Path.Combine(install, "mods", ".unpacked"));
        Directory.CreateDirectory(Path.Combine(install, "mods", "old-test.bak"));
        Directory.CreateDirectory(Path.Combine(install, "profiles", "default"));
        Directory.CreateDirectory(Path.Combine(install, "account-snapshot", "default"));
        Directory.CreateDirectory(Path.Combine(install, "logs", "sessions"));

        Directory.CreateDirectory(Path.Combine(cache, "appearance-downloads"));
        Directory.CreateDirectory(Path.Combine(cache, "exports", "abandoned-export"));
        Directory.CreateDirectory(Path.Combine(cache, "backups", "20261120-120000-completed"));
        Directory.CreateDirectory(Path.Combine(cache, "backups", "20261121-120000-pending"));
        Directory.CreateDirectory(Path.Combine(cache, "transactions"));
        Directory.CreateDirectory(Path.Combine(cache, "gameplay", "build25480438-r5")); // retired catalog

        File.WriteAllBytes(Path.Combine(install, "game", "helldivers2.bin"), new byte[4000]);
        File.WriteAllBytes(Path.Combine(install, "mods", "pack.active", "active.patch_0"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(install, "mods", ".unpacked", ".zip-hashes.json.1234abcd.partial"), new byte[150]);
        File.WriteAllBytes(Path.Combine(install, "mods", "old-test.bak", "stale.bin"), new byte[250]);
        File.WriteAllBytes(Path.Combine(install, "profiles", "default", "profile.json"), Encoding.UTF8.GetBytes("{\"xp\":9999}"));
        File.WriteAllBytes(Path.Combine(install, "account-snapshot", "default", "seal.json"), Encoding.UTF8.GetBytes("{\"seal\":1}"));
        File.WriteAllBytes(Path.Combine(install, "logs", "sessions", "modloader.log"), new byte[300]);

        File.WriteAllBytes(Path.Combine(cache, "appearance-downloads", new string('a', 64) + ".partial"), new byte[600]);
        File.WriteAllBytes(Path.Combine(cache, "exports", "abandoned-export", "part.bin"), new byte[700]);
        var completedOutcome = Encoding.UTF8.GetBytes("{\"outcome\":\"applied\"}");
        File.WriteAllBytes(Path.Combine(cache, "backups", "20261120-120000-completed", "outcome.json"), completedOutcome);
        File.WriteAllBytes(Path.Combine(cache, "backups", "20261120-120000-completed", "old.bin"), new byte[1000]);
        var pendingBackupDir = Path.Combine(cache, "backups", "20261121-120000-pending");
        File.WriteAllBytes(Path.Combine(pendingBackupDir, "must-keep.bin"), new byte[800]);
        File.WriteAllText(Path.Combine(cache, "transactions", "pending.json"),
            JsonSerializer.Serialize(new { State = "pending", Backup = pendingBackupDir }));
        File.WriteAllBytes(Path.Combine(cache, "gameplay", "build25480438-r5", "old.zip"), new byte[500]);

        var before = ForgeDiskManager.Analyze(install);
        long expectedCache = 150 + 600 + 700 + 500;
        long expectedBackups = 250 + completedOutcome.Length + 1000 + 800;
        long expectedReclaimable = 150 + 250 + 600 + 700 + completedOutcome.Length + 1000 + 500; // pending 800 excluded!

        check(before.GameCopyBytes == 4000 &&
              before.ActivePackBytes == 2000 &&
              before.LogsBytes == 300 &&
              before.DownloadCacheBytes == expectedCache &&
              before.OldBackupsBytes == expectedBackups &&
              before.ReclaimableBytes == expectedReclaimable &&
              before.TotalBytes == 4000 + 2000 + 300 + expectedCache + expectedBackups,
              "ForgeDiskManager.Analyze computes exact category totals and protects pending recovery backups from reclaimable count");

        var freed = await ForgeDiskManager.CleanSafeFilesAsync(install);
        var after = ForgeDiskManager.Analyze(install);
        check(freed == expectedReclaimable &&
              after.ReclaimableBytes == 0 &&
              after.GameCopyBytes == 4000 &&
              after.ActivePackBytes == 2000 &&
              File.Exists(Path.Combine(pendingBackupDir, "must-keep.bin")) &&
              File.ReadAllText(Path.Combine(install, "profiles", "default", "profile.json")) == "{\"xp\":9999}" &&
              File.ReadAllText(Path.Combine(install, "account-snapshot", "default", "seal.json")) == "{\"seal\":1}",
              "ForgeDiskManager.CleanSafeFilesAsync frees exact reclaimable bytes while preserving game, active mods, pending recovery, profiles, and account captures");

        var autoFreed = await ForgeDiskManager.AutoCleanStaleAsync(install);
        check(autoFreed == 0, "ForgeDiskManager.AutoCleanStaleAsync completes cleanly with 0 errors");

        // H1 regressions: non-terminal backups, applied-tx-referenced backups, corrupt-tx fail-closed, and locked/fresh gameplay imports!
        var h1Root = Path.Combine(dir, "h1-safety");
        var h1Install = Path.Combine(h1Root, "HellForge");
        var h1Cache = Path.Combine(h1Root, "Clonedivers-HellForge-cache");
        Directory.CreateDirectory(Path.Combine(h1Install, "mods"));
        var h1Backups = Path.Combine(h1Cache, "backups");
        var h1Tx = Path.Combine(h1Cache, "transactions");
        Directory.CreateDirectory(h1Backups);
        Directory.CreateDirectory(h1Tx);

        var unknownBackup = Path.Combine(h1Backups, "20261121-010000-outcome-unknown");
        var recoveryNeededBackup = Path.Combine(h1Backups, "20261121-020000-recovery-needed");
        var inProgressBackup = Path.Combine(h1Backups, "20261121-030000-in-progress");
        var appliedTxBackup = Path.Combine(h1Backups, "20261121-040000-applied-tx");
        var deletableCompleted = Path.Combine(h1Backups, "20261121-050000-unreferenced-completed");
        foreach (var b in new[] { unknownBackup, recoveryNeededBackup, inProgressBackup, appliedTxBackup, deletableCompleted })
        {
            Directory.CreateDirectory(b);
            File.WriteAllBytes(Path.Combine(b, "data.bin"), new byte[400]);
        }
        File.WriteAllText(Path.Combine(unknownBackup, "outcome.json"), """{"outcome":"outcome-unknown"}""");
        File.WriteAllText(Path.Combine(recoveryNeededBackup, "outcome.json"), """{"outcome":"recovery-needed"}""");
        File.WriteAllText(Path.Combine(appliedTxBackup, "outcome.json"), """{"outcome":"applied"}""");
        File.WriteAllText(Path.Combine(deletableCompleted, "outcome.json"), """{"outcome":"restored"}""");
        File.WriteAllText(Path.Combine(h1Tx, "applied.json"), JsonSerializer.Serialize(new { State = "applied", Backup = appliedTxBackup }));

        // Corrupt transaction JSON -> fail-closed on ALL backups/ (even deletableCompleted stays kept!)
        var corruptTxPath = Path.Combine(h1Tx, "corrupt.json");
        File.WriteAllText(corruptTxPath, "{corrupt-json");
        var failClosedAnalysis = ForgeDiskManager.Analyze(h1Install);
        var failClosedFreed = await ForgeDiskManager.CleanSafeFilesAsync(h1Install);
        check(failClosedAnalysis.ReclaimableBytes == 0 && failClosedFreed == 0 && Directory.Exists(deletableCompleted),
              "ForgeDiskManager fails closed on backups/ when any transactions/*.json is corrupt or unreadable (H1)");

        // Remove corrupt.json -> CleanSafeFilesAsync (staleOnly: false) deletes ONLY deletableCompleted, keeping non-terminal & tx-referenced backups!
        File.Delete(corruptTxPath);
        _ = await ForgeDiskManager.CleanSafeFilesAsync(h1Install);
        check(!Directory.Exists(deletableCompleted) &&
              Directory.Exists(unknownBackup) &&
              Directory.Exists(recoveryNeededBackup) &&
              Directory.Exists(inProgressBackup) &&
              Directory.Exists(appliedTxBackup),
              "CleanSafeFilesAsync preserves non-terminal (outcome-unknown, recovery-needed, missing outcome) and applied-tx backups (H1)");

        // Gameplay imports age & transactions/*.lock synchronization check
        var importsDir = Path.Combine(h1Cache, "gameplay", HellForgeGameplayCatalog.Pinned.Id, "imports");
        Directory.CreateDirectory(importsDir);
        var freshImport = Path.Combine(importsDir, "fresh.zip");
        var staleImport = Path.Combine(importsDir, "stale.zip");
        File.WriteAllBytes(freshImport, new byte[300]);
        File.WriteAllBytes(staleImport, new byte[500]);
        File.SetLastWriteTimeUtc(staleImport, DateTime.UtcNow.AddMinutes(-30));

        // While transactions/*.lock is held, NEITHER AutoCleanStaleAsync NOR CleanSafeFilesAsync touches gameplay imports!
        var txLockFile = Path.Combine(h1Tx, "active.json.lock");
        using (new FileStream(txLockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var lockedStale = await ForgeDiskManager.AutoCleanStaleAsync(h1Install);
            var lockedClean = await ForgeDiskManager.CleanSafeFilesAsync(h1Install);
            check(lockedStale == 0 && lockedClean == 0 && File.Exists(staleImport) && File.Exists(freshImport),
                  "ForgeDiskManager skips gameplay imports while transactions/*.lock is held (H1)");
        }

        // Once unlocked, AutoCleanStaleAsync deletes staleImport (>15m) while keeping freshImport (<15m)!
        var unlockedStaleFreed = await ForgeDiskManager.AutoCleanStaleAsync(h1Install);
        check(unlockedStaleFreed == 500 && !File.Exists(staleImport) && File.Exists(freshImport),
              "AutoCleanStaleAsync enforces staleCutoffUtc on gameplay/<id>/imports when unlocked (H1)");
    }

    static void TestSquadFingerprint(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);

        var catalogCode = SquadFingerprint.Compute(HellForgeGameplayCatalog.Current);
        check(System.Text.RegularExpressions.Regex.IsMatch(catalogCode, @"^r10-[0-9a-f]{4}$"),
              $"SquadFingerprint.Compute(Current) produces short deterministic r10 match code ({catalogCode})");

        // Build two player mod lists:
        // Player A: Clonedivers pack + all 7 gameplay:false client code mods enabled + r10 gameplay preset
        // Player B: EmpireDivers pack + 0 gameplay:false client code mods enabled + r10 gameplay preset (reversed order)
        HellForgeMods BuildPlayerMods(string? starWarsMode, bool enableClientCodeMods, string difficultyVersion = "0.16.1", bool enableChaos = true)
        {
            var list = new List<HellForgeMod>();
            foreach (var req in HellForgePreset.Required)
                list.Add(new(req.Id, req.Name, true, "fixture", ["gameplay"], req.Id == "skyeshade.hell-forge" ? difficultyVersion : req.Version));
            list.Add(new("skyeshade.vanilla-plus", "Vanilla Plus", true, "fixture", ["gameplay"], ""));
            list.Add(new(HellForgePreset.ChaosInvasions, "Chaos Invasions", enableChaos, "fixture", ["gameplay", "code"], "0.3.5"));

            foreach (var clientId in new[] { HellForgePreset.CleanExit, HellForgePreset.LegoCompactLeafWriter, HellForgePreset.EmpireLegoYoda, HellForgePreset.EmpireAtstWriter, HellForgePreset.EmpireTieSwitch, HellForgePreset.EmpireCameraPullback, HellForgePreset.StarWarsLocalization, HellForgePreset.DedicatedMeleeSlot })
                list.Add(new(clientId, clientId, enableClientCodeMods, "fixture", ["code"], "0.2.1"));

            if (starWarsMode != null)
                list.Add(new(HellForgePack.PackId(starWarsMode), "Star Wars Pack", true, "fixture", ["cosmetic"], "2026.11.19"));

            return new HellForgeMods(list, [], null);
        }

        var clonesFullClient = BuildPlayerMods("commandos", enableClientCodeMods: true);
        var empireNoClient = BuildPlayerMods("empire", enableClientCodeMods: false);
        empireNoClient.Mods.Reverse();
        var regularPlayer = BuildPlayerMods(null, enableClientCodeMods: true);

        var codeA = SquadFingerprint.Compute(clonesFullClient);
        var codeB = SquadFingerprint.Compute(empireNoClient);
        var codeReg = SquadFingerprint.Compute(regularPlayer);

        check(codeA == catalogCode && codeB == catalogCode && codeReg == catalogCode,
              "players on Clones, Empire, Regular, and different client-only visual/code mods share the exact same Squad Match Code");

        var diffVersion = SquadFingerprint.Compute(BuildPlayerMods("clonedivers", true, difficultyVersion: "0.16.0"));
        var chaosOff = SquadFingerprint.Compute(BuildPlayerMods("clonedivers", true, enableChaos: false));
        check(diffVersion != catalogCode && chaosOff != catalogCode && diffVersion != chaosOff,
              "changing a gameplay mod version or toggling gameplay:true Chaos Invasions changes the Squad Match Code");

        // On-disk custom mod declaring gameplay:false in hellforge-mod.json is excluded
        var customClientFolder = Path.Combine(dir, "custom-hud-mod");
        Directory.CreateDirectory(customClientFolder);
        File.WriteAllText(Path.Combine(customClientFolder, "hellforge-mod.json"), """{"format":1,"id":"user.custom-hud","gameplay":false}""");
        var withCustomClient = BuildPlayerMods("clonedivers", true);
        withCustomClient.Mods.Add(new("user.custom-hud", "Custom HUD", true, customClientFolder, [], "1.0.0"));
        check(SquadFingerprint.Compute(withCustomClient) == catalogCode,
              "custom mod declaring gameplay:false in hellforge-mod.json is excluded from co-op match code");
    }

    static void TestCrashTriage(string dir, Action<bool, string> check)
    {
        static HellForgeStatus MakeStatus(string root, string? failure = null, string? notice = null) =>
            new(root, HellForgeRelease.Version, HellForgePack.SupportedBuild, true, true, false, true, true, "observed", failure, notice);

        static string WriteSession(string baseDir, string name, int exitCode, string modloaderContent)
        {
            var root = Path.Combine(baseDir, name);
            var sess = Path.Combine(root, "logs", "sessions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(sess, "game"));
            File.WriteAllText(Path.Combine(sess, "result.json"),
                JsonSerializer.Serialize(new { exitCode, completedUtc = "2026-11-21T22:00:00Z" }));
            File.WriteAllText(Path.Combine(sess, "game", "modloader.log"), modloaderContent);
            return root;
        }

        // 1. Clean exit
        var cleanRoot = WriteSession(dir, "clean", 0, "[info] session ended normally\n");
        var clean = CrashTriage.AnalyzeLastSession(cleanRoot, MakeStatus(cleanRoot), "ultra");
        check(clean.Kind == CrashKind.Clean && !clean.ShouldUploadCrash && !clean.SuggestStepDown,
              "CrashTriage classifies exit 0 as Clean with ShouldUploadCrash=false");

        // 2. HarmlessQuit (helldivers2.exe+0x626296, exit 1)
        var quitRoot = WriteSession(dir, "quit", 1,
            "2026-11-21 22:00:00 [error] crash: c0000005 on thread 1420 at helldivers2.exe+0x626296 (access violation)\n");
        var quit = CrashTriage.AnalyzeLastSession(quitRoot, MakeStatus(quitRoot), "ultra");
        check(quit.Kind == CrashKind.HarmlessQuit &&
              quit.Summary == "Harmless game-exit bug — your mission saved fine." &&
              !quit.ShouldUploadCrash && !quit.SuggestStepDown,
              "CrashTriage classifies helldivers2.exe+0x626296 exit 1 as HarmlessQuit with exact plain-English message and ShouldUploadCrash=false");

        // 3. OutOfVram (DXGI device hung & 700AFB)
        var vramRoot = WriteSession(dir, "vram", 1,
            "[render] D3D12 error: DXGI_ERROR_DEVICE_HUNG (0x887a0006)\n[error] crash: at helldivers2.exe+0x700AFB\n");
        var vram = CrashTriage.AnalyzeLastSession(vramRoot, MakeStatus(vramRoot), "ultra");
        check(vram.Kind == CrashKind.OutOfVram && vram.SuggestStepDown && vram.SuggestedTierId == "high" && vram.ShouldUploadCrash,
              "CrashTriage classifies D3D12/DXGI/700AFB crash as OutOfVram, suggests next lower tier (high), and sets ShouldUploadCrash=true");

        // 4. ModMismatch (ALAMO041)
        var mismatchRoot = WriteSession(dir, "mismatch", 1, "[net] Lobby join rejected: ALAMO041 mod mismatch with host\n");
        var mismatch = CrashTriage.AnalyzeLastSession(mismatchRoot, MakeStatus(mismatchRoot), "balanced");
        check(mismatch.Kind == CrashKind.ModMismatch && !mismatch.ShouldUploadCrash && !mismatch.SuggestStepDown &&
              mismatch.RecommendedAction!.Contains("Verify & Repair", StringComparison.OrdinalIgnoreCase),
              "CrashTriage classifies ALAMO041 as ModMismatch recommending Verify & Repair with ShouldUploadCrash=false");

        // 5. MissingLinuxNetwork (pasta / passt missing)
        var pastaRoot = Path.Combine(dir, "pasta-missing");
        Directory.CreateDirectory(pastaRoot);
        var pasta = CrashTriage.AnalyzeLastSession(pastaRoot, MakeStatus(pastaRoot, failure: "Required tool pasta is missing from PATH"), "balanced");
        check(pasta.Kind == CrashKind.MissingLinuxNetwork && !pasta.ShouldUploadCrash &&
              pasta.RecommendedAction!.Contains("sudo apt install passt", StringComparison.Ordinal),
              "CrashTriage classifies missing pasta/passt as MissingLinuxNetwork recommending 'sudo apt install passt'");

        // 6. UnexpectedCrash with > 64 KB modloader.log tail truncation
        var bigBuilder = new StringBuilder();
        for (int i = 0; i < 1600; i++)
            bigBuilder.AppendLine($"2026-11-21 21:00:00 [trace] routine engine line {i:D4} padding01234567890123456789");
        for (int i = 1; i <= 10; i++)
            bigBuilder.AppendLine($"2026-11-21 21:59:{i:D2} [error] fatal fault #{i} in C:\\Users\\SecretPlayer\\AppData\\test");
        var unexpectedRoot = WriteSession(dir, "unexpected", -1073741819, bigBuilder.ToString());
        var unexpected = CrashTriage.AnalyzeLastSession(unexpectedRoot, MakeStatus(unexpectedRoot), "balanced");
        check(unexpected.Kind == CrashKind.UnexpectedCrash && unexpected.ShouldUploadCrash && !unexpected.SuggestStepDown &&
              unexpected.RelevantLogLines.Count == 8 &&
              unexpected.RelevantLogLines.All(l => !l.Contains("SecretPlayer", StringComparison.Ordinal)) &&
              unexpected.RelevantLogLines[^1].Contains("fatal fault #10", StringComparison.Ordinal),
              "CrashTriage bounds modloader.log read to 64 KB, returns last 8 sanitized error lines, and sets ShouldUploadCrash=true");

        // 7. FPS step-down check
        var fpsStep = CrashTriage.ShouldSuggestStepDownFromFps(29.8, "balanced");
        var fpsOk = CrashTriage.ShouldSuggestStepDownFromFps(42.0, "balanced");
        var fpsBottom = CrashTriage.ShouldSuggestStepDownFromFps(24.0, "ultra-potato");
        check(fpsStep && fpsStep.SuggestedTierId == "low-512" && !fpsOk && !fpsBottom,
              "ShouldSuggestStepDownFromFps suggests next lower tier below 35 FPS and stops at ultra-potato");
    }

    static async Task TestForgeVerifierAsync(string dir, Action<bool, string> check)
    {
        var install = Path.Combine(dir, "HellForge");
        Directory.CreateDirectory(Path.Combine(install, "mods", ".unpacked"));
        Directory.CreateDirectory(Path.Combine(install, "profiles", "default"));
        Directory.CreateDirectory(Path.Combine(install, "account-snapshot", "default"));
        File.WriteAllText(Path.Combine(install, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");

        // Sentinel save & capture files that Verify & Repair MUST NEVER touch
        var profilePath = Path.Combine(install, "profiles", "default", "profile.json");
        var sealPath = Path.Combine(install, "account-snapshot", "default", "seal.json");
        const string profileBytes = """{"character":"Rex","level":150,"superCredits":1200}""";
        const string sealBytes = """{"steamAccount":"76561198000000001","capturedUtc":"2026-11-20T00:00:00Z"}""";
        File.WriteAllText(profilePath, profileBytes);
        File.WriteAllText(sealPath, sealBytes);

        // Stale partial file to reclaim in Step 5
        var stalePartial = Path.Combine(install, "mods", ".unpacked", ".zip-hashes.json.deadbeef.partial");
        File.WriteAllBytes(stalePartial, new byte[512]);

        var fake = new VerifierFakeCommands(install);
        fake.SeedReadyGameplayMods(install, scrambleOrder: true); // Force Step 3 order/sharing repair!

        // Fixture Star Wars pack for Step 4
        var blob = Encoding.UTF8.GetBytes("verified-star-wars-pack-archive");
        var blobSha = Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant();
        var localSourceDir = Path.Combine(dir, "local-pack-source");
        Directory.CreateDirectory(localSourceDir);
        File.WriteAllBytes(Path.Combine(localSourceDir, "9ba626afa44a3aa3.patch_0"), blob);

        var fixtureSelection = new HellForgePack.Selection(
            "commandos", "reduced512", "2026.11.19-r20", HellForgePack.SupportedBuild, new string('b', 64),
            new Dictionary<string, bool> { ["commandos"] = true, ["droids"] = true, ["mixed-droids"] = true, ["covenant"] = true },
            [new PackFile { Name = "9ba626afa44a3aa3.patch_0", Size = blob.Length, Sha256 = blobSha, Url = "https://github.com/owendavidgoode/clonedivers/releases/download/test/a" }]);

        var prefs = new HellForgePreferences { InstallRoot = install, Mode = "clonedivers", Texture = "reduced512" };
        var progressEvents = new List<OperationProgress>();

        var report = await ForgeVerifier.VerifyAndRepairCoreAsync(
            install,
            prefs,
            new SyncProgress<OperationProgress>(progressEvents.Add),
            CancellationToken.None,
            commands: fake,
            captureBlockCheck: () => null,
            selectionOverride: () => fixtureSelection,
            extraSearchFolders: [localSourceDir]);

        check(report.Healthy && report.Steps.Count == 5 && report.Steps.All(s => s.Passed) &&
              report.GameplayModsRepaired >= 1 && report.PackVerified && report.PackRepaired &&
              report.BytesReclaimed >= 512 && !File.Exists(stalePartial) &&
              File.ReadAllText(profilePath) == profileBytes &&
              File.ReadAllText(sealPath) == sealBytes &&
              progressEvents.Count >= 5,
              "ForgeVerifier runs all 5 check & repair steps, repairs drifted preset & pack, reclaims stale cache, and leaves saves/account untouched");

        // Second run when already healthy performs 0 repairs
        var second = await ForgeVerifier.VerifyAndRepairCoreAsync(
            install,
            prefs,
            null,
            CancellationToken.None,
            commands: fake,
            captureBlockCheck: () => null,
            selectionOverride: () => fixtureSelection,
            extraSearchFolders: [localSourceDir]);
        check(second.Healthy && second.Steps.Count == 5 && second.GameplayModsRepaired == 0 && !second.PackRepaired,
              "ForgeVerifier second pass confirms clean state with zero redundant repairs");

        // Step 2 capture block stops safely before mutating mods
        var blocked = await ForgeVerifier.VerifyAndRepairCoreAsync(
            install,
            prefs,
            null,
            CancellationToken.None,
            commands: fake,
            captureBlockCheck: () => "Account sync cleanup is pending.");
        check(!blocked.Healthy && blocked.Steps.Count == 2 && !blocked.Steps[1].Passed,
              "ForgeVerifier stops safely at Step 2 when hosts/capture cleanup is pending");
    }

    static void TestHostMutatorConfig(string dir, Action<bool, string> check)
    {
        var install = Path.Combine(dir, "HellForge");
        Directory.CreateDirectory(Path.Combine(install, "mods"));

        var ts = new DateTimeOffset(2026, 11, 21, 23, 0, 0, TimeSpan.Zero);
        HostMutatorConfig.Write(install, "total-war", "heavy", fasterCorpseCleanup: true, updatedUtc: ts);

        var filePath = HostMutatorConfig.GetFilePath(install);
        var rawBytes = File.ReadAllBytes(filePath);
        var hasUtf8Bom = rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF;
        using var doc = JsonDocument.Parse(rawBytes);

        check(!hasUtf8Bom &&
              doc.RootElement.GetProperty("format").GetInt32() == 1 &&
              doc.RootElement.GetProperty("chaosIntensity").GetString() == "total-war" &&
              doc.RootElement.GetProperty("heavySeaf").GetString() == "heavy" &&
              doc.RootElement.GetProperty("fasterCorpseCleanup").GetBoolean() &&
              doc.RootElement.TryGetProperty("updatedUtc", out _),
              "HostMutatorConfig writes exact camelCase JSON without UTF-8 BOM at <installRoot>/mods/clonedivers-host-options.json");

        var readBack = HostMutatorConfig.Read(install);
        check(readBack.ChaosIntensity == "total-war" && readBack.HeavySeaf == "heavy" && readBack.FasterCorpseCleanup && readBack.UpdatedUtc == ts,
              "HostMutatorConfig.Read round-trips written options");
    }

    static async Task TestDiscordRpcClientAsync(Action<bool, string> check)
    {
        // 1. Empty App ID gracefully no-ops without throwing
        using (var noopRpc = new DiscordRpcClient(appId: ""))
        {
            noopRpc.SetPresence("In Orbit", "Clonedivers");
            noopRpc.ClearPresence();
            check(!await noopRpc.SetPresenceAsync("In Orbit", "Clonedivers") && !noopRpc.IsConnected,
                  "DiscordRpcClient gracefully no-ops when App ID is empty");
        }

        // 2. Real Windows named pipe server round-trip testing binary LE framing (Handshake + SetPresence + ClearPresence)
        var pipeName = "clonedivers-rpc-test-" + Guid.NewGuid().ToString("N");
        var receivedFrames = new List<(int Opcode, string Json)>();

        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            // Frame 1: Handshake
            var hs = await DiscordRpcClient.ReadFrameAsync(server);
            if (hs != null) receivedFrames.Add(hs.Value);
            await DiscordRpcClient.WriteFrameAsync(server, DiscordRpcClient.OpcodeFrame, """{"cmd":"DISPATCH","evt":"READY","data":{"v":1}}""");

            // Frame 2: SetPresence
            var set = await DiscordRpcClient.ReadFrameAsync(server);
            if (set != null) receivedFrames.Add(set.Value);
            await DiscordRpcClient.WriteFrameAsync(server, DiscordRpcClient.OpcodeFrame, """{"cmd":"SET_ACTIVITY","evt":null,"data":{}}""");

            // Frame 3: ClearPresence
            var clr = await DiscordRpcClient.ReadFrameAsync(server);
            if (clr != null) receivedFrames.Add(clr.Value);
            await DiscordRpcClient.WriteFrameAsync(server, DiscordRpcClient.OpcodeFrame, """{"cmd":"SET_ACTIVITY","evt":null,"data":null}""");
        });

        await using (var rpc = new DiscordRpcClient("123456789012345678", async ct =>
        {
            var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientPipe.ConnectAsync(2000, ct);
            return clientPipe;
        }))
        {
            var setOk = await rpc.SetPresenceAsync("Squad Match r7-a9c4", "Playing Clonedivers");
            var clearOk = await rpc.ClearPresenceAsync();
            await serverTask;

            check(setOk && clearOk && receivedFrames.Count == 3 &&
                  receivedFrames[0].Opcode == DiscordRpcClient.OpcodeHandshake &&
                  receivedFrames[0].Json.Contains("123456789012345678", StringComparison.Ordinal) &&
                  receivedFrames[1].Opcode == DiscordRpcClient.OpcodeFrame &&
                  receivedFrames[1].Json.Contains("Squad Match r7-a9c4", StringComparison.Ordinal) &&
                  receivedFrames[1].Json.Contains("Playing Clonedivers", StringComparison.Ordinal) &&
                  receivedFrames[2].Opcode == DiscordRpcClient.OpcodeFrame &&
                  receivedFrames[2].Json.Contains("\"activity\":null", StringComparison.Ordinal),
                  "DiscordRpcClient exchanges valid LE-framed HANDSHAKE, SET_ACTIVITY, and ClearPresence frames over IPC pipe");
        }

        // 3. M6 regression: WaitAsync timeout/cancellation does NOT call ResetConnection() outside gate, and generation counter drops superseded updates!
        var pipeNameM6 = "clonedivers-rpc-m6-" + Guid.NewGuid().ToString("N");
        var m6Frames = new List<(int Opcode, string Json)>();
        var holdReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverReceivedFirstSet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverReceivedFinalClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var serverM6 = new NamedPipeServerStream(pipeNameM6, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverM6Task = Task.Run(async () =>
        {
            await serverM6.WaitForConnectionAsync();
            var hs = await DiscordRpcClient.ReadFrameAsync(serverM6);
            if (hs != null) m6Frames.Add(hs.Value);
            await DiscordRpcClient.WriteFrameAsync(serverM6, DiscordRpcClient.OpcodeFrame, """{"cmd":"DISPATCH","evt":"READY","data":{"v":1}}""");

            // Hold Caller 1 inside ReadFrameAsync while Caller 2 times out on gate.WaitAsync AND fire-and-forget updates queue up!
            var firstSet = await DiscordRpcClient.ReadFrameAsync(serverM6);
            if (firstSet != null) m6Frames.Add(firstSet.Value);
            serverReceivedFirstSet.TrySetResult();
            await holdReply.Task;
            await DiscordRpcClient.WriteFrameAsync(serverM6, DiscordRpcClient.OpcodeFrame, """{"cmd":"SET_ACTIVITY","evt":null,"data":{}}""");

            // Next frame MUST be ClearPresence ("activity":null) because superseded SetPresence("Superseded") was dropped!
            var nextFrame = await DiscordRpcClient.ReadFrameAsync(serverM6);
            if (nextFrame != null) m6Frames.Add(nextFrame.Value);
            await DiscordRpcClient.WriteFrameAsync(serverM6, DiscordRpcClient.OpcodeFrame, """{"cmd":"SET_ACTIVITY","evt":null,"data":null}""");
            serverReceivedFinalClear.TrySetResult();
        });

        var rpcM6 = new DiscordRpcClient("123456789012345678", async ct =>
        {
            var clientPipe = new NamedPipeClientStream(".", pipeNameM6, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientPipe.ConnectAsync(2000, ct);
            return clientPipe;
        });
        {
            var caller1Task = rpcM6.SetPresenceAsync("Initial Active", "In Mission");
            await serverReceivedFirstSet.Task;

            // Caller 2 times out waiting for gate while Caller 1 holds gate
            using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));
            var caller2TimedOutOk = !await rpcM6.SetPresenceAsync("TimedOutCaller", "ShouldNotReset", ct: shortCts.Token);

            // Queue fire-and-forget SetPresence followed immediately by ClearPresence while gate is STILL held by Caller 1
            rpcM6.SetPresence("Superseded State", "Should Be Dropped");
            rpcM6.ClearPresence();

            // Release Caller 1's reply on the pipe
            holdReply.TrySetResult();
            var caller1Succeeded = await caller1Task;
            await serverReceivedFinalClear.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await serverM6Task;

            check(caller2TimedOutOk && caller1Succeeded && rpcM6.IsConnected &&
                  m6Frames.Count == 3 &&
                  m6Frames[1].Json.Contains("Initial Active", StringComparison.Ordinal) &&
                  m6Frames[2].Json.Contains("\"activity\":null", StringComparison.Ordinal) &&
                  m6Frames.All(f => !f.Json.Contains("Superseded State", StringComparison.Ordinal)),
                  "DiscordRpcClient preserves active connection on gate timeout and drops superseded fire-and-forget updates (M6)");

            // Atomic concurrent Dispose() safety
            Parallel.For(0, 4, _ => rpcM6.Dispose());
            check(!rpcM6.IsConnected, "DiscordRpcClient.Dispose is atomic and idempotent under concurrent calls (M6)");
        }
    }

    static void TestSelfInstallHelper(string dir, Action<bool, string> check)
    {
        var fakeProfile = Path.Combine(dir, "User");
        var fakeDownloads = Path.Combine(fakeProfile, "Downloads");
        var fakeLocalApp = Path.Combine(fakeProfile, "AppData", "Local");
        var fakeDesktop = Path.Combine(fakeProfile, "Desktop");
        var fakeStartMenu = Path.Combine(fakeProfile, "StartMenu", "Programs");
        Directory.CreateDirectory(fakeDownloads);

        var downloadedExe = Path.Combine(fakeDownloads, "Clonedivers.exe");
        var payload = Encoding.UTF8.GetBytes("MZ-fake-clonedivers-single-file-binary-payload");
        File.WriteAllBytes(downloadedExe, payload);

        check(SelfInstallHelper.IsRunningFromDownloads(downloadedExe, fakeProfile, fakeLocalApp, isWindowsOverride: true) &&
              !SelfInstallHelper.IsRunningFromDownloads(Path.Combine(fakeLocalApp, "Clonedivers", "Clonedivers.exe"), fakeProfile, fakeLocalApp, isWindowsOverride: true) &&
              !SelfInstallHelper.IsRunningFromDownloads(@"D:\Tools\Clonedivers.exe", fakeProfile, fakeLocalApp, isWindowsOverride: true),
              "SelfInstallHelper.IsRunningFromDownloads accurately detects %USERPROFILE%\\Downloads and excludes installed paths");

        var result = SelfInstallHelper.InstallToLocalAppDataAndCreateShortcuts(
            downloadedExe,
            localAppData: fakeLocalApp,
            desktopDir: fakeDesktop,
            startMenuProgramsDir: fakeStartMenu,
            createShortcuts: true);

        bool exeCopiedExact = File.Exists(result.InstalledExePath) && File.ReadAllBytes(result.InstalledExePath).SequenceEqual(payload);
        bool desktopValidLnk = result.DesktopShortcutPath != null && File.Exists(result.DesktopShortcutPath) &&
                               BinaryPrimitives.ReadInt32LittleEndian(File.ReadAllBytes(result.DesktopShortcutPath).AsSpan(0, 4)) == 0x0000004C;
        bool startMenuValidLnk = result.StartMenuShortcutPath != null && File.Exists(result.StartMenuShortcutPath) &&
                                 BinaryPrimitives.ReadInt32LittleEndian(File.ReadAllBytes(result.StartMenuShortcutPath).AsSpan(0, 4)) == 0x0000004C;

        check(exeCopiedExact && result.ShortcutsCreated && desktopValidLnk && startMenuValidLnk,
              "SelfInstallHelper copies Clonedivers.exe to LocalAppData\\Clonedivers and creates valid Desktop & Start Menu .lnk shortcuts");

        // H2 regression: PowerShell fallback resolves absolute System32\WindowsPowerShell\v1.0\powershell.exe, never redirects unread pipes, and ignores planted Downloads\powershell.exe!
        var plantedMarker = Path.Combine(fakeDownloads, "planted-ran.txt");
        var plantedPs = Path.Combine(fakeDownloads, "powershell.exe");
        File.WriteAllBytes(plantedPs, Encoding.UTF8.GetBytes("not-a-real-pe-binary"));
        var psShortcut = Path.Combine(fakeDesktop, "Clonedivers-PS.lnk");
        var psi = SelfInstallHelper.CreatePowerShellShortcutStartInfo(psShortcut, result.InstalledExePath, Path.GetDirectoryName(result.InstalledExePath)!, "PS fallback test");
        var expectedSysPs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        bool psCreated = SelfInstallHelper.TryCreateShortcutViaPowerShell(psShortcut, result.InstalledExePath, Path.GetDirectoryName(result.InstalledExePath)!, "PS fallback test");
        bool missingSysRejected = !SelfInstallHelper.TryCreateShortcutViaPowerShell(
            Path.Combine(fakeDesktop, "ShouldNotExist.lnk"),
            result.InstalledExePath,
            Path.GetDirectoryName(result.InstalledExePath)!,
            "missing sys",
            systemDirOverride: Path.Combine(dir, "nonexistent-system32"));
        check(Path.IsPathFullyQualified(psi.FileName) &&
              string.Equals(psi.FileName, expectedSysPs, StringComparison.OrdinalIgnoreCase) &&
              !psi.RedirectStandardOutput &&
              !psi.RedirectStandardError &&
              psCreated && File.Exists(psShortcut) &&
              !File.Exists(plantedMarker) &&
              missingSysRejected,
              "SelfInstallHelper resolves fully-qualified System32 powershell.exe without unread pipe redirection or Downloads hijacking (H2)");
    }

    static async Task TestSquadReportClientAsync(Action<bool, string> check)
    {
        var requests = new List<(string Path, string Body)>();
        using var http = new HttpClient(new MockHttpHandler((req, _) =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((req.RequestUri!.AbsolutePath, body));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        }));

        var client = new SquadReportClient(http, "https://clonedivers-telemetry.goodecraft.com", TimeSpan.FromSeconds(5));
        var session = new SquadSessionReport(
            Player: "Rex 76561198012345678",
            LauncherVersion: "2.0.3",
            ExitCode: 1,
            HostLabel: "Cody",
            MatchHash: "r7-a9c4",
            Mode: "clonedivers",
            PerfTier: "high",
            Gpu: "RTX 3070",
            RamGb: 16.0,
            DurationSec: 1840.5,
            AvgFps: 74.2,
            Low1Fps: 52.1,
            StatusSummary: "Harmless game-exit bug — your mission saved fine.");

        var harmlessTriage = new CrashTriageResult(CrashKind.HarmlessQuit, 1, CrashTriage.HarmlessQuitMessage, null, false, null, false, []);
        var realCrashTriage = new CrashTriageResult(
            CrashKind.UnexpectedCrash,
            -1073741819,
            "Crash in C:\\Users\\OwenSecret\\HellForge with Bearer mySecretToken123 and seal.json",
            null,
            false,
            null,
            true,
            ["token=supersecret999 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"]);

        var sessionOk = await client.SendSessionAsync(session);
        var harmlessBlocked = await client.SendCrashAsync(SquadReportClient.BuildCrashReportFromSession(session, harmlessTriage), harmlessTriage);
        var realCrashOk = await client.SendCrashAsync(SquadReportClient.BuildCrashReportFromSession(session, realCrashTriage), realCrashTriage);

        check(sessionOk && !harmlessBlocked && realCrashOk &&
              requests.Count == 2 && requests[0].Path == "/session" && requests[1].Path == "/crash" &&
              requests[0].Body.Contains("\"matchHash\":\"r7-a9c4\"", StringComparison.Ordinal) &&
              requests[1].Body.Contains("\"crashClass\":\"UnexpectedCrash\"", StringComparison.Ordinal) &&
              !requests[0].Body.Contains("76561198012345678", StringComparison.Ordinal) &&
              !requests[1].Body.Contains("OwenSecret", StringComparison.Ordinal) &&
              !requests[1].Body.Contains("mySecretToken123", StringComparison.Ordinal) &&
              !requests[1].Body.Contains("supersecret999", StringComparison.Ordinal) &&
              !requests[1].Body.Contains("seal.json", StringComparison.Ordinal) &&
              !requests[1].Body.Contains("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", StringComparison.Ordinal),
              "SquadReportClient posts /session and /crash matching #36 Worker schema, blocks HarmlessQuit uploads, and scrubs Steam IDs, tokens, hex hashes, user paths, and capture filenames");

        // Timeout resilience test (never throws!)
        using var slowHttp = new HttpClient(new MockHttpHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var fastTimeoutClient = new SquadReportClient(slowHttp, "https://clonedivers-telemetry.goodecraft.com", TimeSpan.FromMilliseconds(120));
        var timedOutCleanly = !await fastTimeoutClient.SendSessionAsync(session);
        check(timedOutCleanly, "SquadReportClient enforces bounded timeout and returns false without throwing");
    }

    static async Task TestWhatsNewFetcherAsync(Action<bool, string> check)
    {
        const string sampleReleaseBody = """
            > [!NOTE]
            > Prerelease build status banner that should be stripped.

            ## Highlights
            - **Smart 5-Tier Performance Advisor** — automatically picks the safest texture tier for your GPU VRAM and RAM.
            - **1-Click Verify & Repair** — checks Forge `25480438`, repairs all 14 `r7` gameplay mods, and cleans old backups.
            - **Squad Match Fingerprint (`r7-a9c4`)** — instant co-op compatibility check regardless of local Star Wars skins.
            - **Star Wars Holoterminal UI Audio** — subtle procedural chimes with zero external asset bloat.
            - Full Changelog: https://github.com/owendavidgoode/clonedivers/compare/v2.0.2...v2.0.3
            - Clonedivers.exe: 65947696 bytes — a42e43e7afe520d0ccd23fe25f9ac5d818c00419d9401de51e59553c87866742

            ## Checksums & SHA-256
            | File | Bytes | SHA-256 |
            | --- | --- | --- |
            | Clonedivers.exe | 65,947,696 | `a42e43e7afe520d0ccd23fe25f9ac5d818c00419d9401de51e59553c87866742` |
            """;

        using var okHttp = new HttpClient(new MockHttpHandler((req, _) =>
        {
            var json = JsonSerializer.Serialize(new
            {
                tag_name = "v2.0.3",
                html_url = "https://github.com/owendavidgoode/clonedivers/releases/tag/v2.0.3",
                body = sampleReleaseBody
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }));

        var fetched = await WhatsNewFetcher.FetchAsync("2.0.3", client: okHttp, isLinux: false, bypassCache: true);
        check(!fetched.IsFallback && fetched.Tag == "v2.0.3" &&
              fetched.Bullets.Count == 4 &&
              fetched.Bullets[0].StartsWith("Smart 5-Tier Performance Advisor", StringComparison.Ordinal) &&
              fetched.Bullets.All(b => !b.Contains("a42e43e7", StringComparison.OrdinalIgnoreCase) && !b.Contains('|') && !b.Contains("**", StringComparison.Ordinal)),
              "WhatsNewFetcher extracts 3–5 clean bullets and strips SHA tables, callouts, hashes, and markdown noise");

        using var offlineHttp = new HttpClient(new MockHttpHandler((_, _) => throw new HttpRequestException("Simulated offline")));
        var fallback = await WhatsNewFetcher.FetchAsync("linux-v0.9.3", client: offlineHttp, isLinux: true, bypassCache: true);
        check(fallback.IsFallback && fallback.Tag == "linux-v0.9.3" && fallback.Bullets.Count is >= 3 and <= 5,
              "WhatsNewFetcher returns clean 3–5 bullet offline fallback on network failure");
    }

    static void TestUiSoundEffects(Action<bool, string> check)
    {
        foreach (var kind in Enum.GetValues<UiSoundKind>())
        {
            var wav = UiSoundEffects.GetWavBytes(kind);
            var riff = Encoding.ASCII.GetString(wav, 0, 4);
            var wave = Encoding.ASCII.GetString(wav, 8, 4);
            var fmt = Encoding.ASCII.GetString(wav, 12, 4);
            var dataTag = Encoding.ASCII.GetString(wav, 36, 4);
            var audioFormat = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20, 2));
            var channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22, 2));
            var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24, 4));
            var bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34, 2));
            var dataBytes = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40, 4));

            short firstSample = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44, 2));
            short lastSample = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(wav.Length - 2, 2));
            int peak = 0;
            for (int offset = 44; offset + 1 < wav.Length; offset += 2)
                peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(offset, 2))));

            check(riff == "RIFF" && wave == "WAVE" && fmt == "fmt " && dataTag == "data" &&
                  audioFormat == 1 && channels == 1 && sampleRate == 44100 && bitsPerSample == 16 &&
                  dataBytes == wav.Length - 44 && firstSample == 0 && lastSample == 0 &&
                  peak is > 1000 and < 20000,
                  $"UiSoundEffects.{kind} synthesizes valid click-free, quiet 44.1 kHz PCM WAV in memory (peak={peak})");
        }

        var played = new List<UiSoundKind>();
        var prevHandler = UiSoundEffects.CustomPlaybackHandler;
        var prevEnabled = UiSoundEffects.Enabled;
        try
        {
            UiSoundEffects.CustomPlaybackHandler = (k, _) => played.Add(k);
            UiSoundEffects.Enabled = false;
            UiSoundEffects.PlayButtonBlip();
            UiSoundEffects.PlayModeSwitch();
            check(played.Count == 0, "UiSoundEffects respects Enabled=false mute flag");

            UiSoundEffects.Enabled = true;
            UiSoundEffects.PlayLaunchConfirm();
            UiSoundEffects.PlayCompletion();
            check(played.SequenceEqual([UiSoundKind.LaunchConfirm, UiSoundKind.Completion]),
                  "UiSoundEffects dispatches requested holoterminal clips when Enabled=true");
        }
        finally
        {
            UiSoundEffects.CustomPlaybackHandler = prevHandler;
            UiSoundEffects.Enabled = prevEnabled;
        }
    }

    static void TestDiscordReportFormatter(Action<bool, string> check)
    {
        var hw = new HardwarePerformanceRecommendation("NVIDIA GeForce RTX 4070", 12.0, 32.0, "ultra", "12 GB VRAM");

        // Case 1: Clean session (0 error lines)
        var cleanReport = DiscordReportFormatter.Format(
            appVersion: "2.0.3",
            squadMatchCode: "r7-a9c4",
            mode: "clonedivers",
            enemySkin: "mixed-droids",
            covenant: true,
            deltaSquad: true,
            performanceTier: "auto",
            packTexture: "full",
            hardware: hw,
            installRoot: @"C:\Users\Alice\HellForge",
            triage: new CrashTriageResult(CrashKind.Clean, 0, "Last session exited cleanly.", null, false, null, false, []));

        var cleanLines = cleanReport.Split('\n');
        check(cleanLines.Length is >= 10 and <= 14 &&
              cleanReport.Contains("v2.0.3", StringComparison.Ordinal) &&
              cleanReport.Contains("r7-a9c4", StringComparison.Ordinal) &&
              cleanReport.Contains("RTX 4070", StringComparison.Ordinal) &&
              cleanReport.Contains(@"C:\Users\<user>\HellForge", StringComparison.Ordinal) &&
              !cleanReport.Contains("Alice", StringComparison.Ordinal),
              $"DiscordReportFormatter clean report has {cleanLines.Length} lines (within 10–14) and redacts OS username");

        // Case 2: Crash session with 8 error lines -> capped cleanly to 14 total lines
        var crashReport = DiscordReportFormatter.Format(
            appVersion: "2.0.3",
            squadMatchCode: "r7-a9c4",
            mode: "empire",
            enemySkin: "mixed-droids",
            covenant: true,
            deltaSquad: false,
            performanceTier: "high",
            packTexture: "lighter",
            hardware: hw,
            installRoot: @"D:\HellForge",
            triage: new CrashTriageResult(
                CrashKind.OutOfVram,
                1,
                "GPU ran out of video memory or hung (D3D12).",
                "Switch to Balanced",
                true,
                "balanced",
                true,
                ["err 1", "err 2", "err 3", "err 4", "err 5", "err 6", "err 7", "err 8"]));

        var crashLines = crashReport.Split('\n');
        check(crashLines.Length == 14 && crashReport.Length < 1900 &&
              crashReport.Contains(@"D:\HellForge", StringComparison.Ordinal) &&
              crashReport.Contains("err 8", StringComparison.Ordinal) &&
              !crashReport.Contains("err 1", StringComparison.Ordinal),
              $"DiscordReportFormatter crash report caps tail lines to produce exactly {crashLines.Length} lines (within 10–14) and < 1900 chars");

        // Case 3 (L9): Forward-slash Windows/macOS user paths (C:/Users/<user> and /Users/<user>) are redacted across all 3 formatters/scrubbers!
        var fwdDiscord1 = DiscordReportFormatter.Format(
            "2.0.3", "r7-a9c4", "clonedivers", "mixed-droids", true, true, "auto", "full", hw,
            "C:/Users/ForwardAlice/HellForge", null);
        var fwdDiscord2 = DiscordReportFormatter.Format(
            "2.0.3", "r7-a9c4", "clonedivers", "mixed-droids", true, true, "auto", "full", hw,
            "/Users/MacBob/HellForge", null);
        var scrubbedSquad = SquadReportClient.ScrubSensitiveText("Failed loading C:/Users/ForwardAlice/AppData and /Users/MacBob/Library");
        var scrubbedTriage = CrashTriage.SanitizeLogLine("Fault at C:/Users/ForwardAlice/HellForge and /Users/MacBob/HellForge");
        check(fwdDiscord1.Contains("C:/Users/<user>/HellForge", StringComparison.Ordinal) &&
              !fwdDiscord1.Contains("ForwardAlice", StringComparison.Ordinal) &&
              fwdDiscord2.Contains("/Users/<user>/HellForge", StringComparison.Ordinal) &&
              !fwdDiscord2.Contains("MacBob", StringComparison.Ordinal) &&
              !scrubbedSquad.Contains("ForwardAlice", StringComparison.Ordinal) &&
              !scrubbedSquad.Contains("MacBob", StringComparison.Ordinal) &&
              !scrubbedTriage.Contains("ForwardAlice", StringComparison.Ordinal) &&
              !scrubbedTriage.Contains("MacBob", StringComparison.Ordinal),
              "SquadReportClient, DiscordReportFormatter, and CrashTriage redact both backslash and forward-slash user paths (L9)");
    }

    sealed class MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    sealed class VerifierFakeCommands(string installRoot) : IHellForgeCommands
    {
        public List<HellForgeMod> Mods { get; } = [];
        public bool Multiplayer { get; set; } = true;
        public bool LocalPlayFab { get; set; } = true;
        readonly Dictionary<string, string> hashesByFolder = new(StringComparer.OrdinalIgnoreCase);

        public void SeedReadyGameplayMods(string root, bool scrambleOrder)
        {
            Mods.Clear();
            var modsDir = Path.Combine(root, "mods");
            Directory.CreateDirectory(modsDir);

            // Bundled mods
            foreach (var (id, name, ver) in new[]
            {
                ("hellforge.editor", "HellForge Editor", "0.4.2"),
                ("skyeshade.hell-forge", "Hell Forge Difficulty", "0.16.1"),
                ("skyeshade.vanilla-plus", "Vanilla Plus", "")
            })
            {
                var f = Path.Combine(modsDir, id);
                Directory.CreateDirectory(f);
                Mods.Add(new(id, name, true, f, ["gameplay"], ver));
            }

            // All 14 r7 catalog artifacts with matching archives/folders and hashes
            foreach (var artifact in HellForgeGameplayCatalog.Pinned.Artifacts)
            {
                var folder = Path.Combine(modsDir, artifact.Id);
                Directory.CreateDirectory(folder);
                string? archive = null;
                if (artifact.Id.StartsWith("pack.", StringComparison.Ordinal))
                {
                    // Create a matching tiny zip? Wait: InstalledGameplayHashAsync verifies exact artifact.Bytes & Sha256 for pack.* when Archive != null!
                    // Leaving Archive == null makes InstalledGameplayHashAsync call `mod-hash --mod folder` directly, which returns artifact.ContentHash!
                    archive = null;
                }
                hashesByFolder[folder] = artifact.ContentHash;

                HellForgeOption[]? options = artifact.Id == HellForgePreset.GuardDogs
                    ? [
                        new("Smarter Guard Dogs and Sentries", true),
                        new("Guard Dogs", true),
                        new("Sentries", true),
                        new("Intelligence", true),
                        new("Safety", true),
                        new("Sentry Safety", true),
                        new("SEAF Safety", true),
                        new("Targeting Marker", false)
                    ]
                    : null;

                Mods.Add(new(artifact.Id, artifact.Name, true, folder, ["gameplay"], artifact.Version, options, archive));
            }

            if (scrambleOrder)
                Mods.Reverse();
            else
                Mods.Sort((a, b) => HellForgeGameplayCatalog.Pinned.Order.IndexOf(a.Id).CompareTo(HellForgeGameplayCatalog.Pinned.Order.IndexOf(b.Id)));
        }

        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            object response;
            switch (args[0])
            {
                case "status":
                    response = new
                    {
                        install = installRoot,
                        hellforge = HellForgeRelease.Version,
                        engine = HellForgeRelease.Engine,
                        modSdk = HellForgeRelease.Sdk,
                        gameBuild = HellForgePack.SupportedBuild,
                        networkBlock = "wfp",
                        ready = new { gameCopy = true, sessionRunner = true, linuxNamespace = true },
                        session = new { running = false, lastStatus = "observed", lastFailure = (string?)null, lastRun = (string?)null },
                        settings = new { sandbox = true, multiplayer = Multiplayer, localPlayFab = LocalPlayFab }
                    };
                    break;

                case "mods":
                    response = new
                    {
                        mods = Mods.Select(m => new
                        {
                            id = m.Id,
                            name = m.Name,
                            enabled = m.Enabled,
                            folder = m.Folder,
                            tags = m.Tags,
                            version = m.Version,
                            archive = m.Archive,
                            options = m.Options?.Select(o => new { name = o.Name, on = o.On })
                        }),
                        problems = Array.Empty<string>(),
                        modlist = "r7"
                    };
                    break;

                case "mod-hash":
                    var target = args[2];
                    var hash = hashesByFolder.TryGetValue(target, out var known) ? known : new string('0', 64);
                    response = new { hash };
                    break;

                case "mod-switch":
                {
                    var id = args[2];
                    var on = args[4] == "yes";
                    var idx = Mods.FindIndex(m => m.Id == id);
                    if (idx >= 0) Mods[idx] = Mods[idx] with { Enabled = on };
                    response = new { ok = true };
                    break;
                }

                case "mod-pack":
                {
                    var id = args[2];
                    var idx = Mods.FindIndex(m => m.Id == id);
                    if (idx >= 0 && args[3] == "--option")
                    {
                        var optName = args[4];
                        var on = args[6] == "yes";
                        var opts = (Mods[idx].Options ?? []).ToList();
                        var oi = opts.FindIndex(o => o.Name == optName);
                        if (oi >= 0) opts[oi] = new(optName, on);
                        else opts.Add(new(optName, on));
                        Mods[idx] = Mods[idx] with { Options = opts.ToArray() };
                    }
                    response = new { ok = true };
                    break;
                }

                case "mod-order":
                {
                    var orderIds = args[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var rank = orderIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
                    Mods.Sort((a, b) => rank.GetValueOrDefault(a.Id, 999).CompareTo(rank.GetValueOrDefault(b.Id, 999)));
                    response = new { ok = true };
                    break;
                }

                case "mod-add":
                {
                    var manifestOrZip = args[2];
                    if (manifestOrZip.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(manifestOrZip));
                        var guid = doc.RootElement.GetProperty("Guid").GetString()!;
                        var name = doc.RootElement.GetProperty("Name").GetString()!;
                        var packId = "pack." + guid;
                        var dest = Path.Combine(installRoot, "mods", packId);
                        if (Directory.Exists(dest)) Directory.Delete(dest, true);
                        Directory.CreateDirectory(dest);
                        foreach (var srcFile in Directory.GetFiles(Path.GetDirectoryName(manifestOrZip)!))
                            File.Copy(srcFile, Path.Combine(dest, Path.GetFileName(srcFile)), overwrite: true);

                        var existing = Mods.FindIndex(m => m.Id == packId);
                        var modEntry = new HellForgeMod(packId, name, true, dest, ["cosmetic"]);
                        if (existing >= 0) Mods[existing] = modEntry;
                        else Mods.Add(modEntry);
                        response = new { id = packId };
                    }
                    else
                    {
                        response = new { ok = true };
                    }
                    break;
                }

                case "settings":
                    Multiplayer = true;
                    LocalPlayFab = true;
                    response = new { ok = true };
                    break;

                default:
                    response = new { ok = true };
                    break;
            }

            return Task.FromResult(JsonSerializer.SerializeToElement(response));
        }
    }
}
