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

            // v2.0/v2.3 select-mode accepts all 5 texture tiers, --enemy-skin, and --squid-skin (with --deathtrooper removed) while retaining 5-arg backwards compatibility.
            var v2Resp = await playPlayer.RunAsync(["select-mode", "--mode", "clonedivers", "--delta", "yes", "--texture", "reduced512", "--droids", "yes", "--covenant", "yes", "--enemy-skin", "mixed-droids"], CancellationToken.None);
            var v2Prefs = new HellForgePreferences { Mode = v2Resp.GetProperty("Mode").GetString()!, Texture = v2Resp.GetProperty("Texture").GetString()!, EnemySkin = v2Resp.GetProperty("EnemySkin").GetString()!, Droids = v2Resp.GetProperty("Droids").GetBoolean(), Covenant = v2Resp.GetProperty("Covenant").GetBoolean() };
            var v2Selected = v2Prefs.Selection();
            check(v2Prefs.Mode == "clonedivers" && v2Prefs.Texture == "reduced512" && v2Prefs.EnemySkin == "mixed-droids" && v2Prefs.Droids && v2Prefs.Covenant &&
                v2Selected.Version == "2026.11.28-r26" && v2Selected.Texture == "reduced512" && v2Selected.Options["mixed-droids"] && v2Selected.Options["droids"] && v2Selected.Options["covenant"] && v2Selected.Files.Count == 144,
                "bridge select-mode resolves reduced512, mixed-droids, and covenant natively against Clones r26");
            var v21EmpResp = await playPlayer.RunAsync(["select-mode", "--mode", "empire", "--delta", "no", "--texture", "potato256", "--droids", "yes", "--covenant", "yes", "--enemy-skin", "mixed-droids", "--squid-skin", "mixed-covenant"], CancellationToken.None);
            var v21EmpPrefs = new HellForgePreferences
            {
                Mode = v21EmpResp.GetProperty("Mode").GetString()!,
                Texture = v21EmpResp.GetProperty("Texture").GetString()!,
                EnemySkin = v21EmpResp.GetProperty("EnemySkin").GetString()!,
                SquidSkin = v21EmpResp.GetProperty("SquidSkin").GetString()!
            };
            var v21EmpSel = v21EmpPrefs.Selection();
            check(v21EmpSel.Version == "2026.11.28-r55" && v21EmpSel.Texture == "potato256" && v21EmpSel.Options["mixed-covenant"] && v21EmpSel.Options["covenant"] && !v21EmpSel.Options.GetValueOrDefault("deathtrooper") && v21EmpSel.Files.Count == 144,
                "bridge select-mode resolves potato256, mixed-covenant, and strips deathtrooper against Empire r55");
            var backResp = await playPlayer.RunAsync(["select-mode", "--mode", "empire", "--delta", "no", "--texture", "lighter", "--droids", "no", "--covenant", "no"], CancellationToken.None);
            check(backResp.GetProperty("Mode").GetString() == "empire" && backResp.GetProperty("Texture").GetString() == "lighter" &&
                !backResp.GetProperty("Droids").GetBoolean() && backResp.GetProperty("EnemySkin").GetString() == "native-automatons",
                "bridge 5-arg select-mode stays backwards-compatible and maps droids=no to native-automatons");

            // #28: Modern Steam VDF fallbacks (AutoLogin / single user / newest timestamp / registry AutoLoginUser) + mid-startup SDK shim guard.
            var steam28 = Path.Combine(root, "steam-home-28");
            var reg28 = Path.Combine(steam28, ".steam", "registry.vdf");
            var login28 = Path.Combine(steam28, ".steam", "steam", "config", "loginusers.vdf");
            Directory.CreateDirectory(Path.GetDirectoryName(reg28)!);
            Directory.CreateDirectory(Path.GetDirectoryName(login28)!);
            File.WriteAllText(reg28, "\"Registry\"\n{\n\t\"HKCU\"\n\t{\n\t\t\"Software\"\n\t\t{\n\t\t\t\"Valve\"\n\t\t\t{\n\t\t\t\t\"Steam\"\n\t\t\t\t{\n\t\t\t\t\t\"AutoLoginUser\"\t\t\"alamo\"\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n");
            check(PlayerCommands.LinuxSteamUser(steam28) == "autologinuser:alamo",
                "#28 registry.vdf AutoLoginUser fallback caches when ActiveUser and loginusers.vdf are absent");
            File.WriteAllText(login28, "\"users\"\n{\n\t\"76561198000000041\"\n\t{\n\t\t\"AccountName\"\t\t\"alamo\"\n\t\t\"AutoLogin\"\t\t\"1\"\n\t\t\"timestamp\"\t\t\"1795235000\"\n\t}\n}\n");
            check(PlayerCommands.LinuxSteamUser(steam28) == "autologin:76561198000000041",
                "#28 loginusers.vdf AutoLogin=1 without MostRecent caches the signed-in Linux Steam user");
            File.WriteAllText(login28, "\"users\"\n{\n\t\"76561198000000041\"\n\t{\n\t\t\"AccountName\"\t\t\"alamo\"\n\t\t\"timestamp\"\t\t\"1795235000\"\n\t}\n}\n");
            check(PlayerCommands.LinuxSteamUser(steam28) == "single:76561198000000041",
                "#28 single-user loginusers.vdf without MostRecent or AutoLogin caches cleanly");
            File.WriteAllText(login28, "\"users\"\n{\n\t\"76561198000000041\"\n\t{\n\t\t\"timestamp\"\t\t\"1795235000\"\n\t}\n\t\"76561198000000042\"\n\t{\n\t\t\"timestamp\"\t\t\"1795236000\"\n\t}\n}\n");
            check(PlayerCommands.LinuxSteamUser(steam28) == "latest:76561198000000042",
                "#28 multi-user loginusers.vdf without MostRecent picks the unique newest timestamp");

            var shimDir = Path.Combine(root, "temp", "sdk-shim-originals");
            Directory.CreateDirectory(shimDir);
            var shimDll = Path.Combine(shimDir, "steam_api64.dll");
            File.WriteAllText(shimDll, "orig");
            File.SetLastWriteTimeUtc(shimDll, now);
            var backendBeforeShim = backendCalls;
            var shimResp = await playPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeShim && shimResp.GetProperty("detail").GetString()!.Contains("Session starting"),
                "#28 active sdk-shim-originals suppresses background ownership probes during the session startup window");
            Directory.Delete(shimDir, true);
            var afterShim = await playPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == backendBeforeShim && afterShim.GetProperty("account").GetProperty("Imported").GetBoolean(),
                "#28 cached account state survives the session startup shim window cleanly without reverting to Sync account");

            // #37a: Mutable profile.json.bak / launcher.json / profile.lock writes do NOT invalidate AccountFingerprint.
            var callsBeforeAux = backendCalls;
            File.WriteAllText(Path.Combine(profile, "profile.json.bak"), """{"bak":1}""");
            File.WriteAllText(Path.Combine(profile, "launcher.json"), """{"ui":1}""");
            File.WriteAllText(Path.Combine(profile, "profile.lock"), "");
            var afterAux = await playPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == callsBeforeAux && afterAux.GetProperty("account").GetProperty("Imported").GetBoolean(),
                "#37 profile.json.bak, launcher.json, and profile.lock mutations do not re-trigger LocalBackend ownership probes");

            // #37b: SessionShimsActive binary content/length comparison works even when sdk-shim-originals mtime is weeks old.
            var binDir = Path.Combine(root, "game", "bin");
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(shimDir);
            var oldDate = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
            var savedDll = Path.Combine(shimDir, "steam_api64.dll");
            var liveDll = Path.Combine(binDir, "steam_api64.dll");
            File.WriteAllBytes(savedDll, Encoding.UTF8.GetBytes("stock-steam-api64-298384"));
            File.SetLastWriteTimeUtc(savedDll, oldDate);
            File.WriteAllBytes(liveDll, Encoding.UTF8.GetBytes("stock-steam-api64-298384"));
            File.SetLastWriteTimeUtc(liveDll, oldDate);
            check(!HellForgeAccount.SessionShimsActive(root, now),
                "#37 SessionShimsActive is false when restored game/bin/steam_api64.dll matches sdk-shim-originals");
            File.WriteAllBytes(liveDll, Encoding.UTF8.GetBytes("shim-255488"));
            File.SetLastWriteTimeUtc(liveDll, oldDate);
            check(HellForgeAccount.SessionShimsActive(root, now),
                "#37 SessionShimsActive detects swapped shim DLL by length/hash even when mtime is weeks old");
            File.WriteAllBytes(liveDll, Encoding.UTF8.GetBytes("stock-steam-api64-298384"));

            // #37c: Transient Wine/SDK-shim review errors preserve Imported=true instead of flipping UI to Sync account, then re-verify cleanly.
            var transientErr = false;
            Task<string> TransientBackend(IReadOnlyList<string> args, CancellationToken ct)
            {
                backendCalls++;
                if (transientErr) throw new InvalidDataException("The ownership check needs the supported build's own steam_api64.dll");
                return Task.FromResult("""{"categories":{"level":{"vanilla":"12345 XP"}}}""");
            }
            var transientPlayer = new PlayerCommands(root, official, backend: TransientBackend, reviewWarbondsWithStatus: _ => Task.FromResult<int?>(0))
            {
                UtcNow = () => now, CaptureBlockCheck = () => null, SteamUserProbe = () => "active:111"
            };
            var tPrimed = await transientPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(tPrimed.GetProperty("account").GetProperty("Imported").GetBoolean(), "#37 transient player primed");
            transientErr = true;
            File.AppendAllText(Path.Combine(profile, "profile.json"), " ");
            var tDuringWine = await transientPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(tDuringWine.GetProperty("account").GetProperty("Imported").GetBoolean(),
                "#37 transient Wine/shim error after profile.json mutation preserves Imported=true instead of prompting Sync account");
            transientErr = false;
            var callsBeforeRecover = backendCalls;
            var tRecovered = await transientPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(backendCalls == callsBeforeRecover + 1 && tRecovered.GetProperty("account").GetProperty("Imported").GetBoolean(),
                "#37 cleared fingerprint after transient Wine error re-verifies on the very next poll");

            // #37d: OS-wide ManagerWindowOpen prevents spawning duplicate HellForge.Launcher child windows.
            var prevProcProbe = HellForgeWarbonds.AnyProcessNamed;
            try
            {
                if (!OperatingSystem.IsLinux())
                {
                    HellForgeWarbonds.AnyProcessNamed = name => name == "HellForge.Launcher";
                    var dupPlayer = new PlayerCommands(root, official, backend: TransientBackend, reviewWarbondsWithStatus: _ => Task.FromResult<int?>(0));
                    var mgrReply = await dupPlayer.RunAsync(["open-manager"], CancellationToken.None);
                    check(mgrReply.GetProperty("alreadyOpen").GetBoolean(),
                        "#37 open-manager reuses an already-open OS HellForge.Launcher process instead of spawning duplicates");
                }
            }
            finally { HellForgeWarbonds.AnyProcessNamed = prevProcProbe; }

            // v2.3 Linux theater switching synchronizes Smarter SEAF while keeping Guard Dogs SEAF Safety, Sentry Safety, Targeting Laser, and Targeting Marker OFF across both theaters!
            official.IncludeGameplayMods = true;
            official.SmarterSeafAllOn = true;
            official.GuardDogsSeafSafetyOn = true;
            official.GuardDogsSentrySafetyOn = true;
            official.GuardDogsTargetingLaserOn = true;
            official.ModPackCalls.Clear();
            var ukPlayer = new PlayerCommands(root, official, backend: Backend, reviewWarbondsWithStatus: _ => Task.FromResult<int?>(0))
            {
                UtcNow = () => now, CaptureBlockCheck = () => null, SteamUserProbe = () => "active:111"
            };
            await ukPlayer.RunAsync(["select-mode", "--mode", "regular", "--delta", "yes", "--texture", "full", "--droids", "yes", "--covenant", "no", "--theater", "clone-wars"], CancellationToken.None);
            var ukStatus = await ukPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(!official.SmarterSeafAllOn && !official.GuardDogsSeafSafetyOn &&
                  !official.GuardDogsSentrySafetyOn && !official.GuardDogsTargetingLaserOn &&
                  official.ModPackCalls.Contains($"mod-pack --id {HellForgePreset.SmarterSeaf} --all no") &&
                  official.ModPackCalls.Contains($"mod-pack --id {HellForgePreset.GuardDogs} --option SEAF Safety --on no") &&
                  official.ModPackCalls.Contains($"mod-pack --id {HellForgePreset.GuardDogs} --option Sentry Safety --on no") &&
                  official.ModPackCalls.Contains($"mod-pack --id {HellForgePreset.GuardDogs} --option Targeting Laser --on no") &&
                  ukStatus.GetProperty("ready").GetBoolean(),
                  "v2.3 Linux select-mode --theater clone-wars suppresses Smarter SEAF & turns off Guard Dogs SEAF Safety, Sentry Safety, and Targeting Laser");

            official.ModPackCalls.Clear();
            await ukPlayer.RunAsync(["select-mode", "--mode", "regular", "--delta", "yes", "--texture", "full", "--droids", "yes", "--covenant", "no"], CancellationToken.None);
            var stdStatus = await ukPlayer.RunAsync(["player-status"], CancellationToken.None);
            check(official.SmarterSeafAllOn && !official.GuardDogsSeafSafetyOn &&
                  !official.GuardDogsSentrySafetyOn && !official.GuardDogsTargetingLaserOn &&
                  official.ModPackCalls.Contains($"mod-pack --id {HellForgePreset.SmarterSeaf} --all yes") &&
                  stdStatus.GetProperty("ready").GetBoolean(),
                  "v2.3 Linux select-mode returning to Standard restores Smarter SEAF while keeping Guard Dogs SEAF Safety, Sentry Safety, and Targeting Laser OFF");

            // Injected failure during Clone Wars -> Standard restoration retries cleanly on repeated Standard select-mode AND across fresh bridge restart + play!
            await ukPlayer.RunAsync(["select-mode", "--mode", "regular", "--delta", "yes", "--texture", "full", "--droids", "yes", "--covenant", "no", "--theater", "clone-wars"], CancellationToken.None);
            official.FailModPackRemaining = 1;
            var failedDisarm = false;
            try { await ukPlayer.RunAsync(["select-mode", "--mode", "regular", "--delta", "yes", "--texture", "full", "--droids", "yes", "--covenant", "no"], CancellationToken.None); }
            catch (IOException) { failedDisarm = true; }
            check(failedDisarm && !official.SmarterSeafAllOn, "injected mod-pack failure leaves Standard Smarter SEAF restoration incomplete");
            await ukPlayer.RunAsync(["select-mode", "--mode", "regular", "--delta", "yes", "--texture", "full", "--droids", "yes", "--covenant", "no"], CancellationToken.None);
            check(official.SmarterSeafAllOn && !official.GuardDogsSeafSafetyOn,
                "repeating Standard select-mode after failed disarm retries companion restoration until reconciled");

            // Simulate bridge restart while companions are still suppressed/legacy-enabled on disk -> Standard play repairs before PlayRegularAsync!
            official.SmarterSeafAllOn = false;
            official.GuardDogsSeafSafetyOn = true;
            official.GuardDogsSentrySafetyOn = true;
            official.GuardDogsTargetingLaserOn = true;
            var restartedBridge = new PlayerCommands(root, official, backend: Backend, reviewWarbondsWithStatus: _ => Task.FromResult<int?>(0))
            {
                UtcNow = () => now, CaptureBlockCheck = () => null, SteamUserProbe = () => "active:111",
                EnsureAccount = _ => Task.FromResult(HellForgeAccount.Profile)
            };
            await restartedBridge.RunAsync(["select-mode", "--mode", "regular", "--delta", "yes", "--texture", "full", "--droids", "yes", "--covenant", "no"], CancellationToken.None);
            var gameplayMissing = await restartedBridge.RunAsync(["gameplay-status"], CancellationToken.None);
            bool packFolderHashRejected = false;
            try { await official.RunAsync(["mod-hash", "--mod", Path.Combine(root, "mods", HellForgePreset.SmarterSeaf)]); }
            catch (IOException) { packFolderHashRejected = true; }
            check(packFolderHashRejected && gameplayMissing.GetArrayLength() == 0,
                "ForgeBridge gameplay-status hashes archive:null pack.* folders via temporary zip while non-zip pack.* directory mod-hash fails like real 0.5.0-beta");
            await restartedBridge.RunAsync(["play", "--profile", "default"], CancellationToken.None);
            check(official.SmarterSeafAllOn && !official.GuardDogsSeafSafetyOn && !official.GuardDogsSentrySafetyOn && !official.GuardDogsTargetingLaserOn && official.Running,
                "fresh bridge restart in Standard mode repairs Smarter SEAF and clears Guard Dogs SEAF Safety / Sentry Safety / Targeting Laser during play before preset validation");
            official.Running = false;
            official.IncludeGameplayMods = false;
        }
        finally { Directory.Delete(root, true); }
    }

    sealed class Commands(string root) : IHellForgeCommands
    {
        internal bool Running, BadProfile, IncludeGameplayMods, SmarterSeafAllOn = true, GuardDogsSeafSafetyOn = false, GuardDogsSentrySafetyOn = false, GuardDogsTargetingLaserOn = false;
        internal int FailModPackRemaining;
        internal readonly List<string> ModPackCalls = [];
        internal int Calls, StatusCalls, PlayCalls, StatusRunningOnCall;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls++;
            if (args[0] == "play") { PlayCalls++; Running = true; return Task.FromResult(JsonSerializer.SerializeToElement(new { @event = "started" })); }
            if (args[0] == "mod-pack")
            {
                if (FailModPackRemaining > 0) { FailModPackRemaining--; throw new IOException("injected mod-pack failure"); }
                ModPackCalls.Add(string.Join(" ", args));
                if (args.Count >= 5 && args[2] == HellForgePreset.SmarterSeaf && args[3] == "--all")
                    SmarterSeafAllOn = args[4] == "yes";
                else if (args.Count >= 7 && args[2] == HellForgePreset.GuardDogs && args[4] == "SEAF Safety")
                    GuardDogsSeafSafetyOn = args[6] == "yes";
                else if (args.Count >= 7 && args[2] == HellForgePreset.GuardDogs && args[4] == "Sentry Safety")
                    GuardDogsSentrySafetyOn = args[6] == "yes";
                else if (args.Count >= 7 && args[2] == HellForgePreset.GuardDogs && args[4] == "Targeting Laser")
                    GuardDogsTargetingLaserOn = args[6] == "yes";
                return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
            }
            if (args[0] == "profiles")
                return Task.FromResult(JsonSerializer.SerializeToElement(new { profiles = new[] { new { name = "default", template = BadProfile ? "unlock-all" : "starter" } } }));
            if (args[0] == "settings")
                return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
            if (args[0] == "mod-hash")
            {
                var target = args[2];
                string id;
                if (target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(target);
                    if (zip.GetEntry("hellforge-mod.json") is { } hfEntry)
                    {
                        using var s = hfEntry.Open();
                        using var doc = JsonDocument.Parse(s);
                        id = doc.RootElement.GetProperty("id").GetString()!;
                    }
                    else if (zip.GetEntry("manifest.json") is { } packEntry)
                    {
                        using var s = packEntry.Open();
                        using var doc = JsonDocument.Parse(s);
                        id = "pack." + doc.RootElement.GetProperty("Guid").GetString()!;
                    }
                    else throw new InvalidDataException("Archive holds no mod.");
                }
                else
                {
                    var mf = Path.Combine(target, "hellforge-mod.json");
                    if (!File.Exists(mf)) throw new FileNotFoundException($"HellForge: Could not find file '{mf}'.");
                    using var doc = JsonDocument.Parse(File.ReadAllText(mf));
                    id = doc.RootElement.GetProperty("id").GetString()!;
                }
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
                : new { install = root, gameBuild = "25480438", hellforge = "0.5.0-beta", engine = "0.1.58", modSdk = 20,
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
            if (id.StartsWith("pack.", StringComparison.Ordinal))
            {
                var manifestPath = Path.Combine(folder, "manifest.json");
                if (!File.Exists(manifestPath))
                    File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { Version = 1, Guid = id["pack.".Length..], Name = name }));
                var patchPath = Path.Combine(folder, "0000000000000001.patch_0");
                if (!File.Exists(patchPath))
                    File.WriteAllBytes(patchPath, [1, 2, 3, 4]);
            }
            else
            {
                var modJson = Path.Combine(folder, "hellforge-mod.json");
                if (!File.Exists(modJson))
                    File.WriteAllText(modJson, JsonSerializer.Serialize(new { id, name, version }));
            }
            object[]? options = id == HellForgePreset.GuardDogs
                ? new object[]
                {
                    new { name = "Targeting Marker", on = false },
                    new { name = "Targeting Laser", on = GuardDogsTargetingLaserOn },
                    new { name = "Smarter Guard Dogs and Sentries", on = true },
                    new { name = "Guard Dogs", on = true },
                    new { name = "Sentries", on = true },
                    new { name = "Intelligence", on = true },
                    new { name = "Safety", on = true },
                    new { name = "Sentry Safety", on = GuardDogsSentrySafetyOn },
                    new { name = "SEAF Safety", on = GuardDogsSeafSafetyOn }
                }
                : id == HellForgePreset.SmarterSeaf
                    ? new object[] { new { name = "Smarter SEAF", on = SmarterSeafAllOn } }
                    : null;
            return (object)new { id, name, enabled = true, folder, archive = (string?)null, tags = new[] { "gameplay" }, version, options };
        }).ToArray();
    }
}
