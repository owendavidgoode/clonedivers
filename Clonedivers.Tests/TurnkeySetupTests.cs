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
            fake.Mods[dogs] = fake.Mods[dogs] with { Options = [new("Smarter Guard Dogs and Sentries", true), new("Guard Dogs", true), new("Sentries", true), new("Intelligence", true), new("Safety", true), new("Sentry Safety", true), new("SEAF Safety", true), new("Targeting Marker", issue == "options")] };
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
            var chaos = catalog.Artifacts.Single(a => a.Id == HellForgePreset.ChaosInvasions);
            var newSinceR3 = HellForgePreset.Optional.Where(id => id != cleanExit.Id).ToHashSet(StringComparer.Ordinal);
            var newInR5 = new[] { atst.Id, tieSwitch.Id }.ToHashSet(StringComparer.Ordinal);
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
            var expectedR4Upgrade = new[] { leafWriter.Id, chaos.Id, atst.Id, tieSwitch.Id }.ToHashSet(StringComparer.Ordinal);
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedR4Upgrade),
                "an r4 installation upgrades enabled 0.0.9/0.2.0 optionals and offers AT-ST + TIE Switch while keeping disabled Yoda 0.1.2 and removed Clean Exit opted out");
            fake.Calls.Clear(); await restarted.InstallGameplayAsync(restarted.GameplayCache);
            check(fake.Mods.Single(m => m.Id == leafWriter.Id) is { Enabled: true, Version: "0.1.0" } &&
                fake.Mods.Single(m => m.Id == chaos.Id) is { Enabled: true, Version: "0.3.0" } &&
                newInR5.All(id => fake.Mods.Single(m => m.Id == id).Enabled) &&
                fake.Mods.Single(m => m.Id == yoda.Id) is { Enabled: false, Version: "0.1.2" } &&
                fake.Mods.All(m => m.Id != cleanExit.Id) &&
                fake.Calls.Count(c => c[0] == "mod-add") == expectedR4Upgrade.Count,
                "r4->r5 upgrade imports 0.1.0/0.3.0 upgrades and new AT-ST/TIE Switch defaults while leaving disabled Yoda 0.1.2 and removed Clean Exit untouched");

            // Upgrading from an earlier r5 record with partial OptionalDefaults offers newly added optionals while preserving prior opt-outs.
            fake.Mods.RemoveAll(m => HellForgePreset.Optional.Contains(m.Id));
            WriteRecord(HellForgeGameplayCatalog.Pinned.Id, "applied", true, [cleanExit.Id, yoda.Id, atst.Id, tieSwitch.Id]);
            var partialNew = new[] { leafWriter.Id, chaos.Id };
            check((await restarted.MissingGameplayAsync()).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(partialNew),
                "an applied r5 record with partial OptionalDefaults offers newly added optional defaults while keeping prior opt-outs");
            fake.Calls.Clear(); await restarted.InstallGameplayAsync(restarted.GameplayCache);
            check(partialNew.All(id => fake.Mods.Single(m => m.Id == id).Enabled) &&
                fake.Mods.All(m => m.Id != cleanExit.Id && m.Id != yoda.Id && m.Id != atst.Id && m.Id != tieSwitch.Id) &&
                fake.Calls.Count(c => c[0] == "mod-add") == partialNew.Length,
                "partial-OptionalDefaults r5 upgrade installs and enables newly offered optionals while preserving removed prior optionals");

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

    sealed class Reports(Action<ForgeSetupProgress> report) : IProgress<ForgeSetupProgress> { public void Report(ForgeSetupProgress value) => report(value); }
    sealed class Commands(string root) : IHellForgeCommands
    {
        public bool Ready, Running, Sandbox = true, Runner = true;
        public string Engine = HellForgeRelease.Engine;
        public bool FailPackOnce;
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
                case "status": result = new { install = root, hellforge = HellForgeRelease.Version, engine = Engine, modSdk = 16, gameBuild = HellForgePack.SupportedBuild, networkBlock = "firewall",
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
                    var added = ReadModManifest(args[2]) ?? throw new InvalidDataException("Fixture archive lacks hellforge-mod.json");
                    var optional = HellForgeGameplayCatalog.Pinned.Artifacts.Single(a => a.Id == added.Id);
                    Mods.RemoveAll(m => m.Id == optional.Id);
                    Mods.Add(new(optional.Id, optional.Name, false, Path.Combine(root, "mods", optional.Id), ["code"], added.Version)); break;
                case "mod-remove": Mods.RemoveAll(m => m.Id == args[2]); break;
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
