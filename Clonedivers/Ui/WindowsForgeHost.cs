using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;

namespace Clonedivers.Ui;

/// <summary>Production Windows HellForge controller backing the unified Avalonia launcher shell.</summary>
public sealed class WindowsForgeHost : IForgeUiHost
{
    readonly HellForgePreferences preferences;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer homeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer setupTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly DiscordRpcClient discordRpc = new();
    readonly SquadReportClient squadReporter = new();
    readonly HardwarePerformanceRecommendation cachedHardware;

    Func<ForgeConfirmRequest, Task<bool>> confirmAsync = _ => Task.FromResult(false);
    Func<string, Task<string?>> pickFolderAsync = _ => Task.FromResult<string?>(null);
    Func<string, string, Task<string?>> saveFileAsync = (_, _) => Task.FromResult<string?>(null);
    Action<OperationProgress?>? taskbarProgress;
    Action? requestAttention;

    CancellationTokenSource? workCts;
    TaskCompletionSource? refreshCompletion;

    HellForgeStatus? lastStatus;
    HellForgeMods? lastMods;
    string? lastModsFingerprint;
    DateTime lastModsReadUtc, lastPartialSweepUtc;
    HellForgeAccountState? lastAccount;
    string? lastAccountFingerprint;
    DateTime lastAccountReadUtc;
    bool accountReviewFailed;
    string? captureBlock;
    bool captureBlockUnreadable;
    bool warbondsPending;
    bool syncRequested;
    bool installationMissing;
    bool working;
    bool refreshing;
    bool checkingUpdates;
    bool updateDeferred;
    bool updateFailed;
    bool updateDownloading;
    bool launcherReplaced;
    bool selfInstallEligible;
    bool selfInstallDismissed;
    bool sessionWasActive;
    bool gameProcessObserved;
    DateTimeOffset? sessionStartUtc;
    string? lastProcessedSessionKey;
    CrashTriageResult? lastTriage;
    AppRelease? launcherRelease;

    internal Func<bool> IsGameProcessRunning { get; set; } = Game.IsRunning;

    OperationProgress? activeProgress;
    string? lastError;
    string? failureDetail;
    string? operationFailure;
    string? toastNotice;
    string? updateMessage;
    PackSizePreview cachedSizePreview;
    int sizeEstimateVersion;

    // Embedded 4-stage setup state
    bool inSetup;
    int setupStep = 1;
    bool setupBusy;
    bool setupCanCancel;
    bool setupGameReady;
    bool setupPackReady;
    string setupFolder;
    string? setupInstallRoot;
    IReadOnlyList<LocalDriveOption> setupDrives = [];
    string? setupSelectedDrive;
    HellForgePack.Plan? setupPendingPlan;
    string? setupDetail;
    OperationProgress? setupProgress;
    string[]? setupQrRows;
    bool setupPolling;
    bool disposed;

    public event Action? StateChanged;
    public event Action<bool>? SetupVisibilityChanged;

    public bool IsPreview => false;
    public bool SupportsEmbeddedSetup => true;
    public bool SupportsSelfUpdate => true;

    public WindowsForgeHost(HellForgePreferences preferences, string? migrationNotice = null)
    {
        this.preferences = preferences;
        this.preferences.Validate();

        cachedHardware = PerformanceTierAdvisor.ScanHardware();
        if (this.preferences.AutoPerformanceTier)
            this.preferences.Texture = PerformanceTierAdvisor.ResolvePackTexture(PerformanceTierAdvisor.Auto, cachedHardware);

        UiSoundEffects.Enabled = this.preferences.SoundEffectsEnabled;
        discordRpc.Enabled = this.preferences.DiscordRpcEnabled;
        selfInstallEligible = SelfInstallHelper.IsRunningFromDownloads(Environment.ProcessPath);

        toastNotice = string.IsNullOrWhiteSpace(migrationNotice) ? null : migrationNotice;
        setupDrives = LocalDriveCatalog.EnumerateLocalDrives();
        var recommended = setupDrives.FirstOrDefault(d => d.IsRecommended) ?? setupDrives.FirstOrDefault();
        setupSelectedDrive = recommended?.Root;
        setupFolder = preferences.InstallRoot ?? recommended?.SuggestedInstallPath ?? HellForgeSetup.SeparateDefaultRoot();
        cachedSizePreview = new PackSizePreview(
            preferences.IsRegular, 0, 0, 0, 0, 0, 0, false,
            preferences.IsRegular ? "No pack download needed" : "Estimating size…",
            "Checking local cache…");

        homeTimer.Tick += async (_, _) =>
        {
            if (inSetup) return;
            if (lastStatus?.Running == true && !gameProcessObserved && SyncGameProcessObserved())
            {
                homeTimer.Interval = TimeSpan.FromSeconds(5);
                taskbarProgress?.Invoke(null);
                Notify();
            }
            await RefreshAsync();
        };
        setupTimer.Tick += async (_, _) => { if (inSetup) await PollSetupAsync(); };
    }

    public void AttachUiCallbacks(
        Func<ForgeConfirmRequest, Task<bool>> confirmAsync,
        Func<string, Task<string?>> pickFolderAsync,
        Func<string, string, Task<string?>> saveFileAsync,
        Action<OperationProgress?>? taskbarProgress = null,
        Action? requestAttention = null)
    {
        this.confirmAsync = confirmAsync;
        this.pickFolderAsync = pickFolderAsync;
        this.saveFileAsync = saveFileAsync;
        this.taskbarProgress = taskbarProgress;
        this.requestAttention = requestAttention;
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync();
        ScheduleSizeEstimate();
        if (!disposed)
        {
            homeTimer.Start();
            if (preferences.InstallRoot is { } root && !installationMissing && lastStatus?.Running != true)
                _ = ForgeDiskManager.AutoCleanStaleAsync(root, lifetime.Token);
            await CheckLauncherUpdateCoreAsync(interactive: false);
        }
    }

    bool SyncGameProcessObserved()
    {
        if (lastStatus?.Running != true)
        {
            gameProcessObserved = false;
            return false;
        }
        if (!gameProcessObserved && (lastStatus.GameLaunched == true || IsGameProcessRunning()))
            gameProcessObserved = true;
        return gameProcessObserved;
    }

    bool IsPreWindowLaunching => lastStatus?.Running == true && !SyncGameProcessObserved();

    public bool CanClose(out string? reason)
    {
        if (working || setupBusy)
        {
            reason = "Wait for the current step to finish, or cancel the active download first.";
            return false;
        }
        if (IsPreWindowLaunching)
        {
            reason = "Helldivers 2 is still launching (usually 15–30 seconds). Wait for the game window, or click Cancel Launch first.";
            return false;
        }
        reason = null;
        return true;
    }

    static HellForgeClient CreateClient(string root) => new(HellForgeCommands.ValidateRoot(root), HellForgeForm.CreateCommands(root));
    static HellForgeAccount CreateAccount(string root) => HellForgeForm.CreateAccount(root);
    void SavePrefs() => preferences.Save();
    void Notify() => StateChanged?.Invoke();

    string CurrentConcreteTier() => PerformanceTierAdvisor.NormalizeConcreteTier(
        preferences.AutoPerformanceTier ? PerformanceTierAdvisor.Auto : preferences.PerformanceTier,
        cachedHardware);

    string ComputeSquadMatchHash() =>
        lastMods is { Mods.Count: > 0 } m
            ? SquadFingerprint.Compute(m)
            : SquadFingerprint.Compute(HellForgeGameplayCatalog.Current);

    void UpdateDiscordPresence(bool running)
    {
        if (!running || !preferences.DiscordRpcEnabled)
        {
            discordRpc.ClearPresence();
            return;
        }
        var details = ForgeTacticalCopy.DiscordPresenceDetails(preferences.Mode, preferences.DeltaSquad);
        var state = $"Squad {ComputeSquadMatchHash()} · Build {HellForgePack.SupportedBuild}";
        discordRpc.SetPresence(state, details, ForgeTacticalCopy.DiscordPresenceImageKey(preferences.Mode));
    }

