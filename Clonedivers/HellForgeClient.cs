using System.Diagnostics;
using System.Text.Json;

namespace Clonedivers;

public sealed class HellForgePreferences
{
    public int Format { get; set; } = 1;
    public string? InstallRoot { get; set; }
    public string Mode { get; set; } = "clonedivers";
    // Read old launcher preferences, but only the imported account is offered to players.
    public string Loadout { get; set; } = "account";
    public bool DeltaSquad { get; set; } = true;
    public string Texture { get; set; } = "full";
    public bool Droids { get; set; } = true;
    public bool Covenant { get; set; }
    public string? PreparedPack { get; set; }
    public string? AppliedSelection { get; set; }
    public bool IsRegular => Mode == "regular";
    public string PackMode => IsRegular ? "regular" : Mode == "empire" ? "empire" : DeltaSquad ? "commandos" : "clonedivers";
    public string SelectionKey => IsRegular ? $"regular/{HellForgePack.SupportedBuild}" :
        $"r19/{HellForgePack.SupportedBuild}/{PackMode}/{Texture}/{Droids}/{Covenant}";
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers", "hellforge.json");

    public static HellForgePreferences Load(string? path = null)
    {
        path ??= FilePath;
        FileSafety.RejectLink(path);
        if (!File.Exists(path))
        {
            var legacy = LegacyAppearance.ReadFor(path);
            return new() { Mode = legacy.Options.GetValueOrDefault("empire") ? "empire" : "clonedivers",
                DeltaSquad = legacy.Options.GetValueOrDefault("commandos", true), Droids = legacy.Options.GetValueOrDefault("droids", true),
                Covenant = legacy.Options.GetValueOrDefault("covenant"), Texture = legacy.TextureProfile == "lighter" ? "lighter" : "full" };
        }
        if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("HellForge preferences are oversized.");
        var preferences = JsonSerializer.Deserialize<HellForgePreferences>(File.ReadAllText(path), HellForgePack.Json)
            ?? throw new InvalidDataException("HellForge preferences are empty.");
        preferences.Validate();
        return preferences;
    }
    public void Validate()
    {
        if (Format != 1 || Mode is not ("regular" or "clonedivers" or "empire") ||
            Loadout is not ("account" or "armory" or "progression") || Texture is not ("full" or "lighter"))
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
    public HellForgePack.Selection Selection() => IsRegular
        ? throw new InvalidOperationException("Regular HellForge does not use a Star Wars pack.")
        : HellForgePack.PinnedSelection(PackMode, Texture,
            new Dictionary<string, bool> { ["droids"] = Droids, ["covenant"] = Covenant });
}

public interface IHellForgeCommands
{
    Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default);
}

