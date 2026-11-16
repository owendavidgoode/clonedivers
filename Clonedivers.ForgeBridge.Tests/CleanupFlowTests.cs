using System.Text.Json;
using Clonedivers.ForgeBridge;

namespace Clonedivers.ForgeBridge.Tests;

internal static class CleanupFlowTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "bridge-cleanup-flow-" + Guid.NewGuid().ToString("N"));
        var run = Path.Combine(root, "logs", "sessions", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        try
        {
            foreach (var cleanup in new[] { "\"sandboxCopyBackError\":\"fixture copy-back failed\"", "\"sdkShimRestoreFailure\":\"fixture restore failed\"", "\"sdkShimOriginalsRestored\":false" })
            {
                File.WriteAllText(Path.Combine(run, "result.json"), "{\"status\":\"observed\"," + cleanup + "}");
                var running = false; var stops = 0; var statusesAfterStop = 0;
                Task<JsonElement> Execute(IReadOnlyList<string> args, CancellationToken ct)
                {
                    object result;
                    // Reuse even the SAME run folder after failed cleanup: only cleared ownership can refuse this stop.
                    if (args[0] == "status" && stops == 1) running = ++statusesAfterStop == 2;
                    if (args[0] == "status") result = new { install = root, gameBuild = BridgeSession.Build, hellforge = "0.4.0-beta (d8a8b46)", engine = "0.1.47", modSdk = 16,
                        networkBlock = "linux-netns", ready = new { linuxNamespace = true, gameCopy = true }, settings = new { sandbox = true },
                        session = new { running, lastRun = run, lastStatus = "observed", lastFailure = (string?)null } };
                    else if (args[0] == "play") { running = true; result = new { @event = "started", run }; }
                    else { stops++; running = false; result = new { @event = "stopping" }; }
                    return Task.FromResult(JsonSerializer.SerializeToElement(result));
                }
                string Request(int id, params string[] args) => JsonSerializer.Serialize(new { id, args }) + "\n";
                var output = new StringWriter();
                await new BridgeSession(Execute).ServeAsync(new StringReader(Request(1, "play", "--profile", "default") + Request(2, "stop") + Request(3, "stop") + Request(4, "status")), output);
                var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
                var key = cleanup.Split('"')[1];
                check(!replies[1].GetProperty("ok").GetBoolean() && replies[1].GetProperty("error").GetString()!.Contains(key), "observed stop rejects retained result.json cleanup error: " + key);
                check(!replies[2].GetProperty("ok").GetBoolean() && replies[2].GetProperty("error").GetString()!.Contains("does not own the running session") && stops == 1,
                    "failed completed cleanup releases ownership even when another session reuses the same run: " + key);
                check(replies[3].GetProperty("ok").GetBoolean() && !running && stops == 1, "failed completed cleanup keeps diagnostics available without another stop at EOF: " + key);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
