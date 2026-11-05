using System.Diagnostics;
using System.Text.Json;
using static Clonedivers.Palette;

namespace Clonedivers;

/// <summary>Regular, Clones and Empire all launch through HellForge. The official game is opened from Steam.</summary>
public sealed class HellForgeForm : Form
{
    internal static Func<bool> SteamReadyCheck = SteamReady;
    static bool SteamReady()
    {
        Process[] clients = [];
        try { clients = Process.GetProcessesByName("steam"); return clients.Length > 0 && Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", null) is int id && id != 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException) { return false; }
        finally { foreach (var client in clients) client.Dispose(); }
    }
    internal static string FailureSummary(string diagnostic)
    {
        var first = diagnostic.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Session needs attention.";
        if (first.Contains("Steam ownership was not confirmed", StringComparison.OrdinalIgnoreCase))
            return "Steam could not confirm ownership for the last launch. Keep Steam open with the account that owns HD2, then try again. Session details has the full diagnostic.";
        if (first.Length > 300) first = first[..297] + "…";
        return first + (first == diagnostic.Trim() ? "" : " See Session details for the full diagnostic.");
    }
    readonly HellForgePreferences preferences;
    readonly bool preview;
    readonly ModeButton regular = new() { Mode = LauncherMode.Helldivers, Text = "Regular", Dock = DockStyle.Fill };
    readonly ModeButton clone = new() { Mode = LauncherMode.Clonedivers, Text = "Clones", Dock = DockStyle.Fill };
    readonly ModeButton empire = new() { Mode = LauncherMode.EmpireDivers, Text = "Empire", Dock = DockStyle.Fill };
    readonly FlowLayoutPanel choices, textureRow;
    readonly RoundButton accountSync = new() { Text = "SYNC HD2 ACCOUNT", Width = 250, Height = 36, AccessibleName = "Sync HD2 account" };
    readonly Label accountHelp = new() { AutoSize = true, ForeColor = TextDim, MaximumSize = new Size(590, 0) };
    readonly CheckBox delta = new() { Text = "Delta Squad armor", AutoSize = true };
    readonly CheckBox droids = new() { Text = "CIS droids", AutoSize = true };
    readonly CheckBox covenant = new() { Text = "Covenant squids", AutoSize = true };
    readonly RoundButton textures = new() { Width = 230, Height = 36, AccessibleName = "Texture profile",
        AccessibleDescription = "Switch between full textures and full-detail textures streamed on demand." };
    readonly Label state = new() { AutoSize = true, ForeColor = TextMain, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    readonly Label detail = new() { AutoSize = true, ForeColor = TextDim, MaximumSize = new Size(640, 0) };
    readonly Label location = new() { AutoSize = true, ForeColor = TextMute, MaximumSize = new Size(640, 0) };
    readonly Label message = new() { AutoSize = true, ForeColor = TextDim, MaximumSize = new Size(640, 0) };
    readonly RoundButton prepare = new() { Text = "PREPARE STAR WARS PACK", Height = 44, Dock = DockStyle.Fill };
    readonly RoundButton launch = new() { Text = "LAUNCH CLONEDIVERS", Height = 54, Dock = DockStyle.Fill };
    readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 5, Visible = false, Style = ProgressBarStyle.Marquee };
    readonly Button cancelPreparation = new() { Text = "Cancel preparation", AutoSize = true, Visible = false };
    readonly FlowLayoutPanel actions = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    readonly TableLayoutPanel activity = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0, 0, 0, 10) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? operation;
    CancellationToken WorkToken => operation?.Token ?? lifetime.Token;
    HellForgeStatus? lastStatus;
    HellForgeMods? lastMods;
    HellForgeAccountState? lastAccount;
    string? captureBlock;
    bool working, refreshing, initializing = true, resourcesDisposed;
    TaskCompletionSource? refreshCompletion;
    string? lastError;
    string previewState = "setup";