/// <summary>The official CLI manages sessions, mods and settings; no game data, engine or networking writes.</summary>
public sealed class HellForgeCommands : IHellForgeCommands
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> UncertainRoots = new(StringComparer.OrdinalIgnoreCase);
    public string Root { get; }
    public HellForgeCommands(string root) => Root = ValidateRoot(root);
    public static string ValidateRoot(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent)
        {
            FileSafety.RejectLink(directory.FullName);
            if (directory.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose the separate HellForge folder, outside Steam.");
        }
        var marker = FileSafety.Child(root, "hellforge-install.json");
        if (!File.Exists(marker) || new FileInfo(marker).Length > 64 * 1024) throw new InvalidDataException("Choose the HellForge installation folder containing hellforge-install.json.");
        using var doc = JsonDocument.Parse(File.ReadAllText(marker));
        if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported HellForge installation format.");
        foreach (var relative in new[] { "app", "app/cli", "app/cli/HellForge.Cli.exe", "HellForge.exe", "mods" })
            FileSafety.RejectLink(Path.Combine(root, relative));
        if (!File.Exists(Path.Combine(root, "app", "cli", "HellForge.Cli.exe")) || !File.Exists(Path.Combine(root, "HellForge.exe")))
            throw new InvalidDataException("HellForge programs are missing. Repair it with the official installer.");
        return root;
    }
    public async Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (UncertainRoots.ContainsKey(Root) && !ReadAfterUncertain(args)) throw new HellForgeCommandOutcomeUnknownException("The previous CLI could not be confirmed stopped. End session and diagnostics remain available. Confirm the CLI has stopped and review the mod state, then restart this launcher before retrying changes or launch.");
        ValidateRoot(Root);
        var start = new ProcessStartInfo(Path.Combine(Root, "app", "cli", "HellForge.Cli.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Root
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var command = args.FirstOrDefault() ?? "";
        try { return await TimedAsync(command, TimeSpan.FromMinutes(command == "mod-add" ? 10 : 2), t => ReadJsonProcessAsync(start, command == "play", t), ct); }
        catch (HellForgeCommandOutcomeUnknownException) { UncertainRoots[Root] = true; throw; }
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

    public async Task<HellForgeStatus> StatusAsync(CancellationToken ct = default)
    {
        var json = await commands.RunAsync(["status"], ct);
        var returned = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Text(json, "install")));
        if (!returned.Equals(Root, RecoveryPathComparison)) throw new InvalidDataException("HellForge reported a different installation.");
        var ready = json.GetProperty("ready"); var session = json.GetProperty("session"); var settings = json.GetProperty("settings");
        var running = Bool(session, "running");
        var failure = OptionalText(session, "lastFailure");
        string? notice = null;
        if (!running && failure == null) (failure, notice) = CompletedGameDiagnostic(OptionalText(session, "lastRun"), Text(json, "gameBuild"));
        return new(returned, Text(json, "hellforge"), Text(json, "gameBuild"), Bool(ready, "gameCopy"), Bool(ready, "sessionRunner"),
            running, Bool(settings, "multiplayer"), Bool(settings, "localPlayFab"), OptionalText(session, "lastStatus"), failure, notice, InterruptedGameplayProblem() ?? InterruptedPackProblem(), PinnedProblem(json, LinuxNamespace),
            OptionalText(json, "engine"), json.TryGetProperty("modSdk", out var sdk) && sdk.ValueKind == JsonValueKind.Number && sdk.TryGetInt32(out var number) ? number : null);
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
            // "observed" describes runner completion, even when the game crashed with exit1.
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("completedUtc", out var completed) &&
                completed.ValueKind == JsonValueKind.String && json.TryGetProperty("exitCode", out var code) &&
                code.TryGetInt32(out var exit) && exit != 0) {
                // The pinned upstream debugging guide documents this exact game's quit-time fault.
                // Keep it visible, without treating it as evidence of a mission/mod failure.
                if (exit == 1 && build == "25480438" && HasDocumentedQuitCrash(folder))
                    return (null, "The last exit matches HellForge's documented quit-time crash (EXE+0x626296, code 1). This does not establish mission stability; details remain in the session logs.");
                return ($"The last game exited unexpectedly (code {exit}). Check HellForge's session logs before retrying.", null);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            // Missing, pruned or partially written diagnostics must not prevent status checks.
        }
        return (null, null);
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
    // the profile's receipt/journal/profile.json and its restore/import backups (plus the Steam user, added by the caller).
    internal string AccountFingerprint()
    {
        var profile = Path.Combine(Root, "profiles", HellForgeAccount.Profile);
        return string.Join("\n", new[] { Path.Combine(Root, "account-snapshot", HellForgeAccount.Profile), profile, Path.Combine(profile, "backups") }
            .Where(Directory.Exists).SelectMany(folder => new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0 }))
            .Select(f => $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}").OrderBy(p => p, StringComparer.Ordinal));
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

    public async Task ImportAccountAsync(CancellationToken ct = default)
    {
        await SelectAccountAsync(ct);
        await new HellForgeAccount(Root).ImportAsync(ct);
    }

    public async Task InstallPackAsync(string folder, HellForgePack.Selection selection, CancellationToken ct = default)
    {
        await RequireIdleAsync(ct);
        using var operation = LockPackChange();
        RequireNoInterruptedPack();
        RequireCliQuiescent();
        await HellForgePack.VerifyAsync(folder, selection, ct);
        var before = await ModsAsync(ct);
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
        CheckPackCapacity(backup, checked(sources.Sum(f => f.Bytes) + replacementBytes));
        CheckPackCapacity(Path.Combine(Root, "mods"), replacementBytes);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(backup);
        if (File.Exists(settingsPath)) await HellForgePack.CopyLocalAsync(settingsPath, Path.Combine(backup, "hellforge-mods.json"), new FileInfo(settingsPath).Length, ct);
        var evidence = new List<PackBackupFile>();
        foreach (var file in sources)
        {
            var target = Path.Combine(backup, "previous-pack", file.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await HellForgePack.CopyLocalAsync(file.Source, target, file.Bytes, ct);
            evidence.Add(new(file.Relative, file.Bytes, await HellForgePack.HashAsync(target, ct)));
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
            var imported = await commands.RunAsync(["mod-add", "--file", Path.Combine(folder, "manifest.json")], ct);
            if (Text(imported, "id") != HellForgePack.PackId(selection.Mode)) throw new InvalidDataException("HellForge imported an unexpected pack ID.");
            await ConfigureAsync(selection, ct);
            var after = await ModsAsync(ct);
            var selected = RequireSelected(after, selection);
            ValidateModFolder(selected.Folder);
            await HellForgePack.VerifyAsync(selected.Folder, selection, ct);
            SavePackOutcome(backup, "applied");
            WritePackIntent(intent with { State = "applied" });
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
    void CheckPackCapacity(string output, long bytes)
    {
        var required = checked(bytes + HellForgePack.ExportReserve);
        if (AvailablePackBytes(output) is long free && free < required) throw new IOException($"Not enough space for the appearance backup and replacement reserve: {Pack.FormatBytes(required)} needed, {Pack.FormatBytes(Math.Max(0, free))} available.");
    }
    static string PackState(HellForgeMods mods) => JsonSerializer.Serialize(mods.Mods.Select(m => new { m.Id, m.Enabled, m.Name, m.Version, m.Options }).OrderBy(m => m.Id, StringComparer.Ordinal));
    static void SavePackOutcome(string backup, string outcome)
    {
        try { File.WriteAllText(Path.Combine(backup, "outcome.json"), JsonSerializer.Serialize(new { outcome, utc = DateTimeOffset.UtcNow })); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
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
            CheckPackCapacity(Path.Combine(Root, "mods"), evidence.Sum(f => f.Bytes));
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
        foreach (var previous in before.Mods.Where(m => HellForgePack.IsOurPack(m.Id) || m.Id == DifficultyId))
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
        var before = await ModsAsync(ct);
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
            RequireRegular(await ModsAsync(ct));
            SavePackOutcome(backup, "applied");
            WritePackIntent(intent with { State = "applied" });
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
            }
            catch (Exception recoveryError) { throw new IOException("Regular mode failed and recovery needs attention. Backup: " + backup, new AggregateException(error, recoveryError)); }
            throw new IOException("Regular mode did not finish. The previous appearance selection was restored through HellForge. Backup: " + backup, error);
        }
    }

    async Task ConfigureAsync(HellForgePack.Selection? selection, CancellationToken ct)
    {
        var catalog = await ModsAsync(ct);
        var selectedId = selection == null ? null : HellForgePack.PackId(selection.Mode);
        foreach (var mod in catalog.Mods.Where(m => HellForgePack.IsOurPack(m.Id)))
        {
            await RequireIdleAsync(ct);
            await commands.RunAsync(["mod-switch", "--id", mod.Id, "--on", mod.Id == selectedId ? "yes" : "no"], ct);
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

    public async Task<JsonElement> PlayAsync(HellForgePack.Selection selection, CancellationToken ct = default, string? profile = null)
    {
        using var operation = LockPackChange();
        RequireCliQuiescent();
        var status = await StatusAsync(ct);
        RequireReady(status);
        if (!status.Multiplayer || !status.LocalBackend) throw new InvalidDataException("Enable Steam co-op and the local backend in HellForge settings.");
        var catalog = await ModsAsync(ct);
        var selected = RequireSelected(catalog, selection);
        if (HellForgePreset.Problem(catalog, checkOrder: true) is { } preset) throw new InvalidDataException(preset);
        if ((await MissingGameplayAsync(ct)).Count != 0) throw new InvalidDataException("Required gameplay content changed. Use Prepare Star Wars pack to verify and repair the preset before launching.");
        if (!catalog.Mods.Any(m => m.Id == DifficultyId && m.Enabled)) throw new InvalidDataException("Open HellForge setup to install its built-in difficulty 11, then enable it on the Mods page.");
        ValidateModFolder(selected.Folder);
        await HellForgePack.VerifyAsync(selected.Folder, selection, ct);
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
        if (HellForgePreset.Problem(catalog, checkOrder: true) is { } preset) throw new InvalidDataException(preset);
        if ((await MissingGameplayAsync(ct)).Count != 0) throw new InvalidDataException("Required gameplay content changed. Use Prepare to verify and repair the preset before launching.");
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
        if (catalog.Mods.Any(m => m.Enabled && HellForgePack.IsOurPack(m.Id)))
            throw new InvalidDataException("Apply Regular mode to disable the Star Wars packs before playing.");
    }

    public static HellForgeMod RequireSelected(HellForgeMods catalog, HellForgePack.Selection selection)
    {
        if (catalog.Problems.Length > 0) throw new InvalidDataException("HellForge mod notices: " + string.Join("; ", catalog.Problems));
        if (ModConflict(catalog) is { } conflict) throw new InvalidDataException(conflict);
        var active = catalog.Mods.Where(m => m.Enabled && HellForgePack.IsOurPack(m.Id)).ToList();
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
    public static string? Detect()
    {
        foreach (var root in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "HellForge"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HellForge"), @"D:\HellForge" })
            try { return HellForgeCommands.ValidateRoot(root); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or KeyNotFoundException) { }
        return null;
    }
    static string Text(JsonElement json, string key) => OptionalText(json, key) is { Length: > 0 } value ? value : throw new InvalidDataException($"HellForge response lacks {key}.");
    static string? OptionalText(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static bool Bool(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? value.GetBoolean() : throw new InvalidDataException($"HellForge response lacks {key}.");
}
