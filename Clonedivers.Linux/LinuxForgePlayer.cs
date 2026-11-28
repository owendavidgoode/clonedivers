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
    string squidSkin = "mixed-covenant";
    bool squidSkinExplicit;
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
    public bool Covenant
    {
        get => squidSkin != "native-squids";
        set => squidSkin = value ? (squidSkinExplicit && squidSkin == "mixed-covenant" ? "mixed-covenant" : "all-covenant") : "native-squids";
    }
    public string SquidSkin
    {
        get => squidSkin;
        set { squidSkin = value; squidSkinExplicit = true; }
    }
    public bool AutoPerformanceTier { get; set; } = true;
    public string PerformanceTier { get; set; } = PerformanceTierAdvisor.Auto;
    public bool SoundEffectsEnabled { get; set; } = true;
    public bool DiscordRpcEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public string? LastStepDownSuggestedTier { get; set; }
    public string? PendingStepDownReason { get; set; }
    public int VramTierAdvisoryVersion { get; set; } = 1;
    public string HostChaosIntensity { get; set; } = "total-war";
    public string HostHeavySeaf { get; set; } = "constant";
    public bool HostFasterCorpseCleanup { get; set; } = true;
    public string HostTheaterPreset { get; set; } = "clone-wars";
    public int HostTheaterDefaultVersion { get; set; } = 4;
    public bool PlayWithoutAccountSync { get; set; }
    public string? GalacticWar { get; set; }
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

    internal HostMutatorOptions ToHostMutatorOptions(DateTimeOffset? updatedUtc = null) =>
        HostMutatorConfig.BuildOptions(HostChaosIntensity, HostHeavySeaf, HostFasterCorpseCleanup, HostTheaterPreset, updatedUtc, activeMode: Mode);

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
        if (string.IsNullOrEmpty(HostChaosIntensity)) HostChaosIntensity = "total-war";
        if (string.IsNullOrEmpty(HostHeavySeaf)) HostHeavySeaf = "constant";
        if (string.IsNullOrEmpty(HostTheaterPreset)) HostTheaterPreset = "clone-wars";
        if (string.IsNullOrWhiteSpace(LastStepDownSuggestedTier)) LastStepDownSuggestedTier = null;
        if (string.IsNullOrWhiteSpace(PendingStepDownReason)) PendingStepDownReason = null;
        if (string.IsNullOrWhiteSpace(PlayerLabel)) PlayerLabel = null;
        else PlayerLabel = PlayerLabel.Trim();
        if (string.IsNullOrWhiteSpace(GalacticWar)) GalacticWar = null;

        if (Format != 1 || Mode is not ("regular" or "clonedivers" or "empire") ||
            Texture is not ("full" or "lighter" or "reduced1024" or "reduced512" or "potato256") ||
            EnemySkin is not ("all-droids" or "mixed-droids" or "native-automatons") ||
            SquidSkin is not ("all-covenant" or "mixed-covenant" or "native-squids") ||
            !PerformanceTierAdvisor.IsValidPreferenceTier(PerformanceTier) ||
            (LastStepDownSuggestedTier != null && !PerformanceTierAdvisor.IsConcreteTier(LastStepDownSuggestedTier)) ||
            (PendingStepDownReason != null && PendingStepDownReason.Length > 512) ||
            !HostMutatorConfig.IsValidChaosIntensity(HostChaosIntensity) ||
            !HostMutatorConfig.IsValidHeavySeaf(HostHeavySeaf) ||
            !HostMutatorConfig.IsValidTheaterPreset(HostTheaterPreset) ||
            (GalacticWar != null && GalacticWar is not ("hellforge" or "open" or "live" or "custom" or "frozen")) ||
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
            var legacyTexture = legacy.TextureProfile?.Trim().ToLowerInvariant() switch
            {
                "potato256" or "reduced256" or "reduced-256" => "potato256",
                "reduced512" or "reduced-512" => "reduced512",
                "reduced1024" or "reduced-1024" => "reduced1024",
                "lighter" or "skinny" => "lighter",
                "full" => "full",
                _ => null
            };
            var hasExplicitTexture = legacyTexture != null;
            return new()
            {
                Mode = legacy.Options.GetValueOrDefault("empire") ? "empire" : "clonedivers",
                DeltaSquad = legacy.Options.GetValueOrDefault("commandos", true),
                EnemySkin = legacy.Options.TryGetValue("droids", out var legacyDroids) ? (legacyDroids ? "mixed-droids" : "native-automatons") : "mixed-droids",
                SquidSkin = legacy.Options.TryGetValue("covenant", out var legacyCov) ? (legacyCov ? "all-covenant" : "mixed-covenant") : "mixed-covenant",
                Texture = legacyTexture ?? "full",
                AutoPerformanceTier = !hasExplicitTexture,
                PerformanceTier = !hasExplicitTexture ? PerformanceTierAdvisor.Auto : PerformanceTierAdvisor.TierFromPackTexture(legacyTexture),
                VramTierAdvisoryVersion = hasExplicitTexture && legacyTexture == "full" ? 0 : 1
            };
        }
        if (new FileInfo(file).Length > 65536) throw new InvalidDataException("Forge preferences are oversized.");
        var raw = File.ReadAllText(file);
        using var doc = JsonDocument.Parse(raw);
        var value = JsonSerializer.Deserialize<LinuxForgePreferences>(raw, HellForgePack.Json) ?? throw new InvalidDataException("Empty Forge preferences.");
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetJsonProperty(doc.RootElement, "VramTierAdvisoryVersion", out var vramVerProp) ||
                vramVerProp.ValueKind != JsonValueKind.Number ||
                !vramVerProp.TryGetInt32(out var vramVer))
            {
                value.VramTierAdvisoryVersion = 0;
            }
            else
            {
                value.VramTierAdvisoryVersion = vramVer;
            }
            if (!TryGetJsonProperty(doc.RootElement, "EnemySkin", out _))
            {
                value.EnemySkin = TryGetJsonProperty(doc.RootElement, "Droids", out var droidsProp) && droidsProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (droidsProp.GetBoolean() ? "all-droids" : "native-automatons")
                    : "all-droids";
            }
            if (TryGetJsonProperty(doc.RootElement, "SquidSkin", out var squidProp) && squidProp.ValueKind == JsonValueKind.String)
            {
                value.SquidSkin = squidProp.GetString()!;
            }
            else
            {
                value.SquidSkin = TryGetJsonProperty(doc.RootElement, "Covenant", out var covProp) && covProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (covProp.GetBoolean() ? "all-covenant" : "native-squids")
                    : "native-squids";
            }
            if (!TryGetJsonProperty(doc.RootElement, "AutoPerformanceTier", out _))
            {
                value.AutoPerformanceTier = false;
                if (!TryGetJsonProperty(doc.RootElement, "PerformanceTier", out _))
                    value.PerformanceTier = PerformanceTierAdvisor.TierFromPackTexture(value.Texture);
            }
            if (!value.AutoPerformanceTier)
                value.PerformanceTier = PerformanceTierAdvisor.ReconcileManualTier(value.PerformanceTier, value.Texture);
            int thVer = 0;
            if (TryGetJsonProperty(doc.RootElement, "HostTheaterDefaultVersion", out var thVerProp) &&
                thVerProp.ValueKind == JsonValueKind.Number)
            {
                thVerProp.TryGetInt32(out thVer);
            }
            if (thVer < 2)
            {
                if (string.IsNullOrWhiteSpace(value.HostTheaterPreset) ||
                    string.Equals(value.HostTheaterPreset, "standard", StringComparison.OrdinalIgnoreCase))
                {
                    value.HostTheaterPreset = "clone-wars";
                }
            }
            if (thVer < 3)
            {
                if (string.IsNullOrWhiteSpace(value.HostChaosIntensity) ||
                    string.Equals(value.HostChaosIntensity, "standard", StringComparison.OrdinalIgnoreCase))
                {
                    value.HostChaosIntensity = "total-war";
                }
                if (string.IsNullOrWhiteSpace(value.HostHeavySeaf) ||
                    string.Equals(value.HostHeavySeaf, "standard", StringComparison.OrdinalIgnoreCase))
                {
                    value.HostHeavySeaf = "constant";
                }
                value.HostTheaterDefaultVersion = 3;
            }
            if (thVer < 4)
            {
                if (string.IsNullOrWhiteSpace(value.EnemySkin) ||
                    string.Equals(value.EnemySkin, "all-droids", StringComparison.OrdinalIgnoreCase))
                {
                    value.EnemySkin = "mixed-droids";
                }
                if (string.IsNullOrWhiteSpace(value.SquidSkin) ||
                    string.Equals(value.SquidSkin, "native-squids", StringComparison.OrdinalIgnoreCase))
                {
                    value.SquidSkin = "mixed-covenant";
                }
                value.HostTheaterDefaultVersion = 4;
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
    internal string[] SelectionCommand() =>
        ["select-mode", "--mode", Mode, "--delta", DeltaSquad ? "yes" : "no", "--texture", Texture,
            "--droids", Droids ? "yes" : "no", "--covenant", Covenant ? "yes" : "no", "--enemy-skin", EnemySkin,
            "--squid-skin", SquidSkin,
            "--theater", HostMutatorConfig.IsUltrakillTheater(HostTheaterPreset, HostChaosIntensity) ? "clone-wars" : "standard",
            "--play-without-sync", PlayWithoutAccountSync ? "yes" : "no"];
    internal HellForgePack.Selection PackSelection()
    {
        var options = new Dictionary<string, bool>
        {
            ["droids"] = Droids,
            ["covenant"] = Covenant,
            ["mixed-droids"] = EnemySkin == "mixed-droids",
            ["mixed-covenant"] = SquidSkin == "mixed-covenant"
        };
        return HellForgePack.PinnedSelection(Mode == "empire" ? "empire" : DeltaSquad ? "commandos" : "clonedivers", Texture, options);
    }
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
    internal Action<string, string>? StaleCleanupLogger { get; init; }
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
    internal async Task SetGalacticWarAsync(string warMode, CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before changing Galactic War mode.");
        await connection.RunAsync(["settings", "--war", warMode], ct);
        await RefreshAsync(ct);
    }
    internal async Task LaunchAsync(CancellationToken ct = default)
    {
        try
        {
        await SelectAsync(ct);
        await RefreshAsync(ct);
        if (OperatingSystem.IsLinux() && State?.Status.Running == false && !Game.IsRunning() && !string.IsNullOrWhiteSpace(ResolvedInstallRoot))
        {
            var cleaned = LinuxStaleSessionGuard.CleanupStaleOrphans(ResolvedInstallRoot);
            if (cleaned.Count > 0)
            {
                StaleCleanupLogger?.Invoke("stale-session-cleanup", $"Cleaned {cleaned.Count} post-crash orphaned session wrapper(s) before launch: {string.Join(", ", cleaned)}");
                await RefreshAsync(ct);
            }
        }
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
        await SelectAsync(ct);
        var presetTag = SquadFingerprint.ExtractCatalogPrefix();
        richProgress?.Report(new("Checking required gameplay mods", $"Verifying installed {HellForgeGameplayCatalog.Pinned.Artifacts.Count}-mod {presetTag} preset…", IsIndeterminate: true, CanCancel: true));
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
        richProgress?.Report(new("Installing gameplay mods", $"Configuring pinned {presetTag} load order and sharing…", IsIndeterminate: true, CanCancel: false));
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
        var mods = Path.Combine(root, "mods");
        var selection = Preferences.PackSelection();
        return await Task.Run(async () =>
        {
            FileSafety.RejectLink(cache);
            var exports = Directory.Exists(cache) ? new DirectoryInfo(cache).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName).ToList() : [];
            if (await FindExportAsync(exports, selection, progress, ct, richProgress) is { } prepared) return prepared;
            FileSafety.RejectLink(mods);
            IEnumerable<string> sources = Directory.Exists(mods) ? exports.Prepend(mods) : exports;
            return await HellForgePack.PlanAsync(selection, Path.Combine(cache, "linux-" + Guid.NewGuid().ToString("N")), sources, progress, ct, richProgress: richProgress);
        }, ct);
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
            bool createdFreshExport = !Directory.Exists(plan.Output);
            // Pure disk verification/export runs off-thread without touching LinuxForgePlayer state; bridge commands & RefreshAsync run on host context.
            await Task.Run(async () =>
            {
                if (!createdFreshExport)
                {
                    richProgress?.Report(new("Verifying cached pack", "Checking prepared export metadata…", IsIndeterminate: true, CanCancel: true));
                    await HellForgePack.VerifyMetadataAsync(plan.Output, expected, ct); // reused export; the bridge re-hashes before import
                }
                else
                {
                    await EnsureHostExportAndInstallCapacityAsync(ResolvedInstallRoot, plan, ct);
                    await HellForgePack.ExportAsync(plan, allowDownloads, progress, ct, richProgress: richProgress);
                }
            }, ct);
            try
            {
                await SelectAsync(ct);
                richProgress?.Report(new("Installing Star Wars pack", "Importing verified pack into HellForge…", IsIndeterminate: true, CanCancel: false));
                await connection.RunAsync(["prepare-pack", "--file", path], ct);
            }
            catch (Exception ex)
            {
                if (createdFreshExport)
                    HellForgePack.ReclaimFailedInstallExport(plan, ex);
                throw;
            }
            var pruneDir = Path.GetDirectoryName(Path.GetFullPath(plan.Output))!;
            var pruneKeep = plan.Output;
            await Task.Run(() => PruneExports(pruneDir, pruneKeep), CancellationToken.None);
        }
        await RefreshAsync(ct);
    }
    static Task EnsureHostExportAndInstallCapacityAsync(string? installRoot, HellForgePack.Plan plan, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot)) return Task.CompletedTask;
        long oldBytes = 0;
        var modsRoot = Path.Combine(installRoot, "mods");
        if (Directory.Exists(modsRoot))
        {
            var expectedGuid = HellForgePack.PackId(plan.Selection.Mode)["pack.".Length..];
            var prefix = plan.Selection.Mode + "-";
            foreach (var dir in Directory.EnumerateDirectories(modsRoot))
            {
                bool matchesInstalledPack = Path.GetFileName(dir).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                if (!matchesInstalledPack)
                {
                    var manifestPath = Path.Combine(dir, "manifest.json");
                    try
                    {
                        if (File.Exists(manifestPath) && new FileInfo(manifestPath).Length <= 65536)
                        {
                            FileSafety.RejectLink(manifestPath);
                            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                                doc.RootElement.TryGetProperty("Guid", out var gEl) &&
                                string.Equals(gEl.GetString(), expectedGuid, StringComparison.OrdinalIgnoreCase))
                                matchesInstalledPack = true;
                        }
                    }
                    catch { }
                }
                if (!matchesInstalledPack) continue;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        FileSafety.RejectLink(f);
                        oldBytes = checked(oldBytes + new FileInfo(f).Length);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }
        }
        var cacheRoot = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot)))!, "Clonedivers-HellForge-cache");
        PackArtifactEngine.EnsureStagingAndInstallCapacity(installRoot, cacheRoot, plan, oldBytes, HellForgePack.AvailableExportBytes, ct);
        return Task.CompletedTask;
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

