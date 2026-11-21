namespace Clonedivers.Ui;

public enum ForgeHeroTone { Neutral, Ready, Action, Warning, Danger, Running }

public sealed record ForgeUpdateBannerState(
    string Version,
    string Message,
    string ActionLabel,
    bool IsFailed = false,
    bool IsRestartReady = false);

public sealed record ForgeHomeState(
    string PlatformTag,
    string AppVersion,
    string Mode,
    string EnemySkin,
    bool DeltaSquad,
    bool Covenant,
    string Texture,
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
    string? ToastNotice = null);

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
    string[]? QrRows);

public sealed record ForgeHealthItem(string Name, string Status, bool Healthy, bool Warning = false);

public sealed record ForgeDiagnosticsState(
    string? InstallRoot,
    string AppVersion,
    string AccountSummary,
    IReadOnlyList<ForgeHealthItem> HealthChecks,
    string RawReportText,
    bool CanOpenManager,
    bool CanSyncAccount,
    bool CanCheckUpdates,
    bool CanOpenSetup,
    bool IsCheckingUpdates,
    string? UpdateMessage);

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

    // Home actions
    Task SelectModeAsync(string mode);
    Task UpdateCustomizationAsync(string enemySkin, bool deltaSquad, bool covenant, string texture);
    Task TriggerPrimaryActionAsync();
    Task TriggerSecondaryActionAsync();
    Task TriggerUpdateBannerActionAsync();
    void DismissUpdateBanner();
    void OpenSteamLibrary();

    // Embedded guided setup actions (Windows; optional on Linux)
    Task EnterSetupAsync();
    void ExitSetup();
    void SelectSetupDrive(string driveRoot);
    Task BrowseSetupFolderAsync(bool existing);
    Task SetSetupTextureAsync(string texture);
    Task TriggerSetupPrimaryActionAsync();
    void CancelSetupWork();
    Task OpenOfficialSetupFallbackAsync();

    // Settings / Diagnostics actions
    Task LocateInstallAsync();
    void OpenInstallFolder();
    Task OpenManagerAsync();
    Task SyncAccountFromSettingsAsync();
    Task CheckUpdatesAsync();
    Task<string?> SaveSupportReportAsync();
}
