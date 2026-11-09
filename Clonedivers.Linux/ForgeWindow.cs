using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Automation;
using System.Diagnostics;

namespace Clonedivers;

internal sealed class ForgeWindow : Window
{
    static readonly IBrush Navy = Brush.Parse("#0B1020"), Dim = Brush.Parse("#96A2BE"), Blue = Brush.Parse("#3476E6"), Orange = Brush.Parse("#F48C34");
    readonly LinuxForgePreferences preferences;
    readonly string resources;
    readonly bool preview;
    readonly string previewState = "ready";
    LinuxForgePlayer? player;
    bool busy, closing, closed, updating, requestedAccountSync, requestedSetup;
    bool checkingLauncherUpdate, updateDeferred, updateFailed;
    ForgeAction primaryAction, retryAction = ForgeAction.Setup;
    readonly string? renderOutput;
    internal string PreviewName => previewState + "-" + preferences.Mode;
    LauncherUpdateStatus? launcherUpdate;
    string? lastError, failureDetail, reportPath, updateHeadline, updateDetail;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal readonly Dictionary<string, Button> Modes = new();
    readonly Dictionary<string, TextBlock> selectedModes = new();
    internal readonly Button Launch = Action("LAUNCH CLONEDIVERS"), Prepare = Action("Prepare Star Wars pack"), Import = Action("Apply HD2 import");
    readonly Button connect = Action("Connect"), browse = Action("Locate…"), refresh = Action("Refresh"), setup = Action("Setup & mods"), fallback = Action("Official HD2 in Steam"), updates = Action("Check updates"), report = Action("Save support report");
    readonly TextBox folder = new() { Watermark = "Separate HellForge installation inside its dedicated prefix" };
    readonly CheckBox delta = new() { Content = "Delta Squad armor" }, droids = new() { Content = "CIS droids" }, covenant = new() { Content = "Covenant squids" };
    readonly ToggleButton fullTextures = new() { Content = "Full", Width = 110 }, lighterTextures = new() { Content = "Lighter", Width = 110 };
    readonly StackPanel appearance = new() { Spacing = 6, MinHeight = 100 }, cloneChoices = new() { Spacing = 2 };
    readonly TextBlock appearanceLabel = Text("Clones appearance", 13, Dim), appearanceInfo = Text("", 13, Dim);
    readonly Button later = Action("Not now");
    readonly ProgressBar progress = new() { IsIndeterminate = true, Height = 6 };
    readonly TextBlock diagnostics = Text("", 12, Dim);
    readonly Expander moreOptions = new() { Header = "More options", HorizontalAlignment = HorizontalAlignment.Stretch }, sessionDetails = new() { Header = "Session details", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock state = Text("Connect your Forge installation", 18), detail = Text("", 13, Dim), account = Text("One-way account import: HD2 → Forge. Forge rewards stay local.", 12, Dim), message = Text("", 12, Dim);

    internal ForgeWindow(string[] args)
    {
        preview = args.Contains("--forge-preview", StringComparer.Ordinal);
        preferences = preview ? new() : LinuxForgePreferences.Load();
        resources = Path.Combine(AppContext.BaseDirectory, "forge");
        var previewWidth = 880; var previewHeight = 690;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--forge": case "--gui": break;
                case "--forge-root": preferences.InstallRoot = Value(args, ref i); break;
                case "--forge-resources": resources = Value(args, ref i); break;
                case "--forge-preview": previewState = Value(args, ref i); break;
                case "--forge-render":
                    if (!preview) throw new ArgumentException("Rendering requires preview mode.");
                    renderOutput = Path.GetFullPath(Value(args, ref i)); break;
                case "--forge-preview-mode":
                    if (!preview) throw new ArgumentException("Preview modes require preview mode.");
                    preferences.Mode = Value(args, ref i); preferences.Validate(); break;
                case "--forge-preview-size":
                    if (!preview) throw new ArgumentException("Preview sizes require preview mode.");
                    var dimensions = Value(args, ref i).Split('x');
                    if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out previewWidth) || !int.TryParse(dimensions[1], out previewHeight) ||
                        previewWidth is < 700 or > 2400 || previewHeight is < 600 or > 2000) throw new ArgumentException("Invalid preview size.");
                    break;
                default: throw new ArgumentException("Unknown Forge desktop option: " + args[i]);
            }
        if (preview && previewState is not ("ready" or "details" or "running" or "setup" or "error" or "sync" or "installing" or "disconnected" or "recovery" or "update-available" or "update-checking" or "update-failed")) throw new ArgumentException("Unknown preview state.");
        Title = "Clonedivers — HellForge"; Width = previewWidth; Height = previewHeight; MinWidth = 700; MinHeight = 600;
        Background = Navy; Foreground = Brushes.White; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28, 22), Spacing = 12 };
        panel.Children.Add(Text("CLONEDIVERS", 32, Brushes.White, FontWeight.Bold, HorizontalAlignment.Center));
        panel.Children.Add(Text("Choose your universe · HellForge for Linux", 13, Dim, alignment: HorizontalAlignment.Center));
        var cards = new UniformGrid { Columns = 3 };
        foreach (var (id, label, mode) in new[] { ("regular", "Regular", LauncherMode.Helldivers), ("clonedivers", "Clones", LauncherMode.Clonedivers), ("empire", "Empire", LauncherMode.EmpireDivers) })
        {
            var content = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(LauncherWindow.Helmet(mode)); content.Children.Add(Text(label, 18, weight: FontWeight.Bold));
            var selected = Text("", 10, Dim, alignment: HorizontalAlignment.Center); selected.MinHeight = 14; selectedModes[id] = selected; content.Children.Add(selected);
            var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(10, 12), Margin = new Thickness(4), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10) };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => Begin(async () => { preferences.Mode = id; await ChangedAsync(); }); Modes[id] = button; cards.Children.Add(button);
        }
        panel.Children.Add(cards);
        var choices = new WrapPanel { Orientation = Orientation.Horizontal, Children = { delta, droids, covenant } };
        foreach (var control in choices.Children) control.Margin = new Thickness(0, 0, 18, 0);
        var textures = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Children = { fullTextures, lighterTextures } };
        AutomationProperties.SetName(fullTextures, "Full textures"); AutomationProperties.SetName(lighterTextures, "Lighter textures");
        foreach (var segment in new[] { fullTextures, lighterTextures }) { segment.BorderThickness = new Thickness(1); segment.BorderBrush = Brush.Parse("#263047"); segment.CornerRadius = new CornerRadius(0); }
        ToolTip.SetTip(lighterTextures, "Full-detail textures streamed on demand.");
        var textureRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Text("Textures", 13, Dim), textures } };
        cloneChoices.Children.Add(choices); cloneChoices.Children.Add(textureRow);
        appearance.Children.Add(appearanceLabel); appearance.Children.Add(cloneChoices); appearance.Children.Add(appearanceInfo); panel.Children.Add(appearance);
        state.FontWeight = FontWeight.SemiBold; state.TextAlignment = detail.TextAlignment = TextAlignment.Center;
        panel.Children.Add(state); panel.Children.Add(detail);
        Launch.Background = Orange; Launch.Foreground = Navy; Launch.FontSize = 19; Launch.FontWeight = FontWeight.Bold; Launch.Height = 54;
        Launch.HorizontalAlignment = HorizontalAlignment.Stretch; Launch.HorizontalContentAlignment = HorizontalAlignment.Center; panel.Children.Add(Launch);
        panel.Children.Add(progress); later.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(later);
        folder.Text = preferences.InstallRoot ?? "";
        var path = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        path.Children.Add(folder); path.Children.Add(browse); path.Children.Add(connect); Grid.SetColumn(browse, 1); Grid.SetColumn(connect, 2);
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Children = { Prepare, Import, setup, refresh, fallback, updates, report } };
        foreach (var control in tools.Children) control.Margin = new Thickness(0, 0, 8, 8);
        var more = new StackPanel { Spacing = 10, Children = { path, tools } };
        var details = new StackPanel { Spacing = 8, Children = { account, message, diagnostics,
            Text("Linux gameplay, account capture and Windows/Linux co-op still need qualification. Regular HD2 opens through Steam. End Forge sessions before closing this launcher.", 12, Dim) } };
        sessionDetails.Content = details; more.Children.Add(sessionDetails); moreOptions.Content = more;
        moreOptions.Background = sessionDetails.Background = Navy;
        moreOptions.BorderBrush = sessionDetails.BorderBrush = Brush.Parse("#263047");
        if (preview && previewState == "details") moreOptions.IsExpanded = sessionDetails.IsExpanded = true;
        panel.Children.Add(moreOptions);
        panel.Children.Add(Text("v" + AppInfo.Version, 12, Dim, alignment: HorizontalAlignment.Center));
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        foreach (var box in new[] { delta, droids, covenant }) box.IsCheckedChanged += (_, _) => {
            if (updating) return;
            var newDelta = delta.IsChecked == true; var newDroids = droids.IsChecked == true; var newCovenant = covenant.IsChecked == true;
            Begin(async () => { preferences.DeltaSquad = newDelta; preferences.Droids = newDroids; preferences.Covenant = newCovenant; await ChangedAsync(); });
        };
        fullTextures.Click += (_, _) => SetTexture("full"); lighterTextures.Click += (_, _) => SetTexture("lighter");
        later.Click += (_, _) => { updateDeferred = true; updateFailed = false; UpdateView(); };
        connect.Click += (_, _) => { retryAction = ForgeAction.Connect; Begin(ConnectAsync); };
        browse.Click += (_, _) => Begin(BrowseAsync);
        refresh.Click += (_, _) => Begin(RefreshPlayerAsync);
        report.Click += (_, _) => Begin(() => {
            var output = LinuxSupport.SaveReport(null, new Settings(), forge: new LinuxSupport.ForgeReport(preferences.InstallRoot, preferences.Mode, player?.State, failureDetail ?? lastError ?? player?.Error));
            reportPath = output; moreOptions.IsExpanded = sessionDetails.IsExpanded = true; return Task.CompletedTask;
        });
        Import.Click += (_, _) => Begin(FinishAccountAsync);
        Prepare.Click += (_, _) => Begin(PrepareAsync);
        Launch.Click += (_, _) => {
            var action = primaryAction;
            retryAction = action is ForgeAction.Setup or ForgeAction.Connect ? action : ForgeAction.Refresh;
            Begin(async () => {
            switch (action)
            {
                case ForgeAction.Setup: await SetupAsync(); break;
                case ForgeAction.Connect: await ConnectAsync(); break;
                case ForgeAction.EndSession: await player!.EndSessionAsync(lifetime.Token); break;
                case ForgeAction.Launch: await player!.LaunchAsync(lifetime.Token); break;
                case ForgeAction.Manager: await player!.OpenManagerAsync(lifetime.Token); break;
                case ForgeAction.Prepare: await PrepareAsync(); break;
                case ForgeAction.Sync: await FinishAccountAsync(); break;
                case ForgeAction.Refresh: if (player == null) await ConnectAsync(); else await RefreshPlayerAsync(); break;
                case ForgeAction.Update:
                    try { LauncherUpdates.OpenRelease(launcherUpdate!); updateFailed = false; updateDeferred = true; }
                    catch { updateFailed = true; throw; }
                    break;
            }
            });
        };
        setup.Click += (_, _) => { retryAction = player == null ? ForgeAction.Setup : ForgeAction.Refresh; Begin(async () => { if (player != null) await player.OpenManagerAsync(lifetime.Token); else await SetupAsync(); }); };
        fallback.Click += (_, _) => Begin(() =>
        {
            var game = GameLocator.NormalizeGameDir(Settings.Load().GamePath) ?? GameLocator.AutoDetect()
                ?? throw new InvalidOperationException("Open your usual Steam client manually. No installed HD2 library was found, so Clonedivers will not choose another client.");
            message.Text = "Opened HD2 library in " + LinuxPlatform.OpenGameLibrary(game) + ".";
            return Task.CompletedTask;
        });
        updates.Click += async (_, _) => await CheckLauncherUpdateAsync(true);
        timer.Tick += (_, _) => { if (!busy && !closing && player is { Disconnected: false }) Begin(RefreshPlayerAsync); };
        Closing += (_, e) =>
        {
            if (preview) { closed = true; return; }
            if (closed) return;
            e.Cancel = true;
            if (busy || player?.SessionActive == true) { message.Text = "Finish the current operation and end the session in the launcher that started it before closing."; moreOptions.IsExpanded = sessionDetails.IsExpanded = true; return; }
            if (closing) return;
            closing = true; timer.Stop();
            Begin(async () => { if (player != null) await player.DisposeAsync(); closed = true; lifetime.Cancel(); lifetime.Dispose(); Close(); });
        };
        Opened += async (_, _) => {
            UpdateView();
            if (renderOutput != null) { await GuiSmoke.RenderForgeAsync(this, renderOutput); return; }
            if (!preview) {
                preferences.InstallRoot = LinuxForgeBootstrap.StartupRoot(preferences.InstallRoot); folder.Text = preferences.InstallRoot ?? "";
                if (preferences.InstallRoot != null) { retryAction = ForgeAction.Connect; await RunAsync(ConnectAsync); }
                timer.Start(); await CheckLauncherUpdateAsync(false);
            }
        };
        UpdateView();
    }

    void SetTexture(string texture)
    {
        if (updating || busy || preview) { UpdateView(); return; }
        Begin(async () => { preferences.Texture = texture; await ChangedAsync(); });
    }

    static string Value(string[] args, ref int i) => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
    async Task CheckLauncherUpdateAsync(bool open)
    {
        if (preview || checkingLauncherUpdate || busy || closed) return;
        updateHeadline = updateDetail = null;
        checkingLauncherUpdate = true; UpdateView();
        try
        {
            launcherUpdate = await new LauncherUpdates().CheckAsync(lifetime.Token, force: open);
            if (closed) return;
            updates.Content = launcherUpdate.Available == null ? "Check updates" : "View launcher update";
            if (open) {
                updateDeferred = false; updateFailed = false; message.Text = launcherUpdate.Message;
                if (launcherUpdate.Available == null) {
                    updateHeadline = launcherUpdate.CheckSucceeded ? "Launcher is up to date" : "Couldn't check updates";
                    updateDetail = launcherUpdate.CheckSucceeded ? "You're using v" + AppInfo.Version + "." : "Try Check updates in More options in a few minutes.";
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (open && !closed) { failureDetail = error.ToString(); message.Text = "Launcher update check unavailable. Try again later."; } }
        finally { checkingLauncherUpdate = false; if (!closed) UpdateView(); }
    }
    static Button Action(string title) => new() { Content = title, Padding = new Thickness(14, 9), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    static TextBlock Text(string value, double size, IBrush? color = null, FontWeight? weight = null, HorizontalAlignment alignment = HorizontalAlignment.Stretch) => new() { Text = value, FontSize = size, Foreground = color ?? Brushes.White, FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = alignment };
    internal void Begin(Func<Task> action)
    {
        if (busy || preview) return;
        Operation = RunAsync(action);
    }
    async Task RunAsync(Func<Task> action)
    {
        busy = true; lastError = null; updateHeadline = updateDetail = null; UpdateView();
        try { await action(); }
        catch (Exception error) { lastError = error.Message; failureDetail = error.ToString(); if (closing) { closing = false; timer.Start(); } }
        finally { busy = false; if (!closed) UpdateView(); }
    }
    async Task ChangedAsync()
    {
        if (preview) return;
        preferences.Save(); if (player != null) await player.ApplySelectionAsync(lifetime.Token);
    }
    async Task ConnectAsync()
    {
        if (player is { Disconnected: true }) { var lost = player; player = null; await lost.DisposeAsync(); } // closing stdin lets the bridge finish its own cleanup
        if (player != null) throw new InvalidOperationException("Close this launcher before switching its active Forge installation.");
        var root = folder.Text?.Trim(); preferences.InstallRoot = string.IsNullOrEmpty(root) ? null : root;
        if (LinuxForgeBootstrap.ExistingRoot(root) != null) preferences.Save(); // a blank or wrong folder is never persisted over discovery
        message.Text = "Starting the official isolated Forge connection…";
        player = await LinuxForgePlayer.ConnectAsync(preferences, resources, lifetime.Token);
    }
    async Task SetupAsync()
    {
        if (player?.SessionActive == true) throw new InvalidOperationException("End the Forge session before setup.");
        if (player != null) { await player.DisposeAsync(); player = null; }
        var result = await LinuxForgeBootstrap.InstallAsync(preferences.InstallRoot, new Progress<string>(text => message.Text = text), lifetime.Token);
        preferences.InstallRoot = result; preferences.Save(); folder.Text = result;
        await ConnectAsync();
        requestedSetup = true;
        if (player?.State?.Status is { GameCopyReady: true, RunnerReady: true, CompatibilityProblem: null }) { requestedSetup = false; await PrepareAsync(); }
    }
    async Task RefreshPlayerAsync()
    {
        if (player == null) return;
        await player.RefreshAsync(lifetime.Token);
        if (requestedSetup && !player.SessionActive && player.State?.Status is { GameCopyReady: true, RunnerReady: true, CompatibilityProblem: null })
        { requestedSetup = false; await PrepareAsync(); }
        if (requestedAccountSync && !player.SessionActive && !Game.IsRunning() && player.State?.Account is { Captured: true, Imported: false })
        { requestedAccountSync = false; await player.ImportAsync(lifetime.Token); }
    }
    async Task FinishAccountAsync()
    {
        if (player == null) return;
        if (player.State?.Account is { Captured: true, Imported: false }) { requestedAccountSync = false; await player.ImportAsync(lifetime.Token); }
        else if (player.State?.Account?.Imported != true)
        {
            await player.BeginAccountSyncAsync(lifetime.Token); requestedAccountSync = true;
            message.Text = "In official Forge, open Profile > Account sync > Sync Inventory. Default is selected. Approve the Linux helper yourself and pass HD2’s title screen. Keep Clonedivers open; it finishes the verified import after cleanup.";
            moreOptions.IsExpanded = sessionDetails.IsExpanded = true;
        }
    }
    async Task BrowseAsync()
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Separate HellForge installation inside the prefix C: drive", AllowMultiple = false });
        if (selected.Count == 1) folder.Text = selected[0].TryGetLocalPath();
    }
    async Task PrepareAsync()
    {
        if (player == null) return;
        if (player.State?.Status.RecoveryProblem != null)
        {
            if (player.State.Status.RecoveryProblem.StartsWith("Gameplay", StringComparison.Ordinal)) await player.RecoverGameplayAsync(lifetime.Token);
            else await player.RecoverAppearanceAsync(lifetime.Token);
            return;
        }
        var progress = new Progress<string>(text => message.Text = text);
        await player.InstallGameplayAsync(progress, lifetime.Token);
        if (preferences.Mode == "regular") { await player.PrepareAppearanceAsync(null, false, ct: lifetime.Token); await FinishAccountAsync(); return; }
        var plan = await Task.Run(() => player.PlanAppearanceAsync(progress, lifetime.Token));
        if (plan.Missing.Count > 0 && !await ConfirmAsync("Prepare Star Wars appearance?", $"Download {Pack.FormatBytes(plan.Missing.Sum(f => f.Size))} of verified appearance assets? Existing local files are reused. Required gameplay mods were handled by this setup flow.")) return;
        await Task.Run(() => player.PrepareAppearanceAsync(plan, true, progress, lifetime.Token));
        await FinishAccountAsync();
    }
    async Task<bool> ConfirmAsync(string title, string text)
    {
        var dialog = new Window { Title = title, Width = 540, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Navy, Foreground = Brushes.White };
        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 16 }; panel.Children.Add(Text(text, 14));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Action("Cancel"); var apply = Action("Continue"); cancel.Click += (_, _) => dialog.Close(false); apply.Click += (_, _) => dialog.Close(true);
        row.Children.Add(cancel); row.Children.Add(apply); panel.Children.Add(row); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
    void UpdateView()
    {
        updating = true;
        delta.IsChecked = preferences.DeltaSquad; droids.IsChecked = preferences.Droids; covenant.IsChecked = preferences.Covenant;
        fullTextures.IsChecked = preferences.Texture != "lighter"; lighterTextures.IsChecked = preferences.Texture == "lighter";
        var active = preview ? previewState == "running" : player?.SessionActive == true;
        var lost = preview ? previewState == "disconnected" : player?.Disconnected == true;
        var working = busy || preview && previewState == "installing";
        var checking = checkingLauncherUpdate || preview && previewState == "update-checking";
        updates.IsEnabled = !preview && !busy && !active && !checking;
        foreach (var (id, button) in Modes) { button.Background = Brush.Parse(id == preferences.Mode ? "#1D3457" : "#111A2D"); button.BorderBrush = id == preferences.Mode ? Blue : Brush.Parse("#263047"); button.IsEnabled = !working && !active && !checking; selectedModes[id].Text = id == preferences.Mode ? "SELECTED" : ""; }
        foreach (var control in new Control[] { delta, droids, covenant, fullTextures, lighterTextures }) control.IsEnabled = !working && !active && !checking;
        foreach (var segment in new[] { fullTextures, lighterTextures }) segment.Background = segment.IsChecked == true ? Brush.Parse("#1D3457") : Navy;
        cloneChoices.IsVisible = preferences.Mode == "clonedivers"; appearanceInfo.IsVisible = !cloneChoices.IsVisible;
        appearanceLabel.Text = preferences.Mode == "clonedivers" ? "Clones appearance" : preferences.Mode == "empire" ? "Empire appearance" : "Regular HellForge";
        appearanceInfo.Text = preferences.Mode == "empire" ? "Empire r45-v4 · Full textures · CIS droids included" : "Original HD2 appearance with the required Forge gameplay mods.";
        folder.IsEnabled = browse.IsEnabled = !busy && player == null && !preview;
        connect.IsEnabled = !busy && (player == null || lost) && !preview; refresh.IsEnabled = !busy && player != null && !lost && !preview;
        Prepare.IsEnabled = !busy && !active && !lost && player != null && player.State?.Status.CompatibilityProblem == null && !preview;
        Prepare.Content = "Set up selected mode";
        if (player?.State?.Status.RecoveryProblem is { } recovery) Prepare.Content = recovery.StartsWith("Gameplay", StringComparison.Ordinal) ? "Recover gameplay preset" : "Recover previous appearance";
        Import.IsEnabled = !busy && !active && !lost && player?.State?.Status is { GameCopyReady: true, RunnerReady: true, RecoveryProblem: null, CompatibilityProblem: null } && !preview;
        Import.Content = player?.State?.Account is { Captured: true, Imported: false } ? "Finish account sync" : "Sync HD2 account";
        var version = !updateDeferred ? launcherUpdate?.Available?.Version : null;
        if (preview && previewState is "update-available" or "update-failed") version = "0.9.0";
        var viewState = player?.State;
        if (preview && previewState is "sync" or "recovery") viewState = new(new("/home/player/forge/pfx/drive_c/HellForge", "0.4.0-beta", "25480438", true, true, false, true, true, null, null,
            RecoveryProblem: previewState == "recovery" ? "Gameplay installation interrupted" : null), false, "", new(false, false, ""), null);
        var view = ForgePresentation.Resolve(viewState, connected: preview ? previewState != "setup" : player != null,
            disconnected: lost, active: active, ownsSession: preview ? active : player?.OwnsSession == true,
            canLaunch: preview ? previewState is "ready" or "details" : player?.CanLaunch == true, busy: working,
            error: lastError ?? player?.Error ?? (preview && previewState == "error" ? "HttpRequestException: 504 /internal/diagnostic" : null),
            updateVersion: version, updateFailed: updateFailed || preview && previewState == "update-failed", syncing: requestedAccountSync, retryAction: player == null ? retryAction : ForgeAction.Refresh);
        primaryAction = view.Action; state.Text = view.Headline; detail.Text = view.Detail; Launch.Content = view.Button;
        if (!working && !active && lastError == null && updateHeadline != null) { state.Text = updateHeadline; detail.Text = updateDetail; }
        if (checking) { state.Text = "Checking launcher updates…"; detail.Text = "Looking for a compatible Linux package."; Launch.Content = "Checking…"; }
        Launch.IsEnabled = !working && !checking && view.Action != ForgeAction.None;
        AutomationProperties.SetName(Launch, Launch.Content?.ToString());
        progress.IsVisible = working || checking;
        later.IsVisible = version != null && !active && !working && !checking;
        setup.IsEnabled = fallback.IsEnabled = !busy && !active && !preview;
        report.IsEnabled = !busy && !preview;
        account.Text = player?.State?.Account?.Message ?? "One-way account import: HD2 → Forge. Use only a capture the official backend verifies for this PC/account.";
        diagnostics.Text = "Installation: " + (preferences.InstallRoot ?? "Not connected") + "\n" + (failureDetail ?? lastError ?? player?.Error ?? player?.State?.Detail ?? "No session diagnostics.");
        if (reportPath != null) diagnostics.Text += "\nSupport report saved: " + reportPath;
        updating = false;
    }
}
