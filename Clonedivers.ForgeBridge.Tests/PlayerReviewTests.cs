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
            var steamHome = Path.Combine(root, "steam-home");
            var registryFile = Path.Combine(steamHome, ".steam", "registry.vdf");
            var loginUsersFile = Path.Combine(steamHome, ".steam", "steam", "config", "loginusers.vdf");
            Directory.CreateDirectory(Path.GetDirectoryName(registryFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(loginUsersFile)!);
            void SetRegistryUser(string activeUser) => File.WriteAllText(registryFile,
                "\"Registry\"\n{\n\t\"HKCU\"\n\t{\n\t\t\"Software\"\n\t\t{\n\t\t\t\"Valve\"\n\t\t\t{\n\t\t\t\t\"Steam\"\n\t\t\t\t{\n\t\t\t\t\t\"ActiveProcess\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"ActiveUser\"\t\t\"" + activeUser + "\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n");
            void SetLoginUsers(params (string Id, string MostRecent)[] users) => File.WriteAllText(loginUsersFile,
                "\"users\"\n{\n" + string.Concat(users.Select(u => $"\t\"{u.Id}\"\n\t{{\n\t\t\"MostRecent\"\t\t\"{u.MostRecent}\"\n\t}}\n")) + "}\n");
            SetRegistryUser("111");
            var official = new Commands(root);
            var player = new PlayerCommands(root, official, backend: Backend, reviewWarbonds: Warbonds) { UtcNow = () => now, CaptureBlockCheck = () => block, SteamUserProbe = () => PlayerCommands.LinuxSteamUser(steamHome) };
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
            await Poll(); await Poll();
            check(backendCalls == 7 && warbondCalls == 5, "a successful idle poll primes the account cache before editing progression.json");
            File.AppendAllText(Path.Combine(snapshot, "progression.json"), " ");
            var fresh = await Poll();
            check(backendCalls == 8 && warbondCalls == 5 && fresh.GetProperty("account").GetProperty("Captured").GetBoolean() && !fresh.GetProperty("account").GetProperty("Imported").GetBoolean(),
                "a changed capture invalidates the primed cache, triggers backend review and waits for import before opening Warbonds");

            // #18: Linux Steam-user probe, switch invalidation, unknown cache miss, and play failure cache clear.
            File.WriteAllText(Path.Combine(snapshot, "progression.json"), """{"experience":12345}""");
            await Poll(); await Poll();
            check(backendCalls == 9 && warbondCalls == 6, "restored sealed capture re-primes the account cache");
            SetRegistryUser("222");
            await Poll(); await Poll();
            check(backendCalls == 10 && warbondCalls == 7, "switching Steam ActiveUser in registry.vdf immediately invalidates the 5-minute account cache");
            SetRegistryUser("0");
            SetLoginUsers(("76561198000000001", "1"), ("76561198000000002", "0"));
            await Poll(); await Poll();
            check(backendCalls == 11 && warbondCalls == 8, "ActiveUser=0 falls back to MostRecent in loginusers.vdf and caches that user");
            SetLoginUsers(("76561198000000001", "0"), ("76561198000000002", "1"));
            await Poll();
            check(backendCalls == 12 && warbondCalls == 9, "switching MostRecent user in loginusers.vdf invalidates the account cache");
            SetLoginUsers(("76561198000000001", "1"), ("76561198000000002", "1"));
            await Poll(); await Poll();
            check(backendCalls == 14 && warbondCalls == 11, "ambiguous MostRecent users are treated as unknown and never cached");
            File.Delete(loginUsersFile);
            await Poll(); await Poll();
            check(backendCalls == 16 && warbondCalls == 13, "missing Steam user metadata is treated as unknown and never cached");
            SetRegistryUser("0x0000006f");
            await Poll(); await Poll();
            check(backendCalls == 17 && warbondCalls == 14, "hex ActiveUser in registry.vdf parses and caches consistently");
            official.BadProfile = true;
            var playFailed = false;
            try { await player.RunAsync(["play", "--profile", "default"], CancellationToken.None); }
            catch (InvalidDataException) { playFailed = true; }
            official.BadProfile = false;
            await Poll();
            check(playFailed && backendCalls == 18, "an EnsureAccountAsync failure during play clears lastAccount so the next poll re-reads");

            // #17: Deferred Warbond retry on subsequent player-status and play calls without repeating backend review.
            var deferredCalls = 0; int? nextDeferredResult = null;
            Task<int?> DeferredWarbonds(CancellationToken ct)
            {
                deferredCalls++;
                if (nextDeferredResult is { } openedCount)
                {
                    if (openedCount > 0) File.AppendAllText(Path.Combine(profile, "profile.json"), " ");
                    return Task.FromResult<int?>(openedCount);
                }
                return Task.FromResult<int?>(null);
            }
            var backendBeforeDeferred = backendCalls;
            var deferredPlayer = new PlayerCommands(root, official, backend: Backend, reviewWarbondsWithStatus: DeferredWarbonds)
            {
                UtcNow = () => now, CaptureBlockCheck = () => block, SteamUserProbe = () => "active:111"
            };
            var d1 = await deferredPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeDeferred + 1 && deferredCalls == 1 && d1.GetProperty("account").GetProperty("Imported").GetBoolean() &&
                !d1.GetProperty("account").GetProperty("Message").GetString()!.Contains("Opened"),
                "deferred Warbond review leaves account imported without claiming Warbonds opened");
            var d2 = await deferredPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeDeferred + 1 && deferredCalls == 2,
                "a subsequent poll within 5 minutes keeps account review cached while retrying deferred Warbonds");
            nextDeferredResult = 3;
            var d3 = await deferredPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeDeferred + 1 && deferredCalls == 3 &&
                d3.GetProperty("account").GetProperty("Message").GetString()!.Contains("Opened 3 owned Warbonds."),
                "once Warbonds complete on a cached poll, the opened count appears in the account message");
            var d4 = await deferredPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeDeferred + 1 && deferredCalls == 3 &&
                !d4.GetProperty("account").GetProperty("Message").GetString()!.Contains("Opened"),
                "after deferred Warbonds complete and update profile.json, both backend and Warbond reviews stay cached");

            // Also test deferred Warbond retry during play and default OpenOwnedWarbondsAsync deferral wrapper.
            nextDeferredResult = null;
            now = now.AddMinutes(5);
            await deferredPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeDeferred + 2 && deferredCalls == 4, "a fresh 5-minute boundary review records a newly deferred Warbond pass");
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            File.WriteAllText(Path.Combine(root, "mods", "hellforge-mods.json"), """{"share":false}""");
            official.IncludeGameplayMods = true;
            var backendBeforePlay = backendCalls;
            var playPlayer = new PlayerCommands(root, official, backend: Backend, reviewWarbondsWithStatus: DeferredWarbonds)
            {
                UtcNow = () => now, CaptureBlockCheck = () => block, SteamUserProbe = () => "active:111",
                EnsureAccount = _ => Task.FromResult(HellForgeAccount.Profile)
            };
            await playPlayer.RunAsync(["select-mode", "--mode", "regular", "--delta", "yes", "--texture", "full", "--droids", "yes", "--covenant", "no"], CancellationToken.None);
            await playPlayer.RunAsync(["player-status"], CancellationToken.None);
            nextDeferredResult = 2;
            official.Calls = 0;
            await playPlayer.RunAsync(["play", "--profile", "default"], CancellationToken.None);
            check(deferredCalls == 6 && official.PlayCalls == 1, "play retries a deferred Warbond pass before starting the session");
            official.Running = false;
            var afterPlay = await playPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforePlay + 1 && deferredCalls == 6 &&
                afterPlay.GetProperty("account").GetProperty("Message").GetString()!.Contains("Opened 2 owned Warbonds."),
                "play clearing warbondsPending refreshes the cache key and reports the opened Warbonds on the next idle status");
            official.IncludeGameplayMods = false;

            var backendBeforeDefault = backendCalls;
            official.StatusRunningOnCall = 2; // 1st status (StateAsync) is idle, 2nd status (HellForgeWarbonds) sees running -> defers!
            var defaultWarbondsPlayer = new PlayerCommands(root, official, backend: Backend)
            {
                UtcNow = () => now, CaptureBlockCheck = () => block, SteamUserProbe = () => "active:111"
            };
            official.StatusCalls = 0;
            await defaultWarbondsPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeDefault + 1 && official.StatusCalls == 3,
                "default OpenOwnedWarbondsAsync wrapper catches active-session deferral on the first review");
            official.StatusCalls = 0;
            official.StatusRunningOnCall = 2;
            await defaultWarbondsPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeDefault + 1 && official.StatusCalls == 3,
                "default OpenOwnedWarbondsAsync wrapper retries Warbonds on cached polls without re-running backend review");

            // v2.0 select-mode accepts reduced512 and --enemy-skin while retaining 5-arg backwards compatibility.
            var v2Resp = await playPlayer.RunAsync(["select-mode", "--mode", "clonedivers", "--delta", "yes", "--texture", "reduced512", "--droids", "yes", "--covenant", "yes", "--enemy-skin", "mixed-droids"], CancellationToken.None);
            var v2Prefs = new HellForgePreferences { Mode = v2Resp.GetProperty("Mode").GetString()!, Texture = v2Resp.GetProperty("Texture").GetString()!, EnemySkin = v2Resp.GetProperty("EnemySkin").GetString()!, Droids = v2Resp.GetProperty("Droids").GetBoolean(), Covenant = v2Resp.GetProperty("Covenant").GetBoolean() };
            var v2Selected = v2Prefs.Selection();
            check(v2Prefs.Mode == "clonedivers" && v2Prefs.Texture == "reduced512" && v2Prefs.EnemySkin == "mixed-droids" && v2Prefs.Droids && v2Prefs.Covenant &&
                v2Selected.Version == "2026.11.19-r20" && v2Selected.Texture == "reduced512" && v2Selected.Options["mixed-droids"] && v2Selected.Options["droids"] && v2Selected.Options["covenant"] && v2Selected.Files.Count == 114,
                "bridge select-mode resolves reduced512, mixed-droids, and covenant natively against Clones r20");
            var backResp = await playPlayer.RunAsync(["select-mode", "--mode", "empire", "--delta", "no", "--texture", "lighter", "--droids", "no", "--covenant", "no"], CancellationToken.None);
            check(backResp.GetProperty("Mode").GetString() == "empire" && backResp.GetProperty("Texture").GetString() == "lighter" &&
                !backResp.GetProperty("Droids").GetBoolean() && backResp.GetProperty("EnemySkin").GetString() == "native-automatons",
                "bridge 5-arg select-mode stays backwards-compatible and maps droids=no to native-automatons");
        }
        finally { Directory.Delete(root, true); }
    }

    sealed class Commands(string root) : IHellForgeCommands
    {
        internal bool Running, BadProfile, IncludeGameplayMods;
        internal int Calls, StatusCalls, PlayCalls, StatusRunningOnCall;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls++;
            if (args[0] == "play") { PlayCalls++; Running = true; return Task.FromResult(JsonSerializer.SerializeToElement(new { @event = "started" })); }
            if (args[0] == "profiles")
                return Task.FromResult(JsonSerializer.SerializeToElement(new { profiles = new[] { new { name = "default", template = BadProfile ? "unlock-all" : "starter" } } }));
            if (args[0] == "settings")
                return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
            if (args[0] == "mod-hash")
            {
                var id = Path.GetFileName(args[2]);
                var hash = HellForgeGameplayCatalog.Pinned.Artifacts.Single(a => a.Id == id).ContentHash;
                return Task.FromResult(JsonSerializer.SerializeToElement(new { hash }));
            }
            var statusRunning = Running;
            if (args[0] == "status")
            {
                StatusCalls++;
                if (StatusRunningOnCall > 0 && --StatusRunningOnCall == 0) statusRunning = true;
            }
            object result = args[0] == "mods"
                ? new { mods = IncludeGameplayMods ? ReadyMods() : Array.Empty<object>(), problems = Array.Empty<string>(), modlist = (string?)null }
                : new { install = root, gameBuild = "25480438", hellforge = "0.4.0-beta", engine = "0.1.47", modSdk = 16,
                    networkBlock = "linux-netns", ready = new { linuxNamespace = true, gameCopy = true, sessionRunner = true },
                    settings = new { sandbox = true, multiplayer = true, localPlayFab = true },
                    session = new { running = statusRunning, lastRun = (string?)null, lastStatus = (string?)null, lastFailure = (string?)null } };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        object[] ReadyMods() => HellForgeGameplayCatalog.Pinned.Order.Select(id =>
        {
            var artifact = HellForgeGameplayCatalog.Pinned.Artifacts.SingleOrDefault(a => a.Id == id);
            var req = HellForgePreset.Required.SingleOrDefault(r => r.Id == id);
            var name = artifact?.Name ?? (string.IsNullOrEmpty(req.Name) ? "Vanilla Plus" : req.Name);
            var version = artifact?.Version ?? req.Version ?? "";
            var folder = Path.Combine(root, "mods", id);
            Directory.CreateDirectory(folder);
            object[]? options = id == HellForgePreset.GuardDogs
                ? new object[]
                {
                    new { name = "Targeting Marker", on = false },
                    new { name = "Smarter Guard Dogs and Sentries", on = true },
                    new { name = "Guard Dogs", on = true },
                    new { name = "Sentries", on = true },
                    new { name = "Intelligence", on = true },
                    new { name = "Safety", on = true },
                    new { name = "Sentry Safety", on = true },
                    new { name = "SEAF Safety", on = true }
                }
                : null;
            return (object)new { id, name, enabled = true, folder, tags = new[] { "gameplay" }, version, options };
        }).ToArray();
    }
}
