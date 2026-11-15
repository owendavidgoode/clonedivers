using System.Security.Cryptography;
using System.Text.Json;
using Clonedivers;

internal static class ForgePolishTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var operations = new ForgeOperations();
        await operations.RunAsync(() => throw new IOException("unseen operation error"), () => { });
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var poll = operations.RefreshAsync(() => pending.Task, () => { });
        check(operations.Refreshing && !operations.Busy && operations.Error == "unseen operation error", "a pending background poll neither makes the UI busy nor clears an unseen error");
        var polls = 0;
        await operations.RefreshAsync(() => { polls++; return Task.CompletedTask; }, () => { });
        check(polls == 0, "timer ticks do not overlap an in-flight poll");
        pending.SetResult(); await poll;
        check(!operations.Refreshing && !operations.Busy && operations.Error == "unseen operation error", "successful background refresh preserves the foreground failure");
        await operations.RefreshAsync(() => throw new IOException("later refresh error"), () => { });
        check(operations.Error == "unseen operation error", "a background failure does not replace an unseen operation failure");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        poll = operations.RefreshAsync(() => pending.Task, () => { });
        var clicked = false;
        var click = operations.RunAsync(() => { clicked = true; return Task.CompletedTask; }, () => { });
        check(operations.Busy && !clicked, "a click is accepted during a poll and waits before issuing foreground commands");
        pending.SetResult(); await Task.WhenAll(poll, click);
        check(clicked && !operations.Busy && operations.Error == null, "an explicit successful retry clears the error after the poll completes");

        var original = Game.IsRunningCheck;
        try
        {
            Game.IsRunningCheck = () => false;
            var oldConnection = new Connection(); var old = new LinuxForgePlayer(oldConnection, new());
            await old.RefreshAsync(); await old.BeginAccountSyncAsync(); oldConnection.Faulted = true;
            var request = old.PendingAccountSync; await old.DisposeAsync();
            var connection = new Connection(); var player = new LinuxForgePlayer(connection, new());
            player.ResumeAccountSync(request); await player.RefreshAsync();
            check(old.Disconnected && player.RequestedAccountSync && connection.Imports == 0, "pending capture request survives a bridge replacement and its first stale-refusal refresh");
            connection.Account = new(false, false, "Please sync a fresh HD2 capture before continuing"); await player.RefreshAsync();
            check(player.RequestedAccountSync, "refusal wording changes alone cannot cancel a request before any observed capture attempt");
            Game.IsRunningCheck = () => true; await player.RefreshAsync(); Game.IsRunningCheck = () => false;
            await player.RefreshAsync();
            check(!player.RequestedAccountSync && connection.Imports == 0, "a fresh refusal after an observed capture attempt exits waiting");
            await player.BeginAccountSyncAsync();
            connection.Running = true; await player.RefreshAsync(); connection.Running = false;
            request = player.PendingAccountSync;
            connection = new() { Account = new(true, false, "Fresh capture ready") };
            player = new(connection, new()); player.ResumeAccountSync(request); await player.RefreshAsync(); await player.RefreshAsync();
            check(!player.RequestedAccountSync && connection.Imports == 1, "a capture completed during reconnect imports exactly once without another click");
            connection.Fail = true;
            try { await player.RefreshAsync(); } catch (IOException) { }
            connection.Fail = false; await player.RefreshAsync(preserveError: true);
            check(player.Error == "unseen player error" && !player.CanLaunch, "background player refresh retains its error and launch guard");
            await player.RefreshAsync(); check(player.Error == null, "explicit player refresh acknowledges and clears its previous error");
        }
        finally { Game.IsRunningCheck = original; }

        var status = new HellForgeStatus("/fixture", "0.4.0-beta", "25480438", true, true, false, true, true, null, "old crash", "old quit notice");
        var state = new ForgePlayerState(status, false, "", new(true, true, "Imported"), null);
        var prepare = ForgePresentation.Resolve(state, connected: true);
        check(prepare.Action == ForgeAction.Prepare && prepare.Detail == "Install the verified appearance and required gameplay mods before playing.", "session failure and quit notice leave mode preparation and its instruction intact");
        check(ForgePresentation.Resolve(state with { Status = status with { RecoveryProblem = "interrupted" } }, connected: true).Button == "Recover setup", "old session failure cannot hide interrupted-change recovery");
        check(ForgePresentation.Resolve(state with { Account = new(false, false, "Sync a fresh HD2 capture") }, connected: true).Action == ForgeAction.Sync, "old session failure cannot hide fresh account sync");
        check(ForgePresentation.Resolve(state, connected: true, syncing: true).Headline == "Finish account sync", "old session failure cannot hide requested sync progress");
        check(ForgePresentation.Resolve(state with { Status = status with { LastFailure = null } }, connected: true).Detail == prepare.Detail, "quit notice is confined to Ready and diagnostics");
        check(ForgeProtocol.ResultBudget("play") == TimeSpan.FromMinutes(10) && ForgeProtocol.ResultBudget("play") > ForgeProtocol.ResultBudget("status"), "first launch receives a longer authoritative reply budget than status");
        var missing = LinuxForgePlayer.MissingToolsMessage(["pasta"]);
        check(missing.Contains("Ubuntu 24.04 or newer") && missing.Contains("Debian 13 or newer") && missing.Contains("both solo and co-op") && !missing.Contains("unaffected"), "missing-passt guidance names supported distribution versions and the launcher's actual solo restriction");
        check(!LinuxForgePlayer.MissingToolsMessage(["ip"]).Contains("Ubuntu"), "other missing-tool errors do not misdiagnose passt");
        Links(root, check);
    }

    static void Links(string root, Action<bool, string> check)
    {
        var area = Path.Combine(root, "polish-links");
        var prefix = Path.Combine(area, "real", "prefix"); var install = Path.Combine(prefix, "pfx", "drive_c", "Forge");
        foreach (var relative in new[] { "hellforge-install.json", "app/app-build.json", "app/cli-linux/HellForge.Cli", "app/backend-linux/HellForge.LocalBackend", "app/launcher-linux/HellForge.Launcher", "app/linux/hellforge-linux.sh", "app/linux/hf-sandbox" })
        { var file = Path.Combine(install, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "fixture"); }
        File.WriteAllText(Path.Combine(install, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");
        File.WriteAllText(Path.Combine(install, "app", "app-build.json"), """{"version":"0.4.0-beta","engine":"0.1.47"}""");
        var bridge = Path.Combine(area, "bridge"); File.WriteAllText(bridge, "fixture, never executed");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bridge)));
        try
        {
            var alias = Path.Combine(area, "linked-home"); Directory.CreateSymbolicLink(alias, Path.Combine(area, "real"));
            var moved = Path.Combine(area, "moved-prefix"); Directory.CreateSymbolicLink(moved, prefix);
            var plan = LinuxForgePlan.StageNative(install, bridge, hash);
            check(LinuxForgePlan.StageNative(Path.Combine(alias, "prefix", "pfx", "drive_c", "Forge"), bridge, hash) == plan, "native staging resolves a symlink above the prefix to the physical install and cache");
            check(LinuxForgePlan.StageNative(Path.Combine(moved, "pfx", "drive_c", "Forge"), bridge, hash) == plan, "a moved prefix alias resolves to the same verified native plan");
            var linkedInstall = Path.Combine(prefix, "pfx", "drive_c", "LinkedForge"); Directory.CreateSymbolicLink(linkedInstall, install);
            var refused = false;
            try { LinuxForgePlan.StageNative(linkedInstall, bridge, hash); } catch (InvalidDataException) { refused = true; }
            check(refused, "links within the prefix still cannot redirect installation or cache checks");
            var dangling = Path.Combine(area, "dangling"); Directory.CreateSymbolicLink(dangling, Path.Combine(area, "missing"));
            refused = false;
            try { LinuxForgePlan.StageNative(Path.Combine(dangling, "pfx", "drive_c", "Forge"), bridge, hash); } catch (InvalidDataException) { refused = true; }
            check(refused, "dangling prefix aliases fail before staging");
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: prefix symlink fixtures require Windows privileges; Linux CI must run these cases."); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314) { Console.WriteLine("SKIP: prefix symlink fixtures require Windows privileges; Linux CI must run these cases."); }
    }

    sealed class Connection : IForgeConnection
    {
        internal ForgeAccountView Account = new(false, false, "Sync a fresh HD2 capture");
        internal int Imports;
        internal bool Fail, Running;
        public bool Faulted { get; set; }
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (Fail) throw new IOException("unseen player error");
            if (args[0] == "account-import") { Imports++; Account = new(true, true, "Imported"); }
            object result = args[0] == "player-status"
                ? new ForgePlayerState(new("/fixture", "0.4.0-beta", "25480438", true, true, Running, true, true, null, null), Account.Imported && !Running, "", Account, null)
                : new { fixture = true };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
