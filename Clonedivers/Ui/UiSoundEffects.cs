using System.Buffers.Binary;
using System.Diagnostics;

namespace Clonedivers.Ui;

public enum UiSoundKind
{
    ButtonBlip,
    ModeSwitch,
    LaunchConfirm,
    Completion
}

/// <summary>
/// Procedurally synthesizes crisp, quiet, tasteful Star Wars holoterminal WAV clips in memory
/// (zero external audio files or licensing issues) and plays them asynchronously without blocking the UI.
/// </summary>
public static class UiSoundEffects
{
    public const int SampleRate = 44100;

    static readonly Lazy<byte[]> BlipWav = new(SynthesizeButtonBlip);
    static readonly Lazy<byte[]> ModeSwitchWav = new(SynthesizeModeSwitch);
    static readonly Lazy<byte[]> LaunchConfirmWav = new(SynthesizeLaunchConfirm);
    static readonly Lazy<byte[]> CompletionWav = new(SynthesizeCompletion);
    static readonly SemaphoreSlim PlaybackGate = new(2, 2);

    public static bool Enabled { get; set; } = true;

    /// <summary>Optional test hook overriding OS audio output during unit tests.</summary>
    public static Action<UiSoundKind, byte[]>? CustomPlaybackHandler { get; set; }

    public static byte[] GetWavBytes(UiSoundKind kind)
    {
        var source = GetCachedBuffer(kind);
        var copy = new byte[source.Length];
        Buffer.BlockCopy(source, 0, copy, 0, source.Length);
        return copy;
    }

    public static void PlayButtonBlip() => Play(UiSoundKind.ButtonBlip);
    public static void PlayModeSwitch() => Play(UiSoundKind.ModeSwitch);
    public static void PlayLaunchConfirm() => Play(UiSoundKind.LaunchConfirm);
    public static void PlayCompletion() => Play(UiSoundKind.Completion);

    public static void Play(UiSoundKind kind)
    {
        if (!Enabled) return;

        var wav = GetCachedBuffer(kind);
        if (CustomPlaybackHandler is { } handler)
        {
            try { handler(kind, wav); } catch { }
            return;
        }

        _ = Task.Run(async () =>
        {
            if (!await PlaybackGate.WaitAsync(0).ConfigureAwait(false))
                return;
            try
            {
                if (!Enabled) return;
                await PlayCoreAsync(kind, wav).ConfigureAwait(false);
            }
            catch
            {
                // Audio output unavailability must never surface an exception.
            }
            finally
            {
                PlaybackGate.Release();
            }
        });
    }

    static byte[] GetCachedBuffer(UiSoundKind kind) => kind switch
    {
        UiSoundKind.ButtonBlip => BlipWav.Value,
        UiSoundKind.ModeSwitch => ModeSwitchWav.Value,
        UiSoundKind.LaunchConfirm => LaunchConfirmWav.Value,
        UiSoundKind.Completion => CompletionWav.Value,
        _ => BlipWav.Value
    };

    static async Task PlayCoreAsync(UiSoundKind kind, byte[] wav)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            using var stream = new MemoryStream(wav, writable: false);
            using var player = new System.Media.SoundPlayer(stream);
            player.PlaySync(); // Runs on background Task.Run thread so MemoryStream stays alive for the whole clip.
            await Task.CompletedTask;
            return;
        }
