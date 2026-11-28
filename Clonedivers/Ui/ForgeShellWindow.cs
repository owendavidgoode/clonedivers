using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Clonedivers.Ui;

using Application = Avalonia.Application;
using Brushes = Avalonia.Media.Brushes;
using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;
using Control = Avalonia.Controls.Control;
using Cursor = Avalonia.Input.Cursor;
using FontFamily = Avalonia.Media.FontFamily;
using Grid = Avalonia.Controls.Grid;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using Image = Avalonia.Controls.Image;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using Orientation = Avalonia.Layout.Orientation;
using ProgressBar = Avalonia.Controls.ProgressBar;
using Size = Avalonia.Size;
using TextBox = Avalonia.Controls.TextBox;
using ToolTip = Avalonia.Controls.ToolTip;

/// <summary>Unified modern Star Wars holoterminal launcher window shared across Windows and Linux.</summary>
public sealed class ForgeShellWindow : Window
{
    public static IReadOnlyDictionary<string, string> CustomizeTierDescriptions { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [PerformanceTierAdvisor.Ultra] = "Full (Ultra)",
        [PerformanceTierAdvisor.High] = "High (Streamed)",
        [PerformanceTierAdvisor.Balanced] = "1024px (Medium)",
        [PerformanceTierAdvisor.Low512] = "512px (Low / Deck)",
        [PerformanceTierAdvisor.UltraPotato] = "256px + Decimated Meshes (Ultra-Potato)"
    };

    readonly IForgeUiHost host;
    bool showingSetup;
    bool showingCustomize;
    bool showingAdvanced;
    bool showingSettings;
    bool showingWhatsNew;
    bool closingConfirmed;
    bool updatingControls;
    bool wasSessionRunning;
    bool wasGameWindowActive;
    bool minimizeToTrayActive;
    string? hoveredModeId;
    TaskCompletionSource<bool>? pendingConfirm;
    WriteableBitmap? currentQrBitmap;
    string[]? lastQrRows;
    TrayIcon? trayIcon;
    NativeMenuItem? trayEndSessionItem;

    // Header & contextual non-blocking banners
    readonly TextBlock headerSubtitle = ForgeTheme.Label("HellForge Edition", 12, ForgeTheme.TextSecondary);
    readonly Border headerStatusBadge = new() { CornerRadius = new CornerRadius(999), Padding = new Thickness(11, 4), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock headerStatusText = ForgeTheme.Label("Ready", 11.5, ForgeTheme.SuccessText, FontWeight.SemiBold, wrap: false);
    readonly Button muteButton = ForgeTheme.GhostButton("🔊", 12.5, new Thickness(10, 6));
    readonly Button settingsButton = ForgeTheme.GhostButton("Settings", 12, new Thickness(13, 7));

    readonly Border updateBanner = new()
    {
        IsVisible = false,
        Background = ForgeTheme.InfoBg,
        BorderBrush = ForgeTheme.InfoBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 8),
        Margin = new Thickness(28, 0, 28, 8)
    };
    readonly TextBlock updateBannerText = ForgeTheme.Label("", 12.5, ForgeTheme.TextPrimary, FontWeight.Medium);
    readonly Button updateBannerWhatsNew = ForgeTheme.LinkStyleButton("What's New", ForgeTheme.InfoText, 12);
    readonly Button updateBannerAction = ForgeTheme.GhostButton("Update & Restart", 12, new Thickness(12, 5));
    readonly Button updateBannerDismiss = ForgeTheme.LinkStyleButton("Later", ForgeTheme.TextSecondary, 12);

