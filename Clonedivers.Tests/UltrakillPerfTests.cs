using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls.Primitives;
using Clonedivers.Ui;

namespace Clonedivers.Tests;

internal static class UltrakillPerfTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        Console.WriteLine("v2.2.0 Performance Refactor & Level 12 ULTRAKILL (Full Clone Wars) tests:");
        var area = Path.Combine(root, "v220-ultrakill-perf");
        Directory.CreateDirectory(area);

        await TestPackArtifactEngineAsync(Path.Combine(area, "engine"), check);
        TestPinnedPackCatalogImmutability(check);
        TestHostMutatorUltrakillContract(Path.Combine(area, "host-options"), check);
        await TestUltrakillRuntimeProfileAsync(Path.Combine(area, "runtime-profile"), check);
        TestUltrakillCopyFormatting(check);
        await TestV221DefaultTheaterAndDiskReclaimAsync(Path.Combine(area, "v221-disk-reclaim"), check);
    }

    internal static void RunHeadlessUiChecks(Action<bool, string> check)
    {
        var host = new PreviewForgeHost("customize-advanced");
        var win = new ForgeShellWindow(host, "customize-advanced");
        var priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(ForgeShellWindow).GetField("showingCustomize", priv)!.SetValue(win, true);
        typeof(ForgeShellWindow).GetField("showingAdvanced", priv)!.SetValue(win, true);
        typeof(ForgeShellWindow).GetMethod("RefreshAll", priv)!.Invoke(win, null);
        var theaterPills = (Dictionary<string, ToggleButton>)typeof(ForgeShellWindow).GetField("theaterPills", priv)!.GetValue(win)!;
        check(theaterPills.Count == 2 &&
              theaterPills.ContainsKey("standard") && theaterPills["standard"].IsChecked == false &&
              theaterPills.ContainsKey("clone-wars") && theaterPills["clone-wars"].IsChecked == true,
              "ForgeShellWindow Customize -> Advanced defaults Theater Scale to L12 ULTRAKILL · Clone Wars");

        host.UpdateHostMutatorsAsync("standard", "standard", true, "standard").GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        typeof(ForgeShellWindow).GetMethod("RefreshAll", priv)!.Invoke(win, null);
        check(theaterPills["standard"].IsChecked == true && theaterPills["clone-wars"].IsChecked == false,
            "Selecting Standard (D1–11) updates Theater Scale pills in ForgeShellWindow");
        win.Close();
    }

    static async Task TestPackArtifactEngineAsync(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);

        // 1. Single-pass CopyAndHashAsync matches SHA-256 across multi-MiB payload and 0-byte payload
        var payload = new byte[(2 * 1024 * 1024) + 12345];
        new Random(42).NextBytes(payload);
        var expectedSha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var srcFile = Path.Combine(dir, "source.patch_0");
        var dstFile = Path.Combine(dir, "copied.patch_0");
        await File.WriteAllBytesAsync(srcFile, payload);

        long lastProgress = 0;
        var actualSha = await PackArtifactEngine.CopyAndHashAsync(srcFile, dstFile, payload.Length, CancellationToken.None, b => lastProgress = b);
        check(actualSha == expectedSha && lastProgress == payload.Length && File.ReadAllBytes(dstFile).SequenceEqual(payload),
            "PackArtifactEngine.CopyAndHashAsync copies and computes SHA-256 in a single 1 MiB ArrayPool pass");

        var emptySrc = Path.Combine(dir, "empty.patch_0");
        var emptyDst = Path.Combine(dir, "empty_out.patch_0");
        await File.WriteAllBytesAsync(emptySrc, []);
        var emptySha = await PackArtifactEngine.CopyAndHashAsync(emptySrc, emptyDst, 0);
        check(emptySha == PackFile.EmptySha && new FileInfo(emptyDst).Length == 0,
            "PackArtifactEngine.CopyAndHashAsync handles 0-byte files cleanly");

        bool rejectedWrongSize = false;
        try { await PackArtifactEngine.CopyAndHashAsync(srcFile, Path.Combine(dir, "bad.patch_0"), payload.Length - 1); }
        catch (InvalidDataException) { rejectedWrongSize = true; }
        check(rejectedWrongSize, "PackArtifactEngine.CopyAndHashAsync rejects size mismatches upfront");

        // 2. Metadata hash cache with 2-second racy-clean guard
        var cachePath = Path.Combine(dir, PackArtifactEngine.HashCacheFileName);
        var cache = PackArtifactEngine.LoadHashCache(cachePath);
        var agedFile = Path.Combine(dir, "aged.patch_0");
        await File.WriteAllBytesAsync(agedFile, payload);
        var oldTime = DateTime.UtcNow.AddMinutes(-5);
        File.SetCreationTimeUtc(agedFile, oldTime);
        File.SetLastWriteTimeUtc(agedFile, oldTime);

        var cachedHash1 = await PackArtifactEngine.HashWithMetadataCacheAsync(agedFile, cache);
        PackArtifactEngine.SaveHashCache(cachePath, cache);
        var reloadedCache = PackArtifactEngine.LoadHashCache(cachePath);
        var cachedHash2 = await PackArtifactEngine.HashWithMetadataCacheAsync(agedFile, reloadedCache);
        check(cachedHash1 == expectedSha && cachedHash2 == expectedSha && reloadedCache.ContainsKey(Path.GetFullPath(agedFile)),
            "PackArtifactEngine.HashWithMetadataCacheAsync persists and reuses aged file hashes across reloads");

        // Mutating mtime/content invalidates cached entry immediately
        var altPayload = Encoding.UTF8.GetBytes("mutated-bytes-check");
        var altExpected = Convert.ToHexString(SHA256.HashData(altPayload)).ToLowerInvariant();
        await File.WriteAllBytesAsync(agedFile, altPayload);
        File.SetLastWriteTimeUtc(agedFile, DateTime.UtcNow); // fresh write inside racy window!
        var freshHash = await PackArtifactEngine.HashWithMetadataCacheAsync(agedFile, reloadedCache);
        check(freshHash == altExpected,
            "PackArtifactEngine.HashWithMetadataCacheAsync bypasses stale cache on modified or racy-window files");

        // 3. Bounded-parallel VerifyPackFilesAsync
        var verifyDir = Path.Combine(dir, "parallel-verify");
        Directory.CreateDirectory(verifyDir);
        var packFiles = new List<PackFile>();
        for (int i = 0; i < 6; i++)
        {
            var bytes = Encoding.UTF8.GetBytes($"blob-{i}-" + new string((char)('a' + i), 4096));
            var name = $"9ba626afa44a3aa3.patch_{i}";
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await File.WriteAllBytesAsync(Path.Combine(verifyDir, name), bytes);
            packFiles.Add(new PackFile { Name = name, Size = bytes.Length, Sha256 = sha, Url = $"https://github.com/owendavidgoode/clonedivers/releases/download/v2.2.0/{name}" });
        }
        var seenProgress = new List<(long Done, long Total)>();
        await PackArtifactEngine.VerifyPackFilesAsync(verifyDir, packFiles, new SyncProgress<(long Done, long Total)>(seenProgress.Add));
        long expectedTotal = packFiles.Sum(f => f.Size);
        bool monotonic = true;
        for (int i = 1; i < seenProgress.Count; i++)
            if (seenProgress[i].Done < seenProgress[i - 1].Done) monotonic = false;
        check(seenProgress.Count >= 2 && seenProgress[^1] == (expectedTotal, expectedTotal) && monotonic,
            "PackArtifactEngine.VerifyPackFilesAsync verifies multi-file packs in parallel with monotonic byte progress");

        // Tamper one byte -> VerifyPackFilesAsync must fail!
        var tamperedPath = Path.Combine(verifyDir, packFiles[3].Name);
        var tamperedBytes = await File.ReadAllBytesAsync(tamperedPath);
        tamperedBytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(tamperedPath, tamperedBytes);
        bool caughtTamper = false;
        try { await PackArtifactEngine.VerifyPackFilesAsync(verifyDir, packFiles); }
        catch (InvalidDataException) { caughtTamper = true; }
        check(caughtTamper, "PackArtifactEngine.VerifyPackFilesAsync detects 1-byte corruption with identical file length");
    }

    static void TestPinnedPackCatalogImmutability(Action<bool, string> check)
    {
        // Calling Empire selection with droids=false, covenant=false must NEVER mutate the cached manifest
        // for a subsequent call with droids=true, mixed-covenant=true, deathtrooper=true!
        var minimal = HellForgePack.PinnedSelection("empire", "potato256", new Dictionary<string, bool>
        {
            ["droids"] = false,
            ["covenant"] = false,
            ["mixed-droids"] = false,
            ["mixed-covenant"] = false,
            ["deathtrooper"] = false
        });
        var fullMix = HellForgePack.PinnedSelection("empire", "full", new Dictionary<string, bool>
        {
            ["droids"] = true,
            ["covenant"] = true,
            ["mixed-droids"] = true,
            ["mixed-covenant"] = true,
            ["deathtrooper"] = true
        });
        var minimalAgain = HellForgePack.PinnedSelection("empire", "potato256", new Dictionary<string, bool>
        {
            ["droids"] = false,
            ["covenant"] = false,
            ["mixed-droids"] = false,
            ["mixed-covenant"] = false,
            ["deathtrooper"] = false
        });
        check(fullMix.Files.Count > minimal.Files.Count &&
              minimal.Files.Count == minimalAgain.Files.Count &&
              fullMix.ManifestSha256 == minimal.ManifestSha256,
              "PinnedPackCatalog caches Empire & Clones manifests without mutating Files across option/texture switches");
    }

    static void TestHostMutatorUltrakillContract(string dir, Action<bool, string> check)
    {
        var installRoot = Path.Combine(dir, "HellForge");
        Directory.CreateDirectory(Path.Combine(installRoot, "mods"));

        var stdOpts = HostMutatorConfig.BuildOptions("standard", "standard", true, "standard");
        HostMutatorConfig.Write(installRoot, stdOpts);
        var stdJson = File.ReadAllText(HostMutatorConfig.GetFilePath(installRoot));
        var stdRead = HostMutatorConfig.Read(installRoot);
        check(!stdRead.IsUltrakillTheater && !stdJson.Contains("theaterPreset", StringComparison.Ordinal),
            "Standard HostMutatorOptions omits null ULTRAKILL fields to preserve minimal format-1 JSON");

        var cwOpts = HostMutatorConfig.BuildOptions("standard", "standard", true, "clone-wars");
        HostMutatorConfig.Write(installRoot, cwOpts);
        var cwRead = HostMutatorConfig.Read(installRoot);
        check(cwRead.IsUltrakillTheater &&
              cwRead.TheaterPreset == "clone-wars" &&
              cwRead.ChaosIntensity == "clone-wars" &&
              cwRead.HeavySeaf == "clone-wars" &&
              cwRead.FasterCorpseCleanup &&
              cwRead.CorpseDelaySeconds == 0 &&
              cwRead.CorpseTurboReaper == true &&
              cwRead.TargetActiveUnits == 820 &&
              cwRead.VirtualTheaterBudget == 2050 &&
              (cwRead.TargetActiveUnits + cwRead.VirtualTheaterBudget) == 2870 &&
              cwRead.MapRadiusPermille == 920 &&
              cwRead.SeafWaveCeiling == 78,
              "Clone Wars HostMutatorOptions round-trips 2,870 combined theater budget (820 real + 2,050 virtual < 1,024 GwNav::Bots), 920 permille map radius, and 0.0s turbo corpse reaper");
    }

    static async Task TestUltrakillRuntimeProfileAsync(string dir, Action<bool, string> check)
    {
        var installRoot = Path.Combine(dir, "HellForge");
        Directory.CreateDirectory(Path.Combine(installRoot, "game", "data"));
        Directory.CreateDirectory(Path.Combine(installRoot, "userdata", "profiles", "default", "saves"));
        await File.WriteAllTextAsync(Path.Combine(installRoot, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");

        const string origSettingsIni =
            "wwise = {\n" +
            "\tsound_engine = {\n" +
            "\t\tcommand_queue_size = 524288\n" +
            "\t\tpool_size = 33554432\n" +
            "\t\tnum_samples_per_frame = 1024\n" +
            "\t}\n" +
            "}\n" +
            "win32 = {\n" +
            "\trenderer = {\n" +
            "\t\td3d12 = {\n" +
            "\t\t\ttexture_heap_usage_limit = 5376\n" +
            "\t\t\tscratch_space = 128\n" +
            "\t\t}\n" +
            "\t}\n" +
            "}\n" +
            "max_num_jobs = 8192";

        const string origUserSettings =
            "enable_resource_lock_debug = true\n" +
            "render_settings = {\n" +
            "\tshadows = 4\n" +
            "\tterrain_trample_enabled = true\n" +
            "\tforward_local_lights_cast_shadows = true\n" +
            "\tparticle_quality = 3\n" +
            "\tlod_selection_multiplier = 1.5\n" +
            "\tsun_shadow_map_size = [\n" +
            "\t\t4096\n" +
            "\t\t4096\n" +
            "\t]\n" +
            "\thq_bubble_enabled = false\n" +
            "\tparticles_receive_shadows = true\n" +
            "\trender_resolution = [\n" +
            "\t\t5120\n" +
            "\t\t1440\n" +
            "\t]\n" +
            "}\n" +
            "vertical_fov = 85";

        var settingsIniPath = Path.Combine(installRoot, "game", "data", "settings.ini");
        var userSettingsPath = Path.Combine(installRoot, "userdata", "profiles", "default", "user_settings.config");
        var saveUserSettingsPath = Path.Combine(installRoot, "userdata", "profiles", "default", "saves", "110000104043926_user_settings.config");
        var origIniBytes = Encoding.UTF8.GetBytes(origSettingsIni);
        var origCfgBytes = Encoding.UTF8.GetBytes(origUserSettings);
        await File.WriteAllBytesAsync(settingsIniPath, origIniBytes);
        await File.WriteAllBytesAsync(userSettingsPath, origCfgBytes);
        await File.WriteAllBytesAsync(saveUserSettingsPath, origCfgBytes);

        // Apply twice to verify idempotency (second apply must NOT overwrite clean original backup!)
        bool applied1 = UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: true);
        bool applied2 = UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: true);
        var patchedIni = await File.ReadAllTextAsync(settingsIniPath);
        var patchedCfg = await File.ReadAllTextAsync(userSettingsPath);
        var patchedSaveCfg = await File.ReadAllTextAsync(saveUserSettingsPath);

        check(applied1 && applied2 &&
              patchedIni.Contains("max_num_jobs = 32768", StringComparison.Ordinal) &&
              patchedIni.Contains("scratch_space = 256", StringComparison.Ordinal) &&
              patchedIni.Contains("command_queue_size = 2097152", StringComparison.Ordinal) &&
              patchedIni.Contains("pool_size = 67108864", StringComparison.Ordinal) &&
              patchedCfg.Contains("enable_resource_lock_debug = false", StringComparison.Ordinal) &&
              patchedCfg.Contains("lod_selection_multiplier = 1.5", StringComparison.Ordinal) &&
              patchedCfg.Contains("hq_bubble_enabled = true", StringComparison.Ordinal) &&
              patchedCfg.Contains("terrain_trample_enabled = false", StringComparison.Ordinal) &&
              patchedCfg.Contains("particle_quality = 1", StringComparison.Ordinal) &&
              patchedCfg.Contains("vertical_fov = 85", StringComparison.Ordinal) &&
              patchedCfg.Contains("5120", StringComparison.Ordinal) &&
              patchedSaveCfg == patchedCfg,
              "UltrakillRuntimeProfile.SyncEngineFiles patches Stingray fiber/Wwise/D3D12 & crowd shadow settings while preserving lod_selection_multiplier, resolution, & FOV");

        // Simulate a pre-v2.2.1 install whose manifest lacks LodMigratedVersion (0) and whose live user_settings.config had
        // lod_selection_multiplier = 0.6 written by v2.2.0 (plus an in-game FOV edit to 92):
        // SyncEngineFiles(true) must restore lod_selection_multiplier = 1.5 ONCE from originals/ while keeping FOV = 92!
        var liveBackupDir = UltrakillRuntimeProfile.GetBackupDirectory(installRoot);
        var liveManifestPath = Path.Combine(liveBackupDir, UltrakillRuntimeProfile.ManifestFileName);
        var pre221ManifestJson = (await File.ReadAllTextAsync(liveManifestPath))
            .Replace("\"LodMigratedVersion\": 1", "\"LodMigratedVersion\": 0", StringComparison.Ordinal);
        await File.WriteAllTextAsync(liveManifestPath, pre221ManifestJson);
        var v220StaleCfg = patchedCfg
            .Replace("lod_selection_multiplier = 1.5", "lod_selection_multiplier = 0.6", StringComparison.Ordinal)
            .Replace("vertical_fov = 85", "vertical_fov = 92", StringComparison.Ordinal);
        await File.WriteAllTextAsync(userSettingsPath, v220StaleCfg);
        await File.WriteAllTextAsync(saveUserSettingsPath, v220StaleCfg);
        bool upgradedFromV220 = UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: true);
        var upgradedCfg = await File.ReadAllTextAsync(userSettingsPath);
        var upgradedSaveCfg = await File.ReadAllTextAsync(saveUserSettingsPath);
        check(upgradedFromV220 &&
              upgradedCfg.Contains("lod_selection_multiplier = 1.5", StringComparison.Ordinal) &&
              !upgradedCfg.Contains("lod_selection_multiplier = 0.6", StringComparison.Ordinal) &&
              upgradedCfg.Contains("vertical_fov = 92", StringComparison.Ordinal) &&
              upgradedCfg.Contains("shadows = 2", StringComparison.Ordinal) &&
              upgradedSaveCfg == upgradedCfg,
              "UltrakillRuntimeProfile.SyncEngineFiles(true) heals v2.2.0 lod_selection_multiplier = 0.6 once from SHA-256-verified originals/ while keeping player FOV changes");

        // Subsequent player LOD edits (e.g. 1.2 or even 0.6) after migration MUST survive later SyncEngineFiles(true) calls!
        var playerEditedLod12 = upgradedCfg.Replace("lod_selection_multiplier = 1.5", "lod_selection_multiplier = 1.2", StringComparison.Ordinal);
        await File.WriteAllTextAsync(userSettingsPath, playerEditedLod12);
        bool resyncAfter12 = UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: true);
        var afterResync12 = await File.ReadAllTextAsync(userSettingsPath);
        var playerEditedLod06 = afterResync12.Replace("lod_selection_multiplier = 1.2", "lod_selection_multiplier = 0.6", StringComparison.Ordinal);
        await File.WriteAllTextAsync(userSettingsPath, playerEditedLod06);
        bool resyncAfter06 = UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: true);
        var afterResync06 = await File.ReadAllTextAsync(userSettingsPath);
        check(resyncAfter12 && resyncAfter06 &&
              afterResync12.Contains("lod_selection_multiplier = 1.2", StringComparison.Ordinal) &&
              afterResync06.Contains("lod_selection_multiplier = 0.6", StringComparison.Ordinal),
              "Post-migration player edits to lod_selection_multiplier (1.2 and 0.6) are preserved across subsequent SyncEngineFiles(true) runs");

        // Disarm -> restores byte-exact originals!
        bool restored = UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: false);
        check(restored &&
              (await File.ReadAllBytesAsync(settingsIniPath)).SequenceEqual(origIniBytes) &&
              (await File.ReadAllBytesAsync(userSettingsPath)).SequenceEqual(origCfgBytes) &&
              (await File.ReadAllBytesAsync(saveUserSettingsPath)).SequenceEqual(origCfgBytes) &&
              !File.Exists(Path.Combine(UltrakillRuntimeProfile.GetBackupDirectory(installRoot), UltrakillRuntimeProfile.ManifestFileName)),
              "UltrakillRuntimeProfile.SyncEngineFiles(false) restores settings.ini and user_settings.config byte-for-byte with SHA-256 verification");

        // Refuse Steam library paths!
        bool rejectedSteam = false;
        try { UltrakillRuntimeProfile.SyncEngineFiles(Path.Combine(dir, "steamapps", "common", "Helldivers 2"), true); }
        catch (InvalidDataException) { rejectedSteam = true; }
        check(rejectedSteam, "UltrakillRuntimeProfile refuses any path under steamapps");

        // Sibling installations under the same parent folder keep isolated backups and never clobber each other
        var siblingRoot = Path.Combine(dir, "HellForgeSibling");
        Directory.CreateDirectory(Path.Combine(siblingRoot, "game", "data"));
        await File.WriteAllTextAsync(Path.Combine(siblingRoot, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");
        var siblingIniPath = Path.Combine(siblingRoot, "game", "data", "settings.ini");
        var siblingOrigIniBytes = Encoding.UTF8.GetBytes(origSettingsIni.Replace("8192", "4096", StringComparison.Ordinal));
        await File.WriteAllBytesAsync(siblingIniPath, siblingOrigIniBytes);
        check(!string.Equals(
                  UltrakillRuntimeProfile.GetBackupDirectory(installRoot),
                  UltrakillRuntimeProfile.GetBackupDirectory(siblingRoot),
                  PlatformPaths.Comparison),
              "UltrakillRuntimeProfile namespaces backup folders per normalized installation root");
        check(UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: true) &&
              UltrakillRuntimeProfile.SyncEngineFiles(siblingRoot, enableUltrakill: true),
              "Sibling installations both arm ULTRAKILL without colliding in Clonedivers-HellForge-cache/ultrakill");
        check(UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: false) &&
              (await File.ReadAllBytesAsync(settingsIniPath)).SequenceEqual(origIniBytes) &&
              (await File.ReadAllTextAsync(siblingIniPath)).Contains("max_num_jobs = 32768", StringComparison.Ordinal),
              "Disarming installation A restores A byte-for-byte while leaving sibling installation B armed");
        check(UltrakillRuntimeProfile.SyncEngineFiles(siblingRoot, enableUltrakill: false) &&
              (await File.ReadAllBytesAsync(siblingIniPath)).SequenceEqual(siblingOrigIniBytes),
              "Disarming sibling installation B restores B's distinct original bytes byte-for-byte");

        // Refuse to overwrite an existing incompatible backup manifest
        var badBackupDir = UltrakillRuntimeProfile.GetBackupDirectory(installRoot);
        Directory.CreateDirectory(badBackupDir);
        var badManifestPath = Path.Combine(badBackupDir, UltrakillRuntimeProfile.ManifestFileName);
        await File.WriteAllTextAsync(badManifestPath, """{"Format":1,"InstallRoot":"C:\\Other\\HellForge","Files":[]}""");
        bool refusedForeignManifest = false;
        try { UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: true); }
        catch (InvalidDataException) { refusedForeignManifest = true; }
        finally { File.Delete(badManifestPath); }
        check(refusedForeignManifest, "UltrakillRuntimeProfile refuses to overwrite an existing backup manifest from another root");

        // Refuse malformed backup manifest JSON while preserving any existing original backup file untouched
        Directory.CreateDirectory(Path.Combine(badBackupDir, "originals"));
        var sentinelOrig = Path.Combine(badBackupDir, "originals", "sentinel_settings.ini");
        var sentinelBytes = Encoding.UTF8.GetBytes("precious-original-backup");
        await File.WriteAllBytesAsync(sentinelOrig, sentinelBytes);
        await File.WriteAllTextAsync(badManifestPath, "{not-valid-json");
        bool refusedMalformedManifest = false;
        try { UltrakillRuntimeProfile.SyncEngineFiles(installRoot, enableUltrakill: true); }
        catch (InvalidDataException) { refusedMalformedManifest = true; }
        check(refusedMalformedManifest && (await File.ReadAllBytesAsync(sentinelOrig)).SequenceEqual(sentinelBytes),
            "UltrakillRuntimeProfile rejects malformed manifest JSON without clobbering existing backup files");
        File.Delete(badManifestPath);
        File.Delete(sentinelOrig);

        // Companion Lua scan suppression preserves Squad Match Code (Enabled stays true; only options toggle)
        var cliCalls = new List<string>();
        var mockCatalog = new HellForgeMods(
            Mods:
            [
                new HellForgeMod(HellForgePreset.SmarterSeaf, "Smarter SEAF", true, @"D:\HellForge\mods\smarter", ["gameplay"], "1.0.4",
                    Options: [new HellForgeOption("Main", true)]),
                new HellForgeMod(HellForgePreset.GuardDogs, "Guard Dogs", true, @"D:\HellForge\mods\dogs", ["gameplay"], "1.0.2",
                    Options: [new HellForgeOption("SEAF Safety", true)])
            ],
            Problems: [],
            Code: null);
        await UltrakillRuntimeProfile.SyncCompanionModOptionsAsync(
            (args, _) => { cliCalls.Add(string.Join(" ", args)); return Task.CompletedTask; },
            mockCatalog,
            enableUltrakill: true);
        check(cliCalls.Count == 2 &&
              cliCalls[0] == $"mod-pack --id {HellForgePreset.SmarterSeaf} --all no" &&
              cliCalls[1] == $"mod-pack --id {HellForgePreset.GuardDogs} --option SEAF Safety --on no",
              "UltrakillRuntimeProfile.SyncCompanionModOptionsAsync suppresses Smarter SEAF & SEAF Safety options without disabling mods");
    }

    static void TestUltrakillCopyFormatting(Action<bool, string> check)
    {
        var demoHw = new HardwarePerformanceRecommendation("NVIDIA GeForce RTX 5090", 32.0, 64.0, "ultra", "reason");
        var summary = ForgeTacticalCopy.FormatLoadoutSummary(
            "clonedivers", "mixed-droids", true, true, true, "auto", demoHw,
            hostChaosIntensity: "clone-wars", hostHeavySeaf: "clone-wars", hostFasterCorpseCleanup: true,
            squidSkin: "mixed-covenant", deathTrooperVoice: false, hostTheaterPreset: "clone-wars");
        var readyLine = ForgeTacticalCopy.ReadyDetail("clonedivers", firstLaunch: false, hostTheaterPreset: "clone-wars");
        var discord = DiscordReportFormatter.Format(
            appVersion: AppInfo.Version, squadMatchCode: "r14-1234", mode: "clonedivers", enemySkin: "mixed-droids",
            covenant: true, deltaSquad: true, performanceTier: "auto", packTexture: "full", hardware: demoHw,
            installRoot: @"D:\HellForge", triage: null, redactUserPaths: true, squidSkin: "mixed-covenant",
            deathTrooperVoice: false, hostTheaterPreset: "clone-wars");

        check(summary == "Full Star Wars Mix · Auto (Ultra) · L12 ULTRAKILL · Clone Wars (2–3k)" &&
              readyLine == "Venator in orbit · D12 ULTRAKILL ready · Steam co-op enabled" &&
              discord.Contains("· L12 ULTRAKILL", StringComparison.Ordinal) &&
              discord.Split('\n').Length is >= DiscordReportFormatter.MinLineCount and <= DiscordReportFormatter.MaxLineCount,
              "ForgeTacticalCopy & DiscordReportFormatter surface Level 12 ULTRAKILL Clone Wars status within bounded line limits");
    }

    static async Task TestV221DefaultTheaterAndDiskReclaimAsync(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);

        // 1. Default Clone Wars migration for pre-v2.2.1 configs vs preserved v2.2.1+ 'standard' choice
        var freshPath = Path.Combine(dir, "fresh-v221", "hellforge.json");
        var fresh = HellForgePreferences.Load(freshPath);
        check(fresh.HostTheaterPreset == "clone-wars" && fresh.HostTheaterDefaultVersion == 2,
            "Brand-new HellForgePreferences defaults HostTheaterPreset to 'clone-wars' (HostTheaterDefaultVersion=2)");

        var pre221Path = Path.Combine(dir, "pre221-hellforge.json");
        await File.WriteAllTextAsync(pre221Path, """{"Format":1,"Mode":"empire","HostTheaterPreset":"standard"}""");
        var migrated = HellForgePreferences.Load(pre221Path);
        check(migrated.HostTheaterPreset == "clone-wars" && migrated.HostTheaterDefaultVersion == 2,
            "Pre-v2.2.1 hellforge.json without HostTheaterDefaultVersion migrates 'standard' once to 'clone-wars'");

        migrated.HostTheaterPreset = "standard";
        migrated.Save(pre221Path);
        var preservedStd = HellForgePreferences.Load(pre221Path);
        check(preservedStd.HostTheaterPreset == "standard" && preservedStd.HostTheaterDefaultVersion == 2,
            "v2.2.1+ hellforge.json with HostTheaterDefaultVersion=2 preserves an explicit user switch back to 'standard'");

        // 2. ForgeDiskManager reclaims 'applied' / 'restored' backups, '.json.retired-*' records, and 'gameplay-*' backups
        //    while strictly protecting 'pending' transaction backups and failing closed on corrupt JSON!
        var prevRunning = Game.IsRunningCheck;
        try
        {
            Game.IsRunningCheck = () => false;
            var root = Path.Combine(dir, "disk-reclaim-case", "HellForge");
            var cache = Path.Combine(dir, "disk-reclaim-case", "Clonedivers-HellForge-cache");
            var backupsDir = Path.Combine(cache, "backups");
            var txDir = Path.Combine(cache, "transactions");
            var exportsDir = Path.Combine(cache, "exports");
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            Directory.CreateDirectory(backupsDir);
            Directory.CreateDirectory(txDir);
            Directory.CreateDirectory(exportsDir);

            var appliedBackup = Path.Combine(backupsDir, "20261127-053558-6fe594ee04ce4c07908ebe416df3b3aa");
            Directory.CreateDirectory(appliedBackup);
            await File.WriteAllBytesAsync(Path.Combine(appliedBackup, "payload.bin"), new byte[16384]);
            await File.WriteAllTextAsync(Path.Combine(appliedBackup, "outcome.json"), """{"outcome":"applied","utc":"2026-11-27T05:36:00Z"}""");
            await File.WriteAllTextAsync(Path.Combine(txDir, "abc.json"),
                JsonSerializer.Serialize(new { Root = root, Backup = appliedBackup, State = "applied" }));

            var pendingBackup = Path.Combine(backupsDir, "20261127-053900-11111111111111111111111111111111");
            Directory.CreateDirectory(pendingBackup);
            await File.WriteAllBytesAsync(Path.Combine(pendingBackup, "payload.bin"), new byte[8192]);
            await File.WriteAllTextAsync(Path.Combine(pendingBackup, "outcome.json"), """{"outcome":"applied","utc":"2026-11-27T05:39:00Z"}""");
            await File.WriteAllTextAsync(Path.Combine(txDir, "pending.json"),
                JsonSerializer.Serialize(new { Root = root, Backup = pendingBackup, State = "pending" }));

            var retiredPendingBackup = Path.Combine(backupsDir, "gameplay-22222222222222222222222222222222");
            Directory.CreateDirectory(retiredPendingBackup);
            await File.WriteAllBytesAsync(Path.Combine(retiredPendingBackup, "old.zip"), new byte[4096]);
            await File.WriteAllTextAsync(Path.Combine(txDir, "abc.gameplay.json.retired-deadbeef"),
                JsonSerializer.Serialize(new { Root = root, Backup = retiredPendingBackup, State = "pending" }));

            var unattestedGameplayBackup = Path.Combine(backupsDir, "gameplay-25252525252525252525252525252525");
            Directory.CreateDirectory(unattestedGameplayBackup);
            await File.WriteAllBytesAsync(Path.Combine(unattestedGameplayBackup, "unattested.zip"), new byte[2048]);

            var completedGameplayBackup = Path.Combine(backupsDir, "gameplay-29292929292929292929292929292929");
            Directory.CreateDirectory(completedGameplayBackup);
            await File.WriteAllBytesAsync(Path.Combine(completedGameplayBackup, "done.zip"), new byte[4096]);
            await File.WriteAllTextAsync(Path.Combine(txDir, "done.gameplay.json"),
                JsonSerializer.Serialize(new { Root = root, Backup = completedGameplayBackup, State = "applied" }));

            var analysis = ForgeDiskManager.Analyze(root);
            check(analysis.ReclaimableBytes >= 16384 + 4096 && analysis.ReclaimableBytes < 16384 + 4096 + 4096,
                "ForgeDiskManager.Analyze counts 'State':'applied' pack & gameplay backups as reclaimable while excluding retired 'pending' & unattested gameplay backups");

            var freed = ForgeDiskManager.CleanSafeFilesSync(root);
            check(freed >= 16384 + 4096 &&
                  !Directory.Exists(appliedBackup) &&
                  !Directory.Exists(completedGameplayBackup) &&
                  Directory.Exists(retiredPendingBackup) &&
                  Directory.Exists(unattestedGameplayBackup) &&
                  Directory.Exists(pendingBackup),
                "ForgeDiskManager.CleanSafeFilesSync reclaims 'applied' pack & gameplay backups while strictly protecting active 'pending', retired 'pending', and unattested gameplay backups");

            // Corrupt retired tx JSON ALSO fails closed on backups/
            var survivedOnCorrupt = Path.Combine(backupsDir, "20261127-054000-33333333333333333333333333333333");
            Directory.CreateDirectory(survivedOnCorrupt);
            await File.WriteAllBytesAsync(Path.Combine(survivedOnCorrupt, "payload.bin"), new byte[4096]);
            await File.WriteAllTextAsync(Path.Combine(survivedOnCorrupt, "outcome.json"), """{"outcome":"applied"}""");
            var corruptTx = Path.Combine(txDir, "corrupt.gameplay.json.retired-bad");
            await File.WriteAllTextAsync(corruptTx, "{corrupt-json");
            ForgeDiskManager.CleanSafeFilesSync(root);
            check(Directory.Exists(survivedOnCorrupt),
                "ForgeDiskManager fails closed on backups/ when an active or retired transaction record is corrupt");
            File.Delete(corruptTx);
            File.Delete(Path.Combine(txDir, "abc.gameplay.json.retired-deadbeef"));
            File.Delete(Path.Combine(txDir, "pending.json"));

            // 3. EnsureStagingAndInstallCapacity reclaims trapped 'applied' backup + unpreserved export without double-counting ExportReserve
            var trappedBackup = Path.Combine(backupsDir, "20261127-054500-44444444444444444444444444444444");
            Directory.CreateDirectory(trappedBackup);
            await File.WriteAllBytesAsync(Path.Combine(trappedBackup, "trapped.bin"), new byte[32768]);
            await File.WriteAllTextAsync(Path.Combine(trappedBackup, "outcome.json"), """{"outcome":"applied"}""");
            await File.WriteAllTextAsync(Path.Combine(txDir, "trapped.json"),
                JsonSerializer.Serialize(new { Root = root, Backup = trappedBackup, State = "applied" }));

            var sampleBytes = Encoding.UTF8.GetBytes("empire-pack-payload-bytes");
            var sampleSha = Convert.ToHexString(SHA256.HashData(sampleBytes)).ToLowerInvariant();
            var sampleSel = new HellForgePack.Selection("empire", "full", "2026.11.27-r54", HellForgePack.SupportedBuild, new string('a', 64),
                new() { ["droids"] = true }, [new PackFile { Name = "9ba626afa44a3aa3.patch_1000", Size = sampleBytes.Length, Sha256 = sampleSha }]);

            var staleExport = Path.Combine(exportsDir, "clonedivers-" + new string('b', 32));
            Directory.CreateDirectory(staleExport);
            await File.WriteAllBytesAsync(Path.Combine(staleExport, "9ba626afa44a3aa3.patch_0"), new byte[16384]);
            await File.WriteAllTextAsync(Path.Combine(staleExport, HellForgePack.ReceiptName),
                JsonSerializer.Serialize(new HellForgePack.Receipt
                {
                    Selection = new HellForgePack.Selection("clonedivers", "full", "2026.11.27-r25", HellForgePack.SupportedBuild, new string('c', 64),
                        new() { ["droids"] = true }, [])
                }, HellForgePack.Json));

            var targetExport = Path.Combine(exportsDir, "empire-" + new string('e', 32));
            var plan = new HellForgePack.Plan(sampleSel, targetExport, new());

            // Simulate tight free space: below required BEFORE reclaim, above required AFTER trappedBackup + staleExport are deleted!
            long singleReserveRequired = checked(plan.Bytes + 65536L + HellForgePack.ExportReserve + (plan.Bytes * 2));
            long simulatedBaseFree = singleReserveRequired - 20000L; // Needs >= 20,000 B reclaimed; trappedBackup (32,768) + staleExport (16,384) = 49,152 B!
            long? DynamicFree(string _)
            {
                long stillOccupying = (Directory.Exists(trappedBackup) ? 32768L : 0L) + (Directory.Exists(staleExport) ? 16384L : 0L);
                return simulatedBaseFree + (49152L - stillOccupying);
            }

            PackArtifactEngine.EnsureStagingAndInstallCapacity(root, cache, plan, existingInstalledPackBytes: 0, DynamicFree);
            check(!Directory.Exists(trappedBackup) && !Directory.Exists(staleExport),
                "PackArtifactEngine.EnsureStagingAndInstallCapacity reclaims 'State':'applied' backups and unpreserved launcher exports on low-space drives without double-counting ExportReserve");

            // 4. callerOwnedLockPath allows in-flight InstallPackAsync/PrunePackStorage to clean while holding its own lock, yet blocks on foreign locks!
            var selfLockBackup = Path.Combine(backupsDir, "20261127-055000-55555555555555555555555555555555");
            Directory.CreateDirectory(selfLockBackup);
            await File.WriteAllBytesAsync(Path.Combine(selfLockBackup, "payload.bin"), new byte[8192]);
            await File.WriteAllTextAsync(Path.Combine(selfLockBackup, "outcome.json"), """{"outcome":"applied"}""");
            var myLock = Path.Combine(txDir, "self.json.lock");
            var foreignLock = Path.Combine(txDir, "foreign.json.lock");
            using (new FileStream(myLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                using (new FileStream(foreignLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    ForgeDiskManager.CleanSafeFilesSync(root, callerOwnedLockPath: myLock);
                    check(Directory.Exists(selfLockBackup),
                        "ForgeDiskManager skips cleanup when another root's transaction lock is held alongside callerOwnedLockPath");
                }
                ForgeDiskManager.CleanSafeFilesSync(root, callerOwnedLockPath: myLock);
                check(!Directory.Exists(selfLockBackup),
                    "ForgeDiskManager reclaims completed backups when only callerOwnedLockPath is held");
            }
        }
        finally
        {
            Game.IsRunningCheck = prevRunning;
        }
    }
}
