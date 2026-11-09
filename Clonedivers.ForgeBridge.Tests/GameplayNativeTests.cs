using System.Text.Json;
using Clonedivers;

namespace Clonedivers.ForgeBridge.Tests;

internal static class GameplayNativeTests
{
    internal static async Task Run(string fixture, Action<bool, string> check)
    {
        var previous = Game.IsRunningCheck; Game.IsRunningCheck = () => false;
        try
        {
            var root = Path.Combine(fixture, "gameplay-native", "pfx", "drive_c", "Forge Folder");
            var connection = new Connection(root);
            var player = new LinuxForgePlayer(connection, new() { InstallRoot = root });
            await player.InstallGameplayAsync();
            check(connection.Imported == "C:\\Clonedivers-HellForge-cache\\gameplay\\" + HellForgeGameplayCatalog.Pinned.Id, "native gameplay stages outside the namespace and sends only the bounded prefix C: cache path to the bridge");
            var badRoot = Path.Combine(fixture, "gameplay-rejected", "pfx", "drive_c", "Forge");
            connection = new Connection(badRoot) { Missing = [HellForgeGameplayCatalog.Pinned.Artifacts[0] with { Url = "https://untrusted.invalid/fixture.zip" }] };
            player = new(connection, new() { InstallRoot = badRoot });
            try { await player.InstallGameplayAsync(); throw new Exception("Unpinned download request was accepted."); }
            catch (InvalidDataException) { check(connection.Imported == null && !Directory.Exists(Path.GetDirectoryName(badRoot)), "a bridge cannot make the native frontend download an unpinned artifact or create staging first"); }
            connection = new(root) { Running = true }; player = new(connection, new() { InstallRoot = root }); await player.RefreshAsync();
            try { await player.InstallGameplayAsync(); throw new Exception("Active session accepted gameplay changes."); }
            catch (InvalidOperationException) { check(connection.Imported == null, "native gameplay setup refuses an active session before staging or importing"); }
            BridgeSession.Validate(["gameplay-status"]); BridgeSession.Validate(["recover-gameplay"]);
            var staged = OperatingSystem.IsLinux() ? Path.Combine(fixture, "native-stage", HellForgeGameplayCatalog.Pinned.Id) : "C:\\Clonedivers-HellForge-cache\\gameplay\\" + HellForgeGameplayCatalog.Pinned.Id;
            BridgeSession.Validate(["install-gameplay", "--file", staged]);
            check(true, "bridge accepts the complete-preset protocol without exposing executable or account controls");
            foreach (var path in new[] { "Z:\\tmp\\mods", "C:\\cache\\..\\profiles" })
            {
                try { BridgeSession.Validate(["install-gameplay", "--file", path]); throw new Exception("Unsafe gameplay import path accepted."); }
                catch (ArgumentException) { check(true, "bridge rejects unsafe gameplay staging: " + path); }
            }
        }
        finally { Game.IsRunningCheck = previous; }
    }

    sealed class Connection(string root) : IForgeConnection
    {
        internal List<GameplayArtifact> Missing = [];
        internal string? Imported;
        internal bool Running;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            object reply = args[0] switch
            {
                "gameplay-status" => Missing,
                "install-gameplay" => Install(args[2]),
                "player-status" => new ForgePlayerState(new(root, "0.4.0-beta", HellForgePack.SupportedBuild, true, true, Running, true, true, null, null), false, "Fixture requires own account", null, null),
                _ => throw new InvalidOperationException("Unexpected fixture command: " + args[0])
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(reply, HellForgePack.Json));
        }
        object Install(string path) { Imported = path; return new { installed = true }; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
