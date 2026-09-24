using static Clonedivers.Palette;

namespace Clonedivers;

public sealed partial class MainForm
{
    bool collectingDiagnostics;
    SessionObserver? sessionObserver;
    DateTimeOffset nextSessionObservation;
    TelemetryRecorder? telemetryRecorder;

    SessionLog.Context SessionContext()
    {
        string? mode = null;
        string? build = null;
        try { var selected = LauncherModes.Current(ModFiles.GetState(gameDir), settings, manifest?.Pack); if (selected is {} m) mode = LauncherModes.Name(m); } catch { }
        try { build = SteamAcf.Read(gameDir)?.BuildId; } catch { }
        bool? Option(string key) => settings.Options.TryGetValue(key, out var on) ? on : null;
        return new(settings.InstalledPackVersion, build, settings.InstalledTextureProfile, mode, Option("commandos"), Option("droids"), Option("aimpoints"));
    }
    void RecordSession(string kind, double? seconds = null)
    {
        if (preview) return;
        var context=SessionContext();
        SessionLog.Record(new(DateTimeOffset.UtcNow, kind, context, seconds));
        telemetryRecorder?.UpdateContext(context);
        telemetryRecorder?.Event(kind, seconds);
    }

    void StartSessionObservation()
    {
        if (preview) return;
        if(settings.Telemetry.Ready) telemetryRecorder = new(settings.Telemetry, SessionContext());
        var last = SessionLog.Read().LastOrDefault();
        if (last is not null && last.Event != "launcher-closed") RecordSession("previous-monitor-interrupted");
        RecordSession("launcher-opened");
        sessionObserver = new SessionObserver(RecordSession);
        ObserveSession();
    }

    void ObserveSession()
    {
        if (preview || sessionObserver is null || DateTimeOffset.UtcNow < nextSessionObservation) return;
        nextSessionObservation = DateTimeOffset.UtcNow.AddSeconds(5);
        bool? running;
        try { running = Game.IsRunning(); } catch { running = null; }
        sessionObserver.Observe(running, DateTimeOffset.UtcNow);
    }

    void RefreshModeLayout()
    {
        var combined = manifest?.Pack?.CombinedRoster == true;
        for (int i = 0; i < modeButtons.Count; i++)
        {
            var visible = !combined || modeButtons[i].Mode != LauncherMode.CommandoDivers;
            modeButtons[i].Visible = visible;
            modeRow.ColumnStyles[i].SizeType = visible ? SizeType.Percent : SizeType.Absolute;
            modeRow.ColumnStyles[i].Width = visible ? 100F / (combined ? 2 : 3) : 0;
        }
    }

    void RefreshExtras(bool armed)
    {
        var pack = manifest?.Pack;
        foreach (var button in extraButtons)
        {
            var option = pack?.Options.FirstOrDefault(o => o.Id == (string)button.Tag!);
            if (option is null) continue;
            var on = OptionEnabled(option);
            button.Enabled = armed && (preview || pack is { IsPublished: true, IsPerFile: true });
            button.Text = option.Name + (on ? ": On" : ": Off");
            button.AccessibleDescription = option.Description + (on ? " Enabled." : " Disabled.");
            button.BackColor = button.Enabled ? (on ? Blue : Slate) : Disabled;
            button.ForeColor = button.Enabled ? TextMain : TextDim;
            Tip(button, option.Id == "droids" ? "CIS enemies and vehicles." : option.Description);
        }
    }

