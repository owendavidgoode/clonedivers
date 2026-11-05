using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Clonedivers;

internal sealed class LauncherWindow : Window
{
    static readonly IBrush Navy = Brush.Parse("#0B1020"), Dim = Brush.Parse("#96A2BE"), Blue = Brush.Parse("#3476E6"), Orange = Brush.Parse("#F48C34");
    readonly Settings settings = Settings.Load();
    readonly string? source;
    string? gameDir;
    PackManifest? pack;
    LauncherMode selected = LauncherMode.Clonedivers;
    bool busy, updatingOptions, pathDirty, closing;
    CancellationTokenSource? operationCancellation;
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal readonly Dictionary<LauncherMode, Button> Modes = new();
    internal readonly Button ApplyButton = ActionButton("Install / update pack"), RepairButton = ActionButton("Check & repair"), FinishButton = ActionButton("Finish update"), LaunchButton = ActionButton("LAUNCH CLONEDIVERS");
    internal readonly Button UseFolderButton = ActionButton("Use folder"), CancelButton = ActionButton("Cancel");
    internal readonly TextBox Folder = new() { Watermark = "Choose the Helldivers 2 folder", MinWidth = 200 };
    internal readonly CheckBox Droids = new() { Content = "Droid skins" }, Covenant = new() { Content = "Covenant squids" }, Potato = new() { Content = PackProfiles.PotatoName };
    readonly TextBlock state = Text("Choose your universe.", 16), installed = Text("", 12, Dim), message = Text("", 13, Dim), progressText = Text("", 12, Dim);
    readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 5 };
    readonly Button browse = ActionButton("Browse…"), report = ActionButton("Save report"), recording = ActionButton("Recording…"), shortcut = ActionButton("Add app shortcut");
    readonly LauncherUpdates launcherUpdates = new();
    readonly CancellationTokenSource updateCancellation = new();
    LauncherUpdateStatus? launcherUpdate;
    internal readonly TextBlock UpdateNotice = Text("Checking launcher updates…", 12, Dim);
    internal readonly Button CheckUpdateButton = ActionButton("Check for updates"), ViewUpdateButton = ActionButton("View release");
    readonly Border updatePanel = new() { IsVisible = false, Background = Brush.Parse("#172440"), CornerRadius = new CornerRadius(7), Padding = new Thickness(12) };
    internal Task UpdateOperation { get; private set; } = Task.CompletedTask;
    internal bool Smoke { get; }
    internal string? SmokeOutput { get; }
    internal string? GameDirectory => gameDir;

    public LauncherWindow(string[] args)
    {
        string? requested = null;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--gui": break;
                case "--game": requested = Next(args, ref i); break;
                case "--manifest": source = Next(args, ref i); break;
                case "--ui-smoke": Smoke = true; SmokeOutput = Next(args, ref i); break;
                default: throw new ArgumentException("Unknown desktop option: " + args[i]);
            }
        gameDir = GameLocator.NormalizeGameDir(requested ?? settings.GamePath) ?? (requested is null ? GameLocator.AutoDetect() : null);
        Title = "Clonedivers"; Width = 780; Height = 820; MinWidth = 640; MinHeight = 560;
        Background = Navy; Foreground = Brushes.White; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28, 20), Spacing = 12 };
        var header = new StackPanel { Spacing = 3 };
        header.Children.Add(Text("CLONEDIVERS", 30, Brushes.White, FontWeight.Bold));
        header.Children.Add(Text("Clones and commandos, together.  ·  Linux", 13, Dim)); panel.Children.Add(header);
        var updateRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        updateRow.Children.Add(UpdateNotice); Grid.SetColumn(ViewUpdateButton, 1); updateRow.Children.Add(ViewUpdateButton); updatePanel.Child = updateRow; panel.Children.Add(updatePanel);
        var modes = new UniformGrid { Columns = 3, Rows = 1 };
        foreach (var mode in new[] { LauncherMode.Helldivers, LauncherMode.Clonedivers, LauncherMode.EmpireDivers })
        {
            var content = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(Helmet(mode)); content.Children.Add(Text(LauncherModes.Name(mode), 17, Brushes.White, FontWeight.SemiBold));
            content.Children.Add(Text(mode == LauncherMode.Helldivers ? "THE ORIGINAL" : mode == LauncherMode.EmpireDivers ? "FOR THE EMPIRE" : "FOR THE REPUBLIC", 10, Dim));
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 12), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10) };
            button.Click += (_, _) => { selected = mode; UpdateView(); }; Modes[mode] = button; modes.Children.Add(button);
        }
        panel.Children.Add(modes);
        panel.Children.Add(new StackPanel { Spacing = 5, Children = { state, installed } });
        var options = new StackPanel { Spacing = 4 };
        options.Children.Add(Text("PACK OPTIONS", 11, Dim, FontWeight.SemiBold));
        var extras = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, Children = { Droids, Covenant } }; options.Children.Add(extras); options.Children.Add(Potato);
        ToolTip.SetTip(Potato, "Stream full-detail textures as needed. Does not guarantee a higher frame rate.");
        foreach (var check in new[] { Droids, Covenant, Potato }) check.IsCheckedChanged += (_, _) => SaveOptions();
        panel.Children.Add(options);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { ApplyButton, RepairButton, FinishButton, CancelButton }) { button.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(button); }
        panel.Children.Add(row);
        LaunchButton.Background = Orange; LaunchButton.Foreground = Navy; LaunchButton.FontSize = 19; LaunchButton.FontWeight = FontWeight.Bold;
        LaunchButton.HorizontalAlignment = HorizontalAlignment.Stretch; LaunchButton.HorizontalContentAlignment = HorizontalAlignment.Center; LaunchButton.Height = 56;
        panel.Children.Add(LaunchButton);
        panel.Children.Add(new StackPanel { Spacing = 6, Children = { progress, progressText, message } });
        panel.Children.Add(new Separator()); var folderLabel = Text("HELLDIVERS 2 FOLDER", 11, Dim, FontWeight.SemiBold);
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7 };
        folderRow.Children.Add(Folder); Grid.SetColumn(browse, 1); folderRow.Children.Add(browse); Grid.SetColumn(UseFolderButton, 2); folderRow.Children.Add(UseFolderButton);
        panel.Children.Insert(1, folderLabel); panel.Children.Insert(2, folderRow);
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { report, recording, shortcut, CheckUpdateButton }) { button.Margin = new Thickness(0, 0, 8, 0); tools.Children.Add(button); }
        panel.Children.Add(tools); panel.Children.Add(Text($"v{AppInfo.Version}  ·  Steam launches the game normally.  ·  For the Republic.", 11, Dim));
        ViewUpdateButton.IsVisible = false;
        CheckUpdateButton.Click += (_, _) => UpdateOperation = CheckLauncherUpdatesAsync(force: true);
        ViewUpdateButton.Click += (_, _) =>
        {
            try { if (launcherUpdate is not null) LauncherUpdates.OpenRelease(launcherUpdate); }
            catch (Exception ex) { UpdateNotice.Text = "Open " + (launcherUpdate?.Url ?? LauncherUpdates.ReleasesPage) + " in your browser. " + ex.Message; }
        };
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Folder.Text = gameDir ?? "";
        Folder.TextChanged += (_, _) => { pathDirty = Folder.Text != gameDir; UpdateView(); };
        UseFolderButton.Click += (_, _) => SetFolder(Folder.Text);
        browse.Click += (_, _) => Begin(BrowseAsync);
        ApplyButton.Click += (_, _) => Begin(ApplySelectedAsync);
        RepairButton.Click += (_, _) => Begin(RepairAsync);
        FinishButton.Click += (_, _) => Begin(FinishAsync);
        LaunchButton.Click += (_, _) => Begin(LaunchAsync);
        CancelButton.Click += (_, _) => operationCancellation?.Cancel();
        report.Click += (_, _) => Begin(SaveReportAsync);
        recording.Click += (_, _) => Begin(RecordingAsync);
        shortcut.Click += (_, _) => Begin(AddShortcutAsync);
        Closing += (_, e) =>
        {
            if (busy) { e.Cancel = true; operationCancellation?.Cancel(); message.Text = "Stopping the operation. File finalization finishes safely; close again when ready."; }
            else { closing = true; refresh.Stop(); }
        };
        refresh.Tick += (_, _) => { if (!busy && !closing) UpdateView(); };
        Opened += async (_, _) =>
        {
            UpdateOperation = CheckLauncherUpdatesAsync();
            AdoptInstalledMode(); Begin(LoadFeedAsync); await Operation; refresh.Start();
            if (Smoke) await GuiSmoke.RunAsync(this);
        };
        Closed += (_, _) => { closing = true; updateCancellation.Cancel(); updateCancellation.Dispose(); };
        UpdateView();
    }

    async Task CheckLauncherUpdatesAsync(bool force = false)
    {
        CheckUpdateButton.IsEnabled = false; UpdateNotice.Text = "Checking launcher updates…"; if (force) updatePanel.IsVisible = true;
        try
        {
            var result = await launcherUpdates.CheckAsync(updateCancellation.Token, force);
            if (closing) return;
            launcherUpdate = result; UpdateNotice.Text = result.Message; ViewUpdateButton.IsVisible = result.Available is not null;
            updatePanel.IsVisible = force || result.Available is not null;
            UpdateNotice.Foreground = result.Available is not null ? Orange : Dim;
            ToolTip.SetTip(ViewUpdateButton, "Download the new launcher, close this window, then install the .deb or replace the portable executable. Pack files and settings are preserved.");
        }
        catch (OperationCanceledException) { }
        finally { if (!closing) CheckUpdateButton.IsEnabled = true; }
    }

    static string Next(string[] args, ref int i) => ++i < args.Length ? args[i] : throw new ArgumentException("Missing desktop option value.");
    static TextBlock Text(string value, double size, IBrush? color = null, FontWeight? weight = null) => new() { Text = value, FontSize = size, Foreground = color ?? Brushes.White, FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
    static Button ActionButton(string title) => new() { Content = title, Padding = new Thickness(13, 9), CornerRadius = new CornerRadius(6) };
    internal void Begin(Func<Task> action)
    {
        if (busy) return;
        Operation = RunAsync(action);
    }
    async Task RunAsync(Func<Task> action)
    {
        busy = true; operationCancellation = new(); progress.IsIndeterminate = true; progressText.Text = "Working…"; message.Text = ""; UpdateView();
        try { await action(); }
        catch (OperationCanceledException) { message.Text = "Stopped. Downloaded files are kept so you can resume."; }
        catch (Exception ex) { message.Text = ex.Message; LinuxSupport.Record("error", ex.GetType().Name + ": " + ex.Message); }
        finally { busy = false; progress.IsIndeterminate = false; operationCancellation.Dispose(); operationCancellation = null; progressText.Text = ""; UpdateView(); }
    }

    void AdoptInstalledMode()
    {
        if (gameDir is null) return;
        var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir), gameDir);
        if (ModFiles.GetState(gameDir) == ModState.Off) selected = LauncherMode.Helldivers;
        else selected = (receipt?.Options ?? settings.Options).GetValueOrDefault("empire") ? LauncherMode.EmpireDivers : LauncherMode.Clonedivers;
    }
    void SetFolder(string? path)
    {
        var normalized = GameLocator.NormalizeGameDir(path?.Trim().Trim('"', '\''));
        if (normalized is null) { message.Text = "Choose the Helldivers 2 folder containing data and bin/helldivers2.exe."; return; }
        gameDir = normalized; settings.ForgetInstallationForDifferentGame(gameDir); settings.GamePath = gameDir; settings.Save();
        Folder.Text = gameDir; pathDirty = false; AdoptInstalledMode(); message.Text = "Game folder selected."; UpdateView();
    }
    async Task BrowseAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose Helldivers 2", AllowMultiple = false });
        if (folders.Count > 0) SetFolder(folders[0].TryGetLocalPath());
    }
    async Task LoadFeedAsync()
    {
        progressText.Text = "Checking the current pack…";
        var cache = ManifestStore.CachePath(source, PlatformPaths.ConfigDirectory);
        Manifest metadata;
        try
        {
            metadata = await ManifestStore.FetchAsync(source, operationCancellation!.Token); ManifestStore.ValidateForUse(metadata);
            metadata = ManifestStore.PreserveProfiles(ManifestStore.Load(cache), metadata);
            try { ManifestStore.Save(cache, metadata); } catch (IOException) { }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metadata = ManifestStore.Load(cache) ?? throw new IOException("Pack information is unavailable. Connect once to download or update. An already-installed pack can still launch.", ex);
            message.Text = "Offline — using saved pack information.";
        }
        pack = metadata.Pack; updatingOptions = true;
        Droids.IsChecked = settings.Options.GetValueOrDefault("droids", pack!.Options.FirstOrDefault(o => o.Id == "droids")?.Default ?? false);
        Covenant.IsChecked = settings.Options.GetValueOrDefault("covenant", pack.Options.FirstOrDefault(o => o.Id == "covenant")?.Default ?? false);
        try { Potato.IsChecked = settings.ProfileFor(pack) != "full"; } catch (InvalidDataException) { settings.TextureProfile = "full"; Potato.IsChecked = false; }
        updatingOptions = false;
    }
    void SaveOptions()
    {
        if (updatingOptions || pack is null) return;
        settings.Options["droids"] = Droids.IsChecked == true; settings.Options["covenant"] = Covenant.IsChecked == true;
        settings.TextureProfile = Potato.IsChecked == true ? PackProfiles.Potato(pack)?.Id ?? "full" : "full"; settings.Save();
        message.Text = "Options apply when you install/update or launch the selected mode.";
    }
    void UpdateView()
    {
        try
        {
            var running = Game.IsRunning(); var pending = gameDir is not null && Pack.HasPendingUpdate(gameDir, LinuxInstaller.PlanPathFor(gameDir));
            var recoverable = pending && RecoveryTarget.Read(gameDir!, LinuxInstaller.PlanPathFor(gameDir!)) is not null;
            var ready = !busy && !running && gameDir is not null && !pathDirty;
            foreach (var pair in Modes)
            {
                pair.Value.IsEnabled = !busy && !running;
                pair.Value.Background = Brush.Parse(pair.Key == selected ? "#182F52" : "#101729"); pair.Value.BorderBrush = pair.Key == selected ? Blue : Brush.Parse("#26304D");
            }
            state.Text = running ? "Helldivers 2 is running — close it before making changes." : gameDir is null ? "Choose your Helldivers 2 folder to get started." : pending ? recoverable ? "An interrupted update needs to be finished." : "Unfinished files found. Install/update or repair to restore the pack." : LauncherModes.Name(selected) + " selected";
            var receipt = gameDir is null ? null : InstallReceipt.Load(Settings.ReceiptPathFor(gameDir), gameDir);
            installed.Text = receipt is null ? "Pack not recorded yet. Install/update verifies and reuses existing files." : $"Installed: {receipt.PackVersion}  ·  {receipt.TextureProfile} textures  ·  {(ModFiles.GetState(gameDir) == ModState.Off ? "mods parked" : "mods active")}";
            ApplyButton.IsEnabled = ready && !recoverable && (pack is not null || selected == LauncherMode.Helldivers && !pending);
            RepairButton.IsEnabled = ready && !recoverable && receipt is not null;
            FinishButton.IsVisible = recoverable; FinishButton.IsEnabled = ready && recoverable;
            LaunchButton.IsEnabled = ready && !pending; LaunchButton.Content = "LAUNCH " + LauncherModes.Name(selected).ToUpperInvariant();
            CancelButton.IsVisible = busy; CancelButton.IsEnabled = busy;
            browse.IsEnabled = !busy && !running; UseFolderButton.IsEnabled = !busy && !running; Folder.IsEnabled = !busy && !running;
            foreach (var check in new[] { Droids, Covenant, Potato }) check.IsEnabled = ready && !pending && pack is not null && selected != LauncherMode.Helldivers;
            Potato.IsVisible = pack is null || PackProfiles.Potato(pack) is not null;
            report.IsEnabled = !busy; recording.IsEnabled = !busy && gameDir is not null && !pathDirty; shortcut.IsEnabled = !busy;
        }
        catch (Exception ex) { message.Text = ex.Message; ApplyButton.IsEnabled = RepairButton.IsEnabled = LaunchButton.IsEnabled = false; }
    }
    void RequireGame()
    {
        if (gameDir is null || pathDirty) throw new InvalidOperationException("Choose and confirm the game folder first.");
        LinuxInstaller.RequireClosed();
    }
    async Task<bool> ConfirmAsync(string title, string body)
    {
        if (Smoke) return true;
        var dialog = Dialog(title, body); var yes = ActionButton("Continue"); var no = ActionButton("Cancel");
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { no, yes } };
        ((StackPanel)dialog.Content!).Children.Add(row); yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(this);
    }
    static Window Dialog(string title, string body) => new() { Title = title, Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 700, Background = Navy, Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new StackPanel { Margin = new Thickness(24), Spacing = 18, Children = { Text(body, 14) } } };
    Task<bool> ConfirmFromWorker(string title, string body)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () => { try { result.TrySetResult(await ConfirmAsync(title, body)); } catch (Exception ex) { result.TrySetException(ex); } });
        return result.Task;
    }
    async Task CheckBuildAsync(string? tested)
    {
        LinuxInstaller.RequireNoSteamUpdate(gameDir!); var actual = SteamAcf.Read(gameDir)?.BuildId;
        if (tested is null || actual != tested)
            if (!await ConfirmAsync("Game compatibility", $"Your game build: {actual ?? "unknown"}\nPack tested against: {tested ?? "unknown"}\n\nCompatibility with this build is unverified. Continue?")) throw new OperationCanceledException();
    }
    async Task ApplyTargetAsync(RecoveryTarget target)
    {
        await CheckBuildAsync(target.GameBuild); progressText.Text = "Checking local files…";
        var watch = Stopwatch.StartNew();
        var reporter = new SyncProgress<(long done, long total, int file, int files)>(p =>
        {
            if (watch.ElapsedMilliseconds < 150 && p.done < p.total) return; watch.Restart();
            Dispatcher.UIThread.Post(() => { if (!busy) return; progress.IsIndeterminate = false; progress.Value = p.total > 0 ? p.done * 100d / p.total : 0; progressText.Text = $"Downloading {p.file}/{p.files}  ·  {Pack.FormatBytes(p.done)} / {Pack.FormatBytes(p.total)}"; });
        });
        var token = operationCancellation!.Token;
        var result = await Task.Run(() => LinuxInstaller.ApplyWithConfirmationAsync(gameDir!, target, LinuxInstaller.PlanPathFor(gameDir!), Settings.ReceiptPathFor(gameDir!),
            plan => ConfirmFromWorker("Apply pack", $"Pack {target.PackVersion}\nDownload: {Pack.FormatBytes(plan.BytesToDownload)}\nLocal copies: {Pack.FormatBytes(plan.BytesToCopy)}\nReplaced files are kept in mods_old for recovery.\n\nContinue?"), reporter, token));
        if (result.Interrupted) throw new IOException("Update interrupted. Use Finish update. " + result.Reason + " " + string.Join(", ", result.Failed));
        settings.RecordInstall(result); settings.Save(); RefreshRecordingSelection();
        LinuxSupport.Record("installed", $"Pack {result.PackVersion}, texture profile {result.TextureProfile}.");
        selected = target.TargetActive ? target.Options.GetValueOrDefault("empire") ? LauncherMode.EmpireDivers : LauncherMode.Clonedivers : LauncherMode.Helldivers;
        progress.Value = 100; message.Text = "Pack ready. You can launch through Steam.";
    }
    internal async Task ApplySelectedAsync()
    {
        RequireGame(); using var gameLock = LinuxInstaller.Lock(gameDir!);
        if (Pack.HasPendingUpdate(gameDir, LinuxInstaller.PlanPathFor(gameDir!)) && (selected == LauncherMode.Helldivers || RecoveryTarget.Read(gameDir!, LinuxInstaller.PlanPathFor(gameDir!)) is not null)) throw new InvalidOperationException("Finish the interrupted update before changing modes.");
        if (selected == LauncherMode.Helldivers)
        {
            var count = await Task.Run(() => ModFiles.MoveAllPatchFiles(Path.Combine(gameDir!, ModFiles.DataFolder), Path.Combine(gameDir!, ModFiles.OffFolder)));
            var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!);
            if (receipt is not null && receipt.MatchesLayout(gameDir!)) { var result = receipt.ToApplyResult(); result.TargetActive = false; InstallReceipt.Write(Settings.ReceiptPathFor(gameDir!), gameDir!, receipt.Files.Select(f => f.File).ToList(), result); }
            RefreshRecordingSelection(); LinuxSupport.Record("vanilla", $"Parked {count} mod files.");
            message.Text = $"Vanilla ready. Parked {count} mod files."; return;
        }
        if (pack is null) await LoadFeedAsync();
        if (pack!.IsBroken) throw new InvalidOperationException("Pack updates are paused: " + pack.StatusNotes);
        var options = LauncherModes.OptionsFor(settings, pack, selected); var profile = settings.ProfileFor(pack);
        await ApplyTargetAsync(new RecoveryTarget { PackVersion = pack.Version, GameBuild = pack.GameBuild, TextureProfile = profile, Options = options, TargetActive = true, Files = Pack.EffectiveFiles(pack, id => options.GetValueOrDefault(id), profile) });
    }
    async Task RepairAsync()
    {
        RequireGame(); using var gameLock = LinuxInstaller.Lock(gameDir!);
        if (RecoveryTarget.Read(gameDir!, LinuxInstaller.PlanPathFor(gameDir!)) is not null) throw new InvalidOperationException("Finish the interrupted update before repairing.");
        var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!) ?? throw new InvalidOperationException("Install/update once to record this selection.");
        await ApplyTargetAsync(LinuxInstaller.RepairTarget(gameDir!, receipt));
    }
    async Task FinishAsync()
    {
        RequireGame(); using var gameLock = LinuxInstaller.Lock(gameDir!);
        var target = RecoveryTarget.Read(gameDir!, LinuxInstaller.PlanPathFor(gameDir!)) ?? throw new InvalidOperationException("No valid saved target. Use Install/update or Check & repair to rebuild unfinished files.");
        await ApplyTargetAsync(target);
    }
    async Task LaunchAsync()
    {
        RequireGame();
        if (Pack.HasPendingUpdate(gameDir, LinuxInstaller.PlanPathFor(gameDir!))) throw new InvalidOperationException("Finish the interrupted update first.");
        var stateOnDisk = ModFiles.GetState(gameDir);
        var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!);
        var desired = selected == LauncherMode.Helldivers ? false : true;
        var matches = receipt is not null && receipt.MatchesLayout(gameDir!) && stateOnDisk == (desired ? ModState.On : ModState.Off) &&
            (!desired || receipt.Options.GetValueOrDefault("empire") == (selected == LauncherMode.EmpireDivers) && receipt.TextureProfile == (pack is null ? settings.TextureProfile ?? receipt.TextureProfile : settings.ProfileFor(pack)) &&
            new[] { "droids", "covenant" }.All(id => !settings.Options.ContainsKey(id) || settings.Options[id] == receipt.Options.GetValueOrDefault(id)));
        if (!(selected == LauncherMode.Helldivers && stateOnDisk is ModState.Off or ModState.NoModFiles) && !matches) await ApplySelectedAsync();
        if (selected != LauncherMode.Helldivers) await CheckBuildAsync(InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!)?.GameBuild);
        using var gameLock = LinuxInstaller.Lock(gameDir!); LinuxInstaller.RequireClosed();
        FpvDefaults.EnsureForInstalledPack(gameDir); var client = LinuxPlatform.LaunchGame(gameDir!); message.Text = "Launch request sent to " + client + "."; LinuxSupport.Record("launch", "Desktop " + client + " launch requested.");
    }
    async Task SaveReportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save support report", SuggestedFileName = $"clonedivers-support-{DateTime.Now:yyyyMMdd-HHmmss}.zip", DefaultExtension = "zip", FileTypeChoices = new[] { new FilePickerFileType("ZIP report") { Patterns = new[] { "*.zip" } } } });
        if (file is null) return;
        var path = file.TryGetLocalPath() ?? throw new IOException("Choose a local report destination.");
        await Task.Run(() => LinuxSupport.SaveReport(gameDir, settings, path)); message.Text = "Report saved locally: " + path + ". Review before sharing; nothing was uploaded.";
    }
    async Task RecordingAsync()
    {
        if (gameDir is null) throw new InvalidOperationException("Choose the game folder first.");
        var line = LinuxRecording.Setup(gameDir, settings);
        var dialog = Dialog("Optional session recording", "Install MangoHud in Steam's environment. Paste the line below into the game's Steam Launch Options once. Keep existing environment assignments before the wrapper. Desktop sessions only; Snap Vulkan capture is experimental.");
        var panel = (StackPanel)dialog.Content!; var field = new TextBox { Text = line, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(field); var info = Text("", 12, Dim); panel.Children.Add(info);
        var row = new WrapPanel();
        foreach (var label in new[] { "Copy setup", "Record next session", "Cancel pending", "Show result", "Close" })
        {
            var button = ActionButton(label); button.Margin = new Thickness(0, 0, 6, 6); row.Children.Add(button);
            button.Click += async (_, _) =>
            {
                try
                {
                    if (label == "Close") { dialog.Close(); return; }
                    if (label == "Copy setup") { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(line); info.Text = "Launch-option line copied."; }
                    else if (label == "Record next session") { using var gate = LinuxInstaller.Lock(gameDir); var path = LinuxRecording.Arm(gameDir, settings); info.Text = "Next Steam launch is armed. Logs: " + path; }
                    else if (label == "Cancel pending") { using var gate = LinuxInstaller.Lock(gameDir); LinuxRecording.Cancel(gameDir); info.Text = "Pending capture cancelled. Saved recordings retained."; }
                    else { var latest = LinuxRecording.LatestSession(gameDir); info.Text = latest is null ? "No recorded session yet." : latest + "\n" + LinuxSupport.Tail(Path.Combine(latest, "status.txt"), 8192); }
                }
                catch (Exception ex) { info.Text = ex.Message; }
            };
        }
        panel.Children.Add(row); await dialog.ShowDialog(this);
    }
    async Task AddShortcutAsync()
    {
        var path = DesktopShortcut.Install(); message.Text = "Added to your Applications menu. Keep the launcher in its current folder.\n" + path;
        await Task.CompletedTask;
    }
    void RefreshRecordingSelection()
    {
        try { LinuxRecording.RefreshSelection(gameDir!, settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { LinuxSupport.Record("error", "Recording metadata: " + ex.Message); }
    }
    internal static Control Helmet(LauncherMode mode)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };
        void Shape(string geometry, string color) => canvas.Children.Add(new ShapePath { Data = Geometry.Parse(geometry), Fill = Brush.Parse(color) });
        if (mode == LauncherMode.EmpireDivers)
        {
            Shape("M14 40 C13 -1 87 -1 86 40 L90 51 87 65 94 81 81 93 66 94 58 88 42 88 34 94 19 93 6 81 13 65 10 51 Z", "#F0F3FA");
            Shape("M14 34 L32 31 68 31 86 34 87 40 67 37 33 37 13 40 Z M19 43 L31 40 46 46 42 55 25 53 Z M81 43 L69 40 54 46 58 55 75 53 Z M31 73 L42 61 50 57 58 61 69 73 59 69 50 64 41 69 Z", "#0B1020");
            Shape("M11 77 L20 71 30 76 35 87 29 90 20 86 Z M89 77 L80 71 70 76 65 87 71 90 80 86 Z", "#99A8BE");
        }
        else
        {
            Shape("M18 45 C15 0 85 0 82 45 L86 75 72 92 28 92 14 75 Z", "#F0F3FA");
            if (mode == LauncherMode.Clonedivers)
            {
                Shape("M45 11 H55 V35 H45 Z", "#3476E6");
                Shape("M22 40 L78 40 73 51 56 53 54 74 46 74 44 53 27 51 Z M39 82 H61 V86 H39 Z", "#0B1020");
            }
            else { Shape("M19 36 L81 36 76 56 61 64 39 64 24 56 Z M44 72 H56 V87 H44 Z", "#0B1020"); Shape("M22 39 L78 39 75 46 25 46 Z M59 13 H66 V32 H59 Z", "#FFC828"); }
        }
        return new Viewbox { Child = canvas, Width = 72, Height = 72 };
    }
}
