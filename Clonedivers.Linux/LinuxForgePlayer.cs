using System.Text.Json;

namespace Clonedivers;

internal interface IForgeConnection : IAsyncDisposable
{
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
    internal bool SessionActive => OwnsSession || State?.Status.Running == true;
    internal string? Error { get; private set; }
    internal bool CanLaunch => !SessionActive && Error == null && State?.Ready == true;

    internal static async Task<LinuxForgePlayer> ConnectAsync(LinuxForgePreferences preferences, string resourceDirectory, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Forge sessions require Linux or the Windows launcher; previews never connect.");
        if (Game.IsRunning()) throw new InvalidOperationException("Close the running HD2 session before starting Forge's dedicated namespace.");
        preferences.Validate();
        var root = preferences.InstallRoot ?? throw new InvalidOperationException("Choose the separate HellForge installation inside its dedicated prefix C: drive.");
        var metadata = Path.Combine(resourceDirectory, "companion.json"); FileSafety.RejectLink(resourceDirectory); FileSafety.RejectLink(metadata);
        if (!File.Exists(metadata) || new FileInfo(metadata).Length > 65536) throw new FileNotFoundException("Forge companion resources are missing. Install the complete Clonedivers package, including its forge folder.");
        using var doc = JsonDocument.Parse(File.ReadAllText(metadata));
        if (doc.RootElement.GetProperty("format").GetInt32() != 1 || doc.RootElement.GetProperty("gameBuild").GetString() != LinuxForgePlan.GameBuild ||
            doc.RootElement.GetProperty("bridgeVersion").GetString() != LinuxForgePlan.BridgeVersion)
            throw new InvalidDataException("Forge companion targets a different client or game build. Install the complete matching Clonedivers package.");
        var plan = LinuxForgePlan.Stage(root, Path.Combine(resourceDirectory, LinuxForgePlan.BridgeName), doc.RootElement.GetProperty("bridgeSha256").GetString()!, Path.Combine(resourceDirectory, "linux"));
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
        try { await connection.RunAsync(Preferences.SelectionCommand(), ct); await RefreshAsync(ct); }
        catch (Exception error) { Error = error.Message; throw; }
    }
    internal async Task LaunchAsync(CancellationToken ct = default)
    {
        try
        {
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
    internal async Task OpenManagerAsync(CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before changing setup or mods.");
        await connection.RunAsync(["open-manager"], ct);
        await RefreshAsync(ct);
    }
    internal async Task<HellForgePack.Plan> PlanAppearanceAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before preparing this mode.");
        if (Preferences.Mode == "regular") throw new InvalidOperationException("Regular needs no appearance download.");
        var root = Preferences.InstallRoot ?? throw new InvalidOperationException("Choose a Forge installation.");
        var cache = Path.Combine(Path.GetDirectoryName(root)!, "Clonedivers-HellForge-cache", "exports");
        FileSafety.RejectLink(cache);
        var sources = new List<string> { Path.Combine(root, "mods") };
        if (Directory.Exists(cache)) sources.AddRange(new DirectoryInfo(cache).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName));
        return await HellForgePack.PlanAsync(Preferences.PackSelection(), Path.Combine(cache, "linux-" + Guid.NewGuid().ToString("N")), sources, progress, ct);
    }
    internal async Task PrepareAppearanceAsync(HellForgePack.Plan? plan, bool allowDownloads, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (SessionActive) throw new InvalidOperationException("End the current session before applying this mode.");
        if (Preferences.Mode == "regular") await connection.RunAsync(["prepare-regular"], ct);
        else
        {
            ArgumentNullException.ThrowIfNull(plan);
            var expected = Preferences.PackSelection();
            if (plan.Selection.Mode != expected.Mode || plan.Selection.Texture != expected.Texture || plan.Selection.ManifestSha256 != expected.ManifestSha256 ||
                plan.Selection.Options.Count != expected.Options.Count || expected.Options.Any(pair => !plan.Selection.Options.TryGetValue(pair.Key, out var on) || on != pair.Value))
                throw new InvalidDataException("The selected mode/options changed after planning. Prepare the selection again.");
            var path = WindowsPath(Preferences.InstallRoot!, plan.Output);
            // This code runs in the native frontend, outside the isolated Wine namespace.
            await HellForgePack.ExportAsync(plan, allowDownloads, progress, ct);
            await connection.RunAsync(["prepare-pack", "--file", path], ct);
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
        if (OwnsSession) await EndSessionAsync();
        await connection.DisposeAsync();
    }
}
