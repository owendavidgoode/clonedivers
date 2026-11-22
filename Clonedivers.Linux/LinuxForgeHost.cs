using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using Clonedivers.Ui;

namespace Clonedivers;

/// <summary>Linux HellForge controller backing the shared unified Avalonia launcher shell.</summary>
internal sealed class LinuxForgeHost : IForgeUiHost
{
    readonly LinuxForgePreferences preferences;
    readonly string resources;
    readonly bool preview;
    readonly string previewState = "ready";
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly ForgeOperations operations = new();
    readonly ForgeSyncHandoff syncHandoff = new();
    readonly DiscordRpcClient discordRpc = new();
    readonly SquadReportClient squadReporter = new();
    readonly HardwarePerformanceRecommendation cachedHardware;

    CancellationTokenSource? workCts;
    LinuxForgePlayer? player;
    ForgeAction primaryAction;
    ForgeAction retryAction = ForgeAction.Setup;
    bool requestedSetup;
    bool checkingUpdates;
    bool updateDeferred;
    bool updateFailed;
    bool disposed;
    bool sessionWasActive;
    DateTimeOffset? sessionStartUtc;
    string? lastProcessedSessionKey;
    CrashTriageResult? lastTriage;
    LauncherUpdateStatus? launcherUpdate;
    OperationProgress? activeProgress;
    string? failureDetail;
    string? reportPath;
    string? toastNotice;
    string? updateMessage;
    PackSizePreview cachedSizePreview;
    int sizeEstimateVersion;

    Func<ForgeConfirmRequest, Task<bool>> confirmAsync = _ => Task.FromResult(false);
    Func<string, Task<string?>> pickFolderAsync = _ => Task.FromResult<string?>(null);
    Action<OperationProgress?>? taskbarProgress;
    Action? requestAttention;

    public event Action? StateChanged;
    public event Action<bool>? SetupVisibilityChanged;

    public bool IsPreview => preview;
    public bool SupportsEmbeddedSetup => false;
    public bool SupportsSelfUpdate => false;
    internal string? RenderOutput { get; }
    internal string PreviewName => previewState + "-" + preferences.Mode;
    internal double PreviewWidth { get; } = 900;
    internal double PreviewHeight { get; } = 680;

