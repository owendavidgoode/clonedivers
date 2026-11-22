using System.Text.Json;

namespace Clonedivers;

internal interface IForgeConnection : IAsyncDisposable
{
    bool NativePaths => false;
    bool Faulted => false; // an unknown outcome closed the stream for good; only a fresh connection can continue
    Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default);
}
internal sealed class LinuxForgePreferences
{
    string enemySkin = "mixed-droids";
    public int Format { get; set; } = 1;
    public string? InstallRoot { get; set; }
    public string Mode { get; set; } = "clonedivers";
    public bool DeltaSquad { get; set; } = true;
    public string Texture { get; set; } = "full";
    public bool Droids
    {
        get => enemySkin != "native-automatons";
        set => enemySkin = value ? (enemySkin == "native-automatons" ? "all-droids" : enemySkin) : "native-automatons";
    }
    public string EnemySkin
    {
        get => enemySkin;
        set => enemySkin = value;
    }
    public bool Covenant { get; set; } = true;
    public bool AutoPerformanceTier { get; set; } = true;
    public string PerformanceTier { get; set; } = PerformanceTierAdvisor.Auto;
    public bool SoundEffectsEnabled { get; set; } = true;
    public bool DiscordRpcEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public string? LastStepDownSuggestedTier { get; set; }
    public string? PendingStepDownReason { get; set; }
    public string HostChaosIntensity { get; set; } = "standard";
    public string HostHeavySeaf { get; set; } = "standard";
    public bool HostFasterCorpseCleanup { get; set; } = true;
    public string? PlayerLabel { get; set; }
    internal static string PathName => Path.Combine(PlatformPaths.ConfigDirectory, "forge.json");

    internal void ApplyPerformanceTier(string tierId, HardwarePerformanceRecommendation? hardware = null)
    {
        if (!PerformanceTierAdvisor.IsValidPreferenceTier(tierId))
            throw new ArgumentException($"Unsupported performance tier: {tierId}", nameof(tierId));
        PerformanceTier = tierId;
        AutoPerformanceTier = tierId == PerformanceTierAdvisor.Auto;
        Texture = PerformanceTierAdvisor.ResolvePackTexture(tierId, hardware);
    }

    internal HostMutatorOptions ToHostMutatorOptions(DateTimeOffset? updatedUtc = null) => new()
    {
        Format = 1,
        ChaosIntensity = HostChaosIntensity,
        HeavySeaf = HostHeavySeaf,
        FasterCorpseCleanup = HostFasterCorpseCleanup,
        UpdatedUtc = updatedUtc ?? DateTimeOffset.UtcNow
    };

