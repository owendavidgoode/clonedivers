using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

// Read-only selection comparison against the actual bundled launcher assembly.
// Bundle layout: dotnet/runtime v8.0.0 HostWriter.cs, Manifest.cs, FileEntry.cs.
static class BinaryCheck
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static object? Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
    static object Pin(string path) => new { path = Path.GetFullPath(path), bytes = new FileInfo(path).Length, sha256 = Sha(File.ReadAllBytes(path)) };
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static async Task<int> Main(string[] args)
    {
        try
        {
            Require(args.Length == 4, "launcher-exe manifest production96 fresh-output-folder");
            var output = Path.GetFullPath(args[3]);
            Require(!Directory.Exists(output), "Fresh output folder required");
            var workspace = Path.GetFullPath(AppContext.BaseDirectory + "../../../../../");
            Require(output.StartsWith(Path.Combine(workspace, "dist", "production-r21-2026-10-08", "qa") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "Output must be in workspace QA integration");
            var exe = File.ReadAllBytes(args[0]);
            var signature = Convert.FromHexString("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae");
            var marker = exe.AsSpan().IndexOf(signature);
            Require(marker >= 8 && exe.AsSpan(marker + signature.Length).IndexOf(signature) < 0, "Unique bundle marker required");
            var header = BitConverter.ToInt64(exe, marker - 8);
            Require(header > marker && header < exe.Length, "Bundle header outside executable");
            using var stream = new MemoryStream(exe, false);
            using var reader = new BinaryReader(stream);
            stream.Position = header;
            var major = reader.ReadUInt32(); var minor = reader.ReadUInt32(); var count = reader.ReadInt32();
            Require(major == 6 && minor == 0 && count is > 0 and < 2000, "Expected reviewed .NET 6 bundle format");
            var bundleId = reader.ReadString();
            var depsOffset = reader.ReadInt64(); var depsSize = reader.ReadInt64();
            var runtimeOffset = reader.ReadInt64(); var runtimeSize = reader.ReadInt64(); var flags = reader.ReadUInt64();
            var entries = new List<object>(); byte[]? managed = null;
            for (int i = 0; i < count; i++)
            {
                var offset = reader.ReadInt64(); var size = reader.ReadInt64(); var compressed = reader.ReadInt64();
                var kind = reader.ReadByte(); var name = reader.ReadString();
                Require(offset >= 0 && size >= 0 && compressed >= 0 && offset + (compressed > 0 ? compressed : size) <= header,
                    "Embedded entry outside payload");
                entries.Add(new { name, offset, size, compressed, kind });
                if (name != "Clonedivers.dll") continue;
                Require(kind == 1 && managed is null && size < 50_000_000, "Expected one managed launcher DLL");
                var packed = exe.AsSpan(checked((int)offset), checked((int)(compressed > 0 ? compressed : size))).ToArray();
                if (compressed > 0)
                {
                    using var input = new MemoryStream(packed, false);
                    using var inflater = new DeflateStream(input, CompressionMode.Decompress);
                    using var expanded = new MemoryStream();
                    await inflater.CopyToAsync(expanded);
                    managed = expanded.ToArray();
                }
                else managed = packed;
                Require(managed.LongLength == size, "Managed assembly decompression size differs");
            }
            Require(managed is not null, "Launcher DLL not found");
            Directory.CreateDirectory(output);
            var dllPath = Path.Combine(output, "Clonedivers.dll");
            File.WriteAllBytes(dllPath, managed!);
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
            var manifestType = assembly.GetType("Clonedivers.Manifest", true)!;
            var settingsType = assembly.GetType("Clonedivers.Settings", true)!;
            var modeType = assembly.GetType("Clonedivers.LauncherMode", true)!;
            var modesType = assembly.GetType("Clonedivers.LauncherModes", true)!;
            var packType = assembly.GetType("Clonedivers.Pack", true)!;
            var manifest = manifestType.GetMethod("Parse")!.Invoke(null, new object[] { File.ReadAllText(args[1]) })!;
            var pack = Property(manifest, "Pack")!;
            using var baseline = JsonDocument.Parse(File.ReadAllText(args[2]));
            Require(baseline.RootElement.GetProperty("passed").GetBoolean()
                && baseline.RootElement.GetProperty("manifestSha256").GetString() == Sha(File.ReadAllBytes(args[1])), "Selector pin mismatch");
            var profiles = new[] { "full", "lighter" };
            var comparisons = new List<object>();
            foreach (var profile in profiles)
            foreach (var droids in new[] { false, true })
            foreach (var covenant in new[] { false, true })
            foreach (var commandos in new[] { false, true })
            foreach (var aimpoints in new[] { false, true })
            foreach (var mode in new[] { "Clonedivers", "CommandoDivers", "EmpireDivers" })
            {
                var settings = Activator.CreateInstance(settingsType)!;
                settingsType.GetProperty("TextureProfile")!.SetValue(settings, profile);
                var requested = new Dictionary<string, bool> { ["droids"] = droids, ["covenant"] = covenant,
                    ["commandos"] = commandos, ["aimpoints"] = aimpoints };
                settingsType.GetProperty("Options")!.SetValue(settings, requested);
                var effective = (Dictionary<string, bool>)modesType.GetMethod("OptionsFor")!.Invoke(null,
                    new[] { settings, pack, Enum.Parse(modeType, mode) })!;
                var files = packType.GetMethod("EffectiveFiles")!.Invoke(null,
                    new object[] { pack, (Func<string, bool>)(id => effective[id]), profile })!;
                var match = baseline.RootElement.GetProperty("selections").EnumerateArray().Single(row =>
                    row.GetProperty("mode").GetString() == mode && row.GetProperty("profile").GetString() == profile
                    && requested.All(flag => row.GetProperty("requestedOptions").GetProperty(flag.Key).GetBoolean() == flag.Value));
                var expected = baseline.RootElement.GetProperty("fileSets").GetProperty(match.GetProperty("fileSet").GetString()!);
                using var actualJson = JsonDocument.Parse(JsonSerializer.Serialize(files, Json));
                Require(JsonNode.DeepEquals(JsonNode.Parse(actualJson.RootElement.GetRawText()), JsonNode.Parse(expected.GetRawText())),
                    "Binary/source selected payload metadata differ: " + mode + "/" + profile);
                Require(effective.All(flag => match.GetProperty("effectiveOptions").GetProperty(flag.Key).GetBoolean() == flag.Value),
                    "Binary/source effective flags differ");
                comparisons.Add(new { profile, mode, requested, effective, files = ((ICollection)files).Count, equal = true });
            }
            Require(comparisons.Count == 96, "Expected 96 binary selections");
            var receiptVectors = new List<object>();
            foreach (var gamePath in new[] { @"D:\Games\Helldivers 2", @"E:\Bibliothèque\Jeux\helldivers 2",
                @"F:\Oyunlar\ıİiI\Helldivers 2", @"G:\Δοκιμή\straße\Helldivers 2",
                @"H:\玩家\ヘルダイバー\Helldivers 2", @"\\NAS\Steam Library\helldivers 2",
                "D:/SteamLibrary/steamapps/common/Helldivers 2/" })
            {
                var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath)).ToUpperInvariant();
                var expectedSha = Sha(System.Text.Encoding.UTF8.GetBytes(canonical));
                var actualPath = (string)settingsType.GetMethod("ReceiptPathFor")!.Invoke(null, new object[] { gamePath })!;
                Require(Path.GetFileName(actualPath) == expectedSha + ".json", "Actual launcher receipt identity differs");
                receiptVectors.Add(new { gamePath, canonical, sha256 = expectedSha, receiptFile = Path.GetFileName(actualPath) });
            }
            var result = new { passed = true, utc = DateTimeOffset.UtcNow, launcher = Pin(args[0]), extractedAssembly = Pin(dllPath),
                assemblyName = assembly.FullName, manifest = Pin(args[1]), sourceProductionSelector = Pin(args[2]),
                bundle = new { major, minor, count, bundleId, header, flags, depsOffset, depsSize, runtimeOffset, runtimeSize, entries },
                binarySelections = comparisons, all96BinarySelectionsEqualCurrentSource = true, receiptPathParityVectors = receiptVectors,
                launcherMainOrUIInvoked = false, gameOrSettingsChanged = false, gameplayTested = false };
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(result, Json));
            Console.WriteLine("PASS actual launcher assembly: all 96 selections equal current source.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
