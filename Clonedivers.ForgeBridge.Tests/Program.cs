using System.Text.Json;
using Clonedivers.ForgeBridge;
using Clonedivers;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

var passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
if (args.Contains("--native-control-only"))
{
    var area = Path.Combine(Path.GetTempPath(), "clonedivers-native-control-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(area);
    await Clonedivers.ForgeBridge.Tests.NativeControlTests.Run(area, Check);
    Console.WriteLine($"{passed} native-control boundary fixtures passed; no game/account/namespace was started."); return;
}
if (args is ["--hard-exit-import-fixture", var root]) { await Clonedivers.ForgeBridge.Tests.ImportTests.CrashChildAsync(root); return; }
if (args.Contains("--compatibility-only"))
{
    await Clonedivers.ForgeBridge.Tests.CompatibilityTests.RunAsync(Check);
    Console.WriteLine($"{passed} pinned-compatibility fixtures passed; no engine/isolation changes made."); return;
}
if (args.Contains("--recovery-only"))
{
    await Clonedivers.ForgeBridge.Tests.ImportTests.RecoveryAsync(Check);
    await Clonedivers.ForgeBridge.Tests.ImportTests.RegularAsync(Check);
    Console.WriteLine($"{passed} durable interruption/recovery fixtures passed; no installed game/account changed."); return;
}
if (args.Contains("--budget-only"))
{
    Check(ForgeProtocol.ResultBudget("prepare-pack") >= TimeSpan.FromMinutes(10 + 10 + 20), "pack preparation's reply budget includes official import, recovery and IO work");
    Check(ForgeProtocol.ResultBudget("prepare-regular") == ForgeProtocol.ResultBudget("prepare-pack"), "Regular mode also retains an authoritative reply through bounded rollback");
    Check(ForgeProtocol.ResultBudget("install-gameplay") >= TimeSpan.FromMinutes(35) && ForgeProtocol.ResultBudget("recover-gameplay") >= TimeSpan.FromMinutes(15), "gameplay setup retains its reply through bounded import and recovery");
    Check(ForgeProtocol.ResultBudget("mod-add") == TimeSpan.FromMinutes(20) && ForgeProtocol.ResultBudget("status") == TimeSpan.FromMinutes(3), "direct imports and diagnostic calls retain their narrower result budgets");
    Console.WriteLine($"{passed} timeout-budget checks passed without a wall-clock delay."); return;
}
if (args.FirstOrDefault() == "--cli-wait-fixture")
{
    var folder = Path.GetFullPath(Path.GetDirectoryName(args[1])!);
    if (!folder.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(folder).StartsWith("clonedivers-cli-exit-", StringComparison.Ordinal)) throw new IOException("Invalid fixture probe path.");
    await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString());
    Console.WriteLine("{\"fixture\":true}");
    await Task.Delay(TimeSpan.FromMinutes(1)); return;
}
if (args.Contains("--cli-only"))
{
    await Clonedivers.ForgeBridge.Tests.CliTests.RunAsync(Check);
    Console.WriteLine($"{passed} CLI quiescence fixtures passed; only the owned fixture process was stopped."); return;
}
if (args is ["--cli-sentinel-fixture", var sentinelProbe])
{
    var folder = Path.GetFullPath(Path.GetDirectoryName(sentinelProbe)!);
    if (!folder.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(folder).StartsWith("clonedivers-cli-flood-", StringComparison.Ordinal)) throw new IOException("Invalid independent-child fixture.");
    // The parent waits for this path before flooding its inherited pipes.
    // Publish only a complete, closed PID file: existence alone otherwise
    // races WriteAllTextAsync and Windows readers can see an exclusive writer.
    await File.WriteAllTextAsync(sentinelProbe + ".tmp", Environment.ProcessId.ToString());
    File.Move(sentinelProbe + ".tmp", sentinelProbe);
    await Task.Delay(TimeSpan.FromSeconds(30)); return;
}
if (args is ["--cli-flood-fixture", var probe, var stream])
{
    var folder = Path.GetFullPath(Path.GetDirectoryName(probe)!);
    if (!folder.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(folder).StartsWith("clonedivers-cli-flood-", StringComparison.Ordinal) || stream is not ("stdout" or "stderr")) throw new IOException("Invalid flood fixture.");
    await File.WriteAllTextAsync(probe, Environment.ProcessId.ToString());
    var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--cli-sentinel-fixture"); start.ArgumentList.Add(probe + ".sentinel");
    using var sentinel = System.Diagnostics.Process.Start(start)!; // inherits these pipe handles, like an independent session
    for (var i = 0; i < 500 && !File.Exists(probe + ".sentinel"); i++) await Task.Delay(10);
    if (!File.Exists(probe + ".sentinel")) throw new IOException("Independent child fixture did not start.");
    var writer = stream == "stderr" ? Console.Error : Console.Out;
    for (var i = 0; i < (stream == "stderr" ? 24 : 640); i++) await writer.WriteAsync(new string('x', 8192));
    await writer.FlushAsync();
    await Task.Delay(TimeSpan.FromSeconds(30)); return;
}
if (args.Contains("--output-only"))
{
    await Clonedivers.ForgeBridge.Tests.CliTests.OutputAsync(Check);
    Console.WriteLine($"{passed} bounded CLI output fixtures passed; no installed game/account changed."); return;
}
if (args.Contains("--import-only"))
{
    await Clonedivers.ForgeBridge.Tests.ImportTests.RunAsync(Check);
    Console.WriteLine($"{passed} appearance transaction fixtures passed; no installed game/account changed.");
    return;
}
if (args.Contains("--export-only"))
{
    await Clonedivers.ForgeBridge.Tests.ExportTests.RunAsync(Check);
    Console.WriteLine($"{passed} export I/O fixtures passed; no installed game/account changed.");
    return;
}
string Request(long id, params string[] args) => JsonSerializer.Serialize(new { id, args }) + "\n";
Check(ForgeEntryPolicy.Normalize([], true).SequenceEqual(new[] { "--forge" }), "Forge package defaults to Forge rather than silently opening the Steam-era UI");
Check(ForgeEntryPolicy.Normalize(["--steam", "--gui"], true).SequenceEqual(new[] { "--gui" }), "explicit Steam fallback retains the existing desktop route");
Check(ForgeEntryPolicy.Normalize(["--forge-preview", "running", "--gui"], true).SequenceEqual(new[] { "--forge-preview", "running", "--gui" }), "preview entry remains disconnected from live Forge routing");
Check(ForgeEntryPolicy.Normalize(["--gui"], false).SequenceEqual(new[] { "--gui" }), "ordinary Linux package behavior remains unchanged without Forge edition");
Check(ForgeEntryPolicy.Normalize(["--steam", "--forge", "--gui"], true).SequenceEqual(new[] { "--gui" }) && ForgeEntryPolicy.Normalize(["--steam", "--gui"], false).SequenceEqual(new[] { "--gui" }),
    "explicit Steam fallback takes precedence consistently in both package editions");

