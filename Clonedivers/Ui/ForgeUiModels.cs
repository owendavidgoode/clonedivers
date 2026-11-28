namespace Clonedivers.Ui;

public enum ForgeHeroTone { Neutral, Ready, Action, Warning, Danger, Running }

public sealed record ForgeUpdateBannerState(
    string Version,
    string Message,
    string ActionLabel,
    bool IsFailed = false,
    bool IsRestartReady = false);

public sealed record ForgeStepDownBannerState(
    string Reason,
    string NextTierId,
    string NextTierDisplayName,
    string ActionLabel);

public sealed record ForgeSelfInstallBannerState(
    string Message,
    string ActionLabel);

public sealed record ForgeHomeState(
    string PlatformTag,
    string AppVersion,
    string Mode,
    string EnemySkin,
    bool DeltaSquad,
    bool Covenant,
    string Texture,
    bool AutoPerformanceTier,
    string PerformanceTier,
    HardwarePerformanceRecommendation Hardware,
    string HostChaosIntensity,
    string HostHeavySeaf,
    bool HostFasterCorpseCleanup,
    bool SoundEffectsEnabled,
    string SquadMatchHash,
    string HeaderBadgeText,
    ForgeHeroTone HeaderBadgeTone,
    string Headline,
    string Detail,
    string PrimaryActionLabel,
    bool PrimaryActionEnabled,
    ForgeHeroTone PrimaryActionTone,
    string? SecondaryActionLabel,
    bool ShowLocateMissing,
    bool IsBusy,
    bool IsSessionRunning,
    bool ControlsLocked,
    bool HasInstallation,
    string? InstallRoot,
    bool AccountImported,
    bool IsModeAppliedOnDisk,
    OperationProgress? Progress,
    ForgeUpdateBannerState? UpdateBanner,
    PackSizePreview SizePreview,
    string? ToastNotice = null,
    ForgeStepDownBannerState? StepDownBanner = null,
    ForgeSelfInstallBannerState? SelfInstallBanner = null,
    bool MinimizeToTray = false,
    string SquidSkin = "mixed-covenant",
    string HostTheaterPreset = "clone-wars",
    bool PlayWithoutAccountSync = false,
    string GalacticWar = "hellforge");

public sealed record ForgeSetupState(
    int Step, // 1..4
    bool SteamReady,
    bool HasInstallation,
    bool ForgeUpgradeAvailable,
    bool GameCopyReady,
    bool PackReady,
    bool IsBusy,
    bool CanCancel,
    string InstallFolder,
    IReadOnlyList<LocalDriveOption> Drives,
    string? SelectedDriveRoot,
    string SpaceSummary,
    bool SpaceOk,
    AccountSyncSnapshot AccountSync,
    string SelectedModeSummary,
    string SelectedTexture,
    long PendingDownloadBytes,
    int PendingCachedFiles,
    string Headline,
    string Detail,
    string PrimaryActionLabel,
    bool PrimaryActionEnabled,
    bool ShowOfficialFallback,
    OperationProgress? Progress,
    string[]? QrRows,
    bool CanSkipAccountSync = false);

public sealed record ForgeHealthItem(string Name, string Status, bool Healthy, bool Warning = false);

public sealed record ForgeDiagnosticsState(
    string? InstallRoot,
    string AppVersion,
    string AccountSummary,
    IReadOnlyList<ForgeHealthItem> HealthChecks,
    string RawReportText,
    string DiscordReportText,
    string DiskUsageSummary,
    bool CanOpenManager,
    bool CanSyncAccount,
    bool CanCheckUpdates,
    bool CanOpenSetup,
    bool CanCleanCache,
    bool CanVerifyAndRepair,
    bool IsCheckingUpdates,
    string? UpdateMessage,
    bool SoundEffectsEnabled,
    bool DiscordRpcEnabled,
    bool MinimizeToTray);

public sealed record ForgeConfirmRequest(
    string Title,
    string Body,
    string ConfirmLabel = "Continue",
    string CancelLabel = "Cancel",
    bool IsDanger = false);

/// <summary>Platform bridge connecting the shared Avalonia shell to Windows or Linux HellForge engines.</summary>
public interface IForgeUiHost : IAsyncDisposable
{
    event Action? StateChanged;
    event Action<bool>? SetupVisibilityChanged;

    bool IsPreview { get; }
    bool SupportsEmbeddedSetup { get; }
    bool SupportsSelfUpdate { get; }

    ForgeHomeState GetHomeState();
    ForgeSetupState GetSetupState();
    Task<ForgeDiagnosticsState> GetDiagnosticsAsync(CancellationToken ct = default);

    void AttachUiCallbacks(
        Func<ForgeConfirmRequest, Task<bool>> confirmAsync,
        Func<string, Task<string?>> pickFolderAsync,
        Func<string, string, Task<string?>> saveFileAsync,
        Action<OperationProgress?>? taskbarProgress = null,
        Action? requestAttention = null);

    Task InitializeAsync();
    bool CanClose(out string? reason);

    // Home & Customize actions
    Task SelectModeAsync(string mode);
    Task UpdateCustomizationAsync(string enemySkin, bool deltaSquad, bool covenant, string texture, string? squidSkin = null);
    Task SetPerformanceTierAsync(string tierId);
    Task UpdateHostMutatorsAsync(string chaosIntensity, string heavySeaf, bool fasterCorpseCleanup, string? theaterPreset = null);
    Task SetGalacticWarAsync(string warMode);
    Task ApplyCustomizationNowAsync();
    Task TriggerPrimaryActionAsync();
    Task TriggerSecondaryActionAsync();
    Task TriggerUpdateBannerActionAsync();
    void DismissUpdateBanner();
    Task AcceptStepDownAsync();
    void DismissStepDownBanner();
    Task InstallSelfShortcutsAsync();
    void DismissSelfInstallBanner();
    Task<WhatsNewResult> FetchWhatsNewAsync(string? versionOrTag, CancellationToken ct = default);
    void ToggleSoundEffects();
    void OpenSteamLibrary();

    // Embedded guided setup actions (Windows; optional on Linux)
    Task EnterSetupAsync();
    void ExitSetup();
    void SelectSetupDrive(string driveRoot);
    Task BrowseSetupFolderAsync(bool existing);
    Task SetSetupTextureAsync(string texture);
    Task TriggerSetupPrimaryActionAsync();
    Task SkipSetupAccountSyncAsync();
    void CancelSetupWork();
    Task OpenOfficialSetupFallbackAsync();

    // Settings / Diagnostics actions
    Task LocateInstallAsync();
    void OpenInstallFolder();
    Task OpenManagerAsync();
    Task SyncAccountFromSettingsAsync();
    Task CheckUpdatesAsync();
    Task CleanCacheAsync();
    Task VerifyAndRepairAsync();
    void SetDiscordRpcEnabled(bool enabled);
    void SetMinimizeToTray(bool enabled);
    Task<string?> SaveSupportReportAsync();
}
