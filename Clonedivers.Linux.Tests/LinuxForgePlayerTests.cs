using System.Security.Cryptography;
using System.Text.Json;
using Clonedivers;

internal static class LinuxForgePlayerTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        const string hello = "{\"bridge\":\"clonedivers-forge\",\"format\":1}\n";
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
        check(play > select && select >= 0 && connection.Calls[select][2] == "regular", "launch re-sends the shown selection so a refused select-mode never plays the stale mode");
        preferences.Mode = "clonedivers";

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
        public bool Faulted { get; set; }
        internal readonly List<string[]> Calls = [];
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (Faulted) throw new InvalidOperationException("Forge bridge is not connected; review cleanup before reconnecting.");
            Calls.Add(args.ToArray());
            if (args[0] == "select-mode" && RefuseSelect) { RefuseSelect = false; throw new ForgeCommandException("Finish the running session before changing or launching Forge."); }
            object result = args[0] switch {
                "player-status" => new ForgePlayerState(new(Path.GetTempPath(), "fixture", "25480438", !Running, true, Running, true, true, "observed", null), !Running, "Fixture ready", null, null),
                "play" => new { @event = "started" },
                _ => new { fixture = true }
            };
            if (args[0] == "play") Running = true;
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync() { Disposed = true; return FailDispose ? ValueTask.FromException(new IOException("Official Linux wrapper ended abnormally")) : ValueTask.CompletedTask; }
    }
}
