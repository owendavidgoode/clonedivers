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

    LinuxForgePlayer? player;
    ForgeAction primaryAction;
    ForgeAction retryAction = ForgeAction.Setup;
    bool requestedSetup;
    bool checkingUpdates;
    bool updateDeferred;
    bool updateFailed;
    bool disposed;
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

    public event Action? StateChanged;
    public event Action<bool>? SetupVisibilityChanged;

    public bool IsPreview => preview;
    public bool SupportsEmbeddedSetup => false;
    public bool SupportsSelfUpdate => false;
    internal string? RenderOutput { get; }
    internal string PreviewName => previewState + "-" + preferences.Mode;
    internal double PreviewWidth { get; } = 900;
    internal double PreviewHeight { get; } = 680;

    internal LinuxForgeHost(string[] args)
    {
        preview = args.Contains("--forge-preview", StringComparer.Ordinal);
        preferences = preview ? new() : LinuxForgePreferences.Load();
        resources = Path.Combine(AppContext.BaseDirectory, "forge");

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
            preferences.Mode == "regular" ? "No pack download needed" : (preview ? "Ready — cached locally" : "Estimating size…"),
            preferences.Mode == "regular" ? "Original HD2 appearance with required Forge gameplay mods." : "Checking local cache…");

        timer.Tick += async (_, _) =>
        {
            if (!disposed && !preview && player is { Disconnected: false })
            {
                await operations.PollAsync(
                    () => RefreshPlayerAsync(background: true),
                    () => !disposed && ForgeWindowFlow.NeedsForegroundRefresh(requestedSetup, player),
                    action => _ = RunOpAsync(action),
                    () => RefreshPlayerAsync(background: false),
                    Notify);
            }
        };
    }

    static string NextValue(string[] args, ref int i) => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
    void Notify() => StateChanged?.Invoke();

    public void AttachUiCallbacks(
        Func<ForgeConfirmRequest, Task<bool>> confirmAsync,
        Func<string, Task<string?>> pickFolderAsync,
        Func<string, string, Task<string?>> saveFileAsync,
        Action<OperationProgress?>? taskbarProgress = null,
        Action? requestAttention = null)
    {
        this.confirmAsync = confirmAsync;
        this.pickFolderAsync = pickFolderAsync;
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
                : new PackSizePreview(false, 10_920_000_000, 10_920_000_000, 0, 378, 378, 0, true, "Ready — cached locally", "All 378 files found locally; switching needs no download.");
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
                string? cache = null;
                if (!string.IsNullOrWhiteSpace(root))
                {
                    try { cache = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))!, "Clonedivers-HellForge-cache"); }
                    catch { }
                }
                var folders = PackSizeEstimator.CollectSearchFolders(root, cache, GameLocator.AutoDetect());
                est = PackSizeEstimator.Estimate(isRegular, selection, folders, player?.CanLaunch == true);
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

    async Task RunOpAsync(Func<Task> action)
    {
        if (operations.Busy || preview) return;
        failureDetail = null;
        await operations.RunAsync(action, Notify);
        activeProgress = null;
        Notify();
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

        // Keep update banner non-blocking instead of hijacking the primary CTA.
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
        var ctaTone = view.Action switch
        {
            ForgeAction.Launch => ForgeHeroTone.Ready,
            ForgeAction.EndSession => ForgeHeroTone.Running,
            ForgeAction.None => ForgeHeroTone.Neutral,
            _ => ForgeHeroTone.Action
        };
        var badgeText = working ? "Working…"
            : active ? "Session Active"
            : lost ? "Disconnected"
            : view.Action == ForgeAction.Launch ? "Forge 0.4.0 · Ready"
            : view.Action == ForgeAction.Setup ? "Setup Needed"
            : "Action Needed";
        var badgeTone = working || active ? ForgeHeroTone.Running
            : lost || operations.Error != null ? ForgeHeroTone.Danger
            : view.Action == ForgeAction.Launch ? ForgeHeroTone.Ready
            : ForgeHeroTone.Warning;

        var ctaLabel = view.Action == ForgeAction.Launch
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

        var prog = activeProgress ?? (working
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
            HeaderBadgeText: badgeText,
            HeaderBadgeTone: badgeTone,
            Headline: view.Headline,
            Detail: view.Detail,
            PrimaryActionLabel: ctaLabel,
            PrimaryActionEnabled: !working && view.Action != ForgeAction.None,
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
            ToastNotice: toastNotice);
    }

    public ForgeSetupState GetSetupState() => throw new NotSupportedException("Linux uses native official setup.");

    public async Task SelectModeAsync(string mode)
    {
        if (preview || operations.Busy || player?.SessionActive == true) return;
        await RunOpAsync(async () =>
        {
            preferences.Mode = mode;
            preferences.Save();
            if (player != null) await player.ApplySelectionAsync(lifetime.Token);
            ScheduleSizeEstimate();
        });
    }

    public async Task UpdateCustomizationAsync(string enemySkin, bool deltaSquad, bool covenant, string texture)
    {
        if (preview || operations.Busy || player?.SessionActive == true) return;
        await RunOpAsync(async () =>
        {
            preferences.EnemySkin = enemySkin;
            preferences.DeltaSquad = deltaSquad;
            preferences.Covenant = covenant;
            preferences.Texture = texture;
            preferences.Save();
            if (player != null) await player.ApplySelectionAsync(lifetime.Token);
            ScheduleSizeEstimate();
        });
    }

    public async Task TriggerPrimaryActionAsync()
    {
        if (preview || operations.Busy) return;
        var action = primaryAction;
        retryAction = action is ForgeAction.Setup or ForgeAction.Connect ? action : ForgeAction.Refresh;
        await RunOpAsync(async () =>
        {
            switch (action)
            {
                case ForgeAction.Setup: await SetupAsync(); break;
                case ForgeAction.Connect: await ConnectAsync(); break;
                case ForgeAction.EndSession: await player!.EndSessionAsync(lifetime.Token); break;
                case ForgeAction.Launch: await player!.LaunchAsync(lifetime.Token); break;
                case ForgeAction.Manager: await player!.OpenManagerAsync(lifetime.Token); break;
                case ForgeAction.Prepare: await PrepareAsync(); break;
                case ForgeAction.Sync: await FinishAccountAsync(); break;
                case ForgeAction.Refresh:
                    if (player == null) await ConnectAsync();
                    else await RefreshPlayerAsync(background: false);
                    break;
            }
        });
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

    async Task ConnectAsync()
    {
        if (player is { Disconnected: true }) { var lost = player; player = null; await syncHandoff.ReleaseAsync(lost); }
        if (player != null) throw new InvalidOperationException("Close this launcher before switching its active Forge installation.");
        activeProgress = new OperationProgress("Connecting to HellForge", "Starting isolated Linux namespace bridge…", IsIndeterminate: true);
        Notify();
        await syncHandoff.ConnectAsync(() => LinuxForgePlayer.ConnectAsync(preferences, resources, lifetime.Token), p => player = p, lifetime.Token);
        ScheduleSizeEstimate();
    }

    async Task SetupAsync()
    {
        if (player?.SessionActive == true) throw new InvalidOperationException("End the Forge session before setup.");
        if (player != null) { var existing = player; player = null; await syncHandoff.ReleaseAsync(existing); }
        var throttler = new ProgressThrottler(p => { activeProgress = p; Notify(); });
        var result = await LinuxForgeBootstrap.InstallAsync(preferences.InstallRoot, null, throttler.AsProgress(), lifetime.Token);
        preferences.InstallRoot = result;
        preferences.Save();
        await ConnectAsync();
        requestedSetup = true;
        if (ForgeWindowFlow.ShouldPrepareAfterSetup(requestedSetup, player)) { requestedSetup = false; await PrepareAsync(); }
    }

    async Task RefreshPlayerAsync(bool background)
    {
        if (player == null) return;
        await player.RefreshAsync(lifetime.Token, preserveError: background, allowImport: !background);
        if (!background && ForgeWindowFlow.ShouldPrepareAfterSetup(requestedSetup, player))
        {
            requestedSetup = false;
            await PrepareAsync();
        }
    }

    async Task FinishAccountAsync()
    {
        if (player == null) return;
        if (player.State?.Account is { Captured: true, Imported: false })
        {
            activeProgress = new OperationProgress("Importing HD2 account", "Merging sealed capture into Forge…", IsIndeterminate: true);
            Notify();
            await player.ImportAsync(lifetime.Token);
        }
        else if (player.State?.Account?.Imported != true)
        {
            await player.BeginAccountSyncAsync(lifetime.Token);
            toastNotice = ForgePresentation.SyncInstructions;
        }
    }

    async Task PrepareAsync()
    {
        if (player == null) return;
        if (player.State?.Status.RecoveryProblem != null)
        {
            activeProgress = new OperationProgress("Recovering previous setup", null, IsIndeterminate: true);
            Notify();
            if (player.State.Status.RecoveryProblem.StartsWith("Gameplay", StringComparison.Ordinal))
                await player.RecoverGameplayAsync(lifetime.Token);
            else
                await player.RecoverAppearanceAsync(lifetime.Token);
            return;
        }
        var progress = new Progress<string>(text => { activeProgress = new OperationProgress("Preparing loadout", text, IsIndeterminate: true); Notify(); });
        await player.InstallGameplayAsync(progress, lifetime.Token);
        if (preferences.Mode == "regular")
        {
            await player.PrepareAppearanceAsync(null, false, ct: lifetime.Token);
            await FinishAccountAsync();
            return;
        }
        var plan = await Task.Run(() => player.PlanAppearanceAsync(progress, lifetime.Token));
        if (plan.Missing.Count > 0)
        {
            var bytes = plan.Missing.Sum(f => f.Size);
            var ok = await confirmAsync(new ForgeConfirmRequest(
                "Download Star Wars Pack?",
                $"Download {Pack.FormatBytes(bytes)} of verified appearance assets? Existing local files are reused.",
                $"Download ({Pack.FormatBytes(bytes)})",
                "Cancel"));
            if (!ok) return;
        }
        await Task.Run(() => player.PrepareAppearanceAsync(plan, true, progress, lifetime.Token));
        await FinishAccountAsync();
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
        await RunOpAsync(async () =>
        {
            if (player != null) await player.OpenManagerAsync(lifetime.Token);
            else await SetupAsync();
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

    public Task<ForgeDiagnosticsState> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var st = player?.State?.Status;
        var health = new List<ForgeHealthItem>
        {
            new("HellForge", st?.Version ?? (player == null ? "Not connected" : "Connected"), Healthy: st != null && st.CompatibilityProblem == null, Warning: st == null),
            new("Game Copy", st?.GameCopyReady == true ? $"Build {st.Build}" : "Not ready", Healthy: st?.GameCopyReady == true, Warning: st == null),
            new("Account Sync", player?.State?.Account?.Imported == true ? "Imported" : "Not synced", Healthy: player?.State?.Account?.Imported == true, Warning: player?.State?.Account?.Imported != true)
        };
        var sb = new StringBuilder();
        sb.AppendLine($"Launcher: Clonedivers Forge Linux v{AppInfo.Version}");
        sb.AppendLine($"Installation: {player?.ResolvedInstallRoot ?? preferences.InstallRoot ?? "Not connected"}");
        sb.AppendLine($"Mode: {preferences.Mode} · EnemySkin={preferences.EnemySkin} · Delta={preferences.DeltaSquad} · Covenant={preferences.Covenant} · Texture={preferences.Texture}");
        sb.AppendLine($"Diagnostics: {operations.FailureDetail ?? failureDetail ?? operations.Error ?? player?.Error ?? st?.LastFailure ?? st?.LastNotice ?? player?.State?.Detail ?? "none"}");
        if (reportPath != null) sb.AppendLine("Support report saved: " + reportPath);

        return Task.FromResult(new ForgeDiagnosticsState(
            InstallRoot: player?.ResolvedInstallRoot ?? preferences.InstallRoot,
            AppVersion: AppInfo.Version,
            AccountSummary: player?.State?.Account?.Message ?? "One-way account import: HD2 → Forge. Forge rewards stay local.",
            HealthChecks: health,
            RawReportText: sb.ToString(),
            CanOpenManager: !preview && !operations.Busy && player?.SessionActive != true,
            CanSyncAccount: !preview && !operations.Busy && player != null && player.SessionActive != true,
            CanCheckUpdates: !preview && !operations.Busy && !checkingUpdates,
            CanOpenSetup: false,
            IsCheckingUpdates: checkingUpdates,
            UpdateMessage: updateMessage));
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
        if (player != null) await player.DisposeAsync();
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
