using System.Text.Json;
using Clonedivers;

internal static class ForgeFlowRegressionTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        check(ForgePresentation.SyncInstructions.Contains("Leave Apply import and auto-apply off"), "Linux sync instructions warn against both official import options");
        var wasRunning = Game.IsRunningCheck;
        try
        {
            Game.IsRunningCheck = () => false;
            var connection = new Connection();
            var player = new LinuxForgePlayer(connection, new());
            await player.RefreshAsync(); await player.BeginAccountSyncAsync(); await player.RefreshAsync();
            check(player.RequestedAccountSync && ForgePresentation.Resolve(player.State, connected: true, syncing: player.RequestedAccountSync).Action == ForgeAction.Refresh,
                "a requested sync waits while no verified capture is available");
            Game.IsRunningCheck = () => true;
            connection.Account = new(true, false, "Your HD2 capture is ready."); await player.RefreshAsync();
            check(player.RequestedAccountSync && connection.Imports == 0, "a captured account waits for HD2 to close before launcher import");
            connection.Account = new(false, false, "This character already has an official Forge import. Sync a fresh HD2 capture before linking it to Clonedivers; its earned Forge progress will be kept and an old capture will not be replayed.");
            Game.IsRunningCheck = () => false; await player.RefreshAsync();
            var refusal = ForgePresentation.Resolve(player.State, connected: true, syncing: player.RequestedAccountSync);
            check(!player.RequestedAccountSync && connection.Imports == 0, "native import followed by fresh-capture refusal leaves the Linux waiting state");
            check(refusal.Action == ForgeAction.Sync && refusal.Detail == connection.Account.Message, "fresh-capture refusal is visible and offers reopening account sync");
            await player.BeginAccountSyncAsync();
            check(player.RequestedAccountSync && connection.Managers == 2 && connection.Selections == 2, "retry sync reopens the official manager with the account selected");
            await player.RefreshAsync();
            check(player.RequestedAccountSync && connection.Imports == 0, "idle refresh between retry and capture keeps the pending request despite the old refusal");
            Game.IsRunningCheck = () => true; await player.RefreshAsync(); Game.IsRunningCheck = () => false;
            connection.Account = new(true, false, "Fresh capture ready."); await player.RefreshAsync();
            check(!player.RequestedAccountSync && player.State?.Account?.Imported == true && connection.Imports == 1,
                "fresh capture after refusal completes exactly one launcher import and leaves waiting");
            await player.RefreshAsync();
            check(connection.Imports == 1, "idle refresh does not repeat an already completed requested import");

            await player.LaunchAsync(); connection.FailCleanup = true;
            var failed = false;
            try { await player.EndSessionAsync(); } catch (ForgeCommandException) { failed = true; }
            check(failed && player.Error?.Contains("sandboxCopyBackError") == true, "End session retains the bridge cleanup failure");
            check(!player.OwnsSession && !player.SessionActive && !player.CanLaunch, "failed cleanup after stopped game releases the close guard without enabling play");
            await player.RefreshAsync();
            var failure = ForgePresentation.Resolve(player.State, connected: true, canLaunch: player.CanLaunch);
            check(failure.Headline == "Last session needs attention" && failure.Detail == connection.LastFailure && failure.Action == ForgeAction.Launch,
                "idle refresh keeps last failure visible while allowing retry when Forge reports ready");
            var unready = ForgePresentation.Resolve(player.State! with { Status = player.State!.Status with { GameCopyReady = false } }, connected: true);
            check(unready.Headline == "Finish Forge setup" && unready.Action == ForgeAction.Manager,
                "an old cleanup failure keeps unfinished game setup actionable");
            connection.LastFailure = null; connection.LastNotice = "The last exit matches HellForge's documented quit-time crash."; await player.RefreshAsync();
            var notice = ForgePresentation.Resolve(player.State, connected: true, canLaunch: player.CanLaunch);
            check(notice.Action == ForgeAction.Launch && notice.Detail == connection.LastNotice, "last session notice remains visible with the ready Launch action");
            await player.DisposeAsync();
        }
        finally { Game.IsRunningCheck = wasRunning; }
    }

    sealed class Connection : IForgeConnection
    {
        internal ForgeAccountView Account = new(false, false, "Sync your account.");
        internal bool Running, FailCleanup;
        internal int Imports, Managers, Selections;
        internal string? LastFailure, LastNotice;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            object result = new { fixture = true };
            switch (args[0])
            {
                case "player-status":
                    result = new ForgePlayerState(new("/fixture", "0.4.0-beta", "25480438", true, true, Running, true, true, "observed", LastFailure, LastNotice),
                        Account.Imported && !Running, "Fixture ready", Account, null); break;
                case "select-account": Selections++; break;
                case "open-manager": Managers++; break;
                case "account-import": Imports++; Account = new(true, true, "Imported"); break;
                case "play": Running = true; result = new { @event = "started" }; break;
                case "stop":
                    Running = false;
                    if (FailCleanup) { LastFailure = "sandboxCopyBackError: fixture copy-back failed"; throw new ForgeCommandException(LastFailure); }
                    result = new { @event = "ended" }; break;
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
