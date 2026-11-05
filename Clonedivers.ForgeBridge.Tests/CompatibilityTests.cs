using System.Text.Json;
using System.Text.Json.Nodes;
using Clonedivers;

namespace Clonedivers.ForgeBridge.Tests;

internal static class CompatibilityTests
{
    static JsonObject Status(string root) => JsonSerializer.SerializeToNode(new {
        install = root, hellforge = "0.3.0-beta (fixture)", engine = "0.1.41", modSdk = 15, gameBuild = HellForgePack.SupportedBuild,
        networkBlock = "wfp", ready = new { gameCopy = true, sessionRunner = true, linuxNamespace = false },
        settings = new { sandbox = true, multiplayer = true, localPlayFab = true },
        session = new { running = false, lastStatus = (string?)null, lastFailure = (string?)null }
    })!.AsObject();
    static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "clonedivers-compatibility-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "installation");
        check(HellForgeClient.PinnedProblem(Element(Status(install))) == null, "the pinned Windows HF/engine/SDK/WFP/sandbox status is accepted");
        foreach (var name in new[] { "engine", "sdk", "sdk-string", "sdk-overflow", "hf", "missing-engine", "missing-sdk", "sandbox-off", "missing-sandbox", "no-isolation", "linux-marker-in-windows" })
        {
            var status = Status(install);
            switch (name)
            {
                case "engine": status["engine"] = "0.1.42"; break;
                case "sdk": status["modSdk"] = 16; break;
                case "sdk-string": status["modSdk"] = "15"; break;
                case "sdk-overflow": status["modSdk"] = long.MaxValue; break;
                case "hf": status["hellforge"] = "0.4.0"; break;
                case "missing-engine": status.Remove("engine"); break;
                case "missing-sdk": status.Remove("modSdk"); break;
                case "sandbox-off": status["settings"]!["sandbox"] = false; break;
                case "missing-sandbox": status["settings"]!.AsObject().Remove("sandbox"); break;
                case "no-isolation": status["networkBlock"] = "none"; break;
                case "linux-marker-in-windows": status["networkBlock"] = "linux-netns"; break;
            }
            check(HellForgeClient.PinnedProblem(Element(status)) != null, "changed or unverifiable pin fails closed: " + name);
        }
        var linux = Status(install); linux["networkBlock"] = "linux-netns"; linux["ready"]!["linuxNamespace"] = true;
        check(HellForgeClient.PinnedProblem(Element(linux), linuxNamespace: true) == null, "the namespace bridge requires the actual Linux isolation status fields");
        linux["ready"]!["linuxNamespace"] = false;
        check(HellForgeClient.PinnedProblem(Element(linux), linuxNamespace: true) != null && HellForgeClient.PinnedProblem(Element(Status(install)), linuxNamespace: true) != null,
            "the Linux route cannot accept an inactive namespace or a Windows WFP result");
        Directory.CreateDirectory(root);
        try
        {
            var commands = new Commands(install); var client = new HellForgeClient(install, commands);
            check((await client.StatusAsync()).CompatibilityProblem != null, "incompatibility remains a read-only diagnostic instead of hiding session status");
            foreach (var action in new Func<Task>[] { () => client.PrepareRegularAsync(), () => client.SelectAccountAsync(), () => client.PlayRegularAsync(), () => client.RecoverInterruptedPackAsync() })
            {
                bool refused = false;
                try { await action(); } catch (InvalidDataException) { refused = true; }
                check(refused && commands.Calls.All(c => c == "status"), "an incompatible idle runtime cannot receive a prepare/account/launch/recovery mutation");
            }
            commands.Running = true;
            check((await client.StatusAsync()) is { Running: true, GameCopyReady: false, CompatibilityProblem: not null }, "an incompatible active session preserves its staged gameCopy=false status");
            await client.EndSessionAsync();
            check(!commands.Running && commands.Calls.Count(c => c == "stop") == 1 && commands.Calls.All(c => c is "status" or "stop"),
                "End session still completes official cleanup when engine compatibility changes");
        }
        finally
        {
            var full = Path.GetFullPath(root);
            if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("clonedivers-compatibility-", StringComparison.Ordinal)) throw new IOException("Invalid fixture cleanup path.");
            Directory.Delete(full, true);
        }
    }
    sealed class Commands(string root) : IHellForgeCommands
    {
        internal bool Running;
        internal List<string> Calls = [];
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Calls.Add(args[0]);
            if (args[0] == "stop") { Running = false; return Task.FromResult(JsonSerializer.SerializeToElement(new { @event = "stopping" })); }
            if (args[0] != "status") throw new Exception("Unexpected mutation in compatibility fixture: " + args[0]);
            var status = Status(root); status["engine"] = "0.1.42"; status["session"]!["running"] = Running; status["ready"]!["gameCopy"] = !Running;
            return Task.FromResult(Element(status));
        }
    }
}
