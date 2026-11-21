namespace Clonedivers.Ui;

/// <summary>Deterministic preview host used for interactive inspection and headless 100%/150% DPI visual QA.</summary>
public sealed class PreviewForgeHost : IForgeUiHost
{
    readonly string preset;
    string mode;
    string enemySkin = "all-droids";
    bool deltaSquad = true;
    bool covenant;
    string texture = "full";
    bool updateDismissed;
    int setupStep = 1;
    string selectedDrive = @"D:\";

    public event Action? StateChanged;
    public event Action<bool>? SetupVisibilityChanged;

    public bool IsPreview => true;
    public bool SupportsEmbeddedSetup => true;
    public bool SupportsSelfUpdate => true;

    public static readonly string[] AllPresets =
    [
        "home-clones",
        "home-empire",
        "home-regular",
        "home-update",
        "customize",
        "downloading",
        "running",
        "error",
        "setup-step1",
        "setup-step2-qr",
        "setup-step3-sync",
        "setup-step4-pack",
        "settings"
    ];

    public PreviewForgeHost(string preset = "home-clones")
    {
        this.preset = preset;
        mode = preset switch
        {
            "home-empire" => "empire",
            "home-regular" => "regular",
            _ => "clonedivers"
        };
        setupStep = preset switch
        {
            "setup-step2-qr" => 2,
            "setup-step3-sync" => 3,
            "setup-step4-pack" => 4,
            _ => 1
        };
        if (preset == "home-empire") { texture = "reduced512"; covenant = true; }
    }

    public string? InitialOverlay => preset switch
    {
        "customize" => "customize",
        "settings" => "settings",
        "setup-step1" or "setup-step2-qr" or "setup-step3-sync" or "setup-step4-pack" => "setup",
        _ => null
    };

    public ForgeHomeState GetHomeState()
    {
        var isBusy = preset == "downloading";
        var isRunning = preset == "running";
        var isError = preset == "error";
        var hasUpdate = preset == "home-update" && !updateDismissed;

        var previewSize = mode == "regular"
            ? new PackSizePreview(true, 0, 0, 0, 0, 0, 0, true, "Active · No download", "Standard Helldivers 2 visuals with required Forge gameplay mods.")
            : texture == "reduced512"
                ? new PackSizePreview(false, 2_840_000_000, 1_120_000_000, 1_720_000_000, 294, 148, 146, false, "~1.6 GB download", "Downloads ~1.6 GB (146 files) and reuses ~1.0 GB locally.")
                : new PackSizePreview(false, 10_920_000_000, 10_920_000_000, 0, 378, 378, 0, true, "Ready — cached locally", "All 378 files (10.2 GB) found locally; switching needs no download.");

        var progress = isBusy
            ? new OperationProgress(
                Stage: "Downloading Star Wars pack",
                SubStep: "9ba626afa44a3aa3.patch_0",
                ItemIndex: 142,
                ItemCount: 378,
                BytesDone: 5_798_205_849,
                BytesTotal: 10_952_166_604,
                BytesPerSecond: 44_354_764,
                Eta: TimeSpan.FromSeconds(116),
                IsIndeterminate: false,
                CanCancel: true)
            : null;

        var (badgeText, badgeTone, headline, detail, ctaLabel, ctaTone, secondary) = preset switch
        {
            "downloading" => ("Downloading · 53%", ForgeHeroTone.Running, "Downloading Star Wars pack", "Temporary connection errors retry automatically.", "CANCEL DOWNLOAD", ForgeHeroTone.Danger, (string?)null),
            "running" => ("Session Running", ForgeHeroTone.Running, "HellForge session is running", "End the session here when you finish playing so cleanup completes cleanly.", "END SESSION", ForgeHeroTone.Running, (string?)null),
            "error" => ("Needs Attention", ForgeHeroTone.Danger, "Download service didn't respond", "GitHub timed out while fetching verified pack assets. Your saved files are intact.", "RETRY", ForgeHeroTone.Action, "View Diagnostics"),
            _ => ("Forge 0.4.0 · Ready", ForgeHeroTone.Ready, "Ready to play", "Choose difficulties 1–11 at the war table. Steam co-op & local backend enabled.",
                mode == "regular" ? "LAUNCH REGULAR HELLFORGE" : mode == "empire" ? "LAUNCH EMPIREDIVERS" : "LAUNCH CLONEDIVERS",
                ForgeHeroTone.Ready, (string?)null)
        };

        return new ForgeHomeState(
            PlatformTag: OperatingSystem.IsLinux() ? "HellForge · Linux" : "HellForge · Windows",
            AppVersion: AppInfo.Version,
            Mode: mode,
            EnemySkin: enemySkin,
            DeltaSquad: deltaSquad,
            Covenant: covenant,
            Texture: texture,
            HeaderBadgeText: badgeText,
            HeaderBadgeTone: badgeTone,
            Headline: headline,
            Detail: detail,
            PrimaryActionLabel: ctaLabel,
            PrimaryActionEnabled: true,
            PrimaryActionTone: ctaTone,
            SecondaryActionLabel: secondary,
            ShowLocateMissing: false,
            IsBusy: isBusy,
            IsSessionRunning: isRunning,
            ControlsLocked: isBusy || isRunning,
            HasInstallation: true,
            InstallRoot: @"D:\HellForge",
            AccountImported: true,
            IsModeAppliedOnDisk: previewSize.IsAppliedOnDisk,
            Progress: progress,
            UpdateBanner: hasUpdate ? new ForgeUpdateBannerState("2.1.0", "Clonedivers v2.1.0 is ready to install — game copy and saves stay untouched.", "Update & Restart") : null,
            SizePreview: previewSize);
    }

