using System.Text.Json;
using System.Diagnostics;
using Clonedivers;

namespace Clonedivers.ForgeBridge;

/// <summary>Reuse the verified Forge account/preset backend with the installed official native tools.</summary>
internal sealed class PlayerCommands
{
    readonly IHellForgeCommands official;
    readonly HellForgeClient client;
    readonly HellForgeAccount account;
    readonly HellForgePreferences selection = new();
    readonly Action openManager;
    string? warbondMessage;
    internal PlayerCommands(string root, IHellForgeCommands official, Action? openManager = null)
    {
        this.official = official;
        client = new(root, official) { LinuxNamespace = true };
        account = new(root);
        this.openManager = openManager ?? (() => {
            using var manager = Process.Start(ManagerStart(root)) ?? throw new IOException("Official HellForge manager did not start.");
            // The long-lived manager must not hold the frontend's protocol pipes (our stdio), or their EOF never arrives on close.
            manager.StandardInput.Close(); manager.StandardOutput.Close(); manager.StandardError.Close();
        });
    }
    internal static ProcessStartInfo ManagerStart(string root) =>
        new(OperatingSystem.IsLinux() ? Path.Combine(root, "app", "launcher-linux", "HellForge.Launcher") : Path.Combine(root, "HellForge.exe")) {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
    internal async Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        switch (args[0])
        {
            case "open-manager": openManager(); return JsonSerializer.SerializeToElement(new { opened = "official-manager" });
            case "account-status": return JsonSerializer.SerializeToElement(await account.ReadAsync(ct));
            case "select-account":
                await client.SelectAccountAsync(ct, reviewCapture: false); return JsonSerializer.SerializeToElement(new { selected = "default" });
            case "account-import":
                var opened = await client.ImportAccountAsync(ct);
                if (opened > 0) warbondMessage = $"Opened {opened} owned Warbonds.";
                return JsonSerializer.SerializeToElement(AccountMessage(await account.ReadAsync(ct)));
            case "select-mode":
                var values = args.Skip(1).Chunk(2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
                selection.Mode = values["--mode"]; selection.DeltaSquad = values["--delta"] == "yes";
                selection.Texture = values["--texture"]; selection.Droids = values["--droids"] == "yes"; selection.Covenant = values["--covenant"] == "yes";
                selection.Validate();
                return JsonSerializer.SerializeToElement(new { selection.Mode, selection.DeltaSquad, selection.Texture, selection.Droids, selection.Covenant });
            case "prepare-regular":
                if (!selection.IsRegular) throw new InvalidOperationException("Select Regular before applying that mode.");
                await client.PrepareRegularAsync(ct); return JsonSerializer.SerializeToElement(new { prepared = true });
            case "prepare-pack":
                if (selection.IsRegular) throw new InvalidOperationException("Regular uses no appearance pack.");
                await client.InstallPackAsync(args[2], selection.Selection(), ct);
                return JsonSerializer.SerializeToElement(new { prepared = true, selection = selection.SelectionKey });
            case "recover-pack":
                await client.RecoverInterruptedPackAsync(ct); return JsonSerializer.SerializeToElement(new { recovered = true });
            case "gameplay-status": return JsonSerializer.SerializeToElement(await client.MissingGameplayAsync(ct));
            case "install-gameplay":
                await client.InstallGameplayAsync(args[2], ct: ct); return JsonSerializer.SerializeToElement(new { installed = true });
            case "recover-gameplay":
                await client.RecoverGameplayAsync(ct); return JsonSerializer.SerializeToElement(new { recovered = true });
            case "player-status": return await StateAsync(ct);
            case "play":
                // No direct CLI shortcut can skip the one-way account or full appearance verification.
                var profile = await client.EnsureAccountAsync(ct);
                return selection.IsRegular ? await client.PlayRegularAsync(ct, profile) : await client.PlayAsync(selection.Selection(), ct, profile);
            default:
                if (args[0] is not ("status" or "mods" or "profiles" or "logs" or "stop") && !(args.Count == 1 && args[0] == "settings") &&
                    client.InterruptedPackProblem() is { } interrupted) throw new InvalidOperationException(interrupted);
                return await official.RunAsync(args, ct);
        }
    }

    async Task<JsonElement> StateAsync(CancellationToken ct)
    {
        var status = await client.StatusAsync(ct);
        // Preserve a known running status even if a later mod/account query fails.
        if (status.Running) return JsonSerializer.SerializeToElement(new { status, ready = false, detail = "Session running. End session waits for official cleanup.", account = (object?)null, mods = (object?)null });
        var catalog = await client.ModsAsync(ct);
        HellForgeAccountState imported;
        try
        {
            imported = HellForgeAccount.CaptureBlock() is { } capturing ? new(false, false, capturing) : await account.ReadAsync(ct);
            if (imported.Imported)
            {
                var opened = await client.TryOpenOwnedWarbondsAsync(ct);
                if (opened > 0) warbondMessage = $"Opened {opened} owned Warbonds.";
                imported = AccountMessage(imported);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { imported = new(false, false, error.Message); }
        string? problem = null;
        try
        {
            // Version/isolation are checked for diagnostics as well as mutations.
            BridgeSession.RequirePinnedIsolation(await official.RunAsync(["status"], ct));
            HellForgeClient.RequireReady(status);
            if (selection.IsRegular) HellForgeClient.RequireRegular(catalog);
            else
            {
                var chosen = HellForgeClient.RequireSelected(catalog, selection.Selection());
                await HellForgePack.VerifyMetadataAsync(chosen.Folder, selection.Selection(), ct);
            }
            if (HellForgePreset.Problem(catalog) is { } preset) throw new InvalidDataException(preset);
            if (!status.Multiplayer || !status.LocalBackend) throw new InvalidDataException("Enable the official local backend and Steam multiplayer in HellForge.");
            if (!imported.Imported) throw new InvalidDataException(imported.Message);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException) { problem = error.Message; }
        return JsonSerializer.SerializeToElement(new { status, mods = catalog, account = imported, ready = problem == null,
            detail = problem ?? "Ready for a private Linux trial. Appearance hashes are checked before launch; Linux gameplay and squad compatibility remain unqualified." });
    }
    HellForgeAccountState AccountMessage(HellForgeAccountState state) => warbondMessage == null ? state : state with { Message = warbondMessage + " " + state.Message };
}
