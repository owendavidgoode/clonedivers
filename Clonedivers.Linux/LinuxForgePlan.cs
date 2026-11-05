using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Clonedivers;

/// <summary>Prepare only our helper/cache in a dedicated C: prefix; never alter the Forge installation or Steam.</summary>
internal sealed record LinuxForgePlan(string Install, string Prefix, string WindowsInstall, string Bridge, string Script)
{
    internal string? SteamClientRoot { get; init; }
    internal const string BridgeName = "HellForge.Launcher.ClonediversBridge.exe";
    internal const string BridgeVersion = "0.3.8";
    internal const string GameBuild = "25480438";
    internal static bool SupportsArchitecture(Architecture architecture) => architecture == Architecture.X64;
    internal static readonly string[] Scripts = ["hellforge-linux.sh", "hellforge-offline.sh", "hf-inner.sh", "hf-bridge-host.sh", "allow-namespace.sh"];

    internal static LinuxForgePlan Stage(string install, string bridgeSource, string expectedBridgeHash, string scriptBundle)
    {
        if (!SupportsArchitecture(RuntimeInformation.ProcessArchitecture)) throw new PlatformNotSupportedException("This Forge/Proton trial requires x86-64 Linux. The existing ARM64 Steam launcher is a separate workflow.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        RejectLinks(root);
        for (var ancestor = new DirectoryInfo(root); ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Forge needs a dedicated prefix outside Steam's live game/prefix folders.");
        DirectoryInfo? drive = null;
        for (var ancestor = new DirectoryInfo(root).Parent; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.Name == "drive_c" && ancestor.Parent?.Name == "pfx") { drive = ancestor; break; }
        if (drive?.Parent?.Parent is not { } prefix) throw new InvalidDataException("Choose an installation inside its dedicated prefix's pfx/drive_c folder.");
        foreach (var file in new[] { "hellforge-install.json", "HellForge.exe", "app/cli/HellForge.Cli.exe" })
        {
            var path = Path.Combine(root, file); RejectLinks(path);
            if (!File.Exists(path)) throw new FileNotFoundException("Official Forge file missing: " + file);
        }
        var marker = Path.Combine(root, "hellforge-install.json");
        if (new FileInfo(marker).Length > 65536) throw new InvalidDataException("Installation marker is oversized.");
        using (var doc = JsonDocument.Parse(File.ReadAllText(marker)))
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1 || doc.RootElement.GetProperty("buildId").GetString() != GameBuild)
                throw new InvalidDataException("This Forge game build is outside the pinned Clonedivers trial.");
        if (!IsHash(expectedBridgeHash)) throw new InvalidDataException("A trusted packaged bridge SHA256 is required.");
        VerifyFile(bridgeSource, expectedBridgeHash);
        RejectLinks(scriptBundle);
        var metadata = Path.Combine(scriptBundle, "bundle.json"); RejectLinks(metadata);
        if (new FileInfo(metadata).Length > 65536) throw new InvalidDataException("Script bundle metadata is oversized.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(metadata));
        if (manifest.RootElement.GetProperty("Format").GetInt32() != 1) throw new InvalidDataException("Unsupported script bundle.");
        var entries = manifest.RootElement.GetProperty("Scripts").EnumerateArray().ToArray();
        if (entries.Length != Scripts.Length || !entries.Select(e => e.GetProperty("Name").GetString()).ToHashSet(StringComparer.Ordinal).SetEquals(Scripts))
            throw new InvalidDataException("Script bundle must contain the exact pinned script set.");
        var hashes = Scripts.ToDictionary(name => name, name => entries.Single(e => e.GetProperty("Name").GetString() == name).GetProperty("BundleSha256").GetString()
            ?? throw new InvalidDataException("Missing script hash."), StringComparer.Ordinal);
        foreach (var (name, hash) in hashes) VerifyFile(Path.Combine(scriptBundle, name), hash);
        var license = Path.Combine(scriptBundle, "COPYING"); RejectLinks(license);
        if (!File.Exists(license) || new FileInfo(license).Length > 128 * 1024) throw new InvalidDataException("Script source/license notice missing or oversized.");
        // Hash the entire known script set so changing a script always creates a separate cache.
        var bundleHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", Scripts.Select(s => s + ":" + hashes[s]))))).ToLowerInvariant();
        var cache = Path.Combine(drive.FullName, "ClonediversLauncher", expectedBridgeHash.ToLowerInvariant()[..16] + "-" + bundleHash[..16]);
        if (Within(cache, root) || Within(root, cache)) throw new InvalidDataException("Launcher cache must be separate from the Forge installation.");
        RejectLinks(cache);
        Directory.CreateDirectory(cache);
        var bridge = Path.Combine(cache, BridgeName);
        CopyVerified(bridgeSource, bridge, expectedBridgeHash);
        var scripts = Path.Combine(cache, "linux"); RejectLinks(scripts); Directory.CreateDirectory(scripts);
        foreach (var (name, hash) in hashes) CopyVerified(Path.Combine(scriptBundle, name), Path.Combine(scripts, name), hash);
        CopyVerified(license, Path.Combine(scripts, "COPYING"), Hash(license));
        var windows = "C:\\" + Path.GetRelativePath(drive.FullName, root).Replace(Path.DirectorySeparatorChar, '\\');
        return new(root, prefix.FullName, windows, bridge, Path.Combine(scripts, "hellforge-linux.sh"));
    }

    internal ProcessStartInfo StartInfo()
    {
        var start = new ProcessStartInfo("bash") { WorkingDirectory = Install, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(Script); start.ArgumentList.Add(Bridge); start.ArgumentList.Add("--install"); start.ArgumentList.Add(WindowsInstall);
        start.Environment["INSTALL"] = Install;
        start.Environment["STEAM_COMPAT_DATA_PATH"] = Prefix;
        if (SteamClientRoot is not null) start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = SteamClientRoot;
        start.Environment.Remove("HELLFORGE_LINUX_NETNS"); // only the official wrapper may set its namespace marker
        return start;
    }

    static bool Within(string child, string root) => child == root || child.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    static void VerifyFile(string file, string hash)
    {
        RejectLinks(file);
        if (!IsHash(hash) || !File.Exists(file) || new FileInfo(file).Length > 128 * 1024 * 1024 || !Hash(file).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Packaged Forge helper/script hash mismatch: " + Path.GetFileName(file));
    }
    static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            // Exists() follows links and returns false for dangling links; inspect the entry itself.
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked Forge/cache/package paths are unsupported."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
    }
    static void CopyVerified(string source, string target, string hash)
    {
        RejectLinks(target);
        if (File.Exists(target)) { VerifyFile(target, hash); return; }
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary, false);
            VerifyFile(temporary, hash);
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
