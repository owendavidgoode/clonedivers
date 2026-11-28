using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clonedivers;

namespace Clonedivers.Tests;

internal static class TurnkeySetupTests
{
    internal static async Task Run(string area, Action<bool, string> check)
    {
        var root = Path.Combine(area, "turnkey", "Forge");
        Directory.CreateDirectory(Path.Combine(root, "app", "cli")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "HellForge.exe"), "fixture"); File.WriteAllText(Path.Combine(root, "app", "cli", "HellForge.Cli.exe"), "fixture");
        var marker = Path.Combine(root, "hellforge-install.json");
        var layoutCalls = 0;
        Task<JsonElement> Create(IReadOnlyList<string> args, CancellationToken token)
        {
            check(args.SequenceEqual(new[] { "create-install", "--root", root }), "unmarked payload uses the supported create-install contract");
            layoutCalls++; File.WriteAllText(marker, "{\"schemaVersion\":1,\"buildId\":\"25480438\"}"); return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
        }
        await HellForgeSetup.CreateLayoutAsync(root, default, Create); await HellForgeSetup.CreateLayoutAsync(root, default, Create);
        check(layoutCalls == 1, "resuming an already marked unpack never recreates its layout or profiles");
        var fake = new Commands(root);
        HellForgeClient Client() => new(root, fake) { RequireCliQuiescent = () => { } };
        int downloads = 0, settings = 0, spaces = 0, copies = 0;
        var localSource = "none";
        var localManifest = Path.Combine(area, "Steam", "steamapps", "appmanifest_553850.acf");
        Task<JsonElement> Run(IReadOnlyList<string> args, IProgress<ForgeSetupProgress>? report, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (args[0] == "discover") return Task.FromResult(JsonSerializer.SerializeToElement(localSource == "none" ? Array.Empty<string>() : new[] { localManifest }));
            if (args[0] == "inspect")
            {
                if (localSource == "unreadable") throw new IOException("local copy unavailable");
                return Task.FromResult(JsonSerializer.SerializeToElement(new { pin = new { buildId = localSource == "supported" ? HellForgePack.SupportedBuild : "newer" } }));
            }
            if (args[0] == "settings") { settings++; fake.Sandbox = true; return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true })); }
            if (args[0] == "install-game")
            {
                copies++; fake.Ready = true;
                check(args.SequenceEqual(new[] { "install-game", "--manifest", localManifest, "--destination", Path.Combine(root, "game") }), "a supported local build uses the official read-only Steam-copy contract");
                return Task.FromResult(JsonSerializer.SerializeToElement(new { buildId = HellForgePack.SupportedBuild, destination = Path.Combine(root, "game") }));
            }
            check(args.SequenceEqual(new[] { "steam-download", "--out", Path.Combine(root, "game"), "--sign-in", "qr" }), "game download uses only the pinned CLI QR flow and separate game folder");
            downloads++; fake.Ready = true;
            return Task.FromResult(JsonSerializer.SerializeToElement(new { buildId = HellForgePack.SupportedBuild, destination = Path.Combine(root, "game") }));
        }
        fake.Sandbox = false;
        await HellForgeSetup.PrepareGameAsync(root, null, default, Client(), Run, _ => spaces++);
        check(downloads == 1 && settings == 1 && spaces == 1 && (await Client().StatusAsync()).CompatibilityProblem == null,
            "fresh game download enables sandbox and reaches readiness before first-launch network approval");
        await HellForgeSetup.PrepareGameAsync(root, null, default, Client(), Run, _ => spaces++);
        check(downloads == 1 && spaces == 1 && settings == 2, "ready game copies skip download and disk reservation on repeated setup");
        foreach (var reason in new[] { "active", "engine", "runner", "space", "cancel", "failure", "wrong-build", "wrong-destination", "incomplete" })
        {
            fake.Ready = false; fake.Running = reason == "active"; fake.Engine = reason == "engine" ? "unknown" : HellForgeRelease.Engine; fake.Runner = reason != "runner";
            var calls = 0;
            using var cancel = new CancellationTokenSource(); if (reason == "cancel") cancel.Cancel();
            Task<JsonElement> Refuse(IReadOnlyList<string> args, IProgress<ForgeSetupProgress>? report, CancellationToken token)
            {
                if (args[0] == "discover") return Task.FromResult(JsonSerializer.SerializeToElement(Array.Empty<string>()));
                calls++;
                if (reason == "failure") throw new IOException("lost Steam connection");
                fake.Ready = reason != "incomplete";
                return Task.FromResult(JsonSerializer.SerializeToElement(new { buildId = reason == "wrong-build" ? "wrong" : HellForgePack.SupportedBuild,
                    destination = reason == "wrong-destination" ? area : Path.Combine(root, "game") }));
            }
            try
            {
                await HellForgeSetup.PrepareGameAsync(root, null, cancel.Token, Client(), Refuse, _ => { if (reason == "space") throw new IOException("space fixture"); });
                check(false, "game setup must refuse " + reason);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException)
            {
                check(reason is "failure" or "wrong-build" or "wrong-destination" or "incomplete" ? calls == 1 : calls == 0,
                    "game setup handles " + reason + " without proceeding to a later mutation");
            }
        }
        fake.Engine = HellForgeRelease.Engine; fake.Runner = true; fake.Running = false; fake.Ready = false;
        await HellForgeSetup.PrepareGameAsync(root, null, default, Client(), Run, _ => { });
        check(fake.Ready && settings == 3, "a retry after download failure reuses the same destination and completes setup");
        foreach (var source in new[] { "supported", "newer", "unreadable" })
        {
            fake.Ready = false; localSource = source;
            var previousDownloads = downloads; var previousCopies = copies;
            await HellForgeSetup.PrepareGameAsync(root, null, default, Client(), Run, _ => { }, Path.Combine(area, "Steam"));
            check(fake.Ready && downloads == previousDownloads + (source == "supported" ? 0 : 1) && copies == previousCopies + (source == "supported" ? 1 : 0),
                source + " Steam build selects direct copy or the resumable QR download automatically");
        }
        await LinkTests(root, fake, Client(), check);
        foreach (var free in new[] { 1L << 30, 49L << 30 })
        {
            try { HellForgeSetup.CheckGameSpace(free, 0); check(false, "fresh download needs its space budget"); }
            catch (IOException) { check(true, "insufficient fresh-download space gives a recoverable failure"); }
        }
        HellForgeSetup.CheckGameSpace(3L << 30, 68L << 30);
        check(true, "resume accounts for retained game bytes while preserving a free-space reserve");
        var start = HellForgeSetup.SetupStartInfo(root, ["steam-download", "--out", Path.Combine(root, "game with spaces"), "--sign-in", "qr"]);
        check(!start.UseShellExecute && start.CreateNoWindow && start.StandardErrorEncoding?.CodePage == HellForgeSetup.TerminalEncoding.CodePage && start.ArgumentList.Count == 5,
            "setup uses the CLI's console encoding for QR output without a console or shell argument parsing");
        QrTests(check);
        await ProcessTests(check);
        await RepairTests(root, fake, Client(), check);
        await Step4WizardTests(area, check);
    }

    static async Task LinkTests(string root, Commands fake, HellForgeClient client, Action<bool, string> check)
    {
        var game = Path.Combine(root, "game");
        var nested = Path.Combine(game, "hidden", "nested"); Directory.CreateDirectory(nested);
        File.SetAttributes(Path.Combine(game, "hidden"), FileAttributes.Directory | FileAttributes.Hidden);
        var outside = Path.Combine(Path.GetDirectoryName(root)!, "outside game"); Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.dat"); File.WriteAllText(sentinel, "must survive setup");
        var missing = Path.Combine(outside, "missing.dat");
        foreach (var kind in new[] { "junction", "partial", "dangling partial" })
        {
            var link = Path.Combine(nested, kind == "junction" ? "data" : "game.dll.partial");
            bool CreateLink()
            {
                if (kind == "junction")
                {
                    var script = "$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + outside.Replace("'", "''") + "' | Out-Null";
                    var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
                    start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
                    start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
                    using var process = Process.Start(start)!; process.WaitForExit();
                    check(process.ExitCode == 0, "game destination junction fixture is created");
                    if (process.ExitCode != 0) throw new IOException("Could not create the game junction fixture.");
                    return true;
                }
                try { File.CreateSymbolicLink(link, kind == "partial" ? sentinel : missing); return true; }
                catch (Exception e) when (e is UnauthorizedAccessException || e is IOException && (e.HResult & 0xffff) is 5 or 1314)
                { Console.WriteLine("  SKIP  " + kind + " fixture requires Windows symbolic-link privileges: " + e.Message); return false; }
            }
            void RemoveLink() { if (kind == "junction") Directory.Delete(link); else File.Delete(link); }
            if (!CreateLink()) continue;
            RemoveLink();
            foreach (var local in new[] { true, false })
                foreach (var late in new[] { false, true })
                {
                    fake.Ready = false; var mutations = 0; var linked = false; var spaces = 0;
                    Task<JsonElement> Run(IReadOnlyList<string> args, IProgress<ForgeSetupProgress>? progress, CancellationToken ct)
                    {
                        if (args[0] == "discover")
                        {
                            if (late) linked = CreateLink();
                            return Task.FromResult(JsonSerializer.SerializeToElement(local ? new[] { "fixture manifest" } : Array.Empty<string>()));
                        }
                        if (args[0] == "inspect") return Task.FromResult(JsonSerializer.SerializeToElement(new { pin = new { buildId = HellForgePack.SupportedBuild } }));
                        mutations++; throw new IOException("Setup must reject links before invoking " + args[0]);
                    }
                    try
                    {
                        if (!late) linked = CreateLink();
                        await HellForgeSetup.PrepareGameAsync(root, null, default, client, Run, _ => spaces++, "fixture Steam root");
                        check(false, "game setup must refuse " + kind);
                    }
                    catch (InvalidDataException e)
                    {
                        check(linked && mutations == 0 && spaces == (late ? 1 : 0) && e.Message.Contains(link) && e.Message.Contains("Remove the link"),
                            (local ? "install-game" : "steam-download") + " refuses " + kind + (late ? " created during discovery" : " before disk inspection"));
                    }
                    finally { if (linked) RemoveLink(); }
                    check(File.ReadAllText(sentinel) == "must survive setup" && !File.Exists(missing), "refused destination links leave outside and dangling targets untouched");
                }
        }
        fake.Ready = true;
    }

    static void QrTests(Action<bool, string> check)
    {
        var reports = new List<ForgeSetupProgress>(); var parser = new SteamSetupOutput(new Reports(reports.Add));
        // Each half-block variant at row 4, inside a four-module quiet zone.
        var rows = Enumerable.Repeat(new string('\u2588', 29), 15).ToArray();
        rows[2] = new string('\u2588', 4) + " \u2580\u2584\u2588" + new string('\u2588', 21);
        foreach (var row in rows) parser.Line("\u001b[97;40m" + row + "\u001b[0m");
        parser.Line("Open the Steam app on your phone, tap the Steam Guard shield, then the QR scanner, and scan this code.");
        check(reports.Count == 1 && reports[0].QrRows!.SequenceEqual(rows), "pinned CLI ANSI half-block payload becomes one in-app QR approval");
        using var qr = SteamSetupOutput.RenderQr(rows);
        var scale = qr.Width / 29;
        check(qr.GetPixel(4 * scale, 4 * scale).ToArgb() == Color.Black.ToArgb() && qr.GetPixel(5 * scale, 4 * scale).ToArgb() == Color.White.ToArgb() &&
            qr.GetPixel(5 * scale, 5 * scale).ToArgb() == Color.Black.ToArgb() && qr.GetPixel(6 * scale, 4 * scale).ToArgb() == Color.Black.ToArgb() &&
            qr.GetPixel(6 * scale, 5 * scale).ToArgb() == Color.White.ToArgb() && qr.GetPixel(0, 0).ToArgb() == Color.White.ToArgb(),
            "QR reconstruction preserves inversion, both half-blocks and the white quiet zone at integer scale");
        parser.Line("Signed in."); parser.Line("45%: 10 of 22 MB (2 MB downloaded, 8 MB copied from Steam's install)");
        check(reports[^2].Message == "Signed in." && reports[^1].Done == 45 && reports[^1].Total == 100 && reports[^1].CanCancel,
            "sign-in completion clears approval and download output supplies pausable percentage progress");
        parser.Line("https://s.team/q/secret"); parser.Line("\u001b[invalid");
        check(!parser.Failure.Contains("secret") && reports.Count == 3, "QR URLs and escape sequences never enter setup diagnostics");
        try { var bad = new SteamSetupOutput(null); bad.Line("\u001b[97;40m" + rows[0] + "\u001b[0m"); bad.Line("next"); check(false, "partial QR must fail"); }
        catch (InvalidDataException) { check(true, "an incomplete QR requests retry instead of displaying an unscannable challenge"); }
    }

    static async Task ProcessTests(Action<bool, string> check)
    {
        var reports = new List<ForgeSetupProgress>();
        var start = HellForgeTests.PipeFixtureStart("--turnkey-stream"); start.StandardErrorEncoding = HellForgeSetup.TerminalEncoding;
        var json = await HellForgeSetup.ReadSetupProcessAsync(start, new Reports(reports.Add), default);
        check(json.GetProperty("ok").GetBoolean() && reports.Any(p => p.Done == 50) && reports.Any(p => p.QrRows is { Length: 15 }), "real fake-CLI pipes preserve terminal QR modules and stream progress with the final stdout acknowledgement");
        try { await HellForgeSetup.ReadSetupProcessAsync(HellForgeTests.PipeFixtureStart("--hellforge-pipe-error"), null, default); check(false, "failed setup process must fail"); }
        catch (IOException error) { check(error.Message.Contains("fixture failure") && error.Message.Contains("retry"), "nonzero setup exit retains a bounded diagnostic and recovery instructions"); }
        using var cancel = new CancellationTokenSource();
        int pid = 0;
        var cancelled = HellForgeSetup.ReadSetupProcessAsync(HellForgeTests.PipeFixtureStart("--turnkey-wait"), new Reports(p => { if (int.TryParse(p.Message, out pid)) cancel.Cancel(); }), cancel.Token);
        try { await cancelled; check(false, "paused setup must cancel"); }
        catch (OperationCanceledException)
        {
            bool exited; try { using var process = Process.GetProcessById(pid); exited = process.HasExited; } catch (ArgumentException) { exited = true; }
            check(pid > 0 && exited, "pause confirms the owned CLI has exited before a download can be resumed");
        }
        foreach (var command in new[] { "install-game", "steam-download" })
        {
            check(HellForgeSetup.SetupCommandTimeout(command) == TimeSpan.FromHours(24), command + " gets a full-day budget for copy, hashing and resume");
            var copyReports = new List<ForgeSetupProgress>();
            var copy = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            copy.ArgumentList.Add("-NoProfile"); copy.ArgumentList.Add("-NonInteractive"); copy.ArgumentList.Add("-EncodedCommand");
            copy.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::Error.WriteLine('Copied 55% (12345 of 22445 MB, 180 s)'); [Console]::Out.WriteLine('{\"ok\":true}')")));
            var copied = await HellForgeSetup.RunSetupProcessAsync(command, copy, new Reports(copyReports.Add), default);
            check(copied.GetProperty("ok").GetBoolean() && copyReports is [{ Done: 55, Total: 100, CanCancel: true }],
                command + " streams pinned install-game copy progress and reads the fake CLI acknowledgement");
            using var pause = new CancellationTokenSource(); int copyPid = 0;
            try
            {
                await HellForgeSetup.RunSetupProcessAsync(command, HellForgeTests.PipeFixtureStart("--turnkey-wait"),
                    new Reports(p => { if (int.TryParse(p.Message, out copyPid)) pause.Cancel(); }), pause.Token);
                check(false, command + " must remain cancellable within its long budget");
            }
            catch (OperationCanceledException)
            {
                bool exited; try { using var process = Process.GetProcessById(copyPid); exited = process.HasExited; } catch (ArgumentException) { exited = true; }
                check(copyPid > 0 && exited, command + " pause stops its owned fake CLI despite the long timeout budget");
            }
        }
        check(HellForgeSetup.SetupCommandTimeout("inspect") == TimeSpan.FromMinutes(2), "short setup commands retain their bounded two-minute budget");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { await HellForgeSetup.ReadSetupProcessAsync(HellForgeTests.PipeFixtureStart("--hellforge-preview-errors"), null, limit.Token); check(false, "oversized streaming output must fail"); }
        catch (InvalidDataException) { check(true, "oversized streaming lines terminate the owned CLI without a blocked pipe"); }
    }

    static async Task RepairTests(string root, Commands fake, HellForgeClient client, Action<bool, string> check)
    {
        var file = Path.Combine(root, "mods", "hellforge-mods.json");
        foreach (var issue in new[] { "difficulty", "options", "order", "sharing", "overlap" })
        {
            fake.Mods.Clear();
            foreach (var id in HellForgeGameplayCatalog.Pinned.Order)
            {
                var required = HellForgePreset.Required.FirstOrDefault(r => r.Id == id);
                var artifact = HellForgeGameplayCatalog.Pinned.Artifacts.FirstOrDefault(a => a.Id == id);
                fake.Mods.Add(new(id, required.Name ?? artifact?.Name ?? "Vanilla Plus", true, Path.Combine(root, "mods", id), [], required.Version ?? artifact?.Version ?? ""));
            }
            var dogs = fake.Mods.FindIndex(m => m.Id == HellForgePreset.GuardDogs);
            fake.Mods[dogs] = fake.Mods[dogs] with { Options = [new("Smarter Guard Dogs and Sentries", true), new("Guard Dogs", true), new("Sentries", true), new("Intelligence", true), new("Safety", true), new("Sentry Safety", issue == "options"), new("SEAF Safety", false), new("Targeting Laser", issue == "options"), new("Targeting Marker", issue == "options")] };
            if (issue == "difficulty") { var index = fake.Mods.FindIndex(m => m.Id == HellForgeClient.DifficultyId); fake.Mods[index] = fake.Mods[index] with { Enabled = false }; }
            if (issue == "order") fake.Mods.Reverse();
            if (issue == "overlap") fake.Mods.Add(new("pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91", "Reinforcement Beacons Fixed", true, Path.Combine(root, "mods", "beacons"), []));
            File.WriteAllText(file, JsonSerializer.Serialize(new { format = 1, share = issue == "sharing", unrelated = "keep" }));
            check(HellForgePreset.Problem(await client.ModsAsync(), checkOrder: true) != null, issue + " drift is detected as a repairable setup state");
            fake.Calls.Clear(); await client.InstallGameplayAsync(client.GameplayCache);
            check(HellForgePreset.Problem(await client.ModsAsync(), checkOrder: true) == null && fake.Calls.All(c => c[0] != "mod-add" && c[0] != "play") && File.ReadAllText(file).Contains("keep"),
                issue + " repairs through the existing transaction and CLI without reimporting verified mods or launching");
            fake.Calls.Clear(); await client.InstallGameplayAsync(client.GameplayCache);
            check(fake.Calls.All(c => c[0] is "status" or "mods" or "mod-hash"), issue + " repair is idempotent on retry");
        }
        await OptionalTests(root, fake, client, check);
    }

    static async Task OptionalTests(string root, Commands fake, HellForgeClient client, Action<bool, string> check)
    {
        var catalog = HellForgeGameplayCatalog.Pinned;
        var optionalSpecs = catalog.Artifacts.Select((artifact, index) => (artifact, index)).Where(x => x.artifact.Optional).ToArray();
        Directory.CreateDirectory(client.GameplayCache);
        for (int i = 0; i < optionalSpecs.Length; i++)
        {
            var (pinned, index) = optionalSpecs[i];
            var fixture = Path.Combine(client.GameplayCache, $"optional-fixture-{i}.zip");
            using (var zip = ZipFile.Open(fixture, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("hellforge-mod.json").Open()))
                writer.Write(JsonSerializer.Serialize(new { id = pinned.Id, name = pinned.Name, version = pinned.Version, gameplay = false, coop = "per-player" }));
            var artifact = pinned with { Bytes = new FileInfo(fixture).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture))).ToLowerInvariant() };
            File.Move(fixture, Path.Combine(client.GameplayCache, artifact.FileName));
            catalog.Artifacts[index] = artifact; // isolated import fixture; production pins are restored below
        }
        var record = client.PackIntentPath + ".gameplay.json";
        var backup = Path.Combine(client.CacheRoot, "backups", "gameplay-test");
        Directory.CreateDirectory(backup);
        void WriteRecord(string catalogId, string state = "applied", bool? defaultsApplied = null, string[]? optionalDefaults = null)
        {
            var payload = new Dictionary<string, object?> { ["Root"] = client.Root, ["Catalog"] = catalogId, ["Backup"] = backup, ["SnapshotSha256"] = new string('a', 64), ["State"] = state };
            if (defaultsApplied is { } applied) payload["OptionalDefaultsApplied"] = applied;
            if (optionalDefaults != null) payload["OptionalDefaults"] = optionalDefaults;
            File.WriteAllText(record, JsonSerializer.Serialize(payload));
        }
        try
        {
            var cleanExit = catalog.Artifacts.Single(a => a.Id == HellForgePreset.CleanExit);
            var leafWriter = catalog.Artifacts.Single(a => a.Id == HellForgePreset.LegoCompactLeafWriter);
            var yoda = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireLegoYoda);
            var atst = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireAtstWriter);
            var tieSwitch = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireTieSwitch);
            var cameraPullback = catalog.Artifacts.Single(a => a.Id == HellForgePreset.EmpireCameraPullback);
            var swLoc = catalog.Artifacts.Single(a => a.Id == HellForgePreset.StarWarsLocalization);
            var dedicatedMelee = catalog.Artifacts.Single(a => a.Id == HellForgePreset.DedicatedMeleeSlot);
            var chaos = catalog.Artifacts.Single(a => a.Id == HellForgePreset.ChaosInvasions);
            var newSinceR3 = HellForgePreset.Optional.Where(id => id != cleanExit.Id).ToHashSet(StringComparer.Ordinal);
            var newInR5 = new[] { atst.Id, tieSwitch.Id, cameraPullback.Id, swLoc.Id, dedicatedMelee.Id }.ToHashSet(StringComparer.Ordinal);
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id) || HellForgePreset.IsConflictingAlternative(m));
            foreach (var oldCatalog in new[] { "build25480438-r1", "build25480438-r2" })
            {
                WriteRecord(oldCatalog);
                check((await client.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(HellForgePreset.Optional),
                    "an " + oldCatalog + " installation offers all six optional defaults once");
            }
            fake.Ready = true;
            check((await client.PlayRegularAsync()).GetProperty("event").GetString() == "started", "optional mods are absent from launch requirements before first r5 setup");
            fake.Calls.Clear(); await client.InstallGameplayAsync(client.GameplayCache);
            check(HellForgePreset.Optional.All(id => fake.Mods.Single(m => m.Id == id).Enabled) && fake.Calls.Count(c => c[0] == "mod-add") == HellForgePreset.Optional.Length &&
                HellForgePreset.Problem(await client.ModsAsync(), checkOrder: true) == null, "r5 setup imports, verifies, enables and orders all six optional mods by default");
            var listed = await client.ModsAsync();
            var clientLast = listed with { Mods = [.. listed.Mods.Where(m => !HellForgePreset.Optional.Contains(m.Id)), .. listed.Mods.Where(m => HellForgePreset.Optional.Contains(m.Id)).Reverse()] };
            check(HellForgePreset.Problem(clientLast, checkOrder: true) == null,
                "the real CLI's listing, which puts client-only optional mods after every gameplay mod in any relative order, satisfies the pinned order");
            foreach (var id in HellForgePreset.Optional)
            {
                var optional = fake.Mods.FindIndex(m => m.Id == id);
                fake.Mods[optional] = fake.Mods[optional] with { Enabled = false, Version = "player-version" };
            }
            var difficulty = fake.Mods.FindIndex(m => m.Id == HellForgeClient.DifficultyId);
            fake.Mods[difficulty] = fake.Mods[difficulty] with { Enabled = false };
            fake.Calls.Clear(); await client.InstallGameplayAsync(client.GameplayCache);
            check(HellForgePreset.Optional.All(id => !fake.Mods.Single(m => m.Id == id).Enabled) &&
                fake.Calls.All(c => c[0] != "mod-add" && !(c[0] == "mod-switch" && HellForgePreset.Optional.Contains(c[2]))),
                "required-mod repair preserves disabled or player-replaced optional mods");
            fake.Mods.RemoveAll(m => m.Id == yoda.Id || m.Id == chaos.Id);
            var restarted = new HellForgeClient(root, fake) { RequireCliQuiescent = () => { } };
            check((await restarted.MissingGameplayAsync()).Count == 0 && HellForgePreset.Problem(await restarted.ModsAsync(), checkOrder: true) == null,
                "removing optional gameplay and client mods while keeping disabled optionals survives a fresh client and leaves the required preset ready");
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            check((await restarted.MissingGameplayAsync()).Count == 0 && HellForgePreset.Problem(await restarted.ModsAsync(), checkOrder: true) == null,
                "removing all six optional mods survives a fresh client and leaves the required preset ready");
            fake.Ready = true;
            check((await restarted.PlayRegularAsync()).GetProperty("event").GetString() == "started", "optional removal still reaches the normal verified launch entry point");
            fake.Calls.Clear(); await restarted.InstallGameplayAsync(restarted.GameplayCache);
            check(fake.Calls.All(c => c[0] is "status" or "mods" or "mod-hash"), "repeated setup after optional removal is a no-op");
            fake.Mods.Reverse();
            check(HellForgePreset.Problem(await restarted.ModsAsync(), checkOrder: true)?.Contains("order") == true,
                "missing optional mods do not hide required mod-order drift");
            fake.Calls.Clear(); await restarted.InstallGameplayAsync(restarted.GameplayCache);
            check(HellForgePreset.Optional.All(id => fake.Mods.All(m => m.Id != id)) &&
                fake.Calls.All(c => c[0] != "mod-add" && !(c[0] == "mod-order" && HellForgePreset.Optional.Any(id => c[2].Split(',').Contains(id)))) &&
                HellForgePreset.Problem(await restarted.ModsAsync(), checkOrder: true) == null, "order repair excludes removed optional IDs and keeps them removed");
            difficulty = fake.Mods.FindIndex(m => m.Id == HellForgeClient.DifficultyId);
            fake.Mods[difficulty] = fake.Mods[difficulty] with { Enabled = false };
            fake.FailPackOnce = true;
            try { await restarted.InstallGameplayAsync(restarted.GameplayCache); check(false, "repair failure must restore the previous selection"); }
            catch (IOException error) { check(error.Message.Contains("previous mod selection was restored"), "required-mod repair failure rolls back through the normal transaction"); }
            check(restarted.InterruptedGameplayProblem() == null && (await restarted.MissingGameplayAsync()).Count == 0 &&
                HellForgePreset.Optional.All(id => fake.Mods.All(m => m.Id != id)),
                "repair rollback retains the completed optional-default choices");
            fake.Calls.Clear(); await restarted.InstallGameplayAsync(restarted.GameplayCache);
            check(fake.Calls.All(c => c[0] != "mod-add") && HellForgePreset.Optional.All(id => fake.Mods.All(m => m.Id != id)),
                "retry after rollback keeps removed optional mods removed");

            // Upgrading from r3 preserves prior Clean Exit choices while offering the five new optional mods once.
            WriteRecord("build25480438-r3");
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(newSinceR3),
                "an r3 installation with removed Clean Exit offers only the five newly added optional defaults");
            fake.FailPackOnce = true;
            try { await restarted.InstallGameplayAsync(restarted.GameplayCache); check(false, "failed r3->r5 upgrade must roll back"); }
            catch (IOException error) { check(error.Message.Contains("previous mod selection was restored"), "failed r3->r5 upgrade rolls back newly imported optional mods"); }
            check(fake.Mods.All(m => !HellForgePreset.Optional.Contains(m.Id)) &&
                (await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(newSinceR3),
                "r3->r5 upgrade rollback keeps removed Clean Exit removed and still offers the five new optionals on retry");
            fake.Calls.Clear(); await restarted.InstallGameplayAsync(restarted.GameplayCache);
            check(newSinceR3.All(id => fake.Mods.Single(m => m.Id == id).Enabled) && fake.Mods.All(m => m.Id != cleanExit.Id) &&
                fake.Calls.Count(c => c[0] == "mod-add") == newSinceR3.Count,
                "r3->r5 upgrade installs and enables all five post-r3 optional mods while preserving removed Clean Exit");

            // Upgrading from r4 upgrades enabled 0.0.9/0.1.2/0.2.0 defaults to 0.1.0/0.2.0/0.3.0 and offers AT-ST + TIE Switch while preserving disabled/removed optionals.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            string MakeOldDir(GameplayArtifact item, string ver)
            {
                var dir = Path.Combine(root, "mods", item.Id + "-" + ver);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "hellforge-mod.json"), JsonSerializer.Serialize(new { id = item.Id, name = item.Name, version = ver, gameplay = item.Id == chaos.Id, coop = "per-player" }));
                return dir;
            }
            fake.Mods.Add(new(leafWriter.Id, leafWriter.Name, true, MakeOldDir(leafWriter, "0.0.9"), ["code"], "0.0.9"));
            fake.Mods.Add(new(yoda.Id, yoda.Name, false, MakeOldDir(yoda, "0.1.2"), ["code"], "0.1.2"));
            fake.Mods.Add(new(chaos.Id, chaos.Name, true, MakeOldDir(chaos, "0.2.0"), ["gameplay", "code"], "0.2.0"));
            WriteRecord("build25480438-r4", "applied", true, [cleanExit.Id, leafWriter.Id, yoda.Id, chaos.Id]);
            var expectedR4Upgrade = new[] { leafWriter.Id, chaos.Id, atst.Id, tieSwitch.Id, cameraPullback.Id, swLoc.Id, dedicatedMelee.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR4Upgrade),
                "an r4 installation upgrades enabled 0.0.9/0.2.0 optionals and offers AT-ST, TIE Switch, Camera Pullback, Star Wars Localization, and Dedicated Melee while keeping disabled Yoda 0.1.2 and removed Clean Exit opted out");
            // Simulate an interrupted r4->r8 upgrade (lost import acknowledgement) followed by RecoverGameplayAsync() and a fresh r8 retry.
            fake.LoseFirstModAdd = true;
            try { await restarted.InstallGameplayAsync(restarted.GameplayCache); check(false, "lost r4->r8 acknowledgement must throw"); }
            catch (HellForgeCommandOutcomeUnknownException) { }
            check(restarted.InterruptedGameplayProblem() != null, "interrupted r4->r8 upgrade leaves recovery pending");
            await new HellForgeClient(root, fake) { RequireCliQuiescent = () => { } }.RecoverGameplayAsync();
            var postRecovery = new HellForgeClient(root, fake) { RequireCliQuiescent = () => { } };
            check(postRecovery.InterruptedGameplayProblem() == null &&
                fake.Mods.Single(m => m.Id == leafWriter.Id) is { Enabled: true, Version: "0.0.9" } &&
                fake.Mods.Single(m => m.Id == chaos.Id) is { Enabled: true, Version: "0.2.0" } &&
                (await postRecovery.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR4Upgrade),
                "RecoverGameplayAsync after an interrupted r4->r8 upgrade restores 0.0.9/0.2.0 and still offers the enabled retired-default upgrades on retry");
            fake.Calls.Clear(); await postRecovery.InstallGameplayAsync(postRecovery.GameplayCache);
            check(fake.Mods.Single(m => m.Id == leafWriter.Id) is { Enabled: true, Version: "0.2.4" } &&
                fake.Mods.Single(m => m.Id == chaos.Id) is { Enabled: true, Version: "0.5.2" } &&
                fake.Mods.Single(m => m.Id == atst.Id) is { Enabled: true, Version: "0.2.6" } &&
                newInR5.All(id => fake.Mods.Single(m => m.Id == id).Enabled) &&
                fake.Mods.Single(m => m.Id == yoda.Id) is { Enabled: false, Version: "0.1.2" } &&
                fake.Mods.All(m => m.Id != cleanExit.Id) &&
                fake.Calls.Count(c => c[0] == "mod-add") == expectedR4Upgrade.Count,
                "r4->r16 retry after RecoverGameplayAsync imports 0.2.4/0.2.6/0.5.2 upgrades and new AT-ST/TIE Switch/Camera Pullback/Star Wars Localization/Dedicated Melee defaults while leaving disabled Yoda 0.1.2 and removed Clean Exit untouched");

            // Upgrading from r5 upgrades enabled 0.1.0 LEGO leaf writer and AT-ST writer while offering Camera Pullback + Star Wars Localization + Dedicated Melee and preserving disabled/removed optionals.
            var r5OptionalIds = new[] { cleanExit.Id, leafWriter.Id, yoda.Id, atst.Id, tieSwitch.Id, chaos.Id };
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(leafWriter.Id, leafWriter.Name, true, MakeOldDir(leafWriter, "0.1.0"), ["code"], "0.1.0"));
            fake.Mods.Add(new(atst.Id, atst.Name, true, MakeOldDir(atst, "0.1.0"), ["code"], "0.1.0"));
            fake.Mods.Add(new(yoda.Id, yoda.Name, false, MakeOldDir(yoda, "0.2.0"), ["code"], "0.2.0"));
            WriteRecord("build25480438-r5", "applied", true, r5OptionalIds);
            var expectedR5Upgrade = new[] { leafWriter.Id, atst.Id, cameraPullback.Id, swLoc.Id, dedicatedMelee.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR5Upgrade),
                "an r5 installation upgrades enabled 0.1.0 LEGO leaf writer and AT-ST writer and offers Camera Pullback + Star Wars Localization + Dedicated Melee while keeping disabled/removed optionals opted out");

            // Upgrading from r6 upgrades enabled 0.2.0 LEGO leaf writer and AT-ST writer while offering Camera Pullback + Star Wars Localization + Dedicated Melee and preserving disabled/removed optionals.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(leafWriter.Id, leafWriter.Name, true, MakeOldDir(leafWriter, "0.2.0"), ["code"], "0.2.0"));
            fake.Mods.Add(new(atst.Id, atst.Name, true, MakeOldDir(atst, "0.2.0"), ["code"], "0.2.0"));
            fake.Mods.Add(new(yoda.Id, yoda.Name, false, MakeOldDir(yoda, "0.2.0"), ["code"], "0.2.0"));
            WriteRecord("build25480438-r6", "applied", true, r5OptionalIds);
            var expectedR6Upgrade = new[] { leafWriter.Id, atst.Id, cameraPullback.Id, swLoc.Id, dedicatedMelee.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR6Upgrade),
                "an r6 installation upgrades enabled 0.2.0 LEGO leaf writer and AT-ST writer and offers Camera Pullback + Star Wars Localization + Dedicated Melee while keeping disabled/removed optionals opted out");

            // Upgrading from r7 upgrades enabled AT-ST 0.2.1 -> 0.2.2 and Chaos 0.3.0 -> 0.3.4 while offering Camera Pullback + Star Wars Localization + Dedicated Melee and preserving disabled/removed optionals.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(atst.Id, atst.Name, true, MakeOldDir(atst, "0.2.1"), ["code"], "0.2.1"));
            fake.Mods.Add(new(chaos.Id, chaos.Name, true, MakeOldDir(chaos, "0.3.0"), ["gameplay", "code"], "0.3.0"));
            fake.Mods.Add(new(yoda.Id, yoda.Name, false, MakeOldDir(yoda, "0.2.0"), ["code"], "0.2.0"));
            WriteRecord("build25480438-r7", "applied", true, r5OptionalIds);
            var expectedR7Upgrade = new[] { atst.Id, chaos.Id, cameraPullback.Id, swLoc.Id, dedicatedMelee.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR7Upgrade),
                "an r7 installation upgrades enabled AT-ST 0.2.1 and Chaos 0.3.0 and offers Camera Pullback + Star Wars Localization + Dedicated Melee while keeping disabled/removed optionals opted out");

            // Upgrading from r8 (v2.1.0/v2.1.1) must upgrade enabled Chaos 0.3.3 -> 0.3.5 and Camera Pullback 0.1.2 -> 0.1.4 and offer Star Wars Localization while respecting disabled optionals.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(chaos.Id, chaos.Name, true, MakeOldDir(chaos, "0.3.3"), ["code"], "0.3.3"));
            fake.Mods.Add(new(cameraPullback.Id, cameraPullback.Name, true, MakeOldDir(cameraPullback, "0.1.2"), ["code"], "0.1.2"));
            fake.Mods.Add(new(dedicatedMelee.Id, dedicatedMelee.Name, false, MakeOldDir(dedicatedMelee, "1.5.2-clonedivers.2"), ["code"], "1.5.2-clonedivers.2"));
            WriteRecord("build25480438-r8", "applied", true, null);
            var expectedR8Upgrade = new[] { chaos.Id, cameraPullback.Id, swLoc.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR8Upgrade),
                "an r8 (v2.1.0/v2.1.1) installation upgrades enabled Chaos 0.3.3 and Camera Pullback 0.1.2 and offers Star Wars Localization while keeping disabled/removed optionals opted out");

            // Upgrading from r9 (v2.1.2) must upgrade all 5 enabled r9->r10 optionals (0.2.1->0.2.2, 0.2.0->0.2.1, 0.2.2->0.2.3, 0.1.3->0.1.4, 0.3.4->0.3.5) while keeping disabled/removed optionals opted out.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(leafWriter.Id, leafWriter.Name, true, MakeOldDir(leafWriter, "0.2.1"), ["code"], "0.2.1"));
            fake.Mods.Add(new(yoda.Id, yoda.Name, true, MakeOldDir(yoda, "0.2.0"), ["code"], "0.2.0"));
            fake.Mods.Add(new(atst.Id, atst.Name, true, MakeOldDir(atst, "0.2.2"), ["code"], "0.2.2"));
            fake.Mods.Add(new(cameraPullback.Id, cameraPullback.Name, true, MakeOldDir(cameraPullback, "0.1.3"), ["code"], "0.1.3"));
            fake.Mods.Add(new(chaos.Id, chaos.Name, true, MakeOldDir(chaos, "0.3.4"), ["code"], "0.3.4"));
            fake.Mods.Add(new(dedicatedMelee.Id, dedicatedMelee.Name, false, MakeOldDir(dedicatedMelee, "1.5.2-clonedivers.2"), ["code"], "1.5.2-clonedivers.2"));
            WriteRecord("build25480438-r9", "applied", true, null);
            var expectedR9Upgrade = new[] { leafWriter.Id, yoda.Id, atst.Id, cameraPullback.Id, chaos.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR9Upgrade),
                "an r9 (v2.1.2) installation upgrades all 5 enabled r10 code mods while keeping disabled/removed optionals opted out");

            // Upgrading from r10 (v2.1.3) must upgrade enabled Clean Exit 0.2.0 -> 0.2.1 and Star Wars Localization 0.1.0 -> 0.1.1 while keeping disabled/removed optionals opted out.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(cleanExit.Id, cleanExit.Name, true, MakeOldDir(cleanExit, "0.2.0"), ["code"], "0.2.0"));
            fake.Mods.Add(new(swLoc.Id, swLoc.Name, true, MakeOldDir(swLoc, "0.1.0"), ["code"], "0.1.0"));
            fake.Mods.Add(new(dedicatedMelee.Id, dedicatedMelee.Name, false, MakeOldDir(dedicatedMelee, "1.5.2-clonedivers.2"), ["code"], "1.5.2-clonedivers.2"));
            WriteRecord("build25480438-r10", "applied", true, null);
            var expectedR10Upgrade = new[] { cleanExit.Id, swLoc.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR10Upgrade),
                "an r10 (v2.1.3) installation upgrades enabled Clean Exit 0.2.0 -> 0.2.1 and Star Wars Localization 0.1.0 -> 0.1.1 while keeping disabled/removed optionals opted out");

            // Upgrading from r11 (v2.1.4) must upgrade enabled Empire TIE Switch 0.3.0 -> 0.3.2 while keeping disabled/removed optionals opted out.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(tieSwitch.Id, tieSwitch.Name, true, MakeOldDir(tieSwitch, "0.3.0"), ["code"], "0.3.0"));
            fake.Mods.Add(new(dedicatedMelee.Id, dedicatedMelee.Name, false, MakeOldDir(dedicatedMelee, "1.5.2-clonedivers.2"), ["code"], "1.5.2-clonedivers.2"));
            WriteRecord("build25480438-r11", "applied", true, null);
            var expectedR11Upgrade = new[] { tieSwitch.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR11Upgrade),
                "an r11 (v2.1.4) installation upgrades enabled Empire TIE Switch 0.3.0 -> 0.3.2 while keeping disabled/removed optionals opted out");

            // Upgrading from r12 (v2.1.5) must upgrade all 6 enabled r12->r13 optionals (0.2.2->0.2.3, 0.2.1->0.2.2, 0.2.3->0.2.4, 0.3.1->0.3.2, 1.5.2-clonedivers.2->1.5.2-clonedivers.3, 0.3.5->0.4.0) while keeping disabled/removed optionals opted out.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(leafWriter.Id, leafWriter.Name, true, MakeOldDir(leafWriter, "0.2.2"), ["code"], "0.2.2"));
            fake.Mods.Add(new(yoda.Id, yoda.Name, true, MakeOldDir(yoda, "0.2.1"), ["code"], "0.2.1"));
            fake.Mods.Add(new(atst.Id, atst.Name, true, MakeOldDir(atst, "0.2.3"), ["code"], "0.2.3"));
            fake.Mods.Add(new(tieSwitch.Id, tieSwitch.Name, true, MakeOldDir(tieSwitch, "0.3.1"), ["code"], "0.3.1"));
            fake.Mods.Add(new(dedicatedMelee.Id, dedicatedMelee.Name, true, MakeOldDir(dedicatedMelee, "1.5.2-clonedivers.2"), ["code"], "1.5.2-clonedivers.2"));
            fake.Mods.Add(new(chaos.Id, chaos.Name, true, MakeOldDir(chaos, "0.3.5"), ["code"], "0.3.5"));
            fake.Mods.Add(new(cleanExit.Id, cleanExit.Name, false, MakeOldDir(cleanExit, "0.2.1"), ["code"], "0.2.1"));
            WriteRecord("build25480438-r12", "applied", true, null);
            var expectedR12Upgrade = new[] { leafWriter.Id, yoda.Id, atst.Id, tieSwitch.Id, dedicatedMelee.Id, chaos.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR12Upgrade),
                "an r12 (v2.1.5) installation upgrades all 6 enabled r13 code mods while keeping disabled/removed optionals opted out");

            // Upgrading from r15 (v2.2.2) must upgrade all 5 enabled r15->r16 SDK-20 optionals (0.2.3->0.2.4, 0.2.5->0.2.6, 0.1.4->0.1.5, 1.5.2-clonedivers.3->1.5.2-clonedivers.4, 0.5.1->0.5.2) while keeping disabled/removed optionals opted out.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(leafWriter.Id, leafWriter.Name, true, MakeOldDir(leafWriter, "0.2.3"), ["code"], "0.2.3"));
            fake.Mods.Add(new(atst.Id, atst.Name, true, MakeOldDir(atst, "0.2.5"), ["code"], "0.2.5"));
            fake.Mods.Add(new(cameraPullback.Id, cameraPullback.Name, true, MakeOldDir(cameraPullback, "0.1.4"), ["code"], "0.1.4"));
            fake.Mods.Add(new(dedicatedMelee.Id, dedicatedMelee.Name, true, MakeOldDir(dedicatedMelee, "1.5.2-clonedivers.3"), ["code"], "1.5.2-clonedivers.3"));
            fake.Mods.Add(new(chaos.Id, chaos.Name, true, MakeOldDir(chaos, "0.5.1"), ["gameplay", "code"], "0.5.1"));
            fake.Mods.Add(new(cleanExit.Id, cleanExit.Name, true, MakeOldDir(cleanExit, cleanExit.Version), ["code"], cleanExit.Version));
            fake.Mods.Add(new(yoda.Id, yoda.Name, false, MakeOldDir(yoda, yoda.Version), ["code"], yoda.Version));
            WriteRecord("build25480438-r15", "applied", true, HellForgePreset.Optional);
            var expectedR15Upgrade = new[] { leafWriter.Id, atst.Id, cameraPullback.Id, dedicatedMelee.Id, chaos.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR15Upgrade),
                "an r15 (v2.2.2) installation upgrades all 5 enabled SDK-20 optionals while keeping unchanged Clean Exit 0.2.1 and disabled/removed optionals untouched");

            // Upgrading from an earlier record with partial OptionalDefaults offers newly added optionals while preserving prior opt-outs and player-kept versions across rollback.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            fake.Mods.Add(new(yoda.Id, yoda.Name, true, MakeOldDir(yoda, "0.1.2"), ["code"], "0.1.2"));
            WriteRecord(HellForgeGameplayCatalog.Pinned.Id, "applied", true, [cleanExit.Id, yoda.Id, atst.Id, tieSwitch.Id, cameraPullback.Id, swLoc.Id, dedicatedMelee.Id]);
            var partialNew = new[] { leafWriter.Id, chaos.Id };
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(partialNew),
                "an applied r5 record with partial OptionalDefaults offers newly added optional defaults while keeping prior opt-outs and kept Yoda 0.1.2");
            fake.FailPackOnce = true;
            try { await restarted.InstallGameplayAsync(restarted.GameplayCache); check(false, "failed partial-OptionalDefaults r5 upgrade must roll back"); }
            catch (IOException error) { check(error.Message.Contains("previous mod selection was restored"), "failed partial-OptionalDefaults r5 upgrade rolls back cleanly"); }
            check(fake.Mods.Single(m => m.Id == yoda.Id) is { Enabled: true, Version: "0.1.2" } &&
                (await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(partialNew),
                "same-catalog r5 rollback with partial OptionalDefaults keeps already-offered enabled Yoda 0.1.2 skipped");
            fake.Calls.Clear(); await restarted.InstallGameplayAsync(restarted.GameplayCache);
            check(partialNew.All(id => fake.Mods.Single(m => m.Id == id).Enabled) &&
                fake.Mods.Single(m => m.Id == yoda.Id) is { Enabled: true, Version: "0.1.2" } &&
                fake.Mods.All(m => m.Id != cleanExit.Id && m.Id != atst.Id && m.Id != tieSwitch.Id) &&
                fake.Calls.Count(c => c[0] == "mod-add") == partialNew.Length,
                "partial-OptionalDefaults r5 retry installs and enables newly offered optionals while preserving kept Yoda 0.1.2 and removed prior optionals");

            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            File.Delete(record); fake.FailPackOnce = true;
            try { await restarted.InstallGameplayAsync(restarted.GameplayCache); check(false, "failed clean no-intent r5 install must roll back"); }
            catch (IOException error) { check(error.Message.Contains("previous mod selection was restored"), "failed clean no-intent r5 install rolls back cleanly"); }
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(HellForgePreset.Optional),
                "clean no-intent r5 rollback leaves appliedDefaults empty so all six optional defaults remain offered on retry");

            foreach (var id in HellForgePreset.Optional)
            {
                fake.Mods.RemoveAll(m => m.Id == id);
                fake.Mods.Add(new(id, id, false, Path.Combine(root, "mods", id), ["code"], "player-version"));
            }
            File.Delete(record); fake.Calls.Clear();
            await restarted.InstallGameplayAsync(restarted.GameplayCache);
            check(HellForgePreset.Optional.All(id => !fake.Mods.Single(m => m.Id == id).Enabled) && fake.Calls.All(c => c[0] != "mod-add"),
                "first r5 setup also respects already disabled optional mods");
        }
        finally { foreach (var (pinned, index) in optionalSpecs) catalog.Artifacts[index] = pinned; }
    }

    static async Task Step4WizardTests(string area, Action<bool, string> check)
    {
        var wizRoot = Path.Combine(area, "step4-wizard", "Forge");
        Directory.CreateDirectory(Path.Combine(wizRoot, "app", "cli"));
        Directory.CreateDirectory(Path.Combine(wizRoot, "mods"));
        File.WriteAllText(Path.Combine(wizRoot, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");
        File.WriteAllText(Path.Combine(wizRoot, "HellForge.exe"), "fixture");
        File.WriteAllText(Path.Combine(wizRoot, "app", "cli", "HellForge.Cli.exe"), "fixture");
        File.WriteAllText(Path.Combine(wizRoot, "mods", "hellforge-mods.json"), """{"format":1,"share":false}""");

        var fake = new Commands(wizRoot) { Ready = true };
        foreach (var id in HellForgeGameplayCatalog.Pinned.Order)
        {
            var required = HellForgePreset.Required.FirstOrDefault(r => r.Id == id);
            var artifact = HellForgeGameplayCatalog.Pinned.Artifacts.FirstOrDefault(a => a.Id == id);
            var folder = Path.Combine(wizRoot, "mods", id);
            Directory.CreateDirectory(folder);
            var mod = new HellForgeMod(id, required.Name ?? artifact?.Name ?? "Vanilla Plus", true, folder, ["gameplay"], required.Version ?? artifact?.Version ?? "");
            if (id == HellForgePreset.GuardDogs)
                mod = mod with { Options = [new("Smarter Guard Dogs and Sentries", true), new("Guard Dogs", true), new("Sentries", true), new("Intelligence", true), new("Safety", true), new("Sentry Safety", false), new("SEAF Safety", false), new("Targeting Laser", false), new("Targeting Marker", false)] };
            fake.Mods.Add(mod);
        }
        var client = new HellForgeClient(wizRoot, fake) { RequireCliQuiescent = () => { } };
        var backup = Path.Combine(client.CacheRoot, "backups", "gameplay-step4");
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(Path.GetDirectoryName(client.PackIntentPath)!);
        var heldBackOptional = fake.Mods.Single(m => m.Id == HellForgePreset.DedicatedMeleeSlot);
        fake.Mods.Remove(heldBackOptional);
        void MarkGameplayApplied()
        {
            if (fake.Mods.All(m => m.Id != heldBackOptional.Id)) fake.Mods.Add(heldBackOptional);
            File.WriteAllText(client.PackIntentPath + ".gameplay.json", JsonSerializer.Serialize(new
            {
                Root = client.Root,
                Catalog = HellForgeGameplayCatalog.Pinned.Id,
                Backup = backup,
                SnapshotSha256 = new string('a', 64),
                State = "applied",
                OptionalDefaultsApplied = true,
                OptionalDefaults = HellForgePreset.Optional
            }));
        }

        var bytes0 = Encoding.UTF8.GetBytes("step4-verified-blob-0");
        var bytes1 = Encoding.UTF8.GetBytes("step4-verified-blob-1");
        var sha0 = Convert.ToHexString(SHA256.HashData(bytes0)).ToLowerInvariant();
        var sha1 = Convert.ToHexString(SHA256.HashData(bytes1)).ToLowerInvariant();
        var fixtureSelection = new HellForgePack.Selection("clonedivers", "reduced512", "2026.11.19-r20-fixture", HellForgePack.SupportedBuild,
            new string('c', 64), new() { ["commandos"] = false, ["droids"] = true, ["covenant"] = false },
            [
                new PackFile { Name = "9ba626afa44a3aa3.patch_0", Size = bytes0.Length, Sha256 = sha0, Url = "https://example.invalid/0" },
                new PackFile { Name = "9ba626afa44a3aa3.patch_1", Size = bytes1.Length, Sha256 = sha1, Url = "https://example.invalid/1" }
            ]);

        var prevCreate = HellForgeForm.CreateCommands;
        var prevSync = SynchronizationContext.Current;
        HellForgeForm.CreateCommands = _ => fake;
        try
        {
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var prefs = new HellForgePreferences { InstallRoot = wizRoot, Mode = "clonedivers", DeltaSquad = false, Texture = "reduced512" };
            int stageCalls = 0;
            int exportCalls = 0;
            bool pauseFirstExport = true;
            bool sawPauseEnabledDuringGameplayStage = false;
            bool sawPauseEnabledDuringDownload = false;
            bool sawWorkingDisabledDuringImport = false;
            HellForgePack.Plan? observedSecondPlan = null;
            HellForgeSetupForm? wiz = null;

            fake.FailAppearanceImportOnce = true;
            fake.OnAppearanceAdd = () =>
            {
                var cancelBtn = (Button)typeof(HellForgeSetupForm).GetField("cancel", fields)!.GetValue(wiz!)!;
                sawWorkingDisabledDuringImport = !cancelBtn.Enabled && cancelBtn.Text == "Working…" && cancelBtn.AccessibleName == "Working…";
            };

            wiz = new HellForgeSetupForm(wizRoot, preview: false, preferences: prefs)
            {
                SelectionOverride = () => fixtureSelection,
                StageGameplayOverride = (_, missing, _, ct, _) =>
                {
                    stageCalls++;
                    var cancelBtn = (Button)typeof(HellForgeSetupForm).GetField("cancel", fields)!.GetValue(wiz!)!;
                    sawPauseEnabledDuringGameplayStage = missing.Count == 1 && cancelBtn.Enabled && cancelBtn.Text == "Pause download" && cancelBtn.AccessibleName == "Pause download";
                    if (stageCalls == 1)
                    {
                        if (cancelBtn.Enabled)
                            typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(cancelBtn, [EventArgs.Empty]);
                        ct.ThrowIfCancellationRequested();
                    }
                    if (stageCalls == 2) throw new IOException("Simulated gameplay mirror error");
                    MarkGameplayApplied();
                    return Task.CompletedTask;
                },
                ExportPackOverride = async (plan, _, _, ct, _) =>
                {
                    exportCalls++;
                    var cancelBtn = (Button)typeof(HellForgeSetupForm).GetField("cancel", fields)!.GetValue(wiz!)!;
                    sawPauseEnabledDuringDownload = cancelBtn.Enabled && cancelBtn.Text == "Pause download" && cancelBtn.AccessibleName == "Pause download";
                    Directory.CreateDirectory(plan.Output);
                    foreach (var (sha, src) in plan.Local)
                    {
                        var targetName = plan.Selection.Files.Single(f => f.Sha256 == sha).Name;
                        File.Copy(src, Path.Combine(plan.Output, targetName), overwrite: true);
                    }
                    if (pauseFirstExport)
                    {
                        pauseFirstExport = false;
                        // Write first missing file cleanly, then invoke cancel's Click handler (PerformClick is a no-op on hidden unshown forms) so plan.Output stays on disk as a partial export.
                        await File.WriteAllBytesAsync(Path.Combine(plan.Output, "9ba626afa44a3aa3.patch_0"), bytes0, ct);
                        if (cancelBtn.Enabled)
                            typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(cancelBtn, [EventArgs.Empty]);
                        ct.ThrowIfCancellationRequested();
                    }
                    observedSecondPlan = plan;
                    await File.WriteAllBytesAsync(Path.Combine(plan.Output, "9ba626afa44a3aa3.patch_1"), bytes1, ct);
                    await File.WriteAllTextAsync(Path.Combine(plan.Output, "manifest.json"),
                        JsonSerializer.Serialize(new { ManifestVersion = 2, Name = "Clonedivers", Guid = "e9f12495-c8e7-49f8-89d3-53538a9e9939", Options = Array.Empty<object>() }), ct);
                    await File.WriteAllTextAsync(Path.Combine(plan.Output, HellForgePack.ReceiptName),
                        JsonSerializer.Serialize(new HellForgePack.Receipt { Selection = plan.Selection }, HellForgePack.Json), ct);
                }
            };
            SynchronizationContext.SetSynchronizationContext(null); // no message pump in this isolated fixture
            using (wiz)
            {
                typeof(HellForgeSetupForm).GetField("step", fields)!.SetValue(wiz, Enum.ToObject(typeof(HellForgeSetupForm).GetNestedType("WizardStep", System.Reflection.BindingFlags.NonPublic)!, 4));
                var nextBtn = (Button)typeof(HellForgeSetupForm).GetField("next", fields)!.GetValue(wiz)!;
                var cancelBtn = (Button)typeof(HellForgeSetupForm).GetField("cancel", fields)!.GetValue(wiz)!;
                var detailLbl = (Label)typeof(HellForgeSetupForm).GetField("detail", fields)!.GetValue(wiz)!;

                await wiz.PollAsync();
                await wiz.PollAsync();
                check(stageCalls == 1 && sawPauseEnabledDuringGameplayStage &&
                    detailLbl.Text.Contains("Download paused. Click INSTALL PACK & FINISH", StringComparison.Ordinal) &&
                    nextBtn.Text == "INSTALL PACK & FINISH" && cancelBtn.Enabled && cancelBtn.Text == "Close",
                    "Step 4 gameplay-stage pause sets INSTALL PACK & FINISH pause notice and halts PollAsync auto-restart");

                await wiz.ContinueAsync();
                await wiz.PollAsync();
                check(stageCalls == 2 && detailLbl.Text == "Simulated gameplay mirror error" && nextBtn.Text == "INSTALL PACK & FINISH",
                    "Step 4 gameplay-stage failure preserves error notice and halts PollAsync auto-restart");

                await wiz.ContinueAsync();
                var firstPlan = (HellForgePack.Plan)typeof(HellForgeSetupForm).GetField("pendingPackPlan", fields)!.GetValue(wiz)!;
                check(stageCalls == 3 && exportCalls == 0 && firstPlan.Missing.Count == 2 && nextBtn.Text.StartsWith("DOWNLOAD PACK", StringComparison.Ordinal) &&
                    cancelBtn.Enabled && cancelBtn.Text == "Close" && cancelBtn.AccessibleName == "Close",
                    "Step 4 primary button after gameplay-stage pause/failure plans missing appearance files and stops at DOWNLOAD PACK without starting unapproved downloads");

                await wiz.ContinueAsync();
                check(exportCalls == 1 && sawPauseEnabledDuringDownload && Directory.Exists(firstPlan.Output) &&
                    detailLbl.Text.Contains("Pack download paused", StringComparison.Ordinal) &&
                    nextBtn.Text.StartsWith("DOWNLOAD PACK", StringComparison.Ordinal) &&
                    cancelBtn.Enabled && cancelBtn.Text == "Close",
                    "Step 4 enables Pause download with matching AccessibleName during export and preserves pause notice + partial export folder");

                // Second attempt: replans into a fresh output folder reusing verified patch_0, finishes export, then hits simulated InstallPackAsync failure.
                await wiz.ContinueAsync();
                check(exportCalls == 2 && observedSecondPlan != null && observedSecondPlan.Output != firstPlan.Output &&
                    observedSecondPlan.Local.Count == 1 && observedSecondPlan.Missing.Count == 1 &&
                    sawWorkingDisabledDuringImport && detailLbl.Text.Contains("appearance change did not finish", StringComparison.Ordinal),
                    "Step 4 retry after pause replans into a fresh output folder reusing partial verified bytes and disables cancel (Working…) during import");

                // Third attempt: FindPreparedAsync reuses the completed export from attempt 2 without calling ExportPackOverride again!
                await wiz.ContinueAsync();
                check(exportCalls == 2 && nextBtn.Text == "FINISH & RETURN TO LAUNCHER" &&
                    fake.Mods.Any(m => m.Id == HellForgePack.PackId("clonedivers") && m.Enabled),
                    $"Step 4 retry after post-export import failure reuses the verified export via FindPreparedAsync and finishes setup (exportCalls={exportCalls}, next='{nextBtn.Text}', detail='{detailLbl.Text}')");
            }
        }
        finally { HellForgeForm.CreateCommands = prevCreate; SynchronizationContext.SetSynchronizationContext(prevSync); }
    }

    sealed class Reports(Action<ForgeSetupProgress> report) : IProgress<ForgeSetupProgress> { public void Report(ForgeSetupProgress value) => report(value); }
    sealed class Commands(string root) : IHellForgeCommands
    {
        public bool Ready, Running, Sandbox = true, Runner = true;
        public string Engine = HellForgeRelease.Engine;
        public bool FailPackOnce, LoseFirstModAdd, FailAppearanceImportOnce;
        public Action? OnAppearanceAdd;
        public List<HellForgeMod> Mods = [];
        public List<string[]> Calls = [];
        static (string Id, string Version)? ReadModManifest(string path)
        {
            if (File.Exists(path))
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.GetEntry("hellforge-mod.json");
                if (entry == null) return null;
                using var stream = entry.Open();
                using var doc = JsonDocument.Parse(stream);
                return (doc.RootElement.GetProperty("id").GetString()!, doc.RootElement.GetProperty("version").GetString()!);
            }
            var manifest = Path.Combine(path, "hellforge-mod.json");
            if (!File.Exists(manifest)) return null;
            using var folderDoc = JsonDocument.Parse(File.ReadAllText(manifest));
            return (folderDoc.RootElement.GetProperty("id").GetString()!, folderDoc.RootElement.GetProperty("version").GetString()!);
        }
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Calls.Add(args.ToArray()); object result = new { ok = true };
            switch (args[0])
            {
                case "status": result = new { install = root, hellforge = HellForgeRelease.Version, engine = Engine, modSdk = HellForgeRelease.Sdk, gameBuild = HellForgePack.SupportedBuild, networkBlock = "firewall",
                    ready = new { gameCopy = Ready, sessionRunner = Runner }, session = new { running = Running }, settings = new { multiplayer = true, localPlayFab = true, sandbox = Sandbox } }; break;
                case "mods": result = new { mods = Mods.Select(m => new { id = m.Id, name = m.Name, enabled = m.Enabled, folder = m.Folder, tags = m.Tags, version = m.Version, options = m.Options?.Select(o => new { name = o.Name, on = o.On }) }), problems = Array.Empty<string>() }; break;
                case "mod-hash":
                    if (ReadModManifest(args[2]) is { } hashed)
                    {
                        var pinned = HellForgeGameplayCatalog.Pinned.Artifacts.Single(a => a.Id == hashed.Id);
                        result = new { hash = hashed.Version == pinned.Version
                            ? pinned.ContentHash
                            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashed.Id + ":" + hashed.Version))).ToLowerInvariant() };
                    }
                    else
                        result = new { hash = HellForgeGameplayCatalog.Pinned.Artifacts.Single(a => Mods.Any(m => m.Id == a.Id && m.Folder == args[2])).ContentHash };
                    break;
                case "mod-add":
                    if (Path.GetFileName(args[2]).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                    {
                        OnAppearanceAdd?.Invoke();
                        if (FailAppearanceImportOnce) { FailAppearanceImportOnce = false; throw new IOException("fixture pack import failure"); }
                        var packSrc = Path.GetDirectoryName(args[2])!;
                        using var packDoc = JsonDocument.Parse(File.ReadAllText(args[2]));
                        var packId = "pack." + packDoc.RootElement.GetProperty("Guid").GetString();
                        var packFolder = Path.Combine(root, "mods", "selected");
                        Directory.CreateDirectory(packFolder);
                        foreach (var p in Directory.EnumerateFiles(packSrc)) File.Copy(p, Path.Combine(packFolder, Path.GetFileName(p)), true);
                        Mods.RemoveAll(m => m.Id == packId);
                        Mods.Add(new(packId, "Selected pack", true, packFolder, ["pack"]));
                        result = new { id = packId, installed = packFolder };
                        break;
                    }
                    var added = ReadModManifest(args[2]) ?? throw new InvalidDataException("Fixture archive lacks hellforge-mod.json");
                    var optional = HellForgeGameplayCatalog.Pinned.Artifacts.Single(a => a.Id == added.Id);
                    var modFolder = Path.Combine(root, "mods", optional.Id);
                    Directory.CreateDirectory(modFolder);
                    File.WriteAllText(Path.Combine(modFolder, "hellforge-mod.json"),
                        JsonSerializer.Serialize(new { id = optional.Id, name = optional.Name, version = added.Version, gameplay = optional.Id == HellForgePreset.ChaosInvasions, coop = "per-player" }));
                    Mods.RemoveAll(m => m.Id == optional.Id);
                    Mods.Add(new(optional.Id, optional.Name, false, modFolder, ["code"], added.Version));
                    if (LoseFirstModAdd) { LoseFirstModAdd = false; throw new HellForgeCommandOutcomeUnknownException("Fixture lost the r4->r5 import acknowledgement."); }
                    break;
                case "mod-remove": Mods.RemoveAll(m => m.Id == args[2]); break;
                case "settings": break;
                case "play": result = new { @event = "started" }; break;
                case "mod-switch": { var index = Mods.FindIndex(m => m.Id == args[2]); Mods[index] = Mods[index] with { Enabled = args[4] == "yes" }; break; }
                case "mod-order": { var order = args[2].Split(','); Mods = order.Select(id => Mods.Single(m => m.Id == id)).ToList(); break; }
                case "mod-pack":
                    if (FailPackOnce) { FailPackOnce = false; throw new IOException("fixture configuration failure"); }
                    if (args[3] == "--all") break;
                    var dog = Mods.FindIndex(m => m.Id == args[2]);
                    Mods[dog] = Mods[dog] with { Options = Mods[dog].Options!.Select(o => o.Name == args[4] ? o with { On = args[6] == "yes" } : o).ToArray() }; break;
                default: throw new InvalidOperationException("Unexpected turnkey fixture command " + args[0]);
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }
}
