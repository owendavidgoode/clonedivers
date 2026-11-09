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
        check(HellForgeSetup.Installer.Url == HellForgeSetup.InstallerUrl && HellForgeSetup.Installer.Size == 526467508 && HellForgeSetup.Installer.Sha256 == "97dbdd07638e5198b98bf2ad74a1ba907c53f569fcdcdcff330e6c74359f04d7", "author installer identity is pinned independently of an update feed");
        check(HellForgeSetup.SupportsVersion("0.4.0-beta (4a5373b)") && HellForgeSetup.SupportsVersion("0.4.0-beta") && !HellForgeSetup.SupportsVersion("0.4.0-beta2") && !HellForgeSetup.SupportsVersion("0.4.0"), "setup accepts the real commit-decorated pinned version without accepting another release");

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
        foreach (var scenario in new[] { "success", "failure", "unknown", "active" })
        {
            var root = Path.Combine(area, "upgrade-" + scenario); FakeInstall(root);
            var character = Path.Combine(root, "profiles", "default", "profile.json"); Directory.CreateDirectory(Path.GetDirectoryName(character)!);
            File.WriteAllText(character, "existing owner progress");
            var version = "0.3.0-beta"; int calls = 0;
            Task<HellForgeStatus> Status(CancellationToken _) => Task.FromResult(new HellForgeStatus(root, version, HellForgePack.SupportedBuild, true, true, scenario == "active", true, true, null, null,
                Engine: version == "0.3.0-beta" ? "0.1.41" : "0.1.47", ModSdk: version == "0.3.0-beta" ? 15 : 16));
            Task Unpack(string _, string target)
            {
                calls++; version = calls == 1 ? "0.4.0-beta" : "0.3.0-beta";
                if (calls == 1 && scenario == "failure") throw new IOException("Installer failure fixture");
                if (calls == 1 && scenario == "unknown") throw new HellForgeCommandOutcomeUnknownException("Installer exit unconfirmed");
                return Task.CompletedTask;
            }
            try
            {
                await HellForgeSetup.UpdateAsync(root, null, CancellationToken.None, cache, artifact, artifact, Unpack, Status, _ => { });
                check(scenario == "success" && calls == 1 && File.ReadAllText(character) == "existing owner progress", "official application update preserves an existing character and commits only the new application version");
            }
            catch (HellForgeCommandOutcomeUnknownException)
            {
                check(scenario == "unknown" && calls == 1, "unknown installer outcome keeps recovery pending without a blind rollback");
                // Locate this installation's own durable record, not another trial's backup.
                var intentPath = Directory.EnumerateFiles(Path.Combine(cache, "updates"), "*.json").Single(file => File.ReadAllText(file).Contains("upgrade-unknown"));
                using var intent = System.Text.Json.JsonDocument.Parse(File.ReadAllText(intentPath));
                var backup = Path.Combine(intent.RootElement.GetProperty("Backup").GetString()!, "protected.json");
                var bytes = File.ReadAllBytes(backup); File.WriteAllText(backup, "damaged");
                try { await HellForgeSetup.RecoverUpgradeAsync(root, CancellationToken.None, cache, artifact, Unpack, Status, _ => { }); check(false, "changed recovery metadata must fail"); }
                catch (InvalidDataException) { check(calls == 1, "changed account backup metadata is rejected before recovery executes an installer"); }
                File.WriteAllBytes(backup, bytes);
                await HellForgeSetup.RecoverUpgradeAsync(root, CancellationToken.None, cache, artifact, Unpack, Status, _ => { });
                check(calls == 2 && version == "0.3.0-beta" && File.ReadAllText(character) == "existing owner progress", "a fresh recovery call restores the verified previous application without replaying profile data");
            }
            catch (IOException) { check(scenario == "failure" && calls == 2 && version == "0.3.0-beta" && File.ReadAllText(character) == "existing owner progress", "a failed installer invokes the retained verified previous installer and preserves profile bytes"); }
            catch (InvalidOperationException) { check(scenario == "active" && calls == 0, "an active runner blocks application updates before any installer executes"); }
        }
        // The setup dialog offers Pause exactly while reports say CanCancel.
        foreach (var pause in new[] { true, false })
        {
            var root = Path.Combine(area, pause ? "upgrade-paused" : "upgrade-reported"); FakeInstall(root);
            var version = "0.3.0-beta"; int calls = 0; var reports = new List<ForgeSetupProgress>();
            using var paused = new CancellationTokenSource();
            Task<HellForgeStatus> Status(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(new HellForgeStatus(root, version, HellForgePack.SupportedBuild, true, true, false, true, true, null, null,
                Engine: version == "0.3.0-beta" ? "0.1.41" : "0.1.47", ModSdk: version == "0.3.0-beta" ? 15 : 16)); }
            var progress = new SyncProgress(p => { reports.Add(p); if (pause && p.CanCancel) paused.Cancel(); });
            try
            {
                await HellForgeSetup.UpdateAsync(root, progress, paused.Token, cache, artifact, artifact, (_, _) => { calls++; version = "0.4.0-beta"; return Task.CompletedTask; }, Status, _ => { });
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
}