    public ForgeSetupState GetSetupState()
    {
        var drives = new[]
        {
            new LocalDriveOption(@"D:\", "Games NVMe", 412L << 30, 1000L << 30, true, @"D:\HellForge", IsRecommended: true),
            new LocalDriveOption(@"C:\", "Windows", 48L << 30, 512L << 30, false, @"C:\Users\Player\HellForge", IsRecommended: false)
        };
        var selected = drives.FirstOrDefault(d => d.Root == selectedDrive) ?? drives[0];

        var syncSnap = AccountSyncTracker.Evaluate(
            managerOpen: true,
            captureBlock: "Account sync is still running.",
            captureBlockUnreadable: false,
            gameRunning: true,
            accountCaptured: false,
            accountImported: false,
            syncRequested: true);

        string[]? qrRows = null;
        OperationProgress? prog = null;
        if (setupStep == 2)
        {
            qrRows = BuildDemoQrRows();
            prog = new OperationProgress("Waiting for Steam Guard QR approval", "Approve sign-in on your phone to download build 25480438", IsIndeterminate: true, CanCancel: true, QrRows: qrRows);
        }

        var (headline, detail, cta) = setupStep switch
        {
            1 => ("Check Space & Steam", "Pick a local drive with at least 70 GB free outside Steam and OneDrive.", "INSTALL FORGE & COPY GAME"),
            2 => ("Install Forge & Game Copy", "Approve the Steam Guard QR code below so HellForge can download build 25480438.", "WAITING FOR QR APPROVAL…"),
            3 => (syncSnap.Headline, syncSnap.Detail, syncSnap.PrimaryActionLabel),
            _ => ("Download Star Wars Pack", "Pick your texture tier below, then download and verify your selected pack.", texture == "reduced512" ? "DOWNLOAD PACK (1.6 GB)" : "DOWNLOAD PACK (5.4 GB)")
        };

        return new ForgeSetupState(
            Step: setupStep,
            SteamReady: true,
            HasInstallation: setupStep >= 2,
            ForgeUpgradeAvailable: false,
            GameCopyReady: setupStep >= 3,
            PackReady: false,
            IsBusy: setupStep == 2,
            CanCancel: setupStep == 2,
            InstallFolder: selected.SuggestedInstallPath,
            Drives: drives,
            SelectedDriveRoot: selected.Root,
            SpaceSummary: selected.MeetsBudget ? $"Space: {Pack.FormatBytes(selected.FreeBytes)} free (OK)" : $"Space: {Pack.FormatBytes(selected.FreeBytes)} free (Low)",
            SpaceOk: selected.MeetsBudget,
            AccountSync: syncSnap,
            SelectedModeSummary: "Clonedivers (Delta Squad) · All CIS Droids · Build 25480438",
            SelectedTexture: texture,
            PendingDownloadBytes: texture == "reduced512" ? 1_720_000_000 : 5_798_205_849,
            PendingCachedFiles: 148,
            Headline: headline,
            Detail: detail,
            PrimaryActionLabel: cta,
            PrimaryActionEnabled: setupStep != 2,
            ShowOfficialFallback: setupStep <= 2,
            Progress: prog,
            QrRows: qrRows);
    }

    public Task<ForgeDiagnosticsState> GetDiagnosticsAsync(CancellationToken ct = default) => Task.FromResult(new ForgeDiagnosticsState(
        InstallRoot: @"D:\HellForge",
        AppVersion: AppInfo.Version,
        AccountSummary: "HD2 account imported · Forge rewards save locally",
        HealthChecks:
        [
            new ForgeHealthItem("HellForge", "0.4.0-beta · SDK 16", Healthy: true),
            new ForgeHealthItem("Game Copy", "Build 25480438", Healthy: true),
            new ForgeHealthItem("Gameplay Preset", "14/14 verified", Healthy: true),
            new ForgeHealthItem("Account Sync", "Imported", Healthy: true),
            new ForgeHealthItem("Co-op Network", "Sandbox + WFP OK", Healthy: true)
        ],
        RawReportText:
            "Installation: D:\\HellForge\r\n" +
            "Launcher: Clonedivers v" + AppInfo.Version + " (Preview)\r\n" +
            "HellForge: 0.4.0-beta · Engine 0.1.47 · SDK 16 · Build 25480438\r\n" +
            "Account: Imported (one-way HD2 -> Forge)\r\n" +
            "Mods: 14 required gameplay mods verified · Clonedivers r20 active · 0 problems\r\n" +
            "Last session: observed (clean exit)",
        CanOpenManager: true,
        CanSyncAccount: true,
        CanCheckUpdates: true,
        CanOpenSetup: true,
        IsCheckingUpdates: false,
        UpdateMessage: $"Up to date (v{AppInfo.Version})"));

    public void AttachUiCallbacks(Func<ForgeConfirmRequest, Task<bool>> confirmAsync, Func<string, Task<string?>> pickFolderAsync, Func<string, string, Task<string?>> saveFileAsync, Action<OperationProgress?>? taskbarProgress = null, Action? requestAttention = null) { }
    public Task InitializeAsync() => Task.CompletedTask;
    public bool CanClose(out string? reason) { reason = null; return true; }
    public Task SelectModeAsync(string newMode) { mode = newMode; StateChanged?.Invoke(); return Task.CompletedTask; }
    public Task UpdateCustomizationAsync(string skin, bool delta, bool cov, string tex) { enemySkin = skin; deltaSquad = delta; covenant = cov; texture = tex; StateChanged?.Invoke(); return Task.CompletedTask; }
    public Task TriggerPrimaryActionAsync() => Task.CompletedTask;
    public Task TriggerSecondaryActionAsync() => Task.CompletedTask;
    public Task TriggerUpdateBannerActionAsync() => Task.CompletedTask;
    public void DismissUpdateBanner() { updateDismissed = true; StateChanged?.Invoke(); }
    public void OpenSteamLibrary() { }
    public Task EnterSetupAsync() { SetupVisibilityChanged?.Invoke(true); return Task.CompletedTask; }
    public void ExitSetup() => SetupVisibilityChanged?.Invoke(false);
    public void SelectSetupDrive(string driveRoot) { selectedDrive = driveRoot; StateChanged?.Invoke(); }
    public Task BrowseSetupFolderAsync(bool existing) => Task.CompletedTask;
    public Task SetSetupTextureAsync(string tex) { texture = tex; StateChanged?.Invoke(); return Task.CompletedTask; }
    public Task TriggerSetupPrimaryActionAsync() { if (setupStep < 4) { setupStep++; StateChanged?.Invoke(); } else SetupVisibilityChanged?.Invoke(false); return Task.CompletedTask; }
    public void CancelSetupWork() { }
    public Task OpenOfficialSetupFallbackAsync() => Task.CompletedTask;
    public Task LocateInstallAsync() => Task.CompletedTask;
    public void OpenInstallFolder() { }
    public Task OpenManagerAsync() => Task.CompletedTask;
    public Task SyncAccountFromSettingsAsync() => Task.CompletedTask;
    public Task CheckUpdatesAsync() => Task.CompletedTask;
    public Task<string?> SaveSupportReportAsync() => Task.FromResult<string?>(null);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Deterministic valid 29x29 finder-pattern QR matrix (in half-block encoding) for visual QA only.</summary>
    static string[] BuildDemoQrRows()
    {
        const int n = 29;
        var dark = new bool[n, n];
        void Finder(int ox, int oy)
        {
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 7; x++)
                    dark[oy + y, ox + x] = x == 0 || x == 6 || y == 0 || y == 6 || (x is >= 2 and <= 4 && y is >= 2 and <= 4);
        }
        Finder(2, 2);
        Finder(n - 9, 2);
        Finder(2, n - 9);
        for (int i = 10; i < n - 10; i++) { dark[8, i] = i % 2 == 0; dark[i, 8] = i % 2 == 0; }
        for (int y = 10; y < n - 2; y++)
            for (int x = 10; x < n - 2; x++)
                dark[y, x] = ((x * 7 + y * 13 + x * y) % 5) is 0 or 2;

        var rows = new string[(n + 1) / 2];
        for (int r = 0; r < rows.Length; r++)
        {
            var chars = new char[n];
            for (int x = 0; x < n; x++)
            {
                bool topLight = !dark[r * 2, x];
                bool botLight = (r * 2 + 1 >= n) || !dark[r * 2 + 1, x];
                chars[x] = (topLight, botLight) switch
                {
                    (true, true) => '\u2588',
                    (true, false) => '\u2580',
                    (false, true) => '\u2584',
                    _ => ' '
                };
            }
            rows[r] = new string(chars);
        }
        return rows;
    }
}
