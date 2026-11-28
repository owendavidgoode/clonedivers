using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Clonedivers;

namespace Clonedivers.ForgeBridge;

/// <summary>Reuse the verified Forge account/preset backend with the installed official native tools.</summary>
internal sealed class PlayerCommands
{
    readonly string root;
    readonly IHellForgeCommands official;
    readonly HellForgeClient client;
    readonly HellForgeAccount account;
    readonly HellForgePreferences selection = new();
    readonly Action? customOpenManager;
    readonly Func<CancellationToken, Task<int?>> reviewWarbonds;
    internal Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;
    internal Func<string?> CaptureBlockCheck { get; init; } = HellForgeAccount.CaptureBlock;
    internal Func<string?> SteamUserProbe { get; init; } = () => LinuxSteamUser();
    internal Func<CancellationToken, Task<string>>? EnsureAccount { get; init; }
    Process? managedWindow;
    string? managerOpenFingerprint;
    HellForgeAccountState? lastAccount;
    string? lastAccountFingerprint;
    DateTime lastAccountReview;
    string? warbondMessage;
    bool warbondsPending;
    bool companionSyncPending = true;
    internal PlayerCommands(string root, IHellForgeCommands official, Action? openManager = null,
        Func<IReadOnlyList<string>, CancellationToken, Task<string>>? backend = null,
        Func<CancellationToken, Task<int>>? reviewWarbonds = null,
        Func<CancellationToken, Task<int?>>? reviewWarbondsWithStatus = null)
    {
        if (reviewWarbonds != null && reviewWarbondsWithStatus != null)
            throw new ArgumentException("Specify at most one Warbond review callback.");
        this.root = root;
        this.official = official;
        client = new(root, official) { LinuxNamespace = true };
        account = new(root, backend);
        this.reviewWarbonds = reviewWarbondsWithStatus
            ?? (reviewWarbonds != null ? async ct => (int?)await reviewWarbonds(ct) : TryOpenOwnedWarbondsOrDeferAsync);
        customOpenManager = openManager;
    }
    bool OpenOrReuseManager()
    {
        try { managerOpenFingerprint = client.AccountFingerprint(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { managerOpenFingerprint = null; }
        if (customOpenManager != null) { customOpenManager(); return false; }
        if (managedWindow != null)
        {
            try { if (!managedWindow.HasExited) return true; }
            catch (InvalidOperationException) { }
            managedWindow.Dispose();
            managedWindow = null;
        }
        if (HellForgeWarbonds.ManagerWindowOpen())
            return true;
        var manager = Process.Start(ManagerStart(root)) ?? throw new IOException("Official HellForge manager did not start.");
        // The long-lived manager must not hold the frontend's protocol pipes (our stdio), or their EOF never arrives on close.
        manager.StandardInput.Close(); manager.StandardOutput.Close(); manager.StandardError.Close();
        managedWindow = manager;
        return false;
    }
    bool ManagerWindowOpen()
    {
        if (managedWindow != null)
        {
            try { if (!managedWindow.HasExited) return true; }
            catch (InvalidOperationException) { }
        }
        return HellForgeWarbonds.ManagerWindowOpen();
    }
    internal static ProcessStartInfo ManagerStart(string root) =>
        new(OperatingSystem.IsLinux() ? Path.Combine(root, "app", "launcher-linux", "HellForge.Launcher") : Path.Combine(root, "HellForge.exe")) {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
    internal async Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        switch (args[0])
        {
            case "open-manager":
                var alreadyOpen = OpenOrReuseManager();
                return JsonSerializer.SerializeToElement(new { opened = "official-manager", alreadyOpen });
            case "account-status": return JsonSerializer.SerializeToElement(await account.ReadAsync(ct));
            case "select-account":
                lastAccount = null; lastAccountFingerprint = null; warbondsPending = false;
                await client.SelectAccountAsync(ct, reviewCapture: false); return JsonSerializer.SerializeToElement(new { selected = "default" });
            case "account-import":
                lastAccount = null; lastAccountFingerprint = null;
                var opened = await client.ImportAccountAsync(ct);
                warbondsPending = opened == 0;
                if (opened > 0) warbondMessage = $"Opened {opened} owned Warbonds.";
                // The frontend ignores the mutation reply and displays the following player-status account message.
                return JsonSerializer.SerializeToElement(await account.ReadAsync(ct));
            case "select-mode":
                var prevUltrakill = HostMutatorConfig.IsUltrakillTheater(selection.HostTheaterPreset, selection.HostChaosIntensity);
                var values = args.Skip(1).Chunk(2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
                var requested = new HellForgePreferences { Mode = values["--mode"], DeltaSquad = values["--delta"] == "yes",
                    Texture = values["--texture"], EnemySkin = values["--droids"] == "yes" ? "all-droids" : "native-automatons", Covenant = values["--covenant"] == "yes",
                    HostTheaterPreset = "standard" };
                if (values.TryGetValue("--enemy-skin", out var skin)) requested.EnemySkin = skin;
                if (values.TryGetValue("--squid-skin", out var squid)) requested.SquidSkin = squid;
                if (values.TryGetValue("--deathtrooper", out var dt)) requested.DeathTrooperVoice = dt == "yes";
                if (values.TryGetValue("--theater", out var th)) requested.HostTheaterPreset = th;
                if (values.TryGetValue("--play-without-sync", out var pws)) requested.PlayWithoutAccountSync = pws == "yes";
                requested.Validate();
                var nowUltrakill = HostMutatorConfig.IsUltrakillTheater(requested.HostTheaterPreset, requested.HostChaosIntensity);
                if (prevUltrakill != nowUltrakill)
                    companionSyncPending = true;
                selection.Mode = requested.Mode; selection.DeltaSquad = requested.DeltaSquad;
                selection.Texture = requested.Texture; selection.EnemySkin = requested.EnemySkin;
                selection.SquidSkin = requested.SquidSkin; selection.DeathTrooperVoice = requested.DeathTrooperVoice;
                selection.HostTheaterPreset = requested.HostTheaterPreset;
                selection.PlayWithoutAccountSync = requested.PlayWithoutAccountSync;
                if (companionSyncPending && await SyncTheaterCompanionsIfReadyAsync(nowUltrakill, ct))
                    companionSyncPending = false;
                return JsonSerializer.SerializeToElement(new { selection.Mode, selection.DeltaSquad, selection.Texture, selection.Droids, selection.EnemySkin, selection.Covenant, selection.SquidSkin, selection.DeathTrooperVoice, selection.HostTheaterPreset, selection.PlayWithoutAccountSync });
            case "prepare-regular":
                if (!selection.IsRegular) throw new InvalidOperationException("Select Regular before applying that mode.");
                await client.PrepareRegularAsync(ct); return JsonSerializer.SerializeToElement(new { prepared = true });
            case "prepare-pack":
                if (selection.IsRegular) throw new InvalidOperationException("Regular uses no appearance pack.");
                await client.InstallPackAsync(args[2], selection.Selection(), ct);
                return JsonSerializer.SerializeToElement(new { prepared = true, selection = selection.SelectionKey });
            case "recover-pack":
                await client.RecoverInterruptedPackAsync(ct); return JsonSerializer.SerializeToElement(new { recovered = true });
            case "gameplay-status": return JsonSerializer.SerializeToElement(await client.MissingGameplayAsync(selection.IsRegular ? "regular" : selection.PackMode, ct));
            case "install-gameplay":
                await client.InstallGameplayAsync(args[2], ct: ct, activeMode: selection.IsRegular ? "regular" : selection.PackMode);
                var installUk = HostMutatorConfig.IsUltrakillTheater(selection.HostTheaterPreset, selection.HostChaosIntensity) || client.IsUltrakillHostTheaterActive();
                if (await SyncTheaterCompanionsIfReadyAsync(installUk, ct))
                    companionSyncPending = false;
                return JsonSerializer.SerializeToElement(new { installed = true });
            case "recover-gameplay":
                await client.RecoverGameplayAsync(ct); return JsonSerializer.SerializeToElement(new { recovered = true });
            case "player-status": return await StateAsync(ct);
            case "play":
            {
                // No direct CLI shortcut can skip the one-way account or full appearance verification unless PlayWithoutAccountSync is opted in on a 0.5+ catalogue install.
                string profile;
                try
                {
                    var ensure = EnsureAccount ?? (CanPlayWithoutSync(null) ? client.EnsureStarterOrImportedAccountAsync : client.EnsureAccountAsync);
                    profile = await ensure(ct);
                }
                catch { lastAccount = null; lastAccountFingerprint = null; throw; }
                await RetryPendingWarbondsAsync(ct);
                var playUk = HostMutatorConfig.IsUltrakillTheater(selection.HostTheaterPreset, selection.HostChaosIntensity) || client.IsUltrakillHostTheaterActive();
                if (await SyncTheaterCompanionsIfReadyAsync(playUk, ct))
                    companionSyncPending = false;
                return selection.IsRegular ? await client.PlayRegularAsync(ct, profile) : await client.PlayAsync(selection.Selection(), ct, profile);
            }
            default:
                if (args[0] is not ("status" or "mods" or "profiles" or "logs" or "stop") && !(args.Count == 1 && args[0] == "settings") &&
                    client.InterruptedPackProblem() is { } interrupted) throw new InvalidOperationException(interrupted);
                return await official.RunAsync(args, ct);
        }
    }

    async Task<JsonElement> StateAsync(CancellationToken ct)
    {
        var status = await client.StatusAsync(ct);
        // Preserve a known running or starting status even if a later mod/account query fails
        // or while HellForge has swapped game/bin/steam_api64.dll for session SDK shims (unless cleanup already reported a restore failure).
        if (status.Running || (status.LastFailure == null && HellForgeAccount.SessionShimsActive(root, UtcNow())))
            return JsonSerializer.SerializeToElement(new { status, ready = false,
                detail = status.Running ? "Session running. End session waits for official cleanup." : "Session starting. Waiting for HellForge to launch the game.",
                account = (object?)null, mods = (object?)null });
        var catalog = await client.ModsAsync(ct);
        HellForgeAccountState imported;
        try
        {
            if (CaptureBlockCheck() is { } capturing)
            {
                lastAccount = null; lastAccountFingerprint = null; imported = new(false, false, capturing);
            }
            else if (ManagerWindowOpen() && managerOpenFingerprint != null && client.AccountFingerprint() == managerOpenFingerprint)
            {
                // Avoid spawning Proton ownership probes while the HellForge window we opened for sync is still active with unchanged files.
                imported = lastAccount ?? new(false, false, "HellForge is open. Finish Sync Inventory in HellForge, quit HD2, and wait for cleanup.");
                imported = AccountMessage(imported);
            }
            else
            {
                var fingerprint = ComputeAccountCacheKey();
                if (lastAccount == null || fingerprint == null || fingerprint != lastAccountFingerprint || UtcNow() - lastAccountReview >= TimeSpan.FromMinutes(5))
                {
                    imported = await account.ReadAsync(ct);
                    if (imported.Imported) await RunWarbondReviewAsync(ct);
                    else warbondsPending = false;
                    if (fingerprint != null && ComputeAccountCacheKey() is { } refreshedFingerprint)
                    {
                        lastAccount = imported;
                        lastAccountFingerprint = refreshedFingerprint; // opening Warbonds can update the profile/backups
                        lastAccountReview = UtcNow();
                    }
                    else
                    {
                        lastAccount = null;
                        lastAccountFingerprint = null;
                    }
                }
                else
                {
                    imported = lastAccount;
                    if (imported.Imported) await RetryPendingWarbondsAsync(ct);
                }
                imported = AccountMessage(imported);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or TimeoutException)
        {
            if (HellForgeAccount.IsTransientReviewError(error, root) &&
                ComputeAccountCacheKey() != null &&
                (lastAccount is { Imported: true } || account.HasLocalImportReceipt()))
            {
                // Transient Wine/SDK-shim/lock contention after a session: keep the verified local import state
                // for UI presentation so we never falsely prompt "Sync account", while clearing fingerprint to retry next poll.
                lastAccountFingerprint = null;
                imported = lastAccount is { Imported: true } prev
                    ? prev
                    : new(true, true, "Your HD2 account is imported. Waiting for Wine/SDK cleanup to finish.");
                imported = AccountMessage(imported);
            }
            else
            {
                lastAccount = null;
                lastAccountFingerprint = null;
                imported = new(false, false, error.Message);
            }
        }
        string? problem = null;
        try
        {
            // Version/isolation are checked for diagnostics as well as mutations.
            BridgeSession.RequirePinnedIsolation(await official.RunAsync(["status"], ct));
            HellForgeClient.RequireReady(status);
            if (selection.IsRegular) HellForgeClient.RequireRegular(catalog);
            else
            {
                var chosenSelection = selection.Selection();
                var chosen = HellForgeClient.RequireSelected(catalog, chosenSelection);
                await HellForgePack.VerifyMetadataAsync(chosen.Folder, chosenSelection, ct);
            }
            var ultrakillActive = HostMutatorConfig.IsUltrakillTheater(selection.HostTheaterPreset, selection.HostChaosIntensity) || client.IsUltrakillHostTheaterActive();
            if (HellForgePreset.Problem(catalog, ultrakillTheaterActive: ultrakillActive) is { } preset) throw new InvalidDataException(preset);
            if (HellForgePreset.ModeCompanionProblem(catalog, selection.IsRegular ? "regular" : selection.PackMode) is { } companionIssue) throw new InvalidDataException(companionIssue);
            if (!status.Multiplayer || !status.LocalBackend) throw new InvalidDataException("Enable the official local backend and Steam multiplayer in HellForge.");
            if (!imported.Imported && !CanPlayWithoutSync(imported)) throw new InvalidDataException(imported.Message);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException) { problem = error.Message; }
        return JsonSerializer.SerializeToElement(new { status, mods = catalog, account = imported, ready = problem == null,
            detail = problem ?? "Ready for a private Linux trial. Appearance hashes are checked before launch; Linux gameplay and squad compatibility remain unqualified." });
    }
    HellForgeAccountState AccountMessage(HellForgeAccountState state)
    {
        var message = warbondMessage; warbondMessage = null;
        return message == null ? state : state with { Message = message + " " + state.Message };
    }
    bool CanPlayWithoutSync(HellForgeAccountState? acct) =>
        selection.PlayWithoutAccountSync &&
        HellForgeAccount.CanPlayWithoutSync(root) &&
        acct is not { Captured: true, Imported: false } &&
        !account.HasPendingJournal();
    async Task<bool> SyncTheaterCompanionsIfReadyAsync(bool enableUltrakill, CancellationToken ct)
    {
        UltrakillRuntimeProfile.SyncEngineFiles(root, enableUltrakill);
        if (!File.Exists(Path.Combine(root, "mods", "hellforge-mods.json"))) return false;
        var status = await client.StatusAsync(ct);
        if (status.Running) return false;
        await client.SyncUltrakillCompanionOptionsAsync(enableUltrakill, ct);
        return true;
    }
    async Task<int?> TryOpenOwnedWarbondsOrDeferAsync(CancellationToken ct)
    {
        try { return await client.OpenOwnedWarbondsAsync(ct); }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or TimeoutException)
        { return null; }
    }
    async Task RunWarbondReviewAsync(CancellationToken ct)
    {
        if (await reviewWarbonds(ct) is { } opened)
        {
            warbondsPending = false;
            if (opened > 0) warbondMessage = $"Opened {opened} owned Warbonds.";
        }
        else warbondsPending = true;
    }
    async Task RetryPendingWarbondsAsync(CancellationToken ct)
    {
        if (!warbondsPending) return;
        await RunWarbondReviewAsync(ct);
        if (warbondsPending || lastAccount == null) return;
        if (ComputeAccountCacheKey() is { } refreshedKey) lastAccountFingerprint = refreshedKey;
        else { lastAccount = null; lastAccountFingerprint = null; }
    }
    string? ComputeAccountCacheKey()
    {
        var user = SteamUserProbe();
        return string.IsNullOrWhiteSpace(user) ? null : client.AccountFingerprint() + "\n" + user;
    }
    internal static string? LinuxSteamUser(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) return null;
        var registryText = ReadVdfText(Path.Combine(home, ".steam", "registry.vdf"));
        if (registryText != null && ParseRegistryActiveUser(registryText) is { } active)
            return active;
        var primaryLogin = Path.Combine(home, ".steam", "steam", "config", "loginusers.vdf");
        var fallbackLogin = Path.Combine(home, ".local", "share", "Steam", "config", "loginusers.vdf");
        var loginText = ReadVdfText(primaryLogin) ?? (File.Exists(primaryLogin) ? null : ReadVdfText(fallbackLogin));
        if (loginText != null)
        {
            var loginUser = ParseLoginUsersMostRecent(loginText);
            if (loginUser != null) return loginUser;
            // If loginusers.vdf exists and is explicitly ambiguous (e.g. multiple MostRecent=1), do not guess from registry.
            if (HasAmbiguousMostRecent(loginText)) return null;
        }
        return registryText != null ? ParseRegistryAutoLoginUser(registryText) : null;
    }
    static string? ReadVdfText(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4 * 1024 * 1024) return null;
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return null; }
    }
    internal static string? ParseRegistryActiveUser(string vdf)
    {
        string? candidate = null;
        foreach (var (parent, _, key, value) in ReadVdfEntries(vdf))
        {
            if (!key.Equals("ActiveUser", StringComparison.OrdinalIgnoreCase)) continue;
            if (parent != null && !parent.Equals("ActiveProcess", StringComparison.OrdinalIgnoreCase)) continue;
            var trimmed = value.Trim();
            ulong id;
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!ulong.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id)) return null;
            }
            else if (!ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out id))
            {
                return null;
            }
            if (id == 0) return null;
            candidate = "active:" + id.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(parent, "ActiveProcess", StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        return candidate;
    }
    internal static string? ParseRegistryAutoLoginUser(string vdf)
    {
        foreach (var (parent, _, key, value) in ReadVdfEntries(vdf))
        {
            if (!key.Equals("AutoLoginUser", StringComparison.OrdinalIgnoreCase)) continue;
            if (parent != null && !parent.Equals("Steam", StringComparison.OrdinalIgnoreCase)) continue;
            var trimmed = value.Trim();
            if (!string.IsNullOrEmpty(trimmed)) return "autologinuser:" + trimmed;
        }
        return null;
    }
    static bool HasAmbiguousMostRecent(string vdf)
    {
        var matches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (parent, grandparent, key, value) in ReadVdfEntries(vdf))
        {
            if (!key.Equals("MostRecent", StringComparison.OrdinalIgnoreCase) || value.Trim() != "1") continue;
            if (string.IsNullOrWhiteSpace(parent) || grandparent != null && !grandparent.Equals("users", StringComparison.OrdinalIgnoreCase)) continue;
            if (ulong.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0)
                matches.Add(id.ToString(CultureInfo.InvariantCulture));
        }
        return matches.Count > 1;
    }
    internal static string? ParseLoginUsersMostRecent(string vdf)
    {
        var mostRecent = new HashSet<string>(StringComparer.Ordinal);
        var autoLogin = new HashSet<string>(StringComparer.Ordinal);
        var allUsers = new HashSet<string>(StringComparer.Ordinal);
        var timestamps = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var (parent, grandparent, key, value) in ReadVdfEntries(vdf))
        {
            if (string.IsNullOrWhiteSpace(parent) || grandparent != null && !grandparent.Equals("users", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ulong.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0) continue;
            var idText = id.ToString(CultureInfo.InvariantCulture);
            allUsers.Add(idText);
            if (key.Equals("MostRecent", StringComparison.OrdinalIgnoreCase) && value.Trim() == "1")
                mostRecent.Add(idText);
            else if (key.Equals("AutoLogin", StringComparison.OrdinalIgnoreCase) && value.Trim() == "1")
                autoLogin.Add(idText);
            else if (key.Equals("timestamp", StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ts) && ts > 0)
                timestamps[idText] = ts;
        }
        if (mostRecent.Count == 1) return "recent:" + mostRecent.First();
        if (mostRecent.Count > 1) return null;
        if (autoLogin.Count == 1) return "autologin:" + autoLogin.First();
        if (allUsers.Count == 1) return "single:" + allUsers.First();
        if (timestamps.Count > 0)
        {
            var maxTs = timestamps.Values.Max();
            var newest = timestamps.Where(kv => kv.Value == maxTs).Select(kv => kv.Key).ToList();
            if (newest.Count == 1) return "latest:" + newest[0];
        }
        return null;
    }
    static IEnumerable<(string? Parent, string? Grandparent, string Key, string Value)> ReadVdfEntries(string text)
    {
        var path = new List<string>();
        string? pendingKey = null;
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] is not ('\r' or '\n')) i++;
                continue;
            }
            if (ch == '{') { path.Add(pendingKey ?? ""); pendingKey = null; i++; continue; }
            if (ch == '}') { if (path.Count > 0) path.RemoveAt(path.Count - 1); pendingKey = null; i++; continue; }
            string token;
            if (ch == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var esc => esc });
                    }
                    else sb.Append(text[i]);
                    i++;
                }
                if (i >= text.Length) yield break;
                i++;
                token = sb.ToString();
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"')) i++;
                token = text[start..i];
            }
            if (pendingKey == null) pendingKey = token;
            else
            {
                var parent = path.Count > 0 ? path[^1] : null;
                var grandparent = path.Count > 1 ? path[^2] : null;
                yield return (parent, grandparent, pendingKey, token);
                pendingKey = null;
            }
        }
    }
}
