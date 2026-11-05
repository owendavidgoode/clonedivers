using System.Text.Json;

namespace Clonedivers;

internal static class LinuxForgeTerminal
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine("Clonedivers Forge Linux " + AppInfo.Version); return 0; }
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("""
                Clonedivers Forge for Linux
                --forge --gui                     Open Regular / Clones / Empire desktop
                --forge [--forge-root PATH] status Read readiness without starting a game
                --forge [--forge-root PATH] mods   Read the official mod catalog
                --forge [--forge-root PATH] account Read verified account import status
                --forge-resources PATH selects the complete companion package.
                Commands preserve the dedicated prefix and official namespace/sandbox.
                Linux gameplay/account capture and Windows/Linux co-op are unqualified.
                --steam opens the preserved Steam fallback; Forge packages default to Forge.
                """);
            return 0;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += stop;
        try
        {
            var preferences = LinuxForgePreferences.Load();
            var resources = Path.Combine(AppContext.BaseDirectory, "forge");
            var command = "status";
            for (var i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--forge": case "--terminal": case "--tui": case "--plain": break;
                    case "--forge-root": preferences.InstallRoot = Value(args, ref i); break;
                    case "--forge-resources": resources = Value(args, ref i); break;
                    case "status": case "mods": case "account": command = args[i]; break;
                    default: throw new ArgumentException("Unsupported Forge diagnostic argument. Use --forge --help; player actions are in the desktop trial.");
                }
            await using var player = await LinuxForgePlayer.ConnectAsync(preferences, resources, cancellation.Token);
            if (command == "mods") Console.WriteLine(JsonSerializer.Serialize(player.State?.Mods, HellForgePack.Json));
            else if (command == "account") Console.WriteLine(JsonSerializer.Serialize(player.State?.Account, HellForgePack.Json));
            else Console.WriteLine(JsonSerializer.Serialize(player.State, HellForgePack.Json));
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Stopped. Review official cleanup before reconnecting."); return 130; }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        finally { Console.CancelKeyPress -= stop; }
    }
    static string Value(string[] args, ref int i) => ++i < args.Length ? args[i] : throw new ArgumentException("Missing Forge option value.");
}