foreach (var command in new[] { new[] { "play", "--profile", "clonedivers-armory" }, ["profiles", "--create", "default", "--template", "unlock-all"],
    ["settings", "--sandbox", "no"], ["settings", "--game-options", "--unsafe"], ["settings", "--profile", "other"],
    ["settings", "--multiplayer", "no"], ["settings", "--profile", "default", "--profile", "other"],
    ["mod-add", "--file", "Z:\\home\\user\\mod.zip"], ["mod-switch", "--install", "C:\\Other", "--on", "yes"] })
    Reject(() => BridgeSession.Validate(command), "restricted command: " + string.Join(" ", command));
BridgeSession.Validate(["mod-pack", "--id", "guard-dogs", "--option", "Targeting Marker", "--on", "no"]);
BridgeSession.Validate(["settings", "--profile", "default", "--multiplayer", "yes", "--local-playfab", "yes"]);
Check(true, "normal option and account-only settings requests are accepted");
BridgeSession.Validate(["open-manager"]);
Reject(() => BridgeSession.Validate(["open-manager", "--file", "C:\\arbitrary.exe"]), "manager action cannot select an arbitrary executable");

var fake = new Fake(); var output = new StringWriter();
await new BridgeSession(fake.Execute).ServeAsync(new StringReader(Request(1, "play", "--profile", "default") + Request(2, "mods") + Request(3, "stop")), output);
var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line)).ToArray();
Check(replies.Length == 3 && replies.All(r => r.RootElement.GetProperty("ok").GetBoolean()), "persistent proxy serves launch, status and stop in one namespace lifetime");
Check(fake.Calls.Any(c => c.SequenceEqual(new[] { "stop", "--run", Fake.Run, "--wait", "yes" })) && !fake.Running,
    "stop targets the acknowledged run and waits through official cleanup");

