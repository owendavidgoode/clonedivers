using System.Diagnostics;
using System.Text.Json;

namespace Clonedivers;

public sealed class HellForgePreferences
{
    string enemySkin = "mixed-droids";
    string squidSkin = "mixed-covenant";
    bool squidSkinExplicit;
    public int Format { get; set; } = 1;
    public string? InstallRoot { get; set; }
    public string Mode { get; set; } = "clonedivers";
    // Read old launcher preferences, but only the imported account is offered to players.
    public string Loadout { get; set; } = "account";
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
    public bool DeathTrooperVoice { get; set; } = false;
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
    public string HostTheaterPreset { get; set; } = "clone-wars";
    public int HostTheaterDefaultVersion { get; set; } = 2;
    public string? PlayerLabel { get; set; }
    public string? PreparedPack { get; set; }
    public string? AppliedSelection { get; set; }
    public bool IsRegular => Mode == "regular";
    public string PackMode => IsRegular ? "regular" : Mode == "empire" ? "empire" : DeltaSquad ? "commandos" : "clonedivers";
    string EnemyKeySegment => enemySkin == "mixed-droids" ? "mixed-droids" : Droids.ToString();
    string SquidKeySegment => squidSkin == "mixed-covenant" ? "mixed-covenant" : Covenant.ToString();
    string EmpireVoiceSuffix => DeathTrooperVoice ? "/deathtrooper" : "";
    public string SelectionKey => IsRegular ? $"regular/{HellForgePack.SupportedBuild}" :
        Mode == "empire" ? $"r54/{HellForgePack.SupportedBuild}/empire/{Texture}/{EnemyKeySegment}/{SquidKeySegment}{EmpireVoiceSuffix}" :
        $"r25/{HellForgePack.SupportedBuild}/{PackMode}/{Texture}/{EnemyKeySegment}/{SquidKeySegment}";
    internal static string? CustomPreferencesPath()
    {
        var custom = Environment.GetEnvironmentVariable("CLONEDIVERS_FORGE_PREFS");
        return string.IsNullOrWhiteSpace(custom) ? null : Path.GetFullPath(custom.Trim());
    }
    public static string FilePath => CustomPreferencesPath() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers", "hellforge.json");

    public void ApplyPerformanceTier(string tierId, HardwarePerformanceRecommendation? hardware = null)
    {
        if (!PerformanceTierAdvisor.IsValidPreferenceTier(tierId))
            throw new ArgumentException($"Unsupported performance tier: {tierId}", nameof(tierId));
        PerformanceTier = tierId;
        AutoPerformanceTier = tierId == PerformanceTierAdvisor.Auto;
        Texture = PerformanceTierAdvisor.ResolvePackTexture(tierId, hardware);
    }

    public HostMutatorOptions ToHostMutatorOptions(DateTimeOffset? updatedUtc = null) =>
        HostMutatorConfig.BuildOptions(HostChaosIntensity, HostHeavySeaf, HostFasterCorpseCleanup, HostTheaterPreset, updatedUtc);

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

    public static HellForgePreferences Load(string? path = null)
    {
        path ??= FilePath;
        FileSafety.RejectLink(path);
        if (!File.Exists(path))
        {
            if (!LegacyAppearance.HasSavedConfigFor(path))
                return new();
            var legacy = LegacyAppearance.ReadFor(path);
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
                EnemySkin = legacy.Options.TryGetValue("droids", out var legacyDroids) ? (legacyDroids ? "all-droids" : "native-automatons") : "mixed-droids",
                SquidSkin = legacy.Options.TryGetValue("covenant", out var legacyCov) ? (legacyCov ? "all-covenant" : "native-squids") : "mixed-covenant",
                Texture = legacyTexture ?? "full",
                AutoPerformanceTier = !hasExplicitTexture,
                PerformanceTier = !hasExplicitTexture ? PerformanceTierAdvisor.Auto : PerformanceTierAdvisor.TierFromPackTexture(legacyTexture)
            };
        }
        if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("HellForge preferences are oversized.");
        var raw = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(raw);
        var preferences = JsonSerializer.Deserialize<HellForgePreferences>(raw, HellForgePack.Json)
            ?? throw new InvalidDataException("HellForge preferences are empty.");
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetJsonProperty(doc.RootElement, "EnemySkin", out _))
            {
                preferences.EnemySkin = TryGetJsonProperty(doc.RootElement, "Droids", out var droidsProp) && droidsProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (droidsProp.GetBoolean() ? "all-droids" : "native-automatons")
                    : "all-droids";
            }
            if (TryGetJsonProperty(doc.RootElement, "SquidSkin", out var squidProp) && squidProp.ValueKind == JsonValueKind.String)
            {
                preferences.SquidSkin = squidProp.GetString()!;
            }
            else
            {
                preferences.SquidSkin = TryGetJsonProperty(doc.RootElement, "Covenant", out var covProp) && covProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (covProp.GetBoolean() ? "all-covenant" : "native-squids")
                    : "native-squids";
            }
            if (!TryGetJsonProperty(doc.RootElement, "AutoPerformanceTier", out _))
            {
                preferences.AutoPerformanceTier = false;
                if (!TryGetJsonProperty(doc.RootElement, "PerformanceTier", out _))
                    preferences.PerformanceTier = PerformanceTierAdvisor.TierFromPackTexture(preferences.Texture);
            }
            if (!preferences.AutoPerformanceTier)
                preferences.PerformanceTier = PerformanceTierAdvisor.ReconcileManualTier(preferences.PerformanceTier, preferences.Texture);
            if (!TryGetJsonProperty(doc.RootElement, "HostTheaterDefaultVersion", out var thVerProp) ||
                thVerProp.ValueKind != JsonValueKind.Number ||
                !thVerProp.TryGetInt32(out var thVer) ||
                thVer < 2)
            {
                if (string.IsNullOrWhiteSpace(preferences.HostTheaterPreset) ||
                    string.Equals(preferences.HostTheaterPreset, "standard", StringComparison.OrdinalIgnoreCase))
                {
                    preferences.HostTheaterPreset = "clone-wars";
                }
                preferences.HostTheaterDefaultVersion = 2;
            }
        }
        preferences.Validate();
        return preferences;
    }
    public void Validate()
    {
        if (string.IsNullOrEmpty(PerformanceTier))
            PerformanceTier = AutoPerformanceTier ? PerformanceTierAdvisor.Auto : PerformanceTierAdvisor.TierFromPackTexture(Texture);
        if (string.IsNullOrEmpty(HostChaosIntensity)) HostChaosIntensity = "standard";
        if (string.IsNullOrEmpty(HostHeavySeaf)) HostHeavySeaf = "standard";
        if (string.IsNullOrEmpty(HostTheaterPreset)) HostTheaterPreset = "clone-wars";
        if (string.IsNullOrWhiteSpace(LastStepDownSuggestedTier)) LastStepDownSuggestedTier = null;
        if (string.IsNullOrWhiteSpace(PendingStepDownReason)) PendingStepDownReason = null;
        if (string.IsNullOrWhiteSpace(PlayerLabel)) PlayerLabel = null;
        else PlayerLabel = PlayerLabel.Trim();

        if (Format != 1 || Mode is not ("regular" or "clonedivers" or "empire") ||
            Loadout is not ("account" or "armory" or "progression") ||
            Texture is not ("full" or "lighter" or "reduced1024" or "reduced512" or "potato256") ||
            EnemySkin is not ("all-droids" or "mixed-droids" or "native-automatons") ||
            SquidSkin is not ("all-covenant" or "mixed-covenant" or "native-squids") ||
            !PerformanceTierAdvisor.IsValidPreferenceTier(PerformanceTier) ||
            (LastStepDownSuggestedTier != null && !PerformanceTierAdvisor.IsConcreteTier(LastStepDownSuggestedTier)) ||
            (PendingStepDownReason != null && PendingStepDownReason.Length > 512) ||
            !HostMutatorConfig.IsValidChaosIntensity(HostChaosIntensity) ||
            !HostMutatorConfig.IsValidHeavySeaf(HostHeavySeaf) ||
            !HostMutatorConfig.IsValidTheaterPreset(HostTheaterPreset) ||
            (PlayerLabel != null && PlayerLabel.Length > 40))
            throw new InvalidDataException("Unsupported HellForge preferences. Restore hellforge.json from backup.");
        Loadout = "account";
    }
    public void Save(string? path = null)
    {
        Validate();
        path ??= FilePath;
        FileSafety.RejectLink(path); FileSafety.RejectLink(path + ".tmp");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        FileSafety.RejectLink(directory);
        Directory.CreateDirectory(directory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, HellForgePack.Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }
    public HellForgePack.Selection Selection()
    {
        if (IsRegular) throw new InvalidOperationException("Regular HellForge does not use a Star Wars pack.");
        var options = new Dictionary<string, bool>
        {
            ["droids"] = Droids,
            ["covenant"] = Covenant,
            ["mixed-droids"] = EnemySkin == "mixed-droids",
            ["mixed-covenant"] = SquidSkin == "mixed-covenant"
        };
        if (Mode == "empire")
            options["deathtrooper"] = DeathTrooperVoice;
        return HellForgePack.PinnedSelection(PackMode, Texture, options);
    }
}

