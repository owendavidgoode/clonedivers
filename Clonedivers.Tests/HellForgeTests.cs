using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace Clonedivers.Tests;

static class HellForgeTests
{
    internal static async Task FormLifecycleTests(Action<bool, string> check)
    {
        using (var firstRun = new HellForgeForm(new HellForgePreferences()))
        {
            var primary = (Button)typeof(HellForgeForm).GetField("launch", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(firstRun)!;
            check(primary.Enabled && primary.Text == "SET UP CLONEDIVERS", "a first-time player receives an enabled setup action without locating an engine or reading GitHub");
        }
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new HellForgeForm(new HellForgePreferences { InstallRoot = Path.GetTempPath() });
        var type = typeof(HellForgeForm);
        void Set(string name, object? value) => type.GetField(name, members)!.SetValue(form, value);
        var previousContext = SynchronizationContext.Current;
        var steamReady = HellForgeForm.SteamReadyCheck;
        SynchronizationContext.SetSynchronizationContext(null); // no message pump in this isolated fixture
        try
        {
            var poll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Set("refreshing", true);
            type.GetField("refreshCompletion", members)?.SetValue(form, poll);
            int actions = 0;
            Func<Task> action = () => { actions++; throw new InvalidOperationException("isolated action finished"); };
            Task Invoke() => (Task)type.GetMethod("OperationAsync", members)!.Invoke(form, [action])!;
            var request = Invoke();
            check(!request.IsCompleted && actions == 0, "a click during a status poll waits instead of disappearing");
            var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            type.GetMethod("OnFormClosing", members)!.Invoke(form, [closing]);
            check(closing.Cancel, "closing waits for a queued operation as well as a running one");
            await Invoke().ConfigureAwait(false);
            Set("refreshing", false); poll.SetResult();
            await request.ConfigureAwait(false);
            check(actions == 1, "a queued operation executes once and ignores a duplicate click");
            string? Diagnostic() => (string?)type.GetProperty("LauncherDiagnostic", members)!.GetValue(form);
            Set("lastError", null); // what the next successful status refresh does
            check(Diagnostic()?.Contains("isolated action finished") == true, "the failed operation's full diagnostic survives the next refresh for Session details");

            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "fixture", HellForgePack.SupportedBuild,
                false, true, true, true, true, "observing", null));
            Set("lastError", "Mods listing failed during an active session");
            Set("launcherRelease", new AppRelease { Version = "999.0.0", Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v999.0.0/Clonedivers.exe", Size = 1, Sha256 = new string('a', 64) });
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            var end = (Button)type.GetField("launch", members)!.GetValue(form)!;
            check(end.Enabled && end.Text == "END SESSION", "a mod/status-detail error cannot hide End session for a known active runner");
            HellForgeForm.SteamReadyCheck = () => false;
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "END SESSION", "Steam going away cannot hide cleanup for a known active session");
            Set("launcherRelease", null);
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "fixture", HellForgePack.SupportedBuild, true, true, false, true, true, null, null));
            Set("lastError", null);
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "OPEN STEAM", "missing Steam offers client startup instead of advertising game readiness");
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "fixture", HellForgePack.SupportedBuild, true, true, false, true, true, null, null,
                RecoveryProblem: "Fixture interrupted appearance; recover the previous selection."));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            var recover = (ToolStripMenuItem)type.GetField("prepare", members)!.GetValue(form)!;
            var sync = (ToolStripMenuItem)type.GetField("accountSync", members)!.GetValue(form)!;
            check(recover.Enabled && recover.Text == "RECOVER PREVIOUS APPEARANCE" && end.Enabled && end.Text == "RECOVER PREVIOUS APPEARANCE" && !sync.Enabled,
                "interrupted appearance uses the primary action for recovery while account import stays blocked, even without Steam");
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "fixture", HellForgePack.SupportedBuild, false, true, true, true, true, null, null,
                RecoveryProblem: "Fixture interrupted appearance."));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "END SESSION" && !recover.Enabled, "durable recovery notices never hide End session for an active game");
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "fixture", HellForgePack.SupportedBuild, true, true, false, true, true, null, null,
                CompatibilityProblem: "Fixture engine/SDK/isolation mismatch."));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "VIEW DETAILS" && !recover.Enabled && !sync.Enabled, "incompatible engine/SDK/isolation offers details while blocking launch, preparation and account import");
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "fixture", HellForgePack.SupportedBuild, false, true, true, true, true, null, null,
                CompatibilityProblem: "Fixture engine/SDK/isolation mismatch."));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "END SESSION", "compatibility mismatch never hides active-session cleanup");
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "0.4.0-beta (4a5373b)", HellForgePack.SupportedBuild, false, true, false, true, true, null, null,
                CompatibilityProblem: "Official WFP isolation has not been set up yet."));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "FINISH SETUP" && !recover.Enabled && !sync.Enabled, "fresh pinned Forge offers guided setup while copy/isolation and account steps remain incomplete");
            HellForgeForm.SteamReadyCheck = () => true;
            var selection = (HellForgePreferences)type.GetField("preferences", members)!.GetValue(form)!;
            selection.Mode = "regular";
            var readyMods = HellForgePreset.Required.Select(item => new HellForgeMod(item.Id, item.Name, true, "fixture", [], item.Version,
                item.Id == HellForgePreset.GuardDogs ? [new("Smarter Guard Dogs and Sentries", true), new("Guard Dogs", true), new("Sentries", true), new("Intelligence", true), new("Safety", true), new("Sentry Safety", true), new("SEAF Safety", true), new("Targeting Marker", false)] : null)).ToList();
            readyMods.Add(new("skyeshade.vanilla-plus", "Vanilla Plus", true, "fixture", []));
            readyMods.Sort((a, b) => HellForgeGameplayCatalog.Pinned.Order.IndexOf(a.Id).CompareTo(HellForgeGameplayCatalog.Pinned.Order.IndexOf(b.Id)));
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "fixture", HellForgePack.SupportedBuild, true, true, false, true, true, null, null));
            Set("lastMods", new HellForgeMods(readyMods, [], null));
            foreach (var issue in new[] { "difficulty", "order", "sharing", "overlap", "notices" })
            {
                var drift = readyMods.ToList();
                if (issue == "difficulty") { var index = drift.FindIndex(m => m.Id == HellForgeClient.DifficultyId); drift[index] = drift[index] with { Enabled = false }; }
                if (issue == "order") drift.Reverse();
                if (issue == "overlap") drift.Add(new("pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91", "Reinforcement Beacons Fixed", true, "fixture", []));
                Set("lastMods", new HellForgeMods(drift, issue == "notices" ? ["Duplicate custom mod ID"] : [], null, issue == "sharing" ? HellForgePreset.SharingProblem(null) : null));
                type.GetMethod("UpdateView", members)!.Invoke(form, null);
                check(end.Enabled && end.Text == (issue == "notices" ? "REVIEW MOD CHOICES" : "INSTALL REQUIRED MODS"),
                    issue + " has an enabled main action for repair or an explained player choice");
            }
            Set("lastMods", new HellForgeMods(readyMods, [], null));
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), HellForgeRelease.Version, HellForgePack.SupportedBuild, true, true, false, true, true, null, null,
                CompatibilityProblem: "Continue guided setup to enable the official sandbox.", Engine: HellForgeRelease.Engine, ModSdk: HellForgeRelease.Sdk));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "CONTINUE GUIDED SETUP", "sandbox setup has a reachable guided repair with the pinned engine");
            Set("lastStatus", new HellForgeStatus(Path.GetTempPath(), "fixture", HellForgePack.SupportedBuild, true, true, false, true, true, null, null));
            Set("lastAccount", new HellForgeAccountState(false, false, "No verified capture in default."));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "SYNC HD2 ACCOUNT", "missing account capture offers an enabled next action instead of a grey launch button");
            Set("lastAccount", new HellForgeAccountState(true, false, "Verified capture ready."));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "FINISH ACCOUNT SYNC", "verified capture offers direct completion from the primary button");
            readyMods.Add(new(HellForgePack.PackId("commandos"), "Republic", true, "fixture", []));
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "SET UP SELECTED MODE", "Regular mode needing appearance removal offers setup from its primary button");
            Set("captureBlock", "Sync is still forwarding the normal game.");
            Set("launcherRelease", new AppRelease { Version = "999.0.0", Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v999.0.0/Clonedivers.exe", Size = 1, Sha256 = new string('a', 64) });
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "VIEW DETAILS" && !recover.Enabled && !sync.Enabled, "account capture cleanup offers read-only details while automatic import and setup stay blocked");
            Set("captureBlock", null);
            Set("updateFailed", true); Set("failureDetail", "Response status code does not indicate success: 504 (Gateway Time-out).");
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Enabled && end.Text == "RETRY UPDATE", "a failed verified update offers an obvious retry after account capture finishes");
            Set("lastError", null); // the periodic successful poll must not erase the update failure
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Text == "RETRY UPDATE", "update failure survives a successful background status poll");
            Set("lastError", "System.IO.IOException: unrelated status read failure");
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            var updateDetail = (Label)type.GetField("detail", members)!.GetValue(form)!;
            check(end.Text == "RETRY UPDATE" && updateDetail.Text.Contains("GitHub") && Diagnostic()?.Contains("504") == true,
                "a later status failure retains the failed update diagnostic and recovery message");
            Set("lastError", null);
            Set("updateDeferred", true);
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            check(end.Text == "SET UP SELECTED MODE", "deferring an update restores the existing setup or play action");
            Set("launcherRelease", null); Set("updateFailed", false);
            type.GetMethod("UpdateView", members)!.Invoke(form, null);
            bool offered = end.Enabled, reenabled = false;
            EventHandler watch = (_, _) => reenabled |= end.Enabled && end.Text.StartsWith("LAUNCH");
            end.EnabledChanged += watch;
            await ((Task)type.GetMethod("OperationAsync", members)!.Invoke(form, [(Func<Task>)(() => Task.CompletedTask)])!).ConfigureAwait(false);
            end.EnabledChanged -= watch;
            check(offered && !reenabled, "a finished launch cannot re-offer Launch before the follow-up status read replaces the pre-launch status");
            check(Diagnostic()?.Contains("504") != true && Diagnostic()?.Contains("isolated action finished") != true, "a new operation retires the previous failure's diagnostic even when the follow-up refresh fails");
            check(HellForgeForm.SyncInstructions.Contains("Apply import") && HellForgeForm.SyncInstructions.Contains("auto-apply"),
                "sync instructions keep players from applying HellForge's own import, which would refuse the Clonedivers link");
            CancellationToken downloadToken = default;
            Func<Task> download = () => {
                type.GetProperty("Cancellable", members)!.SetValue(form, true);
                downloadToken = (CancellationToken)type.GetProperty("WorkToken", members)!.GetValue(form)!;
                return Task.Delay(Timeout.Infinite, downloadToken);
            };
            var downloading = (Task)type.GetMethod("OperationAsync", members)!.Invoke(form, [download])!;
            check(end.Enabled && end.Text == "CANCEL", "cancellable work makes Cancel the single primary action");
            typeof(Button).GetMethod("OnClick", members)!.Invoke(end, [EventArgs.Empty]);
            await downloading.ConfigureAwait(false);
            check(downloadToken.IsCancellationRequested && type.GetField("operation", members)!.GetValue(form) == null,
                "the primary Cancel action interrupts work and releases its cancellation source");
        }
        finally { HellForgeForm.SteamReadyCheck = steamReady; SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    internal static void FormLayoutTests(Action<bool, string> check)
    {
        var futureSteamRelease = new AppRelease { Version = "999.0.0", Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v999.0.0/Clonedivers.exe", Size = 1, Sha256 = new string('a', 64) };
        using (var fallback = new MainForm(new Settings(), null, preview: true, allowAppUpdates: false))
            check(!fallback.CanUpdateApp(futureSteamRelease), "Forge's Steam fallback cannot replace the Forge executable with a future legacy-channel release");
        using (var legacy = new MainForm(new Settings(), null, preview: true))
            check(legacy.CanUpdateApp(futureSteamRelease), "the existing Steam edition retains its valid pinned self-update policy");
        var ownershipFailure = "Steam ownership was not confirmed: Start Steam and sign in.\r\nAt D:\\HellForge\\app\\scripts\\Test-SteamOwnership.ps1:10 char:69\r\nCategoryInfo: OperationStopped";
        var summary = HellForgeForm.FailureSummary(ownershipFailure);
        check(summary.Contains("Steam") && summary.Contains("Session details") && !summary.Contains("CategoryInfo") && !summary.Contains("char:69"),
            "the launch failure remains actionable without pushing the main controls off-screen with a PowerShell stack");
        check(HellForgeForm.FailureSummary(new string('x', 1000)).Length <= 350, "long failure summaries stay bounded while full diagnostics remain in session logs");
        foreach (var code in new[] { 500, 502, 503, 504, 508, 408, 429 })
            check(HellForgeForm.FailureSummary($"Response status code does not indicate success: {code} (fixture).") == "GitHub didn't respond. Try again in a few minutes.",
                "HTTP " + code + " has a plain-language network failure");
        check(!HellForgeForm.FailureSummary("System.InvalidOperationException: secret diagnostic\r\n at fixture:17").Contains("secret diagnostic") &&
            HellForgeForm.FailureSummary("SHA-256 mismatch").Contains("verified") && HellForgeForm.FailureSummary("Access denied").Contains("permissions"),
            "unknown failures keep diagnostics out of the main view and common integrity/access errors stay actionable");
        Exception? failure = null;
        var thread = new Thread(() => {
            try
            {
                using var form = new HellForgeForm(new HellForgePreferences(), preview: true);
                form.SetPreviewState("ready"); form.Show(); Application.DoEvents();
                var defaultClientSize = form.ClientSize;
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                var footer = form.Controls.Cast<Control>().SelectMany(Children).OfType<Label>()
                    .Single(c => c.AccessibleName == "Launcher version");
                check(footer.Text == $"v{AppInfo.Version}", "the footer keeps only the version beside the Steam fallback");
                var fullTextures = (RadioButton)typeof(HellForgeForm).GetField("fullTextures", fields)!.GetValue(form)!;
                var lighterTextures = (RadioButton)typeof(HellForgeForm).GetField("lighterTextures", fields)!.GetValue(form)!;
                var preferences = (HellForgePreferences)typeof(HellForgeForm).GetField("preferences", fields)!.GetValue(form)!;
                check(fullTextures.Checked && !lighterTextures.Checked && fullTextures.Parent!.AccessibleName == "Texture profile",
                    "the texture profile retains its accessible name and exposes the selected Full segment");
                lighterTextures.Checked = true;
                check(preferences.Texture == "lighter" && preferences.SelectionKey.Contains("/lighter/") && !fullTextures.Checked &&
                    lighterTextures.FlatAppearance.BorderColor == Palette.BlueHot, "the Lighter segment selects and highlights the streamed profile");
                fullTextures.Checked = true;
                check(preferences.Texture == "full" && !lighterTextures.Checked && preferences.DeltaSquad,
                    "the Full segment restores full textures without altering appearance options");
                var primaryAction = (Button)typeof(HellForgeForm).GetField("launch", fields)!.GetValue(form)!;
                var appearanceHeading = (Label)typeof(HellForgeForm).GetField("optionsLabel", fields)!.GetValue(form)!;
                var actionTop = primaryAction.PointToScreen(Point.Empty).Y;
                foreach (var mode in new[] { "Empire", "Regular", "Clones" })
                {
                    form.Controls.Cast<Control>().SelectMany(Children).OfType<ModeButton>().Single(b => b.Text == mode).PerformClick();
                    Application.DoEvents();
                    check(appearanceHeading.Text == mode + " appearance" && primaryAction.PointToScreen(Point.Empty).Y == actionTop,
                        mode + " keeps a consistently labelled appearance block and a stable action position");
                    if (mode == "Empire")
                    {
                        check(fullTextures.Enabled && lighterTextures.Enabled, "Empire enables both texture segments");
                        lighterTextures.Checked = true;
                        check(preferences.Selection().Texture == "lighter" && preferences.SelectionKey.Contains("/empire/lighter/"),
                            "the Empire Lighter segment reaches the pinned streamed selection");
                        fullTextures.Checked = true;
                    }
                }
                var more = form.Controls.Cast<Control>().SelectMany(Children).OfType<Button>().Single(b => b.AccessibleName == "More launcher options");
                check(more.FlatAppearance.BorderSize == 1 && more.Font.Size == fullTextures.Font.Size && more.Focus(),
                    "More options is an outlined body-size button with keyboard focus");
                form.SetPreviewState("update-available"); Application.DoEvents();
                var actionRow = (Control)typeof(HellForgeForm).GetField("actions", fields)!.GetValue(form)!;
                var rowCenter = actionRow.PointToScreen(new Point(actionRow.Width / 2, 0)).X;
                var primaryCenter = primaryAction.PointToScreen(new Point(primaryAction.Width / 2, 0)).X;
                var notNow = (Button)typeof(HellForgeForm).GetField("later", fields)!.GetValue(form)!;
                check(Math.Abs(rowCenter - primaryCenter) <= 2 && notNow.Text == "Not now" && notNow.FlatAppearance.BorderSize == 1 &&
                    actionRow.PointToScreen(Point.Empty).Y >= primaryAction.PointToScreen(new Point(0, primaryAction.Height)).Y,
                    "update secondary actions share one centred outlined row directly beneath the primary action");
                form.SetPreviewState("ready"); Application.DoEvents();
                var bottom = form.PointToClient(footer.PointToScreen(new Point(0, footer.Height)));
                check(bottom.Y <= form.ClientSize.Height, "the default Forge layout keeps version and player readiness visible without scrolling");
                var policyDetail = (Label)typeof(HellForgeForm).GetField("detail", fields)!.GetValue(form)!;
                var previousDetail = policyDetail.Text;
                policyDetail.Text = HellForgePreset.SharingProblem(null); Application.DoEvents();
                check(form.PointToClient(footer.PointToScreen(new Point(0, footer.Height))).Y <= form.ClientSize.Height,
                    "the real private-preset sharing instruction keeps the footer visible at the default window size");
                policyDetail.Text = previousDetail; Application.DoEvents();
                form.MinimumSize = Size.Empty;
                form.ClientSize = new Size(760, 600); Application.DoEvents();
                var shortBottom = form.PointToClient(footer.PointToScreen(new Point(0, footer.Height)));
                if (shortBottom.Y > form.ClientSize.Height || shortBottom.Y <= 0)
                    Console.WriteLine($"LAYOUT: client={form.ClientSize}, footerBottom={shortBottom.Y}, footerParent={footer.Parent!.Bounds}, dock={footer.Parent.Dock}, dpi={form.DeviceDpi}");
                check(shortBottom.Y <= form.ClientSize.Height && shortBottom.Y > 0,
                    "a short screen keeps the Steam fallback and version outside the scrolling content");
                var message = (Label)typeof(HellForgeForm).GetField("message", fields)!.GetValue(form)!;
                var activity = (Control)typeof(HellForgeForm).GetField("activity", fields)!.GetValue(form)!;
                message.Text = "Could not open HellForge. Check the selected folder."; Application.DoEvents();
                check(activity.Visible && message.Visible, "a diagnostic raised outside an operation reveals its message instead of hiding it");
                form.ClientSize = new Size(760, 740); message.Text = string.Join(" ", Enumerable.Repeat("Review the selected installation before retrying.", 20));
                Application.DoEvents();
                var scroller = (ScrollableControl)form.Controls[0];
                check(!scroller.HorizontalScroll.Visible, "compact windows wrap long diagnostics instead of requiring horizontal scrolling");
                form.SetPreviewState("running");
                check(!((ToolStripMenuItem)typeof(HellForgeForm).GetField("accountSync", fields)!.GetValue(form)!).Enabled,
                    "running preview matches the real account-sync lock during a session");
                form.ClientSize = defaultClientSize; Application.DoEvents();
                foreach (var preview in HellForgeForm.PreviewStates)
                {
                    form.SetPreviewState(preview); Application.DoEvents();
                    var primary = (Button)typeof(HellForgeForm).GetField("launch", fields)!.GetValue(form)!;
                    var bar = (ProgressBar)typeof(HellForgeForm).GetField("progress", fields)!.GetValue(form)!;
                    check(primary.Visible && form.PointToClient(primary.PointToScreen(new Point(0, primary.Height))).Y < form.ClientSize.Height &&
                        !form.Controls.Cast<Control>().SelectMany(Children).OfType<Button>().Any(b => b.Text == "SET UP SELECTED MODE"),
                        preview + " keeps a single visible primary path and setup in More");
                    check(bar.Visible == (preview is "installing" or "update-downloading"), preview + " exposes progress only while working");
                    check(!((ScrollableControl)form.Controls[0]).VerticalScroll.Visible, preview + " fits the tightened default window without scrolling");
                }
                form.SetPreviewState("update-failed");
                check(policyDetail.Text.Contains("Nothing was changed") && !policyDetail.Text.Contains("504"), "failed updates explain recovery without exposing the raw gateway exception");
                var menu = (ContextMenuStrip)typeof(HellForgeForm).GetField("moreMenu", fields)!.GetValue(form)!;
                check(menu.Items.OfType<ToolStripMenuItem>().Any(i => i.AccessibleName == "Sync HD2 account") &&
                    menu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text == "Session details"), "maintenance and account sync retain keyboard/screen-reader menu entries");
                form.Close(); form.Dispose();
                check(form.IsDisposed, "closing and repeated disposal release launcher resources without a disposed-token exception");
                using var setup = new HellForgeSetupForm(preview: true);
                setup.ClientSize = new Size(620, 480); setup.Show(); Application.DoEvents();
                var setupNext = setup.Controls.Cast<Control>().SelectMany(Children).OfType<Button>().Single(b => b.AccessibleName == "Continue Forge setup");
                check(setupNext.Text == "INSTALL FORGE" && setup.PointToClient(setupNext.PointToScreen(new Point(0, setupNext.Height))).Y <= setup.ClientSize.Height,
                    "small first-run windows keep the install action visible outside the scrolling instructions");
                setup.ClientSize = new Size(700, 830);
                var qrRows = Enumerable.Repeat(new string('\u2588', 29), 15).ToArray();
                typeof(HellForgeSetupForm).GetMethod("SetApproval", fields)!.Invoke(setup, [SteamSetupOutput.RenderQr(qrRows)]); Application.DoEvents();
                var qrControl = (PictureBox)typeof(HellForgeSetupForm).GetField("approval", fields)!.GetValue(setup)!;
                var qrParent = qrControl.Parent!;
                var qrTop = qrParent.PointToClient(qrControl.PointToScreen(Point.Empty));
                check(qrControl.Visible && qrTop.Y >= 0 && qrTop.Y + qrControl.Height <= qrParent.ClientSize.Height,
                    "Steam approval scrolls the complete integer-scale QR into the setup viewport");
                setup.Close();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new InvalidOperationException("Forge layout fixture failed.", failure);

        static IEnumerable<Control> Children(Control control) => new[] { control }.Concat(control.Controls.Cast<Control>().SelectMany(Children));
    }

    internal static ProcessStartInfo PipeFixtureStart(string mode, bool redirect = true)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = redirect, RedirectStandardError = redirect
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(mode);
        return start;
    }

    static async Task PipeInheritanceTest(Action<bool, string> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var response = await HellForgeCommands.ReadJsonProcessAsync(PipeFixtureStart("--hellforge-pipe-parent"), true, timeout.Token);
        using var child = Process.GetProcessById(response.GetProperty("pid").GetInt32());
        try
        {
            check(response.GetProperty("event").GetString() == "started" && !child.HasExited,
                "launch acknowledgement returns while the session child still holds stdout and stderr open");
        }
        finally { if (!child.HasExited) child.Kill(); }
        var status = await HellForgeCommands.ReadJsonProcessAsync(PipeFixtureStart("--hellforge-pipe-json"), false, timeout.Token);
        check(status.GetProperty("ok").GetBoolean(), "ordinary CLI commands still read multiline JSON completely");
        try
        {
            await HellForgeCommands.ReadJsonProcessAsync(PipeFixtureStart("--hellforge-pipe-error"), true, timeout.Token);
            check(false, "failed CLI launch preserves stderr diagnostics");
        }
        catch (IOException error) { check(error.Message.Contains("fixture failure"), "failed CLI launch preserves stderr diagnostics"); }
        static async Task<JsonElement> Hang(CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); return default; }
        Exception? deadline = null, cancel = null;
        try { await HellForgeCommands.TimedAsync("mods", TimeSpan.FromMilliseconds(50), Hang, CancellationToken.None); } catch (Exception e) { deadline = e; }
        check(deadline is TimeoutException && deadline.Message.Contains("'mods'"), "a CLI deadline is a named timeout, never swallowed or shown as a player cancel");
        using (var player = new CancellationTokenSource(50))
            try { await HellForgeCommands.TimedAsync("mods", TimeSpan.FromMinutes(2), Hang, player.Token); } catch (Exception e) { cancel = e; }
        check(cancel is OperationCanceledException, "a real cancellation still arrives as a cancellation");
    }
    public static async Task Run(string root, Action<bool, string> check)
    {
        Console.WriteLine("HellForge pack isolation, import and session boundaries");
        await PipeInheritanceTest(check);
        await AccountProcessLifetimeTests(root, check);
        var area = Path.Combine(root, "hellforge-tests"); Directory.CreateDirectory(area);
        var source = Path.Combine(area, "blobs"); Directory.CreateDirectory(source);
        PackFile FileRow(string name, string text, string[]? modes = null, string? option = null)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(source, sha), bytes);
            return new() { Name = name, Size = bytes.Length, Sha256 = sha, Modes = modes?.ToList(), Option = option,
                Url = "https://github.com/owendavidgoode/clonedivers/releases/download/test/" + sha };
        }
        var feed = new Manifest { Format = 3, Pack = new PackManifest { Version = "test", GameBuild = HellForgePack.SupportedBuild,
            Options = [new() { Id = "empire" }, new() { Id = "extra" }],
            TextureProfiles = [new() { Id = "full", Name = "Full" }], Files = [
                FileRow("9ba626afa44a3aa3.patch_0", "clone", ["clonedivers"]),
                FileRow("9ba626afa44a3aa3.patch_0", "empire", ["empire"]),
                FileRow("9ba626afa44a3aa3.patch_1", "extra", ["clonedivers"], "extra"),
                FileRow("9ba626afa44a3aa3.patch_2", "last-layer", ["clonedivers"]),
                FileRow("9ba626afa44a3aa3.patch_2.gpu_resources", "", ["clonedivers"])
            ] } };
        var manifestJson = JsonSerializer.Serialize(feed);
        var parsed = Manifest.Parse(manifestJson);
        var sha = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifestJson))).ToLowerInvariant();
        var selection = HellForgePack.Select(parsed, sha, "clonedivers", "full", new Dictionary<string, bool> { ["extra"] = false });
        check(selection.Files.Count == 3 && selection.Files[1].Name.EndsWith("patch_1") && selection.Files[2].Name.EndsWith("patch_1.gpu_resources"),
            "HellForge export preserves selected mode, layer order and empty companions when options remove a layer");
        var output = Path.Combine(area, "export");
        var plan = await HellForgePack.PlanAsync(selection, output, [source]);
        await HellForgePack.ExportAsync(plan, false);
        await HellForgePack.VerifyAsync(output, selection);
        check(File.ReadAllText(Path.Combine(output, "9ba626afa44a3aa3.patch_0")) == "clone" && plan.Missing.Count == 0,
            "shared exporter reuses hash-verified local bytes and verifies the complete output");

        async Task<bool> Reject(Func<Task> action)
        {
            try { await action(); return false; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException or JsonException) { return true; }
        }
        File.WriteAllText(Path.Combine(output, "hellforge-mod.json"), "{}");
        check(await Reject(() => HellForgePack.VerifyAsync(output, selection)), "an added native mod manifest cannot masquerade as the verified cosmetic pack");
        File.Delete(Path.Combine(output, "hellforge-mod.json"));
        var firstFile = Path.Combine(output, "9ba626afa44a3aa3.patch_0");
        File.WriteAllText(firstFile, "cl0ne");
        check(await Reject(() => HellForgePack.VerifyAsync(output, selection)), "equal-length tampering is checked against trusted catalog hashes");
        File.Copy(Path.Combine(source, selection.Files[0].Sha256), firstFile, true);
        var empire = HellForgePack.Select(parsed, sha, "empire", "full");
        check(await Reject(() => HellForgePack.VerifyAsync(output, empire)), "a Clone export cannot satisfy an Empire selection");
        var empireVariants = new Manifest { Format = 3, Pack = new PackManifest { Version = "fixture-r45", GameBuild = HellForgePack.SupportedBuild,
            Options = [new() { Id = "empire" }], TextureProfiles = [new() { Id = "full", Name = "Full" }, new() { Id = "lighter", Name = "Lighter" }],
            Files = [FileRow("9ba626afa44a3aa3.patch_1000", "shared", ["empire"]),
                FileRow("9ba626afa44a3aa3.patch_1000.gpu_resources", "full-gpu", ["empire"]),
                FileRow("9ba626afa44a3aa3.patch_1000.gpu_resources", "tail-gpu", ["empire"])] } };
        empireVariants.Pack.Files[1].TextureProfiles = ["full"]; empireVariants.Pack.Files[2].TextureProfiles = ["lighter"];
        var fullVariant = HellForgePack.Select(empireVariants, sha, "empire", "full");
        var lightVariant = HellForgePack.Select(empireVariants, sha, "empire", "lighter");
        var exports = Path.Combine(area, "exports"); Directory.CreateDirectory(exports);
        var interrupted = Path.Combine(exports, "empire-" + Guid.NewGuid().ToString("N"));
        var wrong = Path.Combine(area, "wrong-gpu"); File.WriteAllText(wrong, "bad-data");
        var failedPlan = new HellForgePack.Plan(fullVariant, interrupted, new() {
            [fullVariant.Files[0].Sha256] = Path.Combine(source, fullVariant.Files[0].Sha256), [fullVariant.Files[1].Sha256] = wrong });
        check(await Reject(() => HellForgePack.ExportAsync(failedPlan, false)) &&
            File.Exists(Path.Combine(interrupted, fullVariant.Files[0].Name)) && !File.Exists(Path.Combine(interrupted, HellForgePack.ReceiptName)),
            "an interrupted Empire Full export retains only verified bytes without a ready receipt");
        var tails = Path.Combine(area, "tails"); Directory.CreateDirectory(tails);
        File.Copy(Path.Combine(source, lightVariant.Files[1].Sha256), Path.Combine(tails, "tail"));
        var resumed = Path.Combine(exports, "empire-" + Guid.NewGuid().ToString("N"));
        var resumedPlan = await HellForgePack.PlanAsync(lightVariant, resumed, [tails]);
        check(resumedPlan.Missing.Count == 0 && resumedPlan.Local[lightVariant.Files[0].Sha256].StartsWith(interrupted) &&
            !resumedPlan.Local.ContainsKey(fullVariant.Files[1].Sha256),
            "Empire Lighter recovery reuses shared verified bytes while excluding Full GPU content");
        await HellForgePack.ExportAsync(resumedPlan, false);
        await HellForgePack.VerifyAsync(resumed, lightVariant);
        check(await Reject(() => HellForgePack.VerifyMetadataAsync(resumed, fullVariant)),
            "an Empire Lighter receipt cannot satisfy the Full selection");
        check(!Directory.Exists(interrupted), "a complete Empire Lighter export prunes the reused interrupted Full export");
        check(await Reject(async () => { await HellForgePack.PlanAsync(selection, output, [source]); }), "existing pack outputs are never overwritten");
        parsed.Pack!.GameBuild = "26251122";
        check(await Reject(() => { HellForgePack.Select(parsed, sha, "empire", "full"); return Task.CompletedTask; }), "current Steam-build assets are refused for the older HellForge build");

        var install = Path.Combine(area, "installation"); Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");
        Directory.CreateDirectory(Path.Combine(install, "app", "cli")); Directory.CreateDirectory(Path.Combine(install, "mods"));
        File.WriteAllText(Path.Combine(install, "app", "cli", "HellForge.Cli.exe"), "fixture, never executed");
        File.WriteAllText(Path.Combine(install, "HellForge.exe"), "fixture, never executed");
        check(HellForgeCommands.ValidateRoot(install) == install, "a separate installation is identified by marker and official entry points");
        check(await Reject(async () => { await HellForgePack.PlanAsync(selection, Path.Combine(install, "game", "data", "bad"), [source]); }),
            "exports cannot write inside HellForge, including its game copy");
        var steamRoot = Path.Combine(area, "steamapps", "common", "HellForge");
        check(await Reject(() => { HellForgeCommands.ValidateRoot(steamRoot); return Task.CompletedTask; }), "HellForge cannot target a Steam installation");

        var commands = new FakeCommands(install);
        commands.Mods.Add(new(HellForgePack.PackId("empire"), "Prior Empire", true, Path.Combine(install, "mods", "empire"), ["pack"]));
        // This fake owns no operating-system CLI; real manager polling must not
        // interfere with an isolated transaction fixture.
        var client = new HellForgeClient(install, commands) { RequireCliQuiescent = () => { } };
        var unpacked = Path.Combine(install, "mods", ".unpacked"); Directory.CreateDirectory(unpacked);
        var fingerprint = client.ModsFingerprint();
        var oldPartial = Path.Combine(unpacked, ".zip-hashes.json.0a1b2c3d.partial"); File.WriteAllText(oldPartial, "{}");
        File.SetLastWriteTimeUtc(oldPartial, DateTime.UtcNow.AddHours(-2));
        var freshPartial = Path.Combine(unpacked, ".zip-hashes.json.4e5f6a7b.partial"); File.WriteAllText(freshPartial, "{}");
        var decoy = Path.Combine(unpacked, ".zip-hashes.json.notours.partial"); File.WriteAllText(decoy, "{}");
        File.SetLastWriteTimeUtc(decoy, DateTime.UtcNow.AddHours(-2));
        var hashes = Path.Combine(unpacked, ".zip-hashes.json"); File.WriteAllText(hashes, "{}");
        File.SetLastWriteTimeUtc(hashes, DateTime.UtcNow.AddHours(-2));
        check(client.ModsFingerprint() == fingerprint, "HellForge's own .unpacked cache writes do not trigger another mod scan");
        check(client.SweepHashPartials(DateTime.UtcNow.AddMinutes(-10)) == 1 && !File.Exists(oldPartial) && File.Exists(freshPartial) && File.Exists(decoy) && File.Exists(hashes),
            "idle cleanup removes only old leaked .zip-hashes.json partials, never the live cache or other names");
        File.WriteAllText(Path.Combine(install, "mods", "hellforge-mods.json"), "{}");
        check(client.ModsFingerprint() != fingerprint, "a mod settings change triggers a fresh mod scan");
        File.Delete(Path.Combine(install, "mods", "hellforge-mods.json")); Directory.Delete(unpacked, true);
        var diagnosticRun =Path.Combine(install, "logs", "sessions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(diagnosticRun);
        commands.LastRun = diagnosticRun;
        var resultPath = Path.Combine(diagnosticRun, "result.json");
        File.WriteAllText(resultPath, """{"status":"observed","completedUtc":"2026-11-03T17:58:43Z","exitCode":1}""");
        check((await client.StatusAsync()).LastFailure?.Contains("code 1") == true,
            "observed runner completion still reports a nonzero game exit");
        var gameLogFolder = Path.Combine(diagnosticRun, "game");
        Directory.CreateDirectory(gameLogFolder);
        var gameLog = Path.Combine(gameLogFolder, "modloader.log");
        const string quitCrash = "17:58:40.437 crash: c0000005 on thread 33084 at helldivers2.exe+0x626296 (0x7ff628df6296), reading 0x2e06ba97820\n" +
            "17:58:40.437 crash: by module: at helldivers2.exe+0x626296; stack outside the game: none\n" +
            "17:58:40.658 crash: fail-fast about to end the game (thread 33084, asked from dbgcore.DLL+0x129d2)\n";
        File.WriteAllText(gameLog, quitCrash);
        var quitStatus = await client.StatusAsync();
        check(quitStatus.LastFailure == null && quitStatus.LastNotice?.Contains("documented quit-time") == true,
            "the pinned game's documented quit fault remains visible as a notice rather than a mission failure");
        commands.Build = "26251122";
        check((await client.StatusAsync()).LastFailure != null, "known quit exception never exempts another game build");
        commands.Build = HellForgePack.SupportedBuild;
        File.WriteAllText(gameLog, quitCrash.Replace("0x626296", "0x626297"));
        check((await client.StatusAsync()).LastFailure != null, "another native crash address still warns");
        File.WriteAllText(gameLog, "17:58:39.000 crash: c0000005 on thread 1 at game.dll+0x1234 (0x0)\n" + quitCrash);
        check((await client.StatusAsync()).LastFailure != null, "a second native fault is not hidden by a known quit signature");
        File.WriteAllText(gameLog, new string('x', 70 * 1024) + "\n" + quitCrash);
        check((await client.StatusAsync()).LastNotice != null, "bounded log tail still recognizes the terminal quit fault");
        File.WriteAllText(resultPath, """{"completedUtc":"2026-11-03T17:58:43Z","exitCode":2}""");
        check((await client.StatusAsync()).LastFailure?.Contains("code 2") == true, "a different exit code remains abnormal despite a known signature");
        File.WriteAllText(resultPath, """{"completedUtc":"2026-11-03T17:58:43Z","exitCode":1}""");
        commands.Running = true;
        var activeStatus = await client.StatusAsync();
        check(activeStatus.LastFailure == null && activeStatus.LastNotice == null, "running sessions do not inherit completed-game warnings or notices");
        commands.Running = false;
        File.Delete(gameLog);
        File.WriteAllText(resultPath, """{"completedUtc":"2026-11-03T17:58:43Z","exitCode":0}""");
        check((await client.StatusAsync()).LastFailure == null, "successful game exit clears the prior warning");
        File.WriteAllText(resultPath, """{"exitCode":1}""");
        check((await client.StatusAsync()).LastFailure == null, "unfinished result does not imply a game crash");
        File.WriteAllText(resultPath, "{partial");
        check((await client.StatusAsync()).LastFailure == null, "partially written diagnostic does not break readiness checks");
        File.WriteAllText(resultPath, """{"status":"observed","completedUtc":"2026-11-03T17:58:43Z","exitCode":0,"sandboxCopyBackError":"fixture userdata locked"}""");
        check((await client.StatusAsync()).LastFailure?.Contains("sandboxCopyBackError: fixture userdata locked") == true,
            "a 0.4 cleanup step failure is reported although HellForge keeps the status observed");
        File.WriteAllText(resultPath, """{"status":"observed","completedUtc":"2026-11-03T17:58:43Z","exitCode":0,"jobEmpty":false}""");
        check((await client.StatusAsync()).LastFailure?.Contains("jobEmpty") == true, "a game job left running at cleanup is unclean, as HellForge itself judges it");
        File.WriteAllText(resultPath, """{"status":"observed","completedUtc":"2026-11-03T17:58:43Z","exitCode":0,"jobEmpty":true,"sdkShimOriginalsRestored":true,"networkCensusError":"fixture"}""");
        check((await client.StatusAsync()).LastFailure == null, "a clean job and diagnostic-only step errors do not block the next launch");
        File.WriteAllText(resultPath, """{"completedUtc":"2026-11-03T17:58:43Z","exitCode":1}""");
        commands.LastRun = Path.Combine(area, "outside-sessions");
        Directory.CreateDirectory(commands.LastRun);
        File.Copy(resultPath, Path.Combine(commands.LastRun, "result.json"));
        check((await client.StatusAsync()).LastFailure == null, "diagnostics cannot escape the installation's session directory");
        commands.LastRun = null;
        var sentinel = Path.Combine(area, "steam-config.json"); File.WriteAllText(sentinel, "preserve this config");
        await client.InstallPackAsync(output, selection);
        check(commands.Mods.Single(m => m.Id == HellForgePack.PackId("clonedivers")).Enabled &&
            !commands.Mods.Single(m => m.Id == HellForgePack.PackId("empire")).Enabled &&
            commands.Mods.Single(m => m.Id == HellForgeClient.DifficultyId).Enabled,
            "official CLI import selects exactly one Star Wars mode and enables difficulty 11");
        check(commands.Calls.Any(c => c.SequenceEqual(new[] { "settings", "--multiplayer", "yes", "--local-playfab", "yes", "--steam-udp", "yes" })) &&
            !commands.Calls.Any(c => c.Any(a => a.Contains("sandbox", StringComparison.OrdinalIgnoreCase))),
            "co-op is enabled without changing HellForge sandbox preferences");
        check(File.ReadAllText(sentinel) == "preserve this config", "HellForge operations leave unrelated fallback state untouched");
        var beforePreset = commands.Calls.Count(c => c[0] == "play");
        check(new string?[] { null, "{}", "{\"share\":true}", "{\"share\":\"false\"}", "[]", "{partial" }
            .All(value => HellForgePreset.SharingProblem(value) != null), "missing/default/malformed sharing state cannot authorize redistribution of the private preset");
        check(HellForgePreset.SharingProblem("{\"share\":false,\"unrelated\":123}") == null,
            "an explicit official sharing-OFF setting permits the private cohort without rewriting other settings");
        var sharingFile = Path.Combine(install, "mods", "hellforge-mods.json");
        File.WriteAllText(sharingFile, "{\"format\":1,\"share\":false}");
        check(await Reject(() => client.PlayAsync(selection)) && commands.Calls.Count(c => c[0] == "play") == beforePreset,
            "a cosmetic-only fresh installation cannot launch as the complete player preset");
        foreach (var item in HellForgePreset.Required)
        {
            var index = commands.Mods.FindIndex(m => m.Id == item.Id);
            var mod = new HellForgeMod(item.Id, item.Name, true, Path.Combine(install, "mods", item.Id), ["fixture"], item.Version);
            if (item.Id == HellForgePreset.GuardDogs) mod = mod with { Options = [
                new("Smarter Guard Dogs and Sentries", true), new("Guard Dogs", true), new("Sentries", true),
                new("Intelligence", true), new("Safety", true), new("Sentry Safety", true), new("SEAF Safety", true), new("Targeting Marker", false)] };
            if (index < 0) commands.Mods.Add(mod); else commands.Mods[index] = mod;
        }
        commands.Mods.Add(new("skyeshade.vanilla-plus", "Vanilla Plus", true, Path.Combine(install, "mods", "vanilla-plus"), ["code"]));
        commands.Mods.Add(new(HellForgePreset.CleanExit, "Clonedivers Clean Exit", true, Path.Combine(install, "mods", "clean-exit"), ["code"], "0.2.0"));
        commands.Mods.Add(new("pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91", "Reinforcement Beacons Fixed", true, Path.Combine(install, "mods", "beacons"), ["data"]));
        var conflictPlays = commands.Calls.Count(c => c[0] == "play");
        check(await Reject(() => client.PlayAsync(selection)) && conflictPlays == commands.Calls.Count(c => c[0] == "play"),
            "overlapping reinforcement fixes cannot reach the official Clone launch");
        commands.Mods.RemoveAll(m => m.Id == "pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91");
        commands.Mods.Sort((a, b) => HellForgeGameplayCatalog.Pinned.Order.IndexOf(a.Id).CompareTo(HellForgeGameplayCatalog.Pinned.Order.IndexOf(b.Id)));
        var heavyIndex = commands.Mods.FindIndex(m => m.Id == "local.heavy-seaf-presence");
        var heavy = commands.Mods[heavyIndex];
        commands.Mods[heavyIndex] = heavy with { Version = "1.2.0" };
        check(await Reject(() => client.PlayAsync(selection)) && commands.Calls.All(c => c[0] != "play"),
            "the failing original Heavy build cannot replace the repaired preset silently");
        commands.Mods[heavyIndex] = heavy;
        var dogsIndex = commands.Mods.FindIndex(m => m.Id == HellForgePreset.GuardDogs);
        var dogs = commands.Mods[dogsIndex];
        commands.Mods[dogsIndex] = dogs with { Options = dogs.Options!.Select(o => o.Name == "Targeting Marker" ? o with { On = true } : o).ToArray() };
        check(await Reject(() => client.PlayAsync(selection)) && commands.Calls.All(c => c[0] != "play"),
            "an accidentally reenabled target marker is caught before launch");
        commands.Mods[dogsIndex] = dogs with { Options = null };
        check(await Reject(() => client.PlayAsync(selection)), "unknown Guard Dogs options cannot certify the marker-off preset");
        commands.Mods[dogsIndex] = dogs;
        const string privateSharing = "{\"format\":1,\"share\":true,\"unrelated\":\"keep\"}";
        File.WriteAllText(sharingFile, privateSharing);
        check(await Reject(() => client.PlayAsync(selection)) && commands.Calls.All(c => c[0] != "play") && File.ReadAllText(sharingFile) == privateSharing,
            "a fully installed private preset with sharing ON is blocked before play and its privacy settings are never rewritten");
        File.WriteAllText(sharingFile, "{\"format\":1,\"share\":false}");
        var started = await client.PlayAsync(selection);
        check(started.GetProperty("event").GetString() == "started" && commands.Calls.Count(c => c[0] == "play") == 1,
            "only a ready, verified imported pack reaches the official play command");
        commands.Running = true;
        check(await Reject(() => client.PlayAsync(selection)) && commands.Calls.Count(c => c[0] == "play") == 1,
            "an existing HellForge session prevents a second launch");
        commands.Running = false; commands.Ready = false;
        check(await Reject(() => client.PlayAsync(selection)) && commands.Calls.Count(c => c[0] == "play") == 1,
            "incomplete official game setup prevents launching");
        commands.Ready = true; commands.Multiplayer = false;
        check(await Reject(() => client.PlayAsync(selection)), "missing Steam co-op is refused before play");
        commands.Multiplayer = true; commands.Build = "26251122";
        check(await Reject(() => client.PlayAsync(selection)), "a changed HellForge game build is refused before play");
        commands.Build = HellForgePack.SupportedBuild;
        var cloneMod = commands.Mods.Single(m => m.Id == HellForgePack.PackId("clonedivers"));
        File.WriteAllText(Path.Combine(cloneMod.Folder, "9ba626afa44a3aa3.patch_0"), "cl0ne");
        check(await Reject(() => client.PlayAsync(selection)) && commands.Calls.Count(c => c[0] == "play") == 1,
            "tampering after import is detected before any game process starts");
        Game.IsRunningCheck = () => true;
        int calls = commands.Calls.Count;
        check(await Reject(() => client.InstallPackAsync(output, selection)) && commands.Calls.Count == calls,
            "a running normal game blocks every import/settings mutation before any CLI request");
        Game.IsRunningCheck = () => false;

        check(await Reject(() => client.PlayRegularAsync()), "Regular cannot launch with a Star Wars pack still enabled");
        await client.PrepareRegularAsync();
        check(commands.Mods.Where(m => HellForgePack.IsOurPack(m.Id)).All(m => !m.Enabled) &&
            commands.Mods.Single(m => m.Id == "chef.smarter-seaf").Enabled,
            "Regular disables all Star Wars modes and preserves independently selected HellForge gameplay mods");
        int regularPlays = commands.Calls.Count(c => c[0] == "play");
        commands.Mods.Add(new("pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91", "Reinforcement Beacons Fixed", true, Path.Combine(install, "mods", "beacons"), ["data"]));
        check(await Reject(() => client.PlayRegularAsync()) && regularPlays == commands.Calls.Count(c => c[0] == "play"),
            "Regular cannot bypass the overlapping beacon-fix guard");
        commands.Mods.RemoveAll(m => m.Id == "pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91");
        await client.PlayRegularAsync();
        check(commands.Calls.Count(c => c[0] == "play") == regularPlays + 1 && File.ReadAllText(sentinel) == "preserve this config",
            "Regular starts through the official HellForge CLI without exporting assets or changing Steam settings");
        commands.Running = true;
        int mutations = commands.Calls.Count(c => c[0] is "mod-switch" or "settings");
        check(await Reject(() => client.PrepareRegularAsync()) && mutations == commands.Calls.Count(c => c[0] is "mod-switch" or "settings"),
            "Regular mode changes refuse an active session before a mod/settings mutation");
        check(await Reject(() => client.PlayRegularAsync()), "Regular refuses a second game session");
        commands.Running = false; commands.Ready = false;
        check(await Reject(() => client.PlayRegularAsync()), "Regular cannot bypass incomplete official setup");
        commands.Ready = true; commands.Multiplayer = false;
        check(await Reject(() => client.PlayRegularAsync()), "Regular requires the same Steam co-op settings as the themed modes");
        commands.Multiplayer = true;

        var armory = await client.EnsureLoadoutAsync("armory");
        check(armory == HellForgeClient.ArmoryProfile && commands.Profiles[armory] == "unlock-all" && commands.Profiles["default"] == "starter",
            "Full armory creates a separate official unlock-all profile and preserves the progression profile");
        int createdProfiles = commands.Calls.Count(c => c[0] == "profiles" && c.Length > 1);
        await client.EnsureLoadoutAsync("armory");
        check(createdProfiles == commands.Calls.Count(c => c[0] == "profiles" && c.Length > 1),
            "an existing armory profile is reused without resetting earned inventory or progress");
        check(await client.EnsureLoadoutAsync("progression") == "default" && createdProfiles == commands.Calls.Count(c => c[0] == "profiles" && c.Length > 1),
            "Progression keeps the original local character and never creates a replacement");
        await client.PlayRegularAsync(profile: armory);
        check(commands.Calls.Last().SequenceEqual(new[] { "play", "--profile", HellForgeClient.ArmoryProfile }),
            "the selected loadout is explicit on play instead of falling back to the launcher's starter selection");
        int playCalls = commands.Calls.Count(c => c[0] == "play");
        check(await Reject(() => client.PlayRegularAsync(profile: "--sandbox")) && commands.Calls.Count(c => c[0] == "play") == playCalls,
            "an invalid profile cannot become another play option");
        commands.Profiles[armory] = "starter";
        check(await Reject(async () => { await client.EnsureLoadoutAsync("armory"); }) && createdProfiles == commands.Calls.Count(c => c[0] == "profiles" && c.Length > 1),
            "a conflicting existing profile is refused rather than overwritten into a full armory");
        commands.Profiles[armory] = "unlock-all";
        commands.Profiles.Remove("default");
        check(await Reject(async () => { await client.EnsureLoadoutAsync("progression"); }),
            "missing progression data is reported rather than silently creating a fresh level-one character");
        commands.Profiles["default"] = "starter";
        commands.Running = true;
        int profileCalls = commands.Calls.Count(c => c[0] == "profiles");
        check(await Reject(async () => { await client.EnsureLoadoutAsync("armory"); }) && profileCalls == commands.Calls.Count(c => c[0] == "profiles"),
            "loadout preparation does not touch profiles during an active session");
        commands.Running = false;

        var stops = commands.Calls.Count(c => c[0] == "stop");
        await client.EndSessionAsync();
        check(stops == commands.Calls.Count(c => c[0] == "stop"), "End session is harmless when this HellForge installation has no active session");
        commands.Running = true; commands.Ready = false; commands.Build = "different-build";
        await client.EndSessionAsync();
        check(!commands.Running && commands.Calls.Last(c => c[0] == "stop").SequenceEqual(new[] { "stop" }),
            "End session uses official stop and works during staged-copy readiness or build mismatch");
        commands.Ready = true; commands.Build = HellForgePack.SupportedBuild;
        commands.Running = true; commands.StopCompletes = false;
        bool stopTimedOut = false;
        try { await client.EndSessionAsync(wait: TimeSpan.Zero); } catch (TimeoutException) { stopTimedOut = true; }
        check(stopTimedOut && commands.Running, "a stop acknowledgement is not reported as completed cleanup while the runner remains active");
        commands.StopCompletes = true; commands.StopRestoresCopy = false;
        check(await Reject(() => client.EndSessionAsync()), "failed copy restoration after stop remains an actionable cleanup error");
        commands.StopRestoresCopy = true; commands.Ready = true; commands.Running = false;
        commands.Running = true; commands.StopThrows = true;
        await client.EndSessionAsync();
        check(!commands.Running, "a session ending between status and stop is accepted only after cleanup is checked");
        commands.Running = true; commands.StopCompletes = false;
        check(await Reject(() => client.EndSessionAsync()) && commands.Running,
            "a failed stop with a still-active runner remains an error without a repair or forced kill");
        commands.StopThrows = false; commands.StopAcknowledgement = "unexpected";
        check(await Reject(() => client.EndSessionAsync()) && commands.Running,
            "an unexpected stop response is not mistaken for completed cleanup");
        commands.StopAcknowledgement = "stopping"; commands.StopCompletes = true; commands.LastStatus = "cleanup_failed";
        check(await Reject(() => client.EndSessionAsync()), "an explicit cleanup failure is reported even when copy readiness is true");
        commands.LastStatus = "observed"; commands.LastRun = diagnosticRun; commands.Running = true;
        File.WriteAllText(resultPath, """{"status":"observed","completedUtc":"2026-11-03T17:58:43Z","exitCode":0,"jobStopTimeout":true}""");
        check(await Reject(() => client.EndSessionAsync()), "End session is not reported complete when HellForge 0.4 recorded a failed cleanup step");
        check(await Reject(() => client.EndSessionAsync()) && !commands.Running, "a session that ended before End session still has its cleanup checked");
        commands.LastRun = null;
        commands.LastStatus = null; commands.Running = true;
        stops = commands.Calls.Count(c => c[0] == "stop");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); bool wasCancelled = false;
            try { await client.EndSessionAsync(cancelled.Token); } catch (OperationCanceledException) { wasCancelled = true; }
            check(wasCancelled && commands.Running && stops == commands.Calls.Count(c => c[0] == "stop"),
                "cancelling before End session never sends a stop or touches the active game");
        }
        commands.Running = false;

        var prefsPath = Path.Combine(area, "hellforge.json");
        new HellForgePreferences { InstallRoot = install, Texture = "lighter", DeltaSquad = false }.Save(prefsPath);
        var prefs = HellForgePreferences.Load(prefsPath);
        check(prefs.Mode == "clonedivers" && prefs.Texture == "lighter" && prefs.PackMode == "clonedivers",
            "HellForge preferences round-trip independently from the Steam settings schema");
        check(prefs.Loadout == "account", "new saved settings use the imported account exclusively");
        var olderPrefsPath = Path.Combine(area, "hellforge-before-loadouts.json");
        var olderPrefs = """{"Format":1,"Mode":"clonedivers","Texture":"full"}""";
        File.WriteAllText(olderPrefsPath, olderPrefs);
        check(HellForgePreferences.Load(olderPrefsPath).Loadout == "account" && File.ReadAllText(olderPrefsPath) == olderPrefs,
            "older settings migrate to the account flow without overwriting their saved file");
        File.WriteAllText(olderPrefsPath, """{"Format":1,"Loadout":"armory"}""");
        check(HellForgePreferences.Load(olderPrefsPath).Loadout == "account", "a saved test armory is never offered as the player account");
        var appearanceKey = prefs.SelectionKey;
        prefs.Loadout = "progression";
        check(prefs.SelectionKey == appearanceKey, "changing the loadout does not require exporting the appearance pack again");
        prefs.AppliedSelection = prefs.SelectionKey;
        prefs.Droids = !prefs.Droids;
        check(prefs.AppliedSelection != prefs.SelectionKey, "changed options require preparing again before the launcher offers play");
        prefs.Mode = "regular"; prefs.Save(prefsPath);
        var regularPrefs = HellForgePreferences.Load(prefsPath);
        check(regularPrefs.IsRegular && regularPrefs.DeltaSquad == prefs.DeltaSquad && regularPrefs.Droids == prefs.Droids,
            "Regular preferences retain the user's themed-mode options for switching back");
        check(regularPrefs.Loadout == "account", "legacy progression settings migrate to the same account flow");
        check(await Reject(() => { regularPrefs.Selection(); return Task.CompletedTask; }),
            "Regular never silently resolves to a Clonedivers export");
        File.WriteAllText(prefsPath, """{"Format":99}""");
        check(await Reject(() => { HellForgePreferences.Load(prefsPath); return Task.CompletedTask; }) && File.ReadAllText(prefsPath).Contains("99"),
            "unknown preferences fail without silently overwriting recoverable state");
        var pinned = HellForgePack.PinnedSelection("commandos", "lighter");
        check(pinned.GameBuild == HellForgePack.SupportedBuild && pinned.Version == "2026.09.27-r19" && pinned.Files.Count > 0,
            "the shipped launcher carries a pinned compatible catalog independent of the live Steam feed");
        var oldEmpire = new HellForgePreferences { Mode = "empire", Texture = "lighter", Droids = false, Covenant = true,
            AppliedSelection = $"r19/{HellForgePack.SupportedBuild}/empire/lighter/False/True" };
        var newEmpire = oldEmpire.Selection();
        check(oldEmpire.SelectionKey == $"r45-v4/{HellForgePack.SupportedBuild}/empire/lighter/True/False" &&
            oldEmpire.AppliedSelection != oldEmpire.SelectionKey && newEmpire.Version == "2026.11.08-r45-v4" &&
            newEmpire.Texture == "lighter" && newEmpire.Options["droids"] && !newEmpire.Options["covenant"] && newEmpire.Files.Count == 96,
            "Empire r19 preferences require preparing the fixed r45-v4 selection before play");
        var empireFull = HellForgePack.PinnedSelection("empire", "full");
        check(empireFull.ManifestSha256 == "4a35045a9b628e0f2b476815e7a77f00ab31a6cde957abd574e8597513c7ad36" &&
            empireFull.Files[0].Name == "9ba626afa44a3aa3.patch_1000" && newEmpire.Files[0].Name == empireFull.Files[0].Name,
            "Empire Full keeps its shipped receipt identity and both profiles preserve the fixed layer numbers");
        check(empireFull.Files.Select(f => f.Name).SequenceEqual(newEmpire.Files.Select(f => f.Name)) &&
            empireFull.Files.Sum(f => f.Size) > 0 && newEmpire.Files.Any(f => f.Sha256 != empireFull.Files.Single(g => g.Name == f.Name).Sha256),
            "Empire Lighter contains every Full archive and companion with distinct streamed content");
        check(newEmpire.Files.Where(f => f.Name.EndsWith(".gpu_resources")).Sum(f => f.Size) <
            empireFull.Files.Where(f => f.Name.EndsWith(".gpu_resources")).Sum(f => f.Size) / 2,
            "Empire Lighter lowers resident GPU bytes by more than half");
        oldEmpire.AppliedSelection = oldEmpire.SelectionKey;
        oldEmpire.Texture = "full";
        check(oldEmpire.AppliedSelection != oldEmpire.SelectionKey && oldEmpire.Selection().Texture == "full",
            "switching Empire textures invalidates the applied selection");
        oldEmpire.Texture = "lighter";
        oldEmpire.Mode = "clonedivers";
        check(oldEmpire.SelectionKey == $"r19/{HellForgePack.SupportedBuild}/commandos/lighter/False/True" &&
            oldEmpire.Selection().Version == pinned.Version,
            "switching back to Clones retains r19 textures and optional skins");
        using (var catalog = typeof(HellForgePack).Assembly.GetManifestResourceStream("Clonedivers.HellForge.Manifest.json")!)
        using (var reader = new StreamReader(catalog))
        {
            var original = Manifest.Parse(reader.ReadToEnd());
            foreach (var mode in new[] { "clonedivers", "commandos", "empire" })
            foreach (var texture in new[] { "full", "lighter" })
            for (int mask = 0; mask < 16; mask++)
            {
                var changes = new Dictionary<string, bool> { ["droids"] = (mask & 1) != 0, ["covenant"] = (mask & 2) != 0,
                    ["aimpoints"] = (mask & 4) != 0, ["commandos"] = (mask & 8) != 0 };
                var selected = HellForgePack.PinnedSelection(mode, texture, changes);
                var expected = mode == "empire" ? texture == "lighter" ? newEmpire : empireFull :
                    HellForgePack.Select(original, pinned.ManifestSha256, mode, texture, changes);
                check(JsonSerializer.Serialize(selected, HellForgePack.Json) == JsonSerializer.Serialize(expected, HellForgePack.Json),
                    $"Forge release selection {mode}/{texture}/{mask} preserves Clones or resolves the fixed Empire pack");
            }
        }
        using var form = new HellForgeForm(new HellForgePreferences(), preview: true);
        form.SetPreviewState("ready");
        check(form.Text.Contains("HellForge") && !form.Visible && form.Controls.Count > 0,
            "HellForge preview constructs the real launcher without a session, settings writes or live commands");
        using var regularForm = new HellForgeForm(new HellForgePreferences { Mode = "regular" }, preview: true);
        regularForm.SetPreviewState("ready");
        IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
        check(Descendants(regularForm).OfType<ModeButton>().Select(b => b.Text).SequenceEqual(new[] { "Regular", "Clones", "Empire" }) &&
            Descendants(regularForm).OfType<ModeButton>().Single(b => b.Text == "Regular").Selected,
            "the HellForge surface offers Regular, Clones and Empire with Regular selected independently");
        check(!Descendants(regularForm).OfType<ComboBox>().Any(c => c.AccessibleName == "Loadout") &&
            ((ToolStripMenuItem)typeof(HellForgeForm).GetField("accountSync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(regularForm)!).AccessibleName == "Sync HD2 account",
            "players see only account sync, with no armory or progression selector");
        regularForm.SetPreviewState("running");
        check(Descendants(regularForm).OfType<Button>().Any(c => c.Text == "END SESSION" && c.Enabled) &&
            !((ToolStripMenuItem)typeof(HellForgeForm).GetField("prepare", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(regularForm)!).Enabled,
            "an active-session preview exposes End session while blocking pack changes");

        var selectedBefore = commands.Calls.Count(c => c[0] == "settings");
        commands.Running = true;
        check(await Reject(() => client.SelectAccountAsync()) && selectedBefore == commands.Calls.Count(c => c[0] == "settings"),
            "account setup refuses an active session before changing selection");
        commands.Running = false;
        commands.Profiles["default"] = "unlock-all";
        check(await Reject(() => client.SelectAccountAsync()), "a test template cannot silently become the imported account");
        commands.Profiles["default"] = "starter";
        await client.SelectAccountAsync();
        check(commands.Calls.Last().SequenceEqual(new[] { "settings", "--profile", "default" }), "account setup explicitly selects the preserved starter profile");
        check(await Reject(async () => { await client.EnsureAccountAsync(); }), "launch cannot proceed with an uncaptured account");
        await AccountImportTests(install, check, Reject);
        await AccountLifecycleTests(Path.Combine(area, "account-lifecycle"), check, Reject).ConfigureAwait(false);
        await NativeAccountMigrationTests(Path.Combine(area, "native-account-migration"), check, Reject);
        await FormLifecycleTests(check).ConfigureAwait(false);
        FormLayoutTests(check);
    }

    static async Task AccountImportTests(string install, Action<bool, string> check, Func<Func<Task>, Task<bool>> reject)
    {
        var folder = Path.Combine(install, "profiles", "default"); Directory.CreateDirectory(folder);
        var snapshot = Path.Combine(install, "account-snapshot", "default"); Directory.CreateDirectory(snapshot);
        int imports = 0, verifications = 0; bool refused = false;
        Task<string> Backend(IReadOnlyList<string> args, CancellationToken ct)
        {
            if (refused) throw new InvalidDataException("Snapshot belongs to a different Steam account.");
            if (args[1] == "import-preview") { verifications++; return Task.FromResult("{\"categories\":{\"level\":{\"vanilla\":\"1,000 XP\"}}}"); }
            if (args[1] != "import") throw new InvalidOperationException();
            imports++;
            File.WriteAllText(Path.Combine(folder, "profile.json"), "{\"fixture\":true}");
            return Task.FromResult("Imported fixture");
        }
        var account = new HellForgeAccount(install, Backend);
        check(!(await account.ReadAsync()).Captured && verifications == 0, "missing capture does not infer owned gear or run the importer");
        File.WriteAllText(Path.Combine(snapshot, "snapshot.json"), "{\"progression\":false}");
        File.WriteAllText(Path.Combine(snapshot, "real-inventory.json"), "{\"items\":[]}");
        File.WriteAllText(Path.Combine(snapshot, "progression.json"), "{\"experience\":1000}");
        File.WriteAllText(Path.Combine(snapshot, "seal.json"), "{\"steamAccount\":\"1\",\"capturedUtc\":\"2026-11-01T00:00:00Z\"}");
        check(!(await account.ReadAsync()).Captured, "inventory-only capture cannot become an account import");
        File.WriteAllText(Path.Combine(snapshot, "snapshot.json"), "{\"progression\":true}");
        check((await account.ReadAsync()) is { Captured: true, Imported: false }, "a verified complete capture still needs an explicit import");
        await account.ImportAsync();
        using (var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "account-import.json"))))
        {
            var rules = policy.RootElement.GetProperty("categories");
            check(!policy.RootElement.GetProperty("autoApply").GetBoolean() && rules.GetProperty("warbondItems").GetProperty("rule").GetString() == "Merge" &&
                rules.GetProperty("level").GetProperty("rule").GetString() == "Vanilla" && rules.GetProperty("onboarding").GetProperty("include").GetBoolean(),
                "first import takes the actual account, imported onboarding and earned warbond items, never every-item unlocks");
        }
        check((await account.ReadAsync()).Imported && imports == 1, "only a successful official import records account readiness");
        await account.ImportAsync();
        check(imports == 1, "the same capture cannot repeatedly refill spent Forge currencies");
        refused = true;
        check(await reject(async () => { await account.ReadAsync(); }) && imports == 1, "the official current-account seal check still runs after an import receipt exists");
        refused = false;
        File.WriteAllText(Path.Combine(snapshot, "progression.json"), "{\"experience\":2000}");
        check(!(await account.ReadAsync()).Imported, "a new capture needs a fresh merge rather than inheriting a stale receipt");
        await account.ImportAsync();
        using (var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "account-import.json"))))
        {
            var rules = policy.RootElement.GetProperty("categories");
            check(rules.GetProperty("level").GetProperty("rule").GetString() == "Higher" && rules.GetProperty("armory").GetProperty("rule").GetString() == "Merge" &&
                !rules.GetProperty("ship").GetProperty("include").GetBoolean(), "later captures merge forward without resetting earned Forge progress or mission location");
        }
        check(imports == 2, "one import is applied per new capture");
        var incomplete = new HellForgeAccount(install, (_, _) => Task.FromResult("{\"categories\":{\"level\":{\"vanilla\":\"not captured\"}}}"));
        check(await reject(async () => { await incomplete.ReadAsync(); }), "metadata alone cannot turn an incomplete official review into a ready account");
        File.WriteAllText(Path.Combine(snapshot, "progression.json"), "{\"experience\":3000}");
        var beforePolicy = File.ReadAllText(Path.Combine(folder, "account-import.json"));
        var failing = new HellForgeAccount(install, (args, ct) => args[1] == "import" ? throw new IOException("Import failed") : Backend(args, ct));
        check(await reject(() => failing.ImportAsync()) && File.ReadAllText(Path.Combine(folder, "account-import.json")) == beforePolicy && !(await account.ReadAsync()).Imported,
            "failed official imports restore prior choices and never record a successful receipt");
        foreach (var failure in new Exception[] { new TimeoutException("fixture review timed out"), new System.ComponentModel.Win32Exception(2, "fixture backend missing") })
        {
            var (state, reviewed) = await HellForgeForm.ReviewAccountAsync(new HellForgeAccount(install, (_, _) => throw failure), CancellationToken.None);
            check(!reviewed && !state.Captured && !state.Imported && state.Message == failure.Message,
                failure.GetType().Name + " from the account review becomes an account state instead of failing the refresh and its mods read");
        }
        using (var closing = new CancellationTokenSource())
        {
            closing.Cancel(); bool cancelled = false;
            try { await HellForgeForm.ReviewAccountAsync(account, closing.Token); } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "closing the launcher still cancels an account review");
        }
        var probe = new HellForgeClient(install, new FakeCommands(install));
        var accountKey = probe.AccountFingerprint();
        check(probe.AccountFingerprint() == accountKey, "an unchanged capture and profile reuse the last account review");
        File.WriteAllText(Path.Combine(snapshot, "progression.json"), "{\"experience\":4000}");
        var recaptured = probe.AccountFingerprint();
        check(recaptured != accountKey, "a new HD2 capture triggers a fresh account review");
        Directory.CreateDirectory(Path.Combine(folder, "backups"));
        File.WriteAllText(Path.Combine(folder, "backups", "20261101-000000-000-import.json"), "{}");
        check(probe.AccountFingerprint() != recaptured, "a native HellForge import backup triggers a fresh account review");
        File.Delete(Path.Combine(folder, "backups", "20261101-000000-000-import.json"));
    }

    static async Task NativeAccountMigrationTests(string root, Action<bool, string> check, Func<Func<Task>, Task<bool>> reject)
    {
        var profile = Path.Combine(root, "profiles", "default"); var snapshot = Path.Combine(root, "account-snapshot", "default");
        Directory.CreateDirectory(Path.Combine(profile, "backups")); Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(profile, "profile.json"), "{\"existingNativeProgress\":true}");
        File.WriteAllText(Path.Combine(snapshot, "snapshot.json"), "{\"progression\":true}");
        File.WriteAllText(Path.Combine(snapshot, "real-inventory.json"), "{\"items\":[]}");
        File.WriteAllText(Path.Combine(snapshot, "progression.json"), "{\"experience\":1000}");
        var imported = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(profile, "backups", imported.ToString("yyyyMMdd-HHmmss-fff") + "-import.json"), "{}");
        void Capture(DateTimeOffset time) => File.WriteAllText(Path.Combine(snapshot, "seal.json"), JsonSerializer.Serialize(new { steamAccount = "1", capturedUtc = time }));
        int calls = 0;
        Task<string> Backend(IReadOnlyList<string> args, CancellationToken _)
        {
            if (args[1] == "import-preview") return Task.FromResult("{\"categories\":{\"level\":{\"vanilla\":\"1,000 XP\"}}}");
            calls++;
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "account-import.json")));
            var categories = settings.RootElement.GetProperty("categories");
            check(categories.GetProperty("level").GetProperty("rule").GetString() == "Higher" && categories.GetProperty("armory").GetProperty("rule").GetString() == "Merge" &&
                categories.GetProperty("currencies").GetProperty("rule").GetString() == "Higher" && !categories.GetProperty("ship").GetProperty("include").GetBoolean(),
                "a native Forge character is linked with forward merge, retaining earned progress and ship settings");
            return Task.FromResult("fixture committed");
        }
        var account = new HellForgeAccount(root, Backend); Capture(imported.AddMinutes(-1));
        check(!(await account.ReadAsync()).Captured && await reject(() => account.ImportAsync()) && calls == 0,
            "an existing native import cannot be replayed merely because the Clonedivers receipt is missing");
        Capture(imported.AddMinutes(1)); await account.ImportAsync();
        check(calls == 1 && (await account.ReadAsync()).Imported && File.ReadAllText(Path.Combine(profile, "profile.json")).Contains("existingNativeProgress"),
            "a fresh verified capture links the native character without creating or resetting its profile");
    }

    static async Task AccountLifecycleTests(string install, Action<bool, string> check, Func<Func<Task>, Task<bool>> reject)
    {
        var folder = Path.Combine(install, "profiles", "default"); Directory.CreateDirectory(folder);
        var snapshot = Path.Combine(install, "account-snapshot", "default"); Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(folder, "profile.json"), "{\"revision\":0}");
        File.WriteAllText(Path.Combine(snapshot, "snapshot.json"), "{\"progression\":true}");
        File.WriteAllText(Path.Combine(snapshot, "real-inventory.json"), "{\"items\":[]}");
        File.WriteAllText(Path.Combine(snapshot, "progression.json"), "{\"experience\":1000}");
        void Capture(int account, DateTimeOffset utc) => File.WriteAllText(Path.Combine(snapshot, "seal.json"),
            JsonSerializer.Serialize(new { steamAccount = account.ToString(), capturedUtc = utc }));
        // These are isolated launcher fixtures with an injected verifier, never snapshots accepted by the real backend.
        var captureTime = DateTimeOffset.UtcNow.AddDays(-1);
        Capture(1, captureTime);
        int imports = 0; bool failBeforeCommit = false;
        Task<string> Backend(IReadOnlyList<string> args, CancellationToken ct)
        {
            if (args[1] == "import-preview") return Task.FromResult("{\"categories\":{\"level\":{\"vanilla\":\"1,000 XP\"}}}");
            if (failBeforeCommit) throw new IOException("Fixture failure before commit");
            imports++;
            File.WriteAllText(Path.Combine(folder, "profile.json"), JsonSerializer.Serialize(new { revision = imports }));
            return Task.FromResult("fixture import committed");
        }
        var account = new HellForgeAccount(install, Backend);
        await account.ImportAsync();
        var originalProfile = File.ReadAllText(Path.Combine(folder, "profile.json"));
        Capture(2, captureTime);
        check(await reject(() => account.ImportAsync()) && imports == 1 && File.ReadAllText(Path.Combine(folder, "profile.json")) == originalProfile,
            "a valid capture for another account cannot merge into the existing account character");
        Capture(1, captureTime);

        await Task.Delay(15).ConfigureAwait(false); // official backup names have millisecond resolution
        var restored = DateTimeOffset.UtcNow;
        var backups = Directory.CreateDirectory(Path.Combine(folder, "backups")).FullName;
        File.WriteAllText(Path.Combine(backups, restored.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff") + "-restore.json"), originalProfile);
        File.WriteAllText(Path.Combine(folder, "profile.json"), "{\"revision\":0}");
        check(await reject(async () => { await account.ReadAsync(); }),
            "restoring a pre-import profile cannot reuse the stale Imported receipt without a fresh capture");
        Capture(1, DateTimeOffset.UtcNow.AddSeconds(1));
        await account.ImportAsync();

        Capture(1, DateTimeOffset.UtcNow.AddSeconds(2));
        var receiptPath = Path.Combine(folder, "clonedivers-account-import.json");
        Directory.CreateDirectory(receiptPath + ".tmp"); // block bookkeeping, not the backend's profile commit
        var before = imports;
        check(await reject(() => account.ImportAsync()) && imports == before + 1, "a receipt write failure is detected after the successful official profile commit");
        Directory.Delete(receiptPath + ".tmp");
        var committedProfile = File.ReadAllText(Path.Combine(folder, "profile.json"));
        var committedSeal = File.ReadAllText(Path.Combine(snapshot, "seal.json"));
        await account.ImportAsync();
        check(imports == before + 1 && File.ReadAllText(Path.Combine(folder, "profile.json")) == committedProfile && (await account.ReadAsync()).Imported,
            "retry after a receipt write failure finishes bookkeeping without replaying the account import");
        Capture(1, DateTimeOffset.UtcNow.AddSeconds(3));
        failBeforeCommit = true;
        var settings = File.ReadAllText(Path.Combine(folder, "account-import.json"));
        check(await reject(() => account.ImportAsync()) && File.ReadAllText(Path.Combine(folder, "account-import.json")) == settings,
            "a pre-commit backend failure restores import choices and remains recoverable without changing progression");
        failBeforeCommit = false;
        File.WriteAllText(Path.Combine(snapshot, "seal.json"), committedSeal);
        var boundReceipt = File.ReadAllText(receiptPath);
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(boundReceipt)!.AsObject();
        legacy["policy"] = "one-way-v1"; legacy.Remove("account");
        var legacyText = legacy.ToJsonString();
        File.WriteAllText(receiptPath, legacyText);
        check((await account.ReadAsync()).Imported && File.ReadAllText(receiptPath) == legacyText,
            "reading a legacy receipt for its original verified capture is compatible and read-only");
        before = imports;
        await account.BindLegacyReceiptAsync();
        using (var linked = JsonDocument.Parse(File.ReadAllText(receiptPath)))
            check(linked.RootElement.GetProperty("policy").GetString() == "one-way-v1" && FileSafety.IsSha256(linked.RootElement.GetProperty("account").GetString()) &&
                File.ReadAllText(receiptPath + ".v1.bak") == legacyText && imports == before && File.ReadAllText(Path.Combine(folder, "profile.json")) == committedProfile,
                "linking the original legacy receipt preserves a backup and profile without replaying the import");
        boundReceipt = File.ReadAllText(receiptPath);
        File.WriteAllText(receiptPath, legacyText);
        Capture(2, DateTimeOffset.UtcNow);
        check(await reject(() => account.BindLegacyReceiptAsync()) && imports == before && File.ReadAllText(receiptPath) == legacyText,
            "an old receipt cannot be linked to a different newly verified capture");
        File.WriteAllText(Path.Combine(snapshot, "seal.json"), committedSeal);
        File.WriteAllText(receiptPath, boundReceipt);
        var savedProfile = Path.Combine(folder, "profile.saved");
        File.Move(Path.Combine(folder, "profile.json"), savedProfile);
        check(await reject(() => account.ImportAsync()) && imports == before && !File.Exists(Path.Combine(folder, "profile.json")),
            "a missing imported profile is not silently replaced with a new starter during import");
        File.Move(savedProfile, Path.Combine(folder, "profile.json"));
        var pendingPath = Path.Combine(folder, "clonedivers-account-import.pending.json");
        File.WriteAllText(pendingPath, JsonSerializer.Serialize(new { Phase = "started", Receipt = JsonSerializer.Deserialize<JsonElement>(boundReceipt), BeforeProfileHash = "fixture" }));
        check(await reject(() => account.ImportAsync()) && imports == before,
            "an interrupted import with an unknown commit outcome is not replayed automatically");
        await Task.Delay(15).ConfigureAwait(false);
        var recoveryTime = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(backups, recoveryTime.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff") + "-restore.json"), committedProfile);
        File.WriteAllText(Path.Combine(folder, "profile.json"), "{\"revision\":0}");
        Capture(1, recoveryTime.AddSeconds(1));
        await account.ImportAsync();
        check(imports == before + 1 && (await account.ReadAsync()).Imported && !File.Exists(pendingPath),
            "a reviewed official restore followed by a fresh same-account capture recovers an interrupted import without deleting bookkeeping by hand");
        var settled = File.ReadAllText(receiptPath); // a lost journal delete leaves a committed journal beside its saved receipt
        File.WriteAllText(pendingPath, JsonSerializer.Serialize(new { Phase = "committed", Receipt = JsonSerializer.Deserialize<JsonElement>(settled), BeforeProfileHash = "fixture" }));
        Capture(1, recoveryTime.AddSeconds(2)); before = imports;
        var forward = await account.ReadAsync(); await account.ImportAsync();
        check(forward.Captured && !forward.Imported && imports == before + 1 && (await account.ReadAsync()).Imported && !File.Exists(pendingPath),
            "a committed journal left beside its saved receipt does not block a later sync, which merges forward once");
        var unsaved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        unsaved["capture"] = new string('a', 64); unsaved["importedUtc"] = DateTimeOffset.UtcNow; var unsavedText = unsaved.ToJsonString();
        File.WriteAllText(pendingPath, JsonSerializer.Serialize(new { Phase = "committed", Receipt = JsonSerializer.Deserialize<JsonElement>(unsavedText), BeforeProfileHash = "fixture" }));
        Capture(1, recoveryTime.AddSeconds(3)); before = imports;
        var unfinished = await account.ReadAsync(); await account.ImportAsync();
        using (var finished = JsonDocument.Parse(File.ReadAllText(receiptPath)))
            check(unfinished.Captured && !unfinished.Imported && imports == before && !File.Exists(pendingPath) && finished.RootElement.GetProperty("capture").GetString() == new string('a', 64),
                "a committed import whose capture changed before its receipt was saved finishes bookkeeping without replaying the import");
        await account.ImportAsync();
        check(imports == before + 1 && (await account.ReadAsync()).Imported, "the newer capture then merges forward over the finished import");
    }

    static async Task AccountProcessLifetimeTests(string root, Action<bool, string> check)
    {
        async Task<int> WaitForPid(string path)
        {
            for (var i = 0; i < 300; i++)
            {
                try { if (File.Exists(path) && int.TryParse(await File.ReadAllTextAsync(path), out var pid)) return pid; }
                catch (IOException) { /* The fixture is still publishing its PID. */ }
                await Task.Delay(10);
            }
            throw new IOException("Backend fixture did not start.");
        }
        bool Exited(int pid)
        {
            try { using var child = Process.GetProcessById(pid); return child.HasExited; }
            catch (ArgumentException) { return true; }
        }
        var cancelledPid = Path.Combine(root, "cancelled-preview.pid");
        var cancelStart = PipeFixtureStart("--hellforge-preview-wait");
        cancelStart.Environment["CLONEDIVERS_TEST_BACKEND_PID_FILE"] = cancelledPid;
        using var cancel = new CancellationTokenSource();
        var cancelled = HellForgeAccount.RunBackendProcessAsync(cancelStart, false, cancel.Token);
        var pid = await WaitForPid(cancelledPid);
        cancel.Cancel();
        bool cancelledCorrectly = false;
        try { await cancelled; } catch (OperationCanceledException) { cancelledCorrectly = true; }
        check(cancelledCorrectly && Exited(pid), "closing a launcher cancels its read-only account preview and leaves no owned preview process");

        var timeoutPid = Path.Combine(root, "timed-out-preview.pid");
        var timeoutStart = PipeFixtureStart("--hellforge-preview-wait");
        timeoutStart.Environment["CLONEDIVERS_TEST_BACKEND_PID_FILE"] = timeoutPid;
        bool timedOut = false;
        try { await HellForgeAccount.RunBackendProcessAsync(timeoutStart, false, CancellationToken.None, TimeSpan.FromSeconds(1)); }
        catch (TimeoutException) { timedOut = true; }
        check(timedOut && Exited(await WaitForPid(timeoutPid)), "a stalled account preview reaches its deadline and cleans up only its own process");

        var commitPid = Path.Combine(root, "finishing-import.pid");
        var commitStart = PipeFixtureStart("--hellforge-import-finish");
        commitStart.Environment["CLONEDIVERS_TEST_BACKEND_PID_FILE"] = commitPid;
        using var commitCancel = new CancellationTokenSource();
        var committing = HellForgeAccount.RunBackendProcessAsync(commitStart, true, commitCancel.Token);
        pid = await WaitForPid(commitPid);
        commitCancel.Cancel();
        check((await committing).Contains("fixture completed") && Exited(pid), "an import already committing finishes rather than being killed by launcher cancellation");
        bool diagnostic = false;
        try { await HellForgeAccount.RunBackendProcessAsync(PipeFixtureStart("--hellforge-pipe-error"), false, CancellationToken.None); }
        catch (InvalidDataException error) { diagnostic = error.Message.Contains("fixture failure"); }
        check(diagnostic, "bounded account review preserves official backend failure diagnostics");
        foreach (var mode in new[] { "output", "errors" })
        {
            var probe = Path.Combine(root, "oversized-preview-" + mode + ".pid");
            var start = PipeFixtureStart("--hellforge-preview-" + mode);
            start.Environment["CLONEDIVERS_TEST_BACKEND_PID_FILE"] = probe;
            bool oversized = false; var clock = Stopwatch.StartNew();
            try { await HellForgeAccount.RunBackendProcessAsync(start, false, CancellationToken.None); }
            catch (InvalidDataException error) { oversized = error.Message.Contains("oversized"); }
            check(oversized && Exited(await WaitForPid(probe)) && clock.Elapsed < TimeSpan.FromSeconds(8),
                "oversized read-only account " + mode + " stops its owned preview and cannot become verified input");
        }
        foreach (var mode in new[] { "output", "errors" })
        {
            var probe = Path.Combine(root, "noisy-commit-" + mode + ".pid");
            var complete = probe + ".complete";
            var start = PipeFixtureStart("--hellforge-commit-" + mode);
            start.Environment["CLONEDIVERS_TEST_BACKEND_PID_FILE"] = probe;
            start.Environment["CLONEDIVERS_TEST_BACKEND_COMMIT_FILE"] = complete;
            using var stopped = new CancellationTokenSource();
            var commit = HellForgeAccount.RunBackendProcessAsync(start, true, stopped.Token);
            int committingPid = await WaitForPid(probe); stopped.Cancel();
            var response = await commit;
            check(File.Exists(complete) && Exited(committingPid) && response.Contains("truncated after successful import"),
                "noisy committing account " + mode + " finishes naturally despite cancellation and keeps its successful exit");
            check(response.Length <= 1024 * 1024 + 128, "committing account " + mode + " retains bounded diagnostics while draining excess");
        }
        bool failedCommit = false;
        try { await HellForgeAccount.RunBackendProcessAsync(PipeFixtureStart("--hellforge-commit-failure"), true, CancellationToken.None); }
        catch (InvalidDataException error) { failedCommit = error.Message.Contains("fixture backend refused"); }
        check(failedCommit, "truncated diagnostics cannot turn a nonzero account-import exit into success");
        var heldClock = Stopwatch.StartNew();
        var held = await HellForgeAccount.RunBackendProcessAsync(PipeFixtureStart("--hellforge-pipe-parent"), true, CancellationToken.None);
        using var heldJson = JsonDocument.Parse(held.Split('\n')[0]);
        using var independent = Process.GetProcessById(heldJson.RootElement.GetProperty("pid").GetInt32());
        try
        {
            check(held.Contains("truncated after successful import") && heldClock.Elapsed < TimeSpan.FromSeconds(8),
                "an exit0 commit does not wait for inherited diagnostic pipe EOF or lose its successful outcome");
            check(!independent.HasExited, "post-commit diagnostic cleanup leaves independent descendants running");
        }
        finally { if (!independent.HasExited) { independent.Kill(); await independent.WaitForExitAsync(); } }
    }

    sealed class FakeCommands(string root) : IHellForgeCommands
    {
        public List<string[]> Calls { get; } = [];
        public List<HellForgeMod> Mods { get; } = [new(HellForgeClient.DifficultyId, "Hell Forge Difficulty", false, Path.Combine(root, "mods", "difficulty"), ["bundled", "code"])];
        public bool Ready = true, Running, Multiplayer = true;
        public bool StopCompletes = true, StopRestoresCopy = true;
        public bool StopThrows;
        public string StopAcknowledgement = "stopping";
        public string? LastStatus;
        public string? LastRun;
        public Dictionary<string, string> Profiles { get; } = new() { ["default"] = "starter" };
        public string Build = HellForgePack.SupportedBuild;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add(args.ToArray());
            object result;
            switch (args[0])
            {
                case "status": result = new { install = root, hellforge = "0.4.0-beta (d8a8b46)", engine = "0.1.47", modSdk = 16, networkBlock = "wfp", gameBuild = Build, ready = new { gameCopy = Ready, sessionRunner = true, linuxNamespace = false },
                    session = new { running = Running, lastStatus = LastStatus, lastFailure = (string?)null, lastRun = LastRun }, settings = new { multiplayer = Multiplayer, localPlayFab = true, sandbox = true } }; break;
                case "mods": result = new { mods = Mods.Select(m => new { id = m.Id, name = m.Name, enabled = m.Enabled, folder = m.Folder, tags = m.Tags, version = m.Version,
                    options = m.Options?.Select(o => new { name = o.Name, on = o.On }) }), problems = Array.Empty<string>(), modlist = "fixture-code" }; break;
                case "mod-hash":
                    result = new { hash = HellForgeGameplayCatalog.Pinned.Artifacts.Single(a => Mods.Any(m => m.Id == a.Id && m.Folder == args[2])).ContentHash }; break;
                case "profiles":
                    if (args.Count > 1) Profiles.Add(args[2], args[4]);
                    result = new { profiles = Profiles.Select(p => new { name = p.Key, template = p.Value }) };
                    break;
                case "mod-add":
                    var source = Path.GetDirectoryName(args[2])!;
                    using (var manifest = JsonDocument.Parse(File.ReadAllText(args[2])))
                    {
                        var id = "pack." + manifest.RootElement.GetProperty("Guid").GetString();
                        var folder = Path.Combine(root, "mods", "selected"); Directory.CreateDirectory(folder);
                        foreach (var path in Directory.EnumerateFiles(source)) File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), true);
                        Mods.Add(new(id, "Selected pack", true, folder, ["pack"]));
                        result = new { id, installed = folder };
                    }
                    break;
                case "mod-switch":
                    int index = Mods.FindIndex(m => m.Id == args[2]); Mods[index] = Mods[index] with { Enabled = args[4] == "yes" };
                    result = new { ok = true }; break;
                case "settings": Multiplayer = true; result = new { ok = true }; break;
                case "play": result = new { @event = "started", runId = "fixture", pid = 123 }; break;
                case "stop":
                    if (StopCompletes) { Running = false; Ready = StopRestoresCopy; }
                    if (StopThrows) throw new IOException("fixture stop race");
                    result = new { @event = StopAcknowledgement, run = "fixture" }; break;
                default: throw new InvalidOperationException("Unexpected fake command: " + args[0]);
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }
}