    void TrySyncHostMutatorFile(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot) || installationMissing) return;
        try
        {
            if (Directory.Exists(installRoot))
                HostMutatorConfig.Write(installRoot, preferences.ToHostMutatorOptions());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
    }

    void HandleSessionEnded(string installRoot, HellForgeStatus status)
    {
        double? durationSec = sessionStartUtc is { } started
            ? Math.Max(0d, (DateTimeOffset.UtcNow - started).TotalSeconds)
            : null;
        sessionWasActive = false;
        gameProcessObserved = false;
        homeTimer.Interval = TimeSpan.FromSeconds(5);
        taskbarProgress?.Invoke(null);
        sessionStartUtc = null;
        UpdateDiscordPresence(running: false);

        var currentTier = CurrentConcreteTier();
        var triage = CrashTriage.AnalyzeLastSession(installRoot, status, currentTier);
        lastTriage = triage;

        var sessionKey = $"{triage.SessionId ?? "none"}|{triage.CompletedUtc?.ToUnixTimeSeconds() ?? 0}|{triage.ExitCode ?? 0}";
        if (string.Equals(sessionKey, lastProcessedSessionKey, StringComparison.Ordinal))
            return;
        lastProcessedSessionKey = sessionKey;

        if (triage.Kind == CrashKind.OutOfVram && triage.SuggestStepDown && triage.SuggestedTierId != null)
        {
            preferences.LastStepDownSuggestedTier = triage.SuggestedTierId;
            preferences.PendingStepDownReason = triage.Summary;
            try { SavePrefs(); } catch { }
        }

        if (triage.Kind == CrashKind.HarmlessQuit)
        {
            toastNotice = triage.Summary;
        }
        else if (triage.Kind != CrashKind.Clean)
        {
            toastNotice = string.IsNullOrWhiteSpace(triage.RecommendedAction)
                ? triage.Summary
                : $"{triage.Summary} {triage.RecommendedAction}";
        }

        var report = new SquadSessionReport(
            Player: preferences.PlayerLabel ?? "Anonymous",
            LauncherVersion: AppInfo.Version,
            ExitCode: triage.ExitCode ?? 0,
            HostLabel: null,
            MatchHash: ComputeSquadMatchHash(),
            Mode: preferences.Mode,
            PerfTier: currentTier,
            Gpu: cachedHardware.GpuName,
            RamGb: cachedHardware.RamGb,
            DurationSec: durationSec,
            AvgFps: null,
            Low1Fps: null,
            StatusSummary: triage.Summary);
        squadReporter.ReportSessionCompletedFireAndForget(report, triage);
    }

    void SetProgress(OperationProgress? p, bool forSetup = false)
    {
        void Apply()
        {
            if (disposed) return;
            if (forSetup)
            {
                setupProgress = p;
                setupCanCancel = p?.CanCancel == true;
                if (p?.QrRows != null)
                {
                    setupQrRows = p.QrRows;
                    requestAttention?.Invoke();
                }
            }
            else
            {
                activeProgress = p;
            }
            taskbarProgress?.Invoke(p);
            Notify();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    void ScheduleSizeEstimate()
    {
        var ver = Interlocked.Increment(ref sizeEstimateVersion);
        var isRegular = preferences.IsRegular;
        var installRoot = preferences.InstallRoot;
        var applied = IsSelectionAppliedOnDisk();
        HellForgePack.Selection? selection = null;
        if (!isRegular)
        {
            try { selection = preferences.Selection(); }
            catch { selection = null; }
        }
        Task.Run(() =>
        {
            PackSizePreview estimate;
            try
            {
                var cacheRoot = ForgeDiskManager.GetCacheRoot(installRoot);
                var folders = PackSizeEstimator.CollectSearchFolders(installRoot, cacheRoot, GameLocator.AutoDetect());
                estimate = PackSizeEstimator.Estimate(isRegular, selection, folders, applied, cacheRoot);
            }
            catch
            {
                estimate = new PackSizePreview(isRegular, 0, 0, 0, 0, 0, 0, applied,
                    isRegular ? "No pack download needed" : "Size checked on apply",
                    "Exact download size is verified before any files download.");
            }
            if (ver == Volatile.Read(ref sizeEstimateVersion) && !disposed)
            {
                cachedSizePreview = estimate;
                Notify();
            }
        });
    }

    bool IsSelectionAppliedOnDisk()
    {
        if (lastStatus == null || lastMods == null) return false;
        var ours = lastMods.Mods.Where(m => m.Enabled && HellForgePack.IsStarWarsOverhaulMod(m)).ToArray();
        if (preferences.IsRegular)
            return ours.Length == 0
                && HellForgePreset.ModeCompanionProblem(lastMods, "regular") == null
                && lastStatus.Multiplayer && lastStatus.LocalBackend;
        return preferences.AppliedSelection == preferences.SelectionKey
            && ours.Length == 1
            && ours[0].Id == HellForgePack.PackId(preferences.PackMode)
            && HellForgePreset.ModeCompanionProblem(lastMods, preferences.PackMode) == null
            && lastStatus.Multiplayer && lastStatus.LocalBackend;
    }

    bool NeedsGameplaySetup() => lastMods != null && HellForgePreset.Problem(lastMods, checkOrder: true) != null;
    bool NeedsModeSetup() => !IsSelectionAppliedOnDisk();

    static string SteamUser()
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", null)?.ToString() ?? ""; }
        catch { return ""; }
    }

    static (string? Block, bool Unreadable) ReadCaptureBlock()
    {
        try { return (HellForgeForm.CaptureBlockCheck(), false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (e.ToString(), true); }
    }

    async Task RefreshAsync()
    {
        if (working || refreshing || disposed) return;
        refreshing = true;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        refreshCompletion = completed;
        var root = preferences.InstallRoot;
        try
        {
            if (root == null)
            {
                preferences.InstallRoot = HellForgeClient.Detect();
                root = preferences.InstallRoot;
                if (root != null) SavePrefs();
            }
            installationMissing = root != null && HellForgeForm.InstallationMissing(root);
            if (installationMissing)
            {
                lastStatus = null; lastMods = null; lastAccount = null;
                lastModsFingerprint = lastAccountFingerprint = null;
                syncRequested = false; warbondsPending = false; lastError = null;
                sessionWasActive = false; gameProcessObserved = false;
                homeTimer.Interval = TimeSpan.FromSeconds(5);
                UpdateDiscordPresence(running: false);
                (captureBlock, captureBlockUnreadable) = ReadCaptureBlock();
                return;
            }
            if (root == null) { lastError = null; return; }

            var client = CreateClient(root);
            var status = await client.StatusAsync(lifetime.Token);
            if (root != preferences.InstallRoot) return;
            var wasPreWindow = lastStatus?.Running == true && !gameProcessObserved;
            lastStatus = status;
            SyncGameProcessObserved();
            homeTimer.Interval = status.Running && !gameProcessObserved
                ? TimeSpan.FromMilliseconds(1500)
                : TimeSpan.FromSeconds(5);
            if (status.Running && !gameProcessObserved)
                taskbarProgress?.Invoke(new OperationProgress(ForgeTacticalCopy.LaunchingHeadline, ForgeTacticalCopy.LaunchingDetail, IsIndeterminate: true, CanCancel: false));
            else if (wasPreWindow && activeProgress == null)
                taskbarProgress?.Invoke(null);

            if (status.Running)
            {
                if (!sessionWasActive)
                {
                    sessionWasActive = true;
                    sessionStartUtc ??= DateTimeOffset.UtcNow;
                    UpdateDiscordPresence(running: true);
                }
            }
            else if (sessionWasActive)
            {
                HandleSessionEnded(root, status);
            }
            else if (lastTriage == null && status.HasRun)
            {
                lastTriage = CrashTriage.AnalyzeLastSession(root, status, CurrentConcreteTier());
            }

            string? fingerprint;
            try { fingerprint = root + "\n" + client.ModsFingerprint(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { fingerprint = null; }
            var reuse = lastMods != null && (status.Running ||
                fingerprint != null && fingerprint == lastModsFingerprint && DateTime.UtcNow - lastModsReadUtc < TimeSpan.FromMinutes(1));
            var mods = reuse ? lastMods! : await client.ModsAsync(lifetime.Token);
            if (!reuse) { lastModsFingerprint = fingerprint; lastModsReadUtc = DateTime.UtcNow; }

            if (!status.Running && !Game.IsRunning() && DateTime.UtcNow - lastPartialSweepUtc > TimeSpan.FromMinutes(10))
            {
                lastPartialSweepUtc = DateTime.UtcNow;
                try { client.SweepHashPartials(DateTime.UtcNow.AddMinutes(-10)); }
                catch { }
            }

            var (block, blockUnreadable) = ReadCaptureBlock();
            HellForgeAccountState? account = lastAccount;
            if (!status.Running && !Game.IsRunning() && !HellForgeAccount.SessionShimsActive(root))
            {
                string? accountKey;
                try { accountKey = root + "\n" + client.AccountFingerprint() + "\n" + SteamUser(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { accountKey = null; }
                if (account == null || accountKey == null || accountKey != lastAccountFingerprint || DateTime.UtcNow - lastAccountReadUtc >= TimeSpan.FromMinutes(5))
                {
                    var accountHelper = CreateAccount(root);
                    (var reviewedState, var reviewed, var reviewErr) = await HellForgeForm.ReviewAccountWithErrorAsync(accountHelper, lifetime.Token);
                    if (!reviewed && reviewErr != null && HellForgeAccount.IsTransientReviewError(reviewErr, root) &&
                        accountKey != null && (lastAccount is { Imported: true } || accountHelper.HasLocalImportReceipt()))
                    {
                        account = lastAccount is { Imported: true } prev
                            ? prev
                            : new(true, true, "Your HD2 account is imported for all three modes.");
                        accountReviewFailed = false;
                        lastAccountFingerprint = null;
                        lastAccountReadUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        account = reviewedState;
                        if (reviewed && account.Imported && block == null)
                        {
                            var opened = await HellForgeForm.TryOpenWarbondsAction(client, lifetime.Token);
                            if (opened > 0) { warbondsPending = false; toastNotice = $"Opened {opened} owned Warbonds."; }
                            else if (HellForgeWarbonds.ManagerWindowOpen()) warbondsPending = true;
                            else warbondsPending = false;
                        }
                        else if (reviewed && !account.Imported) warbondsPending = false;
                        accountReviewFailed = !reviewed;
                        lastAccountFingerprint = reviewed ? accountKey : null;
                        lastAccountReadUtc = DateTime.UtcNow;
                    }
                }
                else if (warbondsPending && account.Imported && block == null && !HellForgeWarbonds.ManagerWindowOpen())
                {
                    try
                    {
                        var opened = await HellForgeForm.OpenWarbondsAction(client, lifetime.Token);
                        warbondsPending = false;
                        if (opened > 0) { lastAccountFingerprint = null; toastNotice = $"Opened {opened} owned Warbonds."; }
                    }
                    catch { warbondsPending = true; }
                }
            }
            if (root != preferences.InstallRoot) return;
            lastStatus = status; lastMods = mods; lastAccount = account;
            captureBlock = block; captureBlockUnreadable = blockUnreadable;
            lastError = null;
            ScheduleSizeEstimate();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (!disposed && root == preferences.InstallRoot)
            {
                lastMods = null;
                lastError = e.Message;
                failureDetail = e.ToString();
            }
        }
        finally
        {
            refreshing = false;
            completed.TrySetResult();
            if (!disposed) Notify();
        }

        if (!disposed && !inSetup && syncRequested && captureBlock == null && lastStatus?.Running == false &&
            !Game.IsRunning() && lastAccount is { Captured: true, Imported: false })
        {
            await SyncAccountFromSettingsAsync();
        }
    }

    ForgeStepDownBannerState? ResolveStepDownBanner()
    {
        if (string.IsNullOrWhiteSpace(preferences.PendingStepDownReason) || preferences.IsRegular)
            return null;
        var current = CurrentConcreteTier();
        var suggested = preferences.LastStepDownSuggestedTier;
        if (string.IsNullOrWhiteSpace(suggested) || !PerformanceTierAdvisor.IsConcreteTier(suggested) || suggested == current)
            suggested = PerformanceTierAdvisor.NextLowerTier(current, cachedHardware);
        if (suggested == null) return null;

        var def = PerformanceTierAdvisor.GetTier(suggested, cachedHardware);
        return new ForgeStepDownBannerState(
            Reason: preferences.PendingStepDownReason!,
            NextTierId: def.Id,
            NextTierDisplayName: def.DisplayName,
            ActionLabel: $"Drop to {def.DisplayName}");
    }

    ForgeSelfInstallBannerState? ResolveSelfInstallBanner()
    {
        if (!selfInstallEligible || selfInstallDismissed) return null;
        return new ForgeSelfInstallBannerState(
            Message: "Running from Downloads — Pin Clonedivers to Start Menu & Desktop?",
            ActionLabel: "Install Shortcuts");
    }

    public ForgeHomeState GetHomeState()
    {
        ForgeUpdateBannerState? banner = null;
        if (launcherRelease != null && !updateDeferred && !working)
        {
            banner = launcherReplaced
                ? new ForgeUpdateBannerState(launcherRelease.Version, $"Clonedivers v{launcherRelease.Version} installed. Restart to switch to the new launcher.", "Restart Now", IsRestartReady: true)
                : updateFailed
                    ? new ForgeUpdateBannerState(launcherRelease.Version, $"Launcher update v{launcherRelease.Version} couldn't finish. Nothing was changed.", "Retry Update", IsFailed: true)
                    : new ForgeUpdateBannerState(launcherRelease.Version, $"Clonedivers v{launcherRelease.Version} is available ({Pack.FormatBytes(launcherRelease.Size)}). Game files and saves stay untouched.", "Update & Restart");
        }

        var (badgeText, badgeTone, headline, detail, ctaLabel, ctaEnabled, ctaTone, secondary) = ResolveHomePresentation();

        return new ForgeHomeState(
            PlatformTag: "HellForge · Windows",
            AppVersion: AppInfo.Version,
            Mode: preferences.Mode,
            EnemySkin: preferences.EnemySkin,
            DeltaSquad: preferences.DeltaSquad,
            Covenant: preferences.Covenant,
            Texture: preferences.Texture,
            AutoPerformanceTier: preferences.AutoPerformanceTier,
            PerformanceTier: preferences.PerformanceTier,
            Hardware: cachedHardware,
            HostChaosIntensity: preferences.HostChaosIntensity,
            HostHeavySeaf: preferences.HostHeavySeaf,
            HostFasterCorpseCleanup: preferences.HostFasterCorpseCleanup,
            SoundEffectsEnabled: preferences.SoundEffectsEnabled,
            SquadMatchHash: ComputeSquadMatchHash(),
            HeaderBadgeText: badgeText,
            HeaderBadgeTone: badgeTone,
            Headline: headline,
            Detail: detail,
            PrimaryActionLabel: ctaLabel,
            PrimaryActionEnabled: ctaEnabled,
            PrimaryActionTone: ctaTone,
            SecondaryActionLabel: secondary,
            ShowLocateMissing: installationMissing,
            IsBusy: working,
            IsSessionRunning: lastStatus?.Running == true,
            ControlsLocked: working || lastStatus?.Running == true,
            HasInstallation: preferences.InstallRoot != null && !installationMissing,
            InstallRoot: preferences.InstallRoot,
            AccountImported: lastAccount?.Imported == true,
            IsModeAppliedOnDisk: IsSelectionAppliedOnDisk(),
            Progress: activeProgress ?? (!working && IsPreWindowLaunching
                ? new OperationProgress(ForgeTacticalCopy.LaunchingHeadline, ForgeTacticalCopy.LaunchingDetail, IsIndeterminate: true, CanCancel: false)
                : null),
            UpdateBanner: banner,
            SizePreview: cachedSizePreview,
            ToastNotice: toastNotice,
            StepDownBanner: ResolveStepDownBanner(),
            SelfInstallBanner: ResolveSelfInstallBanner(),
            MinimizeToTray: preferences.MinimizeToTray,
            SquidSkin: preferences.SquidSkin,
            DeathTrooperVoice: preferences.DeathTrooperVoice);
    }

    (string Badge, ForgeHeroTone BadgeTone, string Headline, string Detail, string Cta, bool Enabled, ForgeHeroTone CtaTone, string? Secondary) ResolveHomePresentation()
    {
        if (working)
        {
            if (string.Equals(activeProgress?.Stage, ForgeTacticalCopy.LaunchingHeadline, StringComparison.Ordinal))
                return ("Launching…", ForgeHeroTone.Running, ForgeTacticalCopy.LaunchingHeadline,
                    ForgeTacticalCopy.LaunchingDetail, ForgeTacticalCopy.LaunchingPrimaryButton, false, ForgeHeroTone.Neutral, null);
            bool canCancel = activeProgress?.CanCancel == true;
            var pct = activeProgress?.Percent is { } p ? $" · {p}%" : "";
            return ("Working" + pct, ForgeHeroTone.Running, activeProgress?.Stage ?? "Working…",
                activeProgress?.SubStep ?? "Keep this launcher open while the current step finishes.",
                canCancel ? "CANCEL" : "WORKING…", canCancel, canCancel ? ForgeHeroTone.Danger : ForgeHeroTone.Neutral, null);
        }
        if (lastStatus?.Running == true)
        {
            if (!SyncGameProcessObserved())
                return ("Launching…", ForgeHeroTone.Running, ForgeTacticalCopy.LaunchingHeadline,
                    ForgeTacticalCopy.LaunchingDetail,
                    ForgeTacticalCopy.LaunchingPrimaryButton, false, ForgeHeroTone.Neutral, ForgeTacticalCopy.CancelLaunchButton);
            return ("Session Active", ForgeHeroTone.Running, ForgeTacticalCopy.RunningHeadline,
                ForgeTacticalCopy.RunningDetail,
                "END SESSION", true, ForgeHeroTone.Running, null);
        }
        if (IsGameProcessRunning())
            return ("Game Running", ForgeHeroTone.Warning, "Helldivers 2 is running",
                "Close Helldivers 2 before changing mods or starting a HellForge session.",
                "CLOSE GAME FIRST", false, ForgeHeroTone.Neutral, null);
        if (installationMissing)
            return ("Install Missing", ForgeHeroTone.Warning, "HellForge folder not found",
                "Locate your moved HellForge folder, or run Guided Setup to install fresh.",
                "RUN GUIDED SETUP", true, ForgeHeroTone.Action, "Locate Folder…");
        if (preferences.InstallRoot is { } upgRoot && HellForgeSetup.UpgradeProblem(upgRoot) != null)
            return ("Recovery Needed", ForgeHeroTone.Danger, "Restore previous HellForge",
                "An earlier Forge upgrade was interrupted. Restore the previous app before continuing.",
                "RECOVER FORGE UPGRADE", true, ForgeHeroTone.Action, null);
        if (lastStatus is { } old && HellForgeSetup.IsPrevious(old.Version))
            return ("Forge 0.3 → 0.4", ForgeHeroTone.Warning, "Upgrade HellForge to 0.4.0",
                "Upgrades Forge in place with automatic rollback backup. Your game copy & account stay intact.",
                "UPGRADE HELLFORGE", true, ForgeHeroTone.Action, null);
        if (preferences.InstallRoot == null)
            return ("Setup Needed", ForgeHeroTone.Action, "Let's get you ready",
                "Guided setup installs HellForge, copies build 25480438, syncs your HD2 account, and installs your pack.",
                "START GUIDED SETUP", true, ForgeHeroTone.Action, "Use Existing Forge…");
        if (lastError != null)
            return ("Needs Attention", ForgeHeroTone.Danger, "Couldn't finish that step",
                operationFailure ?? HellForgeForm.FailureSummary(lastError),
                "RETRY", true, ForgeHeroTone.Action, lastError.Contains("HellForge programs are missing", StringComparison.OrdinalIgnoreCase) ? "Locate Folder…" : null);
        if (lastStatus == null)
            return ("Checking…", ForgeHeroTone.Neutral, "Checking HellForge…",
                "Reading installation, gameplay preset, and account status.",
                "CHECKING…", false, ForgeHeroTone.Neutral, null);
        if (!lastStatus.GameCopyReady || !lastStatus.RunnerReady)
            return ("Finish Setup", ForgeHeroTone.Action, "Finish HellForge setup",
                "Continue guided setup to copy or resume the supported Helldivers 2 build (25480438).",
                "CONTINUE GUIDED SETUP", true, ForgeHeroTone.Action, null);
        if (lastStatus.CompatibilityProblem != null)
            return ("Compatibility", ForgeHeroTone.Warning, "HellForge setup needs attention",
                lastStatus.CompatibilityProblem,
                "CONTINUE GUIDED SETUP", true, ForgeHeroTone.Action, null);
        if (lastStatus.RecoveryProblem is { } rec)
        {
            bool gp = rec.StartsWith("Gameplay", StringComparison.Ordinal);
            return ("Recovery Needed", ForgeHeroTone.Warning, gp ? "Recover gameplay preset" : "Recover previous appearance",
                "Restore the interrupted change cleanly before launching.",
                gp ? "RECOVER GAMEPLAY PRESET" : "RECOVER APPEARANCE", true, ForgeHeroTone.Action, null);
        }
        if (!HellForgeForm.SteamReadyCheck())
            return ("Steam Offline", ForgeHeroTone.Warning, "Sign in to Steam",
                "Open Steam and sign in with the account that owns Helldivers 2 before launching.",
                "OPEN STEAM", true, ForgeHeroTone.Action, null);
        if (captureBlock != null)
            return ("Sync Pending", ForgeHeroTone.Warning, "Finish account sync cleanup",
                captureBlockUnreadable ? "Couldn't read hosts file to verify cleanup. Check Settings → Diagnostics."
                    : "In HellForge choose Profile → Account sync → Clean up, then close HellForge.",
                captureBlockUnreadable ? "RETRY CHECK" : "OPEN HELLFORGE TO CLEAN UP", true, ForgeHeroTone.Action, null);
        if (lastMods?.Problems.Length > 0)
            return ("Mod Notice", ForgeHeroTone.Warning, "Review duplicate or custom mods",
                "HellForge flagged mod conflicts. Resolve them in HellForge's Mods page, then retry.",
                "OPEN HELLFORGE MODS", true, ForgeHeroTone.Action, null);

        // #32 True 1-Click UPDATE & LAUNCH when account is already imported!
        if (lastAccount?.Imported == true && (NeedsGameplaySetup() || lastMods?.HostingProblem != null || NeedsModeSetup()))
        {
            var modeName = preferences.IsRegular ? "Regular" : preferences.Mode == "empire" ? "EmpireDivers" : "Clonedivers";
            var headline = NeedsGameplaySetup() || lastMods?.HostingProblem != null
                ? "Update mods & launch"
                : $"Switch to {modeName} & launch";
            var dlHint = !preferences.IsRegular && NeedsModeSetup() && cachedSizePreview.DownloadBytes > 0
                ? $"Downloads ~{Pack.FormatBytes(cachedSizePreview.DownloadBytes)}, applies your loadout, and launches automatically."
                : "Applies your selected loadout and launches Helldivers 2 in one click.";
            return ("Update & Launch", ForgeHeroTone.Action, headline, dlHint, "UPDATE & LAUNCH", true, ForgeHeroTone.Action, null);
        }

        if (NeedsGameplaySetup() || lastMods?.HostingProblem != null)
            return ("Mods Required", ForgeHeroTone.Action, "Install required gameplay mods",
                "Installs and configures the pinned 18-mod gameplay preset and load order.",
                "INSTALL REQUIRED MODS", true, ForgeHeroTone.Action, null);
        if (NeedsModeSetup())
        {
            var modeName = preferences.IsRegular ? "Regular" : preferences.Mode == "empire" ? "EmpireDivers" : "Clones";
            var dlHint = !preferences.IsRegular && cachedSizePreview.DownloadBytes > 0
                ? $"Requires ~{Pack.FormatBytes(cachedSizePreview.DownloadBytes)} verified download."
                : "Uses local cached files — no download required.";
            return ("Apply Loadout", ForgeHeroTone.Action, $"Apply {modeName} loadout",
                dlHint,
                $"SET UP {modeName.ToUpperInvariant()} MODE", true, ForgeHeroTone.Action, null);
        }
        if (lastAccount?.Imported != true)
            return ("Sync Account", ForgeHeroTone.Action,
                lastAccount?.Captured == true ? "Apply verified HD2 import" : "Sync your HD2 account",
                accountReviewFailed ? HellForgeForm.FailureSummary(lastAccount?.Message ?? "")
                    : "One-time guided sync brings your HD2 inventory into Forge (Forge progress stays local).",
                lastAccount?.Captured == true ? "FINISH ACCOUNT SYNC" : "SYNC HD2 ACCOUNT", true, ForgeHeroTone.Action, null);

        var playLabel = "LAUNCH " + (preferences.IsRegular ? "REGULAR HELLFORGE" : preferences.Mode == "empire" ? "EMPIREDIVERS" : "CLONEDIVERS");

        // #11 Plain-English Crash Triage on Home
        if (lastTriage is { Kind: CrashKind.HarmlessQuit })
        {
            return ("Forge 0.4.0 · Ready", ForgeHeroTone.Ready, "Ready to play",
                CrashTriage.HarmlessQuitMessage, playLabel, true, ForgeHeroTone.Ready, null);
        }
        if (lastTriage != null && lastTriage.Kind != CrashKind.Clean)
        {
            var msg = string.IsNullOrWhiteSpace(lastTriage.RecommendedAction)
                ? lastTriage.Summary
                : $"{lastTriage.Summary} {lastTriage.RecommendedAction}";
            return ("Last Run Notice", ForgeHeroTone.Warning, "Last session ended unexpectedly",
                msg, playLabel, true, ForgeHeroTone.Ready, null);
        }
        if (lastStatus.LastStatus is "failed" or "cleanup_failed" || lastStatus.LastFailure != null)
            return ("Last Run Warning", ForgeHeroTone.Warning, "Last session exited unexpectedly",
                lastStatus.LastFailure != null ? HellForgeForm.FailureSummary(lastStatus.LastFailure) : "Review Settings → Diagnostics before launching again.",
                playLabel, true, ForgeHeroTone.Ready, null);

        // #21 Crisp, understated faction-aware tactical ready line
        var readyDetail = lastStatus.LastNotice ?? ForgeTacticalCopy.ReadyDetail(preferences.Mode, firstLaunch: !lastStatus.HasRun);
        return ("Forge 0.4.0 · Ready", ForgeHeroTone.Ready, "Ready to play", readyDetail, playLabel, true, ForgeHeroTone.Ready, null);
    }

    public Task SelectModeAsync(string mode)
    {
        if (working || lastStatus?.Running == true) return Task.CompletedTask;
        try
        {
            operationFailure = null;
            toastNotice = null;
            preferences.Mode = mode;
            SavePrefs();
            ScheduleSizeEstimate();
        }
        catch (Exception e) { toastNotice = "Couldn't save preferences: " + e.Message; }
        Notify();
        return Task.CompletedTask;
    }

    public Task UpdateCustomizationAsync(string enemySkin, bool deltaSquad, bool covenant, string texture, string? squidSkin = null, bool? deathTrooperVoice = null)
    {
        if (working || lastStatus?.Running == true) return Task.CompletedTask;
        try
        {
            operationFailure = null;
            preferences.EnemySkin = enemySkin;
            preferences.DeltaSquad = deltaSquad;
            if (squidSkin != null) preferences.SquidSkin = squidSkin;
            else preferences.Covenant = covenant;
            if (deathTrooperVoice is { } dt) preferences.DeathTrooperVoice = dt;
            if (!string.Equals(preferences.Texture, texture, StringComparison.OrdinalIgnoreCase))
            {
                preferences.Texture = texture;
                preferences.AutoPerformanceTier = false;
                preferences.PerformanceTier = PerformanceTierAdvisor.TierFromPackTexture(texture);
            }
            SavePrefs();
            ScheduleSizeEstimate();
        }
        catch (Exception e) { toastNotice = "Couldn't save preferences: " + e.Message; }
        Notify();
        return Task.CompletedTask;
    }

    public Task SetPerformanceTierAsync(string tierId)
    {
        if (working || lastStatus?.Running == true) return Task.CompletedTask;
        try
        {
            operationFailure = null;
            preferences.ApplyPerformanceTier(tierId, cachedHardware);
            preferences.PendingStepDownReason = null;
            preferences.LastStepDownSuggestedTier = null;
            SavePrefs();
            ScheduleSizeEstimate();
        }
        catch (Exception e) { toastNotice = "Couldn't update performance tier: " + e.Message; }
        Notify();
        return Task.CompletedTask;
    }

    public Task UpdateHostMutatorsAsync(string chaosIntensity, string heavySeaf, bool fasterCorpseCleanup)
    {
        if (working || lastStatus?.Running == true) return Task.CompletedTask;
        try
        {
            preferences.HostChaosIntensity = chaosIntensity;
            preferences.HostHeavySeaf = heavySeaf;
            preferences.HostFasterCorpseCleanup = fasterCorpseCleanup;
            SavePrefs();
            TrySyncHostMutatorFile(preferences.InstallRoot);
        }
        catch (Exception e) { toastNotice = "Couldn't update host rules: " + e.Message; }
        Notify();
        return Task.CompletedTask;
    }

    public async Task ApplyCustomizationNowAsync()
    {
        if (working || lastStatus?.Running == true || preferences.InstallRoot == null || installationMissing) return;
        await PrepareSelectedModeAsync();
    }

    public async Task AcceptStepDownAsync()
    {
        var banner = ResolveStepDownBanner();
        if (banner == null || working || lastStatus?.Running == true) return;
        try
        {
            preferences.ApplyPerformanceTier(banner.NextTierId, cachedHardware);
            preferences.PendingStepDownReason = null;
            preferences.LastStepDownSuggestedTier = null;
            SavePrefs();
            ScheduleSizeEstimate();
            toastNotice = $"Switched to {banner.NextTierDisplayName} performance preset.";
            Notify();
            if (preferences.InstallRoot != null && !installationMissing && !IsSelectionAppliedOnDisk())
                await PrepareSelectedModeAsync();
        }
        catch (Exception e)
        {
            toastNotice = "Couldn't apply performance step-down: " + e.Message;
            Notify();
        }
    }

    public void DismissStepDownBanner()
    {
        preferences.PendingStepDownReason = null;
        preferences.LastStepDownSuggestedTier = null;
        try { SavePrefs(); } catch { }
        Notify();
    }

    public async Task InstallSelfShortcutsAsync()
    {
        if (Environment.ProcessPath is not { } exe || working) return;
        try
        {
            var res = await Task.Run(() => SelfInstallHelper.InstallToLocalAppDataAndCreateShortcuts(exe));
            selfInstallDismissed = true;
            UiSoundEffects.PlayCompletion();
            toastNotice = res.ShortcutsCreated
                ? "Clonedivers installed to LocalAppData and pinned to Start Menu & Desktop."
                : $"Copied Clonedivers to {res.InstalledExePath}.";
        }
        catch (Exception e)
        {
            toastNotice = "Couldn't create shortcuts: " + e.Message;
        }
        Notify();
    }

    public void DismissSelfInstallBanner()
    {
        selfInstallDismissed = true;
        Notify();
    }

    public Task<WhatsNewResult> FetchWhatsNewAsync(string? versionOrTag, CancellationToken ct = default) =>
        WhatsNewFetcher.FetchAsync(versionOrTag, ct, isLinux: false);

    public void ToggleSoundEffects()
    {
        preferences.SoundEffectsEnabled = !preferences.SoundEffectsEnabled;
        UiSoundEffects.Enabled = preferences.SoundEffectsEnabled;
        try { SavePrefs(); } catch { }
        Notify();
    }

    public void SetDiscordRpcEnabled(bool enabled)
    {
        preferences.DiscordRpcEnabled = enabled;
        discordRpc.Enabled = enabled;
        UpdateDiscordPresence(running: lastStatus?.Running == true);
        try { SavePrefs(); } catch { }
        Notify();
    }

    public void SetMinimizeToTray(bool enabled)
    {
        preferences.MinimizeToTray = enabled;
        try { SavePrefs(); } catch { }
        Notify();
    }

    public async Task TriggerPrimaryActionAsync()
    {
        if (working)
        {
            if (activeProgress?.CanCancel == true) workCts?.Cancel();
            return;
        }
        if (lastStatus?.Running == true)
        {
            if (SyncGameProcessObserved()) await EndSessionAsync();
            return;
        }
        if (IsGameProcessRunning()) return;
        if (installationMissing || preferences.InstallRoot == null || lastStatus is { GameCopyReady: false } or { RunnerReady: false } || lastStatus?.CompatibilityProblem != null)
        {
            await EnterSetupAsync();
            return;
        }
        if (preferences.InstallRoot is { } upgRoot && HellForgeSetup.UpgradeProblem(upgRoot) != null)
        {
            await RunWorkAsync(async ct =>
            {
                SetProgress(new("Recovering previous HellForge", "Restoring backed-up application files…", IsIndeterminate: true, CanCancel: false));
                await HellForgeSetup.RecoverUpgradeAsync(upgRoot, ct);
                toastNotice = "Your previous HellForge application has been restored.";
            });
            return;
        }
        if (lastStatus is { } old && HellForgeSetup.IsPrevious(old.Version))
        {
            await UpgradeForgeAsync();
            return;
        }
        if (lastError != null) { await RefreshAsync(); return; }
        if (lastStatus?.RecoveryProblem != null) { await PrepareSelectedModeAsync(); return; }
        if (!HellForgeForm.SteamReadyCheck())
        {
            try { HellForgeForm.OpenSteamClient(); toastNotice = "Sign in to Steam, then click Launch here."; Notify(); }
            catch (Exception e) { toastNotice = e.Message; Notify(); }
            return;
        }
        if (captureBlock != null)
        {
            if (captureBlockUnreadable) await RefreshAsync();
            else await OpenManagerAsync();
            return;
        }
        if (lastMods?.Problems.Length > 0) { await OpenManagerAsync(); return; }
        if (NeedsGameplaySetup() || lastMods?.HostingProblem != null || NeedsModeSetup())
        {
            if (lastAccount?.Imported == true)
                await PrepareAndLaunchAsync();
            else
                await PrepareSelectedModeAsync();
            return;
        }
        if (lastAccount?.Imported != true)
        {
            await EnterSetupAsync(forceStep: 3);
            return;
        }
        await LaunchGameAsync();
    }

    public async Task TriggerSecondaryActionAsync()
    {
        if (working) return;
        if (IsPreWindowLaunching)
        {
            await EndSessionAsync();
            return;
        }
        await LocateInstallAsync();
    }

    async Task RunWorkAsync(Func<CancellationToken, Task> action)
    {
        if (working || disposed) return;
        var pending = refreshCompletion?.Task ?? Task.CompletedTask;
        workCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        working = true;
        lastError = failureDetail = operationFailure = null;
        lastAccountFingerprint = null;
        Power.KeepAwake(true);
        SetProgress(new("Working…", "Please wait while this step finishes.", IsIndeterminate: true, CanCancel: false));
        try
        {
            await pending;
            await action(workCts.Token);
        }
        catch (OperationCanceledException)
        {
            toastNotice = "Operation cancelled. Verified files are saved so you can resume.";
        }
        catch (Exception e)
        {
            lastError = e.Message;
            failureDetail = e.ToString();
            operationFailure = HellForgeForm.FailureSummary(e.ToString());
            if (updateDownloading) updateFailed = true;
            toastNotice = operationFailure;
        }
        finally
        {
            Power.KeepAwake(false);
            working = false;
            updateDownloading = false;
            workCts?.Dispose();
            workCts = null;
            SetProgress(null);
            if (!disposed)
            {
                if (lastError == null) await RefreshAsync();
                Notify();
            }
        }
    }

    async Task UpgradeForgeAsync() => await RunWorkAsync(async ct =>
    {
        var speed = new SpeedTracker();
        var throttler = new ProgressThrottler(p => SetProgress(p));
        var report = new Progress<ForgeSetupProgress>(p =>
        {
            var (rate, eta) = p.Total > 0 ? speed.Update(p.Done, p.Total) : (null, null);
            throttler.Report(new(p.Message, null, 0, 0, p.Done, p.Total, rate, eta, IsIndeterminate: p.Total <= 0, CanCancel: p.CanCancel));
        });
        await HellForgeSetup.UpdateAsync(preferences.InstallRoot!, report, ct);
        var client = CreateClient(preferences.InstallRoot!);
        await InstallGameplayWithProgressAsync(client, p => SetProgress(p), ct);
        UiSoundEffects.PlayCompletion();
        requestAttention?.Invoke();
        toastNotice = "HellForge upgraded to 0.4.0 and verified.";
    });

    async Task InstallGameplayWithProgressAsync(HellForgeClient client, Action<OperationProgress> report, CancellationToken ct)
    {
        var throttler = new ProgressThrottler(report);
        report(new("Checking required gameplay mods", "Verifying installed 18-mod preset…", IsIndeterminate: true, CanCancel: true));
        var activeMode = preferences.IsRegular ? "regular" : preferences.PackMode;
        var missing = await client.MissingGameplayAsync(activeMode, ct);
        if (missing.Count > 0)
            await HellForgeGameplayCatalog.StageAsync(client.GameplayCache, missing, null, ct, null, throttler.AsProgress());
        await client.InstallGameplayAsync(client.GameplayCache, null, throttler.AsProgress(), ct, activeMode);
    }

    async Task PrepareSelectedModeAsync() => await RunWorkAsync(async ct =>
    {
        await PrepareSelectedModeCoreAsync(ct);
        UiSoundEffects.PlayCompletion();
        requestAttention?.Invoke();
    });

    async Task PrepareAndLaunchAsync() => await RunWorkAsync(async ct =>
    {
        bool recoveredOnly = await PrepareSelectedModeCoreAsync(ct);
        if (recoveredOnly)
        {
            UiSoundEffects.PlayCompletion();
            return;
        }
        await LaunchGameCoreAsync(ct);
    });

    /// <summary>Returns true when this run only recovered an interrupted transaction (so caller stops before launching).</summary>
    async Task<bool> PrepareSelectedModeCoreAsync(CancellationToken ct)
    {
        var client = CreateClient(preferences.InstallRoot!);
        var throttler = new ProgressThrottler(p => SetProgress(p));
        if (client.InterruptedGameplayProblem() != null)
        {
            SetProgress(new("Recovering gameplay preset", "Restoring previous gameplay mods…", IsIndeterminate: true, CanCancel: false));
            await client.RecoverGameplayAsync(ct);
            toastNotice = "Previous gameplay preset restored.";
            return true;
        }
        if ((await client.StatusAsync(ct)).RecoveryProblem != null)
        {
            SetProgress(new("Recovering previous appearance", "Restoring backed-up Star Wars pack…", IsIndeterminate: true, CanCancel: false));
            await Task.Run(() => client.RecoverInterruptedPackAsync(ct), ct);
            preferences.AppliedSelection = null; SavePrefs();
            toastNotice = "Previous appearance restored.";
            return true;
        }

        await InstallGameplayWithProgressAsync(client, p => SetProgress(p), ct);
        TrySyncHostMutatorFile(preferences.InstallRoot);

        if (preferences.IsRegular)
        {
            SetProgress(new("Applying Regular mode", "Disabling Star Wars packs while keeping gameplay mods…", IsIndeterminate: true, CanCancel: false));
            await client.PrepareRegularAsync(ct);
            preferences.AppliedSelection = preferences.SelectionKey; SavePrefs();
            toastNotice = "Regular HellForge is ready to play.";
            return false;
        }

        var selection = preferences.Selection();
        HellForgePack.Plan? freshPlan = null;
        var prepared = await FindPreparedAsync(client, selection, throttler.AsProgress(), ct);
        if (prepared == null)
        {
            var sources = CollectLocalSources(client);
            var output = Path.Combine(client.CacheRoot, "exports", preferences.PackMode + "-" + Guid.NewGuid().ToString("N"));
            var plan = await Task.Run(() => HellForgePack.PlanAsync(selection, output, sources, null, ct, throttler.AsProgress()), ct);
            if (plan.Missing.Count > 0)
            {
                var dlBytes = plan.Missing.Sum(f => f.Size);
                requestAttention?.Invoke();
                var ok = await confirmAsync(new ForgeConfirmRequest(
                    "Download Star Wars Pack?",
                    $"Download {Pack.FormatBytes(dlBytes)} ({plan.Missing.Count} files) of verified Star Wars assets?\n\n{plan.Local.Count} local files will be reused. Downloads pause and resume safely.",
                    $"Download ({Pack.FormatBytes(dlBytes)})",
                    "Cancel"));
                if (!ok) throw new OperationCanceledException();
            }
            await client.EnsureExportAndInstallCapacityAsync(plan, ct);
            await Task.Run(() => HellForgePack.ExportAsync(plan, true, null, ct, null, throttler.AsProgress()), ct);
            freshPlan = plan;
            prepared = output;
        }
        preferences.PreparedPack = prepared; SavePrefs();
        try
        {
            await Task.Run(() => client.InstallPackAsync(prepared, selection, throttler.AsProgress(), ct), ct);
        }
        catch (Exception ex)
        {
            if (freshPlan != null)
            {
                HellForgePack.ReclaimFailedInstallExport(freshPlan, ex);
                if (!Directory.Exists(prepared)) { preferences.PreparedPack = null; SavePrefs(); }
            }
            throw;
        }
        preferences.AppliedSelection = preferences.SelectionKey; SavePrefs();
        toastNotice = "Star Wars loadout installed and verified.";
        return false;
    }

    async Task LaunchGameAsync() => await RunWorkAsync(LaunchGameCoreAsync);

    async Task LaunchGameCoreAsync(CancellationToken ct)
    {
        var client = CreateClient(preferences.InstallRoot!);
        TrySyncHostMutatorFile(preferences.InstallRoot);

        if (warbondsPending)
        {
            if (HellForgeWarbonds.ManagerWindowOpen())
            {
                toastNotice = "Close the HellForge window before launching so your owned Warbonds can unlock.";
                return;
            }
            SetProgress(new("Unlocking owned Warbonds", null, IsIndeterminate: true, CanCancel: false));
            await HellForgeForm.OpenWarbondsAction(client, ct);
            warbondsPending = false;
        }
        else if (lastAccount?.Imported == true)
        {
            await HellForgeForm.TryOpenWarbondsAction(client, ct);
        }

        SetProgress(new("Verifying account & loadout", "Checking verified pack integrity…", IsIndeterminate: true, CanCancel: true));
        var profile = await HellForgeForm.EnsureAccountAction(client, ct);
        if (preferences.IsRegular)
        {
            SetProgress(new(ForgeTacticalCopy.LaunchingHeadline, ForgeTacticalCopy.LaunchingDetail, IsIndeterminate: true, CanCancel: false));
            await Task.Run(() => client.PlayRegularAsync(ct, profile), ct);
        }
        else
        {
            var speed = new SpeedTracker();
            var throttler = new ProgressThrottler(p => SetProgress(p));
            var bytes = new SyncProgress<(long Done, long Total)>(p =>
            {
                var (rate, eta) = speed.Update(p.Done, p.Total);
                throttler.Report(new("Verifying Star Wars files", "Fast layout verification before launch", 0, 0, p.Done, p.Total, rate, eta, CanCancel: true));
            });
            var statusReport = new SyncProgress<string>(msg =>
            {
                if (msg.StartsWith("Starting HellForge", StringComparison.OrdinalIgnoreCase))
                    SetProgress(new(ForgeTacticalCopy.LaunchingHeadline, ForgeTacticalCopy.LaunchingDetail, IsIndeterminate: true, CanCancel: false));
                else
                    SetProgress(new(msg, null, IsIndeterminate: true, CanCancel: false));
            });
            await Task.Run(() => client.PlayAsync(preferences.Selection(), ct, profile, statusReport, bytes), ct);
        }

        var launchedNow = IsGameProcessRunning();
        lastStatus = lastStatus != null ? lastStatus with { Running = true, GameLaunched = launchedNow ? true : null } : null;
        gameProcessObserved = launchedNow;
        homeTimer.Interval = launchedNow ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(1500);
        sessionWasActive = true;
        sessionStartUtc = DateTimeOffset.UtcNow;
        UpdateDiscordPresence(running: true);
        toastNotice = null;
    }

    async Task EndSessionAsync() => await RunWorkAsync(async ct =>
    {
        var root = preferences.InstallRoot!;
        bool cancellingLaunch = lastStatus?.Running == true && !gameProcessObserved;
        SetProgress(new(cancellingLaunch ? "Cancelling launch…" : "Ending HellForge session",
            cancellingLaunch ? "Stopping staging and completing sandbox cleanup…" : "Closing game and completing sandbox cleanup…",
            IsIndeterminate: true, CanCancel: false));
        var client = CreateClient(root);
        await client.EndSessionAsync(ct);
        var afterStatus = await client.StatusAsync(ct);
        HandleSessionEnded(root, afterStatus);
        if (lastTriage is { Kind: CrashKind.Clean })
            toastNotice = cancellingLaunch
                ? "Launch cancelled and HellForge cleanup is complete."
                : "Session ended and HellForge cleanup is complete.";
    });

    public async Task CleanCacheAsync()
    {
        if (working || preferences.InstallRoot == null || installationMissing || lastStatus?.Running == true)
            return;
        await RunWorkAsync(async ct =>
        {
            var root = preferences.InstallRoot;
            if (string.IsNullOrWhiteSpace(root) || installationMissing || lastStatus?.Running == true)
                return;
            SetProgress(new("Cleaning Forge cache", "Reclaiming disk space from stale cache and completed backups…", IsIndeterminate: true, CanCancel: false));
            try
            {
                var freed = await ForgeDiskManager.CleanSafeFilesAsync(root, ct);
                var msg = freed > 0
                    ? $"Reclaimed {Pack.FormatBytes(freed)} of stale cache and completed backups."
                    : "Cache is already clean — nothing needed removal.";
                updateMessage = msg;
                toastNotice = msg;
                ScheduleSizeEstimate();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                updateMessage = "Cache cleanup failed: " + e.Message;
                toastNotice = updateMessage;
                throw;
            }
        });
    }

    public async Task VerifyAndRepairAsync()
    {
        if (working || preferences.InstallRoot == null || installationMissing || lastStatus?.Running == true)
            return;
        await RunWorkAsync(async ct =>
        {
            var throttler = new ProgressThrottler(p => SetProgress(p));
            var report = await ForgeVerifier.VerifyAndRepairAsync(preferences.InstallRoot!, preferences, throttler.AsProgress(), ct);
            SavePrefs();
            UiSoundEffects.PlayCompletion();
            requestAttention?.Invoke();
            toastNotice = report.Summary;
            updateMessage = report.Summary;
            if (!report.Healthy)
            {
                lastError = report.Summary;
                operationFailure = report.Summary;
            }
        });
    }

    internal static async Task RequireUpdateIdleCoreAsync(
        string? installRoot,
        Func<bool> gameRunning,
        Func<string?> captureBlockCheck,
        Func<string, bool> installMissing,
        Func<string, bool> programsMissing,
        Func<string, CancellationToken, Task<bool>> sessionRunningAsync,
        CancellationToken ct)
    {
        if (gameRunning() || captureBlockCheck() != null)
            throw new InvalidOperationException("Finish the game or account sync before replacing the launcher.");
        if (installRoot != null && !installMissing(installRoot) && !programsMissing(installRoot) && await sessionRunningAsync(installRoot, ct))
            throw new InvalidOperationException("End the Forge session before replacing the launcher.");
    }

    Task RequireUpdateIdleAsync(CancellationToken ct) => RequireUpdateIdleCoreAsync(
        preferences.InstallRoot,
        Game.IsRunning,
        HellForgeForm.CaptureBlockCheck,
        HellForgeForm.InstallationMissing,
        HellForgeForm.ProgramsMissing,
        async (root, token) => (await CreateClient(root).StatusAsync(token)).Running,
        ct);

    public async Task TriggerUpdateBannerActionAsync()
    {
        if (launcherRelease == null || working) return;
        await RunWorkAsync(async ct =>
        {
            var app = launcherRelease;
            if (!ForgeLauncherUpdate.IsOffer(app, AppInfo.Version))
                throw new InvalidDataException("A verified Forge update is required.");
            await RequireUpdateIdleAsync(ct);
            if (launcherReplaced) { RestartUpdatedLauncher(app!.Version); return; }

            var ok = await confirmAsync(new ForgeConfirmRequest(
                $"Update Clonedivers to v{app!.Version}?",
                $"Downloads {Pack.FormatBytes(app.Size)} and restarts the launcher. Your game copy, mods, and account data stay untouched.",
                "Update & Restart",
                "Not Now"));
            if (!ok) return;

            updateDownloading = true; updateFailed = false;
            var exe = Environment.ProcessPath ?? throw new IOException("Could not locate launcher executable.");
            var paths = SelfUpdate.Paths(exe);
            SelfUpdate.ProbeWritable(paths.update);
            var speed = new SpeedTracker();
            var throttler = new ProgressThrottler(p => SetProgress(p));
            var progress = new SyncProgress<(long done, long total)>(p =>
            {
                var (rate, eta) = speed.Update(p.done, p.total);
                throttler.Report(new($"Downloading Clonedivers v{app.Version}", null, 0, 0, p.done, p.total, rate, eta, CanCancel: true));
            });
            await Pack.DownloadAsync(new PackFile { Url = app.Url, Size = app.Size, Sha256 = app.Sha256 }, paths.update, progress, ct);
            if (new FileInfo(paths.update).Length != app.Size || !string.Equals(await Pack.Sha256Async(paths.update, ct), app.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Launcher update failed SHA-256 verification; nothing was replaced.");
            SetProgress(new("Installing launcher update", "Swapping verified executable and restarting…", IsIndeterminate: true, CanCancel: false));
            await RequireUpdateIdleAsync(ct);
            ForgeLauncherUpdate.PreservePrevious(exe);
            ct.ThrowIfCancellationRequested();
            await Task.Run(() => SelfUpdate.Swap(paths.exe, paths.update, paths.old, TimeSpan.FromSeconds(30)), ct);
            launcherReplaced = true;
            RestartUpdatedLauncher(app.Version);
        });
    }

    internal void PrepareForUpdatedLauncherExit()
    {
        try { ForgeShellWindow.DisposeApplicationTrayIcons(); } catch { }
        try { discordRpc.Dispose(); } catch { }
    }

    void RestartUpdatedLauncher(string version)
    {
        var exe = Environment.ProcessPath!;
        PrepareForUpdatedLauncherExit();
        Program.ReleaseSingleInstance();
        Process.Start(new ProcessStartInfo(exe, "--updated " + version) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        Environment.Exit(0);
    }

    public void DismissUpdateBanner() { updateDeferred = true; updateFailed = false; Notify(); }

    public void OpenSteamLibrary()
    {
        try { Process.Start(new ProcessStartInfo("steam://nav/games/details/553850") { UseShellExecute = true }); }
        catch (Exception e) { toastNotice = HellForgeForm.FailureSummary(e.ToString()); Notify(); }
    }

    // ── Embedded 4-Stage Guided Setup ────────────────────────────────────────

    public Task EnterSetupAsync() => EnterSetupAsync(forceStep: null);

    async Task EnterSetupAsync(int? forceStep)
    {
        if (working) return;
        setupDrives = LocalDriveCatalog.EnumerateLocalDrives();
        var separate = lastStatus?.CompatibilityProblem != null &&
            (!HellForgeSetup.SupportsVersion(lastStatus.Version) || lastStatus.Engine != HellForgeRelease.Engine || lastStatus.ModSdk != HellForgeRelease.Sdk);
        setupInstallRoot = installationMissing || separate ? null : preferences.InstallRoot;
        if (setupInstallRoot != null)
        {
            setupFolder = setupInstallRoot;
            setupSelectedDrive = Path.GetPathRoot(setupInstallRoot);
        }
        else
        {
            var rec = setupDrives.FirstOrDefault(d => d.IsRecommended) ?? setupDrives.FirstOrDefault();
            setupSelectedDrive = rec?.Root;
            setupFolder = rec?.SuggestedInstallPath ?? HellForgeSetup.SeparateDefaultRoot();
        }
        setupPendingPlan = null;
        setupQrRows = null;
        setupDetail = null;
        setupPackReady = false;
        setupGameReady = lastStatus is { GameCopyReady: true, RunnerReady: true, CompatibilityProblem: null };
        setupStep = forceStep ?? (setupInstallRoot == null ? 1 : !setupGameReady ? 2 : lastAccount?.Imported != true ? 3 : 4);
        inSetup = true;
        SetupVisibilityChanged?.Invoke(true);
        setupTimer.Start();
        await PollSetupAsync();
        Notify();
    }

    public void ExitSetup()
    {
        if (setupBusy) return;
        inSetup = false;
        setupTimer.Stop();
        SetupVisibilityChanged?.Invoke(false);
        _ = RefreshAsync();
    }

    public void SelectSetupDrive(string driveRoot)
    {
        if (setupBusy) return;
        var match = setupDrives.FirstOrDefault(d => string.Equals(d.Root, driveRoot, StringComparison.OrdinalIgnoreCase));
        if (match == null) return;
        setupSelectedDrive = match.Root;
        setupFolder = match.SuggestedInstallPath;
        setupInstallRoot = null;
        setupDetail = null;
        Notify();
    }

    public async Task BrowseSetupFolderAsync(bool existing)
    {
        if (setupBusy) return;
        var picked = await pickFolderAsync(existing ? "Choose your existing HellForge folder" : "Choose a drive or parent folder for HellForge");
        if (string.IsNullOrWhiteSpace(picked)) return;
        try
        {
            var validated = existing ? HellForgeCommands.ValidateRoot(picked) : HellForgeSetup.ValidateTarget(Path.Combine(picked, "HellForge"));
            setupFolder = validated;
            setupSelectedDrive = Path.GetPathRoot(validated);
            setupInstallRoot = existing ? validated : null;
            setupPendingPlan = null;
            setupDetail = null;
            if (existing)
            {
                preferences.InstallRoot = validated;
                SavePrefs();
                installationMissing = false;
                await PollSetupAsync();
            }
            Notify();
        }
        catch (Exception e) { setupDetail = e.Message; Notify(); }
    }

    public Task SetSetupTextureAsync(string texture)
    {
        if (setupBusy || setupPackReady || preferences.Texture == texture) return Task.CompletedTask;
        preferences.Texture = texture;
        preferences.AutoPerformanceTier = false;
        preferences.PerformanceTier = PerformanceTierAdvisor.TierFromPackTexture(texture);
        SavePrefs();
        setupPendingPlan = null;
        setupDetail = "Texture quality updated. Click below to compute the exact download size.";
        Notify();
        ScheduleSizeEstimate();
        return Task.CompletedTask;
    }

    public void CancelSetupWork()
    {
        if (setupBusy && setupCanCancel) workCts?.Cancel();
    }

    public async Task OpenOfficialSetupFallbackAsync()
    {
        if (setupInstallRoot == null) return;
        try { HellForgeForm.OpenManager(setupInstallRoot); }
        catch (Exception e) { setupDetail = e.Message; Notify(); }
        await Task.CompletedTask;
    }

    public ForgeSetupState GetSetupState()
    {
        var steamReady = HellForgeForm.SteamReadyCheck();
        var freeBytes = HellForgeSetup.AvailableDriveBytes(setupFolder);
        var spaceOk = freeBytes is not long b || b >= HellForgeSetup.SetupBudget;
        var driveName = Path.GetPathRoot(setupFolder) ?? "Drive";
        var spaceSummary = freeBytes is long fb
            ? $"Space ({driveName}): {Pack.FormatBytes(fb)} free · {(spaceOk ? "OK" : "Low (< 70 GB)")}"
            : "Space: 70 GB required";

        var syncSnap = AccountSyncTracker.Evaluate(
            managerOpen: HellForgeWarbonds.ManagerWindowOpen(),
            captureBlock: captureBlock,
            captureBlockUnreadable: captureBlockUnreadable,
            gameRunning: Game.IsRunning(),
            accountCaptured: lastAccount?.Captured == true,
            accountImported: lastAccount?.Imported == true,
            syncRequested: syncRequested);

        var modeDesc = preferences.IsRegular
            ? "Regular Helldivers 2 + 18-mod gameplay preset"
            : $"{(preferences.Mode == "empire" ? "EmpireDivers" : preferences.DeltaSquad ? "Clonedivers (Delta Squad)" : "Clonedivers")} · Build {HellForgePack.SupportedBuild}";

        string headline, detail, cta;
        bool ctaEnabled = !setupBusy;
        if (setupStep == 1)
        {
            headline = "Step 1 of 4 · Space & Steam";
            detail = setupDetail ?? "Pick a local drive with at least 70 GB free outside Steam and OneDrive.";
            cta = !steamReady ? "OPEN STEAM & SIGN IN"
                : setupInstallRoot == null ? "INSTALL FORGE & COPY GAME"
                : lastStatus != null && HellForgeSetup.IsPrevious(lastStatus.Version) ? "UPGRADE HELLFORGE TO 0.4.0"
                : setupGameReady ? "CONTINUE TO ACCOUNT SYNC" : "CONTINUE GAME COPY";
        }
        else if (setupStep == 2)
        {
            headline = "Step 2 of 4 · Install Forge & Game Copy";
            detail = setupDetail ?? (setupQrRows != null
                ? "Scan the QR code below with the Steam mobile app to approve downloading build 25480438."
                : "Installing HellForge and preparing the separate build 25480438 game copy.");
            cta = setupBusy ? "INSTALLING…" : setupGameReady ? "CONTINUE TO ACCOUNT SYNC" : "RETRY GAME SETUP";
        }
        else if (setupStep == 3)
        {
            headline = "Step 3 of 4 · " + syncSnap.Headline;
            detail = setupDetail ?? syncSnap.Detail;
            cta = syncSnap.PrimaryActionLabel;
            ctaEnabled = !setupBusy && syncSnap.Phase != AccountSyncPhase.LaunchHd2AndPassTitle;
        }
        else
        {
            headline = setupPackReady ? "Step 4 of 4 · Ready to Play!" : "Step 4 of 4 · Gameplay & Star Wars Pack";
            detail = setupDetail ?? (setupPackReady
                ? "Everything is installed and verified. Return to Home to launch!"
                : setupPendingPlan is { Missing.Count: > 0 } plan
                    ? $"Ready to download {Pack.FormatBytes(plan.Missing.Sum(f => f.Size))} ({plan.Local.Count} local files reused)."
                    : "Choose your preferred texture quality, then click below to prepare your loadout.");
            cta = setupPackReady ? "FINISH & GO TO LAUNCHER"
                : setupPendingPlan is { Missing.Count: > 0 } p ? $"DOWNLOAD PACK ({Pack.FormatBytes(p.Missing.Sum(f => f.Size))})"
                : "INSTALL MODS & FINISH";
        }

        return new ForgeSetupState(
            Step: setupStep,
            SteamReady: steamReady,
            HasInstallation: setupInstallRoot != null,
            ForgeUpgradeAvailable: lastStatus != null && HellForgeSetup.IsPrevious(lastStatus.Version),
            GameCopyReady: setupGameReady,
            PackReady: setupPackReady,
            IsBusy: setupBusy,
            CanCancel: setupCanCancel,
            InstallFolder: setupFolder,
            Drives: setupDrives,
            SelectedDriveRoot: setupSelectedDrive,
            SpaceSummary: spaceSummary,
            SpaceOk: spaceOk,
            AccountSync: syncSnap,
            SelectedModeSummary: modeDesc,
            SelectedTexture: preferences.Texture,
            PendingDownloadBytes: setupPendingPlan?.Missing.Sum(f => f.Size) ?? 0,
            PendingCachedFiles: setupPendingPlan?.Local.Count ?? 0,
            Headline: headline,
            Detail: detail,
            PrimaryActionLabel: cta,
            PrimaryActionEnabled: ctaEnabled,
            ShowOfficialFallback: setupStep <= 2 && setupInstallRoot != null,
            Progress: setupProgress,
            QrRows: setupQrRows);
    }

    async Task PollSetupAsync()
    {
        if (!inSetup || setupBusy || setupPolling || disposed) return;
        setupPolling = true;
        try
        {
            (captureBlock, captureBlockUnreadable) = ReadCaptureBlock();
            if (setupInstallRoot != null && !HellForgeForm.InstallationMissing(setupInstallRoot))
            {
                var client = CreateClient(setupInstallRoot);
                try
                {
                    lastStatus = await client.StatusAsync(lifetime.Token);
                    setupGameReady = lastStatus.GameCopyReady && lastStatus.RunnerReady && !lastStatus.Running && lastStatus.CompatibilityProblem == null;
                }
                catch { }
                if (setupStep >= 3 && captureBlock == null && !Game.IsRunning() && lastStatus?.Running != true)
                {
                    (lastAccount, _) = await HellForgeForm.ReviewAccountAsync(CreateAccount(setupInstallRoot), lifetime.Token);
                    if (setupStep == 3 && lastAccount.Imported && captureBlock == null)
                    {
                        setupStep = 4;
                        setupDetail = null;
                    }
                    else if (setupStep == 3 && syncRequested && lastAccount is { Captured: true, Imported: false } && !HellForgeWarbonds.ManagerWindowOpen())
                    {
                        syncRequested = false;
                        await RunSetupAccountImportAsync();
                    }
                }
            }
        }
        finally
        {
            setupPolling = false;
            Notify();
        }
    }

    public async Task TriggerSetupPrimaryActionAsync()
    {
        if (setupBusy) return;
        if (setupStep == 1)
        {
            if (!HellForgeForm.SteamReadyCheck())
            {
                try { HellForgeForm.OpenSteamClient(); setupDetail = "Sign in to Steam, then click Continue here."; }
                catch (Exception e) { setupDetail = e.Message; }
                Notify();
                return;
            }
            if (setupInstallRoot == null)
            {
                var free = HellForgeSetup.AvailableDriveBytes(setupFolder);
                if (free is long b && b < HellForgeSetup.SetupBudget)
                {
                    setupDetail = $"Drive {Path.GetPathRoot(setupFolder)} only has {Pack.FormatBytes(b)} free. Pick a drive with at least 70 GB free.";
                    Notify();
                    return;
                }
            }
            setupStep = setupGameReady ? 3 : 2;
            setupDetail = null;
            Notify();
            if (setupStep == 2) await RunSetupStep2Async();
            return;
        }
        if (setupStep == 2)
        {
            if (setupGameReady) { setupStep = 3; setupDetail = null; Notify(); }
            else await RunSetupStep2Async();
            return;
        }
        if (setupStep == 3)
        {
            await RunSetupStep3ActionAsync();
            return;
        }
        if (setupPackReady)
        {
            ExitSetup();
            return;
        }
        await RunSetupStep4Async(allowDownload: setupPendingPlan is { Missing.Count: > 0 });
    }

    async Task RunSetupStep2Async()
    {
        setupBusy = true;
        setupDetail = null;
        Power.KeepAwake(true);
        workCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        Notify();
        try
        {
            var speed = new SpeedTracker();
            var throttler = new ProgressThrottler(p => SetProgress(p, forSetup: true));
            var report = new Progress<ForgeSetupProgress>(p =>
            {
                if (p.Message == "Signed in.") setupQrRows = null;
                var (rate, eta) = p.Total > 100 ? speed.Update(p.Done, p.Total) : (null, null);
                throttler.Report(new(
                    Stage: p.QrRows != null ? "Approve Steam Guard QR" : p.Message,
                    SubStep: p.QrRows != null ? "Scan the QR code in your Steam mobile app" : null,
                    BytesDone: p.Total > 100 ? p.Done : 0,
                    BytesTotal: p.Total > 100 ? p.Total : 0,
                    ItemIndex: p.Total == 100 ? (int)p.Done : 0,
                    ItemCount: p.Total == 100 ? 100 : 0,
                    BytesPerSecond: rate,
                    Eta: eta,
                    IsIndeterminate: p.Total <= 0,
                    CanCancel: p.CanCancel,
                    QrRows: p.QrRows));
            });

            if (setupInstallRoot == null)
            {
                setupInstallRoot = await HellForgeSetup.InstallAsync(setupFolder, report, workCts.Token);
                preferences.InstallRoot = setupInstallRoot;
                installationMissing = false;
                SavePrefs();
            }
            else if (lastStatus != null && HellForgeSetup.IsPrevious(lastStatus.Version))
            {
                await HellForgeSetup.UpdateAsync(setupInstallRoot, report, workCts.Token);
            }

            await HellForgeSetup.PrepareGameAsync(setupInstallRoot!, report, workCts.Token, client: CreateClient(setupInstallRoot!));
            lastStatus = await CreateClient(setupInstallRoot!).StatusAsync(workCts.Token);
            setupGameReady = lastStatus.GameCopyReady && lastStatus.RunnerReady && !lastStatus.Running && lastStatus.CompatibilityProblem == null;
            if (setupGameReady)
            {
                setupQrRows = null;
                (lastAccount, _) = await HellForgeForm.ReviewAccountAsync(CreateAccount(setupInstallRoot!), lifetime.Token);
                setupStep = lastAccount?.Imported == true ? 4 : 3;
                setupDetail = null;
                UiSoundEffects.PlayCompletion();
                requestAttention?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            setupDetail = "Download paused. Click Retry to resume saved files.";
        }
        catch (Exception e) { setupDetail = e.Message; }
        finally
        {
            Power.KeepAwake(false);
            setupBusy = false;
            setupCanCancel = false;
            setupQrRows = null;
            workCts?.Dispose();
            workCts = null;
            SetProgress(null, forSetup: true);
        }
    }

    async Task RunSetupStep3ActionAsync()
    {
        if (setupInstallRoot == null) return;
        if (captureBlock != null)
        {
            if (!captureBlockUnreadable) HellForgeForm.OpenManager(setupInstallRoot);
            else await PollSetupAsync();
            return;
        }
        if (lastAccount is { Captured: true, Imported: false })
        {
            await RunSetupAccountImportAsync();
            return;
        }
        setupBusy = true;
        Notify();
        try
        {
            var client = CreateClient(setupInstallRoot);
            await HellForgeForm.SelectAccountAction(client, lifetime.Token);
            CreateAccount(setupInstallRoot).DisableAutoApply();
            HellForgeForm.OpenManager(setupInstallRoot);
            syncRequested = true;
            setupDetail = "HellForge opened. Choose Profile → Account sync → Sync Inventory, pass HD2's title screen, quit HD2, then close HellForge.";
        }
        catch (Exception e) { setupDetail = e.Message; }
        finally { setupBusy = false; Notify(); }
    }

    async Task RunSetupAccountImportAsync()
    {
        if (setupInstallRoot == null || setupBusy) return;
        setupBusy = true;
        SetProgress(new("Importing verified HD2 account", "Merging sealed capture into Forge with backup…", IsIndeterminate: true, CanCancel: false), forSetup: true);
        try
        {
            var client = CreateClient(setupInstallRoot);
            await HellForgeForm.SelectAccountAction(client, lifetime.Token);
            await HellForgeForm.ImportAccountAction(client, lifetime.Token);
            lastAccount = await CreateAccount(setupInstallRoot).ReadAsync(lifetime.Token);
            if (lastAccount.Imported)
            {
                await HellForgeForm.TryOpenWarbondsAction(client, lifetime.Token);
                setupStep = 4;
                setupDetail = null;
                UiSoundEffects.PlayCompletion();
            }
        }
        catch (Exception e) { setupDetail = e.Message; }
        finally
        {
            setupBusy = false;
            SetProgress(null, forSetup: true);
        }
    }

    async Task RunSetupStep4Async(bool allowDownload)
    {
        if (setupInstallRoot == null || setupBusy) return;
        setupBusy = true;
        setupDetail = null;
        Power.KeepAwake(true);
        workCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        Notify();
        try
        {
            var ct = workCts.Token;
            var client = CreateClient(setupInstallRoot);
            var throttler = new ProgressThrottler(p => SetProgress(p, forSetup: true));

            await InstallGameplayWithProgressAsync(client, p => SetProgress(p, forSetup: true), ct);
            TrySyncHostMutatorFile(setupInstallRoot);

            if (preferences.IsRegular)
            {
                SetProgress(new("Applying Regular mode", "Configuring HellForge…", IsIndeterminate: true, CanCancel: false), forSetup: true);
                await client.PrepareRegularAsync(ct);
                preferences.AppliedSelection = preferences.SelectionKey; SavePrefs();
                setupPackReady = true;
                UiSoundEffects.PlayCompletion();
                requestAttention?.Invoke();
                return;
            }

            var consentedBytes = allowDownload && setupPendingPlan != null ? setupPendingPlan.Missing.Sum(f => f.Size) : 0L;
            var selection = preferences.Selection();
            if (setupPendingPlan == null ||
                HellForgePack.SelectionIdentity(setupPendingPlan.Selection) != HellForgePack.SelectionIdentity(selection) ||
                Directory.Exists(setupPendingPlan.Output) || File.Exists(setupPendingPlan.Output))
            {
                var reused = await FindPreparedAsync(client, selection, throttler.AsProgress(), ct);
                if (reused != null)
                {
                    setupPendingPlan = null;
                    preferences.PreparedPack = reused; SavePrefs();
                    await Task.Run(() => client.InstallPackAsync(reused, selection, throttler.AsProgress(), ct), ct);
                    preferences.AppliedSelection = preferences.SelectionKey; SavePrefs();
                    setupPackReady = true;
                    UiSoundEffects.PlayCompletion();
                    requestAttention?.Invoke();
                    return;
                }
                var sources = CollectLocalSources(client);
                var output = Path.Combine(client.CacheRoot, "exports", preferences.PackMode + "-" + Guid.NewGuid().ToString("N"));
                setupPendingPlan = await Task.Run(() => HellForgePack.PlanAsync(selection, output, sources, null, ct, throttler.AsProgress()), ct);
                var newMissing = setupPendingPlan.Missing.Sum(f => f.Size);
                if (!allowDownload && newMissing > 0) return;
                if (allowDownload && newMissing > consentedBytes)
                {
                    setupDetail = $"Download size updated to {Pack.FormatBytes(newMissing)}. Click Download Pack to confirm.";
                    return;
                }
            }

            if (setupPendingPlan.Missing.Count > 0 && !allowDownload) return;
            var plan = setupPendingPlan;
            await client.EnsureExportAndInstallCapacityAsync(plan, ct);
            await Task.Run(() => HellForgePack.ExportAsync(plan, true, null, ct, null, throttler.AsProgress()), ct);
            preferences.PreparedPack = plan.Output; SavePrefs();
            try
            {
                await Task.Run(() => client.InstallPackAsync(plan.Output, selection, throttler.AsProgress(), ct), ct);
            }
            catch (Exception ex)
            {
                HellForgePack.ReclaimFailedInstallExport(plan, ex);
                if (!Directory.Exists(plan.Output)) { preferences.PreparedPack = null; SavePrefs(); }
                throw;
            }
            preferences.AppliedSelection = preferences.SelectionKey; SavePrefs();
            setupPendingPlan = null;
            setupPackReady = true;
            UiSoundEffects.PlayCompletion();
            requestAttention?.Invoke();
        }
        catch (OperationCanceledException)
        {
            setupDetail = "Download paused. Verified files are saved — click Download Pack to resume.";
        }
        catch (Exception e) { setupDetail = e.Message; }
        finally
        {
            Power.KeepAwake(false);
            setupBusy = false;
            setupCanCancel = false;
            workCts?.Dispose();
            workCts = null;
            SetProgress(null, forSetup: true);
        }
    }

    async Task<string?> FindPreparedAsync(HellForgeClient client, HellForgePack.Selection selection, IProgress<OperationProgress> report, CancellationToken ct)
    {
        var candidates = new List<string>();
        if (preferences.PreparedPack is { } saved && Directory.Exists(saved)) candidates.Add(saved);
        var exports = Path.Combine(client.CacheRoot, "exports");
        FileSafety.RejectLink(exports);
        if (Directory.Exists(exports))
            candidates.AddRange(new DirectoryInfo(exports).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            report.Report(new("Checking cached pack exports", Path.GetFileName(candidate), IsIndeterminate: true, CanCancel: true));
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

    // ── Settings & Diagnostics ───────────────────────────────────────────────

    public async Task<ForgeDiagnosticsState> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var installRoot = preferences.InstallRoot;
        bool hasInstall = installRoot != null && !installationMissing;
        var disk = hasInstall
            ? await Task.Run(() => ForgeDiskManager.Analyze(installRoot), ct)
            : new ForgeDiskAnalysis(0, 0, 0, 0, 0, 0, 0);

        var triage = lastTriage ?? (hasInstall ? CrashTriage.AnalyzeLastSession(installRoot, lastStatus, CurrentConcreteTier()) : null);
        var matchHash = ComputeSquadMatchHash();
        var discordReport = DiscordReportFormatter.Format(
            appVersion: AppInfo.Version,
            squadMatchCode: matchHash,
            mode: preferences.Mode,
            enemySkin: preferences.EnemySkin,
            covenant: preferences.Covenant,
            deltaSquad: preferences.DeltaSquad,
            performanceTier: preferences.AutoPerformanceTier ? PerformanceTierAdvisor.Auto : preferences.PerformanceTier,
            packTexture: preferences.Texture,
            hardware: cachedHardware,
            installRoot: installRoot,
            triage: triage,
            redactUserPaths: true,
            squidSkin: preferences.SquidSkin,
            deathTrooperVoice: preferences.DeathTrooperVoice);

        var health = new List<ForgeHealthItem>
        {
            new("HellForge", lastStatus?.Version ?? (installationMissing ? "Missing" : "Not installed"),
                Healthy: lastStatus != null && HellForgeSetup.SupportsVersion(lastStatus.Version) && lastStatus.CompatibilityProblem == null,
                Warning: lastStatus == null),
            new("Game Copy", lastStatus?.GameCopyReady == true ? $"Build {lastStatus.Build}" : "Not ready",
                Healthy: lastStatus?.GameCopyReady == true && lastStatus.Build == HellForgePack.SupportedBuild,
                Warning: lastStatus == null),
            new("Gameplay Preset", lastMods == null ? "Unknown" : NeedsGameplaySetup() ? "Needs install" : $"{HellForgeGameplayCatalog.Current.Artifacts.Count}/{HellForgeGameplayCatalog.Current.Artifacts.Count} ({matchHash})",
                Healthy: lastMods != null && !NeedsGameplaySetup(),
                Warning: lastMods == null),
            new("Account Sync", lastAccount?.Imported == true ? "Imported" : lastAccount?.Captured == true ? "Captured" : "Not synced",
                Healthy: lastAccount?.Imported == true,
                Warning: lastAccount?.Imported != true),
            new("Steam", HellForgeForm.SteamReadyCheck() ? "Signed in" : "Not signed in",
                Healthy: HellForgeForm.SteamReadyCheck(),
                Warning: !HellForgeForm.SteamReadyCheck())
        };

        var sb = new StringBuilder();
        sb.AppendLine($"Launcher: Clonedivers v{AppInfo.Version} (Windows Avalonia)");
        sb.AppendLine($"Squad Match: {matchHash}");
        sb.AppendLine($"Installation: {installRoot ?? "Not selected"}");
        sb.AppendLine($"Disk Usage: {ForgeTacticalCopy.FormatDiskUsageSummary(disk, hasInstall)}");
        sb.AppendLine($"Selected Mode: {preferences.Mode} · EnemySkin={preferences.EnemySkin} · SquidSkin={preferences.SquidSkin} · Delta={preferences.DeltaSquad} · DeathTrooperVoice={preferences.DeathTrooperVoice} · PerfTier={preferences.PerformanceTier} · Texture={preferences.Texture}");
        sb.AppendLine($"Host Rules: Chaos={preferences.HostChaosIntensity} · HeavySeaf={preferences.HostHeavySeaf} · FasterCorpseCleanup={preferences.HostFasterCorpseCleanup}");
        sb.AppendLine($"Hardware: {cachedHardware.GpuName} ({cachedHardware.VramGb:0.#} GB VRAM, {cachedHardware.RamGb:0.#} GB RAM)");
        sb.AppendLine($"Account: {lastAccount?.Message ?? "Not checked"}");
        sb.AppendLine($"Capture Block: {captureBlock ?? "none"}");
        sb.AppendLine($"Launcher Diagnostic: {failureDetail ?? lastError ?? "none"}");
        if (lastStatus != null)
            sb.AppendLine($"Status: HF={lastStatus.Version} Engine={lastStatus.Engine} SDK={lastStatus.ModSdk} Build={lastStatus.Build} Copy={lastStatus.GameCopyReady} Runner={lastStatus.RunnerReady} Running={lastStatus.Running} Coop={lastStatus.Multiplayer} LocalBackend={lastStatus.LocalBackend} LastStatus={lastStatus.LastStatus ?? "none"} LastFailure={lastStatus.LastFailure ?? "none"}");
        if (lastMods != null)
            sb.AppendLine($"Mods ({lastMods.Mods.Count}): problems={lastMods.Problems.Length} hosting={lastMods.HostingProblem ?? "ok"}");

        bool idleInstall = !working && hasInstall && lastStatus?.Running != true;

        return new ForgeDiagnosticsState(
            InstallRoot: installRoot,
            AppVersion: AppInfo.Version,
            AccountSummary: lastAccount?.Message ?? "One-way HD2 → Forge account import. Forge rewards save locally.",
            HealthChecks: health,
            RawReportText: sb.ToString(),
            DiscordReportText: discordReport,
            DiskUsageSummary: ForgeTacticalCopy.FormatDiskUsageSummary(disk, hasInstall),
            CanOpenManager: !working && hasInstall,
            CanSyncAccount: idleInstall,
            CanCheckUpdates: !working && !checkingUpdates,
            CanOpenSetup: !working && lastStatus?.Running != true,
            CanCleanCache: idleInstall,
            CanVerifyAndRepair: idleInstall,
            IsCheckingUpdates: checkingUpdates,
            UpdateMessage: updateMessage,
            SoundEffectsEnabled: preferences.SoundEffectsEnabled,
            DiscordRpcEnabled: preferences.DiscordRpcEnabled,
            MinimizeToTray: preferences.MinimizeToTray);
    }

    public async Task LocateInstallAsync()
    {
        if (working) return;
        var picked = await pickFolderAsync("Choose the HellForge folder containing HellForge.exe");
        if (string.IsNullOrWhiteSpace(picked)) return;
        try
        {
            preferences.InstallRoot = HellForgeCommands.ValidateRoot(picked);
            SavePrefs();
            installationMissing = false;
            lastStatus = null; lastMods = null; lastAccount = null; lastError = null; lastTriage = null;
            Notify();
            await RefreshAsync();
        }
        catch (Exception e) { toastNotice = e.Message; Notify(); }
    }

    public void OpenInstallFolder()
    {
        if (string.IsNullOrWhiteSpace(preferences.InstallRoot) || !Directory.Exists(preferences.InstallRoot)) return;
        try { Process.Start(new ProcessStartInfo(preferences.InstallRoot) { UseShellExecute = true }); } catch { }
    }

    public Task OpenManagerAsync()
    {
        if (string.IsNullOrWhiteSpace(preferences.InstallRoot)) return Task.CompletedTask;
        try { HellForgeForm.OpenManager(preferences.InstallRoot); }
        catch (Exception e) { toastNotice = e.Message; Notify(); }
        return Task.CompletedTask;
    }

    public async Task SyncAccountFromSettingsAsync() => await EnterSetupAsync(forceStep: 3);

    public Task CheckUpdatesAsync() => CheckLauncherUpdateCoreAsync(interactive: true);

    async Task CheckLauncherUpdateCoreAsync(bool interactive)
    {
        if (checkingUpdates || working || disposed) return;
        if (!interactive && HellForgePreferences.CustomPreferencesPath() != null &&
            Environment.GetEnvironmentVariable("CLONEDIVERS_FORGE_SKIP_UPDATE_CHECK") == "1")
            return;
        checkingUpdates = true;
        Notify();
        try
        {
            launcherRelease = await ForgeLauncherUpdate.CheckAsync(AppInfo.Version, lifetime.Token);
            if (disposed) return;
            updateDeferred = false;
            updateMessage = launcherRelease == null
                ? $"Up to date (v{AppInfo.Version})"
                : $"Clonedivers v{launcherRelease.Version} is available.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (interactive) updateMessage = "Update check failed: " + HellForgeForm.FailureSummary(e.ToString());
        }
        finally
        {
            checkingUpdates = false;
            if (!disposed) Notify();
        }
    }

    public async Task<string?> SaveSupportReportAsync()
    {
        var diag = await GetDiagnosticsAsync();
        var suggested = $"clonedivers-support-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        var path = await saveFileAsync("Save Clonedivers support report", suggested);
        if (string.IsNullOrWhiteSpace(path)) return null;
        await File.WriteAllTextAsync(path, diag.RawReportText);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        homeTimer.Stop();
        setupTimer.Stop();
        Power.KeepAwake(false);
        await discordRpc.DisposeAsync();
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
