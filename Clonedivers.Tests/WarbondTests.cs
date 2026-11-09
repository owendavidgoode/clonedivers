using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clonedivers.Tests;

static class WarbondTests
{
    const string Profile = """
        {"schemaVersion":1,"progression":{"experience":12345},"customization":{"items":[]},"inventory":{"items":[
        {"mixId":301,"amount":5,"isDiscovered":false},{"mixId":401,"amount":0,"isDiscovered":false},
        {"mixId":701,"amount":0,"isDiscovered":true},{"mixId":777,"amount":90,"isDiscovered":true}]}}
        """;
    const string Inventory = """
        {"items":[{"mixId":100,"amount":1,"isDiscovered":true},{"mixId":202,"amount":1,"isDiscovered":false},
        {"mixId":302,"amount":1,"isDiscovered":true},{"mixId":401,"amount":1,"isDiscovered":true},
        {"mixId":601,"amount":0,"isDiscovered":true},{"mixId":701,"amount":1,"isDiscovered":true}]}
        """;
    const string Data = """
        {"seasonPassDetails":{"free":{"id32":1929468580,"premiumTokenMixIds":[100]},
        "owned":{"id32":10,"premiumTokenMixIds":[201,202]},"held":{"id32":11,"premiumTokenMixIds":[301,302]},
        "zero":{"id32":12,"premiumTokenMixIds":[401]},"unowned":{"id32":13,"premiumTokenMixIds":[501]},
        "zeroOwned":{"id32":14,"premiumTokenMixIds":[601]},"zeroHeld":{"id32":15,"premiumTokenMixIds":[701]}}}
        """;
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    sealed class Fixture
    {
        public string Root { get; }
        public string ProfilePath => Path.Combine(Root, "profiles", "default", "profile.json");
        public string Snapshot => Path.Combine(Root, "account-snapshot", "default");
        public string Backups => Path.Combine(Root, "profiles", "default", "backups");
        public Commands Cli { get; }
        public bool ProcessesRunning, RefuseSeal, ChangeSnapshot, ChangeProfile, Cancel, StartSession;
        public int Verifications;
        public CancellationTokenSource Cancellation { get; } = new();
        public Fixture(string root)
        {
            Root = root; Cli = new(root);
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!); Directory.CreateDirectory(Snapshot); Directory.CreateDirectory(Path.Combine(root, "app"));
            File.WriteAllText(ProfilePath, Profile);
            File.WriteAllText(Path.Combine(root, "app", "progression-data.json"), Data);
            File.WriteAllText(Path.Combine(Snapshot, "snapshot.json"), "{\"progression\":true}");
            File.WriteAllText(Path.Combine(Snapshot, "real-inventory.json"), Inventory);
            File.WriteAllText(Path.Combine(Snapshot, "progression.json"), "{\"experience\":12345}");
            File.WriteAllText(Path.Combine(Snapshot, "seal.json"), JsonSerializer.Serialize(new {
                steamAccount = "1", capturedUtc = "2026-11-01T00:00:00Z",
                inventorySha256 = Hash(File.ReadAllBytes(Path.Combine(Snapshot, "real-inventory.json"))),
                progressionSha256 = Hash(File.ReadAllBytes(Path.Combine(Snapshot, "progression.json"))) }));
        }
        Task<string> Backend(IReadOnlyList<string> args, CancellationToken ct)
        {
            if (args[1] != "import-preview") throw new InvalidOperationException("Warbonds must use only read-only verification.");
            Verifications++;
            if (RefuseSeal) throw new InvalidDataException("Fixture account seal rejected.");
            if (ChangeSnapshot) File.AppendAllText(Path.Combine(Snapshot, "real-inventory.json"), " ");
            if (ChangeProfile) File.AppendAllText(ProfilePath, " ");
            if (Cancel) Cancellation.Cancel();
            if (StartSession) Cli.Running = true;
            return Task.FromResult("{\"categories\":{\"level\":{\"vanilla\":\"12,345 XP\"}}}");
        }
        public Task<int> Open() => new HellForgeWarbonds(Root, Cli, Backend, () => ProcessesRunning).OpenAsync(Cancellation.Token);
    }
    sealed class Commands(string root) : IHellForgeCommands
    {
        public bool Running;
        public string Install = root;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!args.SequenceEqual(new[] { "status" })) throw new InvalidOperationException("Unexpected Warbond command.");
            return Task.FromResult(JsonSerializer.SerializeToElement(new { install = Install, session = new { running = Running } }));
        }
    }
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var area = Path.Combine(root, "warbonds");
        var fixture = new Fixture(Path.Combine(area, "success"));
        var before = File.ReadAllBytes(fixture.ProfilePath);
        check(await fixture.Open() == 3, "owned Warbonds open with exactly one snapshot-held token per missing Warbond");
        var updated = JsonNode.Parse(File.ReadAllBytes(fixture.ProfilePath))!.AsObject();
        var items = updated["inventory"]!["items"]!.AsArray().ToDictionary(i => i!["mixId"]!.GetValue<uint>());
        check(items.ContainsKey(202) && !items.ContainsKey(201), "only the snapshot-owned alternative token is added");
        check(!items.ContainsKey(100) && !items.ContainsKey(501) && !items.ContainsKey(601), "free, unowned and zero-amount snapshot tokens are ignored");
        check(items[301]!["amount"]!.GetValue<int>() == 5 && !items[301]!["isDiscovered"]!.GetValue<bool>() && !items.ContainsKey(302), "an already-open Warbond keeps its held variant, amount and discovery state");
        check(items[401]!["amount"]!.GetValue<int>() == 1 && !items[401]!["isDiscovered"]!.GetValue<bool>(), "zero-amount entries are incremented without changing discovery as ProfileStore.AddItem does");
        check(items[202]!["amount"]!.GetValue<int>() == 1 && items[202]!["isDiscovered"]!.GetValue<bool>(), "new tokens have amount one and are discovered");
        check(items[777]!["amount"]!.GetValue<int>() == 90 && updated["progression"]!["experience"]!.GetValue<int>() == 12345, "Warbond opening preserves currencies and progression");
        var backup = Directory.GetFiles(fixture.Backups, "*-warbonds.json").Single();
        check(File.ReadAllBytes(backup).SequenceEqual(before) && File.ReadAllText(backup + ".sha256").StartsWith(Hash(before), StringComparison.OrdinalIgnoreCase), "the timestamped backup and SHA-256 match the original profile");
        check(File.ReadAllBytes(fixture.ProfilePath + ".bak").SequenceEqual(before), "atomic replacement retains the backend-compatible profile.json.bak");
        var saved = File.ReadAllBytes(fixture.ProfilePath);
        check(await fixture.Open() == 0 && File.ReadAllBytes(fixture.ProfilePath).SequenceEqual(saved) && Directory.GetFiles(fixture.Backups, "*-warbonds.json").Length == 1, "repeated opening is byte-identical and creates no extra backup");

        async Task Refuses(string name, Action<Fixture> prepare, bool verified = false)
        {
            var blocked = new Fixture(Path.Combine(area, name)); prepare(blocked);
            var original = File.ReadAllBytes(blocked.ProfilePath);
            bool refused = false;
            try { await blocked.Open(); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException or OperationCanceledException) { refused = true; }
            check(refused && File.ReadAllBytes(blocked.ProfilePath).SequenceEqual(original) && !Directory.Exists(blocked.Backups), name + " refuses without changing the profile or creating a backup");
            check((blocked.Verifications > 0) == verified, name + " reaches only the expected verification stage");
        }
        await Refuses("running-session", f => f.Cli.Running = true);
        await Refuses("running-game-or-manager", f => f.ProcessesRunning = true);
        await Refuses("session-started-during-verification", f => f.StartSession = true, true);
        await Refuses("different-installation", f => f.Cli.Install = f.Root + "-other");
        if (!OperatingSystem.IsWindows())
            await Refuses("different-case-installation", f => f.Cli.Install = f.Root.ToUpperInvariant());
        await Refuses("corrupt-snapshot", f => File.AppendAllText(Path.Combine(f.Snapshot, "real-inventory.json"), " "));
        await Refuses("missing-snapshot", f => File.Delete(Path.Combine(f.Snapshot, "seal.json")));
        await Refuses("untrusted-seal-or-account", f => f.RefuseSeal = true, true);
        await Refuses("snapshot-changed-during-verification", f => f.ChangeSnapshot = true, true);
        await Refuses("cancelled-before-write", f => f.Cancel = true, true);
        await Refuses("malformed-inventory", f => File.WriteAllText(f.ProfilePath, "{\"schemaVersion\":1,\"inventory\":{\"items\":[null]}}"), true);
        await Refuses("malformed-catalogue", f => File.WriteAllText(Path.Combine(f.Root, "app", "progression-data.json"), "{\"seasonPassDetails\":{}}"), true);
        var locked = new Fixture(Path.Combine(area, "profile-locked"));
        using (var held = new FileStream(Path.Combine(Path.GetDirectoryName(locked.ProfilePath)!, "profile.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            bool refused = false;
            try { await locked.Open(); } catch (IOException) { refused = true; }
            check(refused && locked.Verifications == 0 && File.ReadAllText(locked.ProfilePath) == Profile, "a held profile.lock refuses before verification or profile writes");
        }
        var accountLocked = new Fixture(Path.Combine(area, "account-locked"));
        using (var held = new HellForgeAccount(accountLocked.Root).Lease())
        {
            bool refused = false;
            try { await accountLocked.Open(); } catch (IOException) { refused = true; }
            check(refused && accountLocked.Verifications == 0, "an import or launch account lease excludes Warbond opening");
        }
        var changed = new Fixture(Path.Combine(area, "profile-changed")) { ChangeProfile = true };
        bool changedRefused = false;
        try { await changed.Open(); } catch (InvalidDataException) { changedRefused = true; }
        check(changedRefused && File.ReadAllText(changed.ProfilePath) == Profile + " " && !Directory.Exists(changed.Backups), "a concurrent profile edit is preserved and refuses the stale write");
        try
        {
            foreach (var name in new[] { "profile", "snapshot", "lock", "backup", "fallback", "ancestor" })
            {
                var linked = new Fixture(Path.Combine(area, "linked-" + name, "HellForge"));
                var original = File.ReadAllBytes(linked.ProfilePath);
                var target = Path.Combine(area, "target-" + name);
                string link;
                if (name is "profile" or "fallback" or "lock")
                {
                    link = name == "profile" ? linked.ProfilePath : Path.Combine(Path.GetDirectoryName(linked.ProfilePath)!, name == "lock" ? "profile.lock" : "profile.json.bak");
                    File.WriteAllText(target, "untouched target");
                    File.Delete(link); File.CreateSymbolicLink(link, target);
                }
                else
                {
                    link = name switch { "snapshot" => linked.Snapshot, "backup" => linked.Backups, _ => Path.GetDirectoryName(linked.Root)! };
                    if (Directory.Exists(link)) Directory.Move(link, target); else Directory.CreateDirectory(target);
                    Directory.CreateSymbolicLink(link, target);
                }
                bool refused = false;
                try { await linked.Open(); } catch (InvalidDataException) { refused = true; }
                check(refused && (name == "profile" ? File.ReadAllText(target) == "untouched target" : File.ReadAllBytes(linked.ProfilePath).SequenceEqual(original)),
                    "linked " + name + " is refused without following it for a Warbond write");
            }
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: Warbond symlink fixtures require Windows privileges; run on Linux."); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314) { Console.WriteLine("SKIP: Warbond symlink fixtures require Windows privileges; run on Linux."); }
    }
}
