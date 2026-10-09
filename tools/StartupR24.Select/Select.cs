using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// No launcher main/UI, saved settings, engine, or external mutation is invoked.
static class StartupSelect
{
    static void Need(bool ok, string reason) { if (!ok) throw new InvalidDataException(reason); }
    static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    static object? Prop(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static byte[] Extract(byte[] exe)
    {
        var signature = Convert.FromHexString("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae");
        var marker = exe.AsSpan().IndexOf(signature); Need(marker >= 8, "bundle marker");
        var offset = BitConverter.ToInt64(exe, marker - 8); Need(offset > marker && offset < exe.Length, "bundle extent");
        using var input = new MemoryStream(exe, false); using var reader = new BinaryReader(input); input.Position = offset;
        Need(reader.ReadUInt32() == 6 && reader.ReadUInt32() == 0, "bundle version"); var count = reader.ReadInt32();
        reader.ReadString(); for (int i = 0; i < 5; i++) reader.ReadUInt64(); byte[]? result = null;
        for (int i = 0; i < count; i++)
        {
            var at = reader.ReadInt64(); var size = reader.ReadInt64(); var compressed = reader.ReadInt64(); var kind = reader.ReadByte(); var name = reader.ReadString();
            if (name != "Clonedivers.dll") continue;
            Need(result is null && kind == 1 && at >= 0 && at + (compressed > 0 ? compressed : size) <= offset, "managed extent");
            var packed = exe.AsSpan((int)at, (int)(compressed > 0 ? compressed : size)).ToArray();
            if (compressed > 0) { using var source = new MemoryStream(packed, false); using var zip = new DeflateStream(source, CompressionMode.Decompress); using var target = new MemoryStream(); zip.CopyTo(target); result = target.ToArray(); }
            else result = packed;
            Need(result.LongLength == size, "managed decompression");
        }
        Need(result is not null, "managed assembly present"); return result!;
    }
    static int Main(string[] args)
    {
        try
        {
            Need(args.Length == 3, "workspace candidate fresh-output-folder");
            var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[2]);
            Need(output.StartsWith(Path.Combine(root, "dist", "production-r24-2026-10-09") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(output), "fresh scoped output");
            var candidatePath = Path.GetFullPath(args[1]); var candidateBytes = File.ReadAllBytes(candidatePath);
            Need(Sha(candidateBytes) == "52ce94fc76141535aa094c46de69f447eb5d04623787426d3c9f06c4f874da9c", "r24 candidate pin");
            var oldPath = Path.Combine(root, "dist/production-r23-2026-10-08/release-ready-v1/manifest.json"); var oldBytes = File.ReadAllBytes(oldPath);
            Need(Sha(oldBytes) == "b08614df277dad2388a70ddeab7e230fa9990b7045e0abbe3c687e2c6966e5f7", "r23 public source pin");
            var exe = File.ReadAllBytes(Path.Combine(root, "dist/local-current/Clonedivers.exe"));
            Need(Sha(exe) == "2effe777eb2673d7237b5fc9aed1cbe85434ba86f3c31fbae9f73129a06cd51e", "desktop launcher pin");
            var managed = Extract(exe); using var assemblyStream = new MemoryStream(managed, false); var assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
            Need(assembly.GetName().Version == new Version(1, 7, 3, 0), "actual launcher version");
            Type T(string name) => assembly.GetType("Clonedivers." + name, true)!;
            object Call(string type, string method, params object?[] values) => T(type).GetMethod(method)!.Invoke(null, values)!;
            var oldManifest = Call("Manifest", "Parse", Encoding.UTF8.GetString(oldBytes)); var manifest = Call("Manifest", "Parse", Encoding.UTF8.GetString(candidateBytes));
            var oldPack = Prop(oldManifest, "Pack")!; var pack = Prop(manifest, "Pack")!;
            Need((string)Prop(oldPack, "Version")! == "2026.10.08-r23" && (string)Prop(pack, "Version")! == "2026.10.09-r24", "release versions");
            var oldGlobal = JsonNode.Parse(oldBytes)!["pack"]!["files"]!.AsArray(); var newGlobal = JsonNode.Parse(candidateBytes)!["pack"]!["files"]!.AsArray();
            Need(oldGlobal.Count == 1809 && newGlobal.Count == oldGlobal.Count, "all global rows retained");
            var changes = Enumerable.Range(0, oldGlobal.Count).Where(i => !JsonNode.DeepEquals(oldGlobal[i], newGlobal[i])).ToArray();
            Need(changes.Length == 4, "exact four MAIN changes");
            var edits = changes.ToDictionary(i => oldGlobal[i]!["sha256"]!.GetValue<string>(), i => newGlobal[i]!.AsObject());
            Need(changes.Select(i => newGlobal[i]!["name"]!.GetValue<string>()).ToHashSet().SetEquals(new[] { "9ba626afa44a3aa3.patch_429", "9ba626afa44a3aa3.patch_453", "9ba626afa44a3aa3.patch_462", "9ba626afa44a3aa3.patch_463" }), "only intended MAINs");
            var fileSets = new Dictionary<string, JsonArray>(); var selections = new List<object>(); var comparisons = new List<object>();
            foreach (var profile in new[] { "full", "lighter" })
            foreach (var droids in new[] { false, true }) foreach (var covenant in new[] { false, true })
            foreach (var delta in new[] { false, true }) foreach (var aimpoints in new[] { false, true })
            foreach (var mode in new[] { "Clonedivers", "CommandoDivers", "EmpireDivers" })
            {
                var requested = new Dictionary<string, bool> { ["droids"] = droids, ["covenant"] = covenant, ["commandos"] = delta, ["aimpoints"] = aimpoints };
                var settings = Activator.CreateInstance(T("Settings"))!; T("Settings").GetProperty("TextureProfile")!.SetValue(settings, profile); T("Settings").GetProperty("Options")!.SetValue(settings, requested);
                var oldFlags = (Dictionary<string, bool>)Call("LauncherModes", "OptionsFor", settings, oldPack, Enum.Parse(T("LauncherMode"), mode));
                var flags = (Dictionary<string, bool>)Call("LauncherModes", "OptionsFor", settings, pack, Enum.Parse(T("LauncherMode"), mode));
                Need(oldFlags.Count == flags.Count && oldFlags.All(row => flags[row.Key] == row.Value), "effective options unchanged");
                var oldFiles = Call("Pack", "EffectiveFiles", oldPack, (Func<string, bool>)(id => oldFlags[id]), profile);
                var files = Call("Pack", "EffectiveFiles", pack, (Func<string, bool>)(id => flags[id]), profile);
                var expected = JsonSerializer.SerializeToNode(oldFiles, oldFiles.GetType(), Json)!.AsArray();
                var actual = JsonSerializer.SerializeToNode(files, files.GetType(), Json)!.AsArray();
                int changed = 0;
                foreach (var row in expected)
                    if (edits.TryGetValue(row!["sha256"]!.GetValue<string>(), out var replacement))
                    { foreach (var field in new[] { "sha256", "size", "url" }) row[field] = replacement[field]!.DeepClone(); changed++; }
                Need(JsonNode.DeepEquals(expected, actual), "every selected row exact outside four authorized MAIN identities");
                foreach (var group in actual.GroupBy(row => row!["name"]!.GetValue<string>().Split('.')[0]))
                {
                    var indices = group.Select(row => int.Parse(Regex.Match(row!["name"]!.GetValue<string>(), @"\.patch_(\d+)").Groups[1].Value)).Distinct().Order().ToArray();
                    Need(indices.SequenceEqual(Enumerable.Range(0, indices.Length)), "dense physical indices");
                }
                var id = Sha(Encoding.UTF8.GetBytes(actual.ToJsonString())); fileSets.TryAdd(id, actual);
                selections.Add(new { profile, mode, requestedOptions = requested, effectiveOptions = flags, fileSet = id });
                comparisons.Add(new { profile, mode, requestedOptions = requested, changedMains = changed, selectedFiles = actual.Count, allOtherRowsExact = true });
            }
            Need(selections.Count == 96 && fileSets.Count == 16, "complete 96 states /16 sets");
            Directory.CreateDirectory(output);
            var report = new { passed = true, assemblyName = assembly.FullName, actualLauncherSha256 = Sha(exe), extractedAssemblySha256 = Sha(managed), manifestSha256 = Sha(candidateBytes), baselineManifestSha256 = Sha(oldBytes), productionSelector = "Actual bundled 1.7.3 LauncherModes.OptionsFor + Pack.EffectiveFiles", requestedStates = selections.Count, uniqueFileSets = fileSets.Count, selections, fileSets, comparisons, all96StatesOnlyAuthorizedFourMainDeltas = true, savedSettingsRead = false, launcherMainOrUIInvoked = false, gameOrSettingsChanged = false, gameplayTested = false };
            File.WriteAllText(Path.Combine(output, "production96.json"), JsonSerializer.Serialize(report, Json));
            Console.WriteLine("PASS actual bundled desktop 1.7.3:96 states/16 sets, exact four MAIN release deltas only."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