fake = new Fake(); output = new StringWriter();
await new BridgeSession(fake.Execute).ServeAsync(new StringReader(Request(1, "play", "--profile", "default")), output);
Check(!fake.Running && fake.Calls.Last().SequenceEqual(new[] { "stop", "--run", Fake.Run, "--wait", "yes" }), "frontend EOF cleans up its owned session using official stop");

fake = new Fake { IsolationAfterPlay = "other-engine" }; output = new StringWriter();
await new BridgeSession(fake.Execute).ServeAsync(new StringReader(Request(1, "play", "--profile", "default") + Request(2, "stop")), output);
Check(!fake.Running && !output.ToString().Contains("\"ok\":false"), "a changed build/readiness cannot block cleanup of an owned active session");

fake = new Fake { Running = true }; output = new StringWriter();
await new BridgeSession(fake.Execute).ServeAsync(new StringReader(Request(1, "stop") + Request(2, "mod-switch", "--id", "fixture", "--on", "no")), output);
Check(fake.Calls.All(c => c[0] == "status") && fake.Running, "preexisting sessions cannot be stopped or mutated by a new bridge");

foreach (var isolation in new[] { "none", "sandbox-off", "other-engine" })
{
    fake = new Fake { Isolation = isolation }; output = new StringWriter();
    await new BridgeSession(fake.Execute).ServeAsync(new StringReader(Request(1, "play", "--profile", "default")), output);
    Check(fake.Calls.All(c => c[0] == "status") && output.ToString().Contains("\"ok\":false"), "no launch outside pinned isolation: " + isolation);
}

fake = new Fake(); output = new StringWriter();
await new BridgeSession(fake.Execute).ServeAsync(new StringReader("malformed\n" + Request(2, "status")), output);
Check(output.ToString().Contains("\"ok\":false") && output.ToString().Contains("\"id\":2,\"ok\":true"), "a malformed request reports failure without corrupting the next response");
fake = new Fake(); output = new StringWriter();
try { await new BridgeSession(fake.Execute).ServeAsync(new StringReader(Request(1, "play", "--profile", "default") + new string('x', BridgeSession.MaxRequest + 1)), output); throw new Exception("FAIL: oversized bridge input was accepted"); }
catch (InvalidDataException) { Check(!fake.Running && fake.Calls.Last()[0] == "stop", "oversized input is bounded and still cleans up the owned session"); }
const string hello = "{\"bridge\":\"clonedivers-forge\",\"format\":1}\n";
var diagnostics = new List<string>(); var writes = new StringWriter();
var protocol = new ForgeProtocol(new StringReader("Steam bridge ready\n{\"war\":true}\n" + hello + "Proton diagnostic\n{\"id\":1,\"ok\":true,\"result\":{\"running\":false}}\n"), writes, diagnostics.Add);
await protocol.ConnectAsync(CancellationToken.None);
Check(!(await protocol.SendAsync(["status"])).GetProperty("running").GetBoolean() && diagnostics.Count == 3,
    "native protocol separates script/Proton diagnostics from handshake and correlated CLI result");
