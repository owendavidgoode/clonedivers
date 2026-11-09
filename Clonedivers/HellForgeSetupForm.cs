using System.Diagnostics;
using static Clonedivers.Palette;

namespace Clonedivers;

internal sealed class HellForgeSetupForm : Form
{
    readonly bool preview;
    readonly Label steam = Row(), installed = Row(), game = Row(), detail = Row();
    readonly TextBox folder = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Forge install folder" };
    readonly RoundButton next = new() { Height = 48, Dock = DockStyle.Fill, AccessibleName = "Continue Forge setup" };
    readonly Button change = new() { Text = "Change folder…", AutoSize = true }, locate = new() { Text = "Use existing Forge…", AutoSize = true }, cancel = new() { Text = "Close", AutoSize = true };
    readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 8, Visible = false };
    readonly PictureBox approval = new() { SizeMode = PictureBoxSizeMode.CenterImage, Width = 330, Height = 330, Visible = false, AccessibleName = "Steam Guard sign-in QR code" };
    readonly Button official = new() { Text = "Official setup (fallback)", AutoSize = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 3000 };
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? download;
    HellForgeStatus? status;
    bool busy, polling, managerOpened, ready, disposed, canPause;
    Process? manager;
    internal string? InstallationRoot { get; private set; }
    internal event Action<string>? Connected;

    internal HellForgeSetupForm(string? root = null, bool preview = false)
    {
        this.preview = preview;
        Text = "Clonedivers — Forge setup"; BackColor = Bg; ForeColor = TextMain;
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(700, 560); MinimumSize = new Size(620, 480);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "LET’S GET YOU READY", Font = new Font("Segoe UI", 20, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        var intro = Row(); intro.Text = "We’ll install Forge, connect its game copy, then install the required gameplay mods and selected appearance. Your regular HD2 game stays in Steam."; layout.Controls.Add(intro);
        foreach (var label in new[] { steam, installed, game }) { label.Font = new Font("Segoe UI", 11, FontStyle.Bold); label.Margin = new Padding(0, 10, 0, 8); layout.Controls.Add(label); }
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 12, 0, 8) };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(folder); pathRow.Controls.Add(change); layout.Controls.Add(pathRow);
        folder.BackColor = Disabled; folder.ForeColor = TextMain; change.Margin = new Padding(8, 0, 0, 0);
        var storage = Row(); storage.Text = "502 MB installer · Allow at least 50 GB for setup, plus appearance packs."; storage.ForeColor = TextMute; layout.Controls.Add(storage);
        detail.Margin = new Padding(0, 14, 0, 12); layout.Controls.Add(detail); layout.Controls.Add(progress);
        layout.Controls.Add(approval);
        next.BackColor = Orange; next.ForeColor = Bg; next.FlatStyle = FlatStyle.Flat; next.FlatAppearance.BorderSize = 0; next.FlatAppearance.MouseOverBackColor = OrangeHot;
        next.Font = new Font("Segoe UI", 11, FontStyle.Bold); next.Margin = new Padding(0, 12, 0, 8);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, Padding = new Padding(26, 0, 26, 18) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.Controls.Add(next);
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Controls = { locate, official, cancel } }; footer.Controls.Add(bottom); Controls.Add(footer);
        layout.SizeChanged += (_, _) => {
            var max = new Size(Math.Max(160, layout.ClientSize.Width - layout.Padding.Horizontal - 20), 0);
            foreach (var label in new[] { intro, steam, installed, game, storage, detail }) label.MaximumSize = max;
        };
        folder.Text = root ?? HellForgeSetup.DefaultRoot;
        if (root != null && !preview) InstallationRoot = HellForgeCommands.ValidateRoot(root);
        next.Click += async (_, _) => await ContinueAsync();
        official.Click += async (_, _) => { if (!busy && !polling) { try { await OpenManagerAsync(); } catch (Exception e) { detail.Text = e.Message; } UpdateView(); } };
        cancel.Click += (_, _) => { if (busy && canPause) download?.Cancel(); else if (!busy) Close(); };
        change.Click += (_, _) => ChooseFolder(false); locate.Click += async (_, _) => { ChooseFolder(true); await PollAsync(); };
        timer.Tick += async (_, _) => await PollAsync();
        Shown += async (_, _) => { if (!preview) { await PollAsync(); timer.Start(); } };
        FormClosing += (_, e) => { if (busy || polling) { e.Cancel = true; detail.Text = busy ? "Wait for installation to finish, or pause the download first." : "Checking setup… Please try closing again in a moment."; } };
        UpdateView();
    }

    static Label Row() => new() { AutoSize = true, ForeColor = TextDim, MaximumSize = new Size(630, 0), Margin = new Padding(0, 0, 0, 8) };
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dwm.ApplyDark(Handle, Bg, TextMain, Border); }

    void ChooseFolder(bool existing)
    {
        if (busy || preview) return;
        using var picker = new FolderBrowserDialog { Description = existing ? "Choose your existing HellForge folder" : "Choose a drive or parent folder for HellForge", UseDescriptionForTitle = true, ShowNewFolderButton = !existing };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var selected = existing ? HellForgeCommands.ValidateRoot(picker.SelectedPath) : HellForgeSetup.ValidateTarget(Path.Combine(picker.SelectedPath, "HellForge"));
            folder.Text = selected; InstallationRoot = existing ? selected : null; status = null; ready = managerOpened = false;
            if (InstallationRoot != null) Connected?.Invoke(InstallationRoot);
            detail.Text = ""; UpdateView();
        }
        catch (Exception e) { detail.Text = e.Message; }
    }

    void UpdateView()
    {
        var steamReady = preview || HellForgeForm.SteamReadyCheck();
        steam.Text = "1. Steam — " + (steamReady ? "signed in" : "open Steam and sign in");
        installed.Text = "2. Forge — " + (status != null && HellForgeSetup.IsPrevious(status.Version) ? "update available" : InstallationRoot != null ? "installed" : "ready to install");
        game.Text = "3. Game copy — " + (ready ? "ready" : managerOpened ? "finish in the Forge window" : "download or resume here");
        change.Enabled = locate.Enabled = !busy && !polling && !managerOpened;
        official.Enabled = !busy && !polling && InstallationRoot != null && status?.Running != true;
        next.Enabled = !preview && !busy && !polling && status?.Running != true && !Game.IsRunning();
        cancel.Enabled = !busy || canPause; cancel.Text = busy ? canPause ? "Pause download" : "Installing…" : "Close";
        next.Text = ready ? "RETURN TO CLONEDIVERS" : !steamReady ? "OPEN STEAM" : InstallationRoot == null ? "INSTALL FORGE" : status != null && HellForgeSetup.IsPrevious(status.Version) ? "UPDATE HELLFORGE" : "CONTINUE GAME SETUP";
        if (status?.Running == true || !preview && Game.IsRunning()) detail.Text = "A game session is running. End it in its launcher before continuing setup.";
        else if (ready) detail.Text = "Forge game setup passed. Return to install the required gameplay preset and selected appearance automatically. You’ll then sync your own HD2 account.";
        else if (!busy && string.IsNullOrWhiteSpace(detail.Text)) detail.Text = "No downloads, folders or commands to find. We’ll guide you through each step.";
    }

    async Task ContinueAsync()
    {
        if (preview || busy || polling) return;
        if (ready) { DialogResult = DialogResult.OK; Close(); return; }
        if (Game.IsRunning() || status?.Running == true) { UpdateView(); return; }
        if (!HellForgeForm.SteamReadyCheck())
        {
            try { Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true }); detail.Text = "Finish signing in to the Steam account that owns HD2, then continue here."; }
            catch (Exception e) { detail.Text = e.Message; }
            return;
        }
        busy = true; UpdateView();
        try
        {
            // Both installs and upgrades only download while CanCancel; upgrades
            // stop observing the token once their recovery record is saved.
            download = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var updates = new Progress<ForgeSetupProgress>(p => {
                if (IsDisposed) return;
                canPause = p.CanCancel;
                progress.Visible = true; progress.Style = p.Total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (p.Total > 0) progress.Value = (int)Math.Clamp(p.Done * 100 / p.Total, 0, 100);
                if (p.QrRows != null)
                {
                    ClientSize = new Size(ClientSize.Width, Math.Max(ClientSize.Height, 830));
                    var pixels = Math.Min(330, Math.Max(120, approval.Parent!.ClientSize.Height - 60));
                    SetApproval(SteamSetupOutput.RenderQr(p.QrRows, pixels));
                }
                else if (p.Message == "Signed in.") SetApproval(null);
                detail.Text = p.Message + (p.Total > 100 ? $" {Pack.FormatBytes(p.Done)} / {Pack.FormatBytes(p.Total)}" : "");
                UpdateView();
            });
            if (InstallationRoot == null)
            {
                canPause = true;
                InstallationRoot = await HellForgeSetup.InstallAsync(folder.Text, updates, download.Token);
                Connected?.Invoke(InstallationRoot);
            }
            else if (status != null && HellForgeSetup.IsPrevious(status.Version))
            { canPause = true; await HellForgeSetup.UpdateAsync(InstallationRoot, updates, download.Token); }
            if (ManagerRunning(InstallationRoot!)) throw new InvalidOperationException("Close the HellForge window before downloading here, or finish with Official setup.");
            await HellForgeSetup.PrepareGameAsync(InstallationRoot!, updates, download.Token);
            status = await new HellForgeClient(InstallationRoot!).StatusAsync(download.Token);
            ready = status.GameCopyReady && status.RunnerReady && !status.Running && status.CompatibilityProblem == null;
            if (ready) { busy = false; DialogResult = DialogResult.OK; Close(); }
        }
        catch (OperationCanceledException) { detail.Text = "Download paused. Continue to resume the saved files. Steam may ask for a fresh QR approval."; }
        catch (Exception e) { detail.Text = e.Message; }
        finally { download?.Dispose(); download = null; canPause = false; busy = false; SetApproval(null); progress.Visible = false; if (!IsDisposed) UpdateView(); }
    }

    void SetApproval(Image? image)
    {
        var previous = approval.Image; approval.Image = image; approval.Visible = image != null; previous?.Dispose();
        if (image != null)
        {
            approval.Size = image.Size;
            if (approval.Parent is ScrollableControl scroller) scroller.ScrollControlIntoView(approval);
        }
    }

    async Task OpenManagerAsync()
    {
        var root = HellForgeCommands.ValidateRoot(InstallationRoot!);
        var current = await new HellForgeClient(root).StatusAsync(lifetime.Token);
        if (current.Running || Game.IsRunning()) throw new InvalidOperationException("End the current game session before continuing setup.");
        if (!HellForgeSetup.SupportsVersion(current.Version) || current.Build != HellForgePack.SupportedBuild)
            throw new InvalidDataException("This installation is a different Forge version. Setup will not replace it. Choose a separate pinned installation.");
        if (current.GameCopyReady && current.RunnerReady && current.CompatibilityProblem == null) { ready = true; DialogResult = DialogResult.OK; Close(); return; }
        if (ManagerRunning(root)) { managerOpened = true; detail.Text = "In the Forge window, click Start setup. Approve its Windows prompt when asked. We’ll detect completion here."; return; }
        manager?.Dispose(); manager = Process.Start(new ProcessStartInfo(Path.Combine(root, "HellForge.exe")) { UseShellExecute = true, WorkingDirectory = root });
        managerOpened = true;
        detail.Text = "In the Forge window, click Start setup and approve its Windows prompt. If Steam needs the supported build, follow Forge’s download instructions. Keep this launcher open; it will detect completion.";
    }

    async Task PollAsync()
    {
        if (preview || busy || polling || InstallationRoot == null) { if (!busy) UpdateView(); return; }
        polling = true;
        try
        {
            status = await new HellForgeClient(InstallationRoot).StatusAsync(lifetime.Token);
            ready = status.GameCopyReady && status.RunnerReady && !status.Running && status.CompatibilityProblem == null;
            if (ready && managerOpened) { polling = false; DialogResult = DialogResult.OK; Close(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { ready = false; detail.Text = "Setup is not ready yet. " + HellForgeForm.FailureSummary(e.Message); }
        finally { polling = false; if (!IsDisposed) UpdateView(); }
    }

    static bool ManagerRunning(string root)
    {
        // HellForge.exe may hand off to app/launcher and exit immediately.
        // Check the installation's actual manager, never a wrapper PID alone.
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
        if (disposing && !disposed) { disposed = true; timer.Stop(); timer.Dispose(); lifetime.Cancel(); SetApproval(null); manager?.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
