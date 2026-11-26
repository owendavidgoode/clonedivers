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

        var bgOps = new ForgeOperations();
        await bgOps.RefreshAsync(() => throw new IOException("transient poll error 1"), () => { });
        check(bgOps.Error == "transient poll error 1" && bgOps.FailureDetail != null, "a failed background poll exposes its error");
        await bgOps.RefreshAsync(() => throw new IOException("transient poll error 2"), () => { });
        check(bgOps.Error == "transient poll error 2", "a subsequent failed background poll updates the background error");
        await bgOps.RefreshAsync(() => Task.CompletedTask, () => { });
        check(bgOps.Error == null && bgOps.FailureDetail == null, "a succeeding background poll clears an error that came from a background poll");

        var original = Game.IsRunningCheck;
        try
        {
            Game.IsRunningCheck = () => false;
            var handoff = new ForgeSyncHandoff();
            var oldConnection = new Connection(); var old = new LinuxForgePlayer(oldConnection, new());
            await old.RefreshAsync(); await old.BeginAccountSyncAsync(); oldConnection.Faulted = true;
            await handoff.ReleaseAsync(old);
            var connectFailed = false;
            try { await handoff.ConnectAsync(() => Task.FromException<LinuxForgePlayer>(new IOException("reconnect failed")), _ => { }, CancellationToken.None); }
            catch (IOException) { connectFailed = true; }
            check(connectFailed && handoff.Pending != null, "failed reconnect preserves pending account sync for the next retry");
            var connection = new Connection();
            var player = await handoff.ConnectAsync(() => Task.FromResult(new LinuxForgePlayer(connection, new())), _ => { }, CancellationToken.None);
            check(old.Disconnected && player.RequestedAccountSync && handoff.Pending == null && connection.Imports == 0,
                "pending capture request survives a bridge replacement via ForgeWindow's handoff helper and its first stale-refusal refresh");
            connection.Account = new(false, false, "Please sync a fresh HD2 capture before continuing"); await player.RefreshAsync();
            check(player.RequestedAccountSync, "refusal wording changes alone cannot cancel a request before any observed capture attempt");
            Game.IsRunningCheck = () => true; await player.RefreshAsync(); Game.IsRunningCheck = () => false;
            await player.RefreshAsync();
            check(!player.RequestedAccountSync && connection.Imports == 0, "a fresh refusal after an observed capture attempt exits waiting");
            await player.BeginAccountSyncAsync();
            connection.Running = true; await player.RefreshAsync(); connection.Running = false;
            await handoff.ReleaseAsync(player);
            connection = new() { Account = new(true, false, "Fresh capture ready") };
            player = await handoff.ConnectAsync(() => Task.FromResult(new LinuxForgePlayer(connection, new())), _ => { }, CancellationToken.None);
            await player.RefreshAsync();
            check(!player.RequestedAccountSync && connection.Imports == 1, "a capture completed during reconnect imports exactly once without another click");
            await player.BeginAccountSyncAsync();
            await handoff.ReleaseAsync(player);
            connection = new() { Account = new(true, false, "Fresh capture ready") };
            player = await handoff.ConnectAsync(() => Task.FromResult(new LinuxForgePlayer(connection, new())), _ => { }, CancellationToken.None);
            check(!player.RequestedAccountSync && connection.Imports == 1, "pending capture request also survives a setup release and auto-imports on reconnect");

            connection.Fail = true;
            try { await player.RefreshAsync(preserveError: true, allowImport: false); } catch (IOException) { }
            check(player.Error == "unseen player error" && !player.CanLaunch, "a failed background player poll records its error and blocks Launch");
            connection.Fail = false; await player.RefreshAsync(preserveError: true, allowImport: false);
            check(player.Error == null && player.CanLaunch, "a succeeding background player poll clears its own error and restores CanLaunch");

            connection.Fail = true;
            try { await player.RefreshAsync(); } catch (IOException) { }
            connection.Fail = false; await player.RefreshAsync(preserveError: true, allowImport: false);
            check(player.Error == "unseen player error" && !player.CanLaunch, "background player refresh retains its error and launch guard");
            connection.Fail = true;
            try { await player.RefreshAsync(preserveError: true, allowImport: false); } catch (IOException) { }
            connection.Fail = false; await player.RefreshAsync(preserveError: true, allowImport: false);
            check(player.Error == "unseen player error" && !player.CanLaunch, "transient background poll failures and recovery never clear a foreground error");
            await player.RefreshAsync(); check(player.Error == null && player.CanLaunch, "explicit player refresh acknowledges and clears its previous error");

            var escOps = new ForgeOperations(); Task foregroundOp = Task.CompletedTask;
            void BeginEsc(Func<Task> action) { if (!escOps.Busy) foregroundOp = escOps.RunAsync(action, () => { }); }
            var autoConn = new Connection(); var autoPlayer = new LinuxForgePlayer(autoConn, new());
            await autoPlayer.RefreshAsync(); await autoPlayer.BeginAccountSyncAsync();
            autoConn.Account = new(true, false, "Fresh capture ready");
            autoConn.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            autoConn.ImportGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pollTask = escOps.PollAsync(
                () => autoPlayer.RefreshAsync(preserveError: true, allowImport: false),
                () => ForgeWindowFlow.NeedsForegroundRefresh(false, autoPlayer),
                BeginEsc,
                () => autoPlayer.RefreshAsync(),
                () => { });
            check(escOps.Refreshing && !escOps.Busy && autoConn.Imports == 0, "background poll stays strictly read-only and non-busy while checking capture readiness");
            autoConn.StatusGate.SetResult(); autoConn.StatusGate = null;
            await pollTask;
            check(escOps.Busy && autoConn.Imports == 1 && !autoPlayer.RequestedAccountSync, "poll escalates due captured auto-import to foreground Begin so Busy=true while importing");
            autoConn.ImportGate.SetResult(); autoConn.ImportGate = null;
            await foregroundOp;
            check(!escOps.Busy && autoPlayer.CanLaunch && autoConn.Imports == 1, "escalated foreground auto-import completes and restores idle readiness");

            var setupConn = new Connection { GameCopyReady = false, Account = new(true, true, "Imported") };
            var setupPlayer = new LinuxForgePlayer(setupConn, new());
            var requestedSetup = true; var prepareCalls = 0;
            var prepareStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var prepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task RunSetupRefresh(bool background)
            {
                await setupPlayer.RefreshAsync(preserveError: background, allowImport: !background);
                if (!background && ForgeWindowFlow.ShouldPrepareAfterSetup(requestedSetup, setupPlayer))
                { requestedSetup = false; prepareCalls++; prepareStarted.TrySetResult(); await prepareGate.Task; }
            }
            await escOps.PollAsync(() => RunSetupRefresh(true), () => ForgeWindowFlow.NeedsForegroundRefresh(requestedSetup, setupPlayer), BeginEsc, () => RunSetupRefresh(false), () => { });
            check(prepareCalls == 0 && requestedSetup && !escOps.Busy, "background poll does not escalate setup PrepareAsync before GameCopyReady");
            setupConn.GameCopyReady = true;
            setupConn.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pollTask = escOps.PollAsync(() => RunSetupRefresh(true), () => ForgeWindowFlow.NeedsForegroundRefresh(requestedSetup, setupPlayer), BeginEsc, () => RunSetupRefresh(false), () => { });
            check(escOps.Refreshing && !escOps.Busy && prepareCalls == 0, "background poll stays read-only and skips PrepareAsync while Refreshing");
            setupConn.StatusGate.SetResult(); setupConn.StatusGate = null;
            await pollTask; await prepareStarted.Task;
            check(escOps.Busy && prepareCalls == 1 && !requestedSetup, "poll dispatches foreground Begin for due requestedSetup so Busy=true while PrepareAsync runs");
            prepareGate.SetResult(); await foregroundOp;
            check(!escOps.Busy && prepareCalls == 1, "escalated foreground PrepareAsync clears Busy when finished");

            requestedSetup = true; var beginCount = 0; var userClickRan = false;
            setupConn.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pollTask = escOps.PollAsync(() => RunSetupRefresh(true), () => ForgeWindowFlow.NeedsForegroundRefresh(requestedSetup, setupPlayer), a => { beginCount++; BeginEsc(a); }, () => RunSetupRefresh(false), () => { });
            BeginEsc(() => { userClickRan = true; return Task.CompletedTask; });
            setupConn.StatusGate.SetResult(); setupConn.StatusGate = null;
            await Task.WhenAll(pollTask, foregroundOp);
            check(userClickRan && beginCount == 0 && prepareCalls == 1, "a foreground click during a poll takes priority and skips duplicate poll escalation");
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
        var nonApt = LinuxForgeBootstrap.NonAptToolsMessage;
        var installFailedPasta = LinuxForgeBootstrap.InstallToolsFailureMessage(["pasta"]);
        var installFailedOther = LinuxForgeBootstrap.InstallToolsFailureMessage([]);
        check(nonApt.Contains("Ubuntu 24.04 or newer") && nonApt.Contains("Debian 13 or newer") && nonApt.Contains("both solo and co-op"), "non-apt bootstrap error includes distribution and solo-plus-co-op pasta guidance");
        check(installFailedPasta == missing && installFailedOther.Contains("both solo and co-op") && installFailedOther.Contains("Ubuntu 24.04 or newer"),
            "bootstrap tool installation failure uses missing-tools pasta/distribution guidance and never says only co-op needs pasta");
        check(ForgePresentation.FailureSummary(installFailedPasta).Contains("Ubuntu 24.04+ / Debian 13+") && ForgePresentation.FailureSummary(nonApt).Contains("both solo and co-op"),
            "failure summary surfaces actionable pasta/distribution guidance on the main card");
        check(ForgePresentation.FailureSummary("UnauthorizedAccessException: Access to '/home/pasta/forge' is denied").Contains("folder permissions"),
            "permission errors still map to folder permissions even if a path mentions pasta");

        // QA Bugfix #2 & #4 + Version 0.10.0 checks on Linux
        check(AppInfo.Version == (ForgeEntryPolicy.ForgeEdition ? "0.10.5" : "0.5.0"),
            "Linux Forge assembly version is bumped to 0.10.5 (0.5.0 when ForgeEdition=false)");
        using (var linuxIco = typeof(LinuxForgePlayer).Assembly.GetManifestResourceStream("Clonedivers.Icon.ico"))
        {
            check(linuxIco != null, "Clonedivers.Icon.ico is embedded in Clonedivers.Linux assembly");
            using var ms = new MemoryStream();
            linuxIco!.CopyTo(ms);
            check(Clonedivers.Ui.ForgeTheme.TryExtractLargestPngFromIco(ms.ToArray(), out var png) && png.Length > 4000,
                "Linux assembly extracts embedded 256x256 PNG icon slice for X11/Wayland docks");
        }

        // Deterministic OperationProgress on LinuxForgePlayer.PlanAppearanceAsync & PrepareAppearanceAsync
        var progArea = Path.Combine(root, "linux-det-progress");
        var progInstall = Path.Combine(progArea, "pfx", "drive_c", "Forge");
        Directory.CreateDirectory(Path.Combine(progInstall, "mods"));
        var progConn = new Connection { Account = new(true, true, "Imported") };
        var progPrefs = new LinuxForgePreferences { InstallRoot = progInstall, Mode = "clonedivers", Texture = "reduced512" };
        var progPlayer = new LinuxForgePlayer(progConn, progPrefs, progInstall);
        var richEvents = new List<OperationProgress>();
        var wasRun2 = Game.IsRunningCheck;
        try
        {
            Game.IsRunningCheck = () => false;
            await progPlayer.InstallGameplayAsync(null, CancellationToken.None, new SyncProgress<OperationProgress>(richEvents.Add));
            var detPlan = await progPlayer.PlanAppearanceAsync(null, CancellationToken.None, new SyncProgress<OperationProgress>(richEvents.Add));
            check(detPlan.Selection.Files.Count > 0 &&
                  richEvents.Any(e => e.Stage == "Checking required gameplay mods") &&
                  richEvents.Any(e => e.Stage == "Verifying cached files" && !e.IsIndeterminate && e.ItemCount > 0 && e.BytesTotal > 0 && e.CanCancel),
                "LinuxForgePlayer emits deterministic OperationProgress (files, bytes, CanCancel) during gameplay & pack planning");
        }
        finally { Game.IsRunningCheck = wasRun2; }

        await Links(root, check);
    }

    static async Task Links(string root, Action<bool, string> check)
    {
        var area = Path.Combine(root, "polish-links");
        var prefix = Path.Combine(area, "real", "prefix"); var install = Path.Combine(prefix, "pfx", "drive_c", "Forge");
        foreach (var relative in new[] { "hellforge-install.json", "app/app-build.json", "app/cli-linux/HellForge.Cli", "app/backend-linux/HellForge.LocalBackend", "app/launcher-linux/HellForge.Launcher", "app/linux/hellforge-linux.sh", "app/linux/hf-sandbox" })
        { var file = Path.Combine(install, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "fixture"); }
        File.WriteAllText(Path.Combine(install, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");
        File.WriteAllText(Path.Combine(install, "app", "app-build.json"), """{"version":"0.4.0-beta","engine":"0.1.47"}""");
        var bridge = Path.Combine(area, "bridge"); File.WriteAllText(bridge, "fixture, never executed");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bridge)));

        var aliasUncond = Path.Combine(area, "alias-prefix", "pfx", "drive_c", "Forge");
        var uncondPrefs = new LinuxForgePreferences { InstallRoot = aliasUncond, Mode = "regular" };
        var uncondConn = new Connection();
        var uncondPlayer = new LinuxForgePlayer(uncondConn, uncondPrefs, install);
        var savedAliasFile = Path.Combine(area, "forge-alias.json");
        uncondPrefs.Save(savedAliasFile);
        check(uncondPrefs.InstallRoot == aliasUncond && uncondPlayer.ResolvedInstallRoot == install && LinuxForgePreferences.Load(savedAliasFile).InstallRoot == aliasUncond,
            "LinuxForgePlayer stores ResolvedInstallRoot separately without overwriting the user's saved InstallRoot alias");
        var wasRunning = Game.IsRunningCheck;
        try
        {
            Game.IsRunningCheck = () => false;
            await uncondPlayer.InstallGameplayAsync();
        }
        finally { Game.IsRunningCheck = wasRunning; }
        check(uncondConn.LastGameplayPath == "C:\\Clonedivers-HellForge-cache\\gameplay\\" + HellForgeGameplayCatalog.Pinned.Id && uncondPrefs.InstallRoot == aliasUncond,
            "InstallGameplayAsync computes prefix WindowsPath from ResolvedInstallRoot while keeping the InstallRoot alias intact");
        var uncondExport = Path.Combine(prefix, "pfx", "drive_c", "Clonedivers-HellForge-cache", "exports", "test");
        check(LinuxForgePlayer.WindowsPath(uncondPlayer.ResolvedInstallRoot!, uncondExport) == "C:\\Clonedivers-HellForge-cache\\exports\\test",
            "WindowsPath uses the resolved install root for cache paths");

        try
        {
            var alias = Path.Combine(area, "linked-home"); Directory.CreateSymbolicLink(alias, Path.Combine(area, "real"));
            var moved = Path.Combine(area, "moved-prefix"); Directory.CreateSymbolicLink(moved, prefix);
            var plan = LinuxForgePlan.StageNative(install, bridge, hash);
            var aliasedInstall = Path.Combine(alias, "prefix", "pfx", "drive_c", "Forge");
            var aliasedPlan = LinuxForgePlan.StageNative(aliasedInstall, bridge, hash);
            check(aliasedPlan == plan, "native staging resolves a symlink above the prefix to the physical install and cache");
            check(LinuxForgePlan.StageNative(Path.Combine(moved, "pfx", "drive_c", "Forge"), bridge, hash) == plan, "a moved prefix alias resolves to the same verified native plan");
            var prefs = new LinuxForgePreferences { InstallRoot = aliasedInstall, Mode = "regular" };
            var conn = new Connection();
            var linkedPlayer = await LinuxForgePlayer.ConnectWithPlanAsync(prefs, aliasedPlan, (_, _) => Task.FromResult<IForgeConnection>(conn));
            var linkedPrefsFile = Path.Combine(area, "linked-forge.json");
            prefs.Save(linkedPrefsFile);
            check(prefs.InstallRoot == aliasedInstall && linkedPlayer.ResolvedInstallRoot == plan.Install && LinuxForgePreferences.Load(linkedPrefsFile).InstallRoot == aliasedInstall,
                "connecting through a symlinked prefix alias preserves the alias in forge.json while storing ResolvedInstallRoot");
            prefs.Mode = "clonedivers";
            var packPlan = await linkedPlayer.PlanAppearanceAsync();
            check(packPlan.Output.StartsWith(Path.Combine(prefix, "pfx", "drive_c", "Clonedivers-HellForge-cache", "exports"), StringComparison.Ordinal) &&
                LinuxForgePlayer.WindowsPath(linkedPlayer.ResolvedInstallRoot!, packPlan.Output).StartsWith("C:\\Clonedivers-HellForge-cache\\exports\\", StringComparison.Ordinal),
                "appearance planning and WindowsPath use ResolvedInstallRoot across a symlinked prefix alias");
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
        internal bool Fail, Running, GameCopyReady = true;
        internal string? LastGameplayPath;
        internal TaskCompletionSource? StatusGate, ImportGate;
        public bool Faulted { get; set; }
        public async Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (Fail) throw new IOException("unseen player error");
            if (args[0] == "player-status" && StatusGate != null) await StatusGate.Task;
            if (args[0] == "install-gameplay") LastGameplayPath = args[2];
            if (args[0] == "account-import")
            {
                Imports++;
                if (ImportGate != null) await ImportGate.Task;
                Account = new(true, true, "Imported");
            }
            object result = args[0] switch
            {
                "player-status" => new ForgePlayerState(new("/fixture", "0.4.0-beta", "25480438", GameCopyReady, true, Running, true, true, null, null), Account.Imported && GameCopyReady && !Running, "", Account, null),
                "gameplay-status" => Array.Empty<GameplayArtifact>(),
                _ => new { fixture = true }
            };
            return JsonSerializer.SerializeToElement(result);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
