using System.Text.Json;
using Clonedivers.ForgeBridge;

try
{
    if (args is ["--help"])
    {
        Console.WriteLine("Clonedivers private Forge CLI bridge. Start through official hellforge-linux.sh with --install <C:-drive installation>. JSON requests on stdin; no network listener.");
        return 0;
    }
    if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("HELLFORGE_LINUX_NETNS") != "1")
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