    static bool TryGetJsonProperty(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    internal void Validate()
    {
        if (string.IsNullOrEmpty(PerformanceTier))
            PerformanceTier = AutoPerformanceTier ? PerformanceTierAdvisor.Auto : PerformanceTierAdvisor.TierFromPackTexture(Texture);
        if (string.IsNullOrEmpty(HostChaosIntensity)) HostChaosIntensity = "standard";
        if (string.IsNullOrEmpty(HostHeavySeaf)) HostHeavySeaf = "standard";
        if (string.IsNullOrWhiteSpace(LastStepDownSuggestedTier)) LastStepDownSuggestedTier = null;
        if (string.IsNullOrWhiteSpace(PendingStepDownReason)) PendingStepDownReason = null;
        if (string.IsNullOrWhiteSpace(PlayerLabel)) PlayerLabel = null;
        else PlayerLabel = PlayerLabel.Trim();

        if (Format != 1 || Mode is not ("regular" or "clonedivers" or "empire") || Texture is not ("full" or "lighter" or "reduced512") ||
            EnemySkin is not ("all-droids" or "mixed-droids" or "native-automatons") ||
            !PerformanceTierAdvisor.IsValidPreferenceTier(PerformanceTier) ||
            (LastStepDownSuggestedTier != null && !PerformanceTierAdvisor.IsConcreteTier(LastStepDownSuggestedTier)) ||
            (PendingStepDownReason != null && PendingStepDownReason.Length > 512) ||
            !HostMutatorConfig.IsValidChaosIntensity(HostChaosIntensity) ||
            !HostMutatorConfig.IsValidHeavySeaf(HostHeavySeaf) ||
            (PlayerLabel != null && PlayerLabel.Length > 40))
            throw new InvalidDataException("Unsupported Forge preferences; restore forge.json from backup.");
    }
    internal static LinuxForgePreferences Load(string? file = null)
    {
        file ??= PathName; FileSafety.RejectLink(file);
        if (!File.Exists(file))
        {
            if (!LegacyAppearance.HasSavedConfigFor(file))
                return new();
            var legacy = LegacyAppearance.ReadFor(file);
            var hasExplicitTexture = !string.IsNullOrWhiteSpace(legacy.TextureProfile);
            return new()
            {
                Mode = legacy.Options.GetValueOrDefault("empire") ? "empire" : "clonedivers",
                DeltaSquad = legacy.Options.GetValueOrDefault("commandos", true),
                EnemySkin = legacy.Options.TryGetValue("droids", out var legacyDroids) ? (legacyDroids ? "all-droids" : "native-automatons") : "mixed-droids",
                Covenant = legacy.Options.TryGetValue("covenant", out var legacyCov) ? legacyCov : true,
                Texture = legacy.TextureProfile == "lighter" ? "lighter" : "full",
                AutoPerformanceTier = !hasExplicitTexture,
                PerformanceTier = !hasExplicitTexture ? PerformanceTierAdvisor.Auto : (legacy.TextureProfile == "lighter" ? PerformanceTierAdvisor.Balanced : PerformanceTierAdvisor.Ultra)
            };
        }
        if (new FileInfo(file).Length > 65536) throw new InvalidDataException("Forge preferences are oversized.");
        var raw = File.ReadAllText(file);
        using var doc = JsonDocument.Parse(raw);
        var value = JsonSerializer.Deserialize<LinuxForgePreferences>(raw, HellForgePack.Json) ?? throw new InvalidDataException("Empty Forge preferences.");
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetJsonProperty(doc.RootElement, "EnemySkin", out _))
            {
                value.EnemySkin = TryGetJsonProperty(doc.RootElement, "Droids", out var droidsProp) && droidsProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (droidsProp.GetBoolean() ? "all-droids" : "native-automatons")
                    : "all-droids";
            }
            if (!TryGetJsonProperty(doc.RootElement, "Covenant", out _))
                value.Covenant = false;
            if (!TryGetJsonProperty(doc.RootElement, "AutoPerformanceTier", out _))
            {
                value.AutoPerformanceTier = false;
                if (!TryGetJsonProperty(doc.RootElement, "PerformanceTier", out _))
                    value.PerformanceTier = PerformanceTierAdvisor.TierFromPackTexture(value.Texture);
            }
        }
        value.Validate(); return value;
    }
    internal void Save(string? file = null)
    {
        Validate(); file ??= PathName;
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        FileSafety.RejectLink(folder); FileSafety.RejectLink(file); FileSafety.RejectLink(file + ".tmp");
        Directory.CreateDirectory(folder);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, HellForgePack.Json));
        File.Move(file + ".tmp", file, true);
    }
    internal string[] SelectionCommand() => ["select-mode", "--mode", Mode, "--delta", DeltaSquad ? "yes" : "no", "--texture", Texture,
        "--droids", Droids ? "yes" : "no", "--covenant", Covenant ? "yes" : "no", "--enemy-skin", EnemySkin];
    internal HellForgePack.Selection PackSelection() => HellForgePack.PinnedSelection(Mode == "empire" ? "empire" : DeltaSquad ? "commandos" : "clonedivers", Texture,
        new Dictionary<string, bool> { ["droids"] = Droids, ["covenant"] = Covenant, ["mixed-droids"] = EnemySkin == "mixed-droids" });
}
internal sealed record ForgeAccountView(bool Captured, bool Imported, string Message)
{
    internal bool NeedsFreshCapture => !Captured && !Imported && Message.Contains("fresh HD2 capture", StringComparison.OrdinalIgnoreCase);
}
internal sealed record ForgePlayerState(HellForgeStatus Status, bool Ready, string Detail, ForgeAccountView? Account, HellForgeMods? Mods);