Check(writes.ToString().Contains("\"id\":1,\"args\":[\"status\"]"), "native requests serialize argument arrays without shell construction");
protocol = new ForgeProtocol(new StringReader(hello + "{\"id\":1,\"ok\":false,\"error\":\"fixture refusal\"}\n{\"id\":2,\"ok\":true,\"result\":{}}\n"), new StringWriter(), _ => { });
await protocol.ConnectAsync(CancellationToken.None);
try { await protocol.SendAsync(["mods"]); throw new Exception("FAIL: command refusal was ignored"); } catch (ForgeCommandException e) { Check(e.Message == "fixture refusal", "a definitive official command failure is preserved"); }
Check((await protocol.SendAsync(["status"])).ValueKind == JsonValueKind.Object, "a definitive failure leaves protocol synchronization usable");
protocol = new ForgeProtocol(new StringReader(hello + "{\"id\":99,\"ok\":true,\"result\":{}}\n"), new StringWriter(), _ => { });
await protocol.ConnectAsync(CancellationToken.None);
try { await protocol.SendAsync(["status"]); throw new Exception("FAIL: wrong response id was accepted"); } catch (InvalidDataException) { Check(true, "mismatched response ids fail the transport"); }
try { await protocol.SendAsync(["status"]); throw new Exception("FAIL: poisoned stream was reused"); } catch (InvalidOperationException) { Check(true, "an unknown result cannot be retried on a poisoned stream"); }
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); writes = new StringWriter();
    protocol = new ForgeProtocol(new StringReader(hello), writes, _ => { }); await protocol.ConnectAsync(CancellationToken.None);
    try { await protocol.SendAsync(["mod-switch", "--id", "fixture", "--on", "no"], cancelled.Token); throw new Exception("FAIL: cancelled request was accepted"); }
    catch (OperationCanceledException) { Check(writes.ToString() == "", "cancelling before send writes no partial mutation request"); }
}
using (var cancelled = new CancellationTokenSource())
{
    var writer = new CancelOnWrite(cancelled);
    protocol = new ForgeProtocol(new StringReader(hello + "{\"id\":1,\"ok\":true,\"result\":{\"committed\":true}}\n"), writer, _ => { }); await protocol.ConnectAsync(CancellationToken.None);
    Check((await protocol.SendAsync(["mod-switch", "--id", "fixture", "--on", "no"], cancelled.Token)).GetProperty("committed").GetBoolean() && cancelled.IsCancellationRequested,
        "cancellation after send waits for the authoritative commit result");
}
try { await ForgeProtocol.ReadLineAsync(new StringReader(new string('x', 33)), 32, CancellationToken.None); throw new Exception("FAIL: oversized native line was accepted"); }
catch (InvalidDataException) { Check(true, "native protocol rejects an oversized line before accumulating unbounded output"); }

var fixture = Path.Combine(Path.GetTempPath(), "clonedivers-forge-plan-" + Guid.NewGuid().ToString("N"));
await Clonedivers.ForgeBridge.Tests.GameplayNativeTests.Run(fixture, Check);
Check(LinuxForgePlan.SupportsArchitecture(Architecture.X64) && !LinuxForgePlan.SupportsArchitecture(Architecture.Arm64) && !LinuxForgePlan.SupportsArchitecture(Architecture.X86),
    "Forge refuses unsupported architectures without confusing the separate ARM64 Steam package with Proton qualification");
