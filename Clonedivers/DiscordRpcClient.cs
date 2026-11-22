using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Clonedivers;

/// <summary>
/// Lightweight zero-dependency Discord local IPC Rich Presence client
/// (\\.\pipe\discord-ipc-0..9 on Windows, $XDG_RUNTIME_DIR/discord-ipc-0..9 on Linux).
/// Gracefully no-ops if App ID is empty or Discord is not running; never throws.
/// </summary>
public sealed class DiscordRpcClient : IDisposable, IAsyncDisposable
{
    public const string AppIdEnvVar = "CLONEDIVERS_DISCORD_APP_ID";
    public const string DefaultAppId = "";

    public const int OpcodeHandshake = 0;
    public const int OpcodeFrame = 1;
    public const int OpcodeClose = 2;
    public const int OpcodePing = 3;
    public const int OpcodePong = 4;

    readonly string appId;
    readonly Func<CancellationToken, Task<Stream?>>? connectOverride;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly long sessionStartUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    Stream? transport;
    bool handshakeCompleted;
    int disposedFlag;
    long presenceGeneration;

    bool IsDisposed => Volatile.Read(ref disposedFlag) != 0;

    public bool Enabled { get; set; } = true;
    public bool IsConnected => Volatile.Read(ref transport) != null && Volatile.Read(ref handshakeCompleted) && !IsDisposed;
    public string AppId => appId;

    public DiscordRpcClient(string? appId = null, Func<CancellationToken, Task<Stream?>>? connectOverride = null)
    {
        this.appId = ResolveAppId(appId);
        this.connectOverride = connectOverride;
    }

    public static string ResolveAppId(string? explicitAppId = null)
    {
        var candidate = !string.IsNullOrWhiteSpace(explicitAppId)
            ? explicitAppId.Trim()
            : Environment.GetEnvironmentVariable(AppIdEnvVar)?.Trim();
        return string.IsNullOrWhiteSpace(candidate) ? DefaultAppId : candidate;
    }

    public void SetPresence(string state, string details, string largeImageKey = "clonedivers")
    {
        if (IsDisposed || !Enabled || string.IsNullOrWhiteSpace(appId)) return;
        var generation = Interlocked.Increment(ref presenceGeneration);
        _ = Task.Run(() => SetPresenceCoreAsync(state, details, largeImageKey, generation, CancellationToken.None));
    }

