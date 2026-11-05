using System.Security.Cryptography;
using System.Text.Json;
using Clonedivers;

namespace Clonedivers.ForgeBridge.Tests;

internal static class NativeControlTests
{
    internal static Task Run(string area, Action<bool, string> check)
    {
        var install = Path.Combine(area, "native-prefix", "pfx", "drive_c", "Forge");
        foreach (var relative in new[] { "hellforge-install.json", "app/app-build.json", "app/cli-linux/HellForge.Cli", "app/backend-linux/HellForge.LocalBackend", "app/launcher-linux/HellForge.Launcher", "app/linux/hellforge-linux.sh", "app/linux/hf-sandbox" })
        { var path = Path.Combine(install, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture"); }
        File.WriteAllText(Path.Combine(install, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");
        File.WriteAllText(Path.Combine(install, "app", "app-build.json"), """{"version":"0.4.0-beta","engine":"0.1.47"}""");
        var source = Path.Combine(area, "native-control-source"); File.WriteAllText(source, "fixture bridge; never executed");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();
        var plan = LinuxForgePlan.StageNative(install, source, hash);
        var start = plan.StartInfo();
        check(plan.NativeControl && start.FileName == plan.Bridge && start.ArgumentList.SequenceEqual(new[] { "--install", install }),
            "native control starts the pinned native bridge directly, leaving Proton/session construction to the official native CLI");
        check(!start.Environment.ContainsKey("HELLFORGE_LINUX_NETNS") && !start.Environment.ContainsKey("HELLFORGE_STEAM_ACCOUNT"),
            "native control does not fabricate a namespace marker or signed-in account through environment overrides");
        check(plan.Script == "" && File.ReadAllText(Path.Combine(install, "app", "linux", "hellforge-linux.sh")) == "fixture",
            "0.3 helper scripts never replace or overlay the installed 0.4 relay/sandbox runtime");
        File.AppendAllText(plan.Bridge, "changed");
        try { LinuxForgePlan.StageNative(install, source, hash); check(false, "changed helper must fail"); }
        catch (InvalidDataException) { check(true, "a changed native helper cache is rejected instead of silently trusted"); }
        if (OperatingSystem.IsLinux())
        {
            BridgeSession.Validate(["install-gameplay", "--file", Path.Combine(install, "cache")]); check(true, "native protocol accepts absolute host staging paths without a Wine Z: shortcut");
            foreach (var path in new[] { "relative", "/tmp/../profile" })
            {
                try { BridgeSession.Validate(["install-gameplay", "--file", path]); check(false, "unsafe native staging must fail"); }
                catch (ArgumentException) { check(true, "native protocol rejects ambiguous staging: " + path); }
            }
        }
        return Task.CompletedTask;
    }
}
