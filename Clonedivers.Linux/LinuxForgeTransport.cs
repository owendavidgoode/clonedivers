using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Clonedivers;

internal sealed class ForgeCommandException(string message) : IOException(message);

/// <summary>Serialized bounded protocol; cancellation after write never abandons a committing CLI reply.</summary>
internal sealed class ForgeProtocol(TextReader input, TextWriter output, Action<string> diagnostic)
{
    // prepare-pack includes hash verification, backup, a bounded official import and possible recovery.
    // Do not abandon its authoritative reply at the direct mod-add deadline.
    internal static TimeSpan ResultBudget(string? command) => TimeSpan.FromMinutes(command switch {
        "prepare-pack" or "prepare-regular" => 45,
        "install-gameplay" => 35,
        "recover-gameplay" => 15,
        "mod-add" or "account-import" or "recover-pack" => 20,
        _ => 3
    });
    readonly SemaphoreSlim requests = new(1, 1);
    bool connected, faulted;
    long nextId;
    internal async Task ConnectAsync(CancellationToken ct)
    {
        if (connected || faulted) throw new InvalidOperationException("Forge protocol already initialized or closed.");
        try
        {
            for (var i = 0; i < 256; i++)
            {
                var line = await ReadLineAsync(input, 4 * 1024 * 1024, ct) ?? throw new EndOfStreamException("Forge bridge closed before its handshake.");
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("bridge", out var name) && name.GetString() == "clonedivers-forge")
                    {
                        if (doc.RootElement.GetProperty("format").GetInt32() != 1) throw new InvalidDataException("Unsupported Forge bridge protocol.");
                        connected = true; return;
                    }
                }
                catch (JsonException) { }
                diagnostic(line);
            }
            throw new InvalidDataException("Forge bridge handshake was not found within the diagnostic limit.");
        }
        catch { faulted = true; throw; }
    }

    internal async Task<JsonElement> SendAsync(IReadOnlyList<string> args, CancellationToken ct = default, TimeSpan? limit = null)
    {
        await requests.WaitAsync(ct);
        try
        {
            if (!connected || faulted) throw new InvalidOperationException("Forge bridge is not connected; review cleanup before reconnecting.");
            ct.ThrowIfCancellationRequested();
            var id = checked(++nextId);
            var line = JsonSerializer.Serialize(new { id, args });
            if (line.Length > 16 * 1024) throw new ArgumentException("Forge request exceeds the bridge limit.");
            using var timeout = new CancellationTokenSource(limit ?? ResultBudget(args.FirstOrDefault()));
            try
            {
                // Once sent, await the authoritative result. A user cancellation cannot imply rollback.
                await output.WriteLineAsync(line.AsMemory(), timeout.Token);
                await output.FlushAsync(timeout.Token);
                for (var i = 0; i < 256; i++)
                {
                    var response = await ReadLineAsync(input, 4 * 1024 * 1024, timeout.Token) ?? throw new EndOfStreamException("Forge bridge closed before the command result; do not retry mutations blindly.");
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(response); }
                    catch (JsonException) { diagnostic(response); continue; }
                    using (doc)
                    {
                        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("id", out var key)) { diagnostic(response); continue; }
                        if (key.ValueKind != JsonValueKind.Number || key.GetInt64() != id) throw new InvalidDataException("Forge response id does not match the pending request.");
                        if (!doc.RootElement.GetProperty("ok").GetBoolean())
                            throw new ForgeCommandException(doc.RootElement.GetProperty("error").GetString() ?? "Official Forge command failed.");
                        return doc.RootElement.GetProperty("result").Clone();
                    }
                }
                throw new InvalidDataException("Forge result was not found within the diagnostic limit.");
            }
            catch (ForgeCommandException) { throw; } // a definitive failure preserves protocol synchronization
            catch { faulted = true; throw; } // an unknown outcome must never reuse/replay this stream
        }
        finally { requests.Release(); }
    }

    internal static async Task<string?> ReadLineAsync(TextReader input, int limit, CancellationToken ct)
    {
        var result = new StringBuilder(); var one = new char[1];
        while (await input.ReadAsync(one.AsMemory(), ct) > 0)
        {
            if (one[0] == '\n') return result.ToString().TrimEnd('\r');
            if (result.Length >= limit) throw new InvalidDataException("Forge protocol line exceeds the size limit.");
            result.Append(one[0]);
        }
        return result.Length > 0 ? result.ToString() : null;
    }
}

/// <summary>Owns one official wrapper, without killing Wine, Steam or game descendants.</summary>
internal sealed class LinuxForgeTransport : IForgeConnection
{
    readonly Process process;
    readonly ForgeProtocol protocol;
    readonly Queue<string> diagnostics = new();
    readonly Task errorReader;
    bool disposed;
    LinuxForgeTransport(Process process)
    {
        this.process = process;
        protocol = new(process.StandardOutput, process.StandardInput, Record);
        errorReader = DrainErrorsAsync(process.StandardError);
    }
    void Record(string value)
    {
        lock (diagnostics) { diagnostics.Enqueue(value.Length > 1024 ? value[..1024] : value); while (diagnostics.Count > 40) diagnostics.Dequeue(); }
    }
    internal string Diagnostics { get { lock (diagnostics) return string.Join("\n", diagnostics); } }
    async Task DrainErrorsAsync(TextReader errors)
    {
        var buffer = new char[1024]; int count;
        try { while ((count = await errors.ReadAsync(buffer)) > 0) Record(new string(buffer, 0, count)); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
    }
    internal static async Task<LinuxForgeTransport> StartAsync(LinuxForgePlan plan, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The official Forge namespace wrapper runs on Linux.");
        var process = Process.Start(plan.StartInfo()) ?? throw new IOException("Official Forge Linux script did not start.");
        var transport = new LinuxForgeTransport(process);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startup.CancelAfter(TimeSpan.FromMinutes(20)); // official script may obtain pinned mod tools on first start
        try { await transport.protocol.ConnectAsync(startup.Token); return transport; }
        catch (Exception error)
        {
            try { await transport.CloseAsync(checkExitCode: false); } catch (Exception cleanup) { throw new IOException("Forge startup failed and wrapper cleanup is unconfirmed. " + transport.Diagnostics, new AggregateException(error, cleanup)); }
            throw new IOException("Forge startup did not complete. " + transport.Diagnostics, error);
        }
    }
    public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return protocol.SendAsync(args, ct);
    }
    public ValueTask DisposeAsync() => CloseAsync(checkExitCode: true);
    async ValueTask CloseAsync(bool checkExitCode)
    {
        if (disposed) return;
        disposed = true;
        // Frontend must await pending commands and confirm End session before disposing.
        // EOF is an additional bridge-side official cleanup safeguard after a frontend failure.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            try { process.StandardInput.Close(); } catch (IOException) { } catch (ObjectDisposedException) { }
            var drain = DrainOutputAsync(cleanup.Token);
            await process.WaitForExitAsync(cleanup.Token);
            await drain;
            await errorReader.WaitAsync(cleanup.Token);
            if (checkExitCode && process.ExitCode != 0) throw new IOException("Official Linux wrapper ended abnormally; review its logs. " + Diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Official Forge wrapper cleanup is not confirmed. Keep the logs and check the session before starting another instance. " + Diagnostics);
        }
        finally { process.StandardOutput.Dispose(); process.StandardError.Dispose(); process.Dispose(); }
    }
    async Task DrainOutputAsync(CancellationToken ct)
    {
        var buffer = new char[4096];
        while (await process.StandardOutput.ReadAsync(buffer.AsMemory(), ct) > 0) { }
    }
}
