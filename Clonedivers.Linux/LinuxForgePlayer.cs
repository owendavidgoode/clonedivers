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
    public int Format { get; set; } = 1;
    public string? InstallRoot { get; set; }
    public string Mode { get; set; } = "clonedivers";
    public bool DeltaSquad { get; set; } = true;
    public string Texture { get; set; } = "full";
    public bool Droids { get; set; } = true;
    public bool Covenant { get; set; }
    internal static string PathName => Path.Combine(PlatformPaths.ConfigDirectory, "forge.json");
    internal void Validate()
    {
        if (Format != 1 || Mode is not ("regular" or "clonedivers" or "empire") || Texture is not ("full" or "lighter"))
            throw new InvalidDataException("Unsupported Forge preferences; restore forge.json from backup.");
    }
    internal static LinuxForgePreferences Load(string? file = null)
    {
        file ??= PathName; FileSafety.RejectLink(file);
        if (!File.Exists(file))
        {
            var legacy = LegacyAppearance.ReadFor(file);
            return new() { Mode = legacy.Options.GetValueOrDefault("empire") ? "empire" : "clonedivers",
                DeltaSquad = legacy.Options.GetValueOrDefault("commandos", true), Droids = legacy.Options.GetValueOrDefault("droids", true),
                Covenant = legacy.Options.GetValueOrDefault("covenant"), Texture = legacy.TextureProfile == "lighter" ? "lighter" : "full" };
        }
        if (new FileInfo(file).Length > 65536) throw new InvalidDataException("Forge preferences are oversized.");
        var value = JsonSerializer.Deserialize<LinuxForgePreferences>(File.ReadAllText(file), HellForgePack.Json) ?? throw new InvalidDataException("Empty Forge preferences.");
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
        "--droids", Droids ? "yes" : "no", "--covenant", Covenant ? "yes" : "no"];
    internal HellForgePack.Selection PackSelection() => HellForgePack.PinnedSelection(Mode == "empire" ? "empire" : DeltaSquad ? "commandos" : "clonedivers", Texture,
        new Dictionary<string, bool> { ["droids"] = Droids, ["covenant"] = Covenant });
}
internal sealed record ForgeAccountView(bool Captured, bool Imported, string Message);
internal sealed record ForgePlayerState(HellForgeStatus Status, bool Ready, string Detail, ForgeAccountView? Account, HellForgeMods? Mods);

internal sealed class LinuxForgePlayer(IForgeConnection connection, LinuxForgePreferences preferences) : IAsyncDisposable
{
    internal LinuxForgePreferences Preferences { get; } = preferences;
    internal ForgePlayerState? State { get; private set; }
    internal bool OwnsSession { get; private set; }
    // A faulted bridge can neither confirm nor end a session; closing stdin hands cleanup to the bridge's EOF path, so it must not pin the window.
    internal bool Disconnected => connection.Faulted;
    internal bool SessionActive => !Disconnected && (OwnsSession || State?.Status.Running == true);
    internal string? Error { get; private set; }
    internal bool CanLaunch => !SessionActive && !Disconnected && Error == null && State?.Ready == true;