    void RestoreInstallationMetadata()
    {
        if (settings.ForgetInstallationForDifferentGame(gameDir)) settings.Save();
        if (gameDir is null || Pack.HasPendingUpdate(gameDir, Settings.PlanPath)) return;
        var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir), gameDir);
        if (receipt is null || !receipt.MatchesLayout(gameDir)) return;
        settings.RecordInstall(receipt.ToApplyResult(), preserveTexturePreference: true);
        settings.Save();
    }
    string? AvailableTextureProfile(PackManifest pack)
    {
        try { return settings.ProfileFor(pack); }
        catch (InvalidDataException) { return null; }
    }
    void UpdateMetadataFooter()
    {
        var source = string.IsNullOrWhiteSpace(settings.ManifestUrl) ? "" : $"  ·  test feed: {HostOf(settings.ManifestUrl)}";
        footer.Text = $"v{AppInfo.Version}{source}  ·  " + (metadataOffline ? "Offline · saved pack information" : "For the Republic.");
    }

    void OnPotatoToggle()
    {
        var pack = manifest?.Pack ?? new PackManifest();
        var on = AvailableTextureProfile(pack) is {} current && current != "full";
        var target = on ? PackProfiles.Supported(pack).First(p => p.Id == "full") : PackProfiles.Potato(pack);
        if (target is not null) OnTextureProfile(target);
    }
    void OnTextureProfile(PackTextureProfile profile)
    {
        var pack = manifest?.Pack ?? new PackManifest();
        if (Busy || (!preview && (Game.IsRunning() || Pack.HasPendingUpdate(gameDir, Settings.PlanPath)))) return;
        if (string.Equals(AvailableTextureProfile(pack), profile.Id, StringComparison.OrdinalIgnoreCase)) return;
        if (!preview && gameDir is not null && ModFiles.GetState(gameDir) == ModState.On)
        {
            if (!pack.IsPublished || !pack.IsPerFile) return;
            _ = RunPackAsync(pack, verify: false, optionChange: null, textureProfileChange: profile.Id);
            return;
        }
        settings.TextureProfile = profile.Id;
        settings.Options["skinny"] = profile.Id != "full";
        if (!preview) settings.Save();
        ShowProgressDone(PackProfiles.PotatoName + (profile.Id == "full" ? " off." : " on."));
        RefreshState();
    }

    void OnRepair()
    {
        if (preview || Busy || gameDir is null) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        if (Pack.HasPendingUpdate(gameDir, Settings.PlanPath)) { _ = FinishUpdateAsync(); return; }
        var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir), gameDir);
        if (receipt is not null)
        {
            var target = receipt.ToRecoveryTarget(ModFiles.GetState(gameDir) == ModState.On);
            var installed = new PackManifest {
                Version = receipt.PackVersion, GameBuild = receipt.GameBuild, Files = target.Files,
                Options = new() {
                    new() { Id = "commandos", Default = receipt.Options?.GetValueOrDefault("commandos") ?? false },
                    new() { Id = "skinny", Default = receipt.TextureProfile != "full" }
                }
            };
            _ = RunPackAsync(installed, verify: true, optionChange: null, savedTarget: target);
        }
        else if (manifest?.Pack is { IsPublished: true, IsPerFile: true } pack)
        {
            if (AvailableTextureProfile(pack) is null) { ShowProgressDone("Choose an available texture profile before repairing this unrecorded installation."); return; }
            _ = RunPackAsync(pack, verify: true, optionChange: null);
        }
        else
            ShowProgressDone("Connect once to retrieve pack information, then check and repair.");
    }

    async Task ShowDiagnosticsAsync()
    {
        if (Busy || preview || collectingDiagnostics) return;
        collectingDiagnostics = true;
        diagnosticsButton.Enabled = false;
        try
        {
            var report = await Task.Run(() => Diagnostics.Build(settings, gameDir, manifest, lastOperationError));
            if (IsDisposed) return;
            using var dialog = new Form {
                Text = "Help & diagnostics", StartPosition = FormStartPosition.CenterParent,
                Size = new Size(720, 600), MinimumSize = new Size(540, 420),
                BackColor = Bg, ForeColor = TextMain, Font = new Font("Segoe UI", 10F)
            };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill,
                Text = "Copy or save this support report. Performance sharing: " + (settings.Telemetry.Ready ? telemetryRecorder?.Status ?? "enabled" : "off") + ".", Margin = new Padding(0, 0, 0, 12) }, 0, 0);
            layout.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                WordWrap = false, Dock = DockStyle.Fill, Text = report, BackColor = Slate, ForeColor = TextMain,
                Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.FixedSingle }, 0, 1);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var copy = new Button { Text = "Copy report", AutoSize = true };
            var save = new Button { Text = "Save report…", AutoSize = true };
            var import = new Button { Text = "Install from ZIP…", AutoSize = true };
            var performance = new Button { Text = "Performance sharing…", AutoSize = true };
            performance.Click += (_, _) => ShowPerformanceSettings(dialog);
            foreach (var button in new[] { close, copy, save, import, performance }) { Style(button, Slate, SlateHot, TextMain, 10F); button.Padding = new Padding(10, 5, 10, 5); buttons.Controls.Add(button); }
            save.Click += (_, _) => {
                using var picker = new SaveFileDialog { Filter = "Support report (*.txt)|*.txt", FileName = $"clonedivers-support-{DateTime.Now:yyyyMMdd-HHmmss}.txt", AddExtension = true, DefaultExt = "txt" };
                if (picker.ShowDialog(dialog) != DialogResult.OK) return;
                try { File.WriteAllText(picker.FileName, report); save.Text = "Saved"; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { save.Text = "Couldn't save"; }
            };
            copy.Click += (_, _) => {
                try { Clipboard.SetText(report); copy.Text = "Copied"; }
                catch (System.Runtime.InteropServices.ExternalException) { copy.Text = "Try copying again"; }
            };
            bool installZip = false;
            import.Click += (_, _) => { installZip = true; dialog.Close(); };
            layout.Controls.Add(buttons, 0, 2);
            dialog.Controls.Add(layout); dialog.CancelButton = close;
            dialog.ShowDialog(this);
            if (installZip) OnInstallFromFile();
        }
        catch (Exception e)
        {
            lastOperationError = e.Message;
            ShowProgressDone("Couldn't collect diagnostics. Try again after the current operation finishes.");
        }
        finally { collectingDiagnostics = false; if (!IsDisposed) diagnosticsButton.Enabled = !Busy; }
    }
}
