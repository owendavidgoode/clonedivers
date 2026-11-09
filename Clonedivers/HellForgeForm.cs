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
    static string SteamUser()
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", null)?.ToString() ?? ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return ""; }
    }
    internal const string SyncInstructions = "In HellForge choose Profile > Account sync > Sync Inventory (default is selected). Approve Windows and pass HD2's title screen. " +
        "When HD2 closes, return here. Leave Apply import and auto-apply off. Clonedivers verifies and merges your sealed capture automatically.";
    internal static string FailureSummary(string diagnostic)
    {
        if (diagnostic.Contains("Steam ownership was not confirmed", StringComparison.OrdinalIgnoreCase))
            return "Sign in to Steam with the account that owns HD2 and review Session details to confirm ownership.";
        if (System.Text.RegularExpressions.Regex.IsMatch(diagnostic, @"\b(5\d\d|408|429)\b") ||
            diagnostic.Contains("HttpRequestException") || diagnostic.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("responding", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("No such host", StringComparison.OrdinalIgnoreCase))
            return "GitHub didn't respond. Try again in a few minutes.";
        if (diagnostic.Contains("SHA", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("integrity", StringComparison.OrdinalIgnoreCase))
            return "Download again because the file could not be verified.";
        if (diagnostic.Contains("space", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("disk full", StringComparison.OrdinalIgnoreCase))
            return "Free some disk space before trying again.";
        if (diagnostic.Contains("denied", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("permission", StringComparison.OrdinalIgnoreCase))
            return "Review folder permissions in More > Session details before trying again.";
        if (diagnostic.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "Review More > Session details before retrying the timed-out HellForge step.";
        return "Open More > Session details to review why this step couldn't finish.";
    }
    readonly HellForgePreferences preferences;
    readonly bool preview;
    readonly ModeButton regular = new() { Mode = LauncherMode.Helldivers, Text = "Regular", Dock = DockStyle.Fill };
    readonly ModeButton clone = new() { Mode = LauncherMode.Clonedivers, Text = "Clones", Dock = DockStyle.Fill };
    readonly ModeButton empire = new() { Mode = LauncherMode.EmpireDivers, Text = "Empire", Dock = DockStyle.Fill };
    readonly FlowLayoutPanel choices, textureRow;
    readonly ToolStripMenuItem accountSync = new("Sync HD2 account") { AccessibleName = "Sync HD2 account" };
    readonly Label optionsLabel = new() { AutoSize = true, ForeColor = TextDim };
    readonly Label appearanceInfo = new() { AutoSize = true, ForeColor = TextDim };
    readonly TableLayoutPanel appearance = new() { Height = 108, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 0, 12) };
    readonly CheckBox delta = new() { Text = "Delta Squad armor", AutoSize = true };
    readonly CheckBox droids = new() { Text = "CIS droids", AutoSize = true };
    readonly CheckBox covenant = new() { Text = "Covenant squids", AutoSize = true };
    readonly TableLayoutPanel textures = new() { Width = 240, Height = 36, ColumnCount = 2, RowCount = 1, AccessibleName = "Texture profile", AccessibleRole = AccessibleRole.Grouping,
        AccessibleDescription = "Switch between full textures and full-detail textures streamed on demand." };
    readonly RadioButton fullTextures = new() { Text = "Full", AccessibleName = "Full textures", Appearance = Appearance.Button, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleCenter };
    readonly RadioButton lighterTextures = new() { Text = "Lighter", AccessibleName = "Lighter textures", AccessibleDescription = "Full-detail textures streamed on demand.", Appearance = Appearance.Button, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleCenter };
    readonly Label state = new() { AutoSize = true, ForeColor = TextMain, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    readonly Label detail = new() { AutoSize = true, ForeColor = TextDim, MaximumSize = new Size(640, 0) };
    readonly ToolStripMenuItem location = new() { Enabled = false };
    readonly Label message = new() { AutoSize = true, ForeColor = TextDim, MaximumSize = new Size(640, 0) };
    readonly ToolStripMenuItem prepare = new("Set up selected mode");
    readonly RoundButton launch = new() { Text = "LAUNCH CLONEDIVERS", Height = 54, Dock = DockStyle.Fill };
    readonly ForgeProgressBar progress = new() { Dock = DockStyle.Fill, Height = 12, AccessibleName = "Download progress", Visible = false, Style = ProgressBarStyle.Marquee };
    bool cancellable;
    bool Cancellable
    {
        get => cancellable;
        set {
            cancellable = value;
            if (!working) return;
            launch.Text = value ? "CANCEL" : "WORKING…"; launch.Enabled = value; launch.AccessibleName = launch.Text;
        }
    }
    readonly FlowLayoutPanel actions = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.None, WrapContents = true, Margin = new Padding(0, 0, 0, 12) };
    readonly TableLayoutPanel activity = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0, 0, 0, 10) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    readonly ToolStripMenuItem updates = new("Check updates");
    readonly Button later = new() { Text = "Not now", Width = 120, Height = 36, Visible = false, AccessibleName = "Update later" };
    readonly ContextMenuStrip moreMenu = new() { BackColor = Slate, ForeColor = TextMain, RenderMode = ToolStripRenderMode.System };
    bool updateDeferred, updateFailed, updateDownloading, updateCancelled, launcherReplaced;
    AppRelease? launcherRelease;
    bool checkingUpdates;
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? operation;
    CancellationToken WorkToken => operation?.Token ?? lifetime.Token;
    HellForgeStatus? lastStatus;
    HellForgeMods? lastMods;
    string? lastModsFingerprint;
    DateTime lastModsReadUtc, lastPartialSweepUtc;
    HellForgeAccountState? lastAccount;
    bool accountReviewFailed;
    string? lastAccountFingerprint;
    DateTime lastAccountReadUtc;
    string? captureBlock;
    bool syncRequested;
    bool working, refreshing, initializing = true, resourcesDisposed, retryStatus, detailsAction;
    TaskCompletionSource? refreshCompletion;
    string? lastError, failureDetail; // a refresh clears lastError; the failed operation's full text stays for Session details
    string? LauncherDiagnostic => failureDetail ?? lastError;
    string previewState = "setup";

    public HellForgeForm(HellForgePreferences preferences, bool preview = false, string? migrationNotice = null)
    {
        this.preferences = preferences;
        this.preview = preview;
        preferences.Validate();
        Text = "Clonedivers — HellForge";
        BackColor = Bg; ForeColor = TextMain; DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(880, 690); MinimumSize = new Size(700, 600);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch (ArgumentException) { }
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        Controls.Add(scroll);
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(32, 24, 32, 8), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.SizeChanged += (_, _) => {
            var wrap = new Size(Math.Max(160, root.ClientSize.Width - root.Padding.Horizontal), 0);
            foreach (var label in new[] { state, detail, optionsLabel, appearanceInfo, message })
                if (label.MaximumSize != wrap) label.MaximumSize = wrap;
        };
        for (int i = 0; i < root.RowCount; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles[4] = new RowStyle(SizeType.Absolute, 64);
        scroll.Controls.Add(root);

        var header = new TableLayoutPanel { Anchor = AnchorStyles.None, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 16) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new HelmetMark { Width = 50, Height = 60, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 16, 0) }, 0, 0);
        var heading = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        heading.Controls.Add(new Label { Text = "CLONEDIVERS", Font = new Font("Segoe UI", 26, FontStyle.Bold), AutoSize = true, Margin = Padding.Empty });
        heading.Controls.Add(new Label { Text = "Choose your universe  ·  HellForge edition", ForeColor = TextDim, Font = new Font("Segoe UI", 10.5F), AutoSize = true, Margin = new Padding(2, 0, 0, 0) });
        header.Controls.Add(heading, 1, 0); root.Controls.Add(header, 0, 0);
        var modes = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Height = 168, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16) };
        for (int i = 0; i < 3; i++) modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        regular.Margin = new Padding(0, 0, 8, 0); clone.Margin = new Padding(4, 0, 4, 0); empire.Margin = new Padding(8, 0, 0, 0);
        foreach (var button in new[] { regular, clone, empire }) button.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        modes.Controls.Add(regular, 0, 0); modes.Controls.Add(clone, 1, 0); modes.Controls.Add(empire, 2, 0); root.Controls.Add(modes, 0, 1);
        appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appearance.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        appearance.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        appearance.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        choices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = true };
        foreach (var check in new[] { delta, droids, covenant }) { check.Margin = new Padding(0, 4, 20, 4); choices.Controls.Add(check); }
        optionsLabel.Margin = Padding.Empty; appearance.Controls.Add(optionsLabel, 0, 0);
        var appearanceOptions = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        appearanceInfo.Location = new Point(0, 4); appearanceOptions.Controls.Add(appearanceInfo); appearanceOptions.Controls.Add(choices);
        appearance.Controls.Add(appearanceOptions, 0, 1);
        textureRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        textureRow.Controls.Add(new Label { Text = "Textures", AutoSize = true, ForeColor = TextDim, Margin = new Padding(0, 5, 12, 0) });
        textures.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); textures.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var option in new[] { fullTextures, lighterTextures }) {
            option.FlatStyle = FlatStyle.Flat; option.FlatAppearance.BorderColor = Border; option.FlatAppearance.CheckedBackColor = Color.FromArgb(24, 47, 82);
            option.FlatAppearance.MouseOverBackColor = SlateHot; option.BackColor = Bg; option.ForeColor = TextMain; option.Font = new Font("Segoe UI", 10F);
        }
        textures.Controls.Add(fullTextures, 0, 0); textures.Controls.Add(lighterTextures, 1, 0);
        textureRow.Controls.Add(textures); appearance.Controls.Add(textureRow, 0, 2); root.Controls.Add(appearance, 0, 2);
        var statusCard = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 12) };
        statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var label in new[] { state, detail }) { label.MaximumSize = new Size(840, 0); label.Anchor = AnchorStyles.None; label.TextAlign = ContentAlignment.MiddleCenter; label.Margin = new Padding(0, 0, 0, 6); statusCard.Controls.Add(label); }
        root.Controls.Add(statusCard, 0, 3);
        Style(launch, Orange, OrangeHot, Bg); launch.Margin = new Padding(0, 0, 0, 10);
        launch.AccessibleName = "Primary action";
        root.Controls.Add(launch, 0, 4);
        activity.Controls.Add(progress); message.Margin = new Padding(0, 5, 0, 0); activity.Controls.Add(message); root.Controls.Add(activity, 0, 6);
        message.TextChanged += (_, _) => activity.Visible = working || !string.IsNullOrWhiteSpace(message.Text) || Cancellable;
        foreach (var (caption, handler) in new (string, EventHandler)[] {
            ("Setup && mods", (_, _) => OpenHellForge()), ("Locate…", (_, _) => Locate()),
            ("Refresh", async (_, _) => await RefreshAsync()), ("Session details", async (_, _) => await ShowDetailsAsync()) })
        {
            var item = new ToolStripMenuItem(caption); item.Click += handler; moreMenu.Items.Add(item);
        }
        moreMenu.Items.Add(new ToolStripSeparator()); moreMenu.Items.Add(prepare); moreMenu.Items.Add(accountSync);
        moreMenu.Items.Add(updates); moreMenu.Items.Add(new ToolStripSeparator()); moreMenu.Items.Add(location);
        var more = new Button { Text = "&More options \u2304", Width = 180, Height = 36, AccessibleName = "More launcher options" };
        foreach (var button in new[] { more, later }) {
            Style(button, Bg, SlateHot, TextMain); button.Font = new Font("Segoe UI", 10F);
            button.FlatAppearance.BorderSize = 1; button.FlatAppearance.BorderColor = Border; button.FlatAppearance.MouseDownBackColor = Slate;
            button.Margin = new Padding(6, 0, 6, 0);
        }
        more.Click += (_, _) => moreMenu.Show(more, new Point(0, more.Height));
        actions.Controls.Add(more);
        later.Click += (_, _) => { updateDeferred = true; updateFailed = false; lastError = null; message.Text = ""; UpdateView(); };
        actions.Controls.Add(later); root.Controls.Add(actions, 0, 5);
        updates.Click += async (_, _) => { if (launcherRelease != null) { updateDeferred = false; await ApplyLauncherUpdateAsync(); } else await CheckLauncherUpdateAsync(true); };
        // Keep the Steam fallback and version visible even when Windows clamps
        // the initial window to a short screen. Only the main content scrolls.
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, ColumnCount = 1,
            Padding = new Padding(32, 4, 32, 18), Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var steam = new LinkLabel { Text = "Official online game: open Helldivers 2 in Steam", AutoSize = true, LinkColor = TextDim, ActiveLinkColor = TextMain, VisitedLinkColor = TextDim, Margin = new Padding(0, 3, 0, 8) };
        steam.Anchor = AnchorStyles.None; steam.LinkClicked += (_, _) => OpenSteamLibrary(); footer.Controls.Add(steam);
        footer.Controls.Add(new Label { Text = $"v{AppInfo.Version}", AccessibleName = "Launcher version", Anchor = AnchorStyles.None, AutoSize = true, ForeColor = TextMute, Font = new Font("Segoe UI", 9F), Margin = Padding.Empty });
        Controls.Add(footer);
        regular.Click += (_, _) => ChangeMode("regular"); clone.Click += (_, _) => ChangeMode("clonedivers"); empire.Click += (_, _) => ChangeMode("empire");
        accountSync.Click += async (_, _) => await SyncAccountAsync();
        delta.Checked = preferences.DeltaSquad; droids.Checked = preferences.Droids; covenant.Checked = preferences.Covenant;
        fullTextures.Checked = preferences.Texture == "full"; lighterTextures.Checked = preferences.Texture == "lighter";
        foreach (var check in new[] { delta, droids, covenant }) check.CheckedChanged += (_, _) => ChangeOptions();
        fullTextures.CheckedChanged += (_, _) => { if (fullTextures.Checked) ChangeTexture("full"); };
        lighterTextures.CheckedChanged += (_, _) => { if (lighterTextures.Checked) ChangeTexture("lighter"); };
        prepare.Click += async (_, _) => await PrepareAsync();
        AcceptButton = launch;
        launch.Click += async (_, _) => {
            if (preview) return;
            if (working) { if (Cancellable) { Cancellable = false; operation?.Cancel(); } return; }
            if (OfferUpdate) await ApplyLauncherUpdateAsync();
            else if (lastStatus?.Running == true) await EndSessionAsync();
            else if (detailsAction) await ShowDetailsAsync();
            else if (retryStatus) await RefreshAsync();
            else if (preferences.InstallRoot is { } root && HellForgeSetup.UpgradeProblem(root) != null) await RecoverForgeAsync();
            else if (lastStatus is { } old && HellForgeSetup.IsPrevious(old.Version)) await UpgradeForgeAsync();
            else if (preferences.InstallRoot == null || lastStatus is { GameCopyReady: false } or { RunnerReady: false }) await SetupAsync();
            else if (lastStatus?.CompatibilityProblem != null) await SetupAsync();
            else if (lastStatus?.RecoveryProblem != null) await PrepareAsync();
            else if (!SteamReadyCheck()) await LaunchAsync();
            else if (lastMods?.Problems.Length > 0) ReviewModNotices();
            else if (NeedsGameplaySetup()) await PrepareAsync();
            else if (NeedsModeSetup()) await PrepareAsync();
            else if (lastAccount?.Imported != true) await SyncAccountAsync();
            else await LaunchAsync();
        };
        timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { if (!preview) { await RefreshAsync(); if (!IsDisposed && !Disposing) { timer.Start(); await CheckLauncherUpdateAsync(false); } } };
        initializing = false;
        AutoScaleDimensions = new SizeF(96, 96);
        UpdateView();
        if (!string.IsNullOrWhiteSpace(migrationNotice)) message.Text = migrationNotice;
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dwm.ApplyDark(Handle, Bg, TextMain, Border); }
    static void Style(Button button, Color back, Color hot, Color text)
    {
        button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = hot;
        button.BackColor = back; button.ForeColor = text; button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI", 11F, FontStyle.Bold); button.Cursor = Cursors.Hand;
    }
    internal static readonly string[] PreviewStates = ["ready", "update-available", "update-downloading", "update-failed", "setup", "installing", "sync", "running", "error", "update-ready"];
    public void SetPreviewState(string value)
    {
        if (!preview) throw new InvalidOperationException();
        if (!PreviewStates.Contains(value)) throw new ArgumentException("Unknown Forge preview state.", nameof(value));
        previewState = value; UpdateView();
    }
    void ChangeMode(string mode) { if (working) return; preferences.Mode = mode; Save(); UpdateView(); }
    void ChangeTexture(string texture) { if (working || preferences.Texture == texture) return; preferences.Texture = texture; ChangeOptions(); }
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
            string? fingerprint;
            try { fingerprint = root + "\n" + client.ModsFingerprint(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { fingerprint = null; }
            // A running session cannot change what is staged; idle, rescan when the folder changes or each minute.
            var reuse = lastMods != null && (status.Running ||
                fingerprint != null && fingerprint == lastModsFingerprint && DateTime.UtcNow - lastModsReadUtc < TimeSpan.FromMinutes(1));
            var mods = reuse ? lastMods! : await client.ModsAsync(lifetime.Token);
            if (!reuse) { lastModsFingerprint = fingerprint; lastModsReadUtc = DateTime.UtcNow; }
            if (!status.Running && !Game.IsRunning() && DateTime.UtcNow - lastPartialSweepUtc > TimeSpan.FromMinutes(10))
            {
                lastPartialSweepUtc = DateTime.UtcNow;
                try { client.SweepHashPartials(DateTime.UtcNow.AddMinutes(-10)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }
            string? block;
            try { block = HellForgeAccount.CaptureBlock(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { block = e.ToString(); }
            HellForgeAccountState? account = lastAccount;
            if (!status.Running && !Game.IsRunning())
            {
                string? accountKey;
                try { accountKey = root + "\n" + client.AccountFingerprint() + "\n" + SteamUser(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { accountKey = null; }
                // The review starts HellForge's backend; idle, rerun it when the capture/profile/Steam user change or every 5 minutes.
                if (lastAccount == null || accountKey == null || accountKey != lastAccountFingerprint || DateTime.UtcNow - lastAccountReadUtc >= TimeSpan.FromMinutes(5))
                {
                    (account, var reviewed) = await ReviewAccountAsync(new HellForgeAccount(root), lifetime.Token);
                    if (reviewed && account.Imported && block == null)
                    {
                        var opened = await client.TryOpenOwnedWarbondsAsync(lifetime.Token);
                        if (opened > 0) message.Text = $"Opened {opened} owned Warbonds";
                    }
                    accountReviewFailed = !reviewed;
                    lastAccountFingerprint = reviewed ? accountKey : null; lastAccountReadUtc = DateTime.UtcNow;
                }
            }
            if (root != preferences.InstallRoot) return;
            lastStatus = status; lastMods = mods; lastAccount = account; captureBlock = block; lastError = null;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (!IsDisposed && root == preferences.InstallRoot) { lastMods = null; lastError = e.ToString(); } }
        finally { refreshing = false; completed.TrySetResult(); if (!IsDisposed) UpdateView(); }
        // Only finish an import the player requested in this launcher. Startup never
        // replays an old capture, and the backend still verifies its account/PC seal.
        if (!IsDisposed && syncRequested && captureBlock == null && lastStatus?.Running == false &&
            !Game.IsRunning() && lastAccount is { Captured: true, Imported: false })
            await SyncAccountAsync();
    }
    // A read-only review failure (timeout, missing backend) is an account state, never a refresh failure that drops the mods read.
    internal static async Task<(HellForgeAccountState State, bool Reviewed)> ReviewAccountAsync(HellForgeAccount account, CancellationToken ct)
    {
        try { return (await account.ReadAsync(ct), true); }
        catch (Exception error) when (error is not OperationCanceledException) { return (new(false, false, error.Message), false); }
    }
    bool OfferUpdate => launcherRelease != null && !updateDeferred && updates.Enabled;
    void ShowUpdateOffer(string version, bool failed)
    {
        state.Text = launcherReplaced ? "Update installed" : failed ? "Update couldn't finish" : "Update available — v" + version;
        detail.Text = "Download the verified launcher and restart to finish updating.";
        launch.Text = "UPDATE LAUNCHER";
        if (failed) {
            detail.Text = FailureSummary(failureDetail ?? "504");
            if (detail.Text.StartsWith("GitHub", StringComparison.Ordinal)) detail.Text += " Nothing was changed.";
            launch.Text = "RETRY UPDATE";
        }
        if (updateCancelled) { detail.Text = "Your update download is saved and ready to resume."; launch.Text = "RESUME UPDATE"; }
        if (launcherReplaced) { detail.Text = "Reopen the launcher to use the installed update."; launch.Text = "RESTART LAUNCHER"; }
        launch.Enabled = true;
        launch.AccessibleName = launch.Text;
        later.Visible = true;
    }
    void UpdateView()
    {
        UpdateState();
        launch.AccessibleName = launch.Text;
        if (preview) return;
        later.Visible = false;
        message.Visible = working;
        activity.Visible = working;
        if (working) return;
        if (OfferUpdate) ShowUpdateOffer(launcherRelease!.Version, updateFailed);
        else if (lastError == null && !string.IsNullOrWhiteSpace(message.Text)) detail.Text = message.Text;
    }
    void UpdateState()
    {
        retryStatus = detailsAction = false;
        activity.Visible = working || !string.IsNullOrWhiteSpace(message.Text) || Cancellable;
        updates.Enabled = !working && !checkingUpdates && lastStatus?.Running != true && (preview || !Game.IsRunning()) && captureBlock == null;
        accountSync.Enabled = !working && (preview ? previewState != "running" : preferences.InstallRoot != null && lastStatus?.Running != true && !Game.IsRunning());
        accountSync.Text = lastAccount is { Captured: true, Imported: false } ? "APPLY HD2 IMPORT" : "SYNC HD2 ACCOUNT";
        regular.Selected = preferences.IsRegular;
        clone.Selected = preferences.Mode == "clonedivers"; empire.Selected = preferences.Mode == "empire";
        appearance.Visible = !working;
        choices.Visible = textureRow.Visible = !working && preferences.Mode == "clonedivers";
        optionsLabel.Text = preferences.Mode switch { "empire" => "Empire appearance", "regular" => "Regular appearance", _ => "Clones appearance" };
        appearanceInfo.Visible = preferences.Mode != "clonedivers";
        appearanceInfo.Text = preferences.IsRegular ? "Standard Helldivers appearance with your selected gameplay mods." : "r45 · Full textures and CIS droids included.";
        delta.Visible = preferences.Mode == "clonedivers";
        delta.Enabled = !working && preferences.Mode == "clonedivers";
        foreach (var control in new Control[] { regular, clone, empire, droids, covenant, textures, actions }) control.Enabled = !working;
        actions.Visible = !working;
        fullTextures.Checked = preferences.Texture == "full"; lighterTextures.Checked = preferences.Texture == "lighter";
        foreach (var option in new[] { fullTextures, lighterTextures }) option.FlatAppearance.BorderColor = option.Checked ? BlueHot : Border;
        prepare.Text = "SET UP SELECTED MODE";
        prepare.Enabled = !working && preferences.InstallRoot != null && !preview;
        location.Text = preview ? "D:\\HellForge" : preferences.InstallRoot ?? "No HellForge installation selected";
        launch.Text = "LAUNCH " + (preferences.IsRegular ? "REGULAR HELLFORGE" : preferences.Mode == "empire" ? "EMPIREDIVERS" : "CLONEDIVERS");
        launch.Enabled = false;
        if (preview)
        {
            var busy = previewState is "installing" or "update-downloading";
            prepare.Enabled = accountSync.Enabled = previewState != "running" && !busy;
            foreach (var control in new Control[] { regular, clone, empire, choices, textures }) control.Enabled = !busy;
            launch.Enabled = !busy;
            state.Text = "Ready to launch";
            detail.Text = "Choose difficulty 1–11 at the war table.";
            message.Text = ""; later.Visible = false;
            progress.Visible = busy; progress.Style = ProgressBarStyle.Continuous; progress.Value = 42;
            choices.Visible = textureRow.Visible = !busy && preferences.Mode == "clonedivers";
            appearance.Visible = !busy;
            actions.Visible = !busy;
            if (previewState == "setup") { state.Text = "Set up HellForge"; detail.Text = "We'll guide you through installing the separate game copy and your chosen mode."; launch.Text = "SET UP CLONEDIVERS"; }
            if (previewState == "sync") { state.Text = "Import your HD2 account"; detail.Text = "Sync once to bring your account into Forge, where progress saves locally."; launch.Text = "SYNC HD2 ACCOUNT"; }
            if (previewState == "installing") { state.Text = "Installing mods"; detail.Text = "Preparing your selected mode for HellForge."; message.Text = "Downloading gameplay mods · 42%"; }
            if (previewState == "running") { state.Text = "Session running"; detail.Text = "End the session to close the game and finish cleanup."; launch.Text = "END SESSION"; }
            if (previewState == "error") { state.Text = "Needs attention"; detail.Text = FailureSummary("Fixture failure"); launch.Text = "RETRY"; }
            if (previewState is "update-available" or "update-failed") ShowUpdateOffer("1.15.0", previewState == "update-failed");
            if (previewState == "update-downloading") { state.Text = "Downloading update · v1.15.0"; detail.Text = "Temporary connection problems retry automatically until you cancel."; launch.Text = "DOWNLOADING…"; message.Text = "28 MB / 66 MB · 42%"; }
            if (previewState == "update-ready") { state.Text = "Update verified"; detail.Text = "The launcher is restarting with the new version."; launch.Text = "RESTARTING…"; launch.Enabled = false; }
            if (busy) { launch.Text = "CANCEL"; launch.Enabled = true; }
            activity.Visible = busy;
            return;
        }
        if (working) { launch.Text = Cancellable ? "CANCEL" : "WORKING…"; launch.Enabled = Cancellable; return; }
        if (lastStatus?.Running == true || Game.IsRunning()) {
            state.Text = lastStatus?.Running == true ? "HellForge session running" : "Your game is running";
            detail.Text = lastStatus?.Running == true ? "End the session to close the game and finish cleanup." :
                "Close the game before installing or launching a Forge session.";
            if (lastError != null) detail.Text = FailureSummary(lastError);
            if (lastStatus?.Running == true) { prepare.Enabled = false; launch.Text = "END SESSION"; launch.Enabled = true; }
            else EnableDetails();
            return;
        }
        if (preferences.InstallRoot is { } upgradeRoot && HellForgeSetup.UpgradeProblem(upgradeRoot) != null) {
            state.Text = "Restore the previous Forge application"; detail.Text = "Restore the previous Forge application before continuing."; launch.Text = "RECOVER FORGE UPGRADE"; launch.Enabled = true;
            prepare.Enabled = accountSync.Enabled = false; return;
        }
        if (lastStatus is { } old && HellForgeSetup.IsPrevious(old.Version)) {
            state.Text = "Upgrade HellForge for squad play"; detail.Text = "Install the official Forge update with a recovery backup of your current application.";
            launch.Text = "UPDATE HELLFORGE"; launch.Enabled = true; prepare.Enabled = accountSync.Enabled = false; return;
        }
        if (lastStatus != null && HellForgeSetup.SupportsVersion(lastStatus.Version) && lastStatus.Build == HellForgePack.SupportedBuild && (!lastStatus.GameCopyReady || !lastStatus.RunnerReady)) {
            state.Text = "Let’s finish your setup";
            detail.Text = "Continue guided setup to prepare the game copy and Windows approval.";
            launch.Text = "FINISH SETUP"; launch.Enabled = true; prepare.Enabled = accountSync.Enabled = false; return;
        }
        if (lastStatus?.CompatibilityProblem != null) {
            state.Text = "Check HellForge compatibility"; detail.Text = "Review your HellForge installation in More > Session details.";
            if (HellForgeSetup.SupportsVersion(lastStatus.Version) && lastStatus.Engine == HellForgeRelease.Engine && lastStatus.ModSdk == HellForgeRelease.Sdk) { launch.Text = "CONTINUE GUIDED SETUP"; launch.Enabled = true; }
            else EnableDetails();
            prepare.Enabled = accountSync.Enabled = false; return;
        }
        if (lastStatus?.RecoveryProblem is { } recovery) {
            var gameplay = recovery.StartsWith("Gameplay", StringComparison.Ordinal);
            state.Text = gameplay ? "Recover the previous gameplay preset" : "Recover the previous appearance"; detail.Text = "Restore the previous setup before preparing your chosen mode.";
            prepare.Text = gameplay ? "RECOVER GAMEPLAY PRESET" : "RECOVER PREVIOUS APPEARANCE"; prepare.Enabled = true; accountSync.Enabled = false;
            launch.Text = prepare.Text; launch.Enabled = true;
            return;
        }
        if (preferences.InstallRoot != null && lastStatus != null && !SteamReadyCheck()) {
            state.Text = "Start Steam and sign in"; detail.Text = "Sign in to Steam with the account that owns HD2 before launching here.";
            launch.Text = "OPEN STEAM"; launch.Enabled = true; return;
        }
        if (lastError != null) { state.Text = "Needs attention"; detail.Text = FailureSummary(lastError); launch.Text = "RETRY"; launch.Enabled = retryStatus = true; return; }
        if (preferences.InstallRoot == null) {
            state.Text = "Let’s get you ready"; detail.Text = "We'll guide you through the separate game copy and your chosen mode.";
            launch.Text = "SET UP CLONEDIVERS"; launch.Enabled = true; return;
        }
        if (lastStatus == null) { state.Text = "Checking HellForge…"; detail.Text = "Reading installation and pack information."; return; }
        if (captureBlock != null) { state.Text = "Finish account sync"; detail.Text = "Review account sync cleanup in Session details before continuing."; accountSync.Enabled = false; prepare.Enabled = false; EnableDetails(); return; }
        if (!lastStatus.GameCopyReady || !lastStatus.RunnerReady) { state.Text = "Finish HellForge setup"; detail.Text = "Continue guided setup to download or resume the supported game copy."; launch.Text = "FINISH SETUP"; launch.Enabled = true; return; }
        if (lastStatus.Build != HellForgePack.SupportedBuild) { state.Text = "Pack update needed"; detail.Text = "This pack requires a compatible HellForge game build."; EnableDetails(); return; }
        if (lastMods?.Problems.Length > 0) { state.Text = "Review conflicting mod files"; detail.Text = "Choose which duplicate or custom mods to keep in HellForge."; launch.Text = "REVIEW MOD CHOICES"; launch.Enabled = true; return; }
        if (NeedsGameplaySetup()) {
            state.Text = "Set up your required gameplay mods";
            detail.Text = "Install the pinned gameplay preset and apply its required mod settings.";
            launch.Text = "INSTALL REQUIRED MODS"; launch.Enabled = true; return;
        }
        if (lastMods != null && HellForgeClient.ModConflict(lastMods) != null) { state.Text = "Resolve the mod overlap"; detail.Text = "Review overlapping mods in More > Setup & mods before continuing."; EnableDetails(); return; }
        if (lastMods?.HostingProblem != null) {
            state.Text = "Confirm your mod-sharing setting"; detail.Text = "Apply the required mod-sharing setting before launching.";
            launch.Text = "INSTALL REQUIRED MODS"; launch.Enabled = true; return;
        }
        if (!preferences.IsRegular && preferences.AppliedSelection != preferences.SelectionKey) { state.Text = "Prepare your Star Wars pack"; detail.Text = "Apply your selected universe and texture options to HellForge."; EnableModeSetup(); return; }
        var our = lastMods?.Mods.Where(m => m.Enabled && HellForgePack.IsOurPack(m.Id)).ToList() ?? [];
        if (preferences.IsRegular && our.Count != 0) { state.Text = "Apply Regular mode"; detail.Text = "Disable the Star Wars appearance packs. Your selected HellForge gameplay mods remain active."; EnableModeSetup(); return; }
        if (!preferences.IsRegular && (our.Count != 1 || our[0].Id != HellForgePack.PackId(preferences.PackMode))) { state.Text = "Prepare your Star Wars pack"; detail.Text = "Apply your selected universe and texture options to HellForge."; EnableModeSetup(); return; }
        if (lastMods?.Mods.Any(m => m.Id == HellForgeClient.DifficultyId && m.Enabled) != true) { state.Text = "Enable Hell Forge Difficulty"; detail.Text = "Restore the bundled difficulty through guided setup."; launch.Text = "INSTALL REQUIRED MODS"; launch.Enabled = true; return; }
        if (lastMods != null && HellForgePreset.Problem(lastMods, checkOrder: true) != null) { state.Text = "Finish the gameplay preset"; detail.Text = "Restore the pinned gameplay preset before launching."; launch.Text = "INSTALL REQUIRED MODS"; launch.Enabled = true; return; }
        if (!lastStatus.Multiplayer || !lastStatus.LocalBackend) { state.Text = "Enable Steam co-op"; detail.Text = "Set up your selected mode to apply the local backend and Steam multiplayer automatically."; EnableModeSetup(); return; }
        if (lastAccount?.Imported != true) {
            state.Text = lastAccount?.Captured == true ? "Apply your account import" : "Import your HD2 account";
            detail.Text = accountReviewFailed ? FailureSummary(lastAccount?.Message ?? "") : "Sync your HD2 account to finish setup with local Forge progress.";
            launch.Text = lastAccount?.Captured == true ? "FINISH ACCOUNT SYNC" : "SYNC HD2 ACCOUNT"; launch.Enabled = true; return;
        }
        state.Text = "Ready to launch";
        detail.Text = "Choose difficulty 1–11 at the war table.";
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
        working = true; progress.Style = ProgressBarStyle.Marquee; progress.Visible = true; message.Text = ""; UpdateView(); state.Text = "Working…"; detail.Text = "Please wait while this step finishes."; lastError = failureDetail = null;
        lastAccountFingerprint = null; // any operation can change the account; review it again afterwards
        try { await pendingRefresh; lastError = null; await action(); }
        catch (OperationCanceledException) { if (!IsDisposed) { updateCancelled = updateDownloading; message.Text = "Operation cancelled."; } }
        catch (Exception e) { if (!IsDisposed) { lastError = e.Message; failureDetail = e.ToString(); if (updateDownloading) updateFailed = true; message.Text = FailureSummary(e.ToString()); } }
        finally
        {
            Cancellable = false; launch.Enabled = false;
            working = false; updateDownloading = false;
            operation.Dispose(); operation = null;
            // Keep the controls locked until the follow-up status read: the pre-operation status would
            // re-enable LAUNCH for a session HellForge has just started.
            if (!IsDisposed) { progress.Visible = false; if (lastError == null) await RefreshAsync(); if (!IsDisposed) UpdateView(); }
        }
    }
    async Task CheckLauncherUpdateAsync(bool interactive)
    {
        if (preview || checkingUpdates || working) return;
        checkingUpdates = true; UpdateView();
        try
        {
            launcherRelease = await ForgeLauncherUpdate.CheckAsync(AppInfo.Version, lifetime.Token);
            if (IsDisposed || Disposing) return;
            updateDeferred = false;
            updates.Text = launcherRelease == null ? "Check updates" : "Update v" + launcherRelease.Version;
            if (interactive) message.Text = launcherRelease == null ? "No newer Forge launcher is available." :
                "Forge launcher " + launcherRelease.Version + " is available. Click its update button to download and restart; game and account data are preserved.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or HttpRequestException or JsonException or InvalidDataException or InvalidOperationException)
        { if (interactive && !IsDisposed) { failureDetail = error.ToString(); message.Text = FailureSummary(error.ToString()); } }
        finally { checkingUpdates = false; if (!IsDisposed && !Disposing) UpdateView(); }
    }
    async Task RequireUpdateIdleAsync()
    {
        if (Game.IsRunning() || HellForgeAccount.CaptureBlock() != null)
            throw new InvalidOperationException("Finish the game or account sync before replacing the launcher.");
        if (preferences.InstallRoot != null && (await Client().StatusAsync(WorkToken)).Running)
            throw new InvalidOperationException("End the Forge session before replacing the launcher.");
    }
    async Task ApplyLauncherUpdateAsync() => await OperationAsync(async () => {
        var app = launcherRelease;
        if (!ForgeLauncherUpdate.IsOffer(app, AppInfo.Version)) throw new InvalidDataException("A verified Forge update is required.");
        await RequireUpdateIdleAsync();
        if (launcherReplaced) { RestartUpdatedLauncher(app!.Version); return; }
        if (MessageBox.Show(this, $"Update the Forge launcher to {app!.Version}?\n\nDownloads {Pack.FormatBytes(app.Size)} and restarts. Game files, account data and mod settings are unchanged.",
            "Update Clonedivers", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        updateDownloading = true; updateFailed = updateCancelled = false;
        var exe = Environment.ProcessPath ?? throw new IOException("The launcher executable could not be located.");
        var paths = SelfUpdate.Paths(exe);
        SelfUpdate.ProbeWritable(paths.update);
        Cancellable = true;
        state.Text = "Downloading update · v" + app.Version;
        detail.Text = "Temporary connection problems retry automatically until you cancel.";
        progress.Style = ProgressBarStyle.Continuous; progress.Value = 0;
        var report = new Progress<(long done, long total)>(p => {
            if (IsDisposed || !working || !updateDownloading) return;
            progress.Value = p.total > 0 ? (int)Math.Clamp(p.done * 100d / p.total, 0, 100) : 0;
            message.Text = Pack.FormatBytes(p.done) + " / " + Pack.FormatBytes(p.total) + " · " + progress.Value + "%";
        });
        await Pack.DownloadAsync(new PackFile { Url = app.Url, Size = app.Size, Sha256 = app.Sha256 }, paths.update, report, WorkToken);
        if (new FileInfo(paths.update).Length != app.Size || !string.Equals(await Pack.Sha256Async(paths.update, WorkToken), app.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The launcher update failed its size/SHA256 check; nothing was replaced.");
        state.Text = "Update verified"; detail.Text = "The launcher is restarting with the new version.";
        Cancellable = false; launch.Text = "RESTARTING…";
        await RequireUpdateIdleAsync();
        ForgeLauncherUpdate.PreservePrevious(exe);
        WorkToken.ThrowIfCancellationRequested();
        await Task.Run(() => SelfUpdate.Swap(paths.exe, paths.update, paths.old, TimeSpan.FromSeconds(30)));
        launcherReplaced = true;
        RestartUpdatedLauncher(app.Version);
    });
    void RestartUpdatedLauncher(string version)
    {
        var exe = Environment.ProcessPath!;
        Program.ReleaseSingleInstance();
        Process.Start(new ProcessStartInfo(exe, "--updated " + version) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        // The committed swap is complete. Closing here must not be refused as an
        // in-flight mod/account operation; those were never started by this path.
        working = false; Close();
    }
    bool NeedsGameplaySetup() => lastMods != null && HellForgePreset.Problem(lastMods, checkOrder: true) != null;
    async Task UpgradeForgeAsync() => await OperationAsync(async () => {
        var report = new Progress<ForgeSetupProgress>(p => { if (!IsDisposed) message.Text = p.Message; });
        await HellForgeSetup.UpdateAsync(preferences.InstallRoot!, report, WorkToken);
        var client = Client(); await InstallGameplayCoreAsync(client);
        // The supported game build and selected appearance have not changed. Keep
        // a prepared appearance; launch still verifies all bytes before staging it.
        var current = await client.ModsAsync(WorkToken);
        bool selected = preferences.AppliedSelection == preferences.SelectionKey;
        try { if (preferences.IsRegular) HellForgeClient.RequireRegular(current); else HellForgeClient.RequireSelected(current, preferences.Selection()); }
        catch (InvalidDataException) { selected = false; }
        var status = await client.StatusAsync(WorkToken);
        if (!selected || !status.Multiplayer || !status.LocalBackend) await PrepareCoreAsync();
        message.Text = "Forge and your selected mode are ready to launch.";
    });
    async Task RecoverForgeAsync() => await OperationAsync(async () => {
        await HellForgeSetup.RecoverUpgradeAsync(preferences.InstallRoot!, WorkToken);
        message.Text = "Your previous Forge application has been restored.";
    });
    void EnableModeSetup() { launch.Text = "SET UP SELECTED MODE"; launch.Enabled = true; }
    void EnableDetails() { detailsAction = true; launch.Text = "VIEW DETAILS"; launch.Enabled = true; }
    bool NeedsModeSetup()
    {
        var ours = lastMods?.Mods.Where(m => m.Enabled && HellForgePack.IsOurPack(m.Id)).ToArray() ?? [];
        return preferences.IsRegular ? ours.Length != 0 || lastStatus is { Multiplayer: false } or { LocalBackend: false }
            : preferences.AppliedSelection != preferences.SelectionKey || ours.Length != 1 || ours[0].Id != HellForgePack.PackId(preferences.PackMode) ||
              lastStatus is { Multiplayer: false } or { LocalBackend: false };
    }
    async Task PrepareAsync() => await OperationAsync(PrepareSelectedCoreAsync);
    async Task PrepareSelectedCoreAsync()
    {
        var client = Client();
        if (client.InterruptedGameplayProblem() != null) { await client.RecoverGameplayAsync(WorkToken); message.Text = "Previous gameplay preset restored."; return; }
        if ((await client.StatusAsync(WorkToken)).RecoveryProblem == null) await InstallGameplayCoreAsync(client);
        await PrepareCoreAsync();
        if (client.InterruptedGameplayProblem() == null && client.InterruptedPackProblem() == null &&
            preferences.AppliedSelection == preferences.SelectionKey &&
            (await new HellForgeAccount(client.Root).ReadAsync(WorkToken)).Imported != true)
            await SyncAccountCoreAsync(client);
    }
    async Task InstallGameplayCoreAsync(HellForgeClient client)
    {
        var report = new Progress<string>(text => { if (!IsDisposed) message.Text = text; });
        message.Text = "Checking every required gameplay mod…";
        var missing = await client.MissingGameplayAsync(WorkToken);
        Cancellable = true;
        state.Text = "Installing mods"; detail.Text = "Preparing your selected mode for HellForge.";
        var downloading = true;
        var bytes = new Progress<(long done, long total)>(p => {
            if (IsDisposed || !working || !downloading) return;
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = p.total > 0 ? (int)Math.Clamp(p.done * 100d / p.total, 0, 100) : 0;
            message.Text = Pack.FormatBytes(p.done) + " / " + Pack.FormatBytes(p.total) + " · " + progress.Value + "%";
        });
        try { await HellForgeGameplayCatalog.StageAsync(client.GameplayCache, missing, report, WorkToken, bytes); }
        finally { downloading = false; }
        Cancellable = false;
        progress.Style = ProgressBarStyle.Marquee;
        await client.InstallGameplayAsync(client.GameplayCache, report, WorkToken);
    }
    async Task PrepareCoreAsync()
    {
        var client = Client();
        if ((await client.StatusAsync(WorkToken)).RecoveryProblem != null)
        {
            message.Text = "Checking and restoring the previous appearance through HellForge…";
            await Task.Run(() => client.RecoverInterruptedPackAsync(WorkToken), WorkToken);
            preferences.AppliedSelection = null; Save();
            message.Text = "Your previous appearance has been restored.";
            return;
        }
        if (preferences.IsRegular)
        {
            message.Text = "Applying standard Helldivers appearance in HellForge…";
            await client.PrepareRegularAsync(WorkToken);
            preferences.AppliedSelection = preferences.SelectionKey; Save();
            message.Text = "Regular HellForge is ready to launch.";
            return;
        }
        var selection = preferences.Selection();
        Cancellable = true;
        var report = new Progress<string>(text => { if (!IsDisposed) message.Text = text; });
        var folder = await FindPreparedAsync(client, selection, report) ?? await ExportAsync(client, selection, report);
        preferences.PreparedPack = folder; Save();
        if (Game.IsRunning() || (await client.StatusAsync(WorkToken)).Running)
        {
            message.Text = "Your pack is prepared for installation after the game closes.";
            return;
        }
        message.Text = "Importing and verifying the pack in HellForge…";
        Cancellable = false;
        WorkToken.ThrowIfCancellationRequested();
        await Task.Run(() => client.InstallPackAsync(folder, selection, WorkToken), WorkToken);
        preferences.AppliedSelection = preferences.SelectionKey; Save();
        message.Text = "Your Star Wars pack is installed.";
    }
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
            message.Text = "Finish signing in to Steam, then launch your Forge session here.";
            return;
        }
        message.Text = "Checking the selected mode and asking HellForge to start…";
        var client = Client();
        var profile = await client.EnsureAccountAsync(WorkToken);
        if (preferences.IsRegular) await Task.Run(() => client.PlayRegularAsync(WorkToken, profile), WorkToken);
        else await Task.Run(() => client.PlayAsync(preferences.Selection(), WorkToken, profile), WorkToken);
        message.Text = "HellForge accepted the launch and session status will update here.";
    });
    async Task EndSessionAsync() => await OperationAsync(async () => {
        state.Text = "Ending session…";
        launch.Text = "ENDING SESSION…";
        message.Text = "Asking HellForge to close the game and finish cleanup…";
        await Client().EndSessionAsync(WorkToken);
        lastStatus = await Client().StatusAsync(WorkToken);
        message.Text = "Your session ended and HellForge cleanup is complete.";
    });
    async Task SyncAccountAsync() => await OperationAsync(async () => {
        var client = Client();
        await SyncAccountCoreAsync(client);
    });
    async Task SyncAccountCoreAsync(HellForgeClient client)
    {
        syncRequested = false;
        await client.SelectAccountAsync(WorkToken);
        lastAccount = await new HellForgeAccount(client.Root).ReadAsync(WorkToken);
        if (lastAccount is { Captured: true, Imported: false })
        {
            message.Text = "Importing your HD2 account with a local backup…";
            var opened = await client.ImportAccountAsync(WorkToken);
            lastAccount = await new HellForgeAccount(client.Root).ReadAsync(WorkToken);
            message.Text = opened > 0 ? $"Account imported with {opened} owned Warbonds opened." : "Your HD2 account is imported for all three modes.";
        }
        else
        {
            Process.Start(new ProcessStartInfo(Path.Combine(client.Root, "HellForge.exe")) { UseShellExecute = true, WorkingDirectory = client.Root });
            syncRequested = true;
            message.Text = "Complete Sync Inventory in HellForge, then return here to import your account.";
            MessageBox.Show(this, SyncInstructions, "Sync HD2 account", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    async void Locate()
    {
        if (preview || working) return;
        using var picker = new FolderBrowserDialog { Description = "Choose the HellForge folder containing HellForge.exe", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try {
            preferences.InstallRoot = HellForgeCommands.ValidateRoot(picker.SelectedPath); syncRequested = false; Save();
            lastStatus = null; lastMods = null; lastAccount = null; captureBlock = null; lastError = null; UpdateView();
            if (refreshCompletion != null) await refreshCompletion.Task;
            if (!IsDisposed) await RefreshAsync();
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Locate HellForge", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    async Task SetupAsync() => await OperationAsync(async () => {
        using var setup = new HellForgeSetupForm(preferences.InstallRoot);
        setup.Connected += root => { preferences.InstallRoot = HellForgeCommands.ValidateRoot(root); syncRequested = false; Save(); };
        var completed = setup.ShowDialog(this) == DialogResult.OK;
        if (setup.InstallationRoot is { } root)
        {
            preferences.InstallRoot = HellForgeCommands.ValidateRoot(root); syncRequested = false; Save();
            lastStatus = null; lastMods = null; lastAccount = null; captureBlock = null;
            message.Text = "Forge is connected and ready for the gameplay mods.";
            if (completed) await PrepareSelectedCoreAsync();
        }
    });
    async void OpenHellForge()
    {
        if (preview || working) return;
        try
        {
            if (preferences.InstallRoot == null || lastStatus is { GameCopyReady: false } or { RunnerReady: false })
            {
                await SetupAsync();
                return;
            }
            var client = Client();
            Process.Start(new ProcessStartInfo(Path.Combine(client.Root, "HellForge.exe")) { UseShellExecute = true, WorkingDirectory = client.Root });
        }
        catch (Exception e) { failureDetail = e.ToString(); message.Text = FailureSummary(e.ToString()); UpdateView(); }
    }
    void ReviewModNotices()
    {
        var notices = string.Join("\n", lastMods?.Problems ?? []);
        if (MessageBox.Show(this, notices + "\n\nThese files need your choice before setup can continue. In HellForge's Mods page, choose which duplicate or custom mod to keep, then return and Refresh.\n\nOpen HellForge to review them?",
            "Review mod choices", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) OpenHellForge();
    }
    void OpenSteamLibrary()
    {
        if (preview || working) return;
        try { Process.Start(new ProcessStartInfo("steam://nav/games/details/553850") { UseShellExecute = true }); }
        catch (Exception e) { failureDetail = e.ToString(); message.Text = FailureSummary(e.ToString()); UpdateView(); }
    }
    async Task ShowDetailsAsync()
    {
        if (preview || working) return;
        var launcherDiagnostic = LauncherDiagnostic; var kept = failureDetail; // OperationAsync clears both before its action.
        await OperationAsync(async () => {
            failureDetail = kept; // viewing details is not a new attempt; keep the failed operation's text for the next view
            var text = "Installation: " + (preferences.InstallRoot ?? "Not selected") + "\r\nLauncher diagnostic: " + (launcherDiagnostic ?? "none") +
                "\r\nAccount review: " + (lastAccount?.Message ?? "none") + "\r\nAccount sync cleanup: " + (captureBlock ?? "none") + "\r\n\r\n";
            if (preferences.InstallRoot != null)
            {
                try
                {
                    text += "Forge upgrade recovery: " + (HellForgeSetup.UpgradeProblem(preferences.InstallRoot) ?? "none") + "\r\n";
                    var client = Client(); var status = await client.StatusAsync(WorkToken); var mods = await client.ModsAsync(WorkToken);
                    text += $"Compatibility: {status.CompatibilityProblem ?? "none"}\r\nRecovery: {status.RecoveryProblem ?? "none"}\r\nMod choices: {string.Join("\r\n", mods.Problems)}\r\nMod overlap: {HellForgeClient.ModConflict(mods) ?? "none"}\r\nMod sharing: {mods.HostingProblem ?? "none"}\r\nPreset: {HellForgePreset.Problem(mods, checkOrder: true) ?? "ready"}\r\n";
                    text += $"HellForge: {status.Version}\r\nGame build: {status.Build}\r\nGame copy ready: {status.GameCopyReady}\r\nSession running: {status.Running}\r\nLast session: {status.LastStatus ?? "none"}\r\nLast failure: {status.LastFailure ?? "none"}\r\nLast notice: {status.LastNotice ?? "none"}\r\nSquad modlist: {mods.Code ?? "no gameplay mods enabled"}\r\n\r\n" +
                        string.Join("\r\n", mods.Mods.Select(m => $"{(m.Enabled ? "ON" : "OFF")}  {m.Name}  ({m.Id})"));
                }
                catch (Exception error) when (error is not OperationCanceledException) { text += "Status read failed\r\n" + error; }
            }
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
        if (disposing && !resourcesDisposed) { resourcesDisposed = true; timer.Dispose(); moreMenu.Dispose(); lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}

sealed class ForgeProgressBar : ProgressBar
{
    readonly System.Windows.Forms.Timer animation = new() { Interval = 50 };
    public new int Value { get => base.Value; set { base.Value = value; Invalidate(); } }
    public ForgeProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        animation.Tick += (_, _) => { if (Style == ProgressBarStyle.Marquee) Invalidate(); };
    }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) animation.Start(); else animation.Stop();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Slate);
        using var fill = new SolidBrush(Orange);
        if (Style == ProgressBarStyle.Marquee)
        {
            var width = Math.Max(1, Width / 4);
            var x = (int)(Environment.TickCount64 % 1800 * (Width + width) / 1800) - width;
            e.Graphics.FillRectangle(fill, x, 0, width, Height);
        }
        else e.Graphics.FillRectangle(fill, 0, 0, (int)(Width * (double)Value / Maximum), Height);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
