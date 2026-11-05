using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection;
using Clonedivers;

namespace Clonedivers.ForgeBridge.Tests;

internal static class ImportTests
{
    internal static async Task CrashChildAsync(string root)
    {
        ValidateFixtureRoot(root);
        await using var fixture = new Fixture("hard-exit", root);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "child.json")));
        fixture.Selection = doc.RootElement.GetProperty("selection").Deserialize<HellForgePack.Selection>(HellForgePack.Json)!;
        fixture.Mods = doc.RootElement.GetProperty("mods").EnumerateArray().Select(m => new Mod(m.GetProperty("id").GetString()!, m.GetProperty("name").GetString()!,
            m.GetProperty("enabled").GetBoolean(), m.GetProperty("folder").GetString()!)).ToList();
        await new HellForgeClient(fixture.Install, fixture) { RequireCliQuiescent = () => { } }.InstallPackAsync(fixture.NewFolder, fixture.Selection);
        throw new Exception("Hard-exit fixture unexpectedly returned.");
    }
    static void ValidateFixtureRoot(string root)
    {
        var full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith("clonedivers-import-tx-", StringComparison.Ordinal)) throw new IOException("Fixture escaped its temporary root.");
    }
    internal static async Task RecoveryAsync(Action<bool, string> check)
    {
        await using var fixture = await Fixture.CreateAsync("hard-exit");
        var original = fixture.State(); var profile = File.ReadAllBytes(fixture.Profile); var privacy = File.ReadAllBytes(fixture.Privacy);
        File.WriteAllText(Path.Combine(fixture.Root, "child.json"), JsonSerializer.Serialize(new { selection = fixture.Selection,
            mods = fixture.Mods.Select(m => new { id = m.Id, name = m.Name, enabled = m.Enabled, folder = m.Folder }) }, HellForgePack.Json));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--hard-exit-import-fixture"); start.ArgumentList.Add(fixture.Root);
        using (var child = Process.Start(start)!)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await child.WaitForExitAsync(limit.Token); }
            finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
            check(child.ExitCode == 73, "a separate fixture frontend exits abruptly after the old appearance is deleted");
        }
        fixture.Mods.RemoveAll(m => m.Id == HellForgePack.PackId("clonedivers"));
        fixture.mode = "recovery";
        var client = new HellForgeClient(fixture.Install, fixture) { RequireCliQuiescent = () => { } };
        check(client.InterruptedPackProblem()?.Contains("interrupted") == true && (await client.StatusAsync()).RecoveryProblem != null,
            "a fresh client finds the durable pending record after process death");
        async Task Refuses(Func<Task> action, string name)
        {
            int mutations = fixture.AddCalls;
            try { await action(); throw new Exception("FAIL: expected recovery refusal: " + name); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException)
            { check(fixture.AddCalls == mutations, name); }
        }
        await Refuses(() => client.InstallPackAsync(fixture.NewFolder, fixture.Selection), "pending recovery blocks replacement after launcher restart");
        await Refuses(() => client.PrepareRegularAsync(), "pending recovery blocks Regular-mode mutation");
        await Refuses(() => client.PlayRegularAsync(), "pending recovery blocks a new launch");
        await Refuses(() => client.SelectAccountAsync(), "pending recovery cannot change the active account");
        check(new HellForgeClient(Path.Combine(fixture.Root, "sibling"), fixture).InterruptedPackProblem() == null, "sibling installations have distinct durable operation records");
        if (OperatingSystem.IsWindows()) check(new HellForgeClient(fixture.Install.ToUpperInvariant(), fixture).InterruptedPackProblem()?.Contains("interrupted") == true,
            "Windows path-case variants identify the same interrupted installation");
        fixture.Running = true;
        await Refuses(() => client.RecoverInterruptedPackAsync(), "recovery defers for an active session without issuing stop or repair");
        fixture.Running = false;
        client.RequireCliQuiescent = () => throw new InvalidOperationException("fixture orphan CLI");
        await Refuses(() => client.RecoverInterruptedPackAsync(), "recovery refuses to run over an outstanding CLI");
        client.RequireCliQuiescent = () => { };
        var intentText = File.ReadAllText(client.PackIntentPath);
        using var journal = JsonDocument.Parse(intentText);
        var backup = journal.RootElement.GetProperty("Backup").GetString()!;
        var before = Path.Combine(backup, "before.json"); var savedBefore = File.ReadAllBytes(before);
        File.AppendAllText(before, " ");
        await Refuses(() => client.RecoverInterruptedPackAsync(), "recovery rejects changed before-snapshot metadata");
        File.WriteAllBytes(before, savedBefore);
        var payload = Path.Combine(backup, "previous-pack", "a.patch_0"); var savedPayload = File.ReadAllBytes(payload); File.WriteAllText(payload, "tampered");
        await Refuses(() => client.RecoverInterruptedPackAsync(), "recovery validates saved appearance bytes before importing");
        File.WriteAllBytes(payload, savedPayload);
        File.WriteAllText(client.PackIntentPath, intentText.Replace(fixture.Install.Replace("\\", "\\\\"), (fixture.Install + "-foreign").Replace("\\", "\\\\")));
        check(client.InterruptedPackProblem() != null, "a foreign-root journal fails closed");
        await Refuses(() => client.RecoverInterruptedPackAsync(), "foreign-root recovery cannot import any files");
        File.WriteAllText(client.PackIntentPath, "{broken");
        check(client.InterruptedPackProblem() != null, "a malformed journal produces an actionable launch block");
        File.WriteAllText(client.PackIntentPath, intentText);
        fixture.Mods.Single(m => m.Id == "keep.native").Enabled = false;
        await Refuses(() => client.RecoverInterruptedPackAsync(), "recovery refuses changed unrelated gameplay choices before importing");
        fixture.Mods.Single(m => m.Id == "keep.native").Enabled = true;
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Refuses(() => client.RecoverInterruptedPackAsync(cancellation.Token), "cancelled recovery leaves the pending record and imported bytes untouched");
        }
        using (var held = new FileStream(client.PackIntentPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Refuses(() => client.RecoverInterruptedPackAsync(), "a concurrent recovery/appearance operation is serialized across clients");
        await client.RecoverInterruptedPackAsync();
        check(fixture.State() == original && client.InterruptedPackProblem() == null, "verified official-CLI recovery restores prior bytes, order, flags and backend choices after process death");
        check(new HellForgeClient(fixture.Install, fixture).InterruptedPackProblem() == null, "a completed recovery remains complete in the next client instance");
        check(File.ReadAllBytes(fixture.Profile).SequenceEqual(profile) && File.ReadAllBytes(fixture.Privacy).SequenceEqual(privacy) &&
            fixture.Calls.All(a => a[0] is not ("stop" or "profiles" or "play")), "crash recovery preserves account/privacy and never stops or launches a game");
        BridgeSession.Validate(["recover-pack"]);
        check(ForgeProtocol.ResultBudget("recover-pack") >= TimeSpan.FromMinutes(10), "the namespace recovery command has an adequate authoritative result budget");
    }
    internal static async Task RegularAsync(Action<bool, string> check)
    {
        foreach (var mode in new[] { "regular-success", "regular-switch-fails", "settings-fail", "regular-unknown" })
        {
            await using var fixture = await Fixture.CreateAsync(mode);
            var original = fixture.State(); var profile = File.ReadAllBytes(fixture.Profile); var privacy = File.ReadAllBytes(fixture.Privacy);
            var client = new HellForgeClient(fixture.Install, fixture) { RequireCliQuiescent = () => { } };
            Exception? failure = null;
            try { await client.PrepareRegularAsync(); } catch (Exception error) { failure = error; }
            if (mode == "regular-success")
                check(failure == null && client.InterruptedPackProblem() == null && fixture.Mods.Where(m => HellForgePack.IsOurPack(m.Id)).All(m => !m.Enabled) &&
                    fixture.Mods.Single(m => m.Id == HellForgeClient.DifficultyId).Enabled, "successful Regular preparation commits its durable operation");
            else if (mode == "regular-unknown")
            {
                check(failure is HellForgeCommandOutcomeUnknownException && client.InterruptedPackProblem() != null, "an unknown Regular switch keeps the durable record pending without guessing recovery");
                fixture.mode = "recovery";
                await new HellForgeClient(fixture.Install, fixture) { RequireCliQuiescent = () => { } }.RecoverInterruptedPackAsync();
                check(fixture.State() == original && client.InterruptedPackProblem() == null, "a new client can officially restore an interrupted Regular selection without appearance-file import");
            }
            else check(failure != null && fixture.State() == original && client.InterruptedPackProblem() == null, "caught Regular failure restores prior flags/order/backend choices: " + mode);
            check(File.ReadAllBytes(fixture.Profile).SequenceEqual(profile) && File.ReadAllBytes(fixture.Privacy).SequenceEqual(privacy) && fixture.AddCalls == 0,
                "Regular transaction preserves account, privacy and existing appearance files: " + mode);
        }
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var mode in new[] { "success", "copy-fails", "cancel-after-delete", "settings-fail", "fresh-settings-fail", "unknown", "active", "restore-fails", "restore-corrupt", "tampered-backup", "nested-backup", "low-space", "concurrent-choice" })
        {
            await using var fixture = await Fixture.CreateAsync(mode);
            var original = fixture.State();
            var profile = File.ReadAllBytes(fixture.Profile);
            var privacy = File.ReadAllBytes(fixture.Privacy);
            // This command fixture owns no real CLI. Keep host processes and
            // parallel fixture suites out of its transaction preconditions,
            // as in the Regular/recovery cases below. RecoveryAsync separately
            // exercises refusal of an outstanding CLI.
            var client = new HellForgeClient(fixture.Install, fixture) { RequireCliQuiescent = () => { } };
            if (mode == "low-space") client.AvailablePackBytes = _ => 0;
            if (mode == "concurrent-choice")
            {
                bool changed = false;
                client.AvailablePackBytes = _ => { if (!changed) { changed = true; fixture.Mods.Single(m => m.Id == "keep.native").Enabled = false; } return long.MaxValue; };
            }
            Exception? failure = null;
            try { await client.InstallPackAsync(fixture.NewFolder, fixture.Selection, fixture.Cancel.Token); }
            catch (Exception error) { failure = error; }
            if (mode == "success")
            {
                if (failure != null) throw new Exception("Successful appearance fixture failed before verification.", failure);
                var selected = fixture.Mods.Single(m => m.Id == HellForgePack.PackId("clonedivers"));
                check(failure == null && fixture.AddCalls == 1 && selected.Enabled && !fixture.Mods.Single(m => m.Id == HellForgePack.PackId("empire")).Enabled &&
                    fixture.Mods.Single(m => m.Id == HellForgeClient.DifficultyId).Enabled && fixture.Multiplayer && fixture.Backend &&
                    File.ReadAllText(Path.Combine(selected.Folder, "a.patch_0")) == "new appearance", "a successful replacement verifies the new pack and enables the selected appearance without a restore");
                check(File.ReadAllBytes(fixture.Profile).SequenceEqual(profile) && File.ReadAllBytes(fixture.Privacy).SequenceEqual(privacy), "successful appearance replacement preserves account and privacy sentinels");
                continue;
            }
            check(failure != null, "simulated import fault is reported: " + mode);
            if (mode is "copy-fails" or "cancel-after-delete" or "settings-fail" or "fresh-settings-fail")
            {
                check(fixture.State() == original && failure!.Message.Contains("restored"), "failed replacement restores prior files, flags, order and backend choices through CLI: " + mode);
                check(fixture.Calls.All(a => a[0] != "profiles" && !a.Contains("--profile")), "appearance recovery never resets or selects an account/profile: " + mode);
            }
            else if (mode == "unknown")
                check(failure is HellForgeCommandOutcomeUnknownException && fixture.AddCalls == 1, "an unknown CLI outcome preserves backup and never retries import/restore");
            else if (mode == "active")
                check(fixture.Running && fixture.AddCalls == 1 && fixture.Calls.All(a => a[0] != "stop"), "a newly active session defers recovery without stopping it or changing more mods");
            else if (mode == "restore-fails")
                check(fixture.AddCalls == 2 && failure!.Message.Contains("recovery needs attention"), "a failed restore retains explicit recovery evidence without a retry loop");
            else if (mode == "restore-corrupt")
                check(fixture.AddCalls == 2 && failure!.Message.Contains("differs from its saved files"), "a restore acknowledgement cannot certify changed installed bytes");
            else if (mode == "tampered-backup")
                check(fixture.AddCalls == 1 && failure!.Message.Contains("backup changed"), "changed backup bytes cannot be silently reimported");
            else if (mode == "nested-backup")
                check(fixture.AddCalls == 1 && failure!.Message.Contains("restorable cosmetic"), "an unexpected backup directory is refused without traversing or importing it");
            else if (mode == "low-space")
                check(fixture.AddCalls == 0 && fixture.State() == original, "backup-plus-replacement capacity refusal precedes any mod mutation");
            else if (mode == "concurrent-choice")
                check(fixture.AddCalls == 0 && !fixture.Mods.Single(m => m.Id == "keep.native").Enabled, "a concurrent native choice is retained and aborts the appearance transaction before import");
            check(File.ReadAllBytes(fixture.Profile).SequenceEqual(profile) && File.ReadAllBytes(fixture.Privacy).SequenceEqual(privacy), "profile and privacy sentinels remain byte-identical: " + mode);
            if (mode is not ("low-space" or "concurrent-choice"))
                check(Directory.EnumerateFiles(fixture.Cache, "before.json", SearchOption.AllDirectories).Any(), "pre-change evidence retained outside installation: " + mode);
        }
    }

    sealed class Mod(string id, string name, bool enabled, string folder)
    {
        internal string Id = id, Name = name, Folder = folder;
        internal bool Enabled = enabled;
    }
    sealed class Fixture : IHellForgeCommands, IAsyncDisposable
    {
        internal string mode;
        internal readonly string Root, Install, NewFolder, Profile, Privacy, Cache;
        internal HellForgePack.Selection Selection = null!;
        internal List<Mod> Mods = [];
        internal readonly List<string[]> Calls = [];
        internal readonly CancellationTokenSource Cancel = new();
        internal int AddCalls;
        internal bool Running, Multiplayer, Backend;
        bool settingsFailed;
        internal Fixture(string mode, string? root = null)
        {
            this.mode = mode;
            Root = root ?? Path.Combine(Path.GetTempPath(), "clonedivers-import-tx-" + Guid.NewGuid().ToString("N"));
            Install = Path.Combine(Root, "installation"); NewFolder = Path.Combine(Root, "new-pack");
            Cache = Path.Combine(Root, "Clonedivers-HellForge-cache");
            Profile = Path.Combine(Install, "profiles", "default", "profile.json"); Privacy = Path.Combine(Install, "mods", "hellforge-mods.json");
        }
        internal static async Task<Fixture> CreateAsync(string mode)
        {
            var fixture = new Fixture(mode);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.Profile)!); Directory.CreateDirectory(Path.GetDirectoryName(fixture.Privacy)!);
            File.WriteAllText(fixture.Profile, "profile sentinel; not a real imported account");
            File.WriteAllText(fixture.Privacy, "{\"share\":false,\"playWithStrangers\":false,\"fixture\":\"preserve\"}");
            async Task<HellForgePack.Selection> Pack(string path, string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var source = Path.Combine(fixture.Root, Guid.NewGuid().ToString("N")); File.WriteAllBytes(source, bytes);
                var selection = new HellForgePack.Selection("clonedivers", "full", text, HellForgePack.SupportedBuild, new string('a', 64), [],
                    [new PackFile { Name = "a.patch_0", Size = bytes.Length, Sha256 = hash }]);
                await HellForgePack.ExportAsync(new(selection, path, new() { [hash] = source }), false);
                return selection;
            }
            var oldSource = Path.Combine(fixture.Root, "old-source"); await Pack(oldSource, "old appearance");
            fixture.Selection = await Pack(fixture.NewFolder, "new appearance");
            if (mode != "fresh-settings-fail") fixture.InstallFolder(oldSource, false);
            fixture.Mods.Add(new(HellForgePack.PackId("empire"), "Prior Empire", true, Path.Combine(fixture.Install, "mods", "empire")));
            fixture.Mods.Add(new(HellForgeClient.DifficultyId, "Difficulty", false, Path.Combine(fixture.Install, "mods", "d11")));
            fixture.Mods.Add(new("keep.native", "Unrelated native", true, Path.Combine(fixture.Install, "mods", "native")));
            return fixture;
        }
        void DeleteOwnedFolder(string path)
        {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(Path.Combine(Install, "mods") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture deletion outside intended mods root.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
        void InstallFolder(string source, bool defaultEnabled)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")));
            var id = "pack." + doc.RootElement.GetProperty("Guid").GetString();
            var previous = Mods.SingleOrDefault(m => m.Id == id); var on = previous?.Enabled ?? defaultEnabled;
            if (previous != null) { DeleteOwnedFolder(previous.Folder); Mods.Remove(previous); }
            var target = Path.Combine(Install, "mods", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            Mods.Add(new(id, doc.RootElement.GetProperty("Name").GetString()!, on, target));
        }
        internal string State() => JsonSerializer.Serialize(new {
            mods = Mods.Select(m => new { m.Id, m.Name, m.Enabled, data = Directory.Exists(m.Folder) ? Directory.GetFiles(m.Folder).OrderBy(f => f).Select(f => new { name = Path.GetFileName(f), hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) }).ToArray() : null }).ToArray(), Multiplayer, Backend
        });
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Calls.Add(args.ToArray()); object result;
            switch (args[0])
            {
                case "status": result = new { install = Install, hellforge = "0.3.0-beta (fixture)", engine = "0.1.41", modSdk = 15, networkBlock = "wfp", gameBuild = HellForgePack.SupportedBuild,
                    ready = new { gameCopy = !Running, sessionRunner = true, linuxNamespace = false }, session = new { running = Running, lastStatus = (string?)null, lastFailure = (string?)null }, settings = new { multiplayer = Multiplayer, localPlayFab = Backend, sandbox = true } }; break;
                case "mods": result = new { mods = Mods.Select(m => new { id = m.Id, name = m.Name, enabled = m.Enabled, folder = m.Folder, version = "", tags = new[] { "fixture" } }), problems = Array.Empty<string>(), modlist = "fixture" }; break;
                case "mod-add":
                    if (Running) throw new InvalidOperationException("Fixture session active");
                    AddCalls++;
                    if (AddCalls == 1 && mode == "hard-exit")
                    {
                        var prior = Mods.Single(m => m.Id == HellForgePack.PackId("clonedivers")); DeleteOwnedFolder(prior.Folder); Mods.Remove(prior);
                        Environment.Exit(73); // skips all client catch/finally paths, only this disposable fixture process
                    }
                    if (AddCalls == 1 && mode is "copy-fails" or "cancel-after-delete" or "unknown" or "active" or "restore-fails" or "restore-corrupt" or "tampered-backup" or "nested-backup")
                    {
                        var prior = Mods.Single(m => m.Id == HellForgePack.PackId("clonedivers")); DeleteOwnedFolder(prior.Folder); Mods.Remove(prior);
                        if (mode == "unknown") throw new HellForgeCommandOutcomeUnknownException("fixture unknown");
                        if (mode == "active") Running = true;
                        if (mode == "cancel-after-delete") { Cancel.Cancel(); throw new OperationCanceledException(ct); }
                        if (mode == "tampered-backup") { var saved = Directory.EnumerateFiles(Cache, "a.patch_0", SearchOption.AllDirectories).Single(); File.WriteAllText(saved, "changed backup"); }
                        if (mode == "nested-backup") { var saved = Directory.EnumerateFiles(Cache, "a.patch_0", SearchOption.AllDirectories).Single(); Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(saved)!, "unexpected")); }
                        throw new IOException("fixture replacement failed after old deletion");
                    }
                    if (AddCalls > 1 && mode == "restore-fails") throw new IOException("fixture recovery copy failed");
                    InstallFolder(Path.GetDirectoryName(args[2])!, true);
                    if (AddCalls > 1 && mode == "restore-corrupt") File.WriteAllText(Path.Combine(Mods.Single(m => m.Id == HellForgePack.PackId("clonedivers")).Folder, "a.patch_0"), "bad appearance");
                    result = new { id = HellForgePack.PackId("clonedivers") }; break;
                case "mod-remove": var removed = Mods.Single(m => m.Id == args[2]); DeleteOwnedFolder(removed.Folder); Mods.Remove(removed); result = new { ok = true }; break;
                case "mod-switch":
                    Mods.Single(m => m.Id == args[2]).Enabled = args[4] == "yes";
                    if (!settingsFailed && args[2] == HellForgePack.PackId("empire") && mode is "regular-switch-fails" or "regular-unknown")
                    {
                        settingsFailed = true;
                        if (mode == "regular-unknown") throw new HellForgeCommandOutcomeUnknownException("fixture Regular CLI unknown");
                        throw new IOException("fixture switch failed after change");
                    }
                    result = new { ok = true }; break;
                case "mod-order": var order = args[2].Split(','); Mods = order.Select(id => Mods.Single(m => m.Id == id)).ToList(); result = new { ok = true }; break;
                case "settings": Multiplayer = args[2] == "yes"; Backend = args[4] == "yes";
                    if (!settingsFailed && mode is "settings-fail" or "fresh-settings-fail") { settingsFailed = true; throw new IOException("fixture settings failed after write"); }
                    result = new { ok = true }; break;
                default: throw new Exception("Unexpected mutation/command: " + args[0]);
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync()
        {
            Cancel.Dispose(); var full = Path.GetFullPath(Root);
            if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("clonedivers-import-tx-", StringComparison.Ordinal)) throw new IOException("Fixture cleanup escaped its temporary root.");
            Directory.Delete(full, true); return ValueTask.CompletedTask;
        }
    }
}