public interface IHellForgeCommands
{
    Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default);
}

/// <summary>The official CLI manages sessions, mods and settings; no game data, engine or networking writes.</summary>
public sealed class HellForgeCommands : IHellForgeCommands
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> UncertainRoots = new(StringComparer.OrdinalIgnoreCase);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long MarkerTicks, long ExpiresUtcTicks)> ValidatedRootsCache = new(StringComparer.OrdinalIgnoreCase);

    int batchDepth;
    JsonElement? cachedBatchStatus;
    long cachedBatchStatusExpiresTicks;

    public string Root { get; }
    public HellForgeCommands(string root) => Root = ValidateRoot(root);

    internal IDisposable BeginBatch()
    {
        Interlocked.Increment(ref batchDepth);
        return new BatchScope(this);
    }

    sealed class BatchScope(HellForgeCommands owner) : IDisposable
    {
        int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (Interlocked.Decrement(ref owner.batchDepth) <= 0)
                owner.InvalidateStatusLease();
        }
    }

    void InvalidateStatusLease()
    {
        cachedBatchStatus = null;
        Interlocked.Exchange(ref cachedBatchStatusExpiresTicks, 0);
    }

    public static string ValidateRoot(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var nowTicks = DateTime.UtcNow.Ticks;
        var marker = Path.Combine(root, "hellforge-install.json");
        if (ValidatedRootsCache.TryGetValue(root, out var cached) && nowTicks < cached.ExpiresUtcTicks)
        {
            try
            {
                var fi = new FileInfo(marker);
                if (fi.Exists && (fi.Attributes & FileAttributes.ReparsePoint) == 0 && fi.LastWriteTimeUtc.Ticks == cached.MarkerTicks &&
                    File.Exists(Path.Combine(root, "app", "cli", "HellForge.Cli.exe")) && File.Exists(Path.Combine(root, "HellForge.exe")))
                {
                    return root;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent)
        {
            FileSafety.RejectLink(directory.FullName);
            if (directory.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose the separate HellForge folder, outside Steam.");
        }
        marker = FileSafety.Child(root, "hellforge-install.json");
        if (!File.Exists(marker) || new FileInfo(marker).Length > 64 * 1024) throw new InvalidDataException("Choose the HellForge installation folder containing hellforge-install.json.");
        using var doc = JsonDocument.Parse(File.ReadAllText(marker));
        if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported HellForge installation format.");
        foreach (var relative in new[] { "app", "app/cli", "app/cli/HellForge.Cli.exe", "HellForge.exe", "mods" })
            FileSafety.RejectLink(Path.Combine(root, relative));
        if (!File.Exists(Path.Combine(root, "app", "cli", "HellForge.Cli.exe")) || !File.Exists(Path.Combine(root, "HellForge.exe")))
            throw new InvalidDataException("HellForge programs are missing. Repair it with the official installer.");

        ValidatedRootsCache[root] = (new FileInfo(marker).LastWriteTimeUtc.Ticks, DateTime.UtcNow.AddSeconds(5).Ticks);
        return root;
    }

    public async Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (UncertainRoots.ContainsKey(Root) && !ReadAfterUncertain(args)) throw new HellForgeCommandOutcomeUnknownException("The previous CLI could not be confirmed stopped. End session and diagnostics remain available. Confirm the CLI has stopped and review the mod state, then restart this launcher before retrying changes or launch.");
        ValidateRoot(Root);
        var command = args.FirstOrDefault() ?? "";
        if (args.Count == 1 && command == "status" && Volatile.Read(ref batchDepth) > 0 && !UncertainRoots.ContainsKey(Root) &&
            cachedBatchStatus is { } leased && DateTime.UtcNow.Ticks < Interlocked.Read(ref cachedBatchStatusExpiresTicks) && !Game.IsRunning())
        {
            return leased;
        }
        if (command is "play" or "stop" or "settings")
            InvalidateStatusLease();

        var start = new ProcessStartInfo(Path.Combine(Root, "app", "cli", "HellForge.Cli.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Root
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            var reply = await TimedAsync(command, TimeSpan.FromMinutes(command == "mod-add" ? 10 : 2), t => ReadJsonProcessAsync(start, command == "play", t), ct);
            if (args.Count == 1 && command == "status" && Volatile.Read(ref batchDepth) > 0 &&
                reply.TryGetProperty("session", out var sess) && sess.TryGetProperty("running", out var runEl) && runEl.ValueKind == JsonValueKind.False)
            {
                cachedBatchStatus = reply;
                Interlocked.Exchange(ref cachedBatchStatusExpiresTicks, DateTime.UtcNow.AddSeconds(2).Ticks);
            }
            return reply;
        }
        catch (HellForgeCommandOutcomeUnknownException) { InvalidateStatusLease(); UncertainRoots[Root] = true; throw; }
        catch { InvalidateStatusLease(); throw; }
    }
    // A deadline is not a player cancel: callers show cancellations as "Operation cancelled" or drop them silently.
    internal static async Task<JsonElement> TimedAsync(string command, TimeSpan limit, Func<CancellationToken, Task<JsonElement>> run, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try { return await run(timeout.Token); }
        catch (OperationCanceledException error) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        { throw new TimeoutException($"HellForge CLI '{command}' did not finish within {limit.TotalMinutes:0} minutes and was stopped. Retry, or open Setup & mods to inspect HellForge.", error); }
    }

    internal static bool ReadAfterUncertain(IReadOnlyList<string> args) => args.FirstOrDefault() is "status" or "mods" or "logs" or "stop" || args.Count == 1 && args[0] == "profiles";
    internal static async Task StopOwnedCliAsync(Process process)
    {
        try
        {
            if (process.HasExited) return;
            process.Kill(); // this CLI only; its independent session/game descendants are never killed here
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(limit.Token);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            try { if (process.HasExited) return; } catch (Exception followup) when (followup is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            throw new HellForgeCommandOutcomeUnknownException("The CLI request could not be confirmed stopped. Do not retry the import or restore over it; review Session details first.", error);
        }
    }

    internal static async Task<JsonElement> ReadJsonProcessAsync(ProcessStartInfo start, bool singleLine, CancellationToken ct)
    {
        using var process = Process.Start(start) ?? throw new IOException("HellForge CLI did not start.");
        using var pipes = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // 'play' emits one JSON line and exits, but its runner can inherit the output
        // handles. EOF then arrives only when the game ends. Wait for the CLI and its
        // acknowledgement, never for the lifetime of that independent session.
        var output = ReadBoundedOutputAsync(process.StandardOutput, 4 * 1024 * 1024, singleLine, pipes.Token);
        var error = ReadBoundedOutputAsync(process.StandardError, 64 * 1024, false, pipes.Token);
        var readFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ObserveAsync(Task read)
        {
            try { await read; } catch (Exception failure) { readFailure.TrySetResult(failure); }
        }
        var observeOutput = ObserveAsync(output); var observeError = ObserveAsync(error);
        try
        {
            var exited = process.WaitForExitAsync(ct);
            // A reader can hit its bound while the CLI is blocked writing to the
            // full pipe. Observe that failure before waiting for process exit.
            if (await Task.WhenAny(exited, readFailure.Task) == readFailure.Task)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(await readFailure.Task).Throw();
            await exited;
            var text = await output;
            if (singleLine && process.ExitCode == 0) pipes.Cancel();
            string why;
            try { why = await error; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && singleLine && process.ExitCode == 0) { why = ""; }
            if (process.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(why) ? $"HellForge failed (exit {process.ExitCode})." : why.Trim());
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("HellForge CLI returned no JSON acknowledgement.");
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch
        {
            // End this CLI request only. A session started by 'play' belongs to HellForge's runner.
            await StopOwnedCliAsync(process);
            throw;
        }
        finally
        {
            pipes.Cancel();
            await Task.WhenAll(observeOutput, observeError);
        }
    }
    internal static async Task<string> ReadBoundedOutputAsync(TextReader reader, int limit, bool singleLine, CancellationToken ct)
    {
        var text = new System.Text.StringBuilder(); var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            var newline = singleLine ? buffer.AsSpan(0, count).IndexOfAny('\r', '\n') : -1;
            var take = newline < 0 ? count : newline;
            if (text.Length + take > limit) throw new InvalidDataException("HellForge CLI output exceeds its supported limit.");
            text.Append(buffer, 0, take);
            if (newline >= 0) return text.ToString();
        }
        return text.ToString();
    }
}

public sealed partial class HellForgeClient(string root, IHellForgeCommands commands)
{
    public const string DifficultyId = "skyeshade.hell-forge";
    public const string ArmoryProfile = "clonedivers-armory";
    public static string ProfileForLoadout(string loadout) => loadout switch {
        "armory" => ArmoryProfile,
        "progression" => "default",
        _ => throw new ArgumentException("Choose Full armory or Progression.", nameof(loadout))
    };
    public string Root { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    internal bool LinuxNamespace { get; init; }
    public HellForgeClient(string root) : this(HellForgeCommands.ValidateRoot(root), new HellForgeCommands(root)) { }

    sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    internal IDisposable BeginCliBatch() => commands is HellForgeCommands native ? native.BeginBatch() : NoopDisposable.Instance;

    internal bool IsUltrakillHostTheaterActive()
    {
        try { return HostMutatorConfig.Read(Root).IsUltrakillTheater; }
        catch { return false; }
    }

    public async Task SyncUltrakillCompanionOptionsAsync(bool enableUltrakill, CancellationToken ct = default)
    {
        using var batch = BeginCliBatch();
        var catalog = await ModsAsync(ct);
        await UltrakillRuntimeProfile.SyncCompanionModOptionsAsync(
            (args, token) => commands.RunAsync(args, token),
            catalog,
            enableUltrakill,
            ct);
    }

    public async Task<HellForgeStatus> StatusAsync(CancellationToken ct = default)
    {
        var json = await commands.RunAsync(["status"], ct);
        var returned = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Text(json, "install")));
        if (!returned.Equals(Root, RecoveryPathComparison)) throw new InvalidDataException("HellForge reported a different installation.");
        var ready = json.GetProperty("ready"); var session = json.GetProperty("session"); var settings = json.GetProperty("settings");
        var running = Bool(session, "running");
        bool? gameLaunched = session.TryGetProperty("gameLaunched", out var gl) && gl.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? gl.GetBoolean()
            : session.TryGetProperty("gamePid", out var gpid) && gpid.ValueKind == JsonValueKind.Number && gpid.TryGetInt32(out var gp)
                ? gp > 0
                : null;
        var failure = OptionalText(session, "lastFailure");
        var lastRun = OptionalText(session, "lastRun");
        var lastStatus = OptionalText(session, "lastStatus");
        string? notice = null;
        if (!running && failure == null) (failure, notice) = CompletedGameDiagnostic(lastRun, Text(json, "gameBuild"));
        return new(returned, Text(json, "hellforge"), Text(json, "gameBuild"), Bool(ready, "gameCopy"), Bool(ready, "sessionRunner"),
            running, Bool(settings, "multiplayer"), Bool(settings, "localPlayFab"), lastStatus, failure, notice, InterruptedGameplayProblem() ?? InterruptedPackProblem(), PinnedProblem(json, LinuxNamespace),
            OptionalText(json, "engine"), json.TryGetProperty("modSdk", out var sdk) && sdk.ValueKind == JsonValueKind.Number && sdk.TryGetInt32(out var number) ? number : null,
            HasRun: !string.IsNullOrWhiteSpace(lastRun) || !string.IsNullOrWhiteSpace(lastStatus), GameLaunched: gameLaunched);
    }

    (string? Failure, string? Notice) CompletedGameDiagnostic(string? run, string build)
    {
        if (string.IsNullOrWhiteSpace(run)) return (null, null);
        try
        {
            var sessions = Path.Combine(Root, "logs", "sessions");
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(run));
            if (!string.Equals(Path.GetDirectoryName(folder), sessions, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) return (null, null);
            var path = Path.Combine(folder, "result.json");
            foreach (var entry in new[] { Root, Path.Combine(Root, "logs"), sessions, folder, path }) FileSafety.RejectLink(entry);
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return (null, null);
            using var result = JsonDocument.Parse(File.ReadAllText(path));
            var json = result.RootElement;
            if (json.ValueKind == JsonValueKind.Object && CleanupProblem(json) is { } cleanup)
                return ($"HellForge did not finish cleaning up the last session ({cleanup}). Check HellForge's session logs before launching again.", null);
            // "observed" describes runner completion, even when the game crashed with exit1 or sandboxed hex gameExitCode.
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("completedUtc", out var completed) &&
                completed.ValueKind == JsonValueKind.String) {
                var exit = CrashTriage.ParseSessionExitCode(json);
                var hasCrashArtifact = HasSessionCrashArtifacts(folder);
                if (exit is { } nonZero && nonZero != 0)
                {
                    // The pinned upstream debugging guide documents this exact game's quit-time fault.
                    // Keep it visible, without treating it as evidence of a mission/mod failure.
                    if (nonZero == 1 && build == "25480438" && HasDocumentedQuitCrash(folder))
                        return (null, "The last exit matches HellForge's documented quit-time crash (EXE+0x626296, code 1). This does not establish mission stability; details remain in the session logs.");
                    // HellForge's own HostBoundedProcess.Stop (user Stop or QuitWatch stuckQuitting cleanup) terminates the job with 0xB7520003;
                    // match HellForge.Session.SessionEnd.Crashed by treating it as a normal session end unless crash artifacts or modloader crash lines exist.
                    if (nonZero == CrashTriage.HellForgeStopExitCode && !hasCrashArtifact && !HasAnyModloaderCrashLine(folder))
                        return (null, null);
                    var label = (uint)nonZero >= 0x10000u ? $"0x{(uint)nonZero:X8}" : nonZero.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return ($"The last game exited unexpectedly (code {label}). Check HellForge's session logs before retrying.", null);
                }
                if (hasCrashArtifact && !(build == "25480438" && HasDocumentedQuitCrash(folder)))
                    return ("The last game crashed during the session. Check HellForge's session logs before retrying.", null);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            // Missing, pruned or partially written diagnostics must not prevent status checks.
        }
        return (null, null);
    }

    internal static int? ParseSessionExitCode(JsonElement json) => CrashTriage.ParseSessionExitCode(json);

    internal static bool HasSessionCrashArtifacts(string sessionDir)
    {
        try
        {
            FileSafety.RejectLink(sessionDir);
            foreach (var sub in new[] { Path.Combine(sessionDir, "game"), Path.Combine(sessionDir, "game", "userdata") })
            {
                if (!Directory.Exists(sub)) continue;
                FileSafety.RejectLink(sub);
                foreach (var dmp in Directory.EnumerateFiles(sub, "crash-*.dmp"))
                {
                    FileSafety.RejectLink(dmp);
                    return true;
                }
            }
            foreach (var file in CrashTriage.FindEngineLogCandidates(sessionDir).TakeLast(2))
            {
                FileSafety.RejectLink(file);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var start = Math.Max(0L, stream.Length - CrashTriage.MaxLogTailBytes);
                stream.Position = start;
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                if (start > 0) _ = reader.ReadLine();
                while (reader.ReadLine() is { } line)
                {
                    if (CrashTriage.HasEngineCrashLine(line))
                        return true;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
        return false;
    }

    // HellForge 0.4 never writes "cleanup_failed": PlaySession.CleanUp records each failed step under its own key while
    // status stays "observed". Mirror its SessionIntegrity.LastEndedUnclean, plus the steps that put files back.
    internal static string? CleanupProblem(JsonElement result)
    {
        foreach (var key in new[] { "cleanupError", "jobStopTimeout", "jobStopError", "releaseHoldsError", "sdkShimRestoreFailure", "sandboxCopyBackError" })
            if (result.TryGetProperty(key, out var value)) return value.ValueKind == JsonValueKind.String ? key + ": " + value.GetString() : key;
        foreach (var key in new[] { "jobEmpty", "sdkShimOriginalsRestored" })
            if (result.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.False) return key + " = false";
        return null;
    }

    static bool HasAnyModloaderCrashLine(string folder) =>
        CrashTriage.ReadBoundedModloaderTail(folder).Any(l => l.Contains(" crash: ", StringComparison.OrdinalIgnoreCase));

    static bool HasDocumentedQuitCrash(string folder)
    {
        try {
            var game = Path.Combine(folder, "game");
            var path = Path.Combine(game, "modloader.log");
            FileSafety.RejectLink(game); FileSafety.RejectLink(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - 64 * 1024);
            stream.Position = start;
            using var reader = new StreamReader(stream);
            // Discard a partial first line when reading a bounded tail.
            if (start > 0) reader.ReadLine();
            var found = false;
            while (reader.ReadLine() is { } line) {
                var marker = line.IndexOf(" crash: ", StringComparison.Ordinal);
                if (marker < 0) continue;
                var crash = line[(marker + 8)..];
                if (System.Text.RegularExpressions.Regex.IsMatch(crash,
                    @"^c0000005 on thread [0-9]+ at helldivers2\.exe\+0x626296 \(")) found = true;
                else if (!crash.StartsWith("by module: at helldivers2.exe+0x626296;", StringComparison.Ordinal) &&
                         !crash.StartsWith("fail-fast about to end the game (", StringComparison.Ordinal)) return false;
            }
            return found;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
    // `HellForge.Cli mods` rewrites mods/.unpacked/.zip-hashes.json through a random .partial file and
    // leaves that file behind when another process holds the target (HellForge 0.4 Mods.SaveHashes).
    // A 5-second poll left one per poll (11,405 on the owner's PC), so the launcher rescans only when the
    // mods folder changed and sweeps old leftovers while idle.
    internal string ModsFingerprint()
    {
        var folder = Path.Combine(Root, "mods");
        if (!Directory.Exists(folder)) return "";
        return string.Join("\n", new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 })
            .Where(i => !i.Name.StartsWith('.'))
            .Select(i => $"{i.Name}|{(i as FileInfo)?.Length}|{i.LastWriteTimeUtc.Ticks}")
            .OrderBy(p => p, StringComparer.Ordinal));
    }
    // Each account review starts HellForge.LocalBackend and re-hashes the capture. Its outcome depends only on the capture,
    // the profile's receipt/journal/profile.json and its restore/import backups (excluding transient .bak, .lock, .tmp, launcher.json).
    static readonly HashSet<string> RelevantProfileFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "profile.json",
        "account-import.json",
        HellForgeAccount.ReceiptName,
        HellForgeAccount.PendingName
    };
    internal string AccountFingerprint()
    {
        var snapshot = Path.Combine(Root, "account-snapshot", HellForgeAccount.Profile);
        var profile = Path.Combine(Root, "profiles", HellForgeAccount.Profile);
        var backups = Path.Combine(profile, "backups");
        var entries = new List<FileInfo>();
        if (Directory.Exists(snapshot))
            entries.AddRange(new DirectoryInfo(snapshot).EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0 }));
        if (Directory.Exists(profile))
            entries.AddRange(new DirectoryInfo(profile).EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0 })
                .Where(f => RelevantProfileFiles.Contains(f.Name)));
        if (Directory.Exists(backups))
            entries.AddRange(new DirectoryInfo(backups).EnumerateFiles("*.json", new EnumerationOptions { AttributesToSkip = 0 })
                .Where(f => f.Name.EndsWith("-restore.json", StringComparison.OrdinalIgnoreCase) ||
                            f.Name.EndsWith("-import.json", StringComparison.OrdinalIgnoreCase)));
        return string.Join("\n", entries
            .Select(f => $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}")
            .OrderBy(p => p, StringComparer.Ordinal));
    }
    static readonly System.Text.RegularExpressions.Regex HashPartial = new(@"^\.zip-hashes\.json\.[0-9a-f]{8}\.partial$");
    internal int SweepHashPartials(DateTime olderThanUtc)
    {
        var cache = Path.Combine(Root, "mods", ".unpacked");
        if (!Directory.Exists(cache)) return 0;
        FileSafety.RejectLink(cache);
        var removed = 0;
        foreach (var file in new DirectoryInfo(cache).EnumerateFiles(".zip-hashes.json.*.partial", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if (!HashPartial.IsMatch(file.Name) || file.LastWriteTimeUtc >= olderThanUtc || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            try { file.Delete(); removed++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }
    public async Task<HellForgeMods> ModsAsync(CancellationToken ct = default)
    {
        var json = await commands.RunAsync(["mods"], ct);
        var mods = json.GetProperty("mods").EnumerateArray().Select(m => new HellForgeMod(Text(m, "id"), Text(m, "name"),
            Bool(m, "enabled"), Text(m, "folder"), m.GetProperty("tags").EnumerateArray().Select(t => t.GetString() ?? "").ToArray(),
            OptionalText(m, "version") ?? "", m.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array
                ? options.EnumerateArray().Select(o => new HellForgeOption(Text(o, "name"), Bool(o, "on"))).ToArray() : null, OptionalText(m, "archive"))).ToList();
        if (mods.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != mods.Count) throw new InvalidDataException("HellForge reported duplicate mod IDs.");
        string? hostingProblem;
        try
        {
            var folder = Path.Combine(Root, "mods");
            FileSafety.RejectLink(folder);
            var file = Path.Combine(folder, "hellforge-mods.json");
            FileSafety.RejectLink(file);
            if (!File.Exists(file)) { file = Path.Combine(folder, "bts-mods.json"); FileSafety.RejectLink(file); }
            if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("Mod settings exceed the supported size.");
            hostingProblem = HellForgePreset.SharingProblem(File.Exists(file) ? File.ReadAllText(file) : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { hostingProblem = "Could not verify HellForge mod-sharing settings: " + e.Message; }
        return new(mods, json.GetProperty("problems").EnumerateArray().Select(p => p.ToString()).ToArray(), OptionalText(json, "modlist"), hostingProblem);
    }
    internal static string? PinnedProblem(JsonElement json, bool linuxNamespace = false)
    {
        if (!HellForgeRelease.Supports(OptionalText(json, "hellforge")) || OptionalText(json, "engine") != HellForgeRelease.Engine ||
            !json.TryGetProperty("modSdk", out var sdk) || sdk.ValueKind != JsonValueKind.Number || !sdk.TryGetInt32(out var sdkNumber) || sdkNumber != HellForgeRelease.Sdk)
            return "This preset requires HellForge 0.4.0-beta, engine0.1.47 and SDK16. Review the installed version in official HellForge before preparing or launching; the launcher will not change engines.";
        static bool True(JsonElement parent, string key) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
        if (!json.TryGetProperty("settings", out var settings) || !True(settings, "sandbox") ||
            (linuxNamespace ? OptionalText(json, "networkBlock") != "linux-netns" : OptionalText(json, "networkBlock") is not ("wfp" or "firewall")) ||
            linuxNamespace && !OperatingSystem.IsLinux() && (!json.TryGetProperty("ready", out var ready) || !True(ready, "linuxNamespace")))
            return linuxNamespace ? "Official Linux namespace and sandbox checks must pass before preparing or launching. Review official HellForge setup; isolation settings will not be changed automatically."
                : "Continue guided setup to enable the official sandbox. HellForge verifies and installs its network block at launch, with Windows approval when needed.";
        return null;
    }
    public static void RequireReady(HellForgeStatus status)
    {
        if (status.Running || Game.IsRunning()) throw new InvalidOperationException("Close the running Helldivers session before changing or launching HellForge.");
        if (status.CompatibilityProblem != null) throw new InvalidDataException(status.CompatibilityProblem);
        if (status.RecoveryProblem != null) throw new InvalidOperationException(status.RecoveryProblem);
        if (status.Build != HellForgePack.SupportedBuild) throw new InvalidDataException("This HellForge game build does not match the pinned Star Wars pack.");
        if (!status.GameCopyReady || !status.RunnerReady) throw new InvalidDataException("Finish setup in HellForge before playing.");
    }
    async Task RequireIdleAsync(CancellationToken ct)
    {
        if (InterruptedGameplayProblem() is { } gameplay) throw new InvalidOperationException(gameplay);
        if (Game.IsRunning()) throw new InvalidOperationException("Keep your game open; finish this step after you close it.");
        var status = await StatusAsync(ct);
        if (status.Running) throw new InvalidOperationException("A HellForge session is already running.");
        if (status.CompatibilityProblem != null) throw new InvalidDataException(status.CompatibilityProblem);
        if (status.Build != HellForgePack.SupportedBuild) throw new InvalidDataException("HellForge and the pack target different game builds.");
    }

    public async Task EndSessionAsync(CancellationToken ct = default, TimeSpan? wait = null)
    {
        var limit = wait ?? TimeSpan.FromSeconds(45);
        if (limit < TimeSpan.Zero || limit > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(wait));
        var status = await StatusAsync(ct);
        // Never stop an unrelated Steam game, and allow recovery while the copy is staged or the build has changed.
        // A session that ended before this request still has its cleanup checked before it is reported complete.
        if (!status.Running) { RequireCompletedCleanup(status); return; }
        JsonElement response;
        try { response = await commands.RunAsync(["stop"], ct); }
        catch (IOException)
        {
            // The game can finish quitting between status and the official stop request.
            status = await StatusAsync(ct);
            if (status.Running) throw;
            RequireCompletedCleanup(status);
            return;
        }
        if (OptionalText(response, "event") != "stopping") throw new InvalidDataException("HellForge did not acknowledge the stop request.");
        var clock = Stopwatch.StartNew();
        do
        {
            status = await StatusAsync(ct);
            if (!status.Running)
            {
                RequireCompletedCleanup(status);
                return;
            }
            if (clock.Elapsed >= limit) break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        } while (true);
        throw new TimeoutException("HellForge accepted End session, but cleanup is still running. Keep the launcher open and check Session details; do not repair an active game.");
    }
    static void RequireCompletedCleanup(HellForgeStatus status)
    {
        if (status.LastStatus == "cleanup_failed" || status.LastFailure != null || !status.GameCopyReady)
            throw new InvalidOperationException("The session stopped, but HellForge cleanup needs attention. " + (status.LastFailure ?? "Open Session details before launching again."));
    }

    public async Task<string> EnsureLoadoutAsync(string loadout, CancellationToken ct = default)
    {
        var name = ProfileForLoadout(loadout);
        await RequireIdleAsync(ct);
        var profiles = await commands.RunAsync(["profiles"], ct);
        var found = profiles.GetProperty("profiles").EnumerateArray().FirstOrDefault(p => Text(p, "name") == name);
        if (found.ValueKind == JsonValueKind.Undefined)
        {
            if (loadout != "armory") throw new InvalidDataException("The original progression profile is missing. Restore it in HellForge's Profile page.");
            await RequireIdleAsync(ct);
            profiles = await commands.RunAsync(["profiles", "--create", name, "--template", "unlock-all"], ct);
            found = profiles.GetProperty("profiles").EnumerateArray().FirstOrDefault(p => Text(p, "name") == name);
            if (found.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("HellForge did not create the full-armory profile.");
        }
        // Never reset an existing profile or replace its inventory/progression.
        if (loadout == "armory" && Text(found, "template") != "unlock-all")
            throw new InvalidDataException("The full-armory profile name is already used by another template. Review it in HellForge's Profile page.");
        return name;
    }

    public async Task SelectAccountAsync(CancellationToken ct = default, bool reviewCapture = true)
    {
        await RequireIdleAsync(ct);
        RequireNoInterruptedPack();
        if (HellForgeAccount.CaptureBlock() is { } capture) throw new InvalidOperationException(capture);
        var profiles = await commands.RunAsync(["profiles"], ct);
        var account = profiles.GetProperty("profiles").EnumerateArray().FirstOrDefault(p => Text(p, "name") == HellForgeAccount.Profile);
        if (account.ValueKind == JsonValueKind.Undefined || Text(account, "template") != "starter")
            throw new InvalidDataException("The default account profile is missing or uses a test template. Restore it in HellForge before syncing; existing characters will not be reset.");
        if (reviewCapture) await new HellForgeAccount(Root).BindLegacyReceiptAsync(ct);
        else new HellForgeAccount(Root).DisableAutoApply();
        await RequireIdleAsync(ct);
        await commands.RunAsync(["settings", "--profile", HellForgeAccount.Profile], ct);
    }

    public async Task<string> EnsureAccountAsync(CancellationToken ct = default)
    {
        await SelectAccountAsync(ct);
        var account = await new HellForgeAccount(Root).ReadAsync(ct);
        if (!account.Imported) throw new InvalidOperationException(account.Message);
        return HellForgeAccount.Profile;
    }

    public Task<int> OpenOwnedWarbondsAsync(CancellationToken ct = default) => new HellForgeWarbonds(Root, commands).OpenAsync(ct);
    // Best effort after an import or an idle review: an open HellForge window or a busy profile only defers it to the next review.
    public async Task<int> TryOpenOwnedWarbondsAsync(CancellationToken ct = default)
    {
        try { return await OpenOwnedWarbondsAsync(ct); }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or TimeoutException) { return 0; }
    }

    public async Task<int> ImportAccountAsync(CancellationToken ct = default)
    {
        await SelectAccountAsync(ct);
        await new HellForgeAccount(Root).ImportAsync(ct);
        return await TryOpenOwnedWarbondsAsync(ct);
    }

    public Task InstallPackAsync(string folder, HellForgePack.Selection selection, CancellationToken ct = default) =>
        InstallPackAsync(folder, selection, null, ct);

    public async Task InstallPackAsync(string folder, HellForgePack.Selection selection, IProgress<OperationProgress>? progress, CancellationToken ct = default)
    {
        await RequireIdleAsync(ct);
        using var operation = LockPackChange();
        RequireNoInterruptedPack();
        RequireCliQuiescent();
        using var batch = BeginCliBatch();
        var verifySpeed = new SpeedTracker();
        progress?.Report(new("Verifying prepared pack", "Checking file integrity before install…", 0, selection.Files.Count, 0, selection.Files.Sum(f => f.Size), CanCancel: true));
        await HellForgePack.VerifyAsync(folder, selection, progress == null ? null : new SyncProgress<(long Done, long Total)>(p =>
        {
            var (rate, eta) = verifySpeed.Update(p.Done, p.Total);
            progress.Report(new("Verifying prepared pack", null, 0, 0, p.Done, p.Total, rate, eta, CanCancel: true));
        }), ct);
        var before = await ModsAsync(ct).ConfigureAwait(false);
        if (before.Problems.Length > 0) throw new InvalidDataException("Resolve HellForge's mod notices before installing: " + string.Join("; ", before.Problems));
        var beforeStatus = await StatusAsync(ct);
        // Preserve the previous selection and any pack about to be replaced, outside the installation.
        var backup = Path.Combine(CacheRoot, "backups", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        for (var parent = new DirectoryInfo(backup); parent != null; parent = parent.Parent) FileSafety.RejectLink(parent.FullName);
        var settingsPath = Path.Combine(Root, "mods", "hellforge-mods.json");
        FileSafety.RejectLink(settingsPath);
        var old = before.Mods.SingleOrDefault(m => m.Id == HellForgePack.PackId(selection.Mode));
        var sources = new List<(string Source, string Relative, long Bytes)>();
        if (old != null)
        {
            ValidateModFolder(old.Folder);
            foreach (var entry in Directory.EnumerateFileSystemEntries(old.Folder, "*", SearchOption.AllDirectories)) FileSafety.RejectLink(entry);
            foreach (var file in Directory.EnumerateFiles(old.Folder, "*", SearchOption.AllDirectories))
            {
                sources.Add((file, Path.GetRelativePath(old.Folder, file), new FileInfo(file).Length));
            }
            ValidateCosmeticBackup(old.Folder, old.Id);
        }
        var replacementBytes = checked(selection.Files.Sum(f => f.Size) + new FileInfo(Path.Combine(folder, "manifest.json")).Length + new FileInfo(Path.Combine(folder, HellForgePack.ReceiptName)).Length);
        bool cleanedPackCacheOnce = false;
        CheckPackCapacity(backup, checked(sources.Sum(f => f.Bytes) + replacementBytes), folder, ref cleanedPackCacheOnce, ct);
        CheckPackCapacity(Path.Combine(Root, "mods"), replacementBytes, folder, ref cleanedPackCacheOnce, ct);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(backup);
        if (File.Exists(settingsPath)) await HellForgePack.CopyLocalAsync(settingsPath, Path.Combine(backup, "hellforge-mods.json"), new FileInfo(settingsPath).Length, ct);
        var evidence = new List<PackBackupFile>();
        long totalBackupBytes = sources.Sum(f => f.Bytes);
        long doneBackupBytes = 0;
        var backupSpeed = new SpeedTracker();
        for (int i = 0; i < sources.Count; i++)
        {
            var file = sources[i];
            var target = Path.Combine(backup, "previous-pack", file.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            long baseBytes = doneBackupBytes;
            var backupSha = await PackArtifactEngine.CopyAndHashAsync(file.Source, target, file.Bytes, ct, progress == null ? null : copied =>
            {
                var (rate, eta) = backupSpeed.Update(baseBytes + copied, totalBackupBytes);
                progress.Report(new("Backing up current appearance", file.Relative, i + 1, sources.Count, baseBytes + copied, totalBackupBytes, rate, eta, CanCancel: true));
            });
            doneBackupBytes += file.Bytes;
            evidence.Add(new(file.Relative, file.Bytes, backupSha));
        }
        await File.WriteAllTextAsync(Path.Combine(backup, "before.json"), JsonSerializer.Serialize(new { mods = before, status = beforeStatus, files = evidence }, HellForgePack.Json), ct);
        await RequireIdleAsync(ct);
        var checkpoint = await StatusAsync(ct);
        if (checkpoint.Running || Game.IsRunning() || PackState(before) != PackState(await ModsAsync(ct)) || checkpoint.Multiplayer != beforeStatus.Multiplayer || checkpoint.LocalBackend != beforeStatus.LocalBackend ||
            old != null && !await MatchesPackBackupAsync(old.Folder, evidence, ct))
            throw new InvalidOperationException("The mod selection changed while its backup was being prepared. Nothing was imported; review your selection and try again.");
        var intent = await BeginPackIntentAsync(backup, selection.Mode, ct);
        try
        {
            progress?.Report(new("Importing pack into HellForge", "HellForge is registering your Star Wars pack — please wait…", IsIndeterminate: true, CanCancel: false));
            var imported = await commands.RunAsync(["mod-add", "--file", Path.Combine(folder, "manifest.json")], ct);
            if (Text(imported, "id") != HellForgePack.PackId(selection.Mode)) throw new InvalidDataException("HellForge imported an unexpected pack ID.");
            progress?.Report(new("Applying pack settings", "Enabling selected universe and Steam co-op…", IsIndeterminate: true, CanCancel: false));
            await ConfigureAsync(selection, ct);
            var after = await ModsAsync(ct);
            var selected = RequireSelected(after, selection);
            ValidateModFolder(selected.Folder);
            var postVerifySpeed = new SpeedTracker();
            await HellForgePack.VerifyAsync(selected.Folder, selection, progress == null ? null : new SyncProgress<(long Done, long Total)>(p =>
            {
                var (rate, eta) = postVerifySpeed.Update(p.Done, p.Total);
                progress.Report(new("Verifying installed pack", null, 0, 0, p.Done, p.Total, rate, eta, CanCancel: false));
            }), ct);
            HellForgePack.SaveVerifiedLayout(selected.Folder, selection, Path.Combine(CacheRoot, "verify-cache.json"));
            SavePackOutcome(backup, "applied");
            WritePackIntent(intent with { State = "applied" });
            PrunePackStorage(backup, after);
        }
        catch (HellForgeCommandOutcomeUnknownException error)
        {
            SavePackOutcome(backup, "outcome-unknown");
            throw new HellForgeCommandOutcomeUnknownException("The appearance change has an unknown CLI outcome. No restore was attempted over it. Review Session details first. Backup: " + backup, error);
        }
        catch (Exception error)
        {
            try
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                await RestorePackChangeAsync(before, beforeStatus, selection.Mode, old, backup, evidence, recovery.Token);
                SavePackOutcome(backup, "restored");
                WritePackIntent(intent with { State = "restored" });
                PrunePackStorage(backup, before);
            }
            catch (Exception recoveryError)
            {
                SavePackOutcome(backup, "recovery-needed");
                throw new IOException("The appearance change failed and recovery needs attention. No active session was modified. Backup: " + backup + ". Recovery: " + recoveryError.Message, new AggregateException(error, recoveryError));
            }
            throw new IOException("The appearance change did not finish. Your previous appearance and selection were restored through HellForge. Backup: " + backup, error);
        }
    }

    internal Func<string, long?> AvailablePackBytes { get; set; } = HellForgePack.AvailableExportBytes;
    sealed record PackBackupFile(string Relative, long Bytes, string Sha256);
    void CheckPackCapacity(string output, long bytes, string preserveFolder, ref bool cleanedOnce, CancellationToken ct)
    {
        var required = checked(bytes + HellForgePack.ExportReserve);
        if (AvailablePackBytes(output) is not long free || free >= required) return;
        if (!cleanedOnce)
        {
            cleanedOnce = true;
            try { ForgeDiskManager.CleanSafeFilesSync(Root, ct, [preserveFolder], reclaimUnpreservedExports: true, callerOwnedLockPath: PackIntentPath + ".lock"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException) { }
            if (AvailablePackBytes(output) is not long freeAfter || freeAfter >= required) return;
            free = freeAfter;
        }
        throw new IOException($"Not enough space for the appearance backup and replacement reserve: {Pack.FormatBytes(required)} needed, {Pack.FormatBytes(Math.Max(0, free))} available.");
    }

    /// <summary>
    /// Verifies upfront that the target volume(s) have enough free space for BOTH staging <paramref name="plan"/>
    /// AND the subsequent <see cref="InstallPackAsync"/> backup + replacement reserve, reclaiming safe stale caches first
    /// so low-space drives never fail halfway after stranding a 12 GiB export folder.
    /// </summary>
    public async Task EnsureExportAndInstallCapacityAsync(HellForgePack.Plan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();

        var before = await ModsAsync(ct).ConfigureAwait(false);
        var old = before.Mods.SingleOrDefault(m => m.Id == HellForgePack.PackId(plan.Selection.Mode));
        long oldBytes = 0;
        if (old != null && Directory.Exists(old.Folder))
        {
            ValidateModFolder(old.Folder);
            foreach (var file in Directory.EnumerateFiles(old.Folder, "*", SearchOption.AllDirectories))
            {
                FileSafety.RejectLink(file);
                oldBytes = checked(oldBytes + new FileInfo(file).Length);
            }
        }

        PackArtifactEngine.EnsureStagingAndInstallCapacity(Root, CacheRoot, plan, oldBytes, AvailablePackBytes, ct);
    }
    static string PackState(HellForgeMods mods) => JsonSerializer.Serialize(mods.Mods.Select(m => new { m.Id, m.Enabled, m.Name, m.Version, m.Options }).OrderBy(m => m.Id, StringComparer.Ordinal));
    static void SavePackOutcome(string backup, string outcome)
    {
        try { File.WriteAllText(Path.Combine(backup, "outcome.json"), JsonSerializer.Serialize(new { outcome, utc = DateTimeOffset.UtcNow })); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    // Called only under the appearance lock, after the durable terminal intent.
    // Unrecorded/incomplete backups and unreceipted exports are never candidates.
    internal void PrunePackStorage(string backup, HellForgeMods current)
    {
        try
        {
            var intent = ReadPackIntent();
            if (intent == null || intent.State == "pending" || !intent.Backup.Equals(backup, RecoveryPathComparison) || InterruptedGameplayProblem() != null) return;
            var normalizedBackup = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backup));
            var currentStamp = Path.GetFileName(normalizedBackup);
            var keep = new HashSet<string>(StringComparer.FromComparison(RecoveryPathComparison)) { normalizedBackup };
            var transactions = Path.GetDirectoryName(PackIntentPath)!;
            RejectAncestors(transactions);
            foreach (var path in Directory.EnumerateFiles(transactions, "*.json"))
            {
                try
                {
                    RejectAncestors(path);
                    if (new FileInfo(path).Length > 16384) continue;
                    using var record = JsonDocument.Parse(File.ReadAllText(path));
                    if (record.RootElement.ValueKind == JsonValueKind.Object &&
                        record.RootElement.TryGetProperty("Backup", out var saved) &&
                        saved.ValueKind == JsonValueKind.String &&
                        saved.GetString() is { Length: > 0 } retained)
                    {
                        var txState = record.RootElement.TryGetProperty("State", out var stProp) && stProp.ValueKind == JsonValueKind.String
                            ? stProp.GetString()
                            : null;
                        if (txState is not ("applied" or "restored"))
                            keep.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(retained)));
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or KeyNotFoundException or ArgumentException or InvalidOperationException or NullReferenceException or NotSupportedException)
                {
                    // Skip unreadable, oversized, or damaged records from other roots without aborting cleanup.
                }
            }
            var backups = Path.Combine(CacheRoot, "backups"); RejectAncestors(Path.Combine(backups, "entry"));
            var exportsRoot = Path.Combine(CacheRoot, "exports"); RejectAncestors(Path.Combine(exportsRoot, "entry"));
            var completed = new List<string>();
            var deleting = new List<string>();
            if (Directory.Exists(backups))
            {
                foreach (var folder in Directory.EnumerateDirectories(backups))
                {
                    try
                    {
                        var name = Path.GetFileName(folder);
                        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
                        if (name.StartsWith("deleting-", StringComparison.Ordinal))
                        {
                            FileSafety.RejectLink(folder);
                            if (!keep.Contains(normalized)) deleting.Add(folder);
                            continue;
                        }
                        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d{8}-\d{6}-(regular-)?[0-9a-f]{32}$")) continue;
                        var outcome = Path.Combine(folder, "outcome.json"); RejectAncestors(outcome);
                        if (!File.Exists(outcome)) continue;
                        if (new FileInfo(outcome).Length > 16384) continue;
                        using var record = JsonDocument.Parse(File.ReadAllText(outcome));
                        if (record.RootElement.ValueKind != JsonValueKind.Object ||
                            !record.RootElement.TryGetProperty("outcome", out var outcomeProp) ||
                            outcomeProp.ValueKind != JsonValueKind.String ||
                            outcomeProp.GetString() is not ("applied" or "restored")) continue;
                        if (!keep.Contains(normalized) && currentStamp.Length >= 15 && string.CompareOrdinal(name[..15], currentStamp[..15]) <= 0)
                            completed.Add(folder);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or KeyNotFoundException or ArgumentException or InvalidOperationException or NullReferenceException)
                    {
                        // Non-(applied/restored) or unreadable outcome.json stays kept without aborting other cleanup.
                    }
                }
            }
            // Read all metadata before deleting anything. A damaged installed-mod record means review.
            var selections = current.Mods.Where(m => HellForgePack.IsOurPack(m.Id)).Select(m => HellForgePack.ReadStorageReceipt(m.Folder))
                .Where(s => s != null).Select(s => HellForgePack.SelectionIdentity(s!)).ToHashSet(StringComparer.Ordinal);
            var exports = HellForgePack.SupersededExports(exportsRoot, selections);
            if (Directory.Exists(exportsRoot))
            {
                foreach (var folder in Directory.EnumerateDirectories(exportsRoot))
                {
                    if (!Path.GetFileName(folder).StartsWith("deleting-", StringComparison.Ordinal)) continue;
                    try
                    {
                        FileSafety.RejectLink(folder);
                        if (!keep.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)))) deleting.Add(folder);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
                }
            }
            foreach (var folder in completed.Concat(exports).Concat(deleting))
                try { HellForgePack.DeleteStorageFolder(CacheRoot, folder); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
            const long LowDiskPostApplyReserveBytes = 32L * 1024 * 1024 * 1024;
            if (intent.State == "applied" && AvailablePackBytes(CacheRoot) is long freeAfter && freeAfter < LowDiskPostApplyReserveBytes)
            {
                try { HellForgePack.DeleteStorageFolder(CacheRoot, normalizedBackup); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
                if (Directory.Exists(exportsRoot))
                {
                    foreach (var folder in Directory.EnumerateDirectories(exportsRoot))
                    {
                        var exportName = Path.GetFileName(folder);
                        if (!System.Text.RegularExpressions.Regex.IsMatch(exportName, "^(clonedivers|commandos|empire|linux)-[0-9a-f]{32}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                        try
                        {
                            if (HellForgePack.ReadStorageReceipt(folder) != null)
                                HellForgePack.DeleteStorageFolder(CacheRoot, folder);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException or NullReferenceException or NotSupportedException) { }
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or KeyNotFoundException or ArgumentException or InvalidOperationException or NullReferenceException) { }
    }
    static void ValidateCosmeticBackup(string folder, string id)
    {
        var manifest = FileSafety.Child(folder, "manifest.json");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 4 * 1024 * 1024 || File.Exists(Path.Combine(folder, "hellforge-mod.json")) || Directory.EnumerateDirectories(folder).Any())
            throw new InvalidDataException("The existing appearance ID is not a restorable cosmetic pack. Review that ID in HellForge before replacing it.");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        if ("pack." + doc.RootElement.GetProperty("Guid").GetString() != id) throw new InvalidDataException("The previous appearance manifest has a different ID; nothing was replaced.");
    }
    static async Task<bool> MatchesPackBackupAsync(string folder, List<PackBackupFile> evidence, CancellationToken ct)
    {
        try
        {
            FileSafety.RejectLink(folder);
            if (Directory.EnumerateDirectories(folder).Any()) return false; // our generated cosmetic packs are flat; never traverse added links/folders
            var files = Directory.GetFiles(folder);
            if (files.Length != evidence.Count) return false;
            foreach (var file in evidence.OrderBy(f => f.Relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                var path = Path.GetFullPath(Path.Combine(folder, file.Relative));
                if (!path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
                for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)!); parent != null && parent.FullName.Length >= folder.Length; parent = parent.Parent) FileSafety.RejectLink(parent.FullName);
                FileSafety.RejectLink(path);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes || await HellForgePack.HashAsync(path, ct) != file.Sha256) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
    }
    async Task RestorePackChangeAsync(HellForgeMods before, HellForgeStatus beforeStatus, string mode, HellForgeMod? old,
        string backup, List<PackBackupFile> evidence, CancellationToken ct)
    {
        await RequireIdleAsync(ct);
        var id = mode == "regular" ? null : HellForgePack.PackId(mode);
        var current = await ModsAsync(ct);
        var found = current.Mods.SingleOrDefault(m => m.Id == id);
        if (id != null && old != null && (found == null || !await MatchesPackBackupAsync(found.Folder, evidence, ct)))
        {
            var source = Path.Combine(backup, "previous-pack");
            ValidateCosmeticBackup(source, id);
            if (!await MatchesPackBackupAsync(source, evidence, ct)) throw new InvalidDataException("The saved appearance backup changed; it was not imported.");
            bool restoreCleanedOnce = false;
            CheckPackCapacity(Path.Combine(Root, "mods"), evidence.Sum(f => f.Bytes), backup, ref restoreCleanedOnce, ct);
            await RequireIdleAsync(ct);
            var imported = await commands.RunAsync(["mod-add", "--file", Path.Combine(source, "manifest.json")], ct);
            if (Text(imported, "id") != id) throw new InvalidDataException("The restore returned a different pack ID.");
        }
        else if (id != null && old == null && found != null)
        {
            ValidateModFolder(found.Folder);
            await RequireIdleAsync(ct);
            await commands.RunAsync(["mod-remove", "--id", id], ct);
        }
        current = await ModsAsync(ct);
        foreach (var previous in before.Mods.Where(m => HellForgePack.IsStarWarsOverhaulMod(m) || m.Id == DifficultyId || HellForgePreset.IsModeCompanion(m.Id)))
        {
            var now = current.Mods.SingleOrDefault(m => m.Id == previous.Id) ?? throw new InvalidDataException("A previous preset mod is missing during recovery: " + previous.Name);
            if (now.Enabled == previous.Enabled) continue;
            await RequireIdleAsync(ct);
            await commands.RunAsync(["mod-switch", "--id", previous.Id, "--on", previous.Enabled ? "yes" : "no"], ct);
        }
        var status = await StatusAsync(ct);
        if (status.Multiplayer != beforeStatus.Multiplayer || status.LocalBackend != beforeStatus.LocalBackend)
        {
            await RequireIdleAsync(ct);
            await commands.RunAsync(["settings", "--multiplayer", beforeStatus.Multiplayer ? "yes" : "no", "--local-playfab", beforeStatus.LocalBackend ? "yes" : "no"], ct);
        }
        current = await ModsAsync(ct);
        if (PackState(current) != PackState(before)) throw new InvalidDataException("The mod catalog changed beyond this appearance operation; review it before continuing.");
        if (!current.Mods.Select(m => m.Id).SequenceEqual(before.Mods.Select(m => m.Id)))
        {
            await RequireIdleAsync(ct);
            await commands.RunAsync(["mod-order", "--ids", string.Join(",", before.Mods.Select(m => m.Id))], ct);
        }
        current = await ModsAsync(ct);
        status = await StatusAsync(ct);
        if (PackState(current) != PackState(before) || !current.Mods.Select(m => m.Id).SequenceEqual(before.Mods.Select(m => m.Id)) ||
            status.Multiplayer != beforeStatus.Multiplayer || status.LocalBackend != beforeStatus.LocalBackend)
            throw new InvalidDataException("The previous selection/settings could not be confirmed restored.");
        if (old != null)
        {
            var restored = (await ModsAsync(ct)).Mods.Single(m => m.Id == id);
            ValidateModFolder(restored.Folder);
            if (!await MatchesPackBackupAsync(restored.Folder, evidence, ct)) throw new InvalidDataException("The restored appearance differs from its saved files.");
        }
    }

    public async Task PrepareRegularAsync(CancellationToken ct = default)
    {
        await RequireIdleAsync(ct);
        using var operation = LockPackChange();
        RequireNoInterruptedPack();
        RequireCliQuiescent();
        using var batch = BeginCliBatch();
        var before = await ModsAsync(ct).ConfigureAwait(false);
        if (before.Problems.Length > 0) throw new InvalidDataException("Resolve HellForge's mod notices before changing modes: " + string.Join("; ", before.Problems));
        if (!before.Mods.Any(m => m.Id == DifficultyId)) throw new InvalidDataException("Finish official HellForge setup to install its bundled difficulty mod.");
        var backup = Path.Combine(CacheRoot, "backups", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-regular-" + Guid.NewGuid().ToString("N"));
        RejectAncestors(Path.Combine(backup, "before.json"));
        var settingsPath = Path.Combine(Root, "mods", "hellforge-mods.json");
        FileSafety.RejectLink(settingsPath);
        Directory.CreateDirectory(backup);
        if (File.Exists(settingsPath)) File.Copy(settingsPath, Path.Combine(backup, "hellforge-mods.json"));
        File.WriteAllText(Path.Combine(backup, "mods.json"), JsonSerializer.Serialize(before, HellForgePack.Json));
        var beforeStatus = await StatusAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(backup, "before.json"), JsonSerializer.Serialize(new { mods = before, status = beforeStatus, files = Array.Empty<PackBackupFile>() }, HellForgePack.Json), ct);
        var intent = await BeginPackIntentAsync(backup, "regular", ct);
        try
        {
            await ConfigureAsync(null, ct);
            var after = await ModsAsync(ct);
            RequireRegular(after);
            SavePackOutcome(backup, "applied");
            WritePackIntent(intent with { State = "applied" });
            PrunePackStorage(backup, after);
        }
        catch (HellForgeCommandOutcomeUnknownException) { SavePackOutcome(backup, "outcome-unknown"); throw; }
        catch (Exception error)
        {
            try
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                await RestorePackChangeAsync(before, beforeStatus, "regular", null, backup, [], recovery.Token);
                SavePackOutcome(backup, "restored");
                WritePackIntent(intent with { State = "restored" });
                PrunePackStorage(backup, before);
            }
            catch (Exception recoveryError) { throw new IOException("Regular mode failed and recovery needs attention. Backup: " + backup, new AggregateException(error, recoveryError)); }
            throw new IOException("Regular mode did not finish. The previous appearance selection was restored through HellForge. Backup: " + backup, error);
        }
    }

    async Task ConfigureAsync(HellForgePack.Selection? selection, CancellationToken ct)
    {
        var catalog = await ModsAsync(ct);
        var selectedId = selection == null ? null : HellForgePack.PackId(selection.Mode);
        var mode = selection?.Mode ?? "regular";
        var neededCompanions = HellForgePreset.RequiredCompanionsForMode(mode).ToHashSet(StringComparer.Ordinal);
        foreach (var mod in catalog.Mods.Where(HellForgePack.IsStarWarsOverhaulMod))
        {
            await RequireIdleAsync(ct);
            await commands.RunAsync(["mod-switch", "--id", mod.Id, "--on", mod.Id == selectedId ? "yes" : "no"], ct);
        }
        foreach (var mod in catalog.Mods.Where(m => HellForgePreset.IsModeCompanion(m.Id)))
        {
            var wantOn = neededCompanions.Contains(mod.Id);
            if (mod.Enabled == wantOn) continue;
            await RequireIdleAsync(ct);
            await commands.RunAsync(["mod-switch", "--id", mod.Id, "--on", wantOn ? "yes" : "no"], ct);
        }
        // The built-in difficulty adds 11 without removing 1–10. Let official setup supply it.
        if (catalog.Mods.Any(m => m.Id == DifficultyId))
        {
            await RequireIdleAsync(ct);
            await commands.RunAsync(["mod-switch", "--id", DifficultyId, "--on", "yes"], ct);
        }
        await RequireIdleAsync(ct);
        await commands.RunAsync(["settings", "--multiplayer", "yes", "--local-playfab", "yes", "--steam-udp", "yes"], ct);
    }

    public Task<JsonElement> PlayAsync(HellForgePack.Selection selection, CancellationToken ct = default, string? profile = null) =>
        PlayAsync(selection, ct, profile, progress: null, bytes: null, useVerifyCache: true);

    internal async Task<JsonElement> PlayAsync(HellForgePack.Selection selection, CancellationToken ct, string? profile,
        IProgress<string>? progress, IProgress<(long Done, long Total)>? bytes, bool useVerifyCache = true)
    {
        using var operation = LockPackChange();
        RequireCliQuiescent();
        var status = await StatusAsync(ct);
        RequireReady(status);
        if (!status.Multiplayer || !status.LocalBackend) throw new InvalidDataException("Enable Steam co-op and the local backend in HellForge settings.");
        var catalog = await ModsAsync(ct);
        var selected = RequireSelected(catalog, selection);
        bool ultrakillActive = IsUltrakillHostTheaterActive();
        if (HellForgePreset.Problem(catalog, checkOrder: true, ultrakillTheaterActive: ultrakillActive) is { } preset) throw new InvalidDataException(preset);
        if (HellForgePreset.ModeCompanionProblem(catalog, selection.Mode) is { } companionIssue) throw new InvalidDataException(companionIssue);
        if ((await MissingGameplayAsync(selection.Mode, ct)).Any(a => !a.Optional || HellForgePreset.RequiredCompanionsForMode(selection.Mode).Contains(a.Id, StringComparer.Ordinal))) throw new InvalidDataException("Required gameplay or mode companion content changed. Use Prepare Star Wars pack to verify and repair the preset before launching.");
        if (!catalog.Mods.Any(m => m.Id == DifficultyId && m.Enabled)) throw new InvalidDataException("Open HellForge setup to install its built-in difficulty 11, then enable it on the Mods page.");
        ValidateModFolder(selected.Folder);
        progress?.Report($"Verifying {Pack.FormatBytes(selection.Files.Sum(f => f.Size))} of Star Wars files…");
        var cachePath = useVerifyCache ? Path.Combine(CacheRoot, "verify-cache.json") : null;
        await HellForgePack.VerifyForLaunchAsync(selected.Folder, selection, cachePath, bytes, ct);
        progress?.Report("Starting HellForge session…");
        return await StartAsync(ct, profile);
    }

    public async Task<JsonElement> PlayRegularAsync(CancellationToken ct = default, string? profile = null)
    {
        using var operation = LockPackChange();
        RequireCliQuiescent();
        var status = await StatusAsync(ct);
        RequireReady(status);
        if (!status.Multiplayer || !status.LocalBackend) throw new InvalidDataException("Enable Steam co-op and the local backend in HellForge settings.");
        var catalog = await ModsAsync(ct);
        RequireRegular(catalog);
        bool ultrakillActive = IsUltrakillHostTheaterActive();
        if (HellForgePreset.Problem(catalog, checkOrder: true, ultrakillTheaterActive: ultrakillActive) is { } preset) throw new InvalidDataException(preset);
        if (HellForgePreset.ModeCompanionProblem(catalog, "regular") is { } companionIssue) throw new InvalidDataException(companionIssue);
        if ((await MissingGameplayAsync(ct)).Any(a => !a.Optional)) throw new InvalidDataException("Required gameplay content changed. Use Prepare to verify and repair the preset before launching.");
        if (!catalog.Mods.Any(m => m.Id == DifficultyId && m.Enabled)) throw new InvalidDataException("Enable HellForge's bundled difficulty mod on its Mods page.");
        return await StartAsync(ct, profile);
    }

    async Task<JsonElement> StartAsync(CancellationToken ct, string? profile)
    {
        ct.ThrowIfCancellationRequested();
        using var account = new HellForgeAccount(Root).Lease();
        RequireNoInterruptedPack();
        if (profile != null)
        {
            FileSafety.ValidateLeaf(profile);
            if (profile.Length > 64 || profile.StartsWith('-')) throw new InvalidDataException("Invalid local profile name.");
        }
        RequireReady(await StatusAsync(ct));
        var started = await commands.RunAsync(profile == null ? ["play"] : ["play", "--profile", profile], ct);
        if (OptionalText(started, "event") != "started") throw new InvalidDataException("HellForge did not acknowledge starting a session. Check its logs before retrying.");
        return started;
    }

    public static void RequireRegular(HellForgeMods catalog)
    {
        if (catalog.Problems.Length > 0) throw new InvalidDataException("HellForge mod notices: " + string.Join("; ", catalog.Problems));
        if (ModConflict(catalog) is { } conflict) throw new InvalidDataException(conflict);
        if (catalog.Mods.Any(m => m.Enabled && HellForgePack.IsStarWarsOverhaulMod(m)))
            throw new InvalidDataException("Apply Regular mode to disable the Star Wars packs before playing.");
    }

    public static HellForgeMod RequireSelected(HellForgeMods catalog, HellForgePack.Selection selection)
    {
        if (catalog.Problems.Length > 0) throw new InvalidDataException("HellForge mod notices: " + string.Join("; ", catalog.Problems));
        if (ModConflict(catalog) is { } conflict) throw new InvalidDataException(conflict);
        var active = catalog.Mods.Where(m => m.Enabled && HellForgePack.IsStarWarsOverhaulMod(m)).ToList();
        if (active.Count != 1 || active[0].Id != HellForgePack.PackId(selection.Mode)) throw new InvalidDataException("Prepare the selected Star Wars mode before playing. Enable only one Star Wars pack.");
        return active[0];
    }
    public static string? ModConflict(HellForgeMods catalog)
    {
        bool Enabled(string id) => catalog.Mods.Any(m => m.Enabled && m.Id == id);
        return Enabled("skyeshade.vanilla-plus") && Enabled("pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91")
            ? "Vanilla Plus and Reinforcement Beacons Fixed both change beacon landing. Disable one in HellForge's Mods page before launching." : null;
    }
    void ValidateModFolder(string folder)
    {
        var mods = Path.Combine(Root, "mods") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(folder).StartsWith(mods, RecoveryPathComparison)) throw new InvalidDataException("HellForge pack folder is outside this installation.");
        for (var path = new DirectoryInfo(folder); path != null && path.FullName.Length >= Root.Length; path = path.Parent) FileSafety.RejectLink(path.FullName);
    }
    public string CacheRoot
    {
        get
        {
            var cache = Path.Combine(Path.GetDirectoryName(Root) ?? throw new InvalidDataException("Choose a HellForge folder, not a drive root."), "Clonedivers-HellForge-cache");
            FileSafety.RejectLink(cache);
            return cache;
        }
    }
    public static string? Detect() => HellForgePreferences.CustomPreferencesPath() != null ? null : Detect(null);

    internal static string? Detect(IEnumerable<string>? candidates)
    {
        var roots = candidates ?? [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "HellForge"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HellForge"),
            @"D:\HellForge"
        ];
        foreach (var root in roots)
            try
            {
                var validated = HellForgeCommands.ValidateRoot(root);
                if (IsSupportedInstallMetadata(validated)) return validated;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or KeyNotFoundException) { }
        return null;
    }

    static bool IsSupportedInstallMetadata(string root)
    {
        var marker = Path.Combine(root, "hellforge-install.json");
        FileSafety.RejectLink(marker);
        using (var doc = JsonDocument.Parse(File.ReadAllText(marker)))
        {
            if (OptionalText(doc.RootElement, "buildId") is { Length: > 0 } build && build != HellForgePack.SupportedBuild)
                return false;
        }
        var appBuild = Path.Combine(root, "app", "app-build.json");
        if (File.Exists(appBuild))
        {
            FileSafety.RejectLink(appBuild);
            if (new FileInfo(appBuild).Length > 64 * 1024) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(appBuild));
            var version = OptionalText(doc.RootElement, "version");
            var engine = OptionalText(doc.RootElement, "engine");
            return HellForgeRelease.Supports(version) && engine == HellForgeRelease.Engine ||
                version != null && (version == "0.3.0-beta" || System.Text.RegularExpressions.Regex.IsMatch(version, @"^0\.3\.0-beta \([0-9a-f]{7,40}\)$")) && engine == "0.1.41";
        }
        var engineJson = Path.Combine(root, "app", "engine.json");
        if (File.Exists(engineJson))
        {
            FileSafety.RejectLink(engineJson);
            if (new FileInfo(engineJson).Length > 1024 * 1024) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(engineJson));
            var version = OptionalText(doc.RootElement, "version");
            var build = OptionalText(doc.RootElement, "build");
            return version is HellForgeRelease.Engine or "0.1.41" && (build == null || build == HellForgePack.SupportedBuild);
        }
        return false;
    }
    static string Text(JsonElement json, string key) => OptionalText(json, key) is { Length: > 0 } value ? value : throw new InvalidDataException($"HellForge response lacks {key}.");
    static string? OptionalText(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static bool Bool(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? value.GetBoolean() : throw new InvalidDataException($"HellForge response lacks {key}.");
}
