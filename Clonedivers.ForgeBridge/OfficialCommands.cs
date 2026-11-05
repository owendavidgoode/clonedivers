using System.Diagnostics;
using System.Text.Json;

namespace Clonedivers.ForgeBridge;

internal sealed class OfficialCommands : Clonedivers.IHellForgeCommands
{
    readonly string root, cli;
    bool outcomeUnknown;
    internal OfficialCommands(string install)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        for (var folder = new DirectoryInfo(root); folder != null; folder = folder.Parent)
        {
            RejectLink(folder.FullName);
            if (folder.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Use a separate Forge installation outside Steam.");
        }
        var marker = Path.Combine(root, "hellforge-install.json");
        RejectLink(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length > 65536) throw new InvalidDataException("HellForge installation marker is missing or oversized.");
        using var document = JsonDocument.Parse(File.ReadAllText(marker));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1 || document.RootElement.GetProperty("buildId").GetString() != BridgeSession.Build)
            throw new InvalidDataException("Unsupported Forge installation.");
        var native = OperatingSystem.IsLinux();
        foreach (var relative in new[] { "app", native ? "app/cli-linux" : "app/cli", native ? "app/cli-linux/HellForge.Cli" : "app/cli/HellForge.Cli.exe" }) RejectLink(Path.Combine(root, relative));
        cli = Path.Combine(root, "app", native ? "cli-linux" : "cli", native ? "HellForge.Cli" : "HellForge.Cli.exe");
        if (!File.Exists(cli)) throw new FileNotFoundException("Official Forge CLI is missing.");
    }

    static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked installation paths are unsupported.");
    }

    public async Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (outcomeUnknown && !Clonedivers.HellForgeCommands.ReadAfterUncertain(args))
            throw new Clonedivers.HellForgeCommandOutcomeUnknownException("The previous CLI outcome needs review. Stop and diagnostics remain available; confirm the CLI has stopped and review the mod state before restarting this connection and retrying changes or launch.");
        var start = new ProcessStartInfo(cli) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(args[0] == "mod-add" ? 10 : 2));
        try
        {
            return await Clonedivers.HellForgeCommands.ReadJsonProcessAsync(start, args[0] == "play", timeout.Token);
        }
        catch (Clonedivers.HellForgeCommandOutcomeUnknownException)
        {
            outcomeUnknown = true;
            throw;
        }
    }
}
