using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

public sealed class TelemetrySettings
{
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "https://clonedivers-telemetry.goodecraft.com/";
    public string Nickname { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Token { get; set; } = "";
    public bool Ready => Enabled && TelemetryStore.ValidEndpoint(Endpoint) && Regex.IsMatch(DeviceId, "^[a-f0-9]{32}$") && Regex.IsMatch(Token, "^[a-f0-9]{64}$");
}

public static class TelemetryStore
{
    public record Row(string Kind, DateTimeOffset Utc, Dictionary<string, object?> Fields);
    public record Chunk(int Version, string DeviceId, string SessionId, string ChunkId, string Nickname, IReadOnlyList<Row> Records);
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers", "telemetry");
    public const int MaxChunkBytes = 480 * 1024;
    public const long MaxQueueBytes = 512L * 1024 * 1024;
    public static bool ValidEndpoint(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "" && uri.AbsolutePath == "/";
    public static string SafeText(string? text, int limit = 1024)
    {
        var value = Regex.Replace(text ?? "", @"[\p{C}]", " ");
        return value[..Math.Min(value.Length, limit)];
    }
    public static Dictionary<string, object?> ParseFrame(string[] header, string line)
    {
        var cells = line.Split(','); var fields = new Dictionary<string, object?>();
        if (cells.Length != header.Length) return fields;
        for (var i = 0; i < header.Length; i++)
        {
            var key = header[i].Trim().Trim('"'); var value = cells[i].Trim().Trim('"');
            if (!Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9_]{0,79}$") || key is "Application" or "ProcessID") continue;
            if (key == "SwapChainAddress") { if (Regex.IsMatch(value, "^(0x)?[0-9A-Fa-f]{1,18}$")) fields[key] = value; }
            else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) fields[key] = number;
            else if (key is "Runtime" or "PresentRuntime" or "PresentMode" or "FrameType" && Regex.IsMatch(value, "^[A-Za-z0-9 :_-]{1,80}$")) fields[key] = value;
            else fields[key] = null;
        }
        return fields;
    }
    public static string Save(Chunk chunk, string? root = null)
    {
        root ??= Root;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(chunk, Json);
        if (bytes.Length > MaxChunkBytes || chunk.Records.Count > 2000 || !Regex.IsMatch(chunk.ChunkId, "^[a-f0-9]{32}$")) throw new InvalidDataException("Telemetry chunk exceeded its bound.");
        FileSafety.RejectLink(root); Directory.CreateDirectory(root);
        var path = FileSafety.Child(root, chunk.ChunkId + ".json");
        FileSafety.RejectLink(path + ".tmp");
        File.WriteAllBytes(path + ".tmp", bytes); File.Move(path + ".tmp", path, true);
        Prune(root); return path;
    }
    public static void Prune(string root, long limit = MaxQueueBytes)
    {
        FileSafety.RejectLink(root);
        var files = new DirectoryInfo(root).GetFiles().Where(f => Regex.IsMatch(f.Name, "^[a-f0-9]{32}\\.(json|invalid)$")).OrderBy(f=>f.LastWriteTimeUtc).ToArray();
        var size = files.Sum(f=>f.Length);
        foreach (var f in files)
        {
            if (size <= limit && f.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-14)) continue;
            FileSafety.RejectLink(f.FullName); var length=f.Length; f.Delete(); size -= length;
        }
    }
    public static HttpClient CreateHttp() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public static async Task<TelemetrySettings> Enroll(string endpoint, string nickname, string invitation, CancellationToken cancel = default)
    {
        endpoint = endpoint.Trim().TrimEnd('/') + "/";
        if (!ValidEndpoint(endpoint) || string.IsNullOrWhiteSpace(nickname) || nickname.Length > 40) throw new InvalidDataException("Enter a HTTPS collector address and a nickname (up to 40 characters).");
        using var http = CreateHttp(); using var request = new HttpRequestMessage(HttpMethod.Post, endpoint + "enroll");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", invitation.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(new { nickname = SafeText(nickname, 40) }), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        var body = await ReadSmall(response.Content, cancel);
        using var parsed = JsonDocument.Parse(body);
        var result = new TelemetrySettings { Enabled = true, Endpoint = endpoint, Nickname = SafeText(nickname,40), DeviceId = parsed.RootElement.GetProperty("deviceId").GetString() ?? "", Token = parsed.RootElement.GetProperty("token").GetString() ?? "" };
        if (!result.Ready) throw new InvalidDataException("Collector returned invalid enrollment.");
        return result;
    }
    static async Task<byte[]> ReadSmall(HttpContent content, CancellationToken cancel)
    {
        using var stream = await content.ReadAsStreamAsync(cancel); using var memory = new MemoryStream();
        var buffer = new byte[1024]; int read;
        while ((read = await stream.ReadAsync(buffer, cancel)) > 0) { if (memory.Length + read > 8192) throw new InvalidDataException("Collector response too large."); memory.Write(buffer, 0, read); }
        return memory.ToArray();
    }
    public static async Task<int> Upload(TelemetrySettings settings, CancellationToken cancel, string? root = null)
    {
        if (!settings.Ready) return 0;
        root ??= Root; if (!Directory.Exists(root)) return 0;
        FileSafety.RejectLink(root); using var http = CreateHttp(); var count = 0;
        foreach (var file in new DirectoryInfo(root).GetFiles("*.json").OrderBy(f=>f.LastWriteTimeUtc))
        {
            if(count>=50)break;
            cancel.ThrowIfCancellationRequested();
            if (!settings.Enabled) break;
            if (!Regex.IsMatch(file.Name, "^[a-f0-9]{32}\\.json$") || file.Length > MaxChunkBytes) continue;
            FileSafety.RejectLink(file.FullName);
            var bytes = await File.ReadAllBytesAsync(file.FullName, cancel);
            JsonDocument parsed;
            try { parsed=JsonDocument.Parse(bytes); if(parsed.RootElement.ValueKind!=JsonValueKind.Object || !parsed.RootElement.TryGetProperty("deviceId",out var device) || device.ValueKind!=JsonValueKind.String || !parsed.RootElement.TryGetProperty("chunkId",out var chunkId) || chunkId.ValueKind!=JsonValueKind.String){parsed.Dispose();throw new JsonException();} }
            catch(JsonException){Quarantine(file.FullName);continue;}
            using var doc = parsed;
            if (doc.RootElement.GetProperty("deviceId").GetString() != settings.DeviceId) continue;
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint.TrimEnd('/') + "/chunks");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token); request.Headers.Add("X-Device-Id", settings.DeviceId);
            using var compressed = new MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionLevel.Fastest, true)) gzip.Write(bytes);
            request.Content = new ByteArrayContent(compressed.ToArray()); request.Content.Headers.ContentType = new("application/json"); request.Content.Headers.ContentEncoding.Add("gzip");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if(response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict){Quarantine(file.FullName);continue;}
            response.EnsureSuccessStatusCode();
            using var acknowledgment = JsonDocument.Parse(await ReadSmall(response.Content, cancel));
            if (!acknowledgment.RootElement.GetProperty("ok").GetBoolean() || acknowledgment.RootElement.GetProperty("id").GetString() != doc.RootElement.GetProperty("chunkId").GetString()) throw new InvalidDataException("Missing upload acknowledgment.");
            File.Delete(file.FullName); count++;
        }
        return count;
    }
    static void Quarantine(string path)
    {
        var target=Path.ChangeExtension(path,"invalid");FileSafety.RejectLink(target);File.Move(path,target,true);
    }
}
