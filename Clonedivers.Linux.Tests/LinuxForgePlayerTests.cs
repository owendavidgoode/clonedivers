using System.Security.Cryptography;
using System.Text.Json;
using Clonedivers;

internal static class LinuxForgePlayerTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        const string hello = "{\"bridge\":\"clonedivers-forge\",\"format\":1}\n";
        var publishedEmpire = new LinuxForgePreferences { Mode = "empire" };
        check(publishedEmpire.PackSelection().Version == "2026.11.09-r47" && publishedEmpire.PackSelection().Files.Count == 96,
            "Linux shipping Empire retains the 96-row Full r47 catalog");
        foreach (var texture in new[] { "full", "lighter" })
        foreach (var droids in new[] { true, false })
        {
            if (texture == "full" && droids) continue;
            var empire = new LinuxForgePreferences { Mode = "empire", Texture = texture, Droids = droids, Covenant = true };
            var saved = Path.Combine(root, $"empire-unavailable-{texture}-{droids}.json"); empire.Save(saved);
            var savedText = File.ReadAllText(saved); var restored = LinuxForgePreferences.Load(saved);
            check(restored.SelectionProblem != null && restored.Texture == texture && restored.Droids == droids && restored.Covenant && File.ReadAllText(saved) == savedText,
                "Linux preserves unavailable saved Empire preferences without a conversion");
            var refusedConnection = new Connection(); var refusedPlayer = new LinuxForgePlayer(refusedConnection, restored);
            bool refused = false;
            try { await refusedPlayer.LaunchAsync(); } catch (InvalidOperationException) { refused = true; }
            check(refused && refusedConnection.Calls.Count == 0, "Linux rejects unavailable Empire launch before any bridge command");
            restored.Mode = "clonedivers";
            check(restored.SelectionProblem == null && restored.PackSelection().Texture == texture && restored.PackSelection().Options["droids"] == droids,
                "returning to Clones preserves its saved texture and droid selection");
        }
        var protocol = new ForgeProtocol(new StringReader(hello + "{\"id\":7,\"ok\":true,\"result\":{}}\n"), new StringWriter(), _ => { });
        await protocol.ConnectAsync(CancellationToken.None);
        try { await protocol.SendAsync(["status"]); } catch (InvalidDataException) { }
        check(protocol.Faulted, "an unknown bridge outcome is visible to the frontend as a lost connection");

        var preferences = new LinuxForgePreferences { InstallRoot = root };
        var connection = new Connection();
        var player = new LinuxForgePlayer(connection, preferences);
        await player.ApplySelectionAsync(); await player.LaunchAsync();
        check(player.OwnsSession && player.SessionActive, "fixture session is owned before the bridge fails");
        connection.Faulted = true; connection.FailDispose = true;
        check(player.Disconnected && !player.SessionActive && !player.CanLaunch, "a faulted bridge no longer pins the window open with a session it cannot end");
        await player.DisposeAsync();
        check(connection.Disposed && connection.Calls.All(c => c[0] != "stop"), "closing after a bridge fault hands cleanup to the bridge's EOF path without a doomed stop");

        connection = new Connection(); player = new LinuxForgePlayer(connection, preferences);
        await player.ApplySelectionAsync();
        preferences.Mode = "regular"; connection.RefuseSelect = true;
        try { await player.ApplySelectionAsync(); throw new Exception("FAIL: refused selection was accepted"); } catch (ForgeCommandException) { }
        await player.LaunchAsync();
        var play = connection.Calls.FindLastIndex(c => c[0] == "play"); var select = connection.Calls.FindLastIndex(c => c[0] == "select-mode");
        check(connection.Refused.Count == 1 && connection.Refused[0][2] == "regular" && play > select && select >= 0 && connection.Calls[select][2] == "regular","launch re-sends the shown selection so a refused select-mode never plays the stale mode");
        preferences.Mode = "clonedivers";

        connection = new Connection(); player = new LinuxForgePlayer(connection, preferences);
        await player.ImportAsync();
        check(player.State?.Account is { Imported: true, Message: "Opened 3 owned Warbonds." } && player.Error == null,
            "Linux import refresh displays the bridge's owned-Warbond status without losing imported-account readiness");
        connection.Account = new(true, true, "Opened 1 owned Warbonds.");
        await player.RefreshAsync();
        check(player.State?.Account.Message == "Opened 1 owned Warbonds.", "Linux idle refresh carries the owned-Warbond status into the displayed account message");
        await player.LaunchAsync();
        var imports = connection.Calls.Count(c => c[0] == "account-import");
        try { await player.ImportAsync(); throw new Exception("FAIL: active session accepted account import"); } catch (InvalidOperationException) { }
        check(connection.Calls.Count(c => c[0] == "account-import") == imports, "Linux active sessions defer imports and their owned-Warbond opening before reaching the bridge");

        var bytes = "fixture pack bytes"u8.ToArray(); var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var source = Path.Combine(root, "player-source.bin"); File.WriteAllBytes(source, bytes);
        HellForgePack.Selection Selection(string texture) => new("clonedivers", texture, "1.0", HellForgePack.SupportedBuild, new string('a', 64),
            new() { ["droids"] = true }, [new PackFile { Name = "0123456789abcdef.patch_0", Size = bytes.Length, Sha256 = sha }]);
        var exports = Path.Combine(root, "player-exports"); var partial = Path.Combine(exports, "linux-partial"); Directory.CreateDirectory(partial);
        var full = Path.Combine(exports, "linux-full");
        await HellForgePack.ExportAsync(new HellForgePack.Plan(Selection("full"), full, new() { [sha] = source }), false);
        var reused = await LinuxForgePlayer.FindExportAsync([partial, full], Selection("full"));
        check(reused?.Output == full && reused.Missing.Count == 0, "a verified earlier export is reused instead of writing another full pack copy");
        check(await LinuxForgePlayer.FindExportAsync([partial, full], Selection("lighter")) == null, "a different selection never reuses a mismatched export");
        var start = DateTime.UtcNow.AddHours(-5);
        for (var i = 0; i < 4; i++) { var old = Path.Combine(exports, "linux-old" + i); Directory.CreateDirectory(old); Directory.SetLastWriteTimeUtc(old, start.AddHours(i)); }
        Directory.SetLastWriteTimeUtc(partial, start.AddHours(-1)); Directory.SetLastWriteTimeUtc(full, start.AddHours(-2));
        var windows = Path.Combine(exports, "clonedivers-windows"); Directory.CreateDirectory(windows); Directory.SetLastWriteTimeUtc(windows, start.AddHours(-3));
        LinuxForgePlayer.PruneExports(exports, full);
        check(Directory.Exists(full) && Directory.Exists(windows) && Directory.Exists(Path.Combine(exports, "linux-old3")) && Directory.Exists(Path.Combine(exports, "linux-old2")) &&
            Directory.EnumerateDirectories(exports, "linux-*").Count() == 3, "an applied import prunes older Linux exports to the imported copy and two recent sources");
    }

    sealed class Connection : IForgeConnection
    {
        internal bool RefuseSelect, FailDispose, Running, Disposed;
        internal ForgeAccountView? Account;
        public bool Faulted { get; set; }
        internal readonly List<string[]> Calls = [], Refused = [];
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (Faulted) throw new InvalidOperationException("Forge bridge is not connected; review cleanup before reconnecting.");
            // Refused calls never reach Calls, so Calls holds only what the bridge accepted.
            if (args[0] == "select-mode" && RefuseSelect) { RefuseSelect = false; Refused.Add(args.ToArray()); throw new ForgeCommandException("Finish the running session before changing or launching Forge."); }
            Calls.Add(args.ToArray());
            object result = args[0] switch {
                "player-status" => new ForgePlayerState(new(Path.GetTempPath(), "fixture", "25480438", !Running, true, Running, true, true, "observed", null), !Running, "Fixture ready", Account, null),
                "play" => new { @event = "started" },
                _ => new { fixture = true }
            };
            if (args[0] == "play") Running = true;
            if (args[0] == "account-import") Account = new(true, true, "Opened 3 owned Warbonds.");
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync() { Disposed = true; return FailDispose ? ValueTask.FromException(new IOException("Official Linux wrapper ended abnormally")) : ValueTask.CompletedTask; }
    }
}