internal sealed class LinuxForgePlayer(IForgeConnection connection, LinuxForgePreferences preferences, string? resolvedInstallRoot = null) : IAsyncDisposable
{
    internal const string PastaGuidance = "pasta (the passt package) requires Ubuntu 24.04 or newer, Debian 13 or newer, or a distribution providing passt. This launcher requires it for both solo and co-op.";
    internal LinuxForgePreferences Preferences { get; } = preferences;
    internal string? ResolvedInstallRoot { get; } = resolvedInstallRoot ?? preferences.InstallRoot;
    internal ForgePlayerState? State { get; private set; }
    internal bool OwnsSession { get; private set; }
    internal bool RequestedAccountSync { get; private set; }
    internal sealed record SyncRequest(bool SawAttempt);
    SyncRequest? syncRequest;
    internal SyncRequest? PendingAccountSync => RequestedAccountSync ? syncRequest : null;
    internal void ResumeAccountSync(SyncRequest? request)
    { syncRequest = request; RequestedAccountSync = request != null; }
    internal bool NeedsAccountImport => RequestedAccountSync && !Disconnected && !SessionActive && !Game.IsRunning() && State?.Account is { Captured: true, Imported: false };
    // A faulted bridge can neither confirm nor end a session; closing stdin hands cleanup to the bridge's EOF path, so it must not pin the window.
    internal bool Disconnected => connection.Faulted;
    internal bool SessionActive => !Disconnected && (OwnsSession || State?.Status.Running == true);
    internal string? Error { get; private set; }
    bool errorFromPoll;
    internal bool CanLaunch => !SessionActive && !Disconnected && Error == null && State?.Ready == true;

