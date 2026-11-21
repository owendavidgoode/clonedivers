using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Clonedivers.Ui;

using Brushes = Avalonia.Media.Brushes;
using Button = Avalonia.Controls.Button;
using Control = Avalonia.Controls.Control;
using FontFamily = Avalonia.Media.FontFamily;
using Grid = Avalonia.Controls.Grid;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using Image = Avalonia.Controls.Image;
using Orientation = Avalonia.Layout.Orientation;
using ProgressBar = Avalonia.Controls.ProgressBar;
using RadioButton = Avalonia.Controls.RadioButton;
using Size = Avalonia.Size;
using TextBox = Avalonia.Controls.TextBox;

/// <summary>Unified modern Star Wars launcher window shared across Windows and Linux.</summary>
public sealed class ForgeShellWindow : Window
{
    readonly IForgeUiHost host;
    bool showingSetup;
    bool showingCustomize;
    bool showingSettings;
    bool closingConfirmed;
    bool updatingControls;
    TaskCompletionSource<bool>? pendingConfirm;
    WriteableBitmap? currentQrBitmap;
    string[]? lastQrRows;

    // Header & update banner
    readonly TextBlock headerSubtitle = ForgeTheme.Label("HellForge Edition", 12, ForgeTheme.TextSecondary);
    readonly Border headerStatusBadge = new() { CornerRadius = new CornerRadius(999), Padding = new Thickness(11, 4), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock headerStatusText = ForgeTheme.Label("Ready", 11.5, ForgeTheme.SuccessText, FontWeight.SemiBold, wrap: false);
    readonly Button settingsButton = ForgeTheme.GhostButton("Settings", 12, new Thickness(13, 7));

    readonly Border updateBanner = new()
    {
        IsVisible = false,
        Background = ForgeTheme.InfoBg,
        BorderBrush = ForgeTheme.InfoBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 9),
        Margin = new Thickness(28, 0, 28, 10)
    };
    readonly TextBlock updateBannerText = ForgeTheme.Label("", 12.5, ForgeTheme.TextPrimary, FontWeight.Medium);
    readonly Button updateBannerAction = ForgeTheme.GhostButton("Update & Restart", 12, new Thickness(12, 5));
    readonly Button updateBannerDismiss = ForgeTheme.LinkStyleButton("Later", ForgeTheme.TextSecondary, 12);

    // Main content swap
    readonly Control homeView;
    readonly Control setupView;