try
{
    var install = Path.Combine(fixture, "Prefix Space", "pfx", "drive_c", "users", "steamuser", "Documents", "Forge Ω $(whoami)");
    Directory.CreateDirectory(Path.Combine(install, "app", "cli"));
    var marker = Path.Combine(install, "hellforge-install.json");
    File.WriteAllText(marker, """{"schemaVersion":1,"buildId":"25480438"}""");
    File.WriteAllText(Path.Combine(install, "HellForge.exe"), "fixture launcher, never executed");
    File.WriteAllText(Path.Combine(install, "app", "cli", "HellForge.Cli.exe"), "fixture CLI, never executed");
    var helper = Path.Combine(fixture, "source-bridge.exe"); File.WriteAllText(helper, "fixture bridge, never executed");
    string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    var bundle = Path.Combine(fixture, "script-bundle"); Directory.CreateDirectory(bundle);
    foreach (var name in LinuxForgePlan.Scripts) File.WriteAllText(Path.Combine(bundle, name), "#!/bin/bash\n# fixture only\n");
    File.WriteAllText(Path.Combine(bundle, "COPYING"), "fixture notice");
    File.WriteAllText(Path.Combine(bundle, "bundle.json"), JsonSerializer.Serialize(new { Format = 1, Scripts = LinuxForgePlan.Scripts.Select(name => new { Name = name, BundleSha256 = Hash(Path.Combine(bundle, name)) }) }));
    var before = Hash(marker);
    var plan = LinuxForgePlan.Stage(install, helper, Hash(helper), bundle);
    var start = plan.StartInfo();
    Check(plan.WindowsInstall == @"C:\users\steamuser\Documents\Forge Ω $(whoami)" && start.ArgumentList.Last() == plan.WindowsInstall,
        "Linux-to-C: mapping preserves spaces, Unicode and shell metacharacters as literal arguments");
    Check(start.FileName == "bash" && !start.UseShellExecute && start.ArgumentList[0] == plan.Script && start.ArgumentList[1] == plan.Bridge,
        "native plan enters the official script before the persistent bridge, never direct Wine/Proton");
    Check(start.Environment["INSTALL"] == install && start.Environment["STEAM_COMPAT_DATA_PATH"] == plan.Prefix && !start.Environment.ContainsKey("HELLFORGE_LINUX_NETNS"),
        "native plan binds the dedicated prefix and leaves namespace marking to the official script");
    Check(Hash(marker) == before && !plan.Bridge.StartsWith(install, StringComparison.Ordinal) && Hash(plan.Bridge) == Hash(helper),
        "staging writes only a verified launcher cache, leaving the Forge installation untouched");
    Check(LinuxForgePlan.Stage(install, helper, Hash(helper), bundle) == plan, "an unchanged verified helper cache is reused");
    var invalidHash = new string('0', 64);
    void RefusePlan(Action action, string name) { try { action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
    RefusePlan(() => LinuxForgePlan.Stage(install, helper, invalidHash, bundle), "a helper SHA mismatch is rejected before cache mutation");
    File.WriteAllText(Path.Combine(bundle, "hf-inner.sh"), "tampered");
    RefusePlan(() => LinuxForgePlan.Stage(install, helper, Hash(helper), bundle), "script drift is rejected against bundle checksums");
    File.WriteAllText(Path.Combine(bundle, "hf-inner.sh"), "#!/bin/bash\n# fixture only\n");
    var bundleMetadata = Path.Combine(bundle, "bundle.json"); var originalBundleMetadata = File.ReadAllText(bundleMetadata);
    File.WriteAllText(bundleMetadata, originalBundleMetadata.Replace("hf-inner.sh", "../untrusted.sh"));
    RefusePlan(() => LinuxForgePlan.Stage(install, helper, Hash(helper), bundle), "unknown/traversing script names fail before any bundle copy");
    File.WriteAllText(bundleMetadata, originalBundleMetadata);
    try
    {
        var linked = Path.Combine(fixture, "linked-prefix"); Directory.CreateSymbolicLink(linked, plan.Prefix);
        var linkedInstall = Path.Combine(linked, Path.GetRelativePath(plan.Prefix, install));
        RefusePlan(() => LinuxForgePlan.Stage(linkedInstall, helper, Hash(helper), bundle), "a linked prefix ancestor cannot redirect helper staging");
        var dangling = Path.Combine(fixture, "dangling-prefix"); Directory.CreateSymbolicLink(dangling, Path.Combine(fixture, "missing-target"));
        RefusePlan(() => LinuxForgePlan.Stage(Path.Combine(dangling, "pfx", "drive_c", "Forge"), helper, Hash(helper), bundle), "a dangling link is rejected even though Exists follows it and reports false");
    }
    catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: symlink fixture creation requires Windows privileges; Linux CI must run these cases."); }
    catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314) { Console.WriteLine("SKIP: symlink fixture creation requires Windows privileges; Linux CI must run these cases."); }
    File.WriteAllText(plan.Bridge, "tampered cache");
    RefusePlan(() => LinuxForgePlan.Stage(install, helper, Hash(helper), bundle), "existing cache tampering is reported and never overwritten");
    RefusePlan(() => LinuxForgePlan.Stage(Path.Combine(fixture, "steamapps", "compatdata", "553850", "pfx", "drive_c", "Forge"), helper, Hash(helper), bundle),
        "Steam's shared/live prefix is rejected before looking for installation files");
    File.WriteAllText(marker, """{"schemaVersion":1,"buildId":"26251122"}""");
    RefusePlan(() => LinuxForgePlan.Stage(install, helper, Hash(helper), bundle), "the live Steam build cannot satisfy the pinned Forge plan");
    File.WriteAllText(marker, """{"schemaVersion":1,"buildId":"25480438"}""");
    Game.IsRunningCheck = () => false;
    var playerOfficial = new Fake { Root = install };
    var managerOpened = false;
    var player = new PlayerCommands(install, playerOfficial, () => managerOpened = true);
    await player.RunAsync(["open-manager"], CancellationToken.None);
    Check(managerOpened && playerOfficial.Calls.Count == 0, "the manager dispatcher opens only the installed manager without running profile/CLI mutations");
    var accountState = await player.RunAsync(["account-status"], CancellationToken.None);
    Check(!accountState.GetProperty("Imported").GetBoolean() && playerOfficial.Calls.Count == 0 && !Directory.Exists(Path.Combine(install, "profiles")),
        "native account status reuses verified account logic without creating/resetting a missing profile");
    try { await player.RunAsync(["play", "--profile", "default"], CancellationToken.None); throw new Exception("FAIL: missing account reached launch"); }
    catch (InvalidOperationException e) { Check(e.Message.Contains("No complete HD2 capture") && playerOfficial.Calls.All(c => c[0] != "play"), "the actual player dispatcher blocks launch without a verified imported account"); }
    Check(playerOfficial.Calls.All(c => c[0] != "profiles" || c.Length == 1) && !Directory.Exists(Path.Combine(install, "profiles")),
        "account gating offers no profile creation, lab or unlock-all fallback");
    var selectionArgs = new[] { "select-mode", "--mode", "regular", "--delta", "yes", "--texture", "full", "--droids", "yes", "--covenant", "no" };
    BridgeSession.Validate(selectionArgs);
    await player.RunAsync(selectionArgs, CancellationToken.None);
    var state = await player.RunAsync(["player-status"], CancellationToken.None);
    Check(!state.GetProperty("ready").GetBoolean() && state.GetProperty("detail").GetString()!.Contains("turn OFF automatic gameplay-mod sharing"),
        "the native player dispatcher blocks the official default sharing-ON state before offering the private preset");
    Directory.CreateDirectory(Path.Combine(install, "mods"));
    File.WriteAllText(Path.Combine(install, "mods", "hellforge-mods.json"), "{\"share\":false}"); // isolated fixture, never the owner's settings
    state = await player.RunAsync(["player-status"], CancellationToken.None);
    Check(!state.GetProperty("ready").GetBoolean() && state.GetProperty("detail").GetString()!.Contains("preset"), "a fresh native Regular mode cannot appear ready with an empty gameplay preset");
    playerOfficial.Running = true; playerOfficial.Calls.Clear();
    state = await player.RunAsync(["player-status"], CancellationToken.None);
    Check(state.GetProperty("status").GetProperty("Running").GetBoolean() && playerOfficial.Calls.Count == 1 && playerOfficial.Calls[0][0] == "status",
        "active staged gameCopy=false preserves session status without account/mod queries or repair");
    var preferences = new LinuxForgePreferences { InstallRoot = install };
    var preferencesFile = Path.Combine(fixture, "forge.json"); preferences.Save(preferencesFile);
    Check(LinuxForgePreferences.Load(preferencesFile).InstallRoot == install && !File.Exists(Path.Combine(fixture, "config.json")), "Forge choices persist separately from Steam preferences");
    preferences.Texture = "unknown";
    RefusePlan(() => preferences.Save(preferencesFile), "invalid Forge texture choices cannot replace saved preferences"); preferences.Texture = "full";
    var connection = new FakeConnection { FailAfterPlay = true };
    var native = new LinuxForgePlayer(connection, preferences);
    await native.ApplySelectionAsync();
    try { await native.LaunchAsync(); throw new Exception("FAIL: expected post-start status failure"); }
    catch (IOException) { Check(native.OwnsSession && native.SessionActive && !native.CanLaunch, "a post-launch poll failure retains End session and blocks another launch"); }
    try { await native.OpenManagerAsync(); throw new Exception("FAIL: opened manager during active play"); }
    catch (InvalidOperationException) { Check(connection.Managers == 0, "setup/mod manager cannot be opened through the native player during an active session"); }
    connection.FailStop = true;
    try { await native.EndSessionAsync(); throw new Exception("FAIL: expected failed official stop"); }
    catch (IOException) { Check(native.OwnsSession, "a failed Stop keeps ownership available for recovery"); }
    await native.EndSessionAsync();
    Check(!native.SessionActive && native.CanLaunch && connection.Stops == 2, "successful Stop refreshes readiness and clears session ownership");
    await native.OpenManagerAsync(); Check(connection.Managers == 1, "idle native setup routes through the same isolated connection");
    await native.DisposeAsync(); Check(connection.Disposed, "native disposal closes a clean connection");
    connection = new FakeConnection { Running = true }; native = new LinuxForgePlayer(connection, preferences); await native.RefreshAsync();
    try { await native.EndSessionAsync(); throw new Exception("FAIL: foreign session was stopped"); }
    catch (InvalidOperationException) { Check(connection.Stops == 0 && native.SessionActive, "native End session never stops a session it did not launch"); }
    connection = new FakeConnection { Ready = false }; native = new LinuxForgePlayer(connection, preferences);
    try { await native.LaunchAsync(); throw new Exception("FAIL: incomplete setup reached play"); }
    catch (InvalidOperationException) { Check(connection.Plays == 0 && !native.CanLaunch, "native launch refuses incomplete account/preset readiness"); }
    var appearancePlan = new HellForgePack.Plan(preferences.PackSelection(), Path.Combine(fixture, "outside-prefix-output"), new());
    preferences.Mode = "empire";
    try { await native.PrepareAppearanceAsync(appearancePlan, true); throw new Exception("FAIL: changed selection was exported"); }
    catch (InvalidDataException) { Check(!Directory.Exists(appearancePlan.Output), "changed appearance selection is rejected before downloads or file creation"); }
    preferences.Mode = "clonedivers";
    try { await native.PrepareAppearanceAsync(appearancePlan, true); throw new Exception("FAIL: outside-prefix export was written"); }
    catch (InvalidDataException) { Check(!Directory.Exists(appearancePlan.Output), "an outside-prefix export path is rejected before any download/write"); }
}
finally
{
    var resolved = Path.GetFullPath(fixture);
    if (!resolved.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(resolved).StartsWith("clonedivers-forge-plan-", StringComparison.Ordinal)) throw new IOException("Fixture cleanup escaped its temporary root.");
    if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
}
await Clonedivers.ForgeBridge.Tests.ExportTests.RunAsync(Check);
await Clonedivers.ForgeBridge.Tests.ImportTests.RunAsync(Check);
await Clonedivers.ForgeBridge.Tests.ImportTests.RecoveryAsync(Check);
await Clonedivers.ForgeBridge.Tests.ImportTests.RegularAsync(Check);
await Clonedivers.ForgeBridge.Tests.CliTests.RunAsync(Check);
await Clonedivers.ForgeBridge.Tests.CliTests.OutputAsync(Check);
await Clonedivers.ForgeBridge.Tests.CompatibilityTests.RunAsync(Check);
Console.WriteLine($"{passed} bridge/plan/protocol assertions passed (fixtures only; no Linux/game qualification).");

sealed class CancelOnWrite(CancellationTokenSource source) : StringWriter
{
    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    { source.Cancel(); return base.WriteLineAsync(buffer, cancellationToken); }
}

sealed class FakeConnection : IForgeConnection
{
    internal bool Running, FailAfterPlay, FailStatus, FailStop, Disposed;
    internal bool Ready = true;
    internal int Plays, Stops, Managers;
    public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        object result;
        if (args[0] == "player-status")
        {
            if (FailStatus) { FailStatus = false; throw new IOException("fixture status failure"); }
            result = new ForgePlayerState(new(Path.GetTempPath(), "fixture", "25480438", !Running, true, Running, true, true, "observed", null),
                Ready && !Running, Ready ? "Fixture ready" : "Import account and finish preset", new(false, false, "fixture account"), null);
        }
        else if (args[0] == "play") { Plays++; Running = true; FailStatus = FailAfterPlay; result = new { @event = "started" }; }
        else if (args[0] == "stop") { Stops++; if (FailStop) { FailStop = false; throw new IOException("fixture stop failure"); } Running = false; result = new { @event = "stopped" }; }
        else if (args[0] == "open-manager") { Managers++; result = new { opened = "official-manager" }; }
        else result = new { fixture = true };
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

sealed class Fake : IHellForgeCommands
{
    internal const string Run = @"C:\HellForge\logs\sessions\12345678901234567890123456789012";
    internal bool Running;
    internal string Isolation = "normal";
    internal string? IsolationAfterPlay;
    internal string Root = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
    internal readonly List<string[]> Calls = [];
    internal Task<JsonElement> Execute(IReadOnlyList<string> args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Calls.Add(args.ToArray());
        object result;
        if (args[0] == "status") result = new { install = Root, gameBuild = BridgeSession.Build, hellforge = "0.4.0-beta (d8a8b46)", engine = Isolation == "other-engine" ? "0.2.0" : "0.1.47", modSdk = 16,
            networkBlock = Isolation == "none" ? "none" : "linux-netns", ready = new { linuxNamespace = Isolation != "none", gameCopy = !Running, sessionRunner = true }, settings = new { sandbox = Isolation != "sandbox-off", multiplayer = true, localPlayFab = true }, session = new { running = Running, lastRun = Run, lastStatus = "observed", lastFailure = (string?)null } };
        else if (args[0] == "play") { Running = true; if (IsolationAfterPlay != null) Isolation = IsolationAfterPlay; result = new { @event = "started", run = Run }; }
        else if (args[0] == "stop") { Running = false; result = new { @event = "stopping" }; }
        else if (args[0] == "profiles") result = new { profiles = new[] { new { name = "default", template = "starter" } } };
        else if (args[0] == "mods") result = new { mods = Array.Empty<object>(), problems = Array.Empty<string>(), modlist = (string?)null };
        else result = new { fixture = true };
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }
    public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default) => Execute(args, ct);
}