    internal static async Task<LinuxForgePlayer> ConnectAsync(LinuxForgePreferences preferences, string resourceDirectory, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Forge sessions require Linux or the Windows launcher; previews never connect.");
        if (Game.IsRunning()) throw new InvalidOperationException("Close the running HD2 session before starting Forge's dedicated namespace.");
        var missing = LinuxForgeBootstrap.MissingTools();
        if (missing.Length != 0) throw new InvalidOperationException(MissingToolsMessage(missing));
        preferences.Validate();
        var root = preferences.InstallRoot ?? throw new InvalidOperationException(
            "No Forge installation selected. Read https://github.com/owendavidgoode/clonedivers/blob/main/docs/FORGE-MIGRATION.md#first-forge-setup\n" +
            "Use Set up / update Forge to obtain the verified native Linux installer.\n" +
            "For a custom existing prefix, use Locate to select the folder containing hellforge-install.json.\n" +
            "Saved selection: " + LinuxForgePreferences.PathName);
        var metadata = Path.Combine(resourceDirectory, "companion.json"); FileSafety.RejectLink(resourceDirectory); FileSafety.RejectLink(metadata);
        if (!File.Exists(metadata) || new FileInfo(metadata).Length > 65536) throw new FileNotFoundException("Forge companion resources are missing. Install the complete Clonedivers package, including its forge folder.");
        using var doc = JsonDocument.Parse(File.ReadAllText(metadata));
        if (doc.RootElement.GetProperty("format").GetInt32() != 1 || doc.RootElement.GetProperty("gameBuild").GetString() != LinuxForgePlan.GameBuild ||
            doc.RootElement.GetProperty("bridgeVersion").GetString() != LinuxForgePlan.BridgeVersion)
            throw new InvalidDataException("Forge companion targets a different client or game build. Install the complete matching Clonedivers package.");
        if (doc.RootElement.GetProperty("control").GetString() != "native-linux") throw new InvalidDataException("Install the complete native Forge companion package.");
        var plan = LinuxForgePlan.StageNative(root, Path.Combine(resourceDirectory, LinuxForgePlan.NativeBridgeName), doc.RootElement.GetProperty("bridgeSha256").GetString()!);
        return await ConnectWithPlanAsync(preferences, plan, async (p, token) => await LinuxForgeTransport.StartAsync(p, token), ct);
    }
    internal static async Task<LinuxForgePlayer> ConnectWithPlanAsync(LinuxForgePreferences preferences, LinuxForgePlan plan,
        Func<LinuxForgePlan, CancellationToken, Task<IForgeConnection>> startConnection, CancellationToken ct = default)
    {
        var transport = await startConnection(plan, ct);
        var player = new LinuxForgePlayer(transport, preferences, plan.Install);
        try { await player.ApplySelectionAsync(ct); return player; }
        catch { await player.DisposeAsync(); throw; }
    }
    internal static string MissingToolsMessage(IReadOnlyList<string> missing) => "Linux components needed: " + string.Join(", ", missing) +
        ". Use Set up / update Forge to install them with desktop approval." + (missing.Contains("pasta") ? " " + PastaGuidance : "");
    internal async Task RefreshAsync(CancellationToken ct = default, bool preserveError = false, bool allowImport = true)
    {
        var previousError = Error;
        var keepForegroundError = preserveError && previousError != null && !errorFromPoll;
        try
        {
            var json = await connection.RunAsync(["player-status"], ct);
            State = json.Deserialize<ForgePlayerState>(HellForgePack.Json) ?? throw new InvalidDataException("Missing player status.");
            if (!State.Status.Running) OwnsSession = false;
            if (!keepForegroundError) { Error = null; errorFromPoll = false; }
            var gameRunning = RequestedAccountSync && Game.IsRunning();
            if (RequestedAccountSync && syncRequest is { } request &&
                (SessionActive || gameRunning)) syncRequest = request with { SawAttempt = true };
            if (RequestedAccountSync && !SessionActive && !gameRunning)
            {
                if (State.Account is { Imported: true } || State.Account is { NeedsFreshCapture: true } && syncRequest?.SawAttempt == true) ResumeAccountSync(null);
                else if (allowImport && State.Account is { Captured: true, Imported: false })
                { ResumeAccountSync(null); await ImportAsync(ct); }
            }
            if (keepForegroundError && Error == null) { Error = previousError; errorFromPoll = false; }
        }
        catch (Exception error)
        {
            if (!preserveError || Error == null || errorFromPoll) { Error = error.Message; errorFromPoll = preserveError; }
            throw;
        }
    }
    internal async Task ApplySelectionAsync(CancellationToken ct = default)
    {
        Preferences.Validate();
        if (SessionActive) throw new InvalidOperationException("End the current Forge session before changing its selection.");
        State = null;
        try { await SelectAsync(ct); await RefreshAsync(ct); }
        catch (Exception error) { Error = error.Message; errorFromPoll = false; throw; }
    }
    // The bridge keeps its own selection; re-send ours so an earlier refused select-mode can never play or apply a stale mode.
    Task SelectAsync(CancellationToken ct) { Preferences.Validate(); return connection.RunAsync(Preferences.SelectionCommand(), ct); }
    internal async Task LaunchAsync(CancellationToken ct = default)
    {
        try
        {
        await SelectAsync(ct);
        await RefreshAsync(ct);
        if (!CanLaunch) throw new InvalidOperationException(State?.Detail ?? Error ?? "Finish Forge setup before launching.");
        var result = await connection.RunAsync(["play", "--profile", "default"], ct);
        if (result.GetProperty("event").GetString() != "started") throw new InvalidDataException("Forge did not acknowledge starting a session.");
        OwnsSession = true; // keep End session available if the following refresh fails
        await RefreshAsync(ct);
        }
        catch (Exception error) { Error = error.Message; errorFromPoll = false; throw; }
    }
    internal async Task EndSessionAsync(CancellationToken ct = default)
    {
        try
        {
        if (!OwnsSession) throw new InvalidOperationException("End this session using the launcher that started it.");
        await connection.RunAsync(["stop"], ct);
        OwnsSession = false; State = null;
        await RefreshAsync(ct);
        }
        catch (Exception error)
        {
            // A definitive cleanup failure can arrive after the game has stopped. Refresh ownership so closing stays available.
            if (!Disconnected)
                try { await RefreshAsync(ct); } catch { }
            Error = error.Message; errorFromPoll = false; throw;
        }
    }
    internal async Task ImportAsync(CancellationToken ct = default)
    {
        try
        {
        if (SessionActive) throw new InvalidOperationException("End the current session before importing your account.");
        await connection.RunAsync(["account-import"], ct);
        await RefreshAsync(ct);
        }
        catch (Exception error) { Error = error.Message; errorFromPoll = false; throw; }
    }
    internal async Task RecoverAppearanceAsync(CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before recovering the appearance.");
        await connection.RunAsync(["recover-pack"], ct);
        await RefreshAsync(ct);
    }
    internal async Task InstallGameplayAsync(IProgress<string>? progress = null, CancellationToken ct = default, IProgress<OperationProgress>? richProgress = null)
    {
        if (SessionActive || Game.IsRunning()) throw new InvalidOperationException("End HD2 before installing the required gameplay preset.");
        richProgress?.Report(new("Checking required gameplay mods", "Verifying installed 14-mod r7 preset…", IsIndeterminate: true, CanCancel: true));
        var missing = (await connection.RunAsync(["gameplay-status"], ct)).Deserialize<List<GameplayArtifact>>(HellForgePack.Json)
            ?? throw new InvalidDataException("Forge returned no gameplay installation plan.");
        var pinned = HellForgeGameplayCatalog.Pinned;
        foreach (var requested in missing)
            if (!pinned.Artifacts.Contains(requested)) throw new InvalidDataException("Bridge requested an artifact outside the pinned preset.");
        var root = ResolvedInstallRoot ?? throw new InvalidOperationException("Choose the Forge installation first.");
        var cache = Path.Combine(Path.GetDirectoryName(root)!, "Clonedivers-HellForge-cache", "gameplay", pinned.Id);
        var windows = connection.NativePaths ? cache : WindowsPath(root, cache);
        // HTTPS runs here in the native frontend, never in the isolated Wine bridge.
        await HellForgeGameplayCatalog.StageAsync(cache, missing, progress, ct, richProgress: richProgress);
        if (SessionActive || Game.IsRunning()) throw new InvalidOperationException("HD2 started while downloading; keep the verified files and retry after closing it.");
        richProgress?.Report(new("Installing gameplay mods", "Configuring pinned r7 load order and sharing…", IsIndeterminate: true, CanCancel: false));
        await connection.RunAsync(["install-gameplay", "--file", windows], ct);
        await RefreshAsync(ct);
    }
    internal async Task RecoverGameplayAsync(CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the session before recovering gameplay mods.");
        await connection.RunAsync(["recover-gameplay"], ct); await RefreshAsync(ct);
    }
    internal async Task OpenManagerAsync(CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before changing setup or mods.");
        await connection.RunAsync(["open-manager"], ct);
        await RefreshAsync(ct);
    }
    internal async Task BeginAccountSyncAsync(CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the session before syncing your account.");
        var request = new SyncRequest(false);
        await connection.RunAsync(["select-account"], ct);
        await connection.RunAsync(["open-manager"], ct);
        ResumeAccountSync(request);
    }
    internal async Task<HellForgePack.Plan> PlanAppearanceAsync(IProgress<string>? progress = null, CancellationToken ct = default, IProgress<OperationProgress>? richProgress = null)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before preparing this mode.");
        if (Preferences.Mode == "regular") throw new InvalidOperationException("Regular needs no appearance download.");
        var root = ResolvedInstallRoot ?? throw new InvalidOperationException("Choose a Forge installation.");
        var cache = Path.Combine(Path.GetDirectoryName(root)!, "Clonedivers-HellForge-cache", "exports");
        FileSafety.RejectLink(cache);
        var selection = Preferences.PackSelection();
        var exports = Directory.Exists(cache) ? new DirectoryInfo(cache).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName).ToList() : [];
        if (await FindExportAsync(exports, selection, progress, ct, richProgress) is { } prepared) return prepared;
        var mods = Path.Combine(root, "mods");
        FileSafety.RejectLink(mods);
        IEnumerable<string> sources = Directory.Exists(mods) ? exports.Prepend(mods) : exports;
        return await HellForgePack.PlanAsync(selection, Path.Combine(cache, "linux-" + Guid.NewGuid().ToString("N")), sources, progress, ct, richProgress: richProgress);
    }
    /// <summary>Like Windows, reuse a verified earlier export instead of writing another full copy; its plan needs no download or export.</summary>
    internal static async Task<HellForgePack.Plan?> FindExportAsync(IEnumerable<string> exports, HellForgePack.Selection selection, IProgress<string>? progress = null, CancellationToken ct = default, IProgress<OperationProgress>? richProgress = null)
    {
        foreach (var export in exports)
            try
            {
                progress?.Report("Checking previously prepared pack files…");
                var speed = new SpeedTracker();
                var byteProgress = richProgress == null
                    ? null
                    : new SyncProgress<(long Done, long Total)>(p =>
                    {
                        var (rate, eta) = speed.Update(p.Done, p.Total);
                        richProgress.Report(new("Checking cached pack exports", Path.GetFileName(export), 0, 0, p.Done, p.Total, rate, eta, CanCancel: true));
                    });
                await HellForgePack.VerifyAsync(export, selection, byteProgress, ct);
                Directory.SetLastWriteTimeUtc(export, DateTime.UtcNow); // most recent, so pruning keeps it
                return new(selection, export, selection.Files.Where(f => f.Size > 0).DistinctBy(f => f.Sha256)
                    .ToDictionary(f => f.Sha256, f => FileSafety.Child(export, f.Name), StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { }
        return null;
    }
    /// <summary>After an applied import, keep that export and the two newest others as copy sources; older Linux copies are only disk weight.</summary>
    internal static void PruneExports(string cache, string keep, int others = 2)
    {
        keep = Path.TrimEndingDirectorySeparator(Path.GetFullPath(keep));
        foreach (var stale in new DirectoryInfo(cache).EnumerateDirectories("linux-*").Where(d => d.FullName != keep).OrderByDescending(d => d.LastWriteTimeUtc).Skip(others))
            try { FileSafety.RejectLink(stale.FullName); Directory.Delete(stale.FullName, true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { } // pruning never fails an applied pack
    }
    internal async Task PrepareAppearanceAsync(HellForgePack.Plan? plan, bool allowDownloads, IProgress<string>? progress = null, CancellationToken ct = default, IProgress<OperationProgress>? richProgress = null)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before applying this mode.");
        if (Preferences.Mode == "regular")
        {
            richProgress?.Report(new("Applying Regular mode", "Disabling Star Wars packs while keeping gameplay mods…", IsIndeterminate: true, CanCancel: false));
            await SelectAsync(ct);
            await connection.RunAsync(["prepare-regular"], ct);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(plan);
            var expected = Preferences.PackSelection();
            if (plan.Selection.Mode != expected.Mode || plan.Selection.Texture != expected.Texture || plan.Selection.ManifestSha256 != expected.ManifestSha256 ||
                plan.Selection.Options.Count != expected.Options.Count || expected.Options.Any(pair => !plan.Selection.Options.TryGetValue(pair.Key, out var on) || on != pair.Value))
                throw new InvalidDataException("The selected mode/options changed after planning. Prepare the selection again.");
            var path = connection.NativePaths ? plan.Output : WindowsPath(ResolvedInstallRoot!, plan.Output);
            // This code runs in the native frontend, outside the isolated Wine namespace.
            if (Directory.Exists(plan.Output))
            {
                richProgress?.Report(new("Verifying cached pack", "Checking prepared export metadata…", IsIndeterminate: true, CanCancel: true));
                await HellForgePack.VerifyMetadataAsync(plan.Output, expected, ct); // reused export; the bridge re-hashes before import
            }
            else
            {
                await HellForgePack.ExportAsync(plan, allowDownloads, progress, ct, richProgress: richProgress);
            }
            await SelectAsync(ct);
            richProgress?.Report(new("Installing Star Wars pack", "Importing verified pack into HellForge…", IsIndeterminate: true, CanCancel: false));
            await connection.RunAsync(["prepare-pack", "--file", path], ct);
            PruneExports(Path.GetDirectoryName(Path.GetFullPath(plan.Output))!, plan.Output);
        }
        await RefreshAsync(ct);
    }
    internal static string WindowsPath(string install, string file)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(install)); directory != null; directory = directory.Parent)
            if (directory.Name == "drive_c" && directory.Parent?.Name == "pfx")
            {
                var relative = Path.GetRelativePath(directory.FullName, Path.GetFullPath(file));
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    throw new InvalidDataException("Prepared assets must be inside this prefix's C: drive.");
                return "C:\\" + relative.Replace(Path.DirectorySeparatorChar, '\\');
            }
        throw new InvalidDataException("Dedicated prefix C: drive not found.");
    }
    public async ValueTask DisposeAsync()
    {
        if (Disconnected) { try { await connection.DisposeAsync(); } catch (IOException) { } return; } // fault already reported; an abnormal wrapper exit is expected
        if (OwnsSession) await EndSessionAsync();
        await connection.DisposeAsync();
    }
}
