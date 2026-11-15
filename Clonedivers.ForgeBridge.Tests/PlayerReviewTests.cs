using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clonedivers;

namespace Clonedivers.ForgeBridge.Tests;

internal static class PlayerReviewTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "bridge-player-review-" + Guid.NewGuid().ToString("N"));
        var snapshot = Path.Combine(root, "account-snapshot", "default");
        var profile = Path.Combine(root, "profiles", "default");
        Directory.CreateDirectory(snapshot); Directory.CreateDirectory(profile);
        try
        {
            File.WriteAllText(Path.Combine(snapshot, "snapshot.json"), """{"progression":true}""");
            File.WriteAllText(Path.Combine(snapshot, "real-inventory.json"), """{"items":[]}""");
            File.WriteAllText(Path.Combine(snapshot, "progression.json"), """{"experience":12345}""");
            File.WriteAllText(Path.Combine(snapshot, "seal.json"), """{"steamAccount":"1","capturedUtc":"2026-11-01T00:00:00Z"}""");
            File.WriteAllText(Path.Combine(profile, "profile.json"), """{"schemaVersion":1,"inventory":{"items":[]}}""");
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (var name in new[] { "real-inventory.json", "progression.json", "seal.json" }) hash.AppendData(SHA256.HashData(File.ReadAllBytes(Path.Combine(snapshot, name))));
                File.WriteAllText(Path.Combine(profile, "clonedivers-account-import.json"), JsonSerializer.Serialize(new {
                    policy = "one-way-v2", capture = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), importedUtc = "2026-11-01T01:00:00Z",
                    account = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Clonedivers account v1:1"))).ToLowerInvariant() }));
            }
            var backendCalls = 0; var warbondCalls = 0; var fail = false;
            Task<string> Backend(IReadOnlyList<string> args, CancellationToken ct)
            {
                backendCalls++;
                if (fail) throw new IOException("fixture backend unavailable");
                if (args[1] != "import-preview") throw new Exception("Unexpected mutation in account review");
                return Task.FromResult("""{"categories":{"level":{"vanilla":"12345 XP"}}}""");
            }
            Task<int> Warbonds(CancellationToken ct)
            {
                warbondCalls++;
                File.AppendAllText(Path.Combine(profile, "profile.json"), " "); // the real review can change this fingerprint
                return Task.FromResult(warbondCalls == 1 ? 2 : 0);
            }
            var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc); string? block = null;
            var official = new Commands(root);
            var player = new PlayerCommands(root, official, backend: Backend, reviewWarbonds: Warbonds) { UtcNow = () => now, CaptureBlockCheck = () => block };
            async Task<JsonElement> Poll() => await player.RunAsync(["player-status"], CancellationToken.None);
            var first = await Poll();
            check(first.GetProperty("account").GetProperty("Imported").GetBoolean() && backendCalls == 1 && warbondCalls == 1, "first idle status reviews the sealed account and owned Warbonds once");
            check(first.GetProperty("account").GetProperty("Message").GetString()!.Contains("Opened 2 owned Warbonds."), "new Warbond result is included in the next player account message");
            now = now.AddMinutes(5).AddTicks(-1);
            var second = await Poll(); await Poll();
            check(backendCalls == 1 && warbondCalls == 1, "unchanged account fingerprint skips both backend and Warbond reviews for under five minutes, including review's own profile change");
            check(!second.GetProperty("account").GetProperty("Message").GetString()!.Contains("Opened"), "Warbond result clears after its first returned message even with a cached account");
            now = now.AddTicks(1); await Poll();
            check(backendCalls == 2 && warbondCalls == 2, "account and Warbond reviews run again at the five-minute boundary");
            File.AppendAllText(Path.Combine(profile, "profile.json"), " "); await Poll();
            check(backendCalls == 3 && warbondCalls == 3, "a changed profile fingerprint triggers immediate account and Warbond review");
            block = "Capture cleanup pending";
            var blocked = await Poll();
            check(!blocked.GetProperty("account").GetProperty("Imported").GetBoolean() && backendCalls == 3, "capture block overrides a cached imported account without invoking the backend");
            block = null; await Poll();
            check(backendCalls == 4 && warbondCalls == 4, "capture cleanup completion invalidates the previous review even with unchanged files");
            fail = true; now = now.AddMinutes(5);
            var failure = await Poll(); await Poll();
            check(backendCalls == 6 && warbondCalls == 4 && !failure.GetProperty("account").GetProperty("Imported").GetBoolean(), "backend review failures are visible and retried rather than cached as a successful import");
            official.Running = true; official.Calls = 0; await Poll();
            check(official.Calls == 1 && backendCalls == 6 && warbondCalls == 4, "active sessions poll only official status without account or Warbond work");
            official.Running = false; fail = false;
            File.AppendAllText(Path.Combine(snapshot, "progression.json"), " ");
            var fresh = await Poll();
            check(backendCalls == 7 && warbondCalls == 4 && fresh.GetProperty("account").GetProperty("Captured").GetBoolean() && !fresh.GetProperty("account").GetProperty("Imported").GetBoolean(),
                "a changed capture triggers backend review and waits for import before opening Warbonds");
        }
        finally { Directory.Delete(root, true); }
    }

    sealed class Commands(string root) : IHellForgeCommands
    {
        internal bool Running;
        internal int Calls;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls++;
            object result = args[0] == "mods" ? new { mods = Array.Empty<object>(), problems = Array.Empty<string>(), modlist = (string?)null }
                : new { install = root, gameBuild = "25480438", hellforge = "0.4.0-beta", engine = "0.1.47", modSdk = 16,
                    networkBlock = "linux-netns", ready = new { linuxNamespace = true, gameCopy = true, sessionRunner = true },
                    settings = new { sandbox = true, multiplayer = true, localPlayFab = true },
                    session = new { running = Running, lastRun = (string?)null, lastStatus = (string?)null, lastFailure = (string?)null } };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }
}