    // Home view controls (7 top-level elements)
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
        Padding = new Thickness(16, 11)
    };
    readonly TextBlock customizeSummaryTitle = ForgeTheme.Label("Loadout", 11.5, ForgeTheme.TextMuted, FontWeight.SemiBold);
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
        Padding = new Thickness(18, 14),
        MinHeight = 96
    };
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

    // Customize Slide-Over Drawer
    readonly Grid customizeOverlay = new() { IsVisible = false, Background = ForgeTheme.BgOverlay };
    readonly TextBlock drawerTitle = ForgeTheme.Label("Customize Clones", 18, ForgeTheme.TextPrimary, FontWeight.Bold);
    readonly TextBlock drawerSubtitle = ForgeTheme.Label("Changes preview download size immediately.", 12, ForgeTheme.TextSecondary);
    readonly Dictionary<string, ToggleButton> enemyPills = new();
    readonly ToggleSwitch deltaToggle = new() { OnContent = "On", OffContent = "Off" };
    readonly Border deltaRow = new();
    readonly ToggleSwitch covenantToggle = new() { OnContent = "On", OffContent = "Off" };
    readonly Dictionary<string, Border> textureCards = new();
    readonly Dictionary<string, RadioButton> textureRadios = new();
    readonly Border drawerPreviewBox = new()
    {
        Background = ForgeTheme.BgElevated,
        BorderBrush = ForgeTheme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 11)
    };
    readonly TextBlock drawerPreviewBadge = ForgeTheme.Label("", 12.5, ForgeTheme.SuccessText, FontWeight.Bold);
    readonly TextBlock drawerPreviewDetail = ForgeTheme.Label("", 12, ForgeTheme.TextSecondary);
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

    // Settings & Diagnostics Modal
    readonly Grid settingsOverlay = new() { IsVisible = false, Background = ForgeTheme.BgOverlay };
    readonly TextBlock settingsInstallPath = ForgeTheme.Label("Not configured", 12.5, ForgeTheme.TextPrimary);
    readonly TextBlock settingsAccountSummary = ForgeTheme.Label("", 12.5, ForgeTheme.TextSecondary);
    readonly TextBlock settingsUpdateNote = ForgeTheme.Label("", 12, ForgeTheme.InfoText);
    readonly WrapPanel settingsHealthWrap = new() { Orientation = Orientation.Horizontal };
    readonly TextBox settingsReportBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 148,
        FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
        FontSize = 11.5,
        Background = ForgeTheme.BgInput,
        Foreground = ForgeTheme.TextSecondary,
        BorderBrush = ForgeTheme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8)
    };
    readonly Button settingsCopyReportButton = ForgeTheme.GhostButton("Copy Report", 12, new Thickness(13, 7));
    readonly Button settingsSaveReportButton = ForgeTheme.GhostButton("Save Report…", 12, new Thickness(13, 7));
    readonly Button settingsOpenFolderButton = ForgeTheme.GhostButton("Open Folder", 12, new Thickness(12, 6));
    readonly Button settingsLocateButton = ForgeTheme.GhostButton("Locate…", 12, new Thickness(12, 6));
    readonly Button settingsManagerButton = ForgeTheme.GhostButton("Open HellForge", 12, new Thickness(12, 6));
    readonly Button settingsGuidedSetupButton = ForgeTheme.GhostButton("Guided Setup…", 12, new Thickness(12, 6));
    readonly Button settingsSyncAccountButton = ForgeTheme.GhostButton("Sync Account", 12, new Thickness(12, 6));
    readonly Button settingsCheckUpdatesButton = ForgeTheme.GhostButton("Check Updates", 12, new Thickness(12, 6));

    // Dark Confirm Modal
    readonly Grid confirmOverlay = new() { IsVisible = false, Background = ForgeTheme.BgOverlay };
    readonly TextBlock confirmTitle = ForgeTheme.Label("", 17, ForgeTheme.TextPrimary, FontWeight.Bold);
    readonly TextBlock confirmBody = ForgeTheme.Label("", 13.5, ForgeTheme.TextSecondary);
    readonly Button confirmCancelButton = ForgeTheme.GhostButton("Cancel", 13, new Thickness(16, 9));
    readonly Button confirmOkButton = ForgeTheme.PrimaryButton("Continue", 38, 13.5);

    public ForgeShellWindow(IForgeUiHost host, string? initialOverlay = null)
    {
        this.host = host;

        Title = "Clonedivers";
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
        BuildConfirmOverlay();

        var shell = new DockPanel { LastChildFill = true };
        var topDock = new StackPanel { Spacing = 0 };
        topDock.Children.Add(BuildHeaderBar());
        topDock.Children.Add(BuildUpdateBanner());
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
        rootLayers.Children.Add(confirmOverlay);
        Content = rootLayers;

        host.AttachUiCallbacks(
            confirmAsync: ShowConfirmOverlayAsync,
            pickFolderAsync: PickFolderAsync,
            saveFileAsync: SaveFileAsync,
            taskbarProgress: p => Dispatcher.UIThread.Post(() => WindowsTaskbar.SetProgress(this, p)),
            requestAttention: () => Dispatcher.UIThread.Post(() => WindowsTaskbar.FlashAttention(this)));

        host.StateChanged += () => Dispatcher.UIThread.Post(RefreshAll);
        host.SetupVisibilityChanged += show => Dispatcher.UIThread.Post(() =>
        {
            showingSetup = show && host.SupportsEmbeddedSetup;
            if (showingSetup) { showingCustomize = false; showingSettings = false; }
            RefreshAll();
        });

        Opened += async (_, _) =>
        {
            WindowsTaskbar.ApplyDarkTitleBar(this);
            if (initialOverlay == "setup" && host.SupportsEmbeddedSetup) showingSetup = true;
            else if (initialOverlay == "customize") showingCustomize = true;
            else if (initialOverlay == "settings") { showingSettings = true; _ = LoadSettingsDiagnosticsAsync(); }
            RefreshAll();
            await host.InitializeAsync();
            RefreshAll();
        };

        Closing += async (_, e) =>
        {
            if (host.IsPreview || closingConfirmed) return;
            if (!host.CanClose(out var reason))
            {
                e.Cancel = true;
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    toastText.Text = reason;
                    toastText.IsVisible = true;
                }
                return;
            }
            e.Cancel = true;
            closingConfirmed = true;
            await host.DisposeAsync();
            Close();
        };

        RefreshAll();
    }

    Control BuildHeaderBar()
    {
        headerStatusBadge.Child = headerStatusText;
        AutomationProperties.SetName(settingsButton, "Settings and diagnostics");
        settingsButton.Click += async (_, _) =>
        {
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

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(headerStatusBadge);
        right.Children.Add(settingsButton);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(28, 16, 28, 12)
        };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    Control BuildUpdateBanner()
    {
        updateBannerAction.Background = ForgeTheme.CtaBlue;
        updateBannerAction.BorderThickness = new Thickness(0);
        updateBannerAction.Click += async (_, _) => await host.TriggerUpdateBannerActionAsync();
        updateBannerDismiss.Click += (_, _) => host.DismissUpdateBanner();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
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

    Control BuildFooterBar()
    {
        steamFooterLink.Click += (_, _) => host.OpenSteamLibrary();
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(28, 8, 28, 14)
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
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(28, 4, 28, 8) };

        // 1. Three Hero Universe Cards
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
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = Brushes.Transparent
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
                Spacing = 7,
                Margin = new Thickness(14, 14, 14, 14),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            body.Children.Add(ForgeTheme.FactionEmblem(id, 62));
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
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            AutomationProperties.SetName(btn, title);
            btn.Click += async (_, _) => await host.SelectModeAsync(id);
            modeButtons[id] = btn;
            cardsGrid.Children.Add(btn);
        }
        stack.Children.Add(cardsGrid);

        // 2. Compact Customize Summary Bar
        customizeSizePill.Child = customizeSizePillText;
        AutomationProperties.SetName(customizeOpenButton, "Customize loadout options");
        customizeOpenButton.Click += (_, _) =>
        {
            showingCustomize = true;
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
        var statusTop = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        statusTop.Children.Add(statusDot);
        Grid.SetColumn(statusHeadline, 1);
        statusTop.Children.Add(statusHeadline);
        statusMetrics.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(statusMetrics, 2);
        statusTop.Children.Add(statusMetrics);

        var deckStack = new StackPanel { Spacing = 8 };
        deckStack.Children.Add(statusTop);
        deckStack.Children.Add(statusDetail);
        deckStack.Children.Add(homeProgress);
        toastText.IsVisible = false;
        deckStack.Children.Add(toastText);
        statusDeck.Child = deckStack;
        stack.Children.Add(statusDeck);

        // 4. Hero Primary CTA (+ optional contextual secondary button)
        AutomationProperties.SetName(heroButton, "Primary action");
        heroButton.Click += async (_, _) => await host.TriggerPrimaryActionAsync();
        secondaryButton.IsVisible = false;
        secondaryButton.Margin = new Thickness(10, 0, 0, 0);
        secondaryButton.Click += async (_, _) => await host.TriggerSecondaryActionAsync();

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
        // Clicking backdrop dismisses the drawer
        var backdrop = new Border { Background = Brushes.Transparent };
        backdrop.PointerPressed += (_, _) => { showingCustomize = false; customizeOverlay.IsVisible = false; };
        customizeOverlay.Children.Add(backdrop);

        var closeBtn = ForgeTheme.GhostButton("✕", 13, new Thickness(10, 5));
        AutomationProperties.SetName(closeBtn, "Close customize drawer");
        closeBtn.Click += (_, _) => { showingCustomize = false; customizeOverlay.IsVisible = false; };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(drawerTitle);
        titleStack.Children.Add(drawerSubtitle);
        header.Children.Add(titleStack);
        Grid.SetColumn(closeBtn, 1);
        closeBtn.VerticalAlignment = VerticalAlignment.Top;
        header.Children.Add(closeBtn);

        // Enemy faction segmented pills
        var enemyWrap = new UniformGrid { Columns = 3 };
        foreach (var (id, label) in new[] { ("all-droids", "All CIS Droids"), ("mixed-droids", "Mixed Bots"), ("native-automatons", "Native Bots") })
        {
            var pill = new ToggleButton
            {
                Content = label,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = ForgeTheme.TextPrimary,
                Background = ForgeTheme.BgElevated,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 8),
                Margin = new Thickness(0, 0, id == "native-automatons" ? 0 : 6, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            AutomationProperties.SetName(pill, label);
            pill.Click += async (_, _) =>
            {
                if (updatingControls) return;
                var s = host.GetHomeState();
                await host.UpdateCustomizationAsync(id, s.DeltaSquad, s.Covenant, s.Texture);
            };
            enemyPills[id] = pill;
            enemyWrap.Children.Add(pill);
        }

        // Extras switches
        deltaRow.Background = ForgeTheme.BgElevated;
        deltaRow.BorderBrush = ForgeTheme.BorderSubtle;
        deltaRow.BorderThickness = new Thickness(1);
        deltaRow.CornerRadius = new CornerRadius(9);
        deltaRow.Padding = new Thickness(11, 7);
        deltaRow.Child = BuildSwitchRow("Delta Squad Armor", "Republic Commandos replace standard Clone armor.", deltaToggle);
        deltaToggle.IsCheckedChanged += async (_, _) =>
        {
            if (updatingControls) return;
            var s = host.GetHomeState();
            await host.UpdateCustomizationAsync(s.EnemySkin, deltaToggle.IsChecked == true, s.Covenant, s.Texture);
        };

        var covenantCard = new Border
        {
            Background = ForgeTheme.BgElevated,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11, 7),
            Child = BuildSwitchRow("Covenant Squids", "Halo Covenant visual overhaul for the Illuminate.", covenantToggle)
        };
        covenantToggle.IsCheckedChanged += async (_, _) =>
        {
            if (updatingControls) return;
            var s = host.GetHomeState();
            await host.UpdateCustomizationAsync(s.EnemySkin, s.DeltaSquad, covenantToggle.IsChecked == true, s.Texture);
        };

        // Texture Quality Cards
        var texStack = new StackPanel { Spacing = 5 };
        var texSpecs = new[]
        {
            ("full", "High (Full)", "Sharpest resident textures. Best for 12 GB+ VRAM GPUs."),
            ("lighter", "Balanced (Streamed)", "Full-detail textures streamed on demand for lower VRAM."),
            ("reduced512", "Low VRAM · 512px (Johnson Ultra-Potato)", "Lowest VRAM & fastest loads. Same meshes & audio.")
        };
        foreach (var (id, title, desc) in texSpecs)
        {
            var radio = new RadioButton { GroupName = "DrawerTextureTier", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            AutomationProperties.SetName(radio, title);
            textureRadios[id] = radio;

            var textCol = new StackPanel { Spacing = 1 };
            textCol.Children.Add(ForgeTheme.Label(title, 12.5, ForgeTheme.TextPrimary, FontWeight.SemiBold));
            textCol.Children.Add(ForgeTheme.Label(desc, 11, ForgeTheme.TextSecondary));

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
            row.Children.Add(radio);
            Grid.SetColumn(textCol, 1);
            row.Children.Add(textCol);

            var card = new Border
            {
                Background = ForgeTheme.BgElevated,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11, 7),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = row
            };
            card.PointerPressed += async (_, _) =>
            {
                if (updatingControls) return;
                var s = host.GetHomeState();
                await host.UpdateCustomizationAsync(s.EnemySkin, s.DeltaSquad, s.Covenant, id);
            };
            radio.IsCheckedChanged += async (_, _) =>
            {
                if (updatingControls || radio.IsChecked != true) return;
                var s = host.GetHomeState();
                await host.UpdateCustomizationAsync(s.EnemySkin, s.DeltaSquad, s.Covenant, id);
            };
            textureCards[id] = card;
            texStack.Children.Add(card);
        }

        var previewStack = new StackPanel { Spacing = 2 };
        previewStack.Children.Add(drawerPreviewBadge);
        previewStack.Children.Add(drawerPreviewDetail);
        drawerPreviewBox.Padding = new Thickness(12, 9);
        drawerPreviewBox.Child = previewStack;

        drawerApplyButton.Click += (_, _) => { showingCustomize = false; customizeOverlay.IsVisible = false; };

        var drawerScrollBody = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 6, 10) };
        drawerScrollBody.Children.Add(ForgeTheme.Label("ENEMY FACTION", 10.5, ForgeTheme.TextMuted, FontWeight.Bold));
        drawerScrollBody.Children.Add(enemyWrap);
        drawerScrollBody.Children.Add(ForgeTheme.Label("EXTRAS", 10.5, ForgeTheme.TextMuted, FontWeight.Bold));
        drawerScrollBody.Children.Add(deltaRow);
        drawerScrollBody.Children.Add(covenantCard);
        drawerScrollBody.Children.Add(ForgeTheme.Label("TEXTURE QUALITY", 10.5, ForgeTheme.TextMuted, FontWeight.Bold));
        drawerScrollBody.Children.Add(texStack);

        var drawerFooter = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        drawerFooter.Children.Add(drawerPreviewBox);
        drawerFooter.Children.Add(drawerApplyButton);

        var drawerDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        drawerDock.Children.Add(header);
        DockPanel.SetDock(drawerFooter, Dock.Bottom);
        drawerDock.Children.Add(drawerFooter);
        drawerDock.Children.Add(new ScrollViewer
        {
            Content = drawerScrollBody,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var drawerPanel = new Border
        {
            Width = 420,
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

    static Grid BuildSwitchRow(string title, string subtitle, ToggleSwitch toggle)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(ForgeTheme.Label(title, 13, ForgeTheme.TextPrimary, FontWeight.SemiBold));
        text.Children.Add(ForgeTheme.Label(subtitle, 11.5, ForgeTheme.TextSecondary));
        toggle.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(text);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    Control BuildSetupView()
    {
        setupBackButton.Click += (_, _) => host.ExitSetup();

        // Top Stepper Bar
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

        // Stage 1: Space, Expectations & Multi-Drive Picker
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

        // Stage 2: Sub-step checklist + fixed non-jumping QR card
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

        // Stage 3: Live Auto-Advancing 5-Step Account Sync Tracker
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

        // Stage 4: Pack Download with Texture Tier Choice BEFORE Downloading
        var step4TexGrid = new UniformGrid { Columns = 3 };
        foreach (var (id, title, sub) in new[]
        {
            ("full", "High (Full)", "12 GB+ VRAM · Sharpest"),
            ("lighter", "Balanced (Streamed)", "Streamed · Lower VRAM"),
            ("reduced512", "Low VRAM (512px)", "Ultra-Potato · Fastest")
        })
        {
            var content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    ForgeTheme.Label(title, 12.5, ForgeTheme.TextPrimary, FontWeight.Bold, TextAlignment.Center, wrap: false),
                    ForgeTheme.Label(sub, 11, ForgeTheme.TextSecondary, FontWeight.Normal, TextAlignment.Center)
                }
            };
            var btn = new Button
            {
                Content = content,
                Background = ForgeTheme.BgElevated,
                BorderBrush = ForgeTheme.BorderSubtle,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 9),
                Margin = new Thickness(id == "full" ? 0 : 5, 0, id == "reduced512" ? 0 : 5, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            btn.Click += async (_, _) => await host.SetSetupTextureAsync(id);
            setupStep4TextureButtons[id] = btn;
            step4TexGrid.Children.Add(btn);
        }
        setupStep4ConsentBox.Child = setupStep4ConsentText;
        setupStage4Panel.Children.Add(setupStep4ModeText);
        setupStage4Panel.Children.Add(ForgeTheme.Label("CHOOSE TEXTURE QUALITY BEFORE DOWNLOADING", 11, ForgeTheme.TextMuted, FontWeight.Bold));
        setupStage4Panel.Children.Add(step4TexGrid);
        setupStage4Panel.Children.Add(setupStep4ConsentBox);

        // Setup progress card
        var progTop = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        progTop.Children.Add(setupProgressStage);
        Grid.SetColumn(setupProgressMetrics, 1);
        progTop.Children.Add(setupProgressMetrics);
        setupProgressCard.Child = new StackPanel { Spacing = 7, Children = { progTop, setupProgressBar } };

        setupPrimaryButton.Click += async (_, _) => await host.TriggerSetupPrimaryActionAsync();
        setupCancelWorkButton.Click += (_, _) => host.CancelSetupWork();
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
        backdrop.PointerPressed += (_, _) => { showingSettings = false; settingsOverlay.IsVisible = false; };
        settingsOverlay.Children.Add(backdrop);

        var closeBtn = ForgeTheme.GhostButton("✕", 13, new Thickness(10, 5));
        closeBtn.Click += (_, _) => { showingSettings = false; settingsOverlay.IsVisible = false; };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(ForgeTheme.Label("Settings & Diagnostics", 18, ForgeTheme.TextPrimary, FontWeight.Bold));
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(closeBtn);

        settingsOpenFolderButton.Click += (_, _) => host.OpenInstallFolder();
        settingsLocateButton.Click += async (_, _) => { await host.LocateInstallAsync(); await LoadSettingsDiagnosticsAsync(); };
        settingsManagerButton.Click += async (_, _) => await host.OpenManagerAsync();
        settingsGuidedSetupButton.Click += async (_, _) => { showingSettings = false; settingsOverlay.IsVisible = false; await host.EnterSetupAsync(); };
        settingsSyncAccountButton.Click += async (_, _) => { showingSettings = false; settingsOverlay.IsVisible = false; await host.SyncAccountFromSettingsAsync(); };
        settingsCheckUpdatesButton.Click += async (_, _) => { await host.CheckUpdatesAsync(); await LoadSettingsDiagnosticsAsync(); };
        settingsCopyReportButton.Click += async (_, _) =>
        {
            try
            {
                if (Clipboard is { } cb)
                {
                    await cb.SetTextAsync(settingsReportBox.Text ?? "");
                    settingsCopyReportButton.Content = "Copied!";
                    await Task.Delay(1600);
                    settingsCopyReportButton.Content = "Copy Report";
                }
            }
            catch (Exception ex) { settingsUpdateNote.Text = "Couldn't copy to clipboard: " + ex.Message; }
        };
        settingsSaveReportButton.Click += async (_, _) =>
        {
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
            Padding = new Thickness(14, 11),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    ForgeTheme.Label("HELLFORGE INSTALLATION", 11, ForgeTheme.TextMuted, FontWeight.Bold),
                    settingsInstallPath,
                    new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            WithMargin(settingsOpenFolderButton, new Thickness(0, 0, 8, 4)),
                            WithMargin(settingsLocateButton, new Thickness(0, 0, 8, 4)),
                            WithMargin(settingsManagerButton, new Thickness(0, 0, 8, 4)),
                            WithMargin(settingsGuidedSetupButton, new Thickness(0, 0, 0, 4))
                        }
                    }
                }
            }
        };

        var accountUpdateCard = new Border
        {
            Background = ForgeTheme.BgElevated,
            BorderBrush = ForgeTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    ForgeTheme.Label("ACCOUNT & LAUNCHER UPDATES", 11, ForgeTheme.TextMuted, FontWeight.Bold),
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
                    }
                }
            }
        };

        var reportActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { settingsCopyReportButton, settingsSaveReportButton }
        };

        settingsReportBox.Height = 108;
        var modalScrollBody = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(0, 10, 6, 4),
            Children =
            {
                installCard,
                accountUpdateCard,
                ForgeTheme.Label("HEALTH CHECKS", 10.5, ForgeTheme.TextMuted, FontWeight.Bold),
                settingsHealthWrap,
                ForgeTheme.Label("SUPPORT DIAGNOSTICS", 10.5, ForgeTheme.TextMuted, FontWeight.Bold),
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
            Width = 660,
            MaxHeight = 620,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = ForgeTheme.BgSurface,
            BorderBrush = ForgeTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 16),
            Child = modalDock
        };
        settingsOverlay.Children.Add(modalCard);
    }

    void BuildConfirmOverlay()
    {
        confirmCancelButton.Click += (_, _) => ResolveConfirm(false);
        confirmOkButton.Width = 140;
        confirmOkButton.HorizontalAlignment = HorizontalAlignment.Right;
        confirmOkButton.Click += (_, _) => ResolveConfirm(true);

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
        settingsInstallPath.Text = diag.InstallRoot ?? "No HellForge folder selected";
        settingsAccountSummary.Text = diag.AccountSummary;
        settingsUpdateNote.Text = diag.UpdateMessage ?? $"Launcher v{diag.AppVersion}";
        settingsUpdateNote.IsVisible = true;
        settingsOpenFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(diag.InstallRoot);
        settingsManagerButton.IsEnabled = diag.CanOpenManager;
        settingsGuidedSetupButton.IsVisible = host.SupportsEmbeddedSetup;
        settingsGuidedSetupButton.IsEnabled = diag.CanOpenSetup;
        settingsSyncAccountButton.IsEnabled = diag.CanSyncAccount;
        settingsCheckUpdatesButton.IsEnabled = diag.CanCheckUpdates && !diag.IsCheckingUpdates;
        settingsCheckUpdatesButton.Content = diag.IsCheckingUpdates ? "Checking…" : "Check Updates";

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
        settingsReportBox.Text = diag.RawReportText;
    }

    void RefreshAll()
    {
        homeView.IsVisible = !showingSetup;
        setupView.IsVisible = showingSetup;
        customizeOverlay.IsVisible = showingCustomize;
        settingsOverlay.IsVisible = showingSettings;

        var home = host.GetHomeState();
        headerSubtitle.Text = home.PlatformTag;
        versionFooter.Text = $"v{home.AppVersion}";

        ApplyBadgeTone(headerStatusBadge, headerStatusText, home.HeaderBadgeText, home.HeaderBadgeTone);

        // Update banner (never hijacks Play!)
        if (home.UpdateBanner is { } banner && !showingSetup)
        {
            updateBanner.IsVisible = true;
            updateBanner.Background = banner.IsFailed ? ForgeTheme.DangerBg : ForgeTheme.InfoBg;
            updateBanner.BorderBrush = banner.IsFailed ? ForgeTheme.DangerBorder : ForgeTheme.InfoBorder;
            updateBannerText.Text = banner.Message;
            updateBannerAction.Content = banner.ActionLabel;
            updateBannerAction.IsEnabled = !home.IsBusy;
            updateBannerDismiss.IsVisible = !banner.IsRestartReady;
        }
        else
        {
            updateBanner.IsVisible = false;
        }

        RefreshHome(home);
        if (showingCustomize) RefreshCustomizeDrawer();
        if (showingSetup) RefreshSetup(host.GetSetupState());
    }

    void RefreshHome(ForgeHomeState s)
    {
        foreach (var (id, card) in modeCards)
        {
            bool selected = string.Equals(s.Mode, id, StringComparison.OrdinalIgnoreCase);
            var accent = ForgeTheme.AccentForMode(id);
            card.Background = selected ? ForgeTheme.BgSelected : ForgeTheme.BgSurface;
            card.BorderBrush = selected ? accent : ForgeTheme.BorderSubtle;
            modeTopBars[id].Background = selected ? accent : Brushes.Transparent;
            modeButtons[id].IsEnabled = !s.ControlsLocked;

            var badge = modeBadges[id];
            var badgeText = modeBadgeTexts[id];
            if (selected)
            {
                badge.IsVisible = true;
                badge.Background = ForgeTheme.GlowForMode(id);
                badge.BorderBrush = accent;
                badgeText.Foreground = accent;
                badgeText.Text = s.IsModeAppliedOnDisk ? "ACTIVE" : "SELECTED";
            }
            else
            {
                badge.IsVisible = true;
                badge.Background = Brushes.Transparent;
                badge.BorderBrush = Brushes.Transparent;
                badgeText.Foreground = ForgeTheme.TextMuted;
                badgeText.Text = "CLICK TO SELECT";
            }
        }

        // Customize summary strip
        if (s.Mode == "regular")
        {
            customizeSummaryTitle.Text = "STANDARD LOADOUT";
            customizeSummaryText.Text = "Original Helldivers 2 visuals with required Forge gameplay mods";
            customizeOpenButton.IsVisible = false;
        }
        else
        {
            customizeSummaryTitle.Text = s.Mode == "empire" ? "EMPIRE LOADOUT" : "CLONES LOADOUT";
            var enemy = s.EnemySkin switch
            {
                "mixed-droids" => "Mixed Bots + CIS",
                "native-automatons" => "Native Automatons",
                _ => "All CIS Droids"
            };
            var tex = s.Texture switch
            {
                "reduced512" => "Low VRAM 512px",
                "lighter" => "Balanced Streamed",
                _ => "High Textures"
            };
            var parts = new List<string> { enemy };
            if (s.Mode == "clonedivers" && s.DeltaSquad) parts.Add("Delta Squad");
            if (s.Covenant) parts.Add("Covenant");
            parts.Add(tex);
            customizeSummaryText.Text = string.Join("  ·  ", parts);
            customizeOpenButton.IsVisible = true;
            customizeOpenButton.IsEnabled = !s.ControlsLocked;
        }

        var preview = s.SizePreview;
        bool cached = preview.DownloadBytes == 0;
        customizeSizePill.Background = cached ? ForgeTheme.SuccessBg : ForgeTheme.WarningBg;
        customizeSizePill.BorderBrush = cached ? ForgeTheme.SuccessBorder : ForgeTheme.WarningBorder;
        customizeSizePillText.Foreground = cached ? ForgeTheme.SuccessText : ForgeTheme.WarningText;
        customizeSizePillText.Text = preview.BadgeText;

        // Fixed-height Status & Progress Deck
        if (s.IsBusy && s.Progress is { } prog)
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
            statusDot.Background = ToneBrush(s.PrimaryActionTone);
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
            drawerTitle.Text = s.Mode == "empire" ? "Customize Empire" : "Customize Clones";
            foreach (var (id, pill) in enemyPills)
            {
                bool on = string.Equals(s.EnemySkin, id, StringComparison.OrdinalIgnoreCase);
                pill.IsChecked = on;
                pill.Background = on ? ForgeTheme.BgSelected : ForgeTheme.BgElevated;
                pill.BorderBrush = on ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
                pill.IsEnabled = !s.ControlsLocked;
            }

            deltaRow.IsVisible = s.Mode == "clonedivers";
            deltaToggle.IsChecked = s.DeltaSquad;
            deltaToggle.IsEnabled = !s.ControlsLocked;

            covenantToggle.IsChecked = s.Covenant;
            covenantToggle.IsEnabled = !s.ControlsLocked;

            foreach (var (id, card) in textureCards)
            {
                bool on = string.Equals(s.Texture, id, StringComparison.OrdinalIgnoreCase);
                textureRadios[id].IsChecked = on;
                textureRadios[id].IsEnabled = !s.ControlsLocked;
                card.Background = on ? ForgeTheme.BgSelected : ForgeTheme.BgElevated;
                card.BorderBrush = on ? ForgeTheme.ClonesAccent : ForgeTheme.BorderSubtle;
            }

            var p = s.SizePreview;
            bool cached = p.DownloadBytes == 0;
            drawerPreviewBox.Background = cached ? ForgeTheme.SuccessBg : ForgeTheme.WarningBg;
            drawerPreviewBox.BorderBrush = cached ? ForgeTheme.SuccessBorder : ForgeTheme.WarningBorder;
            drawerPreviewBadge.Foreground = cached ? ForgeTheme.SuccessText : ForgeTheme.WarningText;
            drawerPreviewBadge.Text = p.BadgeText;
            drawerPreviewDetail.Text = p.DetailText;
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

        // Stage 1: Space & Steam + Drive picker
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
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Child = new StackPanel { Spacing = 6, Children = { top, bar } }
                };
                var rootCopy = d.Root;
                card.PointerPressed += (_, _) => { if (!st.IsBusy) host.SelectSetupDrive(rootCopy); };
                setupDrivesList.Children.Add(card);
            }
        }

        // Stage 2: Substeps + non-jumping QR card
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

        // Stage 3: Live 5-Step Account Sync Tracker
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

        // Stage 4: Texture Tier Choice + Consent Box
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

        // Progress card
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
        await Task.Delay(120);
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
}