#endif
        if (OperatingSystem.IsLinux())
        {
            await PlayLinuxAsync(kind, wav).ConfigureAwait(false);
        }
    }

    static async Task PlayLinuxAsync(UiSoundKind kind, byte[] wav)
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var baseDir = !string.IsNullOrWhiteSpace(runtimeDir) && Path.IsPathFullyQualified(runtimeDir) && Directory.Exists(runtimeDir)
            ? Path.Combine(runtimeDir, "clonedivers-sfx")
            : Path.Combine(Path.GetTempPath(), $"clonedivers-sfx-{Environment.UserName}");

        FileSafety.RejectLink(baseDir);
        Directory.CreateDirectory(baseDir);
        if (OperatingSystem.IsLinux())
        {
            try { File.SetUnixFileMode(baseDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch { }
        }

        var filePath = FileSafety.Child(baseDir, $"{kind.ToString().ToLowerInvariant()}.wav");
        if (!File.Exists(filePath) || new FileInfo(filePath).Length != wav.Length)
        {
            var tmp = FileSafety.Child(baseDir, $"{kind.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(tmp, wav).ConfigureAwait(false);
            File.Move(tmp, filePath, overwrite: true);
        }

        foreach (var (tool, extraArg) in new[] { ("paplay", (string?)null), ("aplay", "-q") })
        {
            var exe = ResolveOnPath(tool);
            if (exe == null) continue;

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            if (extraArg != null) psi.ArgumentList.Add(extraArg);
            psi.ArgumentList.Add(filePath);

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) continue;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    try { proc.Kill(); } catch { }
                    continue;
                }
                if (proc.ExitCode == 0)
                    return;
            }
            catch
            {
                // Try next fallback player (e.g. aplay when paplay fails or is unavailable).
            }
        }
    }

    static string? ResolveOnPath(string commandName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(dir)) continue;
            var candidate = Path.Combine(dir, commandName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ── Procedural Star Wars Holoterminal Synthesis (44.1 kHz, 16-bit PCM Mono) ──

    static byte[] SynthesizeButtonBlip()
    {
        // Crisp 48 ms Republic holoterminal micro-chirp (1318.5 Hz E6 -> 1760 Hz A6), quiet & click-free.
        const double durationSec = 0.048;
        int samples = (int)(SampleRate * durationSec);
        var pcm = new double[samples];
        double phase = 0d;

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double freq = t < 0.018 ? 1318.51 : 1760.00;
            phase += 2.0 * Math.PI * freq / SampleRate;
            double env = SmoothEnvelope(i, samples, attackMs: 2.5, releaseMs: 12.0) * Math.Exp(-t * 36.0);
            double wave = Math.Sin(phase) + 0.22 * Math.Sin(2.0 * phase);
            pcm[i] = wave * env * 0.095;
        }
        return EncodePcm16Wav(pcm);
    }

    static byte[] SynthesizeModeSwitch()
    {
        // 145 ms holoprojector fifth-interval lock chime (D5 587.33 Hz -> A5 880 Hz with subtle harmonic shimmer).
        const double durationSec = 0.145;
        int samples = (int)(SampleRate * durationSec);
        var pcm = new double[samples];

        AddTone(pcm, startSec: 0.000, lengthSec: 0.075, freqHz: 587.33, gain: 0.11, decayRate: 22.0, shimmer: 0.25);
        AddTone(pcm, startSec: 0.048, lengthSec: 0.097, freqHz: 880.00, gain: 0.12, decayRate: 18.0, shimmer: 0.20);
        ApplyMasterFadeEdges(pcm, fadeMs: 3.0);
        return EncodePcm16Wav(pcm);
    }

    static byte[] SynthesizeLaunchConfirm()
    {
        // 245 ms bridge command confirmation triad (A4 440 Hz -> E5 659.25 Hz -> A5 880 Hz + warm A3 undertone).
        const double durationSec = 0.245;
        int samples = (int)(SampleRate * durationSec);
        var pcm = new double[samples];

        AddTone(pcm, startSec: 0.000, lengthSec: 0.180, freqHz: 220.00, gain: 0.055, decayRate: 10.0, shimmer: 0.10);
        AddTone(pcm, startSec: 0.000, lengthSec: 0.085, freqHz: 440.00, gain: 0.105, decayRate: 18.0, shimmer: 0.22);
        AddTone(pcm, startSec: 0.055, lengthSec: 0.095, freqHz: 659.25, gain: 0.110, decayRate: 16.0, shimmer: 0.20);
        AddTone(pcm, startSec: 0.110, lengthSec: 0.135, freqHz: 880.00, gain: 0.125, decayRate: 14.0, shimmer: 0.18);
        ApplyMasterFadeEdges(pcm, fadeMs: 4.0);
        return EncodePcm16Wav(pcm);
    }

    static byte[] SynthesizeCompletion()
    {
        // 280 ms warm holoterminal completion arpeggio (C5 -> E5 -> G5 -> C6).
        const double durationSec = 0.280;
        int samples = (int)(SampleRate * durationSec);
        var pcm = new double[samples];

        AddTone(pcm, startSec: 0.000, lengthSec: 0.080, freqHz: 523.25, gain: 0.095, decayRate: 20.0, shimmer: 0.18);
        AddTone(pcm, startSec: 0.052, lengthSec: 0.085, freqHz: 659.25, gain: 0.100, decayRate: 18.0, shimmer: 0.18);
        AddTone(pcm, startSec: 0.104, lengthSec: 0.095, freqHz: 783.99, gain: 0.105, decayRate: 16.0, shimmer: 0.16);
        AddTone(pcm, startSec: 0.156, lengthSec: 0.124, freqHz: 1046.50, gain: 0.115, decayRate: 14.0, shimmer: 0.14);
        ApplyMasterFadeEdges(pcm, fadeMs: 4.0);
        return EncodePcm16Wav(pcm);
    }

    static void AddTone(double[] buffer, double startSec, double lengthSec, double freqHz, double gain, double decayRate, double shimmer)
    {
        int startIdx = Math.Clamp((int)(startSec * SampleRate), 0, buffer.Length);
        int count = Math.Clamp((int)(lengthSec * SampleRate), 0, buffer.Length - startIdx);
        if (count <= 0) return;

        double phase = 0d;
        double inc = 2.0 * Math.PI * freqHz / SampleRate;
        for (int i = 0; i < count; i++)
        {
            double localSec = (double)i / SampleRate;
            phase += inc;
            double env = SmoothEnvelope(i, count, attackMs: 3.5, releaseMs: 14.0) * Math.Exp(-localSec * decayRate);
            double sample = (Math.Sin(phase) + shimmer * Math.Sin(2.0 * phase) + 0.08 * Math.Sin(3.0 * phase)) * env * gain;
            buffer[startIdx + i] += sample;
        }
    }

    static double SmoothEnvelope(int sampleIndex, int totalSamples, double attackMs, double releaseMs)
    {
        int attackSamples = Math.Max(1, (int)(SampleRate * attackMs / 1000.0));
        int releaseSamples = Math.Max(1, (int)(SampleRate * releaseMs / 1000.0));

        double env = 1.0;
        if (sampleIndex < attackSamples)
        {
            double u = (double)sampleIndex / attackSamples;
            env *= 0.5 * (1.0 - Math.Cos(Math.PI * u));
        }
        int fromEnd = totalSamples - 1 - sampleIndex;
        if (fromEnd < releaseSamples)
        {
            double u = Math.Max(0.0, (double)fromEnd / releaseSamples);
            env *= 0.5 * (1.0 - Math.Cos(Math.PI * u));
        }
        return env;
    }

    static void ApplyMasterFadeEdges(double[] pcm, double fadeMs)
    {
        int fade = Math.Min(pcm.Length / 2, Math.Max(1, (int)(SampleRate * fadeMs / 1000.0)));
        for (int i = 0; i < fade; i++)
        {
            double w = 0.5 * (1.0 - Math.Cos(Math.PI * i / fade));
            pcm[i] *= w;
            pcm[pcm.Length - 1 - i] *= w;
        }
        pcm[0] = 0d;
        pcm[^1] = 0d;
    }

    static byte[] EncodePcm16Wav(double[] samples)
    {
        int dataBytes = samples.Length * 2;
        var wav = new byte[44 + dataBytes];

        // RIFF WAVE header
        wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4, 4), 36 + dataBytes);
        wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';

        // fmt subchunk (16 bytes, PCM = 1, Mono = 1, 44100 Hz, 16-bit)
        wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20, 2), 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22, 2), 1); // 1 channel
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24, 4), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28, 4), SampleRate * 2); // ByteRate
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32, 2), 2); // BlockAlign
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34, 2), 16); // BitsPerSample

        // data subchunk
        wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40, 4), dataBytes);

        int offset = 44;
        for (int i = 0; i < samples.Length; i++)
        {
            double clamped = Math.Clamp(samples[i], -0.95, 0.95);
            short s16 = (short)Math.Round(clamped * 32767.0);
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(offset, 2), s16);
            offset += 2;
        }

        return wav;
    }
}
