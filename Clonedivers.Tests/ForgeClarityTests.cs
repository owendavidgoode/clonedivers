using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clonedivers.Tests;

static class ForgeClarityTests
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(HellForgeForm form, string name) => (T)typeof(HellForgeForm).GetField(name, Members)!.GetValue(form)!;
    static T? OptionalField<T>(HellForgeForm form, string name) => (T?)typeof(HellForgeForm).GetField(name, Members)!.GetValue(form);
    static void Set(HellForgeForm form, string name, object? value) => typeof(HellForgeForm).GetField(name, Members)!.SetValue(form, value);
    static void View(HellForgeForm form) => typeof(HellForgeForm).GetMethod("UpdateView", Members)!.Invoke(form, null);
    static Task Invoke(HellForgeForm form, string name, params object[] args) => (Task)typeof(HellForgeForm).GetMethod(name, Members)!.Invoke(form, args)!;
    static IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>().SelectMany(child => Children(child).Prepend(child));

    sealed class FakeCommands(string root) : IHellForgeCommands
    {
        public int PlayCalls;
        public List<HellForgeMod> Mods { get; } = BuildReadyMods(root);
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            object result = args[0] switch
            {
                "status" => new
                {
                    install = root,
                    hellforge = HellForgeRelease.Version,
                    engine = HellForgeRelease.Engine,
                    modSdk = HellForgeRelease.Sdk,
                    networkBlock = "wfp",
                    gameBuild = HellForgePack.SupportedBuild,
                    ready = new { gameCopy = true, sessionRunner = true, linuxNamespace = false },
                    session = new { running = false, lastStatus = (string?)null, lastFailure = (string?)null, lastRun = (string?)null },
                    settings = new { multiplayer = true, localPlayFab = true, sandbox = true }
                },
                "mods" => new
                {
                    mods = Mods.Select(m => new { id = m.Id, name = m.Name, enabled = m.Enabled, folder = m.Folder, tags = m.Tags, version = m.Version,
                        options = m.Options?.Select(o => new { name = o.Name, on = o.On }) }),
                    problems = Array.Empty<string>(),
                    modlist = "fixture-code"
                },
                "mod-hash" => new { hash = HellForgeGameplayCatalog.Pinned.Artifacts.Single(a => Mods.Any(m => m.Id == a.Id && m.Folder == args[2])).ContentHash },
                "profiles" => new { profiles = new[] { new { name = HellForgeAccount.Profile, template = "starter" } } },
                "settings" => new { ok = true },
                "play" => RecordPlay(),
                _ => throw new InvalidOperationException("Unexpected fake command: " + args[0])
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        object RecordPlay() { PlayCalls++; return new { @event = "started", runId = "fixture", pid = 123 }; }
    }

    static List<HellForgeMod> BuildReadyMods(string root)
    {
        var mods = HellForgePreset.Required.Select(item => new HellForgeMod(item.Id, item.Name, true, Path.Combine(root, "mods", item.Id), [], item.Version,
            item.Id == HellForgePreset.GuardDogs ? [new("Smarter Guard Dogs and Sentries", true), new("Guard Dogs", true), new("Sentries", true), new("Intelligence", true), new("Safety", true), new("Sentry Safety", true), new("SEAF Safety", true), new("Targeting Marker", false)] : null)).ToList();
        mods.Add(new("skyeshade.vanilla-plus", "Vanilla Plus", true, Path.Combine(root, "mods", "vanilla-plus"), []));
        mods.Sort((a, b) => HellForgeGameplayCatalog.Pinned.Order.IndexOf(a.Id).CompareTo(HellForgeGameplayCatalog.Pinned.Order.IndexOf(b.Id)));
        return mods;
    }

    static string CreateForgeRoot(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "app", "cli"));
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "hellforge-install.json"), "{\"schemaVersion\":1,\"buildId\":\"25480438\"}");
        File.WriteAllText(Path.Combine(root, "mods", "hellforge-mods.json"), "{\"share\":false}");
        File.WriteAllText(Path.Combine(root, "app", "cli", "HellForge.Cli.exe"), "fixture, never executed");
        File.WriteAllText(Path.Combine(root, "HellForge.exe"), "fixture, never executed");
        return root;
    }

    static void SeedWarbondFixture(string root, bool createReceipt)
    {
        var profile = Path.Combine(root, "profiles", "default");
        var snapshot = Path.Combine(root, "account-snapshot", "default");
        Directory.CreateDirectory(profile); Directory.CreateDirectory(snapshot); Directory.CreateDirectory(Path.Combine(root, "app"));
        var profilePath = Path.Combine(profile, "profile.json");
        File.WriteAllText(profilePath, """{"schemaVersion":1,"progression":{"experience":1000},"customization":{"items":[]},"inventory":{"items":[]}}""");
        File.WriteAllText(Path.Combine(root, "app", "progression-data.json"),
            """{"seasonPassDetails":{"owned":{"id32":10,"premiumTokenMixIds":[202]}}}""");
        File.WriteAllText(Path.Combine(snapshot, "snapshot.json"), """{"progression":true}""");
        var inventoryBytes = System.Text.Encoding.UTF8.GetBytes("""{"items":[{"mixId":202,"amount":1,"isDiscovered":true}]}""");
        var progressionBytes = System.Text.Encoding.UTF8.GetBytes("""{"experience":1000}""");
        File.WriteAllBytes(Path.Combine(snapshot, "real-inventory.json"), inventoryBytes);
        File.WriteAllBytes(Path.Combine(snapshot, "progression.json"), progressionBytes);
        static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(snapshot, "seal.json"), JsonSerializer.Serialize(new
        {
            steamAccount = "1",
            capturedUtc = "2026-11-01T00:00:00Z",
            inventorySha256 = Sha(inventoryBytes),
            progressionSha256 = Sha(progressionBytes)
        }));
        if (createReceipt)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var file in new[] { "real-inventory.json", "progression.json", "seal.json" })
                hash.AppendData(SHA256.HashData(File.ReadAllBytes(Path.Combine(snapshot, file))));
            var captureKey = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var accountHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("Clonedivers account v1:1"))).ToLowerInvariant();
            File.WriteAllText(Path.Combine(profile, "clonedivers-account-import.json"),
                JsonSerializer.Serialize(new { policy = "one-way-v2", capture = captureKey, importedUtc = DateTimeOffset.UtcNow, account = accountHash }));
        }
    }

    internal static void Run(string area, Action<bool, string> check)
    {
        var game = Game.IsRunningCheck; var steam = HellForgeForm.SteamReadyCheck; var open = HellForgeForm.OpenManager;
        var openSteam = HellForgeForm.OpenSteamClient; var anyProc = HellForgeWarbonds.AnyProcessNamed;
        var createCmds = HellForgeForm.CreateCommands; var createAccount = HellForgeForm.CreateAccount;
        var importAction = HellForgeForm.ImportAccountAction; var selectAction = HellForgeForm.SelectAccountAction;
        var tryWarbonds = HellForgeForm.TryOpenWarbondsAction;
        var openWarbonds = HellForgeForm.OpenWarbondsAction; var ensureAccount = HellForgeForm.EnsureAccountAction;
        var showPrompt = HellForgeForm.ShowPrompt; var capture = HellForgeForm.CaptureBlockCheck;
        var context = SynchronizationContext.Current;
        Game.IsRunningCheck = () => false; HellForgeForm.SteamReadyCheck = () => true; HellForgeForm.CaptureBlockCheck = () => null;
        try
        {
            Instructions(check);
            foreach (var error in new[] { "Not enough disk space: need 512 MB", "disk full: HTTP 503", "Access denied: HTTP 500", "Permission denied: 502" })
                check(HellForgeForm.FailureSummary(error) == (error.Contains("denied", StringComparison.OrdinalIgnoreCase)
                    ? "Review folder permissions in More > Session details before trying again." : "Free some disk space before trying again."),
                    "local storage/permission error takes priority over server-like numbers: " + error);
            check(HellForgeForm.FailureSummary("HTTP 503 Service Unavailable").StartsWith("GitHub"), "actual server failures still offer a network retry");
            var socketError = new HttpRequestException("An attempt was made to access a socket in a way forbidden by its access permissions.", new SocketException(10013));
            check(HellForgeForm.FailureSummary(socketError.ToString()).StartsWith("GitHub"),
                "blocked socket HttpRequestException reports network rather than folder permissions");
            check(HellForgeForm.FailureSummary(new SocketException(10013).ToString()).StartsWith("GitHub") &&
                HellForgeForm.FailureSummary("System.Net.Sockets.SocketException: No such host is known. permission denied").StartsWith("GitHub"),
                "socket and host lookup failures take precedence over permission text");
            check(HellForgeForm.FailureSummary("HellForge programs are missing. Repair it with the official installer.") ==
                "Repair HellForge with the official installer, or Locate another folder",
                "missing HellForge programs map to official repair or locating another folder");
            check(HellForgeWarbonds.WindowsProcessNames.SequenceEqual(new[] { "helldivers2", "HellForge", "HellForge.Launcher" }),
                "Warbond idle checks include both Windows Forge processes and the game");
            WindowActivationTests(check);
            Task.Run(() => WarbondProcessTests(area, check)).GetAwaiter().GetResult();
            States(area, check);
            Task.Run(() => AsyncFlowTests(area, check)).GetAwaiter().GetResult();
        }
        finally
        {
            Game.IsRunningCheck = game; HellForgeForm.SteamReadyCheck = steam; HellForgeForm.OpenManager = open;
            HellForgeForm.OpenSteamClient = openSteam; HellForgeWarbonds.AnyProcessNamed = anyProc;
            HellForgeForm.CreateCommands = createCmds; HellForgeForm.CreateAccount = createAccount;
            HellForgeForm.ImportAccountAction = importAction; HellForgeForm.SelectAccountAction = selectAction;
            HellForgeForm.TryOpenWarbondsAction = tryWarbonds;
            HellForgeForm.OpenWarbondsAction = openWarbonds; HellForgeForm.EnsureAccountAction = ensureAccount;
            HellForgeForm.ShowPrompt = showPrompt; HellForgeForm.CaptureBlockCheck = capture;
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }
    static void Instructions(Action<bool, string> check)
    {
        using var setup = new HellForgeSetupForm(preview: true);
        var intro = Children(setup).OfType<Label>().Single(label => label.Text.Contains("Keep Helldivers 2"));
        check(intro.Text.Contains("Keep Helldivers 2 installed in Steam") &&
            intro.Text.Contains("Steam phone app") && intro.Text.Contains("Official setup (fallback)") &&
            intro.Text.Contains("administrator approval two or three times") && intro.Text.Contains("about 70 GB"),
            "first setup screen states Steam install, QR/fallback choices, admin prompts and 70 GB space need");
        check(HellForgeForm.SyncInstructions.Contains("Keep Helldivers 2 installed and up to date in Steam") &&
            HellForgeForm.SyncInstructions.Contains("Windows will ask for administrator approval") &&
            HellForgeForm.SyncInstructions.Contains("close HellForge") &&
            HellForgeForm.SyncInstructions.Contains("Apply import") &&
            HellForgeForm.SyncInstructions.Contains("auto-apply") &&
            !HellForgeForm.SyncInstructions.Contains("70 GB") &&
            !HellForgeForm.SyncInstructions.Contains("QR") &&
            !HellForgeForm.SyncInstructions.Contains("Steam phone app") &&
            !HellForgeForm.SyncInstructions.Contains("Official setup (fallback)"),
            "sync instructions stay sync-specific (Steam HD2, admin prompt, close HellForge, no QR/fallback/70 GB)");
    }
    static void WindowActivationTests(Action<bool, string> check)
    {
        int restored = 0, activated = 0;
        check(!HellForgeForm.RestoreAndActivateWindow(IntPtr.Zero, _ => true, (_, _) => { restored++; return true; }, _ => { activated++; return true; }) &&
            restored == 0 && activated == 0, "zero window handle falls back without calling ShowWindow or SetForegroundWindow");
        check(HellForgeForm.RestoreAndActivateWindow((IntPtr)42, _ => true, (_, cmd) => { if (cmd == 9) restored++; return true; }, _ => { activated++; return true; }) &&
            restored == 1 && activated == 1, "minimized HellForge window is restored (SW_RESTORE=9) before SetForegroundWindow");
        check(HellForgeForm.RestoreAndActivateWindow((IntPtr)43, _ => false, (_, _) => { restored++; return true; }, _ => { activated++; return true; }) &&
            restored == 1 && activated == 2, "non-minimized HellForge window calls SetForegroundWindow without extra ShowWindow");
    }
    static async Task WarbondProcessTests(string area, Action<bool, string> check)
    {
        var wbRoot = CreateForgeRoot(Path.Combine(area, "warbond-proc-hook"));
        SeedWarbondFixture(wbRoot, createReceipt: true);
        var cmds = new FakeCommands(wbRoot);
        Task<string> Backend(IReadOnlyList<string> args, CancellationToken ct) => Task.FromResult("""{"categories":{"level":{"vanilla":"1,000 XP"}}}""");
        foreach (var proc in new[] { "HellForge.Launcher", "HellForge", "helldivers2" })
        {
            HellForgeWarbonds.AnyProcessNamed = name => name == proc;
            check(HellForgeWarbonds.ManagerWindowOpen() == (proc != "helldivers2"), "ManagerWindowOpen detects only manager process " + proc);
            bool refused = false;
            try { await new HellForgeWarbonds(wbRoot, cmds, Backend).OpenAsync(); }
            catch (InvalidOperationException e) { refused = e.Message.Contains("HellForge window"); }
            check(refused, "HellForgeWarbonds AnyProcessNamed blocks opening when " + proc + " is running");
        }
        HellForgeWarbonds.AnyProcessNamed = _ => false;
        check(!HellForgeWarbonds.ManagerWindowOpen() && await new HellForgeWarbonds(wbRoot, cmds, Backend).OpenAsync() == 1,
            "closing injected process allows HellForgeWarbonds to unlock owned Warbonds");
    }
    static void States(string area, Action<bool, string> check)
    {
        var root = CreateForgeRoot(Path.Combine(area, "forge-clarity"));
        using var form = new HellForgeForm(new HellForgePreferences { InstallRoot = root, Mode = "regular" }, migrationNotice: "Updated to the new launcher.");
        var message = Field<Label>(form, "message"); var detail = Field<Label>(form, "detail"); var state = Field<Label>(form, "state");
        // Detach this label so Visible reflects its own flag without showing the form (which starts live polling).
        message.Parent!.Controls.Remove(message);
        using var detached = message;
        var mods = BuildReadyMods(root);
        var ready = new HellForgeStatus(root, HellForgeRelease.Version, HellForgePack.SupportedBuild, true, true, false, true, true, null, null);
        check(state.Text == "Checking HellForge…" && detail.Text == "Updated to the new launcher.",
            "startup update notice shows during initial Checking HellForge state");
        Set(form, "lastStatus", ready); Set(form, "lastMods", new HellForgeMods(mods, [], null));
        Set(form, "lastAccount", new HellForgeAccountState(true, true, "Imported")); View(form);
        check(state.Text == "Ready to launch" && detail.Text == "Updated to the new launcher." && !message.Visible,
            "startup update notice survives transition out of Checking HellForge onto first live headline");
        Set(form, "working", true); message.Text = "Preparing your pack"; View(form);
        check(message.Visible, "operation progress remains visible while working");
        Set(form, "working", false); message.Text = "Your Star Wars pack is installed."; View(form);
        check(detail.Text == message.Text && !message.Visible, "completed operation text appears once in the detail line");
        Set(form, "lastStatus", ready with { LastStatus = "failed", LastFailure = "The last game exited unexpectedly (code 42)" }); View(form);
        check(state.Text == "Last session needs attention" && message.Text == "" && detail.Text.Contains("Session details"), "a crash replaces stale success text with crash guidance");
        Set(form, "lastStatus", ready); View(form); message.Text = "Your session ended."; View(form);
        Set(form, "launcherRelease", new AppRelease { Version = "999.0.0" }); View(form);
        check(state.Text.Contains("Update available") && message.Text == "" && detail.Text.Contains("restart"), "a launcher update offer clears stale session text");
        message.Text = "Downloaded the launcher."; Set(form, "launcherReplaced", true); View(form);
        check(state.Text == "Update installed" && message.Text == "" && detail.Text.Contains("Reopen"), "an installed launcher update keeps its restart instruction");
        Set(form, "launcherRelease", null); Set(form, "captureBlock", "Account sync cleanup is pending"); View(form);
        var primary = Field<Button>(form, "launch");
        check(primary.Text == "OPEN HELLFORGE TO CLEAN UP" && primary.Enabled && !Field<bool>(form, "detailsAction") && detail.Text.Contains("close it"),
            "blocked account capture offers HellForge and explains the close-window fallback");
        string? opened = null; int steamOpens = 0;
        HellForgeForm.OpenManager = path => opened = path; HellForgeForm.OpenSteamClient = () => steamOpens++;
        typeof(Button).GetMethod("OnClick", Members)!.Invoke(primary, [EventArgs.Empty]);
        check(opened == root, "the capture-block primary action actually opens or activates the selected Forge manager");
        HellForgeForm.OpenManager = _ => throw new InvalidOperationException("Cannot activate fixture window");
        typeof(Button).GetMethod("OnClick", Members)!.Invoke(primary, [EventArgs.Empty]);
        check(detail.Text == "Close the HellForge window, then try account sync again.", "failed manager activation gives an actionable close-window instruction");

        opened = null;
        HellForgeForm.OpenManager = path => opened = path;
        HellForgeForm.SteamReadyCheck = () => false; View(form);
        check(primary.Text == "OPEN STEAM", "signed-out Steam takes precedence over captureBlock in UpdateState");
        HellForgeForm.SteamReadyCheck = () => true; View(form);

        using var detectedNoticeForm = new HellForgeForm(new HellForgePreferences { Mode = "regular" }, migrationNotice: "Forge is now the default launcher.");
        var detDetail = Field<Label>(detectedNoticeForm, "detail"); var detState = Field<Label>(detectedNoticeForm, "state");
        Field<HellForgePreferences>(detectedNoticeForm, "preferences").InstallRoot = root;
        Set(detectedNoticeForm, "lastStatus", ready); Set(detectedNoticeForm, "lastMods", new HellForgeMods(mods, [], null));
        Set(detectedNoticeForm, "lastAccount", new HellForgeAccountState(true, true, "Imported")); View(detectedNoticeForm);
        check(detState.Text == "Ready to launch" && detDetail.Text == "Forge is now the default launcher.",
            "startup notice also survives initial Detect transition from Let's get you ready to first live headline");
    }
    static async Task AsyncFlowTests(string area, Action<bool, string> check)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        HellForgeWarbonds.AnyProcessNamed = _ => false;
        HellForgeForm.SteamReadyCheck = () => true;
        HellForgeForm.CaptureBlockCheck = () => null;
        var root = CreateForgeRoot(Path.Combine(area, "forge-async-flows"));
        SeedWarbondFixture(root, createReceipt: false);
        var cmds = new FakeCommands(root);
        int previewCalls = 0;
        Task<string> Backend(IReadOnlyList<string> args, CancellationToken ct)
        {
            if (args[1] == "import-preview") { previewCalls++; return Task.FromResult("""{"categories":{"level":{"vanilla":"1,000 XP"}}}"""); }
            if (args[1] == "import")
            {
                File.WriteAllText(Path.Combine(root, "profiles", "default", "profile.json"),
                    """{"schemaVersion":1,"progression":{"experience":1000},"customization":{"items":[]},"inventory":{"items":[]}}""");
                return Task.FromResult("fixture imported");
            }
            throw new InvalidOperationException("Unexpected backend command: " + string.Join(' ', args));
        }
        HellForgeForm.CreateCommands = _ => cmds;
        HellForgeForm.CreateAccount = r => new HellForgeAccount(r, Backend);
        HellForgeForm.SelectAccountAction = (client, ct) => client.SelectAccountAsync(ct, reviewCapture: false);
        HellForgeForm.ImportAccountAction = async (client, ct) =>
        {
            await client.SelectAccountAsync(ct, reviewCapture: false);
            await new HellForgeAccount(client.Root, Backend).ImportAsync(ct);
            try { return await new HellForgeWarbonds(client.Root, cmds, Backend).OpenAsync(ct); }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or TimeoutException) { return 0; }
        };
        HellForgeForm.TryOpenWarbondsAction = async (client, ct) =>
        {
            try { return await new HellForgeWarbonds(client.Root, cmds, Backend).OpenAsync(ct); }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or TimeoutException) { return 0; }
        };
        HellForgeForm.OpenWarbondsAction = (client, ct) => new HellForgeWarbonds(client.Root, cmds, Backend).OpenAsync(ct);
        HellForgeForm.EnsureAccountAction = async (client, ct) =>
        {
            await client.SelectAccountAsync(ct, reviewCapture: false);
            var acc = await new HellForgeAccount(client.Root, Backend).ReadAsync(ct);
            if (!acc.Imported) throw new InvalidOperationException(acc.Message);
            return HellForgeAccount.Profile;
        };

        using var form = new HellForgeForm(new HellForgePreferences { InstallRoot = root, Mode = "regular" });
        SynchronizationContext.SetSynchronizationContext(null);
        var message = Field<Label>(form, "message"); var detail = Field<Label>(form, "detail"); var state = Field<Label>(form, "state");
        var primary = Field<Button>(form, "launch");
        message.Parent!.Controls.Remove(message);
        using var detached = message;

        // 1. #13: Sync prompt appears BEFORE OpenManager when capture is absent/not yet imported
        var snapshotMeta = Path.Combine(root, "account-snapshot", "default", "snapshot.json");
        var savedSnapshotMeta = File.ReadAllText(snapshotMeta);
        File.WriteAllText(snapshotMeta, """{"progression":false}""");
        await Invoke(form, "RefreshAsync");
        check(state.Text == "Import your HD2 account", "uncaptured fixture starts in Import your HD2 account");
        var syncOrder = new List<string>();
        HellForgeForm.ShowPrompt = (_, _, _, _, _) => { syncOrder.Add("prompt"); return DialogResult.OK; };
        HellForgeForm.OpenManager = _ => syncOrder.Add("open-manager");
        await Invoke(form, "SyncAccountAsync");
        check(syncOrder.SequenceEqual(new[] { "prompt", "open-manager" }),
            "sync instructions prompt precedes OpenManager so the modal dialog never covers the restored HellForge window");
        File.WriteAllText(snapshotMeta, savedSnapshotMeta);
        Set(form, "syncRequested", false);
        await Invoke(form, "RefreshAsync");
        check(state.Text == "Apply your account import", "restored capture enters Apply your account import");

        // 2. #6: Import while HellForge.Launcher is open defers Warbonds, reports it, blocks Launch, and retries on Refresh when window closes
        HellForgeWarbonds.AnyProcessNamed = name => name == "HellForge.Launcher";
        await Invoke(form, "SyncAccountAsync");
        check(state.Text == "Ready to launch" && Field<bool>(form, "warbondsPending") && !message.Visible &&
            detail.Text == "Account imported. Close the HellForge window to unlock your owned Warbonds.",
            "deferred Warbonds during import preserve headline-changing message telling player to close HellForge");
        await Invoke(form, "LaunchAsync");
        check(cmds.PlayCalls == 0 && Field<bool>(form, "warbondsPending") &&
            detail.Text == "Close the HellForge window before launching to unlock your owned Warbonds.",
            "LaunchAsync refuses to start while pending Warbonds are blocked by an open HellForge window");

        // Close HellForge.Launcher and verify RefreshAsync retries Warbonds immediately without re-running backend review
        HellForgeWarbonds.AnyProcessNamed = _ => false;
        int previewsBefore = previewCalls;
        await Invoke(form, "RefreshAsync");
        // One import-preview inside OpenAsync.VerifySnapshotAsync is expected; ReviewAccountAsync must NOT also run.
        check(!Field<bool>(form, "warbondsPending") && previewCalls == previewsBefore + 1 &&
            detail.Text == "Opened 1 owned Warbonds" &&
            JsonNode.Parse(File.ReadAllText(Path.Combine(root, "profiles", "default", "profile.json")))!["inventory"]!["items"]!.AsArray().Count == 1,
            "closing HellForge window causes immediate Warbond retry on next RefreshAsync within the 5-min cache window");

        // Verify LaunchAsync also retries pending Warbonds directly when window has closed
        Set(form, "warbondsPending", true);
        int warbondRetries = 0;
        var prevOpenWarbonds = HellForgeForm.OpenWarbondsAction;
        HellForgeForm.OpenWarbondsAction = (client, ct) => { warbondRetries++; return prevOpenWarbonds(client, ct); };
        await Invoke(form, "LaunchAsync");
        check(warbondRetries == 1 && !Field<bool>(form, "warbondsPending") && cmds.PlayCalls == 1,
            "LaunchAsync retries pending Warbonds before EnsureAccountAsync and play when manager is closed");
        HellForgeForm.OpenWarbondsAction = prevOpenWarbonds;

        // 3. #9: Headline-changing operation completion message (e.g. "Ready to launch") is preserved into detail
        await Invoke(form, "OperationAsync", (Func<Task>)(() => {
            Set(form, "lastAccount", new HellForgeAccountState(false, false, "reset"));
            message.Text = "Your Star Wars pack is installed.";
            return Task.CompletedTask;
        }));
        // Seed state so headline was NOT "Ready to launch" before OperationAsync
        Set(form, "lastAccount", new HellForgeAccountState(true, false, "Captured")); View(form);
        check(state.Text == "Apply your account import", "pre-operation state changes headline to Apply your account import");
        await Invoke(form, "OperationAsync", (Func<Task>)(() => {
            message.Text = "Your HD2 account is imported for all three modes.";
            return Task.CompletedTask;
        }));
        check(state.Text == "Ready to launch" && detail.Text == "Your HD2 account is imported for all three modes." && !message.Visible,
            "headline-changing operation preserves completion message into detail on first post-op view");

        // 4. #5: Failed operation survives subsequent 5-second RefreshAsync headline transition from Needs attention
        await Invoke(form, "OperationAsync", (Func<Task>)(() => throw new HttpRequestException(
            "An attempt was made to access a socket in a way forbidden by its access permissions.", new SocketException(10013))));
        check(state.Text == "Needs attention" && primary.Text == "RETRY" &&
            detail.Text == "GitHub didn't respond. Try again in a few minutes.",
            "failed operation shows Needs attention with socket network failure summary");
        await Invoke(form, "RefreshAsync");
        check(state.Text == "Ready to launch" && primary.Text == "LAUNCH REGULAR HELLFORGE" &&
            detail.Text == "GitHub didn't respond. Try again in a few minutes.",
            "subsequent successful RefreshAsync restores Ready to launch without wiping failed operation error");
        await Invoke(form, "RefreshAsync");
        check(detail.Text == "GitHub didn't respond. Try again in a few minutes.",
            "second RefreshAsync still preserves operationFailure until next user operation");
        await Invoke(form, "OperationAsync", (Func<Task>)(() => Task.CompletedTask));
        var readyStatus = Field<HellForgeStatus>(form, "lastStatus");
        check(OptionalField<string>(form, "operationFailure") == null &&
            detail.Text == "Windows may ask for network approval on your first launch. Choose difficulty 1–11 at the war table.",
            "starting next operation clears operationFailure and shows first-launch Ready to launch notice before first run");
        Set(form, "lastStatus", readyStatus with { HasRun = true }); View(form);
        check(detail.Text == "Choose difficulty 1–11 at the war table.",
            "once a session has run, Ready to launch hides the first-launch firewall notice");

        // 4b. F6 + F12 regressions: LaunchAcceptedMessage never masks a failure even from Last session needs attention; 512/408 non-HTTP numbers don't become GitHub outages.
        Set(form, "lastStatus", readyStatus with { LastStatus = "failed", LastFailure = "Process exited 42" }); View(form);
        Set(form, "message", message);
        message.Text = "HellForge accepted the launch and session status will update here."; View(form);
        check(state.Text == "Last session needs attention" && message.Text == "" && detail.Text.Contains("Session details"),
            "LaunchAcceptedMessage never masks LastFailure even when already displaying Last session needs attention");
        check(!HellForgeForm.FailureSummary("Copied 512 files in 408 ms").StartsWith("GitHub") &&
            HellForgeForm.FailureSummary("Response status code does not indicate success: 503 (Service Unavailable)").StartsWith("GitHub"),
            "non-HTTP 5xx/408 counts stay out of GitHub outages while real HTTP status failures still match");
        Set(form, "lastStatus", readyStatus with { HasRun = true }); View(form);

        // 4c. F16 CLONEDIVERS_FORGE_PREFS env var isolation seam.
        var customPrefsPath = Path.Combine(area, "isolated-prefs", "forge.json");
        var prevPrefsEnv = Environment.GetEnvironmentVariable("CLONEDIVERS_FORGE_PREFS");
        Environment.SetEnvironmentVariable("CLONEDIVERS_FORGE_PREFS", customPrefsPath);
        try
        {
            var custom = new HellForgePreferences { InstallRoot = root, Mode = "empire", Texture = "reduced512", EnemySkin = "mixed-droids" };
            custom.Save();
            var reloaded = HellForgePreferences.Load();
            check(File.Exists(customPrefsPath) && reloaded.Mode == "empire" && reloaded.Texture == "reduced512" && reloaded.EnemySkin == "mixed-droids" &&
                HellForgeSetup.DefaultRoot != root && HellForgeSetup.CacheRoot.StartsWith(Path.GetDirectoryName(customPrefsPath)!, StringComparison.OrdinalIgnoreCase),
                "CLONEDIVERS_FORGE_PREFS isolates preferences and setup cache from real user AppData");
        }
        finally { Environment.SetEnvironmentVariable("CLONEDIVERS_FORGE_PREFS", prevPrefsEnv); }

        // 5. #12: Unreadable hosts file (IOException / UnauthorizedAccessException) shows Session details rather than OPEN HELLFORGE
        HellForgeForm.CaptureBlockCheck = () => throw new IOException("hosts locked by security software");
        await Invoke(form, "RefreshAsync");
        check(Field<bool>(form, "captureBlockUnreadable") && state.Text == "Review account sync cleanup" &&
            detail.Text.Contains("hosts") && detail.Text.Contains("Session details") &&
            primary.Text == "VIEW DETAILS" && Field<bool>(form, "detailsAction") &&
            !Field<ToolStripMenuItem>(form, "accountSync").Enabled && !Field<ToolStripMenuItem>(form, "prepare").Enabled,
            "unreadable hosts file shows Session details guidance instead of OPEN HELLFORGE sync cleanup");
        HellForgeForm.CaptureBlockCheck = () => null;
        await Invoke(form, "RefreshAsync");
        check(!Field<bool>(form, "captureBlockUnreadable") && state.Text == "Ready to launch",
            "clearing unreadable hosts restores normal Ready to launch state");

        // 6. #8: captureBlock + signed-out Steam click invokes OpenSteamClient instead of OpenManager
        int steamClicks = 0, managerClicks = 0;
        HellForgeForm.OpenSteamClient = () => steamClicks++;
        HellForgeForm.OpenManager = _ => managerClicks++;
        Set(form, "captureBlock", "Account sync cleanup is pending");
        Set(form, "captureBlockUnreadable", false);
        HellForgeForm.SteamReadyCheck = () => false;
        View(form);
        check(primary.Text == "OPEN STEAM", "signed-out Steam takes precedence over captureBlock in UpdateState");
        Set(form, "refreshCompletion", null);
        typeof(Button).GetMethod("OnClick", Members)!.Invoke(primary, [EventArgs.Empty]);
        while (Field<bool>(form, "working") || Field<bool>(form, "refreshing")) await Task.Delay(5);
        if (OptionalField<TaskCompletionSource>(form, "refreshCompletion") is { } pendingClick)
            await pendingClick.Task;
        check(steamClicks == 1 && managerClicks == 0, "captureBlock + signed-out Steam click invokes Steam instead of OpenManager");
        HellForgeForm.SteamReadyCheck = () => true;
        Set(form, "captureBlock", null);
    }
}
