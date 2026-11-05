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
    bool busy, closing, closed, updating;
    bool checkingLauncherUpdate;
    LauncherUpdateStatus? launcherUpdate;
    string? lastError, reportPath;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal readonly Dictionary<string, Button> Modes = new();
    internal readonly Button Launch = Action("LAUNCH CLONEDIVERS"), Prepare = Action("Prepare Star Wars pack"), Import = Action("Apply HD2 import");
    readonly Button connect = Action("Connect"), browse = Action("Locate…"), refresh = Action("Refresh"), setup = Action("Setup & mods"), fallback = Action("Official HD2 in Steam"), updates = Action("Check updates"), report = Action("Save support report");
    readonly TextBox folder = new() { Watermark = "Separate HellForge installation inside its dedicated prefix" };
    readonly CheckBox delta = new() { Content = "Delta Squad armor" }, droids = new() { Content = "CIS droids" }, covenant = new() { Content = "Covenant squids" };
    readonly ComboBox textures = new() { ItemsSource = new[] { "Full textures", "Lighter textures" }, Width = 170 };
    readonly TextBlock state = Text("Connect your Forge installation", 18), detail = Text("", 13, Dim), account = Text("One-way account import: HD2 → Forge. Forge rewards stay local.", 12, Dim), message = Text("", 12, Dim);

    internal ForgeWindow(string[] args)
    {
        preview = args.Contains("--forge-preview", StringComparer.Ordinal);
        preferences = preview ? new() : LinuxForgePreferences.Load();
        resources = Path.Combine(AppContext.BaseDirectory, "forge");
        var previewWidth = 940; var previewHeight = 900;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--forge": case "--gui": break;
                case "--forge-root": preferences.InstallRoot = Value(args, ref i); break;
                case "--forge-resources": resources = Value(args, ref i); break;
                case "--forge-preview": previewState = Value(args, ref i); break;
                case "--forge-preview-size":
                    if (!preview) throw new ArgumentException("Preview sizes require preview mode.");
                    var dimensions = Value(args, ref i).Split('x');
                    if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out previewWidth) || !int.TryParse(dimensions[1], out previewHeight) ||
                        previewWidth is < 760 or > 2400 || previewHeight is < 740 or > 2000) throw new ArgumentException("Invalid preview size.");
                    break;
                default: throw new ArgumentException("Unknown Forge desktop option: " + args[i]);
            }
        if (preview && previewState is not ("ready" or "running" or "setup" or "error")) throw new ArgumentException("Unknown preview state.");
        Title = "Clonedivers — HellForge"; Width = previewWidth; Height = previewHeight; MinWidth = 760; MinHeight = 740;
        Background = Navy; Foreground = Brushes.White; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28, 22), Spacing = 12 };
        panel.Children.Add(Text("CLONEDIVERS", 32, Brushes.White, FontWeight.Bold, HorizontalAlignment.Center));
        panel.Children.Add(Text("Choose your universe · HellForge for Linux", 13, Dim, alignment: HorizontalAlignment.Center));
        var cards = new UniformGrid { Columns = 3 };
        foreach (var (id, label, mode) in new[] { ("regular", "Regular", LauncherMode.Helldivers), ("clonedivers", "Clones", LauncherMode.Clonedivers), ("empire", "Empire", LauncherMode.EmpireDivers) })
        {
            var content = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(LauncherWindow.Helmet(mode)); content.Children.Add(Text(label, 18, weight: FontWeight.Bold));
            var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(10, 18), Margin = new Thickness(4), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10) };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => Begin(async () => { preferences.Mode = id; await ChangedAsync(); }); Modes[id] = button; cards.Children.Add(button);
        }
        panel.Children.Add(cards); panel.Children.Add(state); panel.Children.Add(detail);
        Launch.Background = Orange; Launch.Foreground = Navy; Launch.FontSize = 19; Launch.FontWeight = FontWeight.Bold; Launch.Height = 54;
        Launch.HorizontalAlignment = HorizontalAlignment.Stretch; Launch.HorizontalContentAlignment = HorizontalAlignment.Center; panel.Children.Add(Launch);
        var primary = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 }; primary.Children.Add(Prepare); primary.Children.Add(Import); Grid.SetColumn(Import, 1); panel.Children.Add(primary);
        var choices = new WrapPanel { Orientation = Orientation.Horizontal, Children = { delta, droids, covenant, textures } };
        foreach (var control in choices.Children) control.Margin = new Thickness(0, 0, 14, 0); panel.Children.Add(choices);
        panel.Children.Add(account); panel.Children.Add(message);
        folder.Text = preferences.InstallRoot ?? "";
        var path = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        path.Children.Add(folder); path.Children.Add(browse); path.Children.Add(connect); Grid.SetColumn(browse, 1); Grid.SetColumn(connect, 2); panel.Children.Add(path);
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Children = { setup, refresh, fallback, updates, report } };
        foreach (var control in tools.Children) control.Margin = new Thickness(0, 0, 8, 0); panel.Children.Add(tools);
        panel.Children.Add(Text($"Forge launcher {AppInfo.Version} · Linux gameplay and Windows/Linux co-op still need testing.", 12, Dim));
        panel.Children.Add(Text("Regular HD2: open through Steam. End your Forge session before closing this launcher.", 11, Dim));
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        foreach (var box in new[] { delta, droids, covenant }) box.IsCheckedChanged += (_, _) => { if (!updating) Begin(async () => { preferences.DeltaSquad = delta.IsChecked == true; preferences.Droids = droids.IsChecked == true; preferences.Covenant = covenant.IsChecked == true; await ChangedAsync(); }); };
        textures.SelectionChanged += (_, _) => { if (!updating) Begin(async () => { preferences.Texture = textures.SelectedIndex == 1 ? "lighter" : "full"; await ChangedAsync(); }); };
        connect.Click += (_, _) => Begin(ConnectAsync);
        browse.Click += (_, _) => Begin(BrowseAsync);
        refresh.Click += (_, _) => Begin(async () => { if (player != null) await player.RefreshAsync(lifetime.Token); });
        report.Click += (_, _) => Begin(() => {
            var output = LinuxSupport.SaveReport(null, new Settings(), forge: new LinuxSupport.ForgeReport(preferences.InstallRoot, preferences.Mode, player?.State, lastError ?? player?.Error));
            reportPath = output; return Task.CompletedTask;
        });
        Import.Click += (_, _) => Begin(async () => { if (player != null && await ConfirmAsync("Apply the verified HD2 account capture?", "This imports your own sealed capture into Forge using its one-way merge policy. Forge rewards stay local; existing characters are preserved.")) await player.ImportAsync(lifetime.Token); });
        Prepare.Click += (_, _) => Begin(PrepareAsync);
        Launch.Click += (_, _) => Begin(async () => { if (player == null) return; if (player.OwnsSession) await player.EndSessionAsync(lifetime.Token); else await player.LaunchAsync(lifetime.Token); });
        setup.Click += (_, _) => Begin(async () => { if (player != null) await player.OpenManagerAsync(lifetime.Token); else Process.Start(new ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { "https://github.com/SkyeShade/HellForge/releases/tag/v0.3.0-beta" } }); });
        fallback.Click += (_, _) => Begin(() =>
        {
            var game = GameLocator.NormalizeGameDir(Settings.Load().GamePath) ?? GameLocator.AutoDetect()
                ?? throw new InvalidOperationException("Open your usual Steam client manually. No installed HD2 library was found, so Clonedivers will not choose another client.");
            message.Text = "Opened HD2 library in " + LinuxPlatform.OpenGameLibrary(game) + ".";
            return Task.CompletedTask;
        });
        updates.Click += async (_, _) => await CheckLauncherUpdateAsync(true);
        timer.Tick += (_, _) => { if (!busy && !closing && player != null) Begin(() => player.RefreshAsync(lifetime.Token)); };
        Closing += (_, e) =>
        {
            if (preview) { closed = true; return; }
            if (closed) return;
            e.Cancel = true;
            if (busy || player?.SessionActive == true) { message.Text = "Finish the current operation and end the session in the launcher that started it before closing."; return; }
            if (closing) return;
            closing = true; timer.Stop();
            Begin(async () => { if (player != null) await player.DisposeAsync(); closed = true; lifetime.Cancel(); lifetime.Dispose(); Close(); });
        };
        Opened += async (_, _) => { UpdateView(); if (!preview) { timer.Start(); await CheckLauncherUpdateAsync(false); } };
        UpdateView();
    }

    static string Value(string[] args, ref int i) => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
    async Task CheckLauncherUpdateAsync(bool open)
    {
        if (preview || checkingLauncherUpdate || busy || closed) return;
        checkingLauncherUpdate = true; UpdateView();
        try
        {
            launcherUpdate = await new LauncherUpdates().CheckAsync(lifetime.Token, force: open);
            if (closed) return;
            updates.Content = launcherUpdate.Available == null ? "Check updates" : "Update v" + launcherUpdate.Available.Version;
            if (open) { message.Text = launcherUpdate.Message; if (launcherUpdate.Available != null) LauncherUpdates.OpenRelease(launcherUpdate); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (open && !closed) message.Text = "Launcher update check unavailable: " + error.Message; }
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
        busy = true; lastError = null; UpdateView();
        try { await action(); message.Text = ""; }
        catch (Exception error) { lastError = error.Message; message.Text = error.Message; if (closing) { closing = false; timer.Start(); } }
        finally { busy = false; if (!closed) UpdateView(); }
    }
    async Task ChangedAsync()
    {
        if (preview) return;
        preferences.Save(); if (player != null) await player.ApplySelectionAsync(lifetime.Token);
    }
    async Task ConnectAsync()
    {
        if (player != null) throw new InvalidOperationException("Close this launcher before switching its active Forge installation.");
        preferences.InstallRoot = folder.Text?.Trim(); preferences.Save(); message.Text = "Starting the official isolated Forge connection…";
        player = await LinuxForgePlayer.ConnectAsync(preferences, resources, lifetime.Token);
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
        if (preferences.Mode == "regular") { await player.PrepareAppearanceAsync(null, false, ct: lifetime.Token); return; }
        var plan = await Task.Run(() => player.PlanAppearanceAsync(progress, lifetime.Token));
        if (plan.Missing.Count > 0 && !await ConfirmAsync("Prepare Star Wars appearance?", $"Download {Pack.FormatBytes(plan.Missing.Sum(f => f.Size))} of verified appearance assets? Existing local files are reused. Required gameplay mods were handled by this setup flow.")) return;
        await Task.Run(() => player.PrepareAppearanceAsync(plan, true, progress, lifetime.Token));
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
        delta.IsChecked = preferences.DeltaSquad; droids.IsChecked = preferences.Droids; covenant.IsChecked = preferences.Covenant; textures.SelectedIndex = preferences.Texture == "lighter" ? 1 : 0;
        var active = preview ? previewState == "running" : player?.SessionActive == true;
        updates.IsEnabled = !preview && !busy && !active && !checkingLauncherUpdate;
        foreach (var (id, button) in Modes) { button.Background = Brush.Parse(id == preferences.Mode ? "#1D3457" : "#111A2D"); button.BorderBrush = id == preferences.Mode ? Blue : Brush.Parse("#263047"); button.IsEnabled = !busy && !active; }
        foreach (var control in new Control[] { delta, droids, covenant, textures }) { control.IsEnabled = !busy && !active; control.IsVisible = preferences.Mode != "regular"; }
        delta.IsVisible = preferences.Mode == "clonedivers";
        folder.IsEnabled = browse.IsEnabled = !busy && player == null && !preview;
        connect.IsEnabled = !busy && player == null && !preview; refresh.IsEnabled = !busy && player != null && !preview;
        Prepare.IsEnabled = !busy && !active && player != null && player.State?.Status.CompatibilityProblem == null && !preview;
        Prepare.Content = "Set up selected mode";
        if (player?.State?.Status.RecoveryProblem is { } recovery) Prepare.Content = recovery.StartsWith("Gameplay", StringComparison.Ordinal) ? "Recover gameplay preset" : "Recover previous appearance";
        Import.IsEnabled = !busy && !active && player?.State?.Status.RecoveryProblem == null && player?.State?.Status.CompatibilityProblem == null && player?.State?.Account is { Captured: true, Imported: false } && !preview;
        Launch.Content = (player?.OwnsSession == true || preview && active) ? "END SESSION" : preferences.Mode == "regular" ? "LAUNCH REGULAR HELLFORGE" : preferences.Mode == "empire" ? "LAUNCH EMPIREDIVERS" : "LAUNCH CLONEDIVERS";
        Launch.IsEnabled = !busy && (preview ? previewState is "ready" or "running" : player?.OwnsSession == true || player?.CanLaunch == true && lastError == null);
        setup.IsEnabled = fallback.IsEnabled = !busy && !active && !preview;
        report.IsEnabled = !busy && !preview;
        state.Text = busy ? "Working…" : active ? "Forge session running" : lastError != null || preview && previewState == "error" ? "Needs attention" : preview && previewState == "ready" || player?.CanLaunch == true ? "Ready for a private trial" : player == null ? "Connect your Forge installation" : "Finish Forge setup";
        detail.Text = preview ? active ? "Use End session to close the game and wait for cleanup." : previewState == "error" ? "The gameplay preset is incomplete. Open Setup & mods before launching." : "Selected appearance is checked before launch. Choose difficulty 1–11 at the war table." :
            lastError ?? player?.Error ?? player?.State?.Detail ?? "Choose your separate HellForge installation. Steam ownership and official network isolation remain required.";
        account.Text = player?.State?.Account?.Message ?? "One-way account import: HD2 → Forge. Linux capture still needs qualification; use only a capture the official backend verifies for this PC/account.";
        if (reportPath != null) detail.Text += "\nSupport report saved: " + reportPath;
        updating = false;
    }
}
