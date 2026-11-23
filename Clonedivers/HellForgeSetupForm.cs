using System.Diagnostics;
using System.Text.Json;
using static Clonedivers.Palette;

namespace Clonedivers;

internal sealed class HellForgeSetupForm : Form
{
    enum WizardStep { Preflight = 1, ForgeAndGame = 2, AccountSync = 3, PackAndReady = 4 }

    readonly bool preview;
    readonly HellForgePreferences preferences;
    readonly Action? savePreferences;
    readonly Label[] stepPills = new Label[4];
    readonly Label stepTitle = new() { AutoSize = true, UseMnemonic = false, ForeColor = TextMain, Font = new Font("Segoe UI", 13, FontStyle.Bold), AccessibleName = "Wizard step" };
    readonly Label intro = Row(), steam = Row(), space = Row(), installed = Row(), game = Row(), syncGuide = Row(), packSummary = Row(), detail = Row();
    readonly TextBox folder = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Forge install folder" };
    readonly TableLayoutPanel pathRow;
    readonly Label storage = Row();
    readonly RoundButton next = new() { Height = 48, Dock = DockStyle.Fill, AccessibleName = "Continue Forge setup" };
    readonly Button change = new() { Text = "Change folder…", AutoSize = true }, locate = new() { Text = "Use existing Forge…", AutoSize = true }, cancel = new() { Text = "Close", AutoSize = true };
    readonly ForgeProgressBar progress = new() { Dock = DockStyle.Fill, Height = 10, Visible = false };
    readonly PictureBox approval = new() { SizeMode = PictureBoxSizeMode.CenterImage, Width = 330, Height = 330, Visible = false, AccessibleName = "Steam Guard sign-in QR code" };
    readonly Button official = new() { Text = "Official setup (fallback)", AutoSize = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1500 };
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? workCts;
    HellForgeStatus? status;
    HellForgeAccountState? accountState;
    string? captureBlock;
    bool captureBlockUnreadable;
    HellForgePack.Plan? pendingPackPlan;
    string? step4Notice;
    WizardStep step = WizardStep.Preflight;
    bool busy, polling, managerOpened, gameReady, packReady, disposed, canPause, step4Checked;
    TaskCompletionSource? pollCompletion;
    Process? manager;
    internal string? InstallationRoot { get; private set; }
    internal Func<HellForgePack.Selection>? SelectionOverride { get; init; }
    internal Func<string, IReadOnlyList<GameplayArtifact>, IProgress<string>?, CancellationToken, IProgress<(long Done, long Total)>?, Task>? StageGameplayOverride { get; init; }
    internal Func<HellForgePack.Plan, bool, IProgress<string>?, CancellationToken, IProgress<(long Done, long Total)>?, Task>? ExportPackOverride { get; init; }
    internal event Action<string>? Connected;

    internal HellForgeSetupForm(string? root = null, bool preview = false, HellForgePreferences? preferences = null, Action? savePreferences = null)
    {
        this.preview = preview;
        this.preferences = preferences ?? new HellForgePreferences();
        this.savePreferences = savePreferences;
        Text = "Clonedivers — Setup Wizard"; BackColor = Bg; ForeColor = TextMain;
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(740, 670); MinimumSize = new Size(620, 480);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(26, 20, 26, 10) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(new Label { Text = "LET’S GET YOU READY", Font = new Font("Segoe UI", 20, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 8) });

        var stepper = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        string[] stepNames = ["1. Preflight Check", "2. Forge & Game Copy", "3. Steam Account Sync", "4. Pack & Ready"];
        for (int i = 0; i < 4; i++)
        {
            stepper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            var pill = new Label
            {
                Text = stepNames[i],
                UseMnemonic = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Height = 30,
                Margin = new Padding(i == 0 ? 0 : 4, 0, i == 3 ? 0 : 4, 0),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                BackColor = Disabled,
                ForeColor = TextDim
            };
            stepPills[i] = pill;
            stepper.Controls.Add(pill, i, 0);
        }
        top.Controls.Add(stepper);
        stepTitle.Margin = new Padding(0, 4, 0, 2);
        top.Controls.Add(stepTitle);
        Controls.Add(top);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 4, 26, 12), ColumnCount = 1, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        layout.BringToFront();

        intro.Text = HellForgeForm.SetupRequirements + "\r\n\r\n" +
            "Checklist:\r\n" +
            "• Keep Helldivers 2 installed and updated in Steam (used for game copy & mandatory account sync).\r\n" +
            "• Allow ~70 GB free space on the Forge drive (game copy + Star Wars pack + cache).\r\n" +
            "• Use the Steam Mobile App QR scanner, or click Official setup (fallback) for SteamCMD.\r\n" +
            "• Expect 2–3 Windows administrator (UAC) prompts plus Windows network approval on first launch.";
        intro.Margin = new Padding(0, 4, 0, 10);
        layout.Controls.Add(intro);

