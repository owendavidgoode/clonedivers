using System.Text.Json;
using Clonedivers.ForgeBridge;

try
{
    if (args is ["--help"])
    {
        Console.WriteLine("Clonedivers Forge controller. Native Linux controls the installed official native CLI; a Windows/Wine fallback requires the official namespace. --install <installation>, JSON requests on stdin; no network listener.");
        return 0;
    }
    // Native Linux controls the official native CLI. Its SessionRunner alone starts
    // Proton, the namespace, relay whitelist and sandbox supervisor. A Windows
    // bridge still requires the official namespace and is never a host shortcut.
    if (!OperatingSystem.IsLinux() && (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("HELLFORGE_LINUX_NETNS") != "1"))
        throw new InvalidOperationException("Start the Windows bridge through HellForge's official Linux namespace script.");
    if (args is not ["--install", var root]) throw new ArgumentException("Expected --install <HellForge folder>.");
    var commands = new OfficialCommands(root);
    var player = new PlayerCommands(root, commands);
    var bridge = new BridgeSession(player.RunAsync);
    Console.WriteLine(JsonSerializer.Serialize(new { bridge = "clonedivers-forge", format = 1 }));
    await Console.Out.FlushAsync();
    await bridge.ServeAsync(Console.In, Console.Out);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Clonedivers Forge bridge: " + error.Message);
    return 1;
}