    public HellForgeForm(HellForgePreferences preferences, bool preview = false)
    {
        this.preferences = preferences;
        this.preview = preview;
        preferences.Validate();
        Text = "Clonedivers — HellForge";
        BackColor = Bg; ForeColor = TextMain; DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(940, 900); MinimumSize = new Size(760, 740);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch (ArgumentException) { }
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        Controls.Add(scroll);
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(32, 24, 32, 8), ColumnCount = 1, RowCount = 11 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.SizeChanged += (_, _) => {
            var wrap = new Size(Math.Max(160, root.ClientSize.Width - root.Padding.Horizontal), 0);
            foreach (var label in new[] { state, detail, accountHelp, location, message })
                if (label.MaximumSize != wrap) label.MaximumSize = wrap;
        };
        for (int i = 0; i < root.RowCount; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles[3] = new RowStyle(SizeType.Absolute, 64);
        root.RowStyles[4] = new RowStyle(SizeType.Absolute, 53);
        scroll.Controls.Add(root);

        var header = new TableLayoutPanel { Anchor = AnchorStyles.None, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 20) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new HelmetMark { Width = 50, Height = 60, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 16, 0) }, 0, 0);
        var heading = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        heading.Controls.Add(new Label { Text = "CLONEDIVERS", Font = new Font("Segoe UI", 26, FontStyle.Bold), AutoSize = true, Margin = Padding.Empty });
        heading.Controls.Add(new Label { Text = "Choose your universe  ·  HellForge edition", ForeColor = TextDim, Font = new Font("Segoe UI", 10.5F), AutoSize = true, Margin = new Padding(2, 0, 0, 0) });
        header.Controls.Add(heading, 1, 0); root.Controls.Add(header, 0, 0);
        var modes = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Height = 190, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16) };
        for (int i = 0; i < 3; i++) modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        regular.Margin = new Padding(0, 0, 8, 0); clone.Margin = new Padding(4, 0, 4, 0); empire.Margin = new Padding(8, 0, 0, 0);
        foreach (var button in new[] { regular, clone, empire }) button.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        modes.Controls.Add(regular, 0, 0); modes.Controls.Add(clone, 1, 0); modes.Controls.Add(empire, 2, 0); root.Controls.Add(modes, 0, 1);
        choices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), WrapContents = true };
        foreach (var check in new[] { delta, droids, covenant }) { check.Margin = new Padding(0, 4, 20, 8); choices.Controls.Add(check); }
        root.Controls.Add(choices, 0, 5);
        textureRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 18) };
        textureRow.Controls.Add(new Label { Text = "Textures", AutoSize = true, ForeColor = TextDim, Margin = new Padding(0, 5, 12, 0) });
        Style(textures, Slate, SlateHot, TextMain); textures.Font = new Font("Segoe UI", 10F);
        textureRow.Controls.Add(textures); root.Controls.Add(textureRow, 0, 6);
        var statusCard = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 12) };
        statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var label in new[] { state, detail }) { label.MaximumSize = new Size(840, 0); label.Anchor = AnchorStyles.None; label.TextAlign = ContentAlignment.MiddleCenter; label.Margin = new Padding(0, 0, 0, 6); statusCard.Controls.Add(label); }
        Style(accountSync, Slate, SlateHot, TextMain); accountSync.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        accountSync.Dock = DockStyle.Fill; accountSync.Margin = new Padding(6, 0, 0, 9);
        root.Controls.Add(statusCard, 0, 2);
        Style(prepare, Slate, SlateHot, TextMain); prepare.Margin = new Padding(0, 0, 6, 9);
        Style(launch, Orange, OrangeHot, Bg); launch.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(launch, 0, 3);
        var maintenance = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        maintenance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); maintenance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        maintenance.Controls.Add(prepare, 0, 0); maintenance.Controls.Add(accountSync, 1, 0); root.Controls.Add(maintenance, 0, 4);
        accountHelp.MaximumSize = new Size(840, 0); accountHelp.Margin = new Padding(0, 0, 0, 10); root.Controls.Add(accountHelp, 0, 7);
        activity.Controls.Add(progress); message.Margin = new Padding(0, 5, 0, 0); activity.Controls.Add(message); root.Controls.Add(activity, 0, 8);
        message.TextChanged += (_, _) => activity.Visible = working || !string.IsNullOrWhiteSpace(message.Text) || cancelPreparation.Visible;
        Style(cancelPreparation, Slate, SlateHot, TextMain); cancelPreparation.Font = new Font("Segoe UI", 9F);
        cancelPreparation.Margin = new Padding(0, 8, 0, 0);
        cancelPreparation.Click += (_, _) => operation?.Cancel();
        activity.Controls.Add(cancelPreparation);
        location.MaximumSize = new Size(840, 0); location.Margin = new Padding(0, 0, 0, 8); root.Controls.Add(location, 0, 9);
        foreach (var (caption, handler) in new (string, EventHandler)[] {
            ("Setup && mods", (_, _) => OpenHellForge()), ("Locate…", (_, _) => Locate()),
            ("Refresh", async (_, _) => await RefreshAsync()), ("Session details", async (_, _) => await ShowDetailsAsync()) })
        {
            var button = new Button { Text = caption, AutoSize = true, Padding = new Padding(8, 5, 8, 5), Margin = new Padding(0, 0, 8, 8) };
            Style(button, Slate, SlateHot, TextMain); button.Font = new Font("Segoe UI", 9F); button.Click += handler; actions.Controls.Add(button);
        }
        root.Controls.Add(actions, 0, 10);
        // Keep the Steam fallback and version visible even when Windows clamps
        // the initial window to a short screen. Only the main content scrolls.
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, ColumnCount = 1,
            Padding = new Padding(32, 4, 32, 18), Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var steam = new LinkLabel { Text = "Official online game: open Helldivers 2 in Steam", AutoSize = true, LinkColor = TextDim, ActiveLinkColor = TextMain, VisitedLinkColor = TextDim, Margin = new Padding(0, 3, 0, 8) };
        steam.Anchor = AnchorStyles.None; steam.LinkClicked += (_, _) => OpenSteamLibrary(); footer.Controls.Add(steam);
        footer.Controls.Add(new Label { Text = $"v{AppInfo.Version}  ·  Difficulties 1–11  ·  Multiplayer validation pending", Anchor = AnchorStyles.None, AutoSize = true, ForeColor = TextMute, Font = new Font("Segoe UI", 9F), Margin = Padding.Empty });
        Controls.Add(footer);
        regular.Click += (_, _) => ChangeMode("regular"); clone.Click += (_, _) => ChangeMode("clonedivers"); empire.Click += (_, _) => ChangeMode("empire");
        accountSync.Click += async (_, _) => await SyncAccountAsync();
        delta.Checked = preferences.DeltaSquad; droids.Checked = preferences.Droids; covenant.Checked = preferences.Covenant;
        textures.Text = preferences.Texture == "lighter" ? "Streamed textures" : "Full textures";
        foreach (var check in new[] { delta, droids, covenant }) check.CheckedChanged += (_, _) => ChangeOptions();
        textures.Click += (_, _) => {
            if (working) return;
            preferences.Texture = preferences.Texture == "full" ? "lighter" : "full";
            textures.Text = preferences.Texture == "lighter" ? "Streamed textures" : "Full textures";
            ChangeOptions();
        };
        prepare.Click += async (_, _) => await PrepareAsync();
        launch.Click += async (_, _) => { if (lastStatus?.Running == true) await EndSessionAsync(); else await LaunchAsync(); };
        timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { if (!preview) { await RefreshAsync(); if (!IsDisposed && !Disposing) timer.Start(); } };
        initializing = false;
        UpdateView();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dwm.ApplyDark(Handle, Bg, TextMain, Border); }
    static void Style(Button button, Color back, Color hot, Color text)
    {
        button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = hot;
        button.BackColor = back; button.ForeColor = text; button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI", 11F, FontStyle.Bold); button.Cursor = Cursors.Hand;
    }
    public void SetPreviewState(string value) { if (!preview) throw new InvalidOperationException(); previewState = value; UpdateView(); }
    void ChangeMode(string mode) { if (working) return; preferences.Mode = mode; Save(); UpdateView(); }
    void ChangeOptions()
    {
        if (initializing || working) return;
        preferences.DeltaSquad = delta.Checked; preferences.Droids = droids.Checked; preferences.Covenant = covenant.Checked;
        Save(); message.Text = "Prepare the pack to apply your selection."; UpdateView();
    }
    void Save() { if (!preview) preferences.Save(); }
    HellForgeClient Client() => new(preferences.InstallRoot ?? throw new InvalidOperationException("Locate your HellForge installation first."));

    async Task RefreshAsync()
    {
        if (preview || working || refreshing) return;
        refreshing = true;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        refreshCompletion = completed;
        var root = preferences.InstallRoot;
        try
        {
            if (root == null) { preferences.InstallRoot = HellForgeClient.Detect(); root = preferences.InstallRoot; if (root != null) Save(); }
            if (root == null) { lastError = null; return; }
            var client = new HellForgeClient(root);
            var status = await client.StatusAsync(lifetime.Token);
            // Keep the official runner state even if a later mods/account read fails.
            // End session must remain available during an unrelated catalog error.
            if (root != preferences.InstallRoot) return;
            lastStatus = status;
            var mods = await client.ModsAsync(lifetime.Token);
            var block = HellForgeAccount.CaptureBlock();
            HellForgeAccountState? account = lastAccount;
            if (!status.Running && !Game.IsRunning())
            {
                try { account = await new HellForgeAccount(root).ReadAsync(lifetime.Token); }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
                { account = new(false, false, error.Message); }
            }
            if (root != preferences.InstallRoot) return;
            lastStatus = status; lastMods = mods; lastAccount = account; captureBlock = block; lastError = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed && root == preferences.InstallRoot) { lastMods = null; lastError = e.Message; } }
        finally { refreshing = false; completed.TrySetResult(); if (!IsDisposed) UpdateView(); }
    }
    void UpdateView()
    {
        activity.Visible = working || !string.IsNullOrWhiteSpace(message.Text) || cancelPreparation.Visible;
        accountSync.Enabled = !working && (preview ? previewState != "running" : preferences.InstallRoot != null && lastStatus?.Running != true && !Game.IsRunning());
        accountSync.Text = lastAccount is { Captured: true, Imported: false } ? "APPLY HD2 IMPORT" : "SYNC HD2 ACCOUNT";
        accountHelp.Text = lastAccount?.Imported == true || preview && previewState == "ready" ? "HD2 account imported. Forge progress saves locally."
            : lastAccount?.Message ?? (lastStatus?.Running == true || preview && previewState == "running"
                ? "Account status refreshes after this session ends. Forge progress saves locally."
                : "One-way import: HD2 → Forge. Import your account to start; Forge rewards stay local.");
        regular.Selected = preferences.IsRegular;
        clone.Selected = preferences.Mode == "clonedivers"; empire.Selected = preferences.Mode == "empire";
        choices.Visible = textureRow.Visible = !preferences.IsRegular;
        delta.Visible = preferences.Mode == "clonedivers";
        delta.Enabled = !working && preferences.Mode == "clonedivers";
        foreach (var control in new Control[] { regular, clone, empire, droids, covenant, textures, actions }) control.Enabled = !working;
        prepare.Text = preferences.IsRegular ? "APPLY REGULAR MODE" : "PREPARE STAR WARS PACK";
        prepare.Enabled = !working && preferences.InstallRoot != null && !preview;
        location.Text = preview ? "D:\\HellForge" : preferences.InstallRoot ?? "No HellForge installation selected";
        launch.Text = "LAUNCH " + (preferences.IsRegular ? "REGULAR HELLFORGE" : preferences.Mode == "empire" ? "EMPIREDIVERS" : "CLONEDIVERS");
        launch.Enabled = false;
        if (preview)
        {
            prepare.Enabled = previewState != "running"; launch.Enabled = previewState is "ready" or "running";
            if (previewState == "running") launch.Text = "END SESSION";
            state.Text = previewState switch { "ready" => "Ready for battle", "running" => "Session running", "error" => "Needs attention", _ => "Finish HellForge setup" };
            detail.Text = previewState switch {
                "ready" => preferences.IsRegular ? "Standard Helldivers appearance. Your selected HellForge gameplay mods remain active." : "Star Wars pack prepared. Choose your difficulty at the war table.",
                "running" => "End session closes the game and waits for HellForge to finish cleanup. Closing this launcher leaves the game running.",
                "error" => "The selected pack needs preparing before it can launch.",
                _ => "Open HellForge to prepare its separate game copy, then prepare your Star Wars pack." };
            message.Text = ""; activity.Visible = false;
            return;
        }
        if (working) return;
        if (lastStatus?.Running == true || Game.IsRunning()) {
            state.Text = lastStatus?.Running == true ? "HellForge session running" : "Your game is running";
            detail.Text = lastStatus?.Running == true ? "End session closes the game and waits for HellForge to finish cleanup. Closing this launcher leaves the game running." :
                "Pack files can be prepared alongside it. Installation and launch wait until you close the game.";
            if (lastError != null) detail.Text += " Status check: " + lastError;
            if (lastStatus?.Running == true) { prepare.Enabled = false; launch.Text = "END SESSION"; launch.Enabled = true; }
            return;
        }
        if (lastStatus?.CompatibilityProblem is { } compatibility) {
            state.Text = "Check HellForge compatibility"; detail.Text = compatibility;
            prepare.Enabled = accountSync.Enabled = false; return;
        }
        if (lastStatus?.RecoveryProblem is { } recovery) {
            state.Text = "Recover the previous appearance"; detail.Text = recovery;
            prepare.Text = "RECOVER PREVIOUS APPEARANCE"; prepare.Enabled = true; accountSync.Enabled = false;
            return;
        }
        if (preferences.InstallRoot != null && lastStatus != null && !SteamReadyCheck()) {
            state.Text = "Start Steam and sign in"; detail.Text = "Steam must be running with your game-owning account before Forge can verify ownership. Open Steam, finish signing in, then launch here.";
            launch.Text = "OPEN STEAM"; launch.Enabled = true; return;
        }
        if (lastError != null) { state.Text = "Needs attention"; detail.Text = FailureSummary(lastError); return; }
        if (preferences.InstallRoot == null) { state.Text = "Install or locate HellForge"; detail.Text = "Setup & mods opens the official download. Use Locate for an existing installation."; return; }
        if (lastStatus == null) { state.Text = "Checking HellForge…"; detail.Text = "Reading installation and pack information."; return; }
        if (captureBlock != null) { state.Text = "Finish account sync"; detail.Text = captureBlock; accountSync.Enabled = false; prepare.Enabled = false; return; }
        if (!lastStatus.GameCopyReady || !lastStatus.RunnerReady) { state.Text = "Finish HellForge setup"; detail.Text = "Open HellForge to prepare its separate game copy. Your Star Wars pack can be prepared now."; return; }
        if (lastStatus.Build != HellForgePack.SupportedBuild) { state.Text = "Pack update needed"; detail.Text = "This HellForge version uses a different game build. Keep the Steam fallback available."; return; }
        if (lastMods?.Problems.Length > 0) { state.Text = "Check HellForge’s Mods page"; detail.Text = string.Join("; ", lastMods.Problems); return; }
        if (lastMods != null && HellForgeClient.ModConflict(lastMods) is { } conflict) { state.Text = "Resolve the mod overlap"; detail.Text = conflict; return; }
        if (!preferences.IsRegular && preferences.AppliedSelection != preferences.SelectionKey) { state.Text = "Prepare your Star Wars pack"; detail.Text = "Apply your selected universe and texture options to HellForge."; return; }
        var our = lastMods?.Mods.Where(m => m.Enabled && HellForgePack.IsOurPack(m.Id)).ToList() ?? [];
        if (preferences.IsRegular && our.Count != 0) { state.Text = "Apply Regular mode"; detail.Text = "Disable the Star Wars appearance packs. Your selected HellForge gameplay mods remain active."; return; }
        if (!preferences.IsRegular && (our.Count != 1 || our[0].Id != HellForgePack.PackId(preferences.PackMode))) { state.Text = "Prepare your Star Wars pack"; detail.Text = "Apply your selected universe and texture options to HellForge."; return; }
        if (lastMods?.Mods.Any(m => m.Id == HellForgeClient.DifficultyId && m.Enabled) != true) { state.Text = "Enable Hell Forge Difficulty"; detail.Text = "Open HellForge’s Mods page and enable its bundled difficulty mod. Levels 1–10 remain available."; return; }
        if (lastMods != null && HellForgePreset.Problem(lastMods) is { } preset) { state.Text = "Finish the gameplay preset"; detail.Text = preset; return; }
        if (!lastStatus.Multiplayer || !lastStatus.LocalBackend) { state.Text = "Enable Steam co-op"; detail.Text = "Enable the local backend and Steam multiplayer in HellForge, or prepare the pack again."; return; }
        if (lastAccount?.Imported != true) { state.Text = lastAccount?.Captured == true ? "Apply your account import" : "Import your HD2 account"; detail.Text = "Use the account button to capture and import your profile before launching."; return; }
        state.Text = "Ready to launch";
        detail.Text = preferences.IsRegular ? "Standard Helldivers appearance with your selected HellForge gameplay mods. Choose difficulty at the war table." :
            "The selected pack is checked before launch. Choose difficulty 1–11 at the war table.";
        if (lastStatus.LastStatus is "failed" or "cleanup_failed" || lastStatus.LastFailure != null) {
            state.Text = "Last session needs attention";
            detail.Text = lastStatus.LastFailure is { } failure ? FailureSummary(failure) : "Open Session details and HellForge’s logs before retrying.";
        }
        else if (lastStatus.LastNotice != null) detail.Text = lastStatus.LastNotice;
        launch.Enabled = true;
    }

    async Task OperationAsync(Func<Task> action)
    {
        if (preview || working) return;
        var pendingRefresh = refreshCompletion?.Task ?? Task.CompletedTask;
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        working = true; progress.Visible = true; UpdateView(); state.Text = "Preparing…"; lastError = null;
        try { await pendingRefresh; lastError = null; await action(); }
        catch (OperationCanceledException) { if (!IsDisposed) message.Text = "Operation cancelled. Your existing game session stays open."; }
        catch (Exception e) { if (!IsDisposed) { lastError = e.Message; message.Text = FailureSummary(e.Message); } }
        finally
        {
            working = false;
            operation.Dispose(); operation = null;
            cancelPreparation.Visible = false;
            if (!IsDisposed) { progress.Visible = false; UpdateView(); if (lastError == null) await RefreshAsync(); }
        }
    }
    async Task PrepareAsync() => await OperationAsync(async () => {
        var client = Client();
        if ((await client.StatusAsync(WorkToken)).RecoveryProblem != null)
        {
            message.Text = "Checking and restoring the previous appearance through HellForge…";
            await Task.Run(() => client.RecoverInterruptedPackAsync(WorkToken), WorkToken);
            preferences.AppliedSelection = null; Save();
            message.Text = "Previous appearance restored. Prepare your chosen mode when ready.";
            return;
        }
        if (preferences.IsRegular)
        {
            message.Text = "Applying standard Helldivers appearance in HellForge…";
            await client.PrepareRegularAsync(WorkToken);
            preferences.AppliedSelection = preferences.SelectionKey; Save();
            message.Text = "Regular HellForge selected. Your other gameplay mods are unchanged.";
            return;
        }
        var selection = preferences.Selection();
        cancelPreparation.Visible = true;
        var report = new Progress<string>(text => { if (!IsDisposed) message.Text = text; });
        var folder = await FindPreparedAsync(client, selection, report) ?? await ExportAsync(client, selection, report);
        preferences.PreparedPack = folder; Save();
        if (Game.IsRunning() || (await client.StatusAsync(WorkToken)).Running)
        {
            message.Text = "Pack prepared. Keep playing; click Prepare again after you close the game to install it.";
            return;
        }
        message.Text = "Importing and verifying the pack in HellForge…";
        cancelPreparation.Visible = false;
        WorkToken.ThrowIfCancellationRequested();
        await Task.Run(() => client.InstallPackAsync(folder, selection, WorkToken), WorkToken);
        preferences.AppliedSelection = preferences.SelectionKey; Save();
        message.Text = "Star Wars pack installed. Finish HellForge setup if needed; multiplayer playtest is still pending.";
    });
    async Task<string?> FindPreparedAsync(HellForgeClient client, HellForgePack.Selection selection, IProgress<string> report)
    {
        var candidates = new List<string>();
        if (preferences.PreparedPack is { } saved && Directory.Exists(saved)) candidates.Add(saved);
        var exports = Path.Combine(client.CacheRoot, "exports");
        FileSafety.RejectLink(exports);
        if (Directory.Exists(exports)) candidates.AddRange(new DirectoryInfo(exports).EnumerateDirectories()
            .OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            report.Report("Checking previously prepared pack files…");
            try { await Task.Run(() => HellForgePack.VerifyAsync(candidate, selection, WorkToken), WorkToken); return candidate; }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or KeyNotFoundException) { }
        }
        return null;
    }
    async Task<string> ExportAsync(HellForgeClient client, HellForgePack.Selection selection, IProgress<string> report)
    {
        var sources = new List<string>();
        var game = GameLocator.AutoDetect();
        if (game != null)
            foreach (var name in new[] { "mods_download", "mods_old", "mods_off", "data" })
            { var path = Path.Combine(game, name); if (Directory.Exists(path)) sources.Add(path); }
        var mods = Path.Combine(client.Root, "mods"); if (Directory.Exists(mods)) sources.Add(mods);
        var output = Path.Combine(client.CacheRoot, "exports", preferences.PackMode + "-" + Guid.NewGuid().ToString("N"));
        var plan = await Task.Run(() => HellForgePack.PlanAsync(selection, output, sources, report, WorkToken), WorkToken);
        if (plan.Missing.Count > 0)
        {
            if (MessageBox.Show(this, $"Download {Pack.FormatBytes(plan.Missing.Sum(f => f.Size))} of verified Star Wars assets?\n\nExisting local files will be reused.",
                "Prepare Star Wars pack", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                throw new OperationCanceledException();
        }
        await Task.Run(() => HellForgePack.ExportAsync(plan, true, report, WorkToken), WorkToken);
        return output;
    }
    async Task LaunchAsync() => await OperationAsync(async () => {
        if (!SteamReadyCheck()) {
            Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });
            message.Text = "Steam is opening. Finish signing in, then launch your Forge session here.";
            return;
        }
        message.Text = "Checking the selected mode and asking HellForge to start…";
        var client = Client();
        var profile = await client.EnsureAccountAsync(WorkToken);
        if (preferences.IsRegular) await Task.Run(() => client.PlayRegularAsync(WorkToken, profile), WorkToken);
        else await Task.Run(() => client.PlayAsync(preferences.Selection(), WorkToken, profile), WorkToken);
        message.Text = "HellForge accepted the launch. Session status will update here; check Session details if startup fails.";
    });
    async Task EndSessionAsync() => await OperationAsync(async () => {
        state.Text = "Ending session…";
        launch.Text = "ENDING SESSION…";
        message.Text = "Asking HellForge to close the game and finish cleanup…";
        await Client().EndSessionAsync(WorkToken);
        lastStatus = await Client().StatusAsync(WorkToken);
        message.Text = "Session ended. HellForge cleanup is complete.";
    });
    async Task SyncAccountAsync() => await OperationAsync(async () => {
        var client = Client();
        await client.SelectAccountAsync(WorkToken);
        if (lastAccount is { Captured: true, Imported: false })
        {
            message.Text = "Importing your HD2 account with a local backup…";
            await client.ImportAccountAsync(WorkToken);
            lastAccount = await new HellForgeAccount(client.Root).ReadAsync(WorkToken);
            message.Text = "Account imported. Regular, Clones and Empire now use this character.";
        }
        else
        {
            Process.Start(new ProcessStartInfo(Path.Combine(client.Root, "HellForge.exe")) { UseShellExecute = true, WorkingDirectory = client.Root });
            message.Text = "In HellForge select default, then Profile > Account sync > Sync Inventory. Approve Windows and pass the HD2 title screen. Close HD2 after capture, then return here and Apply HD2 import. Later syncs merge your progress forward.";
        }
    });
    async void Locate()
    {
        if (preview || working) return;
        using var picker = new FolderBrowserDialog { Description = "Choose the HellForge folder containing HellForge.exe", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try {
            preferences.InstallRoot = HellForgeCommands.ValidateRoot(picker.SelectedPath); Save();
            lastStatus = null; lastMods = null; lastAccount = null; captureBlock = null; lastError = null; UpdateView();
            if (refreshCompletion != null) await refreshCompletion.Task;
            if (!IsDisposed) await RefreshAsync();
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Locate HellForge", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    void OpenHellForge()
    {
        if (preview || working) return;
        try
        {
            if (preferences.InstallRoot == null)
            {
                Process.Start(new ProcessStartInfo("https://github.com/SkyeShade/HellForge/releases") { UseShellExecute = true });
                return;
            }
            var client = Client();
            Process.Start(new ProcessStartInfo(Path.Combine(client.Root, "HellForge.exe")) { UseShellExecute = true, WorkingDirectory = client.Root });
        }
        catch (Exception e) { message.Text = e.Message; }
    }
    void OpenSteamLibrary()
    {
        if (preview || working) return;
        try { Process.Start(new ProcessStartInfo("steam://nav/games/details/553850") { UseShellExecute = true }); }
        catch (Exception e) { message.Text = e.Message; }
    }
    async Task ShowDetailsAsync()
    {
        if (preview || working) return;
        var launcherDiagnostic = lastError; // OperationAsync clears transient errors before invoking its action.
        await OperationAsync(async () => {
            var client = Client(); var status = await client.StatusAsync(WorkToken); var mods = await client.ModsAsync(WorkToken);
            var text = $"HellForge: {status.Version}\r\nGame build: {status.Build}\r\nGame copy ready: {status.GameCopyReady}\r\nSession running: {status.Running}\r\nLast session: {status.LastStatus ?? "none"}\r\nLast failure: {status.LastFailure ?? "none"}\r\nLauncher diagnostic: {launcherDiagnostic ?? "none"}\r\nLast notice: {status.LastNotice ?? "none"}\r\nSquad modlist: {mods.Code ?? "no gameplay mods enabled"}\r\n\r\nMultiplayer validation: pending actual squad playtest\r\n\r\n" +
                string.Join("\r\n", mods.Mods.Select(m => $"{(m.Enabled ? "ON" : "OFF")}  {m.Name}  ({m.Id})"));
            using var dialog = new Form { Text = "HellForge session details", Size = new Size(710, 520), StartPosition = FormStartPosition.CenterParent, BackColor = Bg, ForeColor = TextMain };
            dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, BackColor = Bg, ForeColor = TextMain, Text = text, Font = new Font("Consolas", 10) });
            dialog.ShowDialog(this);
        });
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // An in-flight CLI import must be allowed to finish. Closing this window never stops a game.
        if (working) { e.Cancel = true; message.Text = "Please wait for pack preparation or the CLI operation to finish."; }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed) { resourcesDisposed = true; timer.Dispose(); lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