        foreach (var label in new[] { steam, space, installed, game })
        {
            label.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            label.Margin = new Padding(0, 4, 0, 4);
            layout.Controls.Add(label);
        }

        pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 10, 0, 4) };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(folder); pathRow.Controls.Add(change);
        folder.BackColor = Disabled; folder.ForeColor = TextMain; change.Margin = new Padding(8, 0, 0, 0);
        layout.Controls.Add(pathRow);

        storage.Text = "New installs save the 502 MB installer beside Forge. Choose a folder outside Steam and OneDrive.";
        storage.ForeColor = TextMute;
        storage.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(storage);

        syncGuide.Text = "Steam Account Sync is required before first launch so your real HD2 inventory & progression carry into Forge (Forge progress stays local).\r\n\r\n" +
            "1. Click OPEN HELLFORGE TO SYNC below (approve the Windows UAC prompt for the sync proxy).\r\n" +
            "2. In HellForge choose Profile > Account sync > Sync Inventory (default is selected).\r\n" +
            "3. Pass HD2's title screen, wait for HD2 to close, then close HellForge.\r\n" +
            "4. Leave Apply import and auto-apply off — Clonedivers detects and imports your sealed capture automatically.";
        syncGuide.Margin = new Padding(0, 6, 0, 10);
        layout.Controls.Add(syncGuide);

        packSummary.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        packSummary.Margin = new Padding(0, 6, 0, 8);
        layout.Controls.Add(packSummary);

        detail.Margin = new Padding(0, 8, 0, 10);
        layout.Controls.Add(detail);
        layout.Controls.Add(progress);
        layout.Controls.Add(approval);

        next.BackColor = Orange; next.ForeColor = Bg; next.FlatStyle = FlatStyle.Flat; next.FlatAppearance.BorderSize = 0; next.FlatAppearance.MouseOverBackColor = OrangeHot;
        next.Font = new Font("Segoe UI", 11, FontStyle.Bold); next.Margin = new Padding(0, 8, 0, 8);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, Padding = new Padding(26, 4, 26, 16) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.Controls.Add(next);
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Controls = { locate, official, cancel } };
        footer.Controls.Add(bottom);
        Controls.Add(footer);

        layout.SizeChanged += (_, _) =>
        {
            var max = new Size(Math.Max(160, layout.ClientSize.Width - layout.Padding.Horizontal - 20), 0);
            foreach (var label in new[] { intro, steam, space, installed, game, storage, syncGuide, packSummary, detail })
                label.MaximumSize = max;
        };

        folder.Text = root ?? HellForgeSetup.SeparateDefaultRoot();
        if (root != null && !preview) InstallationRoot = HellForgeCommands.ValidateRoot(root);

        next.Click += async (_, _) => await ContinueAsync();
        official.Click += async (_, _) =>
        {
            if (!busy && !polling)
            {
                try { await OpenManagerAsync(); }
                catch (Exception e) { detail.Text = e.Message; }
                UpdateView();
            }
        };
        cancel.Click += (_, _) =>
        {
            if (busy && canPause) workCts?.Cancel();
            else if (!busy) Close();
        };
        change.Click += (_, _) => ChooseFolder(false);
        locate.Click += async (_, _) => { ChooseFolder(true); await PollAsync(); };
        timer.Tick += async (_, _) => await PollAsync();
        Shown += async (_, _) =>
        {
            if (!preview)
            {
                await PollAsync();
                if (!IsDisposed) timer.Start();
            }
        };
        FormClosing += (_, e) =>
        {
            if (busy || polling)
            {
                e.Cancel = true;
                detail.Text = busy ? "Wait for the current step to finish, or pause/cancel the download first." : "Checking setup… Please try closing again in a moment.";
            }
        };
        UpdateView();
    }

    static Label Row() => new() { AutoSize = true, UseMnemonic = false, ForeColor = TextDim, MaximumSize = new Size(650, 0), Margin = new Padding(0, 0, 0, 8) };
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dwm.ApplyDark(Handle, Bg, TextMain, Border); }

    static HellForgeClient CreateClient(string root) => new(HellForgeCommands.ValidateRoot(root), HellForgeForm.CreateCommands(root));
    static HellForgeAccount CreateAccount(string root) => HellForgeForm.CreateAccount(root);

    void SavePrefs()
    {
        if (!preview) savePreferences?.Invoke();
    }

    void ChooseFolder(bool existing)
    {
        if (busy || preview) return;
        using var picker = new FolderBrowserDialog { Description = existing ? "Choose your existing HellForge folder" : "Choose a drive or parent folder for HellForge", UseDescriptionForTitle = true, ShowNewFolderButton = !existing };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var selected = existing ? HellForgeCommands.ValidateRoot(picker.SelectedPath) : HellForgeSetup.ValidateTarget(Path.Combine(picker.SelectedPath, "HellForge"));
            folder.Text = selected;
            InstallationRoot = existing ? selected : null;
            status = null; accountState = null; gameReady = packReady = managerOpened = step4Checked = false;
            pendingPackPlan = null;
            step = WizardStep.Preflight;
            if (InstallationRoot != null) Connected?.Invoke(InstallationRoot);
            detail.Text = "";
            UpdateView();
        }
        catch (Exception e) { detail.Text = e.Message; }
    }

    void UpdateStepper()
    {
        for (int i = 0; i < 4; i++)
        {
            var idx = (int)step;
            bool done = (i + 1) < idx || (i == 3 && packReady);
            bool active = (i + 1) == idx && !done;
            stepPills[i].BackColor = done ? Color.FromArgb(24, 68, 45) : active ? Color.FromArgb(24, 47, 82) : Disabled;
            stepPills[i].ForeColor = done ? Color.FromArgb(134, 239, 172) : active ? TextMain : TextDim;
        }
        stepTitle.Text = step switch
        {
            WizardStep.Preflight => "Step 1 of 4 · Preflight Check",
            WizardStep.ForgeAndGame => "Step 2 of 4 · Forge & Game Copy",
            WizardStep.AccountSync => "Step 3 of 4 · Guided Steam Account Sync (Required)",
            _ => packReady ? "Step 4 of 4 · Ready to Launch" : "Step 4 of 4 · Pack Download & Ready"
        };
        stepTitle.AccessibleName = stepTitle.Text;
    }

    string DescribeSpace()
    {
        var free = HellForgeSetup.AvailableDriveBytes(folder.Text);
        if (free is not { } bytes) return "Disk space — checked when setup starts (70 GB free required)";
        var drive = Path.GetPathRoot(Path.GetFullPath(folder.Text)) ?? "drive";
        return bytes >= HellForgeSetup.SetupBudget
            ? $"Disk space — {drive} has {Pack.FormatBytes(bytes)} free (70 GB required) · OK"
            : $"Disk space — {drive} has {Pack.FormatBytes(bytes)} free (needs 70 GB free; low space)";
    }

    string DescribeModeSelection()
    {
        if (preferences.IsRegular) return "Selected mode: Regular Helldivers 2 + required gameplay preset";
        var universe = preferences.Mode == "empire" ? "EmpireDivers" : preferences.DeltaSquad ? "Clonedivers (Delta Squad)" : "Clonedivers";
        var enemy = preferences.EnemySkin switch { "mixed-droids" => "Mixed Bots + CIS Droids", "native-automatons" => "Native Automatons", _ => "All CIS Droids" };
        var tex = preferences.Texture switch { "reduced512" => "Johnson Ultra-Potato (512px)", "lighter" => "Lighter (Streamed)", _ => "Full" };
        var cov = preferences.Covenant ? " · Covenant ON" : "";
        return $"Selected mode: {universe} · Enemies: {enemy}{cov} · Textures: {tex}";
    }

    void UpdateView()
    {
        UpdateStepper();
        var steamReady = preview || HellForgeForm.SteamReadyCheck();
        steam.Text = "Steam — " + (steamReady ? "signed in" : "open Steam and sign in");
        space.Text = DescribeSpace();
        installed.Text = "Forge — " + (status != null && HellForgeSetup.IsPrevious(status.Version) ? "update available (0.3 -> 0.4)" : InstallationRoot != null ? "installed (0.4.0-beta)" : "ready to install");
        game.Text = "Game copy — " + (gameReady ? "ready (build 25480438)" : managerOpened ? "finish in the Forge window" : "ready to copy or download");

        bool onStep1 = step == WizardStep.Preflight;
        bool onStep2 = step == WizardStep.ForgeAndGame;
        bool onStep3 = step == WizardStep.AccountSync;
        bool onStep4 = step == WizardStep.PackAndReady;

        intro.Visible = onStep1;
        steam.Visible = onStep1;
        space.Visible = onStep1;
        pathRow.Visible = onStep1;
        storage.Visible = onStep1;
        installed.Visible = onStep1 || onStep2;
        game.Visible = onStep1 || onStep2;
        syncGuide.Visible = onStep3;
        packSummary.Visible = onStep4;
        packSummary.Text = DescribeModeSelection();

        change.Enabled = locate.Enabled = onStep1 && !busy && !polling && !managerOpened;
        locate.Visible = onStep1;
        official.Visible = onStep1 || onStep2;
        official.Enabled = !busy && !polling && InstallationRoot != null && status?.Running != true;
        cancel.Enabled = !busy || canPause;
        cancel.Text = busy ? canPause ? "Pause download" : "Working…" : "Close";
        cancel.AccessibleName = cancel.Text;

        if (status?.Running == true || (!preview && Game.IsRunning()))
        {
            next.Enabled = false;
            next.Text = "CLOSE RUNNING GAME FIRST";
            next.AccessibleName = next.Text;
            detail.Text = "A game session is running. End it before continuing setup.";
            return;
        }

        if (onStep1)
        {
            next.Enabled = !preview && !busy && !polling;
            next.Text = !steamReady ? "OPEN STEAM" :
                InstallationRoot == null ? "INSTALL FORGE" :
                status != null && HellForgeSetup.IsPrevious(status.Version) ? "UPDATE HELLFORGE" :
                gameReady ? "CONTINUE TO STEP 3" : "CONTINUE GAME SETUP";
            if (!busy && string.IsNullOrWhiteSpace(detail.Text))
                detail.Text = "Review the checklist above, pick your Forge drive/folder, and click Continue.";
        }
        else if (onStep2)
        {
            next.Enabled = !preview && !busy && !polling;
            next.Text = gameReady ? "CONTINUE TO ACCOUNT SYNC" : "RETRY GAME SETUP";
        }
        else if (onStep3)
        {
            next.Enabled = !preview && !busy;
            if (captureBlock != null && !captureBlockUnreadable)
            {
                bool cleanup = captureBlock.Contains("cleanup", StringComparison.OrdinalIgnoreCase);
                next.Text = cleanup ? "OPEN HELLFORGE TO CLEAN UP" : "OPEN HELLFORGE";
                detail.Text = cleanup
                    ? "An interrupted sync left a hosts redirect behind. Click OPEN HELLFORGE TO CLEAN UP, then in HellForge choose Profile > Account sync > Clean up."
                    : "Account sync capture is running. Pass HD2's title screen, wait for HD2 to exit, then close HellForge.";
            }
            else if (captureBlockUnreadable)
            {
                next.Text = "CHECK SYNC STATUS";
                detail.Text = "Clonedivers could not read the hosts file to check capture cleanup. Review folder permissions or close interfering security tools, then click Check.";
            }
            else if (accountState is { Captured: true, Imported: false })
            {
                next.Text = "APPLY ACCOUNT IMPORT";
                detail.Text = "Sealed HD2 capture found! Click APPLY ACCOUNT IMPORT to import it into Forge.";
            }
            else
            {
                next.Text = "OPEN HELLFORGE TO SYNC";
                if (!busy && string.IsNullOrWhiteSpace(detail.Text))
                    detail.Text = "Waiting for your sealed HD2 capture. Account sync is required before first launch.";
            }
        }
        else
        {
            next.Enabled = !preview && !busy;
            if (packReady)
            {
                next.Text = "FINISH & RETURN TO LAUNCHER";
                detail.Text = "Setup complete! Windows may ask for network (firewall) approval on your first launch.";
            }
            else if (pendingPackPlan is { Missing.Count: > 0 } plan)
            {
                next.Text = $"DOWNLOAD PACK ({Pack.FormatBytes(plan.Missing.Sum(f => f.Size))})";
                if (!busy) detail.Text = step4Notice ?? $"Ready to download {Pack.FormatBytes(plan.Missing.Sum(f => f.Size))} of verified Star Wars assets ({plan.Local.Count} local files reused).";
            }
            else
            {
                next.Text = "INSTALL PACK & FINISH";
                if (!busy)
                {
                    if (step4Notice != null) detail.Text = step4Notice;
                    else if (string.IsNullOrWhiteSpace(detail.Text)) detail.Text = "Preparing gameplay mods and your selected pack…";
                }
            }
        }
        if (!string.IsNullOrEmpty(next.Text)) next.AccessibleName = next.Text;
    }

    internal async Task ContinueAsync()
    {
        if (preview || busy) return;
        if (pollCompletion is { } pendingPoll)
        {
            busy = true;
            UpdateView();
            try { await pendingPoll.Task; }
            finally { busy = false; }
            if (IsDisposed) return;
        }
        if (Game.IsRunning() || status?.Running == true) { UpdateView(); return; }

        if (step == WizardStep.Preflight)
        {
            if (!HellForgeForm.SteamReadyCheck())
            {
                try { HellForgeForm.OpenSteamClient(); detail.Text = "Finish signing in to the Steam account that owns HD2, then click Continue here."; }
                catch (Exception e) { detail.Text = e.Message; }
                return;
            }
            if (InstallationRoot == null)
            {
                var free = HellForgeSetup.AvailableDriveBytes(folder.Text);
                if (free is long bytes && bytes < HellForgeSetup.SetupBudget)
                {
                    detail.Text = $"Drive {Path.GetPathRoot(Path.GetFullPath(folder.Text))} has {Pack.FormatBytes(bytes)} free. Allow at least 70 GB for Forge setup and appearance packs, or choose another drive.";
                    return;
                }
            }
            step = WizardStep.ForgeAndGame;
            detail.Text = "";
            if (gameReady)
            {
                step = WizardStep.AccountSync;
                await AdvanceStep3Or4Async();
                return;
            }
            await RunStep2ForgeAndGameAsync();
            return;
        }

        if (step == WizardStep.ForgeAndGame)
        {
            if (gameReady)
            {
                step = WizardStep.AccountSync;
                await AdvanceStep3Or4Async();
            }
            else await RunStep2ForgeAndGameAsync();
            return;
        }

        if (step == WizardStep.AccountSync)
        {
            await RunStep3ActionAsync();
            return;
        }

        if (step == WizardStep.PackAndReady)
        {
            if (packReady)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            await RunStep4PackAsync(allowDownload: pendingPackPlan is { Missing.Count: > 0 });
        }
    }

    async Task RunStep2ForgeAndGameAsync()
    {
        busy = true;
        Power.KeepAwake(true);
        UpdateView();
        try
        {
            workCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var updates = new Progress<ForgeSetupProgress>(p =>
            {
                if (IsDisposed) return;
                SetCanPause(p.CanCancel);
                progress.Visible = true;
                progress.Style = p.Total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (p.Total > 0) progress.Value = (int)Math.Clamp(p.Done * 100 / p.Total, 0, 100);
                if (p.QrRows != null)
                {
                    CapQrWindowHeight();
                    var pixels = Math.Min(330, Math.Max(120, approval.Parent!.ClientSize.Height - 60));
                    SetApproval(SteamSetupOutput.RenderQr(p.QrRows, pixels));
                }
                else if (p.Message == "Signed in.") SetApproval(null);
                detail.Text = p.Message + (p.Total > 100 ? $" {Pack.FormatBytes(p.Done)} / {Pack.FormatBytes(p.Total)}" : "");
                UpdateView();
            });
            if (InstallationRoot == null)
            {
                SetCanPause(true);
                InstallationRoot = await HellForgeSetup.InstallAsync(folder.Text, updates, workCts.Token);
                preferences.InstallRoot = InstallationRoot;
                SavePrefs();
                Connected?.Invoke(InstallationRoot);
            }
            else if (status != null && HellForgeSetup.IsPrevious(status.Version))
            {
                SetCanPause(true);
                await HellForgeSetup.UpdateAsync(InstallationRoot, updates, workCts.Token);
            }
            if (ManagerRunning(InstallationRoot!))
                throw new InvalidOperationException("Close the HellForge window before downloading here, or finish with Official setup.");
            await HellForgeSetup.PrepareGameAsync(InstallationRoot!, updates, workCts.Token, client: CreateClient(InstallationRoot!));
            status = await CreateClient(InstallationRoot!).StatusAsync(workCts.Token);
            gameReady = status.GameCopyReady && status.RunnerReady && !status.Running && status.CompatibilityProblem == null;
            if (gameReady)
            {
                step = WizardStep.AccountSync;
                detail.Text = "";
                SetApproval(null);
                progress.Visible = false;
                busy = false;
                await AdvanceStep3Or4Async();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            detail.Text = "Download paused. Continue to resume the saved files. Steam may ask for a fresh QR approval.";
        }
        catch (Exception e) { detail.Text = e.Message; }
        finally
        {
            Power.KeepAwake(false);
            workCts?.Dispose(); workCts = null; canPause = false; busy = false;
            SetApproval(null); progress.Visible = false;
            if (!IsDisposed) UpdateView();
        }
    }

    void CapQrWindowHeight()
    {
        var work = Screen.FromControl(this).WorkingArea;
        var chrome = Math.Max(0, Height - ClientSize.Height);
        var maxClientHeight = Math.Max(MinimumSize.Height, work.Height - chrome - 16);
        var targetHeight = Math.Min(Math.Max(ClientSize.Height, 830), maxClientHeight);
        if (ClientSize.Height < targetHeight) ClientSize = new Size(ClientSize.Width, targetHeight);
        if (Bottom > work.Bottom) Top = Math.Max(work.Top, work.Bottom - Height);
    }

    async Task AdvanceStep3Or4Async()
    {
        if (InstallationRoot == null || IsDisposed) return;
        await RefreshAccountAndCaptureAsync(lifetime.Token);
        if (accountState?.Imported == true && captureBlock == null)
        {
            step = WizardStep.PackAndReady;
            UpdateView();
            await RunStep4PackAsync(allowDownload: false);
            return;
        }
        UpdateView();
    }

    async Task RefreshAccountAndCaptureAsync(CancellationToken ct)
    {
        try
        {
            captureBlock = HellForgeForm.CaptureBlockCheck();
            captureBlockUnreadable = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            captureBlock = e.ToString();
            captureBlockUnreadable = true;
        }
        if (captureBlock != null || InstallationRoot == null) return;
        try
        {
            accountState = await CreateAccount(InstallationRoot).ReadAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            accountState = new(false, false, e.Message);
        }
    }

    async Task RunStep3ActionAsync()
    {
        if (InstallationRoot == null) return;
        busy = true;
        UpdateView();
        try
        {
            await RefreshAccountAndCaptureAsync(lifetime.Token);
            if (captureBlock != null)
            {
                if (!captureBlockUnreadable) HellForgeForm.OpenManager(InstallationRoot);
                return;
            }
            if (accountState?.Imported == true)
            {
                step = WizardStep.PackAndReady;
                busy = false;
                await RunStep4PackAsync(allowDownload: false);
                return;
            }
            if (accountState is { Captured: true, Imported: false })
            {
                await ImportDetectedCaptureAsync(lifetime.Token);
                return;
            }
            var client = CreateClient(InstallationRoot);
            await HellForgeForm.SelectAccountAction(client, lifetime.Token);
            HellForgeForm.OpenManager(InstallationRoot);
            detail.Text = "HellForge opened. Choose Profile > Account sync > Sync Inventory (default), pass HD2's title screen, then close HD2 and HellForge.";
        }
        catch (Exception e) { detail.Text = e.Message; }
        finally
        {
            busy = false;
            if (!IsDisposed) UpdateView();
        }
    }

    async Task ImportDetectedCaptureAsync(CancellationToken ct)
    {
        var client = CreateClient(InstallationRoot!);
        detail.Text = "Sealed HD2 capture detected! Importing your account into Forge…";
        progress.Visible = true;
        progress.Style = ProgressBarStyle.Marquee;
        await HellForgeForm.SelectAccountAction(client, ct);
        await HellForgeForm.ImportAccountAction(client, ct);
        accountState = await CreateAccount(InstallationRoot!).ReadAsync(ct);
        if (accountState.Imported)
        {
            await HellForgeForm.TryOpenWarbondsAction(client, ct);
            step = WizardStep.PackAndReady;
            detail.Text = "";
            progress.Visible = false;
            busy = false;
            await RunStep4PackAsync(allowDownload: false);
        }
    }

    void SetCanPause(bool value)
    {
        canPause = value;
        if (IsDisposed) return;
        cancel.Enabled = !busy || canPause;
        cancel.Text = busy ? canPause ? "Pause download" : "Working…" : "Close";
        cancel.AccessibleName = cancel.Text;
    }

    internal async Task RunStep4PackAsync(bool allowDownload)
    {
        if (InstallationRoot == null || busy || IsDisposed) return;
        step4Notice = null;
        busy = true;
        Power.KeepAwake(true);
        UpdateView();
        try
        {
            workCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var ct = workCts.Token;
            var client = CreateClient(InstallationRoot);

            if (client.InterruptedGameplayProblem() != null)
            {
                detail.Text = "Restoring interrupted gameplay setup…";
                await client.RecoverGameplayAsync(ct);
            }
            if ((await client.StatusAsync(ct)).RecoveryProblem != null)
            {
                detail.Text = "Restoring interrupted pack setup…";
                await Task.Run(() => client.RecoverInterruptedPackAsync(ct), ct);
                preferences.AppliedSelection = null; SavePrefs();
            }

            // Stage and install required gameplay preset
            var activeMode = preferences.IsRegular ? "regular" : preferences.PackMode;
            detail.Text = "Checking required gameplay mods…";
            var missingGameplay = await client.MissingGameplayAsync(activeMode, ct);
            if (missingGameplay.Count > 0)
            {
                SetCanPause(true);
                progress.Visible = true;
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = 0;
                var gpText = new Progress<string>(msg => { if (!IsDisposed) detail.Text = msg; });
                var gpBytes = new Progress<(long done, long total)>(p =>
                {
                    if (IsDisposed || !busy) return;
                    progress.Value = p.total > 0 ? (int)Math.Clamp(p.done * 100d / p.total, 0, 100) : 0;
                    detail.Text = $"Downloading gameplay mods · {Pack.FormatBytes(p.done)} / {Pack.FormatBytes(p.total)} · {progress.Value}%";
                });
                if (StageGameplayOverride != null)
                    await StageGameplayOverride(client.GameplayCache, missingGameplay, gpText, ct, gpBytes);
                else
                    await HellForgeGameplayCatalog.StageAsync(client.GameplayCache, missingGameplay, gpText, ct, gpBytes);
            }
            SetCanPause(false);
            progress.Visible = true;
            progress.Style = ProgressBarStyle.Marquee;
            var installReport = new Progress<string>(msg => { if (!IsDisposed) detail.Text = msg; });
            await client.InstallGameplayAsync(client.GameplayCache, installReport, ct, activeMode);

            if (preferences.IsRegular)
            {
                detail.Text = "Applying standard Helldivers appearance in HellForge…";
                await client.PrepareRegularAsync(ct);
                preferences.AppliedSelection = preferences.SelectionKey;
                SavePrefs();
                packReady = true;
                step4Checked = true;
                return;
            }

            var consentedBytes = allowDownload && pendingPackPlan != null ? pendingPackPlan.Missing.Sum(f => f.Size) : 0L;
            var selection = SelectionOverride != null ? SelectionOverride() : preferences.Selection();
            if (pendingPackPlan == null || !SameSelection(pendingPackPlan.Selection, selection) ||
                Directory.Exists(pendingPackPlan.Output) || File.Exists(pendingPackPlan.Output))
            {
                var reused = await FindPreparedAsync(client, selection, installReport, ct);
                if (reused != null)
                {
                    pendingPackPlan = null;
                    await InstallPreparedPackAsync(client, reused, selection, ct);
                    return;
                }
                var sources = CollectLocalSources(client);
                var output = Path.Combine(client.CacheRoot, "exports", preferences.PackMode + "-" + Guid.NewGuid().ToString("N"));
                pendingPackPlan = await Task.Run(() => HellForgePack.PlanAsync(selection, output, sources, installReport, ct), ct);
                var newMissing = pendingPackPlan.Missing.Sum(f => f.Size);
                if (allowDownload && newMissing > consentedBytes)
                {
                    step4Checked = true;
                    step4Notice = detail.Text = $"Download size updated to {Pack.FormatBytes(newMissing)}. Click DOWNLOAD PACK to confirm.";
                    return;
                }
            }

            if (pendingPackPlan.Missing.Count > 0 && !allowDownload)
            {
                step4Checked = true;
                return;
            }

            SetCanPause(true);
            progress.Visible = true;
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = 0;
            var packBytes = new Progress<(long Done, long Total)>(p =>
            {
                if (IsDisposed || !busy) return;
                progress.Value = p.Total > 0 ? (int)Math.Clamp(p.Done * 100d / p.Total, 0, 100) : 0;
                detail.Text = $"Preparing Star Wars pack · {Pack.FormatBytes(p.Done)} / {Pack.FormatBytes(p.Total)} · {progress.Value}%";
            });
            var plan = pendingPackPlan;
            if (ExportPackOverride != null)
                await ExportPackOverride(plan, true, installReport, ct, packBytes);
            else
                await Task.Run(() => HellForgePack.ExportAsync(plan, true, installReport, ct, packBytes), ct);
            SetCanPause(false);
            await InstallPreparedPackAsync(client, plan.Output, selection, ct);
            pendingPackPlan = null;
        }
        catch (OperationCanceledException)
        {
            step4Checked = true;
            step4Notice = detail.Text = pendingPackPlan is { Missing.Count: > 0 }
                ? "Pack download paused. Click DOWNLOAD PACK to resume your verified files."
                : "Download paused. Click INSTALL PACK & FINISH to resume your verified files.";
        }
        catch (Exception e)
        {
            step4Checked = true;
            step4Notice = detail.Text = e.Message;
        }
        finally
        {
            Power.KeepAwake(false);
            workCts?.Dispose(); workCts = null; canPause = false; busy = false;
            progress.Visible = false;
            if (!IsDisposed) UpdateView();
        }
    }

    async Task InstallPreparedPackAsync(HellForgeClient client, string folder, HellForgePack.Selection selection, CancellationToken ct)
    {
        preferences.PreparedPack = folder;
        SavePrefs();
        progress.Visible = true;
        progress.Style = ProgressBarStyle.Marquee;
        detail.Text = "Importing and verifying your Star Wars pack in HellForge…";
        await Task.Run(() => client.InstallPackAsync(folder, selection, ct), ct);
        preferences.AppliedSelection = preferences.SelectionKey;
        SavePrefs();
        packReady = true;
        step4Checked = true;
    }

    async Task<string?> FindPreparedAsync(HellForgeClient client, HellForgePack.Selection selection, IProgress<string> report, CancellationToken ct)
    {
        var candidates = new List<string>();
        if (preferences.PreparedPack is { } saved && Directory.Exists(saved)) candidates.Add(saved);
        var exports = Path.Combine(client.CacheRoot, "exports");
        FileSafety.RejectLink(exports);
        if (Directory.Exists(exports))
            candidates.AddRange(new DirectoryInfo(exports).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            report.Report("Checking previously prepared pack files…");
            try { await Task.Run(() => HellForgePack.VerifyAsync(candidate, selection, ct), ct); return candidate; }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or KeyNotFoundException) { }
        }
        return null;
    }

    static List<string> CollectLocalSources(HellForgeClient client)
    {
        var sources = new List<string>();
        var gameDir = GameLocator.AutoDetect();
        if (gameDir != null)
            foreach (var name in new[] { "mods_download", "mods_old", "mods_off", "data" })
            {
                var path = Path.Combine(gameDir, name);
                if (Directory.Exists(path)) sources.Add(path);
            }
        var mods = Path.Combine(client.Root, "mods");
        if (Directory.Exists(mods)) sources.Add(mods);
        return sources;
    }

    static bool SameSelection(HellForgePack.Selection a, HellForgePack.Selection b) =>
        HellForgePack.SelectionIdentity(a) == HellForgePack.SelectionIdentity(b);

    void SetApproval(Image? image)
    {
        var previous = approval.Image; approval.Image = image; approval.Visible = image != null; previous?.Dispose();
        if (image != null)
        {
            approval.Size = image.Size;
            var workHeight = (Screen.FromControl(this)?.WorkingArea.Height ?? 900) - 40;
            var maxHeight = Math.Max(480, workHeight);
            if (Height > maxHeight) Height = maxHeight;
        }
        if (approval.Parent is ScrollableControl scroller)
        {
            scroller.AutoScrollMinSize = Size.Empty;
            if (image == null) return;
            scroller.PerformLayout();
            scroller.AutoScrollMinSize = new Size(0, approval.Bottom - scroller.AutoScrollPosition.Y + scroller.Padding.Bottom);
            scroller.ScrollControlIntoView(approval);
        }
    }

    async Task OpenManagerAsync()
    {
        var root = HellForgeCommands.ValidateRoot(InstallationRoot!);
        var current = await CreateClient(root).StatusAsync(lifetime.Token);
        if (current.Running || Game.IsRunning()) throw new InvalidOperationException("End the current game session before continuing setup.");
        if (!HellForgeSetup.SupportsVersion(current.Version) || current.Build != HellForgePack.SupportedBuild)
            throw new InvalidDataException("This installation is a different Forge version. Setup will not replace it. Choose a separate pinned installation.");
        if (current.GameCopyReady && current.RunnerReady && current.CompatibilityProblem == null)
        {
            gameReady = true;
            step = WizardStep.AccountSync;
            await AdvanceStep3Or4Async();
            return;
        }
        if (ManagerRunning(root))
        {
            managerOpened = true;
            detail.Text = "In the Forge window, click Start setup. Approve its Windows prompt when asked. We’ll detect completion here.";
            return;
        }
        manager?.Dispose();
        manager = Process.Start(new ProcessStartInfo(Path.Combine(root, "HellForge.exe")) { UseShellExecute = true, WorkingDirectory = root });
        managerOpened = true;
        detail.Text = "In the Forge window, click Start setup and approve its Windows prompt. If Steam needs the supported build, follow Forge’s download instructions. Keep this launcher open; it will detect completion.";
    }

    internal async Task PollAsync()
    {
        if (preview || busy || polling || InstallationRoot == null) { if (!busy) UpdateView(); return; }
        polling = true;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pollCompletion = completed;
        try
        {
            var client = CreateClient(InstallationRoot);
            status = await client.StatusAsync(lifetime.Token);
            gameReady = status.GameCopyReady && status.RunnerReady && !status.Running && status.CompatibilityProblem == null;

            if (step == WizardStep.Preflight && gameReady)
            {
                step = WizardStep.AccountSync;
            }
            if (step == WizardStep.ForgeAndGame && gameReady)
            {
                step = WizardStep.AccountSync;
                detail.Text = "";
            }
            if (step == WizardStep.AccountSync && gameReady)
            {
                await RefreshAccountAndCaptureAsync(lifetime.Token);
                if (captureBlock == null && !Game.IsRunning())
                {
                    if (accountState?.Imported == true)
                    {
                        await HellForgeForm.TryOpenWarbondsAction(client, lifetime.Token);
                        step = WizardStep.PackAndReady;
                        detail.Text = "";
                    }
                    else if (accountState is { Captured: true, Imported: false })
                    {
                        polling = false;
                        completed.TrySetResult();
                        busy = true;
                        UpdateView();
                        try { await ImportDetectedCaptureAsync(lifetime.Token); }
                        catch (Exception ex) { detail.Text = ex.Message; }
                        finally { busy = false; }
                    }
                }
            }
            if (step == WizardStep.PackAndReady && !step4Checked && !packReady && !busy)
            {
                polling = false;
                completed.TrySetResult();
                await RunStep4PackAsync(allowDownload: false);
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { gameReady = false; detail.Text = "Setup is not ready yet. " + HellForgeForm.FailureSummary(e.Message); }
        finally { polling = false; completed.TrySetResult(); if (!IsDisposed) UpdateView(); }
    }

    static bool ManagerRunning(string root)
    {
        var processes = Process.GetProcessesByName("HellForge.Launcher").Concat(Process.GetProcessesByName("HellForge")).ToArray();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;
                    var path = process.MainModule?.FileName;
                    if (path == null || Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (InvalidOperationException) { }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return true; }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            timer.Stop(); timer.Dispose();
            lifetime.Cancel(); SetApproval(null); manager?.Dispose(); lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