    readonly Border stepDownBanner = new()
    {
        IsVisible = false,
        Background = ForgeTheme.WarningBg,
        BorderBrush = ForgeTheme.WarningBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 8),
        Margin = new Thickness(28, 0, 28, 8)
    };
    readonly TextBlock stepDownBannerText = ForgeTheme.Label("", 12, ForgeTheme.WarningText, FontWeight.Medium);
    readonly Button stepDownAcceptButton = ForgeTheme.GhostButton("Drop Tier", 12, new Thickness(12, 5));
    readonly Button stepDownDismissButton = ForgeTheme.LinkStyleButton("Dismiss", ForgeTheme.TextSecondary, 12);

    readonly Border selfInstallBanner = new()
    {
        IsVisible = false,
        Background = ForgeTheme.BgElevated,
        BorderBrush = ForgeTheme.InfoBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 8),
        Margin = new Thickness(28, 0, 28, 8)
    };
    readonly TextBlock selfInstallBannerText = ForgeTheme.Label("", 12, ForgeTheme.TextPrimary, FontWeight.Medium);
    readonly Button selfInstallAcceptButton = ForgeTheme.GhostButton("Install Shortcuts", 12, new Thickness(12, 5));
    readonly Button selfInstallDismissButton = ForgeTheme.LinkStyleButton("Later", ForgeTheme.TextSecondary, 12);

    // Floating visible UI unhandled-exception error toast (QA Bugfix #4)
    readonly Border unhandledErrorBanner = new()
    {
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(28, 12, 28, 0),
        Background = ForgeTheme.WarningBg,
        BorderBrush = ForgeTheme.Warning,
        BorderThickness = new Thickness(1.5),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 9)
    };
    readonly TextBlock unhandledErrorText = ForgeTheme.Label("", 12.5, ForgeTheme.WarningText, FontWeight.SemiBold);

    // Main content swap
    readonly Control homeView;
    readonly Control setupView;

    // Home view controls (ultra-clean 7 top-level elements)
    readonly Dictionary<string, Border> modeCards = new();
    readonly Dictionary<string, Border> modeTopBars = new();
    readonly Dictionary<string, Button> modeButtons = new();
    readonly Dictionary<string, Border> modeBadges = new();
    readonly Dictionary<string, TextBlock> modeBadgeTexts = new();

    readonly Border customizeStrip = new()
    {
        Background = ForgeTheme.BgSurface,
        BorderBrush = ForgeTheme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16, 10)
    };
    readonly TextBlock customizeSummaryTitle = ForgeTheme.Label("CLONES LOADOUT", 11, ForgeTheme.TextMuted, FontWeight.Bold);
    readonly TextBlock customizeSummaryText = ForgeTheme.Label("", 13, ForgeTheme.TextPrimary, FontWeight.Medium);
    readonly Border customizeSizePill = new() { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock customizeSizePillText = ForgeTheme.Label("", 11.5, ForgeTheme.SuccessText, FontWeight.SemiBold, wrap: false);
    readonly Button customizeOpenButton = ForgeTheme.GhostButton("Customize ▸", 12.5, new Thickness(14, 7));

    // Fixed-height Status & Progress Deck (never collapses!)
    readonly Border statusDeck = new()
    {
        Background = ForgeTheme.BgSurface,
        BorderBrush = ForgeTheme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(18, 13),
        MinHeight = 92
    };
    readonly Border statusDeckTopLine = new() { Height = 2, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 0, 8), Background = ForgeTheme.ClonesBarMuted };
    readonly Border statusDot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock statusHeadline = ForgeTheme.Label("Checking HellForge…", 16, ForgeTheme.TextPrimary, FontWeight.Bold);
    readonly TextBlock statusDetail = ForgeTheme.Label("", 12.5, ForgeTheme.TextSecondary);
    readonly TextBlock statusMetrics = ForgeTheme.Label("", 12, ForgeTheme.ClonesAccent, FontWeight.SemiBold, TextAlignment.Right, wrap: false);
    readonly ProgressBar homeProgress = new()
    {
        Height = 7,
        Minimum = 0,
        Maximum = 100,
        CornerRadius = new CornerRadius(4),
        Background = ForgeTheme.BgTrack,
        Foreground = ForgeTheme.ClonesAccent,
        IsVisible = false
    };
    readonly TextBlock toastText = ForgeTheme.Label("", 11.5, ForgeTheme.WarningText, FontWeight.Medium);

    readonly Button heroButton = ForgeTheme.PrimaryButton("LAUNCH CLONEDIVERS", 52, 16);
    readonly Button secondaryButton = ForgeTheme.GhostButton("Locate Existing…", 12.5, new Thickness(16, 12));

    // Footer
    readonly Button steamFooterLink = ForgeTheme.LinkStyleButton("Official online game: Open Helldivers 2 in Steam", ForgeTheme.TextSecondary, 11.5);
    readonly TextBlock versionFooter = ForgeTheme.Label("", 11.5, ForgeTheme.TextMuted, wrap: false);

    // Radically Simplified Customize Slide-Over Drawer
    readonly Grid customizeOverlay = new() { IsVisible = false, Background = ForgeTheme.BgOverlay };
    ScrollViewer? drawerScrollViewer;
    readonly TextBlock drawerTitle = ForgeTheme.Label("Customize Clones", 18, ForgeTheme.TextPrimary, FontWeight.Bold);
    readonly TextBlock drawerSubtitle = ForgeTheme.Label("Auto-tuned for your GPU, with optional overrides below.", 12, ForgeTheme.TextSecondary);

    readonly StackPanel drawerPerfSection = new() { Spacing = 6 };
    readonly TextBlock drawerHardwareText = ForgeTheme.Label("", 12, ForgeTheme.InfoText, FontWeight.SemiBold);
    readonly ToggleSwitch autoTierToggle = new() { OnContent = "Auto", OffContent = "Manual" };
    readonly Dictionary<string, Border> tierRows = new();
    readonly Dictionary<string, Border> tierDots = new();
    readonly Dictionary<string, Border> tierRecPills = new();

    readonly Border advancedHeaderBorder;
    readonly TextBlock advancedHeaderLabel = ForgeTheme.Label("Advanced ▸", 12.5, ForgeTheme.TextPrimary, FontWeight.Bold, wrap: false);
    readonly TextBlock advancedHeaderHint = ForgeTheme.Label("Enemy Mix · Extras · Host Rules", 11, ForgeTheme.TextSecondary, wrap: false);
    readonly StackPanel advancedBodyPanel = new() { Spacing = 10, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };

    readonly StackPanel drawerVisualExtrasSection = new() { Spacing = 8 };
    readonly Dictionary<string, ToggleButton> enemyPills = new();
    readonly Dictionary<string, ToggleButton> squidPills = new();
    readonly ToggleSwitch deltaToggle = new() { OnContent = "On", OffContent = "Off" };
    readonly Border deltaRow = new();
    readonly ToggleSwitch deathTrooperToggle = new() { OnContent = "On", OffContent = "Off" };
    readonly Border deathTrooperRow = new();

    // Host Gameplay Controls (#20 + v2.2 Level 12 ULTRAKILL Theater Scale)
    readonly Dictionary<string, ToggleButton> theaterPills = new();
    readonly TextBlock theaterDescLabel = ForgeTheme.Label("Difficulty 12 ('LEVEL 12: CLONE WARS') unlocks a 2.79 km² map, 2–3k hybrid Clone Wars battalions, and 0.0s turbo corpse cleanup.", 10.5, ForgeTheme.TextSecondary);
    readonly Dictionary<string, ToggleButton> chaosPills = new();
    readonly Dictionary<string, ToggleButton> seafPills = new();
    readonly ToggleSwitch corpseCleanupToggle = new() { OnContent = "On", OffContent = "Off" };

    readonly Border drawerPreviewBox = new()
    {
        Background = ForgeTheme.BgElevated,
        BorderBrush = ForgeTheme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(12, 9)
    };
    readonly TextBlock drawerPreviewBadge = ForgeTheme.Label("", 12.5, ForgeTheme.SuccessText, FontWeight.Bold);
    readonly TextBlock drawerPreviewDetail = ForgeTheme.Label("", 11.5, ForgeTheme.TextSecondary);
    readonly Button drawerApplyButton = ForgeTheme.PrimaryButton("Done", 42, 14);

    // Embedded 4-Stage Guided Setup View
    readonly Button setupBackButton = ForgeTheme.GhostButton("← Back to Home", 12, new Thickness(12, 6));
    readonly Border[] setupStepPills = new Border[4];
    readonly TextBlock[] setupStepLabels = new TextBlock[4];
    readonly TextBlock setupHeadline = ForgeTheme.Label("", 19, ForgeTheme.TextPrimary, FontWeight.Bold);
    readonly TextBlock setupDetail = ForgeTheme.Label("", 13, ForgeTheme.TextSecondary);

    readonly StackPanel setupStage1Panel = new() { Spacing = 12 };
    readonly Border setupSteamPill = new() { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4), BorderThickness = new Thickness(1) };
    readonly TextBlock setupSteamPillText = ForgeTheme.Label("", 12, ForgeTheme.TextPrimary, FontWeight.SemiBold, wrap: false);
    readonly Border setupSpacePill = new() { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4), BorderThickness = new Thickness(1) };
    readonly TextBlock setupSpacePillText = ForgeTheme.Label("", 12, ForgeTheme.TextPrimary, FontWeight.SemiBold, wrap: false);
    readonly StackPanel setupDrivesList = new() { Spacing = 8 };
    readonly TextBlock setupFolderDisplay = ForgeTheme.Label("", 12, ForgeTheme.TextSecondary);
    readonly Button setupBrowseCustomButton = ForgeTheme.GhostButton("Custom Folder…", 12, new Thickness(12, 7));
    readonly Button setupUseExistingButton = ForgeTheme.GhostButton("Use Existing Forge…", 12, new Thickness(12, 7));

    readonly StackPanel setupStage2Panel = new() { Spacing = 12 };
    readonly TextBlock[] setupSubstepTexts = new TextBlock[3];
    readonly Border[] setupSubstepDots = new Border[3];
    readonly Border setupQrCard = new()
    {
        IsVisible = false,
        Background = ForgeTheme.BgElevated,
        BorderBrush = ForgeTheme.InfoBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16, 14)
    };
    readonly Image setupQrImage = new() { Width = 210, Height = 210, Stretch = Stretch.Uniform };
    readonly Button setupOfficialFallbackButton = ForgeTheme.LinkStyleButton("Having trouble with QR? Open official HellForge setup", ForgeTheme.TextSecondary, 12);

    readonly StackPanel setupStage3Panel = new() { Spacing = 8 };
    readonly Border[] syncStepCards = new Border[5];
    readonly Border[] syncStepBadges = new Border[5];
    readonly TextBlock[] syncStepBadgeTexts = new TextBlock[5];
    readonly TextBlock[] syncStepTitles = new TextBlock[5];
    readonly TextBlock[] syncStepSubtitles = new TextBlock[5];

    readonly StackPanel setupStage4Panel = new() { Spacing = 12 };
    readonly TextBlock setupStep4ModeText = ForgeTheme.Label("", 13.5, ForgeTheme.TextPrimary, FontWeight.SemiBold);
    readonly Dictionary<string, Button> setupStep4TextureButtons = new();
    readonly Border setupStep4ConsentBox = new()
    {
        Background = ForgeTheme.BgElevated,
        BorderBrush = ForgeTheme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 11)
    };
    readonly TextBlock setupStep4ConsentText = ForgeTheme.Label("", 12.5, ForgeTheme.TextSecondary);

    readonly Border setupProgressCard = new()
    {
        IsVisible = false,
        Background = ForgeTheme.BgSurface,
        BorderBrush = ForgeTheme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 11)
    };
    readonly TextBlock setupProgressStage = ForgeTheme.Label("", 13, ForgeTheme.TextPrimary, FontWeight.SemiBold);
    readonly TextBlock setupProgressMetrics = ForgeTheme.Label("", 12, ForgeTheme.ClonesAccent, FontWeight.SemiBold, TextAlignment.Right, wrap: false);
    readonly ProgressBar setupProgressBar = new()
    {
        Height = 7,
        Minimum = 0,
        Maximum = 100,
        CornerRadius = new CornerRadius(4),
        Background = ForgeTheme.BgTrack,
        Foreground = ForgeTheme.ClonesAccent
    };
    readonly Button setupPrimaryButton = ForgeTheme.PrimaryButton("CONTINUE", 48, 15);
    readonly Button setupCancelWorkButton = ForgeTheme.GhostButton("Pause Download", 12.5, new Thickness(16, 12));

    // Settings & Diagnostics Modal (#3 Disk Usage, #13 Verify & Repair, #12 Discord Copy Report, #6/#7/#15 toggles)
    readonly Grid settingsOverlay = new() { IsVisible = false, Background = ForgeTheme.BgOverlay };
    readonly TextBlock settingsInstallPath = ForgeTheme.Label("Not configured", 12.5, ForgeTheme.TextPrimary);
    readonly TextBlock settingsDiskUsageText = ForgeTheme.Label("", 12, ForgeTheme.InfoText, FontWeight.SemiBold);
    readonly TextBlock settingsAccountSummary = ForgeTheme.Label("", 12, ForgeTheme.TextSecondary);
    readonly TextBlock settingsUpdateNote = ForgeTheme.Label("", 12, ForgeTheme.InfoText);
    readonly WrapPanel settingsHealthWrap = new() { Orientation = Orientation.Horizontal };
    readonly CheckBox settingsSfxCheck = new() { Content = "UI Sound Effects", FontSize = 12, Foreground = ForgeTheme.TextPrimary };
    readonly CheckBox settingsDiscordCheck = new() { Content = "Discord Rich Presence", FontSize = 12, Foreground = ForgeTheme.TextPrimary };
    readonly CheckBox settingsTrayCheck = new() { Content = "Minimize to tray while playing", FontSize = 12, Foreground = ForgeTheme.TextPrimary };
    readonly TextBox settingsReportBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 96,
        FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
        FontSize = 11,
        Background = ForgeTheme.BgInput,
        Foreground = ForgeTheme.TextSecondary,
        BorderBrush = ForgeTheme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8)
    };
    string latestDiscordReport = "";
    readonly Button settingsCopyReportButton = ForgeTheme.GhostButton("Copy Report", 12, new Thickness(13, 7));
    readonly Button settingsSaveReportButton = ForgeTheme.GhostButton("Save Report…", 12, new Thickness(13, 7));
    readonly Button settingsOpenFolderButton = ForgeTheme.GhostButton("Open Folder", 11.5, new Thickness(9, 5));
    readonly Button settingsLocateButton = ForgeTheme.GhostButton("Locate…", 11.5, new Thickness(9, 5));
    readonly Button settingsManagerButton = ForgeTheme.GhostButton("Open HellForge", 11.5, new Thickness(9, 5));
    readonly Button settingsGuidedSetupButton = ForgeTheme.GhostButton("Guided Setup…", 11.5, new Thickness(9, 5));
    readonly Button settingsCleanCacheButton = ForgeTheme.GhostButton("Clean Cache", 11.5, new Thickness(9, 5));
    readonly Button settingsVerifyRepairButton = ForgeTheme.GhostButton("Verify & Repair", 11.5, new Thickness(10, 5));
    readonly Button settingsSyncAccountButton = ForgeTheme.GhostButton("Sync Account", 11.5, new Thickness(11, 5));
    readonly Button settingsCheckUpdatesButton = ForgeTheme.GhostButton("Check Updates", 11.5, new Thickness(11, 5));

    // What's New Dark Overlay (#5)
    readonly Grid whatsNewOverlay = new() { IsVisible = false, Background = ForgeTheme.BgOverlay };
    readonly TextBlock whatsNewTitle = ForgeTheme.Label("What's New", 17, ForgeTheme.TextPrimary, FontWeight.Bold);
    readonly TextBlock whatsNewSubtitle = ForgeTheme.Label("Highlights in this Clonedivers release", 12, ForgeTheme.TextSecondary);
    readonly StackPanel whatsNewBulletsPanel = new() { Spacing = 8 };
    readonly Button whatsNewOpenWebButton = ForgeTheme.LinkStyleButton("Full release notes on GitHub ↗", ForgeTheme.InfoText, 12);
    readonly Button whatsNewCloseButton = ForgeTheme.PrimaryButton("Got It", 38, 13.5);
    string? currentWhatsNewUrl;

    // Dark Confirm Modal
    readonly Grid confirmOverlay = new() { IsVisible = false, Background = ForgeTheme.BgOverlay };
    readonly TextBlock confirmTitle = ForgeTheme.Label("", 17, ForgeTheme.TextPrimary, FontWeight.Bold);
    readonly TextBlock confirmBody = ForgeTheme.Label("", 13.5, ForgeTheme.TextSecondary);
    readonly Button confirmCancelButton = ForgeTheme.GhostButton("Cancel", 13, new Thickness(16, 9));
    readonly Button confirmOkButton = ForgeTheme.PrimaryButton("Continue", 38, 13.5);

    internal bool IsErrorToastVisible => unhandledErrorBanner.IsVisible;
    internal string ErrorToastMessage => unhandledErrorText.Text ?? "";
    internal bool IsCustomizeOpen => showingCustomize;
    internal bool IsAdvancedExpanded => showingAdvanced;
    internal bool IsSettingsOpen => showingSettings;
    internal bool IsWhatsNewOpen => showingWhatsNew;
    internal bool IsConfirmOpen => confirmOverlay.IsVisible;

    public ForgeShellWindow(IForgeUiHost host, string? initialOverlay = null)
    {
        this.host = host;
        advancedHeaderBorder = new ActionCardBorder(ToggleAdvancedSectionAsync)
        {
            Background = ForgeTheme.BgElevated,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 9),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        Title = "Clonedivers";
        Icon = ForgeTheme.CreateWindowIcon();
        Width = 900;
        Height = 680;
        MinWidth = 760;
        MinHeight = 620;
        Background = ForgeTheme.BgCanvas;
        Foreground = ForgeTheme.TextPrimary;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        homeView = BuildHomeView();
        setupView = BuildSetupView();
        BuildCustomizeOverlay();
        BuildSettingsOverlay();
        BuildWhatsNewOverlay();
        BuildConfirmOverlay();
        BuildUnhandledErrorBanner();

        var shell = new DockPanel { LastChildFill = true };
        var topDock = new StackPanel { Spacing = 0 };
        topDock.Children.Add(BuildHeaderBar());
        topDock.Children.Add(ForgeTheme.HoloAccentLine(margin: new Thickness(28, 0, 28, 8)));
        topDock.Children.Add(BuildUpdateBanner());
        topDock.Children.Add(BuildStepDownBanner());
        topDock.Children.Add(BuildSelfInstallBanner());
        DockPanel.SetDock(topDock, Dock.Top);
        shell.Children.Add(topDock);

        var footerBar = BuildFooterBar();
        DockPanel.SetDock(footerBar, Dock.Bottom);
        shell.Children.Add(footerBar);

        var contentHost = new Grid();
        contentHost.Children.Add(homeView);
        contentHost.Children.Add(setupView);
        shell.Children.Add(contentHost);

        var rootLayers = new Grid();
        rootLayers.Children.Add(shell);
        rootLayers.Children.Add(customizeOverlay);
        rootLayers.Children.Add(settingsOverlay);
        rootLayers.Children.Add(whatsNewOverlay);
        rootLayers.Children.Add(confirmOverlay);
        rootLayers.Children.Add(unhandledErrorBanner);
        Content = rootLayers;

        // QA Bugfix #3: Keyboard shortcuts (Tunnel so Escape & Enter work reliably regardless of child focus)
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        host.AttachUiCallbacks(
            confirmAsync: ShowConfirmOverlayAsync,
            pickFolderAsync: PickFolderAsync,
            saveFileAsync: SaveFileAsync,
            taskbarProgress: p => Dispatcher.UIThread.Post(() => WindowsTaskbar.SetProgress(this, p)),
            requestAttention: () => Dispatcher.UIThread.Post(HandleAttentionRequest));

        host.StateChanged += () => Dispatcher.UIThread.Post(RefreshAll);
        host.SetupVisibilityChanged += show => Dispatcher.UIThread.Post(() =>
        {
            showingSetup = show && host.SupportsEmbeddedSetup;
            if (showingSetup) { showingCustomize = false; showingSettings = false; showingWhatsNew = false; }
            RefreshAll();
        });

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && minimizeToTrayActive && wasSessionRunning && trayIcon != null)
            {
                Dispatcher.UIThread.Post(HideToTray);
            }
        };

        Opened += async (_, _) =>
        {
            WindowsTaskbar.ApplyDarkTitleBar(this);
            InitializeTrayIconIfSupported();
            if (initialOverlay == "setup" && host.SupportsEmbeddedSetup) showingSetup = true;
            else if (initialOverlay == "customize") { showingCustomize = true; showingAdvanced = false; }
            else if (initialOverlay == "customize-advanced")
            {
                showingCustomize = true;
                showingAdvanced = true;
                RefreshAll();
                Dispatcher.UIThread.Post(() => drawerScrollViewer?.ScrollToEnd(), DispatcherPriority.Loaded);
            }
            else if (initialOverlay == "settings") { showingSettings = true; _ = LoadSettingsDiagnosticsAsync(); }
            else if (initialOverlay == "whats-new") { _ = OpenWhatsNewAsync(AppInfo.Version); }
            RefreshAll();
            await host.InitializeAsync();
            RefreshAll();
        };

        Closing += async (_, e) => await HandleClosingCoreAsync(() => e.Cancel = true, Close);

        Closed += (_, _) => DisposeTrayIcon();

        RefreshAll();
    }

    internal async Task HandleClosingCoreAsync(Action cancelClose, Action invokeClose)
    {
        if (host.IsPreview || closingConfirmed) return;
        if (!host.CanClose(out var reason))
        {
            cancelClose();
            if (!string.IsNullOrWhiteSpace(reason))
            {
                toastText.Text = reason;
                toastText.IsVisible = true;
            }
            return;
        }
        cancelClose();
        closingConfirmed = true;
        DisposeTrayIcon();
        try
        {
            await host.DisposeAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error disposing host during shutdown: " + ex);
        }
        finally
        {
            invokeClose();
        }
    }

    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleShortcutKey(e.Key))
            e.Handled = true;
    }

    internal bool HandleShortcutKey(Key key)
    {
        if (key == Key.Escape)
        {
            if (confirmOverlay.IsVisible || pendingConfirm != null)
            {
                UiSoundEffects.PlayButtonBlip();
                ResolveConfirm(false);
                return true;
            }
            if (whatsNewOverlay.IsVisible || showingWhatsNew)
            {
                UiSoundEffects.PlayButtonBlip();
                CloseWhatsNewOverlay();
                return true;
            }
            if (settingsOverlay.IsVisible || showingSettings)
            {
                UiSoundEffects.PlayButtonBlip();
                showingSettings = false;
                settingsOverlay.IsVisible = false;
                return true;
            }
            if (customizeOverlay.IsVisible || showingCustomize)
            {
                UiSoundEffects.PlayButtonBlip();
                showingCustomize = false;
                customizeOverlay.IsVisible = false;
                return true;
            }
            return false;
        }

        if (key == Key.Enter && (confirmOverlay.IsVisible || pendingConfirm != null))
        {
            UiSoundEffects.PlayButtonBlip();
            ResolveConfirm(!confirmCancelButton.IsFocused);
            return true;
        }

        return false;
    }

    public void ShowUnhandledErrorToast(Exception exception)
    {
        var summary = exception?.Message;
        if (string.IsNullOrWhiteSpace(summary)) summary = "An unexpected interface error occurred.";
        ShowErrorToast("Launcher UI error: " + summary);
    }

    public void ShowErrorToast(string message)
    {
        void Apply()
        {
            unhandledErrorText.Text = message;
            unhandledErrorBanner.IsVisible = true;
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    void BuildUnhandledErrorBanner()
    {
        var dismiss = ForgeTheme.GhostButton("✕", 11.5, new Thickness(8, 3));
        AutomationProperties.SetName(dismiss, "Dismiss UI error notice");
        dismiss.Click += (_, _) => unhandledErrorBanner.IsVisible = false;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        unhandledErrorText.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(unhandledErrorText);
        Grid.SetColumn(dismiss, 1);
        grid.Children.Add(dismiss);
        unhandledErrorBanner.Child = grid;
    }

    void InitializeTrayIconIfSupported()
    {
        if (host.IsPreview || trayIcon != null)
            return;
        try
        {
            var openItem = new NativeMenuItem("Open Clonedivers");
            openItem.Click += (_, _) => Dispatcher.UIThread.Post(RestoreFromTray);

            trayEndSessionItem = new NativeMenuItem("End Session") { IsEnabled = false };
            trayEndSessionItem.Click += (_, _) => Dispatcher.UIThread.Post(async () => await HandleTraySessionActionAsync());

            var quitItem = new NativeMenuItem("Quit");
            quitItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (host.CanClose(out _))
                {
                    RestoreFromTray();
                    Close();
                }
                else
                {
                    RestoreFromTray();
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d && d.MainWindow != null)
                        WindowsTaskbar.BringToFront(this);
                }
            });

            var menu = new NativeMenu();
            menu.Items.Add(openItem);
            menu.Items.Add(trayEndSessionItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quitItem);

            trayIcon = new TrayIcon
            {
                Icon = ForgeTheme.CreateWindowIcon(),
                ToolTipText = "Clonedivers",
                Menu = menu,
                IsVisible = true
            };
            trayIcon.Clicked += (_, _) => Dispatcher.UIThread.Post(RestoreFromTray);

            if (Application.Current != null)
            {
                var icons = new TrayIcons { trayIcon };
                TrayIcon.SetIcons(Application.Current, icons);
            }
        }
        catch
        {
            trayIcon = null;
        }
    }

    void HideToTray()
    {
        if (trayIcon == null) return;
        try
        {
            ShowInTaskbar = false;
            Hide();
            trayIcon.ToolTipText = "Clonedivers — Session Running (click to open)";
        }
        catch { }
    }

    void RestoreFromTray()
    {
        try
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowsTaskbar.BringToFront(this);
        }
        catch { }
    }

    internal async Task HandleTraySessionActionAsync()
    {
        RestoreFromTray();
        var s = host.GetHomeState();
        if (!s.IsSessionRunning || s.IsBusy) return;
        if (string.Equals(s.SecondaryActionLabel, ForgeTacticalCopy.CancelLaunchButton, StringComparison.Ordinal))
            await host.TriggerSecondaryActionAsync();
        else if (s.PrimaryActionEnabled)
            await host.TriggerPrimaryActionAsync();
    }

    internal void DisposeTrayIcon()
    {
        var icon = trayIcon;
        trayIcon = null;
        try
        {
            if (Application.Current != null)
                TrayIcon.SetIcons(Application.Current, null);
        }
        catch { }
        if (icon == null) return;
        try
        {
            icon.IsVisible = false;
            icon.Dispose();
        }
        catch { }
    }

    public static void DisposeApplicationTrayIcons()
    {
        void Cleanup()
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;
                if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: ForgeShellWindow shell })
                    shell.DisposeTrayIcon();
                var icons = TrayIcon.GetIcons(app);
                if (icons != null)
                {
                    TrayIcon.SetIcons(app, null);
                    foreach (var icon in icons)
                    {
                        try
                        {
                            icon.IsVisible = false;
                            icon.Dispose();
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Cleanup();
        }
        else
        {
            try
            {
                if (!Dispatcher.UIThread.InvokeAsync(Cleanup).GetTask().Wait(TimeSpan.FromMilliseconds(500)))
                    Cleanup();
            }
            catch
            {
                Cleanup();
            }
        }
    }

    void HandleAttentionRequest()
    {
        if (!IsVisible) RestoreFromTray();
        WindowsTaskbar.FlashAttention(this);
    }

    Control BuildHeaderBar()
    {
        headerStatusBadge.Child = headerStatusText;

        AutomationProperties.SetName(muteButton, "Toggle sound effects");
        ToolTip.SetTip(muteButton, "Toggle Star Wars holoterminal UI sound effects");
        muteButton.Click += (_, _) =>
        {
            host.ToggleSoundEffects();
            if (host.GetHomeState().SoundEffectsEnabled)
                UiSoundEffects.PlayButtonBlip();
            RefreshAll();
        };

        AutomationProperties.SetName(settingsButton, "Settings and diagnostics");
        AutomationProperties.SetAutomationId(settingsButton, "HeaderSettingsButton");
        settingsButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            showingSettings = true;
            settingsOverlay.IsVisible = true;
            await LoadSettingsDiagnosticsAsync();
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(ForgeTheme.HeaderMark(30));
        var titles = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(ForgeTheme.Label("CLONEDIVERS", 19, ForgeTheme.TextPrimary, FontWeight.ExtraBold, wrap: false));
        titles.Children.Add(headerSubtitle);
        left.Children.Add(titles);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(headerStatusBadge);
        right.Children.Add(muteButton);
        right.Children.Add(settingsButton);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(28, 15, 28, 10)
        };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    Control BuildUpdateBanner()
    {
        updateBannerWhatsNew.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            var version = host.GetHomeState().UpdateBanner?.Version;
            await OpenWhatsNewAsync(version);
        };
        updateBannerAction.Background = ForgeTheme.CtaBlue;
        updateBannerAction.BorderThickness = new Thickness(0);
        updateBannerAction.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            await host.TriggerUpdateBannerActionAsync();
        };
        updateBannerDismiss.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            host.DismissUpdateBanner();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(updateBannerWhatsNew);
        buttons.Children.Add(updateBannerAction);
        buttons.Children.Add(updateBannerDismiss);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        updateBannerText.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(updateBannerText);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        updateBanner.Child = row;
        return updateBanner;
    }

    Control BuildStepDownBanner()
    {
        stepDownAcceptButton.Background = ForgeTheme.CtaPrimary;
        stepDownAcceptButton.Foreground = ForgeTheme.TextOnAccent;
        stepDownAcceptButton.BorderThickness = new Thickness(0);
        stepDownAcceptButton.FontWeight = FontWeight.Bold;
        stepDownAcceptButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            await host.AcceptStepDownAsync();
        };
        stepDownDismissButton.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            host.DismissStepDownBanner();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(stepDownAcceptButton);
        buttons.Children.Add(stepDownDismissButton);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        stepDownBannerText.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(stepDownBannerText);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        stepDownBanner.Child = row;
        return stepDownBanner;
    }

    Control BuildSelfInstallBanner()
    {
        selfInstallAcceptButton.BorderBrush = ForgeTheme.ClonesAccent;
        selfInstallAcceptButton.Foreground = ForgeTheme.ClonesAccent;
        selfInstallAcceptButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            await host.InstallSelfShortcutsAsync();
        };
        selfInstallDismissButton.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            host.DismissSelfInstallBanner();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(selfInstallAcceptButton);
        buttons.Children.Add(selfInstallDismissButton);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        selfInstallBannerText.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(selfInstallBannerText);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        selfInstallBanner.Child = row;
        return selfInstallBanner;
    }

    Control BuildFooterBar()
    {
        steamFooterLink.Click += (_, _) => host.OpenSteamLibrary();
        ToolTip.SetTip(versionFooter, "Squad Match Code · Co-op players with the same code can join each other across any Star Wars skin or texture tier.");
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(28, 6, 28, 12)
        };
        steamFooterLink.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(steamFooterLink);
        versionFooter.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(versionFooter, 1);
        grid.Children.Add(versionFooter);
        return grid;
    }

    Control BuildHomeView()
    {
        var stack = new StackPanel { Spacing = 13, Margin = new Thickness(28, 2, 28, 6) };

        // 1. Three Hero Universe Cards with Star Wars holoterminal faction bars & 150ms hover transitions
        var cardsGrid = new UniformGrid { Columns = 3, Rows = 1 };
        var specs = new[]
        {
            ("regular", "Regular", "SUPER EARTH", "Original visuals + gameplay preset"),
            ("clonedivers", "Clones", "FOR THE REPUBLIC", "Clone Troopers + CIS Droids"),
            ("empire", "Empire", "GALACTIC EMPIRE", "Stormtroopers + Imperial Walkers")
        };

        for (int i = 0; i < specs.Length; i++)
        {
            var (id, title, tag, desc) = specs[i];
            var accent = ForgeTheme.AccentForMode(id);

            var topBar = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(11, 11, 0, 0),
                Background = ForgeTheme.MutedBarForMode(id),
                Transitions = ForgeTheme.CreateSmoothTransitions(Border.BackgroundProperty, Border.BorderBrushProperty)
            };
            modeTopBars[id] = topBar;

            var badgeText = ForgeTheme.Label("", 10.5, accent, FontWeight.Bold, wrap: false);
            var badge = new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 3),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                MinHeight = 22,
                Child = badgeText
            };
            modeBadges[id] = badge;
            modeBadgeTexts[id] = badgeText;

            var body = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(14, 13, 14, 13),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            body.Children.Add(ForgeTheme.FactionEmblem(id, 60));
            body.Children.Add(ForgeTheme.Label(title, 18, ForgeTheme.TextPrimary, FontWeight.Bold, TextAlignment.Center, wrap: false));
            body.Children.Add(ForgeTheme.Label(tag, 10, accent, FontWeight.Bold, TextAlignment.Center, wrap: false));
            body.Children.Add(ForgeTheme.Label(desc, 11.5, ForgeTheme.TextSecondary, FontWeight.Normal, TextAlignment.Center));
            body.Children.Add(badge);

            var cardGrid = new DockPanel();
            DockPanel.SetDock(topBar, Dock.Top);
            cardGrid.Children.Add(topBar);
            cardGrid.Children.Add(body);

            var cardBorder = new Border
            {
                Background = ForgeTheme.BgSurface,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Transitions = ForgeTheme.CreateSmoothTransitions(Border.BackgroundProperty, Border.BorderBrushProperty),
                Child = cardGrid
            };
            modeCards[id] = cardBorder;

            var btn = new Button
            {
                Content = cardBorder,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 2 ? 0 : 6, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            btn.PointerEntered += (_, _) =>
            {
                hoveredModeId = id;
                ApplyModeCardVisuals(host.GetHomeState());
            };
            btn.PointerExited += (_, _) =>
            {
                if (hoveredModeId == id) hoveredModeId = null;
                ApplyModeCardVisuals(host.GetHomeState());
            };
            AutomationProperties.SetName(btn, title);
            btn.Click += async (_, _) =>
            {
                var prev = host.GetHomeState().Mode;
                if (!string.Equals(prev, id, StringComparison.OrdinalIgnoreCase))
                    UiSoundEffects.PlayModeSwitch();
                else
                    UiSoundEffects.PlayButtonBlip();
                await host.SelectModeAsync(id);
            };
            modeButtons[id] = btn;
            cardsGrid.Children.Add(btn);
        }
        stack.Children.Add(cardsGrid);

        // 2. Compact Loadout Summary Bar
        customizeSizePill.Child = customizeSizePillText;
        AutomationProperties.SetAutomationId(customizeSummaryText, "LoadoutSummaryText");
        AutomationProperties.SetName(customizeOpenButton, "Customize loadout options");
        AutomationProperties.SetAutomationId(customizeOpenButton, "CustomizeOpenButton");
        customizeOpenButton.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            showingCustomize = true;
            var s = host.GetHomeState();
            showingAdvanced = string.Equals(s.Mode, "regular", StringComparison.OrdinalIgnoreCase);
            RefreshCustomizeDrawer();
            customizeOverlay.IsVisible = true;
        };

        var custLeft = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        custLeft.Children.Add(customizeSummaryTitle);
        custLeft.Children.Add(customizeSummaryText);

        var custRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        custRight.Children.Add(customizeSizePill);
        custRight.Children.Add(customizeOpenButton);

        var custGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        custGrid.Children.Add(custLeft);
        Grid.SetColumn(custRight, 1);
        custGrid.Children.Add(custRight);
        customizeStrip.Child = custGrid;
        stack.Children.Add(customizeStrip);

        // 3. Fixed-Height Mission Status & Deterministic Progress Deck
        AutomationProperties.SetAutomationId(statusHeadline, "StatusHeadline");
        AutomationProperties.SetAutomationId(statusDetail, "StatusDetail");
        AutomationProperties.SetAutomationId(toastText, "ToastNotice");
        var statusTop = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        statusTop.Children.Add(statusDot);
        Grid.SetColumn(statusHeadline, 1);
        statusTop.Children.Add(statusHeadline);
        statusMetrics.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(statusMetrics, 2);
        statusTop.Children.Add(statusMetrics);

        var deckStack = new StackPanel { Spacing = 6 };
        deckStack.Children.Add(statusDeckTopLine);
        deckStack.Children.Add(statusTop);
        deckStack.Children.Add(statusDetail);
        deckStack.Children.Add(homeProgress);
        toastText.IsVisible = false;
        deckStack.Children.Add(toastText);
        statusDeck.Child = deckStack;
        stack.Children.Add(statusDeck);

        // 4. Hero Primary CTA (+ optional contextual secondary button)
        AutomationProperties.SetName(heroButton, "Primary action");
        AutomationProperties.SetAutomationId(heroButton, "HeroPrimaryButton");
        AutomationProperties.SetAutomationId(secondaryButton, "HeroSecondaryButton");
        heroButton.Click += async (_, _) =>
        {
            var label = heroButton.Content?.ToString() ?? "";
            if (label.Contains("LAUNCH", StringComparison.OrdinalIgnoreCase))
                UiSoundEffects.PlayLaunchConfirm();
            else
                UiSoundEffects.PlayButtonBlip();
            await host.TriggerPrimaryActionAsync();
        };
        secondaryButton.IsVisible = false;
        secondaryButton.Margin = new Thickness(10, 0, 0, 0);
        secondaryButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            await host.TriggerSecondaryActionAsync();
        };

        var ctaRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        ctaRow.Children.Add(heroButton);
        Grid.SetColumn(secondaryButton, 1);
        ctaRow.Children.Add(secondaryButton);
        stack.Children.Add(ctaRow);

        return new ScrollViewer
        {
            Content = stack,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    void BuildCustomizeOverlay()
    {
        var backdrop = new Border { Background = Brushes.Transparent };
        backdrop.PointerPressed += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            showingCustomize = false;
            customizeOverlay.IsVisible = false;
        };
        customizeOverlay.Children.Add(backdrop);

        var closeBtn = ForgeTheme.GhostButton("✕", 13, new Thickness(10, 5));
        AutomationProperties.SetName(closeBtn, "Close customize drawer");
        closeBtn.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            showingCustomize = false;
            customizeOverlay.IsVisible = false;
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(drawerTitle);
        titleStack.Children.Add(drawerSubtitle);
        header.Children.Add(titleStack);
        Grid.SetColumn(closeBtn, 1);
        closeBtn.VerticalAlignment = VerticalAlignment.Top;
        header.Children.Add(closeBtn);

        // ── Top Section (Always Visible): Performance Preset Card ──
        var hwDot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(4),
            Background = ForgeTheme.ClonesAccent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var hwRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { hwDot, drawerHardwareText } };

        AutomationProperties.SetAutomationId(autoTierToggle, "AutoTierToggle");
        AutomationProperties.SetName(autoTierToggle, "Auto performance tier");
        var autoSwitchRow = BuildSwitchRow(
            "Auto (Recommended)",
            "Matches texture memory to your detected GPU & RAM.",
            autoTierToggle);
        autoTierToggle.IsCheckedChanged += async (_, _) =>
        {
            if (updatingControls) return;
            UiSoundEffects.PlayButtonBlip();
            if (autoTierToggle.IsChecked == true)
            {
                await host.SetPerformanceTierAsync(PerformanceTierAdvisor.Auto);
            }
            else
            {
                var s = host.GetHomeState();
                var currentConcrete = PerformanceTierAdvisor.NormalizeConcreteTier(s.PerformanceTier, s.Hardware);
                await host.SetPerformanceTierAsync(currentConcrete);
            }
        };

        var tierListStack = new StackPanel { Spacing = 4 };
        var vramHints = new Dictionary<string, string>
        {
            [PerformanceTierAdvisor.Ultra] = "12 GB+ VRAM · Resident",
            [PerformanceTierAdvisor.High] = "8–11 GB VRAM · Streamed",
            [PerformanceTierAdvisor.Balanced] = "6–8 GB VRAM · 1024px",
            [PerformanceTierAdvisor.Low512] = "4–6 GB VRAM · 512px",
            [PerformanceTierAdvisor.UltraPotato] = "≤ 4 GB / iGPU · 256px"
        };

        foreach (var tier in PerformanceTierAdvisor.Tiers)
        {
            var dot = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1.5),
                BorderBrush = ForgeTheme.BorderStrong,
                VerticalAlignment = VerticalAlignment.Center
            };
            tierDots[tier.Id] = dot;

            var recPill = ForgeTheme.Pill("AUTO", ForgeTheme.InfoBg, ForgeTheme.InfoText, ForgeTheme.InfoBorder, 9.5);
            recPill.Padding = new Thickness(7, 1);
            recPill.IsVisible = false;
            tierRecPills[tier.Id] = recPill;

            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    ForgeTheme.Label(tier.DisplayName, 12.5, ForgeTheme.TextPrimary, FontWeight.SemiBold, wrap: false),
                    recPill
                }
            };
            var profileDesc = ForgeTheme.Label(
                CustomizeTierDescriptions.GetValueOrDefault(tier.Id, tier.Description),
                11,
                ForgeTheme.TextSecondary,
                wrap: false);

            var leftTextStack = new StackPanel
            {
                Spacing = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { titleRow, profileDesc }
            };

            var leftCluster = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { dot, leftTextStack }
            };

            var rightHint = ForgeTheme.Label(vramHints.GetValueOrDefault(tier.Id, ""), 11, ForgeTheme.TextSecondary, alignment: TextAlignment.Right, wrap: false);
            rightHint.VerticalAlignment = VerticalAlignment.Center;

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            rowGrid.Children.Add(leftCluster);
            Grid.SetColumn(rightHint, 1);
            rowGrid.Children.Add(rightHint);

            var tierIdCopy = tier.Id;
            async Task SelectTierAsync()
            {
                if (updatingControls || host.GetHomeState().ControlsLocked) return;
                UiSoundEffects.PlayButtonBlip();
                await host.SetPerformanceTierAsync(tierIdCopy);
            }
            var rowBorder = new ActionCardBorder(SelectTierAsync)
            {
                Background = ForgeTheme.BgCanvas,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                Transitions = ForgeTheme.CreateSmoothTransitions(Border.BackgroundProperty, Border.BorderBrushProperty),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = rowGrid
            };
            AutomationProperties.SetAutomationId(rowBorder, $"TierRow-{tier.Id}");
            AutomationProperties.SetName(rowBorder, $"{tier.DisplayName} — {CustomizeTierDescriptions.GetValueOrDefault(tier.Id, tier.Description)}");
            rowBorder.PointerPressed += async (_, _) => await SelectTierAsync();
            tierRows[tier.Id] = rowBorder;
            tierListStack.Children.Add(rowBorder);
        }

        var perfCard = new Border
        {
            Background = ForgeTheme.BgElevated,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children = { hwRow, ForgeTheme.HoloAccentLine(margin: new Thickness(0)), autoSwitchRow, tierListStack }
            }
        };
        drawerPerfSection.Children.Add(ForgeTheme.Label("PERFORMANCE PRESET", 10.5, ForgeTheme.TextMuted, FontWeight.Bold));
        drawerPerfSection.Children.Add(perfCard);

        // ── Collapsible Advanced ▸ Expander (Collapsed by default!) ──
        var advHeaderGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        advHeaderGrid.Children.Add(advancedHeaderLabel);
        advancedHeaderHint.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(advancedHeaderHint, 1);
        advHeaderGrid.Children.Add(advancedHeaderHint);
        advancedHeaderBorder.Child = advHeaderGrid;
        AutomationProperties.SetAutomationId(advancedHeaderBorder, "CustomizeAdvancedToggle");
        AutomationProperties.SetName(advancedHeaderBorder, "Toggle advanced customization");
        advancedHeaderBorder.PointerPressed += async (_, _) => await ToggleAdvancedSectionAsync();

        // Advanced 1: Enemy Skin & Squid Skin segmented pills
        var enemyWrap = new UniformGrid { Columns = 3 };
        var enemySpecs = new[]
        {
            ("mixed-droids", "Mixed Bots + CIS"),
            ("all-droids", "All CIS Droids"),
            ("native-automatons", "Native Automatons")
        };
        for (int i = 0; i < enemySpecs.Length; i++)
        {
            var (id, label) = enemySpecs[i];
            var pill = BuildSegmentedPill(label, i < enemySpecs.Length - 1);
            AutomationProperties.SetAutomationId(pill, $"EnemyPill-{id}");
            pill.Click += async (_, _) =>
            {
                if (updatingControls) return;
                UiSoundEffects.PlayButtonBlip();
                var s = host.GetHomeState();
                await host.UpdateCustomizationAsync(id, s.DeltaSquad, s.Covenant, s.Texture, s.SquidSkin, s.DeathTrooperVoice);
            };
            enemyPills[id] = pill;
            enemyWrap.Children.Add(pill);
        }

        var squidWrap = new UniformGrid { Columns = 3 };
        var squidSpecs = new[]
        {
            ("mixed-covenant", "Mixed Covenant"),
            ("all-covenant", "All Covenant"),
            ("native-squids", "Native Squids")
        };
        for (int i = 0; i < squidSpecs.Length; i++)
        {
            var (id, label) = squidSpecs[i];
            var pill = BuildSegmentedPill(label, i < squidSpecs.Length - 1);
            AutomationProperties.SetAutomationId(pill, $"SquidPill-{id}");
            pill.Click += async (_, _) =>
            {
                if (updatingControls) return;
                UiSoundEffects.PlayButtonBlip();
                var s = host.GetHomeState();
                await host.UpdateCustomizationAsync(s.EnemySkin, s.DeltaSquad, id != "native-squids", s.Texture, id, s.DeathTrooperVoice);
            };
            squidPills[id] = pill;
            squidWrap.Children.Add(pill);
        }

        // Advanced 2: Faction-specific Extras (Clones Delta Squad & Empire Encrypted Death Trooper Voice)
        deltaRow.Background = ForgeTheme.BgElevated;
        deltaRow.BorderBrush = ForgeTheme.BorderSubtle;
        deltaRow.BorderThickness = new Thickness(1);
        deltaRow.CornerRadius = new CornerRadius(9);
        deltaRow.Padding = new Thickness(11, 7);
        deltaRow.Child = BuildSwitchRow("Delta Squad", "Republic Commandos replace standard Clone armor.", deltaToggle);
        deltaToggle.IsCheckedChanged += async (_, _) =>
        {
            if (updatingControls) return;
            UiSoundEffects.PlayButtonBlip();
            var s = host.GetHomeState();
            await host.UpdateCustomizationAsync(s.EnemySkin, deltaToggle.IsChecked == true, s.Covenant, s.Texture, s.SquidSkin, s.DeathTrooperVoice);
        };

        deathTrooperRow.Background = ForgeTheme.BgElevated;
        deathTrooperRow.BorderBrush = ForgeTheme.BorderSubtle;
        deathTrooperRow.BorderThickness = new Thickness(1);
        deathTrooperRow.CornerRadius = new CornerRadius(9);
        deathTrooperRow.Padding = new Thickness(11, 7);
        deathTrooperRow.Child = BuildSwitchRow("Encrypted Death Trooper Voice", "Rogue One scrambled comms on Imperial voice slot 4.", deathTrooperToggle);
        deathTrooperToggle.IsCheckedChanged += async (_, _) =>
        {
            if (updatingControls) return;
            UiSoundEffects.PlayButtonBlip();
            var s = host.GetHomeState();
            await host.UpdateCustomizationAsync(s.EnemySkin, s.DeltaSquad, s.Covenant, s.Texture, s.SquidSkin, deathTrooperToggle.IsChecked == true);
        };

        drawerVisualExtrasSection.Children.Add(ForgeTheme.Label("ENEMY SKIN", 10.5, ForgeTheme.TextMuted, FontWeight.Bold));
        drawerVisualExtrasSection.Children.Add(enemyWrap);
        drawerVisualExtrasSection.Children.Add(ForgeTheme.Label("SQUID SKIN", 10.5, ForgeTheme.TextMuted, FontWeight.Bold));
        drawerVisualExtrasSection.Children.Add(squidWrap);
        drawerVisualExtrasSection.Children.Add(ForgeTheme.Label("EXTRAS", 10.5, ForgeTheme.TextMuted, FontWeight.Bold));
        drawerVisualExtrasSection.Children.Add(deltaRow);
        drawerVisualExtrasSection.Children.Add(deathTrooperRow);

        // Advanced 3: Host Gameplay Controls (#20 + v2.2 Level 12 ULTRAKILL Theater Scale)
        var theaterWrap = new UniformGrid { Columns = 2 };
        var theaterSpecs = new[]
        {
            ("standard", "Standard (D1–11)"),
            ("clone-wars", "L12 ULTRAKILL · Clone Wars")
        };
        for (int i = 0; i < theaterSpecs.Length; i++)
        {
            var (id, label) = theaterSpecs[i];
            var pill = BuildSegmentedPill(label, i < theaterSpecs.Length - 1);
            pill.FontSize = 11;
            AutomationProperties.SetAutomationId(pill, $"TheaterPill-{id}");
            pill.Click += async (_, _) =>
            {
                if (updatingControls) return;
                UiSoundEffects.PlayButtonBlip();
                var s = host.GetHomeState();
                await host.UpdateHostMutatorsAsync(s.HostChaosIntensity, s.HostHeavySeaf, s.HostFasterCorpseCleanup, id);
            };
            theaterPills[id] = pill;
            theaterWrap.Children.Add(pill);
        }

        var chaosWrap = new UniformGrid { Columns = 3 };
        var chaosSpecs = new[] { ("off", "Off"), ("standard", "Standard"), ("total-war", "Total War") };
        for (int i = 0; i < chaosSpecs.Length; i++)
        {
            var (id, label) = chaosSpecs[i];
            var pill = BuildSegmentedPill(label, i < chaosSpecs.Length - 1);
            pill.Click += async (_, _) =>
            {
                if (updatingControls) return;
                UiSoundEffects.PlayButtonBlip();
                var s = host.GetHomeState();
                await host.UpdateHostMutatorsAsync(id, s.HostHeavySeaf, s.HostFasterCorpseCleanup, s.HostTheaterPreset);
            };
            chaosPills[id] = pill;
            chaosWrap.Children.Add(pill);
        }

        var seafWrap = new UniformGrid { Columns = 3 };
        var seafSpecs = new[] { ("standard", "Standard"), ("heavy", "Heavy"), ("constant", "Constant") };
        for (int i = 0; i < seafSpecs.Length; i++)
        {
            var (id, label) = seafSpecs[i];
            var pill = BuildSegmentedPill(label, i < seafSpecs.Length - 1);
            pill.Click += async (_, _) =>
            {
                if (updatingControls) return;
                UiSoundEffects.PlayButtonBlip();
                var s = host.GetHomeState();
                await host.UpdateHostMutatorsAsync(s.HostChaosIntensity, id, s.HostFasterCorpseCleanup, s.HostTheaterPreset);
            };
            seafPills[id] = pill;
            seafWrap.Children.Add(pill);
        }

        corpseCleanupToggle.IsCheckedChanged += async (_, _) =>
        {
            if (updatingControls) return;
            UiSoundEffects.PlayButtonBlip();
            var s = host.GetHomeState();
            await host.UpdateHostMutatorsAsync(s.HostChaosIntensity, s.HostHeavySeaf, corpseCleanupToggle.IsChecked == true, s.HostTheaterPreset);
        };

        var hostCard = new Border
        {
            Background = ForgeTheme.BgElevated,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11, 9),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    ForgeTheme.Label("Applies when you host missions · Squad match code stays identical.", 11, ForgeTheme.TextSecondary),
                    ForgeTheme.Label("Theater Scale (Experimental)", 11.5, ForgeTheme.ClonesAccent, FontWeight.SemiBold),
                    theaterWrap,
                    theaterDescLabel,
                    ForgeTheme.Label("Chaos Invasions", 11.5, ForgeTheme.TextPrimary, FontWeight.SemiBold),
                    chaosWrap,
                    ForgeTheme.Label("Heavy SEAF Reinforcements", 11.5, ForgeTheme.TextPrimary, FontWeight.SemiBold),
                    seafWrap,
                    BuildSwitchRow("Faster Corpse Cleanup", "Clears fallen bodies sooner to protect D10–12 FPS.", corpseCleanupToggle)
                }
            }
        };

        advancedBodyPanel.Children.Add(drawerVisualExtrasSection);
        advancedBodyPanel.Children.Add(ForgeTheme.Label("HOST GAMEPLAY CONTROLS", 10.5, ForgeTheme.TextMuted, FontWeight.Bold));
        advancedBodyPanel.Children.Add(hostCard);

        // Smart Footer (#1)
        var previewStack = new StackPanel { Spacing = 2 };
        previewStack.Children.Add(drawerPreviewBadge);
        previewStack.Children.Add(drawerPreviewDetail);
        drawerPreviewBox.Child = previewStack;

        AutomationProperties.SetAutomationId(drawerApplyButton, "CustomizeApplyButton");
        drawerApplyButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            var s = host.GetHomeState();
            bool needsApply = s.HasInstallation && !s.IsModeAppliedOnDisk && !s.ControlsLocked;
            showingCustomize = false;
            customizeOverlay.IsVisible = false;
            if (needsApply)
                await host.ApplyCustomizationNowAsync();
        };

        var drawerScrollBody = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 12, 10) };
        drawerScrollBody.Children.Add(drawerPerfSection);
        drawerScrollBody.Children.Add(advancedHeaderBorder);
        drawerScrollBody.Children.Add(advancedBodyPanel);

        var drawerFooter = new StackPanel { Spacing = 9, Margin = new Thickness(0, 8, 0, 0) };
        drawerFooter.Children.Add(drawerPreviewBox);
        drawerFooter.Children.Add(drawerApplyButton);

        var drawerDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        drawerDock.Children.Add(header);
        DockPanel.SetDock(drawerFooter, Dock.Bottom);
        drawerDock.Children.Add(drawerFooter);
        drawerScrollViewer = new ScrollViewer
        {
            Content = drawerScrollBody,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        drawerDock.Children.Add(drawerScrollViewer);

        var drawerPanel = new Border
        {
            Width = 440,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = ForgeTheme.BgSurface,
            BorderBrush = ForgeTheme.BorderStrong,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(20, 16),
            Child = drawerDock
        };
        customizeOverlay.Children.Add(drawerPanel);
    }

    static ToggleButton BuildSegmentedPill(string label, bool rightGap)
    {
        var pill = new ToggleButton
        {
            Content = label,
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = ForgeTheme.TextPrimary,
            Background = ForgeTheme.BgCanvas,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 7),
            Margin = new Thickness(0, 0, rightGap ? 5 : 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(pill, label);
        return pill;
    }

    static Grid BuildSwitchRow(string title, string subtitle, ToggleSwitch toggle)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(ForgeTheme.Label(title, 12.5, ForgeTheme.TextPrimary, FontWeight.SemiBold));
        text.Children.Add(ForgeTheme.Label(subtitle, 11, ForgeTheme.TextSecondary));
        toggle.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(text);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    Task ToggleAdvancedSectionAsync()
    {
        UiSoundEffects.PlayButtonBlip();
        showingAdvanced = !showingAdvanced;
        RefreshCustomizeDrawer();
        if (showingAdvanced)
            Dispatcher.UIThread.Post(() => drawerScrollViewer?.ScrollToEnd(), DispatcherPriority.Loaded);
        else
            Dispatcher.UIThread.Post(() => drawerScrollViewer?.ScrollToHome(), DispatcherPriority.Loaded);
        return Task.CompletedTask;
    }

    Control BuildSetupView()
    {
        AutomationProperties.SetAutomationId(setupBackButton, "SetupBackButton");
        AutomationProperties.SetAutomationId(setupHeadline, "SetupHeadline");
        AutomationProperties.SetAutomationId(setupDetail, "SetupDetail");
        AutomationProperties.SetAutomationId(setupPrimaryButton, "SetupPrimaryButton");
        setupBackButton.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            host.ExitSetup();
        };

        var stepper = new UniformGrid { Columns = 4 };
        string[] stepNames = ["1. Space & Steam", "2. Install & Copy", "3. Account Sync", "4. Mod Pack"];
        for (int i = 0; i < 4; i++)
        {
            var lbl = ForgeTheme.Label(stepNames[i], 11.5, ForgeTheme.TextSecondary, FontWeight.SemiBold, TextAlignment.Center, wrap: false);
            var pill = new Border
            {
                Background = ForgeTheme.BgElevated,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 7),
                Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 3 ? 0 : 4, 0),
                Child = lbl
            };
            setupStepPills[i] = pill;
            setupStepLabels[i] = lbl;
            stepper.Children.Add(pill);
        }

        var expectGrid = new UniformGrid { Columns = 4 };
        var expectItems = new[] { ("~70 GB", "Free disk space"), ("15–35 min", "First-time setup"), ("Steam App", "QR code sign-in"), ("2–3 Prompts", "Windows UAC") };
        for (int i = 0; i < expectItems.Length; i++)
        {
            var (k, v) = expectItems[i];
            var tile = new Border
            {
                Background = ForgeTheme.BgElevated,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 9),
                Margin = new Thickness(0, 0, i == expectItems.Length - 1 ? 0 : 8, 0),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        ForgeTheme.Label(k, 13.5, ForgeTheme.ClonesAccent, FontWeight.Bold, TextAlignment.Center, wrap: false),
                        ForgeTheme.Label(v, 11, ForgeTheme.TextSecondary, FontWeight.Normal, TextAlignment.Center)
                    }
                }
            };
            expectGrid.Children.Add(tile);
        }

        setupSteamPill.Child = setupSteamPillText;
        setupSpacePill.Child = setupSpacePillText;
        var checksRow = new WrapPanel { Orientation = Orientation.Horizontal };
        setupSteamPill.Margin = new Thickness(0, 0, 8, 4);
        setupSpacePill.Margin = new Thickness(0, 0, 8, 4);
        checksRow.Children.Add(setupSteamPill);
        checksRow.Children.Add(setupSpacePill);

        setupBrowseCustomButton.Click += async (_, _) => await host.BrowseSetupFolderAsync(existing: false);
        setupUseExistingButton.Click += async (_, _) => await host.BrowseSetupFolderAsync(existing: true);
        var folderActions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        setupFolderDisplay.VerticalAlignment = VerticalAlignment.Center;
        folderActions.Children.Add(setupFolderDisplay);
        Grid.SetColumn(setupBrowseCustomButton, 1);
        folderActions.Children.Add(setupBrowseCustomButton);
        Grid.SetColumn(setupUseExistingButton, 2);
        folderActions.Children.Add(setupUseExistingButton);

        setupStage1Panel.Children.Add(expectGrid);
        setupStage1Panel.Children.Add(checksRow);
        setupStage1Panel.Children.Add(ForgeTheme.Label("CHOOSE INSTALL DRIVE (OUTSIDE STEAM & ONEDRIVE)", 11, ForgeTheme.TextMuted, FontWeight.Bold));
        setupStage1Panel.Children.Add(setupDrivesList);
        setupStage1Panel.Children.Add(folderActions);

        var substepsCard = new Border
        {
            Background = ForgeTheme.BgSurface,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10)
        };
        var substepStack = new StackPanel { Spacing = 5 };
        string[] substepNames = ["Download & install HellForge 0.4.0 (502 MB)", "Copy or download Helldivers 2 build 25480438", "Enable isolated sandbox & verify runner"];
        for (int i = 0; i < 3; i++)
        {
            var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = ForgeTheme.BorderStrong, VerticalAlignment = VerticalAlignment.Center };
            var txt = ForgeTheme.Label(substepNames[i], 12.5, ForgeTheme.TextSecondary, FontWeight.Medium);
            setupSubstepDots[i] = dot;
            setupSubstepTexts[i] = txt;
            var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Children = { dot, txt } };
            substepStack.Children.Add(r);
        }
        substepsCard.Child = substepStack;

        setupQrImage.Width = 156;
        setupQrImage.Height = 156;
        setupQrCard.Padding = new Thickness(14, 10);
        var qrFrame = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = setupQrImage
        };
        var qrText = new StackPanel
        {
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                ForgeTheme.Label("Approve Steam Sign-In", 15, ForgeTheme.TextPrimary, FontWeight.Bold),
                ForgeTheme.Label("1. Open the Steam mobile app on your phone.", 12, ForgeTheme.TextSecondary),
                ForgeTheme.Label("2. Tap the Steam Guard shield → Scan QR code.", 12, ForgeTheme.TextSecondary),
                ForgeTheme.Label("3. Approve sign-in so HellForge can download build 25480438.", 12, ForgeTheme.TextSecondary)
            }
        };
        var qrGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        qrGrid.Children.Add(qrFrame);
        Grid.SetColumn(qrText, 1);
        qrGrid.Children.Add(qrText);
        setupQrCard.Child = qrGrid;

        setupOfficialFallbackButton.HorizontalAlignment = HorizontalAlignment.Left;
        setupOfficialFallbackButton.Click += async (_, _) => await host.OpenOfficialSetupFallbackAsync();

        setupStage2Panel.Spacing = 9;
        setupStage2Panel.Children.Add(substepsCard);
        setupStage2Panel.Children.Add(setupQrCard);
        setupStage2Panel.Children.Add(setupOfficialFallbackButton);

        for (int i = 0; i < 5; i++)
        {
            var badgeText = ForgeTheme.Label((i + 1).ToString(), 11.5, ForgeTheme.TextPrimary, FontWeight.Bold, TextAlignment.Center, wrap: false);
            var badge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = ForgeTheme.BgPill,
                BorderBrush = ForgeTheme.BorderStrong,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = badgeText
            };
            var title = ForgeTheme.Label("", 13, ForgeTheme.TextPrimary, FontWeight.SemiBold);
            var sub = ForgeTheme.Label("", 11.5, ForgeTheme.TextSecondary);
            var textStack = new StackPanel { Spacing = 1, Children = { title, sub } };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            row.Children.Add(badge);
            Grid.SetColumn(textStack, 1);
            row.Children.Add(textStack);
            var card = new Border
            {
                Background = ForgeTheme.BgSurface,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9),
                Child = row
            };
            syncStepCards[i] = card;
            syncStepBadges[i] = badge;
            syncStepBadgeTexts[i] = badgeText;
            syncStepTitles[i] = title;
            syncStepSubtitles[i] = sub;
            setupStage3Panel.Children.Add(card);
        }

        var step4TopGrid = new UniformGrid { Columns = 3 };
        var step4BottomGrid = new UniformGrid { Columns = 2 };
        var step4Specs = new[]
        {
            ("full", "Full (Ultra)", "12 GB+ VRAM · Sharpest"),
            ("lighter", "High (Streamed)", "8–11 GB VRAM · Streamed"),
            ("reduced1024", "1024px (Medium)", "6–8 GB VRAM · 1024px"),
            ("reduced512", "512px (Low / Deck)", "4–6 GB VRAM · 512px"),
            ("potato256", "256px + Decimated Meshes (Ultra-Potato)", "≤ 4 GB / iGPU · Fastest")
        };
        for (int idx = 0; idx < step4Specs.Length; idx++)
        {
            var (id, title, sub) = step4Specs[idx];
            var content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    ForgeTheme.Label(title, 12, ForgeTheme.TextPrimary, FontWeight.Bold, TextAlignment.Center, wrap: false),
                    ForgeTheme.Label(sub, 10.5, ForgeTheme.TextSecondary, FontWeight.Normal, TextAlignment.Center)
                }
            };
            bool inTopRow = idx < 3;
            int colIdx = inTopRow ? idx : idx - 3;
            int colMax = inTopRow ? 2 : 1;
            var btn = new Button
            {
                Content = content,
                Background = ForgeTheme.BgElevated,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 8),
                Margin = new Thickness(colIdx == 0 ? 0 : 4, 0, colIdx == colMax ? 0 : 4, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            AutomationProperties.SetAutomationId(btn, $"SetupTexture-{id}");
            AutomationProperties.SetName(btn, title);
            btn.Click += async (_, _) =>
            {
                UiSoundEffects.PlayButtonBlip();
                await host.SetSetupTextureAsync(id);
            };
            setupStep4TextureButtons[id] = btn;
            if (inTopRow) step4TopGrid.Children.Add(btn);
            else step4BottomGrid.Children.Add(btn);
        }
        var step4TexGrid = new StackPanel { Spacing = 8, Children = { step4TopGrid, step4BottomGrid } };
        setupStep4ConsentBox.Child = setupStep4ConsentText;
        setupStage4Panel.Children.Add(setupStep4ModeText);
        setupStage4Panel.Children.Add(ForgeTheme.Label("CHOOSE TEXTURE QUALITY BEFORE DOWNLOADING", 11, ForgeTheme.TextMuted, FontWeight.Bold));
        setupStage4Panel.Children.Add(step4TexGrid);
        setupStage4Panel.Children.Add(setupStep4ConsentBox);

        var progTop = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        progTop.Children.Add(setupProgressStage);
        Grid.SetColumn(setupProgressMetrics, 1);
        progTop.Children.Add(setupProgressMetrics);
        setupProgressCard.Child = new StackPanel { Spacing = 7, Children = { progTop, setupProgressBar } };

        setupPrimaryButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            await host.TriggerSetupPrimaryActionAsync();
        };
        setupCancelWorkButton.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            host.CancelSetupWork();
        };
        setupCancelWorkButton.IsVisible = false;
        setupCancelWorkButton.Margin = new Thickness(10, 0, 0, 0);

        var ctaGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        ctaGrid.Children.Add(setupPrimaryButton);
        Grid.SetColumn(setupCancelWorkButton, 1);
        ctaGrid.Children.Add(setupCancelWorkButton);

        var topRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, Margin = new Thickness(28, 2, 28, 10) };
        topRow.Children.Add(setupBackButton);
        Grid.SetColumn(stepper, 1);
        topRow.Children.Add(stepper);

        var setupBottom = new StackPanel
        {
            Spacing = 9,
            Margin = new Thickness(28, 8, 28, 6),
            Children = { setupProgressCard, ctaGrid }
        };

        var middleStages = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(28, 0, 28, 4),
            Children =
            {
                new StackPanel { Spacing = 2, Children = { setupHeadline, setupDetail } },
                setupStage1Panel,
                setupStage2Panel,
                setupStage3Panel,
                setupStage4Panel
            }
        };

        var setupDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(topRow, Dock.Top);
        setupDock.Children.Add(topRow);
        DockPanel.SetDock(setupBottom, Dock.Bottom);
        setupDock.Children.Add(setupBottom);
        setupDock.Children.Add(new ScrollViewer
        {
            Content = middleStages,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        return setupDock;
    }

    void BuildSettingsOverlay()
    {
        var backdrop = new Border { Background = Brushes.Transparent };
        backdrop.PointerPressed += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            showingSettings = false;
            settingsOverlay.IsVisible = false;
        };
        settingsOverlay.Children.Add(backdrop);

        var closeBtn = ForgeTheme.GhostButton("✕", 13, new Thickness(10, 5));
        closeBtn.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            showingSettings = false;
            settingsOverlay.IsVisible = false;
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(ForgeTheme.Label("Settings & Diagnostics", 18, ForgeTheme.TextPrimary, FontWeight.Bold));
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(closeBtn);

        AutomationProperties.SetAutomationId(settingsVerifyRepairButton, "SettingsVerifyRepairButton");
        settingsVerifyRepairButton.BorderBrush = ForgeTheme.ClonesAccent;
        settingsVerifyRepairButton.Foreground = ForgeTheme.ClonesAccent;

        settingsOpenFolderButton.Click += (_, _) => host.OpenInstallFolder();
        settingsLocateButton.Click += async (_, _) => { await host.LocateInstallAsync(); await LoadSettingsDiagnosticsAsync(); };
        settingsManagerButton.Click += async (_, _) => await host.OpenManagerAsync();
        settingsGuidedSetupButton.Click += async (_, _) => { showingSettings = false; settingsOverlay.IsVisible = false; await host.EnterSetupAsync(); };
        settingsCleanCacheButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            settingsCleanCacheButton.IsEnabled = false;
            settingsCleanCacheButton.Content = "Cleaning…";
            try
            {
                await host.CleanCacheAsync();
                await LoadSettingsDiagnosticsAsync();
            }
            finally
            {
                settingsCleanCacheButton.Content = "Clean Cache";
            }
        };
        settingsVerifyRepairButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            showingSettings = false;
            settingsOverlay.IsVisible = false;
            await host.VerifyAndRepairAsync();
        };
        settingsSyncAccountButton.Click += async (_, _) => { showingSettings = false; settingsOverlay.IsVisible = false; await host.SyncAccountFromSettingsAsync(); };
        settingsCheckUpdatesButton.Click += async (_, _) => { await host.CheckUpdatesAsync(); await LoadSettingsDiagnosticsAsync(); };

        settingsSfxCheck.IsCheckedChanged += (_, _) =>
        {
            if (updatingControls) return;
            var desired = settingsSfxCheck.IsChecked == true;
            if (host.GetHomeState().SoundEffectsEnabled != desired)
            {
                host.ToggleSoundEffects();
                if (desired) UiSoundEffects.PlayButtonBlip();
                RefreshAll();
            }
        };
        settingsDiscordCheck.IsCheckedChanged += (_, _) =>
        {
            if (updatingControls) return;
            UiSoundEffects.PlayButtonBlip();
            host.SetDiscordRpcEnabled(settingsDiscordCheck.IsChecked == true);
        };
        settingsTrayCheck.IsCheckedChanged += (_, _) =>
        {
            if (updatingControls) return;
            UiSoundEffects.PlayButtonBlip();
            var on = settingsTrayCheck.IsChecked == true;
            minimizeToTrayActive = on;
            host.SetMinimizeToTray(on);
        };

        settingsCopyReportButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            try
            {
                if (Clipboard is { } cb)
                {
                    var text = !string.IsNullOrWhiteSpace(latestDiscordReport) ? latestDiscordReport : (settingsReportBox.Text ?? "");
                    await cb.SetTextAsync(text);
                    settingsCopyReportButton.Content = "Copied for Discord!";
                    await Task.Delay(1600);
                    settingsCopyReportButton.Content = "Copy Report";
                }
            }
            catch (Exception ex) { settingsUpdateNote.Text = "Couldn't copy to clipboard: " + ex.Message; }
        };
        settingsSaveReportButton.Click += async (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            try
            {
                var saved = await host.SaveSupportReportAsync();
                if (!string.IsNullOrWhiteSpace(saved))
                    settingsUpdateNote.Text = "Support report saved to " + saved;
            }
            catch (Exception ex) { settingsUpdateNote.Text = "Couldn't save report: " + ex.Message; }
        };

        var installCard = new Border
        {
            Background = ForgeTheme.BgElevated,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    ForgeTheme.Label("HELLFORGE INSTALLATION & STORAGE", 10.5, ForgeTheme.TextMuted, FontWeight.Bold),
                    settingsInstallPath,
                    settingsDiskUsageText,
                    new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            WithMargin(settingsOpenFolderButton, new Thickness(0, 0, 6, 4)),
                            WithMargin(settingsLocateButton, new Thickness(0, 0, 6, 4)),
                            WithMargin(settingsManagerButton, new Thickness(0, 0, 6, 4)),
                            WithMargin(settingsGuidedSetupButton, new Thickness(0, 0, 6, 4)),
                            WithMargin(settingsCleanCacheButton, new Thickness(0, 0, 6, 4)),
                            WithMargin(settingsVerifyRepairButton, new Thickness(0, 0, 0, 4))
                        }
                    }
                }
            }
        };

        var prefsWrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                WithMargin(settingsSfxCheck, new Thickness(0, 0, 18, 2)),
                WithMargin(settingsDiscordCheck, new Thickness(0, 0, 18, 2)),
                WithMargin(settingsTrayCheck, new Thickness(0, 0, 0, 2))
            }
        };

        var accountUpdateCard = new Border
        {
            Background = ForgeTheme.BgElevated,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    ForgeTheme.Label("ACCOUNT, UPDATES & PREFERENCES", 10.5, ForgeTheme.TextMuted, FontWeight.Bold),
                    settingsAccountSummary,
                    settingsUpdateNote,
                    new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            WithMargin(settingsSyncAccountButton, new Thickness(0, 0, 8, 4)),
                            WithMargin(settingsCheckUpdatesButton, new Thickness(0, 0, 0, 4))
                        }
                    },
                    prefsWrap
                }
            }
        };

        var reportActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { settingsCopyReportButton, settingsSaveReportButton }
        };

        var modalScrollBody = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 8, 12, 4),
            Children =
            {
                installCard,
                accountUpdateCard,
                ForgeTheme.Label("HEALTH CHECKS", 10.5, ForgeTheme.TextMuted, FontWeight.Bold),
                settingsHealthWrap,
                ForgeTheme.Label("DISCORD SUPPORT REPORT", 10.5, ForgeTheme.TextMuted, FontWeight.Bold),
                settingsReportBox
            }
        };

        var modalDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        modalDock.Children.Add(header);
        DockPanel.SetDock(reportActions, Dock.Bottom);
        modalDock.Children.Add(reportActions);
        modalDock.Children.Add(new ScrollViewer
        {
            Content = modalScrollBody,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var modalCard = new Border
        {
            Width = 684,
            MaxHeight = 648,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = ForgeTheme.BgSurface,
            BorderBrush = ForgeTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 14),
            Child = modalDock
        };
        settingsOverlay.Children.Add(modalCard);
    }

    void BuildWhatsNewOverlay()
    {
        var backdrop = new Border { Background = Brushes.Transparent };
        backdrop.PointerPressed += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            CloseWhatsNewOverlay();
        };
        whatsNewOverlay.Children.Add(backdrop);

        var closeIconBtn = ForgeTheme.GhostButton("✕", 12, new Thickness(9, 4));
        closeIconBtn.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            CloseWhatsNewOverlay();
        };

        whatsNewOpenWebButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(currentWhatsNewUrl) &&
                currentWhatsNewUrl.StartsWith("https://github.com/owendavidgoode/clonedivers/", StringComparison.Ordinal))
            {
                try { Process.Start(new ProcessStartInfo(currentWhatsNewUrl) { UseShellExecute = true }); }
                catch { }
            }
        };

        whatsNewCloseButton.Width = 120;
        whatsNewCloseButton.HorizontalAlignment = HorizontalAlignment.Right;
        whatsNewCloseButton.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            CloseWhatsNewOverlay();
        };

        var topHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        topHeader.Children.Add(new StackPanel { Spacing = 2, Children = { whatsNewTitle, whatsNewSubtitle } });
        Grid.SetColumn(closeIconBtn, 1);
        closeIconBtn.VerticalAlignment = VerticalAlignment.Top;
        topHeader.Children.Add(closeIconBtn);

        var bottomBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        whatsNewOpenWebButton.VerticalAlignment = VerticalAlignment.Center;
        bottomBar.Children.Add(whatsNewOpenWebButton);
        Grid.SetColumn(whatsNewCloseButton, 1);
        bottomBar.Children.Add(whatsNewCloseButton);

        var card = new Border
        {
            Width = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = ForgeTheme.BgSurface,
            BorderBrush = ForgeTheme.ClonesAccent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    topHeader,
                    ForgeTheme.HoloAccentLine(ForgeTheme.ClonesBarMuted, new Thickness(0)),
                    whatsNewBulletsPanel,
                    bottomBar
                }
            }
        };
        whatsNewOverlay.Children.Add(card);
    }

    async Task OpenWhatsNewAsync(string? versionOrTag)
    {
        showingWhatsNew = true;
        whatsNewTitle.Text = string.IsNullOrWhiteSpace(versionOrTag) ? "What's New" : $"What's New in {WhatsNewFetcher.NormalizeTag(versionOrTag)}";
        whatsNewSubtitle.Text = "Loading release highlights…";
        whatsNewBulletsPanel.Children.Clear();
        whatsNewOverlay.IsVisible = true;

        var result = await host.FetchWhatsNewAsync(versionOrTag);
        if (!showingWhatsNew) return;

        currentWhatsNewUrl = result.ReleaseUrl;
        whatsNewTitle.Text = $"What's New in {result.Tag}";
        whatsNewSubtitle.Text = result.IsFallback
            ? "Key highlights included in this launcher update:"
            : "Highlights from the official GitHub release:";

        whatsNewBulletsPanel.Children.Clear();
        foreach (var bullet in result.Bullets)
        {
            var marker = ForgeTheme.Label("◆", 10.5, ForgeTheme.ClonesAccent, FontWeight.Bold, wrap: false);
            marker.Margin = new Thickness(0, 2, 0, 0);
            var txt = ForgeTheme.Label(bullet, 12.5, ForgeTheme.TextPrimary);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            row.Children.Add(marker);
            Grid.SetColumn(txt, 1);
            row.Children.Add(txt);
            whatsNewBulletsPanel.Children.Add(row);
        }
    }

    void CloseWhatsNewOverlay()
    {
        showingWhatsNew = false;
        whatsNewOverlay.IsVisible = false;
    }

    void BuildConfirmOverlay()
    {
        confirmCancelButton.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            ResolveConfirm(false);
        };
        confirmOkButton.Width = 140;
        confirmOkButton.HorizontalAlignment = HorizontalAlignment.Right;
        confirmOkButton.Click += (_, _) =>
        {
            UiSoundEffects.PlayButtonBlip();
            ResolveConfirm(true);
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { confirmCancelButton, confirmOkButton }
        };

        var card = new Border
        {
            Width = 480,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = ForgeTheme.BgSurface,
            BorderBrush = ForgeTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 20),
            Child = new StackPanel { Spacing = 14, Children = { confirmTitle, confirmBody, buttons } }
        };
        confirmOverlay.Children.Add(card);
    }

    static T WithMargin<T>(T control, Thickness margin) where T : Control { control.Margin = margin; return control; }

    Task<bool> ShowConfirmOverlayAsync(ForgeConfirmRequest request)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            pendingConfirm?.TrySetResult(false);
            pendingConfirm = tcs;
            confirmTitle.Text = request.Title;
            confirmBody.Text = request.Body;
            confirmOkButton.Content = request.ConfirmLabel;
            confirmCancelButton.Content = request.CancelLabel;
            confirmOkButton.Background = request.IsDanger ? ForgeTheme.CtaDanger : ForgeTheme.CtaPrimary;
            confirmOkButton.Foreground = request.IsDanger ? ForgeTheme.TextPrimary : ForgeTheme.TextOnAccent;
            confirmOverlay.IsVisible = true;
            confirmOkButton.Focus();
        });
        return tcs.Task;
    }

    void ResolveConfirm(bool accepted)
    {
        confirmOverlay.IsVisible = false;
        var tcs = pendingConfirm;
        pendingConfirm = null;
        tcs?.TrySetResult(accepted);
    }

    async Task<string?> PickFolderAsync(string title)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    async Task<string?> SaveFileAsync(string title, string suggestedFileName)
    {
        var ext = Path.GetExtension(suggestedFileName).TrimStart('.');
        var saved = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = string.IsNullOrEmpty(ext) ? "txt" : ext
        });
        return saved?.TryGetLocalPath();
    }

    async Task LoadSettingsDiagnosticsAsync()
    {
        var diag = await host.GetDiagnosticsAsync();
        updatingControls = true;
        try
        {
            settingsInstallPath.Text = diag.InstallRoot ?? "No HellForge folder selected";
            settingsDiskUsageText.Text = diag.DiskUsageSummary;
            settingsDiskUsageText.IsVisible = !string.IsNullOrWhiteSpace(diag.DiskUsageSummary);
            settingsAccountSummary.Text = diag.AccountSummary;
            settingsUpdateNote.Text = diag.UpdateMessage ?? $"Launcher v{diag.AppVersion}";
            settingsUpdateNote.IsVisible = true;
            settingsOpenFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(diag.InstallRoot);
            settingsManagerButton.IsEnabled = diag.CanOpenManager;
            settingsGuidedSetupButton.IsVisible = host.SupportsEmbeddedSetup;
            settingsGuidedSetupButton.IsEnabled = diag.CanOpenSetup;
            settingsCleanCacheButton.IsEnabled = diag.CanCleanCache;
            settingsVerifyRepairButton.IsEnabled = diag.CanVerifyAndRepair;
            settingsSyncAccountButton.IsEnabled = diag.CanSyncAccount;
            settingsCheckUpdatesButton.IsEnabled = diag.CanCheckUpdates && !diag.IsCheckingUpdates;
            settingsCheckUpdatesButton.Content = diag.IsCheckingUpdates ? "Checking…" : "Check Updates";

            settingsSfxCheck.IsChecked = diag.SoundEffectsEnabled;
            settingsDiscordCheck.IsChecked = diag.DiscordRpcEnabled;
            settingsTrayCheck.IsChecked = diag.MinimizeToTray;
            minimizeToTrayActive = diag.MinimizeToTray;

            settingsHealthWrap.Children.Clear();
            foreach (var item in diag.HealthChecks)
            {
                var bg = item.Healthy ? ForgeTheme.SuccessBg : item.Warning ? ForgeTheme.WarningBg : ForgeTheme.DangerBg;
                var fg = item.Healthy ? ForgeTheme.SuccessText : item.Warning ? ForgeTheme.WarningText : ForgeTheme.DangerText;
                var border = item.Healthy ? ForgeTheme.SuccessBorder : item.Warning ? ForgeTheme.WarningBorder : ForgeTheme.DangerBorder;
                var pill = ForgeTheme.Pill($"{item.Name}: {item.Status}", bg, fg, border, 11.5);
                pill.Margin = new Thickness(0, 0, 8, 6);
                settingsHealthWrap.Children.Add(pill);
            }

            latestDiscordReport = diag.DiscordReportText;
            settingsReportBox.Text = !string.IsNullOrWhiteSpace(diag.DiscordReportText) ? diag.DiscordReportText : diag.RawReportText;
        }
        finally { updatingControls = false; }
    }

    void RefreshAll()
    {
        homeView.IsVisible = !showingSetup;
        setupView.IsVisible = showingSetup;
        customizeOverlay.IsVisible = showingCustomize;
        settingsOverlay.IsVisible = showingSettings;
        whatsNewOverlay.IsVisible = showingWhatsNew;

        var home = host.GetHomeState();
        minimizeToTrayActive = home.MinimizeToTray;
        headerSubtitle.Text = home.PlatformTag;
        muteButton.Content = home.SoundEffectsEnabled ? "🔊" : "🔇";
        muteButton.Foreground = home.SoundEffectsEnabled ? ForgeTheme.ClonesAccent : ForgeTheme.TextMuted;
        versionFooter.Text = string.IsNullOrWhiteSpace(home.SquadMatchHash)
            ? $"v{home.AppVersion}"
            : $"{home.SquadMatchHash} · v{home.AppVersion}";

        ApplyBadgeTone(headerStatusBadge, headerStatusText, home.HeaderBadgeText, home.HeaderBadgeTone);

        if (trayEndSessionItem != null)
        {
            bool canCancelLaunch = home.IsSessionRunning &&
                !home.IsBusy &&
                string.Equals(home.SecondaryActionLabel, ForgeTacticalCopy.CancelLaunchButton, StringComparison.Ordinal);
            trayEndSessionItem.Header = canCancelLaunch ? ForgeTacticalCopy.CancelLaunchButton : "End Session";
            trayEndSessionItem.IsEnabled = canCancelLaunch || (home.IsSessionRunning && home.PrimaryActionEnabled);
        }

        var gameWindowActive = home.IsSessionRunning && !home.IsBusy && home.Progress == null;
        if (!host.IsPreview)
        {
            if (gameWindowActive && !wasGameWindowActive && minimizeToTrayActive)
                HideToTray();
            else if (!home.IsSessionRunning && wasSessionRunning && !IsVisible)
                RestoreFromTray();
        }
        wasSessionRunning = home.IsSessionRunning;
        wasGameWindowActive = gameWindowActive;

        // Update banner (never hijacks Play!)
        if (home.UpdateBanner is { } banner && !showingSetup)
        {
            updateBanner.IsVisible = true;
            updateBanner.Background = banner.IsFailed ? ForgeTheme.DangerBg : ForgeTheme.InfoBg;
            updateBanner.BorderBrush = banner.IsFailed ? ForgeTheme.DangerBorder : ForgeTheme.InfoBorder;
            updateBannerText.Text = banner.Message;
            updateBannerAction.Content = banner.ActionLabel;
            updateBannerAction.IsEnabled = !home.IsBusy;
            updateBannerWhatsNew.IsVisible = !banner.IsRestartReady;
            updateBannerDismiss.IsVisible = !banner.IsRestartReady;
        }
        else
        {
            updateBanner.IsVisible = false;
        }

        // Step-Down Banner (#29)
        if (home.StepDownBanner is { } stepDown && !showingSetup && !home.IsBusy && !home.IsSessionRunning)
        {
            stepDownBanner.IsVisible = true;
            stepDownBannerText.Text = stepDown.Reason;
            stepDownAcceptButton.Content = stepDown.ActionLabel;
        }
        else
        {
            stepDownBanner.IsVisible = false;
        }

        // Smart Self-Install Banner (#31)
        if (home.SelfInstallBanner is { } selfInstall && !showingSetup && !home.IsBusy && !home.IsSessionRunning)
        {
            selfInstallBanner.IsVisible = true;
            selfInstallBannerText.Text = selfInstall.Message;
            selfInstallAcceptButton.Content = selfInstall.ActionLabel;
        }
        else
        {
            selfInstallBanner.IsVisible = false;
        }

        RefreshHome(home);
        if (showingCustomize) RefreshCustomizeDrawer();
        if (showingSetup) RefreshSetup(host.GetSetupState());
    }

    void ApplyModeCardVisuals(ForgeHomeState s)
    {
        foreach (var (id, card) in modeCards)
        {
            bool selected = string.Equals(s.Mode, id, StringComparison.OrdinalIgnoreCase);
            bool hovered = !s.ControlsLocked && string.Equals(hoveredModeId, id, StringComparison.OrdinalIgnoreCase);
            var accent = ForgeTheme.AccentForMode(id);

            card.Background = selected
                ? ForgeTheme.BgSelected
                : hovered ? ForgeTheme.BgSurfaceHover : ForgeTheme.BgSurface;
            card.BorderBrush = selected
                ? accent
                : hovered ? ForgeTheme.BorderStrong : ForgeTheme.BorderSubtle;
            modeTopBars[id].Background = selected || hovered ? accent : ForgeTheme.MutedBarForMode(id);
            modeButtons[id].IsEnabled = !s.ControlsLocked;

            var badge = modeBadges[id];
            var badgeText = modeBadgeTexts[id];
            badge.IsVisible = true;
            if (selected)
            {
                badge.Background = ForgeTheme.GlowForMode(id);
                badge.BorderBrush = accent;
                badgeText.Foreground = accent;
                badgeText.Text = s.IsModeAppliedOnDisk ? "ACTIVE" : "SELECTED";
            }
            else
            {
                badge.Background = Brushes.Transparent;
                badge.BorderBrush = Brushes.Transparent;
                badgeText.Foreground = hovered ? ForgeTheme.TextSecondary : ForgeTheme.TextMuted;
                badgeText.Text = "CLICK TO SELECT";
            }
        }
    }

    void RefreshHome(ForgeHomeState s)
    {
        ApplyModeCardVisuals(s);

        statusDeckTopLine.Background = ForgeTheme.MutedBarForMode(s.Mode);

        // Concise Loadout summary strip (#1, #10, #25)
        customizeSummaryTitle.Text = s.Mode == "regular"
            ? "STANDARD LOADOUT"
            : s.Mode == "empire" ? "EMPIRE LOADOUT" : "CLONES LOADOUT";
        customizeSummaryText.Text = ForgeTacticalCopy.FormatLoadoutSummary(
            s.Mode,
            s.EnemySkin,
            s.DeltaSquad,
            s.Covenant,
            s.AutoPerformanceTier,
            s.PerformanceTier,
            s.Hardware,
            s.HostChaosIntensity,
            s.HostHeavySeaf,
            s.HostFasterCorpseCleanup,
            s.SquidSkin,
            s.DeathTrooperVoice,
            s.HostTheaterPreset);
        customizeOpenButton.Content = s.Mode == "regular" ? "Host Rules ▸" : "Customize ▸";
        customizeOpenButton.IsVisible = true;
        customizeOpenButton.IsEnabled = !s.ControlsLocked;

        var preview = s.SizePreview;
        bool isResume = preview.BadgeText.StartsWith("Resume:", StringComparison.OrdinalIgnoreCase);
        bool cached = preview.DownloadBytes == 0;
        customizeSizePill.Background = cached ? ForgeTheme.SuccessBg : isResume ? ForgeTheme.InfoBg : ForgeTheme.WarningBg;
        customizeSizePill.BorderBrush = cached ? ForgeTheme.SuccessBorder : isResume ? ForgeTheme.InfoBorder : ForgeTheme.WarningBorder;
        customizeSizePillText.Foreground = cached ? ForgeTheme.SuccessText : isResume ? ForgeTheme.InfoText : ForgeTheme.WarningText;
        customizeSizePillText.Text = preview.BadgeText;

        // Fixed-height Status & Progress Deck (also pulses indeterminately during pre-window HD2 launch staging!)
        if ((s.IsBusy || s.IsSessionRunning) && s.Progress is { } prog)
        {
            statusDot.Background = ForgeTheme.ClonesAccent;
            statusHeadline.Text = prog.Stage;
            statusDetail.Text = string.IsNullOrWhiteSpace(prog.SubStep) ? s.Detail : prog.SubStep;
            statusMetrics.Text = prog.FormatMetrics();
            statusMetrics.IsVisible = !string.IsNullOrWhiteSpace(statusMetrics.Text);
            homeProgress.IsVisible = true;
            homeProgress.IsIndeterminate = prog.IsIndeterminate || prog.Fraction == null;
            if (prog.Fraction is { } f) homeProgress.Value = f * 100d;
            homeProgress.Foreground = ForgeTheme.AccentForMode(s.Mode);
        }
        else
        {
            var dotTone = s.HeaderBadgeTone is ForgeHeroTone.Warning or ForgeHeroTone.Danger
                ? s.HeaderBadgeTone
                : s.PrimaryActionTone;
            statusDot.Background = ToneBrush(dotTone);
            statusHeadline.Text = s.Headline;
            statusDetail.Text = s.Detail;
            statusMetrics.IsVisible = false;
            homeProgress.IsVisible = false;
        }

        toastText.Text = s.ToastNotice ?? "";
        toastText.IsVisible = !string.IsNullOrWhiteSpace(s.ToastNotice);

        // Hero Primary Button
        heroButton.Content = s.PrimaryActionLabel;
        heroButton.IsEnabled = s.PrimaryActionEnabled;
        AutomationProperties.SetName(heroButton, s.PrimaryActionLabel);
        ApplyCtaTone(heroButton, s.PrimaryActionTone, s.Mode, s.PrimaryActionEnabled);

        if (!string.IsNullOrWhiteSpace(s.SecondaryActionLabel))
        {
            secondaryButton.Content = s.SecondaryActionLabel;
            AutomationProperties.SetName(secondaryButton, s.SecondaryActionLabel);
            secondaryButton.IsVisible = true;
            secondaryButton.IsEnabled = !s.IsBusy;
        }
        else
        {
            secondaryButton.IsVisible = false;
        }
    }

    void RefreshCustomizeDrawer()
    {
        updatingControls = true;
        try
        {
            var s = host.GetHomeState();
            bool isRegular = string.Equals(s.Mode, "regular", StringComparison.OrdinalIgnoreCase);

            drawerTitle.Text = isRegular ? "Host Gameplay Rules" : s.Mode == "empire" ? "Customize Empire" : "Customize Clones";
            drawerSubtitle.Text = isRegular
                ? "Configure mission intensity when you host HellForge co-op."
                : "Auto-tuned for your GPU, with optional overrides below.";

            drawerPerfSection.IsVisible = !isRegular;
            drawerVisualExtrasSection.IsVisible = !isRegular;
            advancedHeaderBorder.IsVisible = !isRegular;

            drawerHardwareText.Text = ForgeTacticalCopy.FormatHardwareDetectionLine(s.Hardware);
            autoTierToggle.IsChecked = s.AutoPerformanceTier;
            autoTierToggle.IsEnabled = !s.ControlsLocked;

            var activeConcreteTier = PerformanceTierAdvisor.NormalizeConcreteTier(
                s.AutoPerformanceTier ? PerformanceTierAdvisor.Auto : s.PerformanceTier,
                s.Hardware);

            foreach (var tier in PerformanceTierAdvisor.Tiers)
            {
                bool selected = string.Equals(activeConcreteTier, tier.Id, StringComparison.OrdinalIgnoreCase);
                bool isRecommended = string.Equals(s.Hardware.RecommendedTierId, tier.Id, StringComparison.OrdinalIgnoreCase);

                var row = tierRows[tier.Id];
                row.IsEnabled = !s.ControlsLocked;
                row.Background = selected ? ForgeTheme.BgSelected : ForgeTheme.BgCanvas;
                row.BorderBrush = selected ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
                row.Opacity = s.ControlsLocked ? 0.6 : 1.0;

                var dot = tierDots[tier.Id];
                dot.Background = selected ? ForgeTheme.ClonesAccent : Brushes.Transparent;
                dot.BorderBrush = selected ? ForgeTheme.ClonesAccent : ForgeTheme.BorderStrong;

                var rec = tierRecPills[tier.Id];
                rec.IsVisible = isRecommended;
            }

            bool showAdv = isRegular || showingAdvanced;
            advancedHeaderLabel.Text = showAdv ? "Advanced ▾" : "Advanced ▸";
            advancedHeaderBorder.BorderBrush = showAdv ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
            advancedBodyPanel.IsVisible = showAdv;

            foreach (var (id, pill) in enemyPills)
            {
                bool on = string.Equals(s.EnemySkin, id, StringComparison.OrdinalIgnoreCase);
                pill.IsChecked = on;
                pill.Background = on ? ForgeTheme.BgSelected : ForgeTheme.BgCanvas;
                pill.BorderBrush = on ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
                pill.IsEnabled = !s.ControlsLocked;
            }

            foreach (var (id, pill) in squidPills)
            {
                bool on = string.Equals(s.SquidSkin, id, StringComparison.OrdinalIgnoreCase);
                pill.IsChecked = on;
                pill.Background = on ? ForgeTheme.BgSelected : ForgeTheme.BgCanvas;
                pill.BorderBrush = on ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
                pill.IsEnabled = !s.ControlsLocked;
            }

            deltaRow.IsVisible = s.Mode == "clonedivers";
            deltaToggle.IsChecked = s.DeltaSquad;
            deltaToggle.IsEnabled = !s.ControlsLocked;

            deathTrooperRow.IsVisible = s.Mode == "empire";
            deathTrooperToggle.IsChecked = s.DeathTrooperVoice;
            deathTrooperToggle.IsEnabled = !s.ControlsLocked;

            bool isEmpireMode = string.Equals(s.Mode, "empire", StringComparison.OrdinalIgnoreCase);
            var d12PillLabel = isEmpireMode ? "L12 ULTRAKILL · Stormtrooper" : "L12 ULTRAKILL · Clone Wars";
            if (theaterPills.TryGetValue("clone-wars", out var d12Pill))
            {
                d12Pill.Content = d12PillLabel;
                AutomationProperties.SetName(d12Pill, d12PillLabel);
            }
            theaterDescLabel.Text = isEmpireMode
                ? "Difficulty 12 ('LEVEL 12: STORMTROOPER') unlocks a 2.79 km² map, 2–3k hybrid Imperial battalions, and 0.0s turbo corpse cleanup."
                : "Difficulty 12 ('LEVEL 12: CLONE WARS') unlocks a 2.79 km² map, 2–3k hybrid Clone Wars battalions, and 0.0s turbo corpse cleanup.";

            bool isUltrakillTheater = HostMutatorConfig.IsUltrakillTheater(s.HostTheaterPreset, s.HostChaosIntensity);
            foreach (var (id, pill) in theaterPills)
            {
                bool on = id == "clone-wars" ? isUltrakillTheater : !isUltrakillTheater;
                pill.IsChecked = on;
                pill.Background = on ? ForgeTheme.BgSelected : ForgeTheme.BgCanvas;
                pill.BorderBrush = on ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
                pill.IsEnabled = !s.ControlsLocked;
            }

            foreach (var (id, pill) in chaosPills)
            {
                bool on = string.Equals(s.HostChaosIntensity, id, StringComparison.OrdinalIgnoreCase);
                pill.IsChecked = on;
                pill.Background = on ? ForgeTheme.BgSelected : ForgeTheme.BgCanvas;
                pill.BorderBrush = on ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
                pill.IsEnabled = !s.ControlsLocked;
            }

            foreach (var (id, pill) in seafPills)
            {
                bool on = string.Equals(s.HostHeavySeaf, id, StringComparison.OrdinalIgnoreCase);
                pill.IsChecked = on;
                pill.Background = on ? ForgeTheme.BgSelected : ForgeTheme.BgCanvas;
                pill.BorderBrush = on ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
                pill.IsEnabled = !s.ControlsLocked;
            }

            corpseCleanupToggle.IsChecked = s.HostFasterCorpseCleanup;
            corpseCleanupToggle.IsEnabled = !s.ControlsLocked;

            var p = s.SizePreview;
            bool cached = p.DownloadBytes == 0;
            drawerPreviewBox.Background = cached ? ForgeTheme.SuccessBg : ForgeTheme.WarningBg;
            drawerPreviewBox.BorderBrush = cached ? ForgeTheme.SuccessBorder : ForgeTheme.WarningBorder;
            drawerPreviewBadge.Foreground = cached ? ForgeTheme.SuccessText : ForgeTheme.WarningText;
            drawerPreviewBadge.Text = p.BadgeText;
            drawerPreviewDetail.Text = p.DetailText;

            // Smart single primary button (#1)
            string applyLabel;
            ForgeHeroTone applyTone;
            if (!s.HasInstallation || s.IsModeAppliedOnDisk)
            {
                applyLabel = "Done";
                applyTone = ForgeHeroTone.Ready;
            }
            else if (!isRegular && p.DownloadBytes > 0)
            {
                applyLabel = $"Apply & Download (~{Pack.FormatBytes(p.DownloadBytes)})";
                applyTone = ForgeHeroTone.Action;
            }
            else
            {
                applyLabel = "Apply Now";
                applyTone = ForgeHeroTone.Action;
            }

            drawerApplyButton.Content = applyLabel;
            AutomationProperties.SetName(drawerApplyButton, applyLabel);
            drawerApplyButton.IsEnabled = !s.ControlsLocked;
            ApplyCtaTone(drawerApplyButton, applyTone, s.Mode, !s.ControlsLocked);
        }
        finally { updatingControls = false; }
    }

    void RefreshSetup(ForgeSetupState st)
    {
        setupBackButton.IsEnabled = !st.IsBusy;
        setupHeadline.Text = st.Headline;
        setupDetail.Text = st.Detail;

        for (int i = 0; i < 4; i++)
        {
            int num = i + 1;
            bool done = num < st.Step || (num == 4 && st.PackReady);
            bool active = num == st.Step && !done;
            setupStepPills[i].Background = done ? ForgeTheme.SuccessBg : active ? ForgeTheme.InfoBg : ForgeTheme.BgElevated;
            setupStepPills[i].BorderBrush = done ? ForgeTheme.SuccessBorder : active ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
            setupStepLabels[i].Foreground = done ? ForgeTheme.SuccessText : active ? ForgeTheme.TextPrimary : ForgeTheme.TextMuted;
        }

        setupStage1Panel.IsVisible = st.Step == 1;
        setupStage2Panel.IsVisible = st.Step == 2;
        setupStage3Panel.IsVisible = st.Step == 3;
        setupStage4Panel.IsVisible = st.Step == 4;

        if (st.Step == 1)
        {
            ApplyBadgeTone(setupSteamPill, setupSteamPillText, st.SteamReady ? "Steam: Signed in" : "Steam: Sign in required", st.SteamReady ? ForgeHeroTone.Ready : ForgeHeroTone.Warning);
            ApplyBadgeTone(setupSpacePill, setupSpacePillText, st.SpaceSummary, st.SpaceOk ? ForgeHeroTone.Ready : ForgeHeroTone.Warning);
            setupFolderDisplay.Text = "Install folder: " + st.InstallFolder;
            setupBrowseCustomButton.IsEnabled = !st.IsBusy;
            setupUseExistingButton.IsEnabled = !st.IsBusy;

            setupDrivesList.Children.Clear();
            foreach (var d in st.Drives)
            {
                bool selected = string.Equals(d.Root, st.SelectedDriveRoot, StringComparison.OrdinalIgnoreCase);
                var bar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = d.UsedFraction * 100d,
                    Height = 5,
                    CornerRadius = new CornerRadius(3),
                    Background = ForgeTheme.BgTrack,
                    Foreground = d.MeetsBudget ? ForgeTheme.ClonesAccent : ForgeTheme.Warning
                };
                var badgeText = d.IsRecommended ? "RECOMMENDED" : d.MeetsBudget ? "OK" : "LOW SPACE";
                var badge = ForgeTheme.Pill(badgeText,
                    d.MeetsBudget ? (d.IsRecommended ? ForgeTheme.InfoBg : ForgeTheme.SuccessBg) : ForgeTheme.WarningBg,
                    d.MeetsBudget ? (d.IsRecommended ? ForgeTheme.InfoText : ForgeTheme.SuccessText) : ForgeTheme.WarningText,
                    d.MeetsBudget ? (d.IsRecommended ? ForgeTheme.InfoBorder : ForgeTheme.SuccessBorder) : ForgeTheme.WarningBorder, 10);

                var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                top.Children.Add(ForgeTheme.Label(
                    $"{d.Root.TrimEnd('\\', '/')} {(string.IsNullOrWhiteSpace(d.Label) ? "" : $"({d.Label})")}  ·  {Pack.FormatBytes(d.FreeBytes)} free of {Pack.FormatBytes(d.TotalBytes)}",
                    12.5, ForgeTheme.TextPrimary, FontWeight.SemiBold));
                Grid.SetColumn(badge, 1);
                top.Children.Add(badge);

                var card = new Border
                {
                    Background = selected ? ForgeTheme.BgSelected : ForgeTheme.BgSurface,
                    BorderBrush = selected ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle,
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(12, 9),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new StackPanel { Spacing = 6, Children = { top, bar } }
                };
                var rootCopy = d.Root;
                card.PointerPressed += (_, _) => { if (!st.IsBusy) { UiSoundEffects.PlayButtonBlip(); host.SelectSetupDrive(rootCopy); } };
                setupDrivesList.Children.Add(card);
            }
        }

        if (st.Step == 2)
        {
            bool s0Done = st.HasInstallation && !st.ForgeUpgradeAvailable;
            bool s1Done = st.GameCopyReady;
            bool s2Done = st.GameCopyReady && !st.IsBusy;
            SetSubstepState(0, s0Done, !s0Done && st.IsBusy);
            SetSubstepState(1, s1Done, s0Done && !s1Done && st.IsBusy);
            SetSubstepState(2, s2Done, s0Done && s1Done && st.IsBusy);

            if (st.QrRows is { Length: > 0 } rows)
            {
                if (!ReferenceEquals(rows, lastQrRows))
                {
                    currentQrBitmap?.Dispose();
                    currentQrBitmap = ForgeTheme.RenderQrBitmap(rows, 210);
                    setupQrImage.Source = currentQrBitmap;
                    lastQrRows = rows;
                }
                setupQrCard.IsVisible = true;
            }
            else
            {
                setupQrCard.IsVisible = false;
                if (lastQrRows != null)
                {
                    setupQrImage.Source = null;
                    currentQrBitmap?.Dispose();
                    currentQrBitmap = null;
                    lastQrRows = null;
                }
            }
            setupOfficialFallbackButton.IsVisible = st.ShowOfficialFallback;
        }

        if (st.Step == 3)
        {
            var steps = st.AccountSync.Steps;
            for (int i = 0; i < Math.Min(5, steps.Count); i++)
            {
                var item = steps[i];
                syncStepTitles[i].Text = item.Title;
                syncStepSubtitles[i].Text = item.Subtitle;
                syncStepCards[i].Background = item.Active ? ForgeTheme.BgSelected : item.Completed ? ForgeTheme.SuccessBg : ForgeTheme.BgSurface;
                syncStepCards[i].BorderBrush = item.Active ? ForgeTheme.ClonesAccent : item.Completed ? ForgeTheme.SuccessBorder : ForgeTheme.BorderSubtle;
                syncStepBadges[i].Background = item.Completed ? ForgeTheme.Success : item.Active ? ForgeTheme.CtaBlue : ForgeTheme.BgPill;
                syncStepBadges[i].BorderBrush = item.Completed ? ForgeTheme.Success : item.Active ? ForgeTheme.ClonesAccent : ForgeTheme.BorderStrong;
                syncStepBadgeTexts[i].Text = item.Completed ? "✓" : item.Number.ToString();
                syncStepBadgeTexts[i].Foreground = item.Completed ? ForgeTheme.TextOnAccent : ForgeTheme.TextPrimary;
            }
        }

        if (st.Step == 4)
        {
            setupStep4ModeText.Text = st.SelectedModeSummary;
            foreach (var (id, btn) in setupStep4TextureButtons)
            {
                bool on = string.Equals(st.SelectedTexture, id, StringComparison.OrdinalIgnoreCase);
                btn.Background = on ? ForgeTheme.BgSelected : ForgeTheme.BgElevated;
                btn.BorderBrush = on ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
                btn.IsEnabled = !st.IsBusy && !st.PackReady;
            }
            setupStep4ConsentText.Text = st.PackReady
                ? "All gameplay mods and your Star Wars pack are verified and active. Windows may ask for network approval on first launch."
                : st.PendingDownloadBytes > 0
                    ? $"Ready to download {Pack.FormatBytes(st.PendingDownloadBytes)} of verified Star Wars assets ({st.PendingCachedFiles} local files reused). Downloads resume safely if paused."
                    : "Click below to install the required gameplay mods and verify your selected Star Wars pack.";
        }

        if (st.IsBusy && st.Progress is { } p)
        {
            setupProgressCard.IsVisible = true;
            setupProgressStage.Text = string.IsNullOrWhiteSpace(p.SubStep) ? p.Stage : $"{p.Stage} — {p.SubStep}";
            setupProgressMetrics.Text = p.FormatMetrics();
            setupProgressBar.IsIndeterminate = p.IsIndeterminate || p.Fraction == null;
            if (p.Fraction is { } frac) setupProgressBar.Value = frac * 100d;
        }
        else
        {
            setupProgressCard.IsVisible = false;
        }

        setupPrimaryButton.Content = st.PrimaryActionLabel;
        AutomationProperties.SetName(setupPrimaryButton, st.PrimaryActionLabel);
        setupPrimaryButton.IsEnabled = st.PrimaryActionEnabled;
        ApplyCtaTone(setupPrimaryButton, st.PackReady ? ForgeHeroTone.Ready : ForgeHeroTone.Action, "clonedivers", st.PrimaryActionEnabled);

        setupCancelWorkButton.IsVisible = st.IsBusy && st.CanCancel;
        setupCancelWorkButton.IsEnabled = st.IsBusy && st.CanCancel;
    }

    void SetSubstepState(int index, bool done, bool active)
    {
        setupSubstepDots[index].Background = done ? ForgeTheme.Success : active ? ForgeTheme.ClonesAccent : ForgeTheme.BorderStrong;
        setupSubstepTexts[index].Foreground = done ? ForgeTheme.SuccessText : active ? ForgeTheme.TextPrimary : ForgeTheme.TextSecondary;
    }

    static IBrush ToneBrush(ForgeHeroTone tone) => tone switch
    {
        ForgeHeroTone.Ready => ForgeTheme.Success,
        ForgeHeroTone.Running => ForgeTheme.ClonesAccent,
        ForgeHeroTone.Warning => ForgeTheme.Warning,
        ForgeHeroTone.Danger => ForgeTheme.Danger,
        ForgeHeroTone.Action => ForgeTheme.CtaPrimary,
        _ => ForgeTheme.TextMuted
    };

    static void ApplyBadgeTone(Border badge, TextBlock text, string label, ForgeHeroTone tone)
    {
        text.Text = label;
        (badge.Background, badge.BorderBrush, text.Foreground) = tone switch
        {
            ForgeHeroTone.Ready => (ForgeTheme.SuccessBg, ForgeTheme.SuccessBorder, ForgeTheme.SuccessText),
            ForgeHeroTone.Running => (ForgeTheme.InfoBg, ForgeTheme.InfoBorder, ForgeTheme.InfoText),
            ForgeHeroTone.Warning or ForgeHeroTone.Action => (ForgeTheme.WarningBg, ForgeTheme.WarningBorder, ForgeTheme.WarningText),
            ForgeHeroTone.Danger => (ForgeTheme.DangerBg, ForgeTheme.DangerBorder, ForgeTheme.DangerText),
            _ => (ForgeTheme.BgElevated, ForgeTheme.BorderSubtle, ForgeTheme.TextSecondary)
        };
    }

    static void ApplyCtaTone(Button button, ForgeHeroTone tone, string mode, bool enabled)
    {
        if (!enabled)
        {
            button.Background = ForgeTheme.BgElevated;
            button.Foreground = ForgeTheme.TextMuted;
            return;
        }
        if (tone == ForgeHeroTone.Danger)
        {
            button.Background = ForgeTheme.CtaDanger;
            button.Foreground = ForgeTheme.TextPrimary;
        }
        else if (tone == ForgeHeroTone.Running)
        {
            button.Background = ForgeTheme.CtaBlue;
            button.Foreground = ForgeTheme.TextPrimary;
        }
        else if (tone == ForgeHeroTone.Ready)
        {
            button.Background = mode == "empire" ? ForgeTheme.EmpireAccent : mode == "clonedivers" ? ForgeTheme.CtaPrimary : ForgeTheme.RegularAccent;
            button.Foreground = mode == "empire" ? ForgeTheme.TextPrimary : ForgeTheme.TextOnAccent;
        }
        else
        {
            button.Background = ForgeTheme.CtaPrimary;
            button.Foreground = ForgeTheme.TextOnAccent;
        }
    }

    /// <summary>Renders crisp visual QA PNGs at 1.0x and 1.5x DPI without touching real game/Forge folders.</summary>
    public async Task RenderSnapshotAsync(string outputDir, string namePrefix, double[]? scales = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Task.Delay(140);
        Measure(new Size(Width, Height));
        Arrange(new Rect(0, 0, Width, Height));
        UpdateLayout();
        Directory.CreateDirectory(outputDir);
        foreach (var scale in scales ?? [1.0, 1.5])
        {
            int pxW = (int)Math.Ceiling(Bounds.Width * scale);
            int pxH = (int)Math.Ceiling(Bounds.Height * scale);
            if (pxW <= 0 || pxH <= 0) { pxW = (int)Math.Ceiling(Width * scale); pxH = (int)Math.Ceiling(Height * scale); }
            using var bitmap = new RenderTargetBitmap(new PixelSize(pxW, pxH), new Vector(96 * scale, 96 * scale));
            bitmap.Render(this);
            var tag = scale == 1.0 ? "100pct" : $"{(int)(scale * 100)}pct";
            bitmap.Save(Path.Combine(outputDir, $"{namePrefix}-{tag}.png"));
        }
    }

    sealed class ActionCardBorder(Func<Task> onInvoke) : Border
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new ActionCardBorderPeer(this, onInvoke);
    }

    sealed class ActionCardBorderPeer(ActionCardBorder owner, Func<Task> onInvoke) : ControlAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        public void Invoke()
        {
            if (!owner.IsEnabled) return;
            Dispatcher.UIThread.Post(async () =>
            {
                if (owner.IsEnabled) await onInvoke();
            });
        }
    }
}