    public void ClearPresence()
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(appId)) return;
        var generation = Interlocked.Increment(ref presenceGeneration);
        _ = Task.Run(() => ClearPresenceCoreAsync(generation, CancellationToken.None));
    }

    public Task<bool> SetPresenceAsync(string state, string details, string largeImageKey = "clonedivers", CancellationToken ct = default)
    {
        if (IsDisposed || !Enabled || string.IsNullOrWhiteSpace(appId)) return Task.FromResult(false);
        var generation = Interlocked.Increment(ref presenceGeneration);
        return SetPresenceCoreAsync(state, details, largeImageKey, generation, ct);
    }

    async Task<bool> SetPresenceCoreAsync(string state, string details, string largeImageKey, long generation, CancellationToken ct)
    {
        if (IsDisposed || !Enabled || string.IsNullOrWhiteSpace(appId)) return false;
        if (generation != Volatile.Read(ref presenceGeneration)) return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            await gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Never call ResetConnection() outside gate when WaitAsync times out or is cancelled.
            return false;
        }

        try
        {
            if (IsDisposed || !Enabled || generation != Volatile.Read(ref presenceGeneration))
                return false;
            if (!await EnsureConnectedAsync(timeout.Token).ConfigureAwait(false))
                return false;
            if (IsDisposed || !Enabled || generation != Volatile.Read(ref presenceGeneration))
                return false;

            var activity = new Dictionary<string, object?>
            {
                ["timestamps"] = new { start = sessionStartUnix }
            };
            if (!string.IsNullOrWhiteSpace(state))
                activity["state"] = ClampString(state, 128);
            if (!string.IsNullOrWhiteSpace(details))
                activity["details"] = ClampString(details, 128);
            if (!string.IsNullOrWhiteSpace(largeImageKey))
            {
                activity["assets"] = new
                {
                    large_image = ClampString(largeImageKey, 64),
                    large_text = "Clonedivers"
                };
            }

            var payload = JsonSerializer.Serialize(new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Environment.ProcessId,
                    activity
                },
                nonce = Guid.NewGuid().ToString("N")
            });

            await WriteFrameAsync(transport!, OpcodeFrame, payload, timeout.Token).ConfigureAwait(false);
            var reply = await ReadFrameAsync(transport!, timeout.Token).ConfigureAwait(false);
            if (reply == null || reply.Value.Opcode == OpcodeClose)
            {
                ResetConnection();
                return false;
            }
            return true;
        }
        catch
        {
            ResetConnection();
            return false;
        }
        finally
        {
            try { gate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    public Task<bool> ClearPresenceAsync(CancellationToken ct = default)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(appId)) return Task.FromResult(false);
        var generation = Interlocked.Increment(ref presenceGeneration);
        return ClearPresenceCoreAsync(generation, ct);
    }

    async Task<bool> ClearPresenceCoreAsync(long generation, CancellationToken ct)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(appId)) return false;
        if (generation != Volatile.Read(ref presenceGeneration)) return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Never call ResetConnection() outside gate when WaitAsync times out or is cancelled.
            return false;
        }

        try
        {
            if (IsDisposed || generation != Volatile.Read(ref presenceGeneration))
                return false;
            if (transport == null || !handshakeCompleted)
                return true; // Already cleared / not connected

            var payload = JsonSerializer.Serialize(new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Environment.ProcessId,
                    activity = (object?)null
                },
                nonce = Guid.NewGuid().ToString("N")
            });

            await WriteFrameAsync(transport, OpcodeFrame, payload, timeout.Token).ConfigureAwait(false);
            var reply = await ReadFrameAsync(transport, timeout.Token).ConfigureAwait(false);
            if (reply == null || reply.Value.Opcode == OpcodeClose)
            {
                ResetConnection();
                return false;
            }
            return true;
        }
        catch
        {
            ResetConnection();
            return false;
        }
        finally
        {
            try { gate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (transport != null && handshakeCompleted)
            return true;

        ResetConnection();
        var stream = connectOverride != null
            ? await connectOverride(ct).ConfigureAwait(false)
            : await OpenLocalIpcStreamAsync(ct).ConfigureAwait(false);

        if (stream == null) return false;
        transport = stream;

        var handshakeJson = JsonSerializer.Serialize(new { v = 1, client_id = appId });
        await WriteFrameAsync(stream, OpcodeHandshake, handshakeJson, ct).ConfigureAwait(false);
        var response = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
        if (response == null || response.Value.Opcode == OpcodeClose)
        {
            ResetConnection();
            return false;
        }

        Volatile.Write(ref handshakeCompleted, true);
        return true;
    }

    static async Task<Stream?> OpenLocalIpcStreamAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            for (int i = 0; i <= 9; i++)
            {
                ct.ThrowIfCancellationRequested();
                NamedPipeClientStream? pipe = null;
                try
                {
                    pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(150, ct).ConfigureAwait(false);
                    return pipe;
                }
                catch
                {
                    try { pipe?.Dispose(); } catch { }
                }
            }
            return null;
        }

        if (OperatingSystem.IsLinux())
        {
            foreach (var socketPath in CandidateLinuxSocketPaths())
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(socketPath)) continue;
                Socket? socket = null;
                try
                {
                    socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    using var connectBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    connectBudget.CancelAfter(150);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), connectBudget.Token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    try { socket?.Dispose(); } catch { }
                }
            }
        }

        return null;
    }

    public static IReadOnlyList<string> CandidateLinuxSocketPaths(string? xdgRuntimeDir = null, string? tmpDir = null)
    {
        var roots = new List<string>();
        var xdg = xdgRuntimeDir ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            roots.Add(xdg);
            roots.Add(Path.Combine(xdg, "app", "com.discordapp.Discord"));
            roots.Add(Path.Combine(xdg, "snap.discord"));
        }
        var temp = tmpDir
            ?? Environment.GetEnvironmentVariable("TMPDIR")
            ?? Environment.GetEnvironmentVariable("TMP")
            ?? Environment.GetEnvironmentVariable("TEMP")
            ?? "/tmp";
        if (!string.IsNullOrWhiteSpace(temp))
            roots.Add(temp);

        var paths = new List<string>();
        foreach (var root in roots.Distinct(StringComparer.Ordinal))
        {
            for (int i = 0; i <= 9; i++)
                paths.Add(Path.Combine(root, $"discord-ipc-{i}"));
        }
        return paths;
    }

    public static async Task WriteFrameAsync(Stream stream, int opcode, string jsonPayload, CancellationToken ct = default)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);
        var frame = new byte[8 + payloadBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), payloadBytes.Length);
        Buffer.BlockCopy(payloadBytes, 0, frame, 8, payloadBytes.Length);
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<(int Opcode, string Json)?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[8];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
            return null;

        var opcode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (length < 0 || length > 64 * 1024)
            return null;

        var body = new byte[length];
        if (length > 0 && !await ReadExactAsync(stream, body, ct).ConfigureAwait(false))
            return null;

        return (opcode, Encoding.UTF8.GetString(body));
    }

    static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read <= 0) return false;
            offset += read;
        }
        return true;
    }

    static string ClampString(string value, int maxChars)
    {
        var clean = value.Trim();
        return clean.Length <= maxChars ? clean : clean[..maxChars];
    }

    void ResetConnection()
    {
        Volatile.Write(ref handshakeCompleted, false);
        var current = Interlocked.Exchange(ref transport, null);
        try { current?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposedFlag, 1) != 0) return;
        Interlocked.Increment(ref presenceGeneration);
        ResetConnection();
        gate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed) return;
        try { await ClearPresenceAsync().ConfigureAwait(false); } catch { }
        Dispose();
    }
}