    internal static async Task<LinuxForgePlayer> ConnectAsync(LinuxForgePreferences preferences, string resourceDirectory, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Forge sessions require Linux or the Windows launcher; previews never connect.");
        if (Game.IsRunning()) throw new InvalidOperationException("Close the running HD2 session before starting Forge's dedicated namespace.");
        if (LinuxForgeBootstrap.MissingTools().Length != 0) throw new InvalidOperationException("Linux components needed: " + string.Join(", ", LinuxForgeBootstrap.MissingTools()) + ". Use Set up / update Forge to install them with desktop approval.");
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
        var transport = await LinuxForgeTransport.StartAsync(plan, ct);
        var player = new LinuxForgePlayer(transport, preferences);
        try { await player.ApplySelectionAsync(ct); return player; }
        catch { await player.DisposeAsync(); throw; }
    }
    internal async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await connection.RunAsync(["player-status"], ct);
            State = json.Deserialize<ForgePlayerState>(HellForgePack.Json) ?? throw new InvalidDataException("Missing player status.");
            if (!State.Status.Running) OwnsSession = false;
            Error = null;
        }
        catch (Exception error) { Error = error.Message; throw; }
    }
    internal async Task ApplySelectionAsync(CancellationToken ct = default)
    {
        Preferences.Validate();
        if (SessionActive) throw new InvalidOperationException("End the current Forge session before changing its selection.");
        State = null;
        try { await SelectAsync(ct); await RefreshAsync(ct); }
        catch (Exception error) { Error = error.Message; throw; }
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
        catch (Exception error) { Error = error.Message; throw; }
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
        catch (Exception error) { Error = error.Message; throw; }
    }
    internal async Task ImportAsync(CancellationToken ct = default)
    {
        try
        {
        if (SessionActive) throw new InvalidOperationException("End the current session before importing your account.");
        await connection.RunAsync(["account-import"], ct);
        await RefreshAsync(ct);
        }
        catch (Exception error) { Error = error.Message; throw; }
    }
    internal async Task RecoverAppearanceAsync(CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before recovering the appearance.");
        await connection.RunAsync(["recover-pack"], ct);
        await RefreshAsync(ct);
    }
    internal async Task InstallGameplayAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (SessionActive || Game.IsRunning()) throw new InvalidOperationException("End HD2 before installing the required gameplay preset.");
        var missing = (await connection.RunAsync(["gameplay-status"], ct)).Deserialize<List<GameplayArtifact>>(HellForgePack.Json)
            ?? throw new InvalidDataException("Forge returned no gameplay installation plan.");
        var pinned = HellForgeGameplayCatalog.Pinned;
        foreach (var requested in missing)
            if (!pinned.Artifacts.Contains(requested)) throw new InvalidDataException("Bridge requested an artifact outside the pinned preset.");
        var root = Preferences.InstallRoot ?? throw new InvalidOperationException("Choose the Forge installation first.");
        var cache = Path.Combine(Path.GetDirectoryName(root)!, "Clonedivers-HellForge-cache", "gameplay", pinned.Id);
        var windows = connection.NativePaths ? cache : WindowsPath(root, cache);
        // HTTPS runs here in the native frontend, never in the isolated Wine bridge.
        await HellForgeGameplayCatalog.StageAsync(cache, missing, progress, ct);
        if (SessionActive || Game.IsRunning()) throw new InvalidOperationException("HD2 started while downloading; keep the verified files and retry after closing it.");
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
        await connection.RunAsync(["select-account"], ct);
        await connection.RunAsync(["open-manager"], ct);
    }
    internal async Task<HellForgePack.Plan> PlanAppearanceAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before preparing this mode.");
        if (Preferences.Mode == "regular") throw new InvalidOperationException("Regular needs no appearance download.");
        var root = Preferences.InstallRoot ?? throw new InvalidOperationException("Choose a Forge installation.");
        var cache = Path.Combine(Path.GetDirectoryName(root)!, "Clonedivers-HellForge-cache", "exports");
        FileSafety.RejectLink(cache);
        var selection = Preferences.PackSelection();
        var exports = Directory.Exists(cache) ? new DirectoryInfo(cache).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName).ToList() : [];
        if (await FindExportAsync(exports, selection, progress, ct) is { } prepared) return prepared;
        return await HellForgePack.PlanAsync(selection, Path.Combine(cache, "linux-" + Guid.NewGuid().ToString("N")), exports.Prepend(Path.Combine(root, "mods")), progress, ct);
    }
    /// <summary>Like Windows, reuse a verified earlier export instead of writing another full copy; its plan needs no download or export.</summary>
    internal static async Task<HellForgePack.Plan?> FindExportAsync(IEnumerable<string> exports, HellForgePack.Selection selection, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        foreach (var export in exports)
            try
            {
                progress?.Report("Checking previously prepared pack files…");
                await HellForgePack.VerifyAsync(export, selection, ct);
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
    internal async Task PrepareAppearanceAsync(HellForgePack.Plan? plan, bool allowDownloads, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before applying this mode.");
        if (Preferences.Mode == "regular") { await SelectAsync(ct); await connection.RunAsync(["prepare-regular"], ct); }
        else
        {
            ArgumentNullException.ThrowIfNull(plan);
            var expected = Preferences.PackSelection();
            if (plan.Selection.Mode != expected.Mode || plan.Selection.Texture != expected.Texture || plan.Selection.ManifestSha256 != expected.ManifestSha256 ||
                plan.Selection.Options.Count != expected.Options.Count || expected.Options.Any(pair => !plan.Selection.Options.TryGetValue(pair.Key, out var on) || on != pair.Value))
                throw new InvalidDataException("The selected mode/options changed after planning. Prepare the selection again.");
            var path = connection.NativePaths ? plan.Output : WindowsPath(Preferences.InstallRoot!, plan.Output);
            // This code runs in the native frontend, outside the isolated Wine namespace.
            if (Directory.Exists(plan.Output)) await HellForgePack.VerifyMetadataAsync(plan.Output, expected, ct); // reused export; the bridge re-hashes before import
            else await HellForgePack.ExportAsync(plan, allowDownloads, progress, ct);
            await SelectAsync(ct);
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
