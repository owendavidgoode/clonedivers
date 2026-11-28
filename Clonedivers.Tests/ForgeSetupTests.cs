using System.Security.Cryptography;
using System.Text;
using Clonedivers;

namespace Clonedivers.Tests;

internal static class ForgeSetupTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var area = Path.Combine(root, "forge-onboarding"); Directory.CreateDirectory(area);
        void Refuses(Action action, string name) { try { action(); check(false, name); } catch (InvalidDataException) { check(true, name); } }
        Refuses(() => HellForgeSetup.ValidateTarget("relative"), "setup refuses a relative install path");
        Refuses(() => HellForgeSetup.ValidateTarget(Path.Combine(area, "steamapps", "Forge")), "setup never installs into Steam's live files or prefix");
        Refuses(() => HellForgeSetup.ValidateTarget(Path.Combine(area, "OneDrive", "Forge")), "setup avoids a named OneDrive location");
        Refuses(() => HellForgeSetup.ValidateTarget(Path.Combine(area, "cloud-account", "Forge"), [Path.Combine(area, "cloud-account")]), "setup honors a custom cloud root outside the default OneDrive name");
        check(HellForgeSetup.ValidateTarget(Path.Combine(area, "cloud-account-backup", "Forge"), [Path.Combine(area, "cloud-account")]).EndsWith("Forge"), "cloud root boundary does not reject a different similarly named folder");
        Refuses(() => HellForgeSetup.ValidateTarget(@"\\server\share\Forge"), "setup requires a local installation rather than a network share");
        var start = HellForgeSetup.UnpackStartInfo(Path.Combine(area, "installer with spaces.exe"), Path.Combine(area, "new Forge"));
        check(!start.UseShellExecute && start.ArgumentList.SequenceEqual(new[] { "--unpack", Path.Combine(area, "new Forge") }) && !start.ArgumentList.Contains("--silent"), "setup uses only the author's supported unpack command and structured path arguments");
        check(HellForgeSetup.Installer.Url == HellForgeSetup.InstallerUrl && HellForgeSetup.Installer.Size == 543508916 && HellForgeSetup.Installer.Sha256 == "9b145622d56afe191724b5af00553f6ffe0ec641dd429fd7839cc4e0b01c677f", "author installer identity is pinned independently of an update feed");
        check(HellForgeSetup.SupportsVersion("0.5.0-beta (4a5373b)") && HellForgeSetup.SupportsVersion("0.5.0-beta") && !HellForgeSetup.SupportsVersion("0.5.0-beta2") && !HellForgeSetup.SupportsVersion("0.5.0") && !HellForgeSetup.SupportsVersion("0.4.0-beta"), "setup accepts the real commit-decorated pinned 0.5.0-beta version without accepting another release");

        var bytes = Encoding.UTF8.GetBytes("author-installer-fixture");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var server = new TestProgram.TinyHttp(bytes);
        var artifact = new PackFile { Name = "fixture.exe", Url = server.Url, Size = bytes.Length, Sha256 = hash };
        var cache = Path.Combine(area, "cache");
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "HellForge-Setup-" + hash + ".exe"), bytes[..5]);
        var cached = await HellForgeSetup.DownloadAsync(cache, null, CancellationToken.None, artifact);
        check(server.RangeRequests == 1 && File.ReadAllBytes(cached).SequenceEqual(bytes), "interrupted installer downloads resume and verify before execution");
        var owner = Path.Combine(area, "existing-owner"); Directory.CreateDirectory(owner);
        var profile = Path.Combine(owner, "progress.json"); File.WriteAllText(profile, "owner progress must survive");
        var destination = Path.Combine(area, "new Forge"); var called = 0;
        Task Unpack(string installer, string staging)
        {
            called++; check(File.ReadAllBytes(installer).SequenceEqual(bytes), "only the verified fixture installer is passed to execution");
            FakeInstall(staging); return Task.CompletedTask;
        }
        var installed = await HellForgeSetup.InstallAsync(destination, null, CancellationToken.None, cache, artifact, Unpack, _ => { });
        check(installed == Path.GetFullPath(destination) && File.Exists(Path.Combine(installed, "HellForge.exe")) && called == 1, "a validated staged installation is promoted into the chosen empty location");
        check(File.ReadAllText(profile) == "owner progress must survive", "new setup leaves an existing installation's profile bytes untouched");
        Refuses(() => HellForgeSetup.ValidateTarget(Path.Combine(destination, "nested-Forge")), "setup cannot nest a second installation inside an existing Forge copy");
        try { await HellForgeSetup.InstallAsync(destination, null, CancellationToken.None, cache, artifact, Unpack, _ => { }); check(false, "existing installation must not be replaced"); }
        catch (InvalidDataException) { check(called == 1, "repeated setup cannot replace an existing installation or execute the installer again"); }
        var corrupt = artifact.Clone(); corrupt.Sha256 = new string('a', 64);
        var badTarget = Path.Combine(area, "bad-download");
        try { await HellForgeSetup.InstallAsync(badTarget, null, CancellationToken.None, cache, corrupt, Unpack, _ => { }); check(false, "corrupt installer must fail"); }
        catch (InvalidDataException) { check(!Directory.Exists(badTarget) && called == 1, "a SHA mismatch cannot execute or create an installation"); }
        var raced = Path.Combine(area, "raced");
        try
        {
            await HellForgeSetup.InstallAsync(raced, null, CancellationToken.None, cache, artifact, (_, staging) =>
            {
                FakeInstall(staging); Directory.CreateDirectory(raced); File.WriteAllText(Path.Combine(raced, "keep.txt"), "unrelated"); return Task.CompletedTask;
            }, _ => { });
            check(false, "a changed target must fail promotion");
        }
        catch (IOException) { check(File.ReadAllText(Path.Combine(raced, "keep.txt")) == "unrelated" && !File.Exists(Path.Combine(raced, "HellForge.exe")), "a target changed during unpack is retained without replacing its files"); }
        var failed = Path.Combine(area, "failed-unpack"); string? retained = null;
        try { await HellForgeSetup.InstallAsync(failed, null, CancellationToken.None, cache, artifact, (_, staging) => { retained = staging; FakeInstall(staging, "wrong-build"); return Task.CompletedTask; }, _ => { }); check(false, "wrong payload must fail"); }
        catch (IOException) { check(!Directory.Exists(failed) && Directory.Exists(retained) && File.ReadAllText(profile) == "owner progress must survive", "a failed or wrong-build unpack stays outside the final installation and preserves existing progress"); }
        var oldCheck = Game.IsRunningCheck;
        try
        {
            var calls = 0; Game.IsRunningCheck = () => ++calls > 1;
            var gameTarget = Path.Combine(area, "game-started");
            try { await HellForgeSetup.InstallAsync(gameTarget, null, CancellationToken.None, cache, artifact, Unpack, _ => { }); check(false, "a new game must stop setup"); }
            catch (InvalidOperationException) { check(!Directory.Exists(gameTarget) && called == 1, "a game starting after download prevents unpack and preserves the cached installer"); }
            Game.IsRunningCheck = () => true;
            try { await HellForgeSetup.InstallAsync(Path.Combine(area, "already-running"), null, CancellationToken.None, cache, artifact, Unpack, _ => { }); check(false, "an active game must stop setup"); }
            catch (InvalidOperationException) { check(called == 1, "active games block installation without repair or shutdown"); }
        }
        finally { Game.IsRunningCheck = oldCheck; }
        await UpgradeFixtures(area, cache, artifact, check);
    }

    static async Task UpgradeFixtures(string area, string cache, PackFile artifact, Action<bool, string> check)
    {
        foreach (var prevVer in new[] { "0.4.0-beta", "0.3.0-beta" })
        foreach (var scenario in new[] { "success", "failure", "unknown", "active" })
        {
            var tag = (prevVer.StartsWith("0.3.", StringComparison.Ordinal) ? "030-" : "040-") + scenario;
            var root = Path.Combine(area, "upgrade-" + tag); FakeInstall(root);
            var character = Path.Combine(root, "profiles", "default", "profile.json"); Directory.CreateDirectory(Path.GetDirectoryName(character)!);
            File.WriteAllText(character, "existing owner progress");
            var modListPath = Path.Combine(root, "mods", "hellforge-mods.json");
            var customModList = """{"format":1,"share":false,"order":["clonedivers.chaos-invasions","skyeshade.hell-forge"],"disabled":["clonedivers.clean-exit"],"packs":{"pack.6f1d2c8e-5a47-4b93-9e0a-3c7b1f4d8a21":{"Targeting Marker":{"on":false}}}}""";
            File.WriteAllText(modListPath, customModList);
            var origModListBytes = File.ReadAllBytes(modListPath);
            var version = prevVer; int calls = 0;
            string EngineFor(string v) => v == "0.3.0-beta" ? "0.1.41" : v == "0.4.0-beta" ? "0.1.47" : "0.1.58";
            int SdkFor(string v) => v == "0.3.0-beta" ? 15 : v == "0.4.0-beta" ? 16 : 20;
            Task<HellForgeStatus> Status(CancellationToken _) => Task.FromResult(new HellForgeStatus(root, version, HellForgePack.SupportedBuild, true, true, scenario == "active", true, true, null, null,
                Engine: EngineFor(version), ModSdk: SdkFor(version)));
            Task Unpack(string _, string target)
            {
                calls++; version = calls == 1 ? "0.5.0-beta" : prevVer;
                // Simulate HellForge 0.5 SetupForm.UnpackQuietly -> KeptVersions.StartModList resetting hellforge-mods.json!
                if (calls == 1) File.WriteAllText(Path.Combine(target, "mods", "hellforge-mods.json"), """{"format":1,"share":true,"order":["hellforge.editor","skyeshade.hell-forge","skyeshade.vanilla-plus"]}""");
                if (calls == 1 && scenario == "failure") throw new IOException("Installer failure fixture");
                if (calls == 1 && scenario == "unknown") throw new HellForgeCommandOutcomeUnknownException("Installer exit unconfirmed");
                return Task.CompletedTask;
            }
            try
            {
                await HellForgeSetup.UpdateAsync(root, null, CancellationToken.None, cache, artifact, artifact, Unpack, Status, _ => { });
                using var restoredMods = System.Text.Json.JsonDocument.Parse(File.ReadAllText(modListPath));
                check(scenario == "success" && calls == 1 && File.ReadAllText(character) == "existing owner progress" &&
                      !restoredMods.RootElement.GetProperty("share").GetBoolean() &&
                      restoredMods.RootElement.GetProperty("importDone").GetBoolean() &&
                      restoredMods.RootElement.GetProperty("order")[0].GetString() == "clonedivers.chaos-invasions" &&
                      restoredMods.RootElement.GetProperty("disabled")[0].GetString() == "clonedivers.clean-exit",
                    $"official {prevVer} -> 0.5.0-beta update preserves character, custom mod order/disabled/packs/share=false, and sets importDone=true");
            }
            catch (HellForgeCommandOutcomeUnknownException)
            {
                check(scenario == "unknown" && calls == 1, "unknown installer outcome keeps recovery pending without a blind rollback");
                var intentPath = Directory.EnumerateFiles(Path.Combine(cache, "updates"), "*.json").Single(file => File.ReadAllText(file).Contains("upgrade-" + tag));
                using var intent = System.Text.Json.JsonDocument.Parse(File.ReadAllText(intentPath));
                var backup = Path.Combine(intent.RootElement.GetProperty("Backup").GetString()!, "protected.json");
                var bytes = File.ReadAllBytes(backup); File.WriteAllText(backup, "damaged");
                try { await HellForgeSetup.RecoverUpgradeAsync(root, CancellationToken.None, cache, artifact, Unpack, Status, _ => { }); check(false, "changed recovery metadata must fail"); }
                catch (InvalidDataException) { check(calls == 1, "changed account backup metadata is rejected before recovery executes an installer"); }
                File.WriteAllBytes(backup, bytes);
                await HellForgeSetup.RecoverUpgradeAsync(root, CancellationToken.None, cache, artifact, Unpack, Status, _ => { });
                check(calls == 2 && version == prevVer && File.ReadAllText(character) == "existing owner progress" &&
                      File.ReadAllBytes(modListPath).SequenceEqual(origModListBytes),
                    $"fresh recovery call restores {prevVer} and byte-identical hellforge-mods.json without replaying profile data");
            }
            catch (IOException)
            {
                check(scenario == "failure" && calls == 2 && version == prevVer && File.ReadAllText(character) == "existing owner progress" &&
                      File.ReadAllBytes(modListPath).SequenceEqual(origModListBytes),
                    $"failed {prevVer} -> 0.5.0-beta installer rolls back to {prevVer} with byte-identical hellforge-mods.json and profile bytes");
            }
            catch (InvalidOperationException) { check(scenario == "active" && calls == 0, "an active runner blocks application updates before any installer executes"); }
        }

        // Verify <root>\installer\*.exe SHA-256 reuse avoids re-downloading the 0.4.0 rollback installer!
        var keptRoot = Path.Combine(area, "upgrade-kept-installer"); FakeInstall(keptRoot);
        var keptPrevBytes = Encoding.UTF8.GetBytes("kept-040-installer-in-root-installer-dir");
        var keptPrevSha = Convert.ToHexString(SHA256.HashData(keptPrevBytes)).ToLowerInvariant();
        Directory.CreateDirectory(Path.Combine(keptRoot, "installer"));
        File.WriteAllBytes(Path.Combine(keptRoot, "installer", "HellForge-Setup.exe"), keptPrevBytes);
        using (var unreachablePrevServer = new TestProgram.TinyHttp(Encoding.UTF8.GetBytes("wrong-server-bytes")))
        {
            var prevKeptArtifact = new PackFile { Name = "HellForge-Setup.exe", Url = unreachablePrevServer.Url, Size = keptPrevBytes.Length, Sha256 = keptPrevSha };
            var keptCache = Path.Combine(area, "kept-cache");
            Directory.CreateDirectory(keptCache);
            File.WriteAllBytes(HellForgeSetup.InstallerPath(keptCache, artifact), File.ReadAllBytes(HellForgeSetup.InstallerPath(cache, artifact)));
            var keptVer = "0.4.0-beta";
            await HellForgeSetup.UpdateAsync(keptRoot, null, CancellationToken.None, keptCache, artifact, prevKeptArtifact,
                (_, _) => { keptVer = "0.5.0-beta"; return Task.CompletedTask; },
                _ => Task.FromResult(new HellForgeStatus(keptRoot, keptVer, HellForgePack.SupportedBuild, true, true, false, true, true, null, null,
                    Engine: keptVer == "0.4.0-beta" ? "0.1.47" : "0.1.58", ModSdk: keptVer == "0.4.0-beta" ? 16 : 20)),
                _ => { });
            check(keptVer == "0.5.0-beta" && File.ReadAllBytes(HellForgeSetup.InstallerPath(keptCache, prevKeptArtifact)).SequenceEqual(keptPrevBytes),
                "UpdateAsync reuses SHA-verified <root>/installer/*.exe rollback binary without hitting network");
        }

        // Verify HellForgeRelease.SyncShippedBundledMods copies app/bundled-mods, cleans stale files, and leaves modder src/ copies untouched.
        var bundledRoot = Path.Combine(area, "bundled-sync-fixture"); FakeInstall(bundledRoot);
        var shippedEditor = Path.Combine(bundledRoot, "app", "bundled-mods", "hellforge-editor");
        Directory.CreateDirectory(shippedEditor);
        File.WriteAllText(Path.Combine(shippedEditor, "hellforge-mod.json"), """{"id":"hellforge.editor","version":"0.4.2"}""");
        File.WriteAllText(Path.Combine(shippedEditor, "hellforge_editor.dll"), "050-dll-bytes");
        var installedEditor = Path.Combine(bundledRoot, "mods", "hellforge-editor");
        Directory.CreateDirectory(installedEditor);
        File.WriteAllText(Path.Combine(installedEditor, "hellforge_editor.dll"), "040-dll-bytes");
        File.WriteAllText(Path.Combine(installedEditor, "obsolete.dll"), "old-shipped");
        File.WriteAllText(Path.Combine(installedEditor, "editor-settings.json"), "player-saved-editor-settings");
        File.WriteAllLines(Path.Combine(installedEditor, ".hellforge-bundled.txt"), ["hellforge_editor.dll", "obsolete.dll"]);
        var shippedVp = Path.Combine(bundledRoot, "app", "bundled-mods", "vanilla-plus");
        Directory.CreateDirectory(shippedVp);
        File.WriteAllText(Path.Combine(shippedVp, "vanilla_plus.dll"), "shipped-vp");
        var modderVp = Path.Combine(bundledRoot, "mods", "vanilla-plus");
        Directory.CreateDirectory(Path.Combine(modderVp, "src"));
        File.WriteAllText(Path.Combine(modderVp, "vanilla_plus.dll"), "modder-custom-vp");
        HellForgeRelease.SyncShippedBundledMods(bundledRoot);
        check(File.ReadAllText(Path.Combine(installedEditor, "hellforge_editor.dll")) == "050-dll-bytes" &&
              !File.Exists(Path.Combine(installedEditor, "obsolete.dll")) &&
              File.ReadAllText(Path.Combine(installedEditor, "editor-settings.json")) == "player-saved-editor-settings" &&
              File.ReadAllText(Path.Combine(modderVp, "vanilla_plus.dll")) == "modder-custom-vp",
            "SyncShippedBundledMods updates shipped bundled files, deletes stale .hellforge-bundled.txt entries, keeps player editor settings, and skips modder src/ folders");

        // The setup dialog offers Pause exactly while reports say CanCancel.
        foreach (var pause in new[] { true, false })
        {
            var root = Path.Combine(area, pause ? "upgrade-paused" : "upgrade-reported"); FakeInstall(root);
            var version = "0.4.0-beta"; int calls = 0; var reports = new List<ForgeSetupProgress>();
            using var paused = new CancellationTokenSource();
            Task<HellForgeStatus> Status(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(new HellForgeStatus(root, version, HellForgePack.SupportedBuild, true, true, false, true, true, null, null,
                Engine: version == "0.4.0-beta" ? "0.1.47" : "0.1.58", ModSdk: version == "0.4.0-beta" ? 16 : 20)); }
            var progress = new SyncProgress(p => { reports.Add(p); if (pause && p.CanCancel) paused.Cancel(); });
            try
            {
                await HellForgeSetup.UpdateAsync(root, progress, paused.Token, cache, artifact, artifact, (_, _) => { calls++; version = "0.5.0-beta"; return Task.CompletedTask; }, Status, _ => { });
                check(!pause && calls == 1 && reports.Any(r => r.CanCancel) && !reports[^1].CanCancel && reports[^1].Message.StartsWith("Upgrading", StringComparison.Ordinal),
                    "an upgrade's downloads are pausable and its installer phase is not");
            }
            catch (OperationCanceledException)
            {
                var records = Path.Combine(cache, "updates");
                check(pause && calls == 0 && (!Directory.Exists(records) || !Directory.EnumerateFiles(records, "*.json").Any(f => File.ReadAllText(f).Contains("upgrade-paused"))),
                    "pausing an upgrade download stops before any recovery record or installer run");
            }
        }
    }
    sealed class SyncProgress(Action<ForgeSetupProgress> report) : IProgress<ForgeSetupProgress> { public void Report(ForgeSetupProgress value) => report(value); }

    static void FakeInstall(string root, string build = HellForgePack.SupportedBuild)
    {
        Directory.CreateDirectory(Path.Combine(root, "app", "cli")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "hellforge-install.json"), "{\"schemaVersion\":1,\"buildId\":\"" + build + "\"}");
        File.WriteAllText(Path.Combine(root, "HellForge.exe"), "fixture"); File.WriteAllText(Path.Combine(root, "app", "cli", "HellForge.Cli.exe"), "fixture");
    }

    internal static async Task RehearseRealUpgradeAsync(string root, string cache, Action<bool, string> check)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var allowedPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(@"D:\ClaudeWork\hellforge-0.5-probe")) + Path.DirectorySeparatorChar;
        var tempPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase) &&
            !(full.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("forge-upgrade-rehearsal-", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Real upgrade rehearsal requires an isolated D:\\ClaudeWork\\hellforge-0.5-probe or temp directory.");

        var profilePath = Path.Combine(full, "profiles", "default", "profile.json");
        var snapPath = Path.Combine(full, "account-snapshot", "default", "snapshot.json");
        var settingsPath = Path.Combine(full, "launcher-settings.json");
        var modListPath = Path.Combine(full, "mods", "hellforge-mods.json");

        var origProfile = File.ReadAllBytes(profilePath);
        var origSnap = File.ReadAllBytes(snapPath);
        var origSettings = File.ReadAllBytes(settingsPath);
        var origModList = File.ReadAllBytes(modListPath);

        var client = new HellForgeClient(full) { RequireCliQuiescent = () => { } };
        var initial = await client.StatusAsync();
        check(HellForgeSetup.Is040(initial.Version) && initial.Engine == "0.1.47" && initial.ModSdk == 16,
            $"rehearsal baseline is real HellForge {initial.Version} (engine {initial.Engine} / SDK {initial.ModSdk})");

        // 1. Injected post-unpack failure after real 0.5.0 --unpack -> real 0.4.0 --unpack rollback + byte-exact restoration
        bool failOnce = true;
        bool rolledBack = false;
        try
        {
            await HellForgeSetup.UpdateAsync(
                full,
                progress: null,
                ct: CancellationToken.None,
                cache: cache,
                unpack: async (exe, target) =>
                {
                    await HellForgeSetup.UnpackAsync(exe, target);
                    if (failOnce)
                    {
                        failOnce = false;
                        throw new IOException("Injected post-unpack rehearsal failure");
                    }
                },
                status: async ct => (await new HellForgeClient(full) { RequireCliQuiescent = () => { } }.StatusAsync(ct)) with { Running = false },
                idle: _ => { });
        }
        catch (IOException ex) when (ex.Message.Contains("previous official application was restored", StringComparison.Ordinal))
        {
            rolledBack = true;
        }
        var afterRollback = await client.StatusAsync();
        check(rolledBack && HellForgeSetup.Is040(afterRollback.Version) && afterRollback.Engine == "0.1.47" && afterRollback.ModSdk == 16 &&
              File.ReadAllBytes(profilePath).SequenceEqual(origProfile) &&
              File.ReadAllBytes(snapPath).SequenceEqual(origSnap) &&
              File.ReadAllBytes(settingsPath).SequenceEqual(origSettings) &&
              File.ReadAllBytes(modListPath).SequenceEqual(origModList),
            "real 0.4.0 -> 0.5.0 post-unpack fault automatically rolls back to 0.4.0-beta with byte-identical profile, snapshot, settings, and hellforge-mods.json");

        // 2. Forward real 0.4.0-beta -> 0.5.0-beta upgrade via HellForgeSetup.UpdateAsync
        await HellForgeSetup.UpdateAsync(
            full,
            progress: null,
            ct: CancellationToken.None,
            cache: cache,
            status: async ct => (await new HellForgeClient(full) { RequireCliQuiescent = () => { } }.StatusAsync(ct)) with { Running = false },
            idle: _ => { });

        var afterUpgrade = await client.StatusAsync();
        var modsAfter = await client.ModsAsync();
        using var upgradedModList = System.Text.Json.JsonDocument.Parse(File.ReadAllText(modListPath));
        bool kept040Exists = Directory.Exists(Path.Combine(full, "app-versions")) &&
                             Directory.EnumerateDirectories(Path.Combine(full, "app-versions"), "0.4.0-beta*").Any();
        check(HellForgeRelease.Supports(afterUpgrade.Version) &&
              afterUpgrade.Engine == "0.1.58" &&
              afterUpgrade.ModSdk == 20 &&
              kept040Exists &&
              File.ReadAllBytes(profilePath).SequenceEqual(origProfile) &&
              File.ReadAllBytes(snapPath).SequenceEqual(origSnap) &&
              File.ReadAllBytes(settingsPath).SequenceEqual(origSettings) &&
              !upgradedModList.RootElement.GetProperty("share").GetBoolean() &&
              upgradedModList.RootElement.GetProperty("importDone").GetBoolean() &&
              upgradedModList.RootElement.GetProperty("order")[0].GetString() == "clonedivers.chaos-invasions" &&
              upgradedModList.RootElement.GetProperty("disabled")[0].GetString() == "clonedivers.clean-exit" &&
              modsAfter.Mods.Any(m => m.Id == "hellforge.editor") &&
              modsAfter.Mods.Any(m => m.Id == "skyeshade.hell-forge") &&
              modsAfter.Mods.Any(m => m.Id == "skyeshade.vanilla-plus"),
            $"real 0.4.0 -> 0.5.0 upgrade promotes {afterUpgrade.Version} (engine {afterUpgrade.Engine} / SDK {afterUpgrade.ModSdk}), retains app-versions/0.4.0-beta*, preserves profile/settings bytes & custom hellforge-mods.json with importDone=true, and runs 0.5 HellForge.Cli mods cleanly");
    }
}
