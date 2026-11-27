namespace Clonedivers.Ui;

/// <summary>Deterministic preview host used for interactive inspection and headless 100%/150% DPI visual QA.</summary>
public sealed class PreviewForgeHost : IForgeUiHost
{
    readonly string preset;
    string mode;
    string enemySkin = "mixed-droids";
    string squidSkin = "mixed-covenant";
    bool deltaSquad = true;
    bool deathTrooperVoice = false;
    string texture = "full";
    bool autoPerformanceTier = true;
    string performanceTier = PerformanceTierAdvisor.Auto;
    string hostChaos = "standard";
    string hostSeaf = "standard";
    bool hostCorpseCleanup = true;
    string hostTheater = "clone-wars";
    bool soundEffectsEnabled = true;
    bool discordRpcEnabled = true;
    bool minimizeToTray;
    bool updateDismissed;
    bool stepDownDismissed;
    bool selfInstallDismissed;
    int setupStep = 1;
    string selectedDrive = @"D:\";

    readonly HardwarePerformanceRecommendation demoHardware = new(
        GpuName: "NVIDIA GeForce RTX 5090",
        VramGb: 32.0,
        RamGb: 64.0,
        RecommendedTierId: PerformanceTierAdvisor.Ultra,
        Reason: "Detected NVIDIA GeForce RTX 5090 with 32 GB VRAM — plenty of headroom for Ultra full-resolution textures.");

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
        "home-stepdown",
        "home-resume",
        "customize",
        "customize-advanced",
        "whats-new",
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
            "home-empire" or "home-resume" => "empire",
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
        if (preset == "customize-advanced")
        {
            autoPerformanceTier = false;
            performanceTier = PerformanceTierAdvisor.Low512;
            texture = "reduced512";
            enemySkin = "all-droids";
            squidSkin = "mixed-covenant";
            hostChaos = "total-war";
            hostSeaf = "heavy";
        }
    }

    public string? InitialOverlay => preset switch
    {
        "customize" => "customize",
        "customize-advanced" => "customize-advanced",
        "whats-new" => "whats-new",
        "settings" => "settings",
        "setup-step1" or "setup-step2-qr" or "setup-step3-sync" or "setup-step4-pack" => "setup",
        _ => null
    };

    public ForgeHomeState GetHomeState()
    {
        var isBusy = preset == "downloading";
        var isLaunching = preset == "launching";
        var isRunning = preset is "running" or "launching";
        var hasUpdate = preset is "home-update" or "whats-new" && !updateDismissed;
        var hasStepDown = preset == "home-stepdown" && !stepDownDismissed;
        var hasResume = preset == "home-resume";

        PackSizePreview previewSize;
        if (mode == "regular")
        {
            previewSize = new PackSizePreview(true, 0, 0, 0, 0, 0, 0, true, "Active · No download", "Standard Helldivers 2 visuals with required Forge gameplay mods.");
        }
        else if (hasResume)
        {
            previewSize = new PackSizePreview(
                IsRegular: false,
                TotalBytes: 9_341_553_868,
                CachedBytes: 4_831_838_208,
                DownloadBytes: 4_509_715_660,
                TotalFiles: 378,
                CachedFiles: 196,
                DownloadFiles: 182,
                IsAppliedOnDisk: false,
                BadgeText: "Resume: 4.2 GB left (4.5 GB saved)",
                DetailText: "Paused download ready to resume — 4.5 GB already saved locally.");
        }
        else if (texture == "reduced512")
        {
            previewSize = new PackSizePreview(false, 2_840_000_000, 1_120_000_000, 1_720_000_000, 294, 148, 146, false, "~1.6 GB download", "Downloads ~1.6 GB (146 files) and reuses ~1.0 GB locally.");
        }
        else
        {
            previewSize = new PackSizePreview(false, 10_920_000_000, 10_920_000_000, 0, 378, 378, 0, true, "Ready · Active", "All 378 files (10.2 GB) are active and ready.");
        }

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
            : isLaunching
                ? new OperationProgress(
                    Stage: ForgeTacticalCopy.LaunchingHeadline,
                    SubStep: ForgeTacticalCopy.LaunchingDetail,
                    IsIndeterminate: true,
                    CanCancel: false)
                : null;

        var (badgeText, badgeTone, headline, detail, ctaLabel, ctaTone, secondary) = preset switch
        {
            "downloading" => ("Downloading · 53%", ForgeHeroTone.Running, "Downloading Star Wars pack", "Temporary connection errors retry automatically.", "CANCEL DOWNLOAD", ForgeHeroTone.Danger, (string?)null),
            "launching" => ("Launching…", ForgeHeroTone.Running, ForgeTacticalCopy.LaunchingHeadline, ForgeTacticalCopy.LaunchingDetail, ForgeTacticalCopy.LaunchingPrimaryButton, ForgeHeroTone.Neutral, ForgeTacticalCopy.CancelLaunchButton),
            "running" => ("Session Active", ForgeHeroTone.Running, ForgeTacticalCopy.RunningHeadline, ForgeTacticalCopy.RunningDetail, "END SESSION", ForgeHeroTone.Running, (string?)null),
            "error" => ("Needs Attention", ForgeHeroTone.Danger, "Download service didn't respond", "GitHub timed out while fetching verified pack assets. Your saved files are intact.", "RETRY", ForgeHeroTone.Action, "View Diagnostics"),
            "home-stepdown" => ("Last Run Notice", ForgeHeroTone.Warning, "Last session ended unexpectedly", "GPU ran out of video memory or hung (D3D12) — try stepping down to High for smoother play.", "LAUNCH CLONEDIVERS", ForgeHeroTone.Ready, (string?)null),
            "home-resume" => ("Update & Launch", ForgeHeroTone.Action, "Switch to EmpireDivers & launch", "Resumes the remaining 4.2 GB download, applies your loadout, and launches automatically.", "UPDATE & LAUNCH", ForgeHeroTone.Action, (string?)null),
            _ => ("Forge 0.4.0 · Ready", ForgeHeroTone.Ready, "Ready to play", ForgeTacticalCopy.ReadyDetail(mode),
                mode == "regular" ? "LAUNCH REGULAR HELLFORGE" : mode == "empire" ? "LAUNCH EMPIREDIVERS" : "LAUNCH CLONEDIVERS",
                ForgeHeroTone.Ready, (string?)null)
        };

        var stepDown = hasStepDown
            ? new ForgeStepDownBannerState(
                Reason: "Last session hit a D3D12 video-memory spike — step down to High streamed textures?",
                NextTierId: PerformanceTierAdvisor.High,
                NextTierDisplayName: "High",
                ActionLabel: "Drop to High")
            : null;

        var selfInstall = hasResume && !selfInstallDismissed
            ? new ForgeSelfInstallBannerState(
                Message: "Running from Downloads — Pin Clonedivers to Start Menu & Desktop?",
                ActionLabel: "Install Shortcuts")
            : null;

        return new ForgeHomeState(
            PlatformTag: OperatingSystem.IsLinux() ? "HellForge · Linux" : "HellForge · Windows",
            AppVersion: AppInfo.Version,
            Mode: mode,
            EnemySkin: enemySkin,
            DeltaSquad: deltaSquad,
            Covenant: squidSkin != "native-squids",
            Texture: texture,
            AutoPerformanceTier: autoPerformanceTier,
            PerformanceTier: performanceTier,
            Hardware: demoHardware,
            HostChaosIntensity: hostChaos,
            HostHeavySeaf: hostSeaf,
            HostFasterCorpseCleanup: hostCorpseCleanup,
            SoundEffectsEnabled: soundEffectsEnabled,
            SquadMatchHash: SquadFingerprint.Compute(HellForgeGameplayCatalog.Current),
            HeaderBadgeText: badgeText,
            HeaderBadgeTone: badgeTone,
            Headline: headline,
            Detail: detail,
            PrimaryActionLabel: ctaLabel,
            PrimaryActionEnabled: !isLaunching,
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
            UpdateBanner: hasUpdate ? new ForgeUpdateBannerState(AppInfo.Version, $"Clonedivers v{AppInfo.Version} is ready to install — game copy and saves stay untouched.", "Update & Restart") : null,
            SizePreview: previewSize,
            ToastNotice: preset == "home-clones" ? null : null,
            StepDownBanner: stepDown,
            SelfInstallBanner: selfInstall,
            MinimizeToTray: minimizeToTray,
            SquidSkin: squidSkin,
            DeathTrooperVoice: deathTrooperVoice,
            HostTheaterPreset: hostTheater);
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
            SelectedModeSummary: "Clonedivers (Full Star Wars Mix) · Auto (Ultra) · Build 25480438",
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

    public Task<ForgeDiagnosticsState> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var matchCode = SquadFingerprint.Compute(HellForgeGameplayCatalog.Current);
        var triage = new CrashTriageResult(CrashKind.Clean, 0, "Last session exited cleanly.", null, false, null, false, []);
        var discord = DiscordReportFormatter.Format(
            appVersion: AppInfo.Version,
            squadMatchCode: matchCode,
            mode: mode,
            enemySkin: enemySkin,
            covenant: squidSkin != "native-squids",
            deltaSquad: deltaSquad,
            performanceTier: autoPerformanceTier ? PerformanceTierAdvisor.Auto : performanceTier,
            packTexture: texture,
            hardware: demoHardware,
            installRoot: @"D:\HellForge",
            triage: triage,
            redactUserPaths: true,
            squidSkin: squidSkin,
            deathTrooperVoice: deathTrooperVoice);

        return Task.FromResult(new ForgeDiagnosticsState(
            InstallRoot: @"D:\HellForge",
            AppVersion: AppInfo.Version,
            AccountSummary: "HD2 account imported · Forge rewards save locally",
            HealthChecks:
            [
                new ForgeHealthItem("HellForge", "0.4.0-beta · SDK 16", Healthy: true),
                new ForgeHealthItem("Game Copy", "Build 25480438", Healthy: true),
                new ForgeHealthItem("Gameplay Preset", $"{HellForgeGameplayCatalog.Current.Artifacts.Count}/{HellForgeGameplayCatalog.Current.Artifacts.Count} ({matchCode})", Healthy: true),
                new ForgeHealthItem("Account Sync", "Imported", Healthy: true),
                new ForgeHealthItem("Co-op Network", "Sandbox + WFP OK", Healthy: true)
            ],
            RawReportText: discord,
            DiscordReportText: discord,
            DiskUsageSummary: "Game 70.2 GB · Pack 10.2 GB · Cache 1.2 GB (850 MB reclaimable)",
            CanOpenManager: true,
            CanSyncAccount: true,
            CanCheckUpdates: true,
            CanOpenSetup: true,
            CanCleanCache: true,
            CanVerifyAndRepair: true,
            IsCheckingUpdates: false,
            UpdateMessage: $"Up to date (v{AppInfo.Version})",
            SoundEffectsEnabled: soundEffectsEnabled,
            DiscordRpcEnabled: discordRpcEnabled,
            MinimizeToTray: minimizeToTray));
    }

    public void AttachUiCallbacks(Func<ForgeConfirmRequest, Task<bool>> confirmAsync, Func<string, Task<string?>> pickFolderAsync, Func<string, string, Task<string?>> saveFileAsync, Action<OperationProgress?>? taskbarProgress = null, Action? requestAttention = null) { }
    public Task InitializeAsync() => Task.CompletedTask;
    public bool CanClose(out string? reason) { reason = null; return true; }
    public Task SelectModeAsync(string newMode) { mode = newMode; StateChanged?.Invoke(); return Task.CompletedTask; }
    public Task UpdateCustomizationAsync(string skin, bool delta, bool cov, string tex, string? squid = null, bool? dtVoice = null)
    {
        enemySkin = skin;
        deltaSquad = delta;
        squidSkin = squid ?? (cov ? "all-covenant" : "native-squids");
        if (dtVoice is { } dt) deathTrooperVoice = dt;
        texture = tex;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }
    public Task SetPerformanceTierAsync(string tierId)
    {
        performanceTier = tierId;
        autoPerformanceTier = tierId == PerformanceTierAdvisor.Auto;
        texture = PerformanceTierAdvisor.ResolvePackTexture(tierId, demoHardware);
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }
    public Task UpdateHostMutatorsAsync(string chaos, string seaf, bool cleanup, string? theater = null)
    {
        hostChaos = chaos;
        hostSeaf = seaf;
        hostCorpseCleanup = cleanup;
        if (theater != null) hostTheater = theater;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }
    public Task ApplyCustomizationNowAsync() => Task.CompletedTask;
    public Task TriggerPrimaryActionAsync() => Task.CompletedTask;
    public Task TriggerSecondaryActionAsync() => Task.CompletedTask;
    public Task TriggerUpdateBannerActionAsync() => Task.CompletedTask;
    public void DismissUpdateBanner() { updateDismissed = true; StateChanged?.Invoke(); }
    public Task AcceptStepDownAsync() { stepDownDismissed = true; autoPerformanceTier = false; performanceTier = PerformanceTierAdvisor.High; texture = "lighter"; StateChanged?.Invoke(); return Task.CompletedTask; }
    public void DismissStepDownBanner() { stepDownDismissed = true; StateChanged?.Invoke(); }
    public Task InstallSelfShortcutsAsync() { selfInstallDismissed = true; StateChanged?.Invoke(); return Task.CompletedTask; }
    public void DismissSelfInstallBanner() { selfInstallDismissed = true; StateChanged?.Invoke(); }
    public Task<WhatsNewResult> FetchWhatsNewAsync(string? versionOrTag, CancellationToken ct = default) => Task.FromResult(new WhatsNewResult(
        Tag: WhatsNewFetcher.NormalizeTag(versionOrTag ?? AppInfo.Version, isLinux: false),
        Bullets:
        [
            "Genuine Difficulty 12 Slot ('LEVEL 12: CLONE WARS') right after D11, armed by default while keeping D1–11 intact.",
            "EmpireDivers r54 & Clonedivers r25: fixed Parade Stormtrooper helmet pop-in & film-scale AT-ST walker culling across all 5 tiers.",
            "Instant Mega-City AT-ST spawn admission (< 50 ms) in empire-atst-writer 0.2.5 + preserved full-detail close-up armor LODs.",
            "Automatic completed-backup & staging disk reclamation on low-space Clonedivers and EmpireDivers installs (Squad Code r14)."
        ],
        IsFallback: false,
        ReleaseUrl: $"https://github.com/owendavidgoode/clonedivers/releases/tag/v{AppInfo.Version}"));
    public void ToggleSoundEffects() { soundEffectsEnabled = !soundEffectsEnabled; StateChanged?.Invoke(); }
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
    public Task CleanCacheAsync() => Task.CompletedTask;
    public Task VerifyAndRepairAsync() => Task.CompletedTask;
    public void SetDiscordRpcEnabled(bool enabled) { discordRpcEnabled = enabled; StateChanged?.Invoke(); }
    public void SetMinimizeToTray(bool enabled) { minimizeToTray = enabled; StateChanged?.Invoke(); }
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
