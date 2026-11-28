using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

/// <summary>
/// Reversibly applies and restores the Level 12 ULTRAKILL / Full Clone Wars crowd-scaling engine
/// and render profile (Stingray fiber job pool 32,768, D3D12 256 MiB scratch, Wwise 2 MiB queue /
/// 64 MiB pool, distance LOD step-down with close-up HQ bubble, trample off, and Lua SEAF scan
/// storm suppression) while preserving SHA-256-verified byte-exact originals in &lt;cacheRoot&gt;/ultrakill/.
/// </summary>
public static class UltrakillRuntimeProfile
{
    public const string ManifestFileName = "ultrakill-backup-manifest.json";
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public sealed record BackedUpFileEntry(
        string RelativeInstallPath,
        string BackupFileName,
        long OriginalBytes,
        string OriginalSha256);

    public sealed class BackupManifest
    {
        public int Format { get; set; } = 1;
        public string InstallRoot { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public int LodMigratedVersion { get; set; }
        public List<BackedUpFileEntry> Files { get; set; } = [];
    }

    public static string GetBackupDirectory(string installRoot)
    {
        var root = NormalizeSafeInstallRoot(installRoot);
        var parent = Path.GetDirectoryName(root)
            ?? throw new InvalidDataException("Choose a HellForge folder, not a drive root.");
        var cacheRoot = Path.Combine(parent, "Clonedivers-HellForge-cache");
        FileSafety.RejectLink(cacheRoot);
        var ultrakillRoot = Path.Combine(cacheRoot, "ultrakill");
        if (Directory.Exists(ultrakillRoot)) FileSafety.RejectLink(ultrakillRoot);
        var identitySource = OperatingSystem.IsWindows() ? root.ToLowerInvariant() : root;
        var rootHash = Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(identitySource))).ToLowerInvariant()[..16];
        var rawLeaf = Path.GetFileName(root);
        var safeLeaf = Regex.Replace(string.IsNullOrWhiteSpace(rawLeaf) ? "install" : rawLeaf, @"[^A-Za-z0-9._-]", "_");
        if (safeLeaf.Length > 32) safeLeaf = safeLeaf[..32];
        return Path.Combine(ultrakillRoot, $"{safeLeaf}-{rootHash}");
    }

    /// <summary>
    /// Synchronizes Stingray job/Wwise/D3D12 settings in &lt;installRoot&gt;/game/data/settings.ini and
    /// crowd-rendering overrides in &lt;installRoot&gt;/userdata/profiles/*/user_settings.config.
    /// Captures byte-exact originals once before the first ULTRAKILL apply, and restores byte-for-byte
    /// (verifying SHA-256) when returning to Standard (D1–11).
    /// </summary>
    public static bool SyncEngineFiles(string installRoot, bool enableUltrakill)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) return false;
        var root = NormalizeSafeInstallRoot(installRoot);
        var marker = Path.Combine(root, "hellforge-install.json");
        if (!File.Exists(marker)) return false; // Skip synthetic partial test roots that lack a Forge layout.
        FileSafety.RejectLink(marker);

        var backupDir = GetBackupDirectory(root);
        var manifestPath = Path.Combine(backupDir, ManifestFileName);

        if (!enableUltrakill)
            return RestoreOriginals(root, backupDir, manifestPath);

        var targets = DiscoverTargetConfigFiles(root);
        if (targets.Count == 0) return false;

        Directory.CreateDirectory(backupDir);
        FileSafety.RejectLink(backupDir);
        var originalsDir = FileSafety.Child(backupDir, "originals");
        Directory.CreateDirectory(originalsDir);
        FileSafety.RejectLink(originalsDir);

        bool hadManifest = File.Exists(manifestPath);
        var manifest = hadManifest
            ? LoadExistingManifestOrThrow(root, manifestPath)
            : new BackupManifest { Format = 1, InstallRoot = root, LodMigratedVersion = 1 };
        bool runLodMigration = hadManifest && manifest.LodMigratedVersion < 1;
        var existingRel = manifest.Files.ToDictionary(f => f.RelativeInstallPath, PlatformPaths.Comparer);
        bool manifestDirty = !hadManifest;

        foreach (var fullTarget in targets)
        {
            FileSafety.RejectLink(fullTarget);
            var rel = Path.GetRelativePath(root, fullTarget).Replace('\\', '/');
            if (!existingRel.ContainsKey(rel))
            {
                var rawOrig = File.ReadAllBytes(fullTarget);
                var origSha = Convert.ToHexString(SHA256.HashData(rawOrig)).ToLowerInvariant();
                var backupLeaf = Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(rel))).ToLowerInvariant()[..24] + "_" + Path.GetFileName(fullTarget);
                var backupFile = FileSafety.Child(originalsDir, backupLeaf);
                WriteBytesAtomic(backupFile, rawOrig);
                var entry = new BackedUpFileEntry(rel, backupLeaf, rawOrig.Length, origSha);
                manifest.Files.Add(entry);
                existingRel[rel] = entry;
                manifestDirty = true;
            }
        }

        // Persist the clean backup manifest atomically BEFORE mutating any target file.
        if (manifestDirty)
            WriteBytesAtomic(manifestPath, Utf8NoBom.GetBytes(JsonSerializer.Serialize(manifest, Json)));

        foreach (var fullTarget in targets)
        {
            var rel = Path.GetRelativePath(root, fullTarget).Replace('\\', '/');
            var currentText = File.ReadAllText(fullTarget, Encoding.UTF8);
            string patchedText;
            if (rel.Equals("game/data/settings.ini", StringComparison.OrdinalIgnoreCase))
            {
                patchedText = ApplySettingsIniOverrides(currentText);
            }
            else
            {
                var baseText = currentText;
                if (runLodMigration &&
                    string.Equals(TryExtractScalarKey(baseText, "lod_selection_multiplier"), "0.6", StringComparison.Ordinal) &&
                    existingRel.TryGetValue(rel, out var backupEntry))
                {
                    var origBytes = ReadVerifiedBackupBytesOrThrow(originalsDir, backupEntry);
                    var origText = Encoding.UTF8.GetString(origBytes);
                    var origLod = TryExtractScalarKey(origText, "lod_selection_multiplier");
                    if (origLod != null && !string.Equals(origLod, "0.6", StringComparison.Ordinal))
                        baseText = ReplaceScalarKey(baseText, "lod_selection_multiplier", origLod);
                }
                patchedText = ApplyUserSettingsOverrides(baseText);
            }

            if (!string.Equals(currentText, patchedText, StringComparison.Ordinal))
                WriteBytesAtomic(fullTarget, Utf8NoBom.GetBytes(patchedText));
        }

        if (runLodMigration)
        {
            manifest.LodMigratedVersion = 1;
            WriteBytesAtomic(manifestPath, Utf8NoBom.GetBytes(JsonSerializer.Serialize(manifest, Json)));
        }
        return true;
    }

    public static bool RestoreOriginals(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) return false;
        var root = NormalizeSafeInstallRoot(installRoot);
        var backupDir = GetBackupDirectory(root);
        return RestoreOriginals(root, backupDir, Path.Combine(backupDir, ManifestFileName));
    }

    static bool RestoreOriginals(string root, string backupDir, string manifestPath)
    {
        if (!File.Exists(manifestPath)) return false;
        FileSafety.RejectLink(backupDir);
        FileSafety.RejectLink(manifestPath);

        var manifest = LoadExistingManifestOrThrow(root, manifestPath);
        var originalsDir = FileSafety.Child(backupDir, "originals");
        FileSafety.RejectLink(originalsDir);

        foreach (var entry in manifest.Files)
        {
            var origBytes = ReadVerifiedBackupBytesOrThrow(originalsDir, entry);

            var targetPath = Path.GetFullPath(Path.Combine(root, entry.RelativeInstallPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!targetPath.StartsWith(root + Path.DirectorySeparatorChar, PlatformPaths.Comparison))
                throw new InvalidDataException("ULTRAKILL restore target escaped installation root.");
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(targetPath)!); dir != null && dir.FullName.Length >= root.Length; dir = dir.Parent)
                FileSafety.RejectLink(dir.FullName);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            WriteBytesAtomic(targetPath, origBytes);
        }

        // Remove armed backup artifacts after all restored files pass SHA-256 verification.
        foreach (var entry in manifest.Files)
        {
            var backupFile = FileSafety.Child(originalsDir, entry.BackupFileName);
            try { if (File.Exists(backupFile)) File.Delete(backupFile); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        try { File.Delete(manifestPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try
        {
            if (Directory.Exists(originalsDir) && !Directory.EnumerateFileSystemEntries(originalsDir).Any())
                Directory.Delete(originalsDir);
            if (Directory.Exists(backupDir) && !Directory.EnumerateFileSystemEntries(backupDir).Any())
                Directory.Delete(backupDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return true;
    }

    /// <summary>
    /// Suppresses Smarter SEAF 1.0.4's 500k–900k/sec Lua FFI ReadProcessMemory storm when Level 12 ULTRAKILL /
    /// Clone Wars theater is armed (keeping the mod enabled so Squad Match Codes stay identical), restores it
    /// when disarmed, and ensures Smarter Guard Dogs &amp; Sentries keeps SEAF Safety, Sentry Safety, Targeting
    /// Laser, and Targeting Marker OFF so turrets shoot enemies in heavy Clone/Stormtrooper SEAF battles and
    /// Star Wars Guard Dog lasers have no barrel-laser visual conflict.
    /// </summary>
    public static async Task SyncCompanionModOptionsAsync(
        Func<string[], CancellationToken, Task> runCliAsync,
        HellForgeMods? catalog,
        bool enableUltrakill,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runCliAsync);
        if (catalog == null) return;

        var smarterSeaf = catalog.Mods.SingleOrDefault(m => m.Id == HellForgePreset.SmarterSeaf && m.Enabled);
        if (smarterSeaf != null)
        {
            bool anyOn = smarterSeaf.Options == null || smarterSeaf.Options.Length == 0 || smarterSeaf.Options.Any(o => o.On);
            bool allOn = smarterSeaf.Options != null && smarterSeaf.Options.Length > 0 && smarterSeaf.Options.All(o => o.On);
            if (enableUltrakill && anyOn)
                await runCliAsync(["mod-pack", "--id", HellForgePreset.SmarterSeaf, "--all", "no"], ct).ConfigureAwait(false);
            else if (!enableUltrakill && !allOn)
                await runCliAsync(["mod-pack", "--id", HellForgePreset.SmarterSeaf, "--all", "yes"], ct).ConfigureAwait(false);
        }

        var dogs = catalog.Mods.SingleOrDefault(m => m.Id == HellForgePreset.GuardDogs && m.Enabled);
        if (dogs != null)
        {
            foreach (var offOption in HellForgePreset.GuardDogsRequiredOffOptions.Concat(HellForgePreset.GuardDogsAuxiliaryOffOptions))
            {
                if (dogs.Options?.SingleOrDefault(o => o.Name == offOption) is { On: true })
                    await runCliAsync(["mod-pack", "--id", HellForgePreset.GuardDogs, "--option", offOption, "--on", "no"], ct).ConfigureAwait(false);
            }
        }
    }

    public static string ApplySettingsIniOverrides(string sjson)
    {
        ArgumentNullException.ThrowIfNull(sjson);
        var newline = sjson.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var updated = ReplaceScalarKey(sjson, "max_num_jobs", "32768");
        updated = ReplaceScalarKeyInBlock(updated, "d3d12", "scratch_space", "256");
        updated = ReplaceScalarKeyInBlock(updated, "sound_engine", "command_queue_size", "2097152");
        updated = ReplaceScalarKeyInBlock(updated, "sound_engine", "pool_size", "67108864");
        return NormalizeLineEndings(updated, newline);
    }

    public static string ApplyUserSettingsOverrides(string sjson)
    {
        ArgumentNullException.ThrowIfNull(sjson);
        var newline = sjson.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var updated = ReplaceScalarKey(sjson, "enable_resource_lock_debug", "false");
        updated = ReplaceScalarKey(updated, "hq_bubble_enabled", "true");
        updated = ReplaceScalarKey(updated, "terrain_trample_enabled", "false");
        updated = ReplaceScalarKey(updated, "shadows", "2");
        updated = ReplaceScalarKey(updated, "forward_local_lights_cast_shadows", "false");
        updated = ReplaceScalarKey(updated, "particles_receive_shadows", "false");
        updated = ReplaceScalarKey(updated, "particle_quality", "1");
        updated = ReplaceTwoIntArrayKey(updated, "sun_shadow_map_size", 2048, 2048, newline);
        return NormalizeLineEndings(updated, newline);
    }

    static byte[] ReadVerifiedBackupBytesOrThrow(string originalsDir, BackedUpFileEntry entry)
    {
        var backupFile = FileSafety.Child(originalsDir, entry.BackupFileName);
        FileSafety.RejectLink(backupFile);
        if (!File.Exists(backupFile))
            throw new InvalidDataException($"Missing ULTRAKILL original backup file: {entry.BackupFileName}");

        var origBytes = File.ReadAllBytes(backupFile);
        var actualSha = Convert.ToHexString(SHA256.HashData(origBytes)).ToLowerInvariant();
        if (origBytes.Length != entry.OriginalBytes || !string.Equals(actualSha, entry.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"ULTRAKILL backup hash mismatch for {entry.RelativeInstallPath}; refusing to overwrite.");
        return origBytes;
    }

    static string? TryExtractScalarKey(string text, string key)
    {
        var pattern = $@"^[ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*([^\r\n#]+)";
        var match = Regex.Match(text, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    static string ReplaceScalarKey(string text, string key, string newValue)
    {
        var pattern = $@"(^[ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*)([^\r\n#]+)";
        return Regex.Replace(text, pattern, m => m.Groups[1].Value + newValue, RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }

    static string ReplaceScalarKeyInBlock(string text, string blockName, string key, string newValue)
    {
        var blockPattern = $@"(\b{Regex.Escape(blockName)}[ \t]*=[ \t]*\{{)([^{{}}]*?)(\}})";
        return Regex.Replace(text, blockPattern, m =>
        {
            var body = ReplaceScalarKey(m.Groups[2].Value, key, newValue);
            return m.Groups[1].Value + body + m.Groups[3].Value;
        }, RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }

    static string ReplaceTwoIntArrayKey(string text, string key, int first, int second, string nl)
    {
        var pattern = $@"(^[ \t]*)({Regex.Escape(key)}[ \t]*=[ \t]*\[)([^\]]*)(\])";
        return Regex.Replace(text, pattern, m =>
        {
            var indent = m.Groups[1].Value;
            return $"{indent}{m.Groups[2].Value}{nl}{indent}\t{first}{nl}{indent}\t{second}{nl}{indent}]";
        }, RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }

    static string NormalizeLineEndings(string text, string nl) =>
        nl == "\r\n"
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal)
            : text.Replace("\r\n", "\n", StringComparison.Ordinal);

    static List<string> DiscoverTargetConfigFiles(string root)
    {
        var files = new List<string>();
        var settingsIni = Path.Combine(root, "game", "data", "settings.ini");
        if (File.Exists(settingsIni))
        {
            RejectLinksAlong(root, settingsIni);
            files.Add(settingsIni);
        }

        var profilesRoot = Path.Combine(root, "userdata", "profiles");
        if (Directory.Exists(profilesRoot))
        {
            RejectLinksAlong(root, profilesRoot);
            foreach (var profileName in new[] { "default", "clonedivers-armory" })
            {
                var pDir = Path.Combine(profilesRoot, profileName);
                if (!Directory.Exists(pDir)) continue;
                FileSafety.RejectLink(pDir);

                var mainCfg = Path.Combine(pDir, "user_settings.config");
                if (File.Exists(mainCfg))
                {
                    FileSafety.RejectLink(mainCfg);
                    files.Add(mainCfg);
                }

                var savesDir = Path.Combine(pDir, "saves");
                if (Directory.Exists(savesDir))
                {
                    FileSafety.RejectLink(savesDir);
                    foreach (var saveCfg in Directory.EnumerateFiles(savesDir, "*_user_settings.config"))
                    {
                        FileSafety.RejectLink(saveCfg);
                        files.Add(saveCfg);
                    }
                }
            }
        }
        return files;
    }

    static BackupManifest LoadExistingManifestOrThrow(string root, string manifestPath)
    {
        FileSafety.RejectLink(manifestPath);
        if (new FileInfo(manifestPath).Length > 256 * 1024)
            throw new InvalidDataException("ULTRAKILL backup manifest is oversized.");
        BackupManifest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath, Encoding.UTF8), Json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("ULTRAKILL backup manifest is unreadable.", e);
        }
        if (parsed == null || parsed.Format != 1 || parsed.Files == null || string.IsNullOrWhiteSpace(parsed.InstallRoot))
            throw new InvalidDataException("ULTRAKILL backup manifest is unreadable or invalid.");
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parsed.InstallRoot)), root, PlatformPaths.Comparison))
            throw new InvalidDataException("ULTRAKILL backup manifest belongs to a different installation; refusing to overwrite.");
        return parsed;
    }

    static void WriteBytesAtomic(string path, byte[] bytes)
    {
        FileSafety.RejectLink(path);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        FileSafety.RejectLink(dir);
        var temp = FileSafety.Child(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            FileSafety.RejectLink(path);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    static void RejectLinksAlong(string root, string descendant)
    {
        for (var cur = Path.GetFullPath(descendant); cur != null && cur.Length >= root.Length; cur = Path.GetDirectoryName(cur))
            FileSafety.RejectLink(cur);
    }

    static string NormalizeSafeInstallRoot(string installRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        for (var dir = new DirectoryInfo(root); dir != null; dir = dir.Parent)
        {
            FileSafety.RejectLink(dir.FullName);
            if (dir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Refusing to modify files inside a Steam library.");
        }
        return root;
    }
}