/// <summary>
/// Detects and cleans up stale orphaned hf-inner.sh / hellforge-offline.sh / steam-bridge inner processes
/// left behind after a crashed incomplete Linux session (#44) ONLY when status is not running, the latest
/// session record lacks completedUtc, and no live game/session/CLI/Proton launch process is running.
/// Never touches steam-bridge host, active or starting launches, HellForge.Session, or ClonediversBridge.
/// </summary>
internal static class LinuxStaleSessionGuard
{
    internal sealed record OrphanProcess(int Pid, string Name, string? StartTimeTicks = null);

    internal static (bool LiveSessionProcessFound, IReadOnlyList<OrphanProcess> Orphans) ScanOrphans(
        string installRoot,
        string procRoot = "/proc",
        int? selfPid = null)
    {
        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(procRoot))
            return (false, []);

        var roots = ResolveInstallRoots(installRoot);
        var ignored = new HashSet<int>();
        int startPid = selfPid ?? Environment.ProcessId;
        int? walk = startPid;
        while (walk is int p && p > 1 && ignored.Add(p))
            walk = ReadParentPid(procRoot, p);

        var orphans = new List<OrphanProcess>();
        try
        {
            foreach (var entry in Directory.EnumerateDirectories(procRoot))
            {
                if (!int.TryParse(Path.GetFileName(entry), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid) ||
                    pid <= 1 || ignored.Contains(pid))
                    continue;

                var cmdline = ReadNullSeparated(Path.Combine(entry, "cmdline"), 8192);
                var environ = ReadNullSeparated(Path.Combine(entry, "environ"), 16384);
                var comm = ReadTextSafe(Path.Combine(entry, "comm"), 128).Trim();
                if (comm.Length == 0 && cmdline.Count == 0) continue;

                var cwd = ReadLinkSafe(Path.Combine(entry, "cwd"));
                var exe = ReadLinkSafe(Path.Combine(entry, "exe"));

                if (!BelongsToInstall(roots, cmdline, environ, cwd, exe))
                    continue;

                if (IsPersistentSteamBridgeHost(roots, cmdline, exe) || IsClonediversBridge(cmdline, exe))
                    continue;

                if (IsOrphanWrapperOrInnerBridge(comm, cmdline, exe) is { } label)
                {
                    orphans.Add(new(pid, label, ReadProcStartTime(procRoot, pid)));
                    continue;
                }

                if (IsLiveGameOrController(comm, cmdline, exe))
                    return (true, []);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return (false, orphans);
    }

    internal static bool HasCrashedIncompleteSession(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) return false;
        try
        {
            var sessionsDir = Path.Combine(Path.GetFullPath(installRoot), "logs", "sessions");
            if (!Directory.Exists(sessionsDir)) return false;
            var latest = new DirectoryInfo(sessionsDir)
                .EnumerateDirectories()
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest == null) return false;
            var resultFile = Path.Combine(latest.FullName, "result.json");
            if (!File.Exists(resultFile))
                return File.Exists(Path.Combine(latest.FullName, "play-session.json"));
            if (new FileInfo(resultFile).Length > 1024 * 1024) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(resultFile));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            return !doc.RootElement.TryGetProperty("completedUtc", out var completed) ||
                   completed.ValueKind == JsonValueKind.Null ||
                   string.IsNullOrWhiteSpace(completed.ToString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    internal static string? ReadProcStartTime(string procRoot, int pid)
    {
        var stat = ReadTextSafe(Path.Combine(procRoot, pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "stat"), 4096);
        if (string.IsNullOrEmpty(stat)) return null;
        int closeParen = stat.LastIndexOf(')');
        if (closeParen < 0 || closeParen + 2 >= stat.Length) return null;
        var fields = stat[(closeParen + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // /proc/<pid>/stat field 3 is state (index 0 after ") "), so field 22 (starttime) is index 19.
        return fields.Length > 19 ? fields[19] : null;
    }

    internal static IReadOnlyList<string> CleanupStaleOrphans(
        string installRoot,
        string procRoot = "/proc",
        int? selfPid = null,
        Action<int>? killOverride = null,
        bool requireIncompleteSession = true)
    {
        if (requireIncompleteSession && !HasCrashedIncompleteSession(installRoot))
            return [];

        var (liveFound, orphans) = ScanOrphans(installRoot, procRoot, selfPid);
        if (liveFound || orphans.Count == 0)
            return [];

        var ended = new List<string>();
        foreach (var orphan in orphans)
        {
            try
            {
                if (orphan.StartTimeTicks != null)
                {
                    var currentStart = ReadProcStartTime(procRoot, orphan.Pid);
                    if (!string.Equals(currentStart, orphan.StartTimeTicks, StringComparison.Ordinal))
                        continue;
                }
                else if (killOverride == null)
                {
                    // On real Linux /proc, refuse termination if /proc/<pid>/stat starttime could not be bound.
                    continue;
                }

                if (killOverride != null)
                {
                    killOverride(orphan.Pid);
                }
                else
                {
                    using var proc = System.Diagnostics.Process.GetProcessById(orphan.Pid);
                    if (!string.Equals(ReadProcStartTime(procRoot, orphan.Pid), orphan.StartTimeTicks, StringComparison.Ordinal))
                        continue;
                    proc.Kill(entireProcessTree: false);
                    proc.WaitForExit(2000);
                }
                ended.Add($"{orphan.Name}({orphan.Pid})");
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        }
        return ended;
    }

    internal static IReadOnlyList<string> ResolveInstallRoots(string installRoot)
    {
        var comp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        var roots = new List<string> { normalized };
        var resolved = ResolvePathThroughLinks(normalized);
        if (!string.IsNullOrEmpty(resolved) && !roots.Contains(resolved, comp == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            roots.Add(resolved);
        return roots;
    }

    static string? ResolvePathThroughLinks(string fullPath)
    {
        try
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)) return null;
            var current = root;
            int hops = 0;
            foreach (var part in fullPath[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                var info = new DirectoryInfo(current);
                if (info.Exists && info.LinkTarget != null)
                {
                    if (++hops > 64) return null;
                    if (info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                        current = target.FullName;
                }
            }
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
        }
        catch { return null; }
    }

    static bool BelongsToInstall(IReadOnlyList<string> roots, IReadOnlyList<string> cmdline, IReadOnlyList<string> environ, string? cwd, string? exe)
    {
        foreach (var root in roots)
        {
            if (environ.Any(kv => kv.Equals("INSTALL=" + root, StringComparison.Ordinal)))
                return true;
            if (IsUnder(exe, root) || IsUnder(cwd, root))
                return true;
            foreach (var arg in cmdline)
            {
                if (IsUnder(arg, root)) return true;
            }
        }
        return false;
    }

    static bool IsPersistentSteamBridgeHost(IReadOnlyList<string> roots, IReadOnlyList<string> cmdline, string? exe)
    {
        foreach (var root in roots)
        {
            if (cmdline is [var prog, "steam-bridge", "host"] &&
                IsUnder(prog, root))
                return true;
            var bridgeHostScript = Path.Combine(root, "app", "linux", "hf-bridge-host.sh");
            if (!IsUnder(exe, root) && cmdline.Any(a => PathEquals(a, bridgeHostScript)))
                return true;
        }
        return false;
    }

    static bool IsClonediversBridge(IReadOnlyList<string> cmdline, string? exe)
    {
        static string Leaf(string? p) => string.IsNullOrEmpty(p) ? "" : p[(p.LastIndexOfAny(['/', '\\']) + 1)..];
        var exeLeaf = Leaf(exe);
        if (exeLeaf.Equals(LinuxForgePlan.NativeBridgeName, StringComparison.OrdinalIgnoreCase) ||
            exeLeaf.Equals(LinuxForgePlan.BridgeName, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var arg in cmdline)
        {
            var leaf = Leaf(arg);
            if (leaf.Equals(LinuxForgePlan.NativeBridgeName, StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals(LinuxForgePlan.BridgeName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static bool IsLiveGameOrController(string comm, IReadOnlyList<string> cmdline, string? exe)
    {
        static string Leaf(string? p) => string.IsNullOrEmpty(p) ? "" : p[(p.LastIndexOfAny(['/', '\\']) + 1)..];
        var exeLeaf = Leaf(exe);
        bool isCli = exeLeaf.Equals("HellForge.Cli", StringComparison.OrdinalIgnoreCase) ||
                     exeLeaf.Equals("HellForge.Cli.exe", StringComparison.OrdinalIgnoreCase) ||
                     comm.Equals("HellForge.Cli", StringComparison.OrdinalIgnoreCase) ||
                     cmdline.Any(a => Leaf(a).Equals("HellForge.Cli", StringComparison.OrdinalIgnoreCase) ||
                                      Leaf(a).Equals("HellForge.Cli.exe", StringComparison.OrdinalIgnoreCase));
        if (isCli && cmdline.Any(a => a is "play" or "run"))
            return true;

        var candidates = cmdline.Select(Leaf).Append(exeLeaf).Append(comm);
        foreach (var leaf in candidates)
        {
            if (string.IsNullOrEmpty(leaf)) continue;
            if (leaf.Equals("helldivers2.exe", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("helldivers2", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("HellForge.Session.exe", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("HellForge.Session", StringComparison.OrdinalIgnoreCase) ||
                leaf.StartsWith("HellForge.Sessi", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("HellForge.Launcher", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("HellForge.exe", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("proton", StringComparison.OrdinalIgnoreCase) ||
                leaf.StartsWith("pressure-vessel", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("pv-adverb", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("steam-runtime-launch-client", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    static string? IsOrphanWrapperOrInnerBridge(string comm, IReadOnlyList<string> cmdline, string? exe)
    {
        static string Leaf(string? p) => string.IsNullOrEmpty(p) ? "" : p[(p.LastIndexOfAny(['/', '\\']) + 1)..];
        foreach (var arg in cmdline)
        {
            var leaf = Leaf(arg);
            if (leaf is "hf-inner.sh" or "hellforge-offline.sh" or "hellforge-linux.sh")
                return leaf;
        }
        if (comm is "hf-inner.sh" or "hellforge-offli" or "hellforge-linux")
            return comm;
        var exeLeaf = Leaf(exe);
        if ((exeLeaf.Equals("HellForge.Cli", StringComparison.OrdinalIgnoreCase) ||
             cmdline.Any(a => Leaf(a).Equals("HellForge.Cli", StringComparison.OrdinalIgnoreCase))) &&
            cmdline.Contains("steam-bridge", StringComparer.Ordinal) &&
            cmdline.Contains("inner", StringComparer.Ordinal))
        {
            return "steam-bridge-inner";
        }
        return null;
    }

    static bool IsUnder(string? candidate, string root)
    {
        if (string.IsNullOrEmpty(candidate) || !Path.IsPathRooted(candidate)) return false;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            var comp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return full.Equals(root, comp) || full.StartsWith(root + Path.DirectorySeparatorChar, comp);
        }
        catch { return false; }
    }

    static bool PathEquals(string? a, string expected)
    {
        if (string.IsNullOrEmpty(a) || !Path.IsPathRooted(a)) return false;
        try
        {
            var comp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return Path.GetFullPath(a).Equals(Path.GetFullPath(expected), comp);
        }
        catch { return false; }
    }

    static int? ReadParentPid(string procRoot, int pid)
    {
        var status = ReadTextSafe(Path.Combine(procRoot, pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "status"), 4096);
        var m = System.Text.RegularExpressions.Regex.Match(status, @"^PPid:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline);
        return m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ppid)
            ? ppid
            : null;
    }

    static string ReadTextSafe(string path, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return "";
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[maxBytes];
            int n = fs.Read(buf, 0, buf.Length);
            return n <= 0 ? "" : System.Text.Encoding.UTF8.GetString(buf, 0, n);
        }
        catch { return ""; }
    }

    static IReadOnlyList<string> ReadNullSeparated(string path, int maxBytes)
    {
        var text = ReadTextSafe(path, maxBytes);
        return string.IsNullOrEmpty(text) ? [] : text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    static string? ReadLinkSafe(string path)
    {
        try { return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName; }
        catch { return null; }
    }
}