    internal LinuxForgeHost(string[] args, LinuxForgePreferences? preferencesOverride = null)
    {
        preview = args.Contains("--forge-preview", StringComparer.Ordinal);
        preferences = preferencesOverride ?? (preview ? new() : LinuxForgePreferences.Load());
        resources = Path.Combine(AppContext.BaseDirectory, "forge");

        cachedHardware = preview
            ? PerformanceTierAdvisor.Recommend("AMD Radeon RX 7800 XT", 16.0, 32.0)
            : PerformanceTierAdvisor.ScanHardware();

        if (!preview && preferences.AutoPerformanceTier)
            preferences.Texture = PerformanceTierAdvisor.ResolvePackTexture(PerformanceTierAdvisor.Auto, cachedHardware);

        UiSoundEffects.Enabled = !preview && preferences.SoundEffectsEnabled;
        discordRpc.Enabled = !preview && preferences.DiscordRpcEnabled;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--forge": case "--gui": break;
                case "--forge-root": preferences.InstallRoot = NextValue(args, ref i); break;
                case "--forge-resources": resources = NextValue(args, ref i); break;
                case "--forge-preview": previewState = NextValue(args, ref i); break;
                case "--forge-render":
                    if (!preview) throw new ArgumentException("Rendering requires preview mode.");
                    RenderOutput = Path.GetFullPath(NextValue(args, ref i));
                    break;
                case "--forge-preview-mode":
                    if (!preview) throw new ArgumentException("Preview modes require preview mode.");
                    preferences.Mode = NextValue(args, ref i);
                    preferences.Validate();
                    break;
                case "--forge-preview-size":
                    if (!preview) throw new ArgumentException("Preview sizes require preview mode.");
                    var dims = NextValue(args, ref i).Split('x');
                    if (dims.Length != 2 || !int.TryParse(dims[0], out var w) || !int.TryParse(dims[1], out var h) || w is < 700 or > 2400 || h is < 600 or > 2000)
                        throw new ArgumentException("Invalid preview size.");
                    PreviewWidth = w; PreviewHeight = h;
                    break;
                default:
                    throw new ArgumentException("Unknown Forge desktop option: " + args[i]);
            }
        }
        if (preview && previewState is not ("ready" or "details" or "running" or "setup" or "error" or "sync" or "installing" or "disconnected" or "recovery" or "update-available" or "update-checking" or "update-failed"))
            throw new ArgumentException("Unknown preview state.");

        cachedSizePreview = new PackSizePreview(
            preferences.Mode == "regular", 0, 0, 0, 0, 0, 0, preview && previewState == "ready",
            preferences.Mode == "regular" ? "No pack download needed" : (preview ? "Ready · Active" : "Estimating size…"),
            preferences.Mode == "regular" ? "Original HD2 appearance with required Forge gameplay mods." : "Checking local cache…");

        timer.Tick += async (_, _) =>
        {
            if (!disposed && !preview && player is { Disconnected: false })
            {
                await operations.PollAsync(
                    () => RefreshPlayerAsync(background: true),
                    () => !disposed && ForgeWindowFlow.NeedsForegroundRefresh(requestedSetup, player),
                    action => _ = RunOpAsync(_ => action()),
                    () => RefreshPlayerAsync(background: false),
                    Notify);
            }
        };
    }

    static string NextValue(string[] args, ref int i) => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
    void Notify() => StateChanged?.Invoke();

    string CurrentConcreteTier() => PerformanceTierAdvisor.NormalizeConcreteTier(
        preferences.AutoPerformanceTier ? PerformanceTierAdvisor.Auto : preferences.PerformanceTier,
        cachedHardware);

    string ComputeSquadMatchHash() =>
        player?.State?.Mods is { Mods.Count: > 0 } m
            ? SquadFingerprint.Compute(m)
            : SquadFingerprint.Compute(HellForgeGameplayCatalog.Current);

    void SetProgress(OperationProgress? p)
    {
        void Apply()
        {
            if (disposed) return;
            activeProgress = p;
            taskbarProgress?.Invoke(p);
            Notify();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    void UpdateDiscordPresence(bool running)
    {
        if (preview) return;
        if (!running || !preferences.DiscordRpcEnabled)
        {
            discordRpc.ClearPresence();
            return;
        }
        var details = ForgeTacticalCopy.DiscordPresenceDetails(preferences.Mode, preferences.DeltaSquad);
        var state = $"Squad {ComputeSquadMatchHash()} · Linux Build {HellForgePack.SupportedBuild}";
        discordRpc.SetPresence(state, details);
    }

    void TrySyncHostMutatorFile()
    {
        if (preview) return;
        var root = player?.ResolvedInstallRoot ?? preferences.InstallRoot;
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            if (Directory.Exists(root))
                HostMutatorConfig.Write(root, preferences.ToHostMutatorOptions());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
    }

    void HandleSessionEnded(string? installRoot, HellForgeStatus? status)
    {
        double? durationSec = sessionStartUtc is { } started
            ? Math.Max(0d, (DateTimeOffset.UtcNow - started).TotalSeconds)
            : null;
        sessionWasActive = false;
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
            try { preferences.Save(); } catch { }
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

        if (!preview)
        {
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
        this.taskbarProgress = taskbarProgress;
        this.requestAttention = requestAttention;
    }

    public async Task InitializeAsync()
    {
        ScheduleSizeEstimate();
        if (preview) return;
        preferences.InstallRoot = LinuxForgeBootstrap.StartupRoot(preferences.InstallRoot);
        if (preferences.InstallRoot != null)
        {
            retryAction = ForgeAction.Connect;
            await RunOpAsync(ConnectAsync);
            var root = player?.ResolvedInstallRoot ?? preferences.InstallRoot;
            if (!disposed && root != null && player?.SessionActive != true)
                _ = ForgeDiskManager.AutoCleanStaleAsync(root, lifetime.Token);
        }
        timer.Start();
        await CheckUpdatesCoreAsync(interactive: false);
    }

    public bool CanClose(out string? reason)
    {
        if (preview) { reason = null; return true; }
        if (operations.Busy || player?.SessionActive == true)
        {
            reason = "Finish the current operation and end the active Forge session before closing.";
            return false;
        }
        reason = null;
        return true;
    }

    void ScheduleSizeEstimate()
    {
        var ver = Interlocked.Increment(ref sizeEstimateVersion);
        var isRegular = preferences.Mode == "regular";
        var root = player?.ResolvedInstallRoot ?? preferences.InstallRoot;
        if (preview)
        {
            cachedSizePreview = isRegular
                ? new PackSizePreview(true, 0, 0, 0, 0, 0, 0, true, "Active · No download", "Original HD2 visuals with required Forge gameplay mods.")
                : new PackSizePreview(false, 10_920_000_000, 10_920_000_000, 0, 378, 378, 0, true, "Ready · Active", "All 378 files found locally; switching needs no download.");
            Notify();
            return;
        }
        HellForgePack.Selection? selection = null;
        if (!isRegular)
        {
            try { selection = preferences.PackSelection(); }
            catch { selection = null; }
        }
        Task.Run(() =>
        {
            PackSizePreview est;
            try
            {
                var cache = ForgeDiskManager.GetCacheRoot(root);
                var folders = PackSizeEstimator.CollectSearchFolders(root, cache, GameLocator.AutoDetect());
                est = PackSizeEstimator.Estimate(isRegular, selection, folders, player?.CanLaunch == true, cache);
            }
            catch
            {
                est = new PackSizePreview(isRegular, 0, 0, 0, 0, 0, 0, player?.CanLaunch == true,
                    isRegular ? "No pack download needed" : "Size checked on apply",
                    "Exact download size is verified before any files download.");
            }
            if (ver == Volatile.Read(ref sizeEstimateVersion) && !disposed)
            {
                cachedSizePreview = est;
                Notify();
            }
        });
    }

    async Task RunOpAsync(Func<CancellationToken, Task> action)
    {
        if (operations.Busy || preview) return;
        failureDetail = null;
        workCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            await operations.RunAsync(() => action(workCts.Token), Notify);
        }
        finally
        {
            workCts?.Dispose();
            workCts = null;
            SetProgress(null);
        }
    }

    ForgeStepDownBannerState? ResolveStepDownBanner()
    {
        if (preview || string.IsNullOrWhiteSpace(preferences.PendingStepDownReason) || preferences.Mode == "regular")
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

    public ForgeHomeState GetHomeState()
    {
        var active = preview ? previewState == "running" : player?.SessionActive == true;
        var lost = preview ? previewState == "disconnected" : player?.Disconnected == true;
        var working = operations.Busy || (preview && previewState == "installing");
        var version = !updateDeferred ? launcherUpdate?.Available?.Version : null;
        if (preview && previewState is "update-available" or "update-failed") version = "1.0.0";

        var viewState = player?.State;
        if (preview && previewState is "sync" or "recovery")
        {
            viewState = new(new("/home/player/forge/pfx/drive_c/HellForge", "0.4.0-beta", "25480438", true, true, false, true, true, null, null,
                RecoveryProblem: previewState == "recovery" ? "Gameplay installation interrupted" : null),
                false, "", new(false, false, ""), null);
        }

        var view = ForgePresentation.Resolve(
            viewState,
            connected: preview ? previewState != "setup" : player != null,
            disconnected: lost,
            active: active,
            ownsSession: preview ? active : player?.OwnsSession == true,
            canLaunch: preview ? previewState is "ready" or "details" : player?.CanLaunch == true,
            busy: working,
            error: operations.Error ?? player?.Error ?? (preview && previewState == "error" ? "HttpRequestException: 504 /internal/diagnostic" : null),
            updateVersion: null,
            updateFailed: false,
            syncing: player?.RequestedAccountSync == true,
            retryAction: player == null ? retryAction : ForgeAction.Refresh);

        primaryAction = view.Action;

        bool canCancel = working && activeProgress?.CanCancel == true;
        bool isUpdateAndLaunch = !working && view.Action == ForgeAction.Prepare &&
                                 viewState?.Account?.Imported == true &&
                                 viewState?.Status.RecoveryProblem == null;

        var ctaTone = working
            ? (canCancel ? ForgeHeroTone.Danger : ForgeHeroTone.Neutral)
            : view.Action switch
            {
                ForgeAction.Launch => ForgeHeroTone.Ready,
                ForgeAction.EndSession => ForgeHeroTone.Running,
                ForgeAction.None => ForgeHeroTone.Neutral,
                _ => ForgeHeroTone.Action
            };

        var pctSuffix = working && activeProgress?.Percent is { } pct ? $" · {pct}%" : "";
        var badgeText = working ? "Working" + pctSuffix
            : active ? "Session Active"
            : lost ? "Disconnected"
            : isUpdateAndLaunch ? "Update & Launch"
            : view.Action == ForgeAction.Launch ? "Forge 0.4.0 · Ready"
            : view.Action == ForgeAction.Setup ? "Setup Needed"
            : "Action Needed";

        var badgeTone = working || active ? ForgeHeroTone.Running
            : lost || operations.Error != null ? ForgeHeroTone.Danger
            : view.Action == ForgeAction.Launch ? ForgeHeroTone.Ready
            : ForgeHeroTone.Warning;

        var headline = view.Headline;
        var detail = view.Detail;
        if (isUpdateAndLaunch)
        {
            var modeName = preferences.Mode == "regular" ? "Regular" : preferences.Mode == "empire" ? "EmpireDivers" : "Clonedivers";
            headline = $"Switch to {modeName} & launch";
            detail = preferences.Mode != "regular" && cachedSizePreview.DownloadBytes > 0
                ? $"Downloads ~{Pack.FormatBytes(cachedSizePreview.DownloadBytes)}, applies your loadout, and launches automatically."
                : "Applies your selected loadout and launches Helldivers 2 in one click.";
        }
        else if (view.Action == ForgeAction.Launch && viewState?.Status.LastFailure == null && viewState?.Status.LastNotice == null)
        {
            if (lastTriage is { Kind: CrashKind.HarmlessQuit })
                detail = CrashTriage.HarmlessQuitMessage;
            else if (lastTriage != null && lastTriage.Kind != CrashKind.Clean)
                detail = string.IsNullOrWhiteSpace(lastTriage.RecommendedAction) ? lastTriage.Summary : $"{lastTriage.Summary} {lastTriage.RecommendedAction}";
            else
                detail = ForgeTacticalCopy.ReadyDetail(preferences.Mode);
        }

        var ctaLabel = working
            ? (canCancel ? "CANCEL" : "WORKING…")
            : isUpdateAndLaunch ? "UPDATE & LAUNCH"
            : view.Action == ForgeAction.Launch
                ? "LAUNCH " + (preferences.Mode == "regular" ? "REGULAR HELLFORGE" : preferences.Mode == "empire" ? "EMPIREDIVERS" : "CLONEDIVERS")
                : view.Button.ToUpperInvariant();

        ForgeUpdateBannerState? banner = null;
        if (version != null && !active && !working)
        {
            banner = new ForgeUpdateBannerState(
                version,
                updateFailed || (preview && previewState == "update-failed")
                    ? $"Couldn't open Clonedivers v{version} release page."
                    : $"Clonedivers v{version} for Linux is ready on GitHub.",
                "View Release",
                IsFailed: updateFailed || (preview && previewState == "update-failed"));
        }

        var prog = activeProgress ?? (preview && previewState == "installing"
            ? new OperationProgress("Downloading Star Wars pack", "9ba626afa44a3aa3.patch_0", 142, 378, 5_798_205_849, 10_952_166_604, 44_354_764, TimeSpan.FromSeconds(116), CanCancel: true)
            : working
                ? new OperationProgress("Working…", "Keep this launcher open while the current step finishes.", IsIndeterminate: true)
                : null);

        return new ForgeHomeState(
            PlatformTag: "HellForge · Linux",
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
            PrimaryActionEnabled: working ? canCancel : view.Action != ForgeAction.None,
            PrimaryActionTone: ctaTone,
            SecondaryActionLabel: !working && player == null && !preview ? "Locate Existing…" : null,
            ShowLocateMissing: false,
            IsBusy: working,
            IsSessionRunning: active,
            ControlsLocked: working || active,
            HasInstallation: player != null || !string.IsNullOrWhiteSpace(preferences.InstallRoot),
            InstallRoot: player?.ResolvedInstallRoot ?? preferences.InstallRoot,
            AccountImported: viewState?.Account?.Imported == true,
            IsModeAppliedOnDisk: view.Action == ForgeAction.Launch,
            Progress: prog,
            UpdateBanner: banner,
            SizePreview: cachedSizePreview,
            ToastNotice: toastNotice,
            StepDownBanner: ResolveStepDownBanner(),
            SelfInstallBanner: null,
            MinimizeToTray: preferences.MinimizeToTray);
    }

    public ForgeSetupState GetSetupState() => throw new NotSupportedException("Linux uses native official setup.");

    public async Task SelectModeAsync(string mode)
    {
        if (preview || operations.Busy || player?.SessionActive == true) return;
        await RunOpAsync(async ct =>
        {
            preferences.Mode = mode;
            preferences.Save();
            if (player != null) await player.ApplySelectionAsync(ct);
            ScheduleSizeEstimate();
        });
    }

    public async Task UpdateCustomizationAsync(string enemySkin, bool deltaSquad, bool covenant, string texture)
    {
        if (preview || operations.Busy || player?.SessionActive == true) return;
        await RunOpAsync(async ct =>
        {
            preferences.EnemySkin = enemySkin;
            preferences.DeltaSquad = deltaSquad;
            preferences.Covenant = covenant;
            if (!string.Equals(preferences.Texture, texture, StringComparison.OrdinalIgnoreCase))
            {
                preferences.Texture = texture;
                preferences.AutoPerformanceTier = false;
                preferences.PerformanceTier = PerformanceTierAdvisor.TierFromPackTexture(texture);
            }
            preferences.Save();
            if (player != null) await player.ApplySelectionAsync(ct);
            ScheduleSizeEstimate();
        });
    }

    public async Task SetPerformanceTierAsync(string tierId)
    {
        if (preview || operations.Busy || player?.SessionActive == true) return;
        await RunOpAsync(async ct =>
        {
            preferences.ApplyPerformanceTier(tierId, cachedHardware);
            preferences.PendingStepDownReason = null;
            preferences.LastStepDownSuggestedTier = null;
            preferences.Save();
            if (player != null) await player.ApplySelectionAsync(ct);
            ScheduleSizeEstimate();
        });
    }

    public Task UpdateHostMutatorsAsync(string chaosIntensity, string heavySeaf, bool fasterCorpseCleanup)
    {
        if (preview || operations.Busy || player?.SessionActive == true) return Task.CompletedTask;
        try
        {
            preferences.HostChaosIntensity = chaosIntensity;
            preferences.HostHeavySeaf = heavySeaf;
            preferences.HostFasterCorpseCleanup = fasterCorpseCleanup;
            preferences.Save();
            TrySyncHostMutatorFile();
        }
        catch (Exception e) { toastNotice = "Couldn't save host rules: " + e.Message; }
        Notify();
        return Task.CompletedTask;
    }

    public async Task ApplyCustomizationNowAsync()
    {
        if (preview || operations.Busy || player == null || player.SessionActive) return;
        await RunOpAsync(async ct =>
        {
            if (await PrepareAsync(ct))
            {
                UiSoundEffects.PlayCompletion();
                requestAttention?.Invoke();
            }
        });
    }

    public async Task AcceptStepDownAsync()
    {
        var banner = ResolveStepDownBanner();
        if (banner == null || preview || operations.Busy || player?.SessionActive == true) return;
        await SetPerformanceTierAsync(banner.NextTierId);
        toastNotice = $"Switched to {banner.NextTierDisplayName} performance preset.";
        Notify();
        if (player != null && !player.CanLaunch)
            await ApplyCustomizationNowAsync();
    }

    public void DismissStepDownBanner()
    {
        preferences.PendingStepDownReason = null;
        preferences.LastStepDownSuggestedTier = null;
        if (!preview) { try { preferences.Save(); } catch { } }
        Notify();
    }

    public Task InstallSelfShortcutsAsync() => Task.CompletedTask;
    public void DismissSelfInstallBanner() { }

    public Task<WhatsNewResult> FetchWhatsNewAsync(string? versionOrTag, CancellationToken ct = default) =>
        WhatsNewFetcher.FetchAsync(versionOrTag, ct, isLinux: true);

    public void ToggleSoundEffects()
    {
        preferences.SoundEffectsEnabled = !preferences.SoundEffectsEnabled;
        if (!preview)
        {
            UiSoundEffects.Enabled = preferences.SoundEffectsEnabled;
            try { preferences.Save(); } catch { }
        }
        Notify();
    }

    public void SetDiscordRpcEnabled(bool enabled)
    {
        preferences.DiscordRpcEnabled = enabled;
        discordRpc.Enabled = !preview && enabled;
        UpdateDiscordPresence(running: player?.SessionActive == true);
        if (!preview) { try { preferences.Save(); } catch { } }
        Notify();
    }

    public void SetMinimizeToTray(bool enabled)
    {
        preferences.MinimizeToTray = enabled;
        if (!preview) { try { preferences.Save(); } catch { } }
        Notify();
    }

    public async Task TriggerPrimaryActionAsync()
    {
        if (preview) return;
        if (operations.Busy)
        {
            if (activeProgress?.CanCancel == true) workCts?.Cancel();
            return;
        }
        var action = primaryAction;
        bool autoLaunchAfterPrepare = action == ForgeAction.Prepare &&
                                      player?.State?.Account?.Imported == true &&
                                      player?.State?.Status.RecoveryProblem == null;
        retryAction = action is ForgeAction.Setup or ForgeAction.Connect ? action : ForgeAction.Refresh;
        await RunOpAsync(async ct =>
        {
            switch (action)
            {
                case ForgeAction.Setup: await SetupAsync(ct); break;
                case ForgeAction.Connect: await ConnectAsync(ct); break;
                case ForgeAction.EndSession:
                    await player!.EndSessionAsync(ct);
                    HandleSessionEnded(player.ResolvedInstallRoot ?? preferences.InstallRoot, player.State?.Status);
                    break;
                case ForgeAction.Launch:
                    await LaunchCoreAsync(ct);
                    break;
                case ForgeAction.Manager: await player!.OpenManagerAsync(ct); break;
                case ForgeAction.Prepare:
                    var preparedOk = await PrepareAsync(ct);
                    if (preparedOk && autoLaunchAfterPrepare && player?.CanLaunch == true)
                        await LaunchCoreAsync(ct);
                    else if (preparedOk)
                    {
                        UiSoundEffects.PlayCompletion();
                        requestAttention?.Invoke();
                    }
                    break;
                case ForgeAction.Sync: await FinishAccountAsync(ct); break;
                case ForgeAction.Refresh:
                    if (player == null) await ConnectAsync(ct);
                    else await RefreshPlayerAsync(background: false);
                    break;
            }
        });
    }

    async Task LaunchCoreAsync(CancellationToken ct)
    {
        if (player == null) return;
        TrySyncHostMutatorFile();
        SetProgress(new("Starting HellForge session", "Verifying loadout and launching Helldivers 2…", IsIndeterminate: true));
        await player.LaunchAsync(ct);
        sessionWasActive = true;
        sessionStartUtc = DateTimeOffset.UtcNow;
        UpdateDiscordPresence(running: true);
    }

    public async Task TriggerSecondaryActionAsync() => await LocateInstallAsync();

    public Task TriggerUpdateBannerActionAsync()
    {
        if (launcherUpdate == null || preview) return Task.CompletedTask;
        try { LauncherUpdates.OpenRelease(launcherUpdate); updateFailed = false; updateDeferred = true; }
        catch (Exception ex) { updateFailed = true; failureDetail = ex.ToString(); }
        Notify();
        return Task.CompletedTask;
    }

    public void DismissUpdateBanner() { updateDeferred = true; updateFailed = false; Notify(); }

    public void OpenSteamLibrary()
    {
        if (preview) return;
        try
        {
            var game = GameLocator.NormalizeGameDir(Settings.Load().GamePath) ?? GameLocator.AutoDetect()
                ?? throw new InvalidOperationException("Open your usual Steam client manually. No installed HD2 library was found.");
            toastNotice = "Opened HD2 library in " + LinuxPlatform.OpenGameLibrary(game) + ".";
        }
        catch (Exception e) { toastNotice = e.Message; }
        Notify();
    }

    async Task ConnectAsync(CancellationToken ct)
    {
        if (player is { Disconnected: true }) { var lost = player; player = null; await syncHandoff.ReleaseAsync(lost); }
        if (player != null) throw new InvalidOperationException("Close this launcher before switching its active Forge installation.");
        SetProgress(new("Connecting to HellForge", "Starting isolated Linux namespace bridge…", IsIndeterminate: true));
        await syncHandoff.ConnectAsync(() => LinuxForgePlayer.ConnectAsync(preferences, resources, ct), p => player = p, ct);
        if (player?.SessionActive == true)
        {
            sessionWasActive = true;
            sessionStartUtc ??= DateTimeOffset.UtcNow;
            UpdateDiscordPresence(running: true);
        }
        else if (player?.State?.Status is { HasRun: true } st)
        {
            lastTriage = CrashTriage.AnalyzeLastSession(player.ResolvedInstallRoot ?? preferences.InstallRoot, st, CurrentConcreteTier());
        }
        ScheduleSizeEstimate();
    }

    async Task SetupAsync(CancellationToken ct)
    {
        if (player?.SessionActive == true) throw new InvalidOperationException("End the Forge session before setup.");
        if (player != null) { var existing = player; player = null; await syncHandoff.ReleaseAsync(existing); }
        var throttler = new ProgressThrottler(p => SetProgress(p));
        var result = await LinuxForgeBootstrap.InstallAsync(preferences.InstallRoot, null, throttler.AsProgress(), ct);
        preferences.InstallRoot = result;
        preferences.Save();
        await ConnectAsync(ct);
        requestedSetup = true;
        if (ForgeWindowFlow.ShouldPrepareAfterSetup(requestedSetup, player)) { requestedSetup = false; _ = await PrepareAsync(ct); }
    }

    async Task RefreshPlayerAsync(bool background)
    {
        if (player == null) return;
        await player.RefreshAsync(lifetime.Token, preserveError: background, allowImport: !background);
        if (player.SessionActive)
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
            HandleSessionEnded(player.ResolvedInstallRoot ?? preferences.InstallRoot, player.State?.Status);
        }

        if (!background && ForgeWindowFlow.ShouldPrepareAfterSetup(requestedSetup, player))
        {
            requestedSetup = false;
            _ = await PrepareAsync(lifetime.Token);
        }
    }

    async Task FinishAccountAsync(CancellationToken ct)
    {
        if (player == null) return;
        if (player.State?.Account is { Captured: true, Imported: false })
        {
            SetProgress(new("Importing HD2 account", "Merging sealed capture into Forge…", IsIndeterminate: true));
            await player.ImportAsync(ct);
            UiSoundEffects.PlayCompletion();
        }
        else if (player.State?.Account?.Imported != true)
        {
            await player.BeginAccountSyncAsync(ct);
            toastNotice = ForgePresentation.SyncInstructions;
        }
    }

    /// <summary>Deterministic Linux loadout preparation (QA Bugfix #2: files, GB, MB/s, ETA, ProgressThrottler + SpeedTracker).</summary>
    async Task<bool> PrepareAsync(CancellationToken ct)
    {
        if (player == null) return false;
        if (player.State?.Status.RecoveryProblem != null)
        {
            SetProgress(new("Recovering previous setup", "Restoring interrupted transaction…", IsIndeterminate: true, CanCancel: false));
            if (player.State.Status.RecoveryProblem.StartsWith("Gameplay", StringComparison.Ordinal))
                await player.RecoverGameplayAsync(ct);
            else
                await player.RecoverAppearanceAsync(ct);
            return false;
        }

        var throttler = new ProgressThrottler(p => SetProgress(p));
        await player.InstallGameplayAsync(null, ct, throttler.AsProgress());
        TrySyncHostMutatorFile();

        if (preferences.Mode == "regular")
        {
            await player.PrepareAppearanceAsync(null, false, null, ct, throttler.AsProgress());
            await FinishAccountAsync(ct);
            ScheduleSizeEstimate();
            return true;
        }

        var plan = await player.PlanAppearanceAsync(null, ct, throttler.AsProgress());
        if (plan.Missing.Count > 0)
        {
            var bytes = plan.Missing.Sum(f => f.Size);
            requestAttention?.Invoke();
            var ok = await confirmAsync(new ForgeConfirmRequest(
                "Download Star Wars Pack?",
                $"Download {Pack.FormatBytes(bytes)} ({plan.Missing.Count} files) of verified appearance assets? {plan.Local.Count} local files will be reused.",
                $"Download ({Pack.FormatBytes(bytes)})",
                "Cancel"));
            if (!ok) return false;
        }
        await player.PrepareAppearanceAsync(plan, true, null, ct, throttler.AsProgress());
        await FinishAccountAsync(ct);
        ScheduleSizeEstimate();
        return true;
    }

    public async Task CleanCacheAsync()
    {
        var initialRoot = player?.ResolvedInstallRoot ?? preferences.InstallRoot;
        if (preview || operations.Busy || string.IsNullOrWhiteSpace(initialRoot) || player?.SessionActive == true)
            return;
        await RunOpAsync(async ct =>
        {
            var root = player?.ResolvedInstallRoot ?? preferences.InstallRoot;
            if (string.IsNullOrWhiteSpace(root) || player?.SessionActive == true)
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
        if (preview || operations.Busy || player == null || player.SessionActive)
            return;
        await RunOpAsync(async ct =>
        {
            var throttler = new ProgressThrottler(p => SetProgress(p));
            throttler.Report(new("Verifying Forge installation", "Checking HellForge 0.4.0-beta & build 25480438…", 1, 5, CanCancel: true));
            await player.RefreshAsync(ct);

            if (player.State?.Status.RecoveryProblem != null)
            {
                if (player.State.Status.RecoveryProblem.StartsWith("Gameplay", StringComparison.Ordinal))
                    await player.RecoverGameplayAsync(ct);
                else
                    await player.RecoverAppearanceAsync(ct);
            }

            throttler.Report(new("Verifying gameplay preset", "Checking 14-mod r7 hashes and load order…", 2, 5, CanCancel: true));
            await player.InstallGameplayAsync(null, ct, throttler.AsProgress());
            TrySyncHostMutatorFile();

            throttler.Report(new("Verifying active pack", preferences.Mode == "regular" ? "Checking Regular configuration…" : "Checking Star Wars pack integrity…", 3, 5, CanCancel: true));
            if (preferences.Mode == "regular")
            {
                await player.PrepareAppearanceAsync(null, false, null, ct, throttler.AsProgress());
            }
            else
            {
                var plan = await player.PlanAppearanceAsync(null, ct, throttler.AsProgress());
                await player.PrepareAppearanceAsync(plan, true, null, ct, throttler.AsProgress());
            }

            var root = player.ResolvedInstallRoot ?? preferences.InstallRoot;
            throttler.Report(new("Cleaning stale caches", "Reclaiming disk space from old backups and partial files…", 5, 5, CanCancel: false));
            var freed = await ForgeDiskManager.CleanSafeFilesAsync(root, ct);

            UiSoundEffects.PlayCompletion();
            requestAttention?.Invoke();
            var summary = freed > 0
                ? $"Verified Forge, r7 gameplay preset, and active pack; freed {Pack.FormatBytes(freed)}."
                : "All checks passed — Forge, r7 gameplay preset, and active pack are healthy.";
            toastNotice = summary;
            updateMessage = summary;
            ScheduleSizeEstimate();
        });
    }

    public Task EnterSetupAsync() => Task.CompletedTask;
    public void ExitSetup() => SetupVisibilityChanged?.Invoke(false);
    public void SelectSetupDrive(string driveRoot) { }
    public Task BrowseSetupFolderAsync(bool existing) => Task.CompletedTask;
    public Task SetSetupTextureAsync(string texture) => Task.CompletedTask;
    public Task TriggerSetupPrimaryActionAsync() => Task.CompletedTask;
    public void CancelSetupWork() { }
    public Task OpenOfficialSetupFallbackAsync() => Task.CompletedTask;

    public async Task LocateInstallAsync()
    {
        if (preview || operations.Busy || player != null) return;
        var picked = await pickFolderAsync("Separate HellForge installation inside the prefix C: drive");
        if (string.IsNullOrWhiteSpace(picked)) return;
        preferences.InstallRoot = picked;
        if (LinuxForgeBootstrap.ExistingRoot(picked) != null) preferences.Save();
        await RunOpAsync(ConnectAsync);
    }

    public void OpenInstallFolder()
    {
        var root = player?.ResolvedInstallRoot ?? preferences.InstallRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        try { Process.Start(new ProcessStartInfo("xdg-open", root) { UseShellExecute = false }); } catch { }
    }

    public async Task OpenManagerAsync()
    {
        if (preview || operations.Busy) return;
        await RunOpAsync(async ct =>
        {
            if (player != null) await player.OpenManagerAsync(ct);
            else await SetupAsync(ct);
        });
    }

    public async Task SyncAccountFromSettingsAsync()
    {
        if (preview || operations.Busy || player == null) return;
        await RunOpAsync(FinishAccountAsync);
    }

    public Task CheckUpdatesAsync() => CheckUpdatesCoreAsync(interactive: true);

    async Task CheckUpdatesCoreAsync(bool interactive)
    {
        if (preview || checkingUpdates || operations.Busy || disposed) return;
        checkingUpdates = true;
        Notify();
        try
        {
            launcherUpdate = await new LauncherUpdates().CheckAsync(lifetime.Token, force: interactive);
            if (disposed) return;
            if (interactive)
            {
                updateDeferred = false;
                updateFailed = false;
                updateMessage = launcherUpdate.Available == null
                    ? (launcherUpdate.CheckSucceeded ? $"Up to date (v{AppInfo.Version})" : "Couldn't check updates right now.")
                    : $"Launcher v{launcherUpdate.Available.Version} is available.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (interactive) { failureDetail = ex.ToString(); updateMessage = "Update check failed."; } }
        finally { checkingUpdates = false; if (!disposed) Notify(); }
    }

    public async Task<ForgeDiagnosticsState> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var st = player?.State?.Status;
        var installRoot = player?.ResolvedInstallRoot ?? preferences.InstallRoot;
        bool hasInstall = !preview && !string.IsNullOrWhiteSpace(installRoot) && Directory.Exists(installRoot);
        var disk = preview
            ? new ForgeDiskAnalysis(75_161_927_680, 10_952_166_604, 891_289_600, 419_430_400, 12_582_912, 87_437_397_196, 891_289_600)
            : hasInstall
                ? await Task.Run(() => ForgeDiskManager.Analyze(installRoot), ct)
                : new ForgeDiskAnalysis(0, 0, 0, 0, 0, 0, 0);

        var matchHash = ComputeSquadMatchHash();
        var triage = lastTriage ?? (hasInstall ? CrashTriage.AnalyzeLastSession(installRoot, st, CurrentConcreteTier()) : null);
        var discordReport = DiscordReportFormatter.Format(new DiscordReportInput(
            AppVersion: AppInfo.Version,
            Platform: "Linux",
            GameBuild: HellForgePack.SupportedBuild,
            SquadMatchCode: matchHash,
            Mode: preferences.Mode,
            EnemySkin: preferences.EnemySkin,
            Covenant: preferences.Covenant,
            DeltaSquad: preferences.DeltaSquad,
            PerformanceTier: preferences.AutoPerformanceTier ? PerformanceTierAdvisor.Auto : preferences.PerformanceTier,
            PackTexture: preferences.Texture,
            Hardware: cachedHardware,
            InstallRoot: installRoot,
            Triage: triage,
            RedactUserPaths: true));

        var health = new List<ForgeHealthItem>
        {
            new("HellForge", st?.Version ?? (player == null ? "Not connected" : "Connected"), Healthy: st != null && st.CompatibilityProblem == null, Warning: st == null),
            new("Game Copy", st?.GameCopyReady == true ? $"Build {st.Build}" : "Not ready", Healthy: st?.GameCopyReady == true, Warning: st == null),
            new("Squad Match", matchHash, Healthy: true),
            new("Account Sync", player?.State?.Account?.Imported == true ? "Imported" : "Not synced", Healthy: player?.State?.Account?.Imported == true, Warning: player?.State?.Account?.Imported != true)
        };

        var diskSummary = ForgeTacticalCopy.FormatDiskUsageSummary(disk, preview || hasInstall);
        var sb = new StringBuilder();
        sb.AppendLine($"Launcher: Clonedivers Forge Linux v{AppInfo.Version}");
        sb.AppendLine($"Squad Match: {matchHash}");
        sb.AppendLine($"Installation: {installRoot ?? "Not connected"}");
        sb.AppendLine($"Disk Usage: {diskSummary}");
        sb.AppendLine($"Mode: {preferences.Mode} · EnemySkin={preferences.EnemySkin} · Delta={preferences.DeltaSquad} · Covenant={preferences.Covenant} · PerfTier={preferences.PerformanceTier} · Texture={preferences.Texture}");
        sb.AppendLine($"Host Rules: Chaos={preferences.HostChaosIntensity} · HeavySeaf={preferences.HostHeavySeaf} · FasterCorpseCleanup={preferences.HostFasterCorpseCleanup}");
        sb.AppendLine($"Diagnostics: {operations.FailureDetail ?? failureDetail ?? operations.Error ?? player?.Error ?? st?.LastFailure ?? st?.LastNotice ?? player?.State?.Detail ?? "none"}");
        if (reportPath != null) sb.AppendLine("Support report saved: " + reportPath);

        bool idleConnected = !preview && !operations.Busy && player != null && player.SessionActive != true;

        return new ForgeDiagnosticsState(
            InstallRoot: installRoot,
            AppVersion: AppInfo.Version,
            AccountSummary: player?.State?.Account?.Message ?? "One-way account import: HD2 → Forge. Forge rewards stay local.",
            HealthChecks: health,
            RawReportText: sb.ToString(),
            DiscordReportText: discordReport,
            DiskUsageSummary: diskSummary,
            CanOpenManager: !preview && !operations.Busy && player?.SessionActive != true,
            CanSyncAccount: idleConnected,
            CanCheckUpdates: !preview && !operations.Busy && !checkingUpdates,
            CanOpenSetup: false,
            CanCleanCache: !preview && !operations.Busy && hasInstall && player?.SessionActive != true,
            CanVerifyAndRepair: idleConnected,
            IsCheckingUpdates: checkingUpdates,
            UpdateMessage: updateMessage,
            SoundEffectsEnabled: preferences.SoundEffectsEnabled,
            DiscordRpcEnabled: preferences.DiscordRpcEnabled,
            MinimizeToTray: preferences.MinimizeToTray);
    }

    public Task<string?> SaveSupportReportAsync()
    {
        if (preview) return Task.FromResult<string?>(null);
        var output = LinuxSupport.SaveReport(null, new Settings(), forge: new LinuxSupport.ForgeReport(
            preferences.InstallRoot, preferences.Mode, player?.State,
            operations.FailureDetail ?? failureDetail ?? operations.Error ?? player?.Error));
        reportPath = output;
        Notify();
        return Task.FromResult<string?>(output);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        await discordRpc.DisposeAsync();
        if (player != null) await player.DisposeAsync();
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
