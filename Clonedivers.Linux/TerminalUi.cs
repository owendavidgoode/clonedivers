using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace Clonedivers;

internal sealed record TerminalItem(string Key, string Label, string Detail = "", bool Enabled = true, bool Toggle = false);
internal sealed record TerminalView(string Title, IReadOnlyList<string> Summary, IReadOnlyList<TerminalItem> Items, string? Footer = null);

internal sealed class TerminalUi : IDisposable
{
    readonly bool ansi, color;
    readonly Stopwatch activityClock = Stopwatch.StartNew();
    readonly ConcurrentQueue<ConsoleKeyInfo> keys = new();
    readonly CancellationTokenSource inputStop = new();
    readonly Task? inputPump;
    string lastPhase = "";
    public string Notice { get; set; } = "";
    public bool KeyboardNavigation => ansi;

    public TerminalUi(bool plain, Action? cancel = null)
    {
        ansi = !plain && !Console.IsOutputRedirected && !Console.IsInputRedirected && Environment.GetEnvironmentVariable("TERM") != "dumb";
        color = ansi && Environment.GetEnvironmentVariable("NO_COLOR") is null;
        if (ansi)
        {
            Console.Write("\u001b[?1049h\u001b[?25l\u001b[?2004l");
            inputPump = Task.Run(async () =>
            {
                try
                {
                    while (!inputStop.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) cancel?.Invoke();
                            else if (keys.Count < 4096) keys.Enqueue(key);
                        }
                        else await Task.Delay(20, inputStop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { /* The terminal disconnected. */ }
            });
        }
    }
    public void Dispose()
    {
        inputStop.Cancel(); inputPump?.GetAwaiter().GetResult(); inputStop.Dispose();
        if (ansi) Console.Write((color ? "\u001b[0m" : "") + "\u001b[?25h\u001b[?1049l");
    }

    internal static string Safe(string? value)
    {
        if (value is null) return "";
        value = Regex.Replace(value, @"\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)", "");
        value = Regex.Replace(value, @"\x1b\[[0-?]*[ -/]*[@-~]", "");
        return Regex.Replace(value, @"[\x00-\x1f\x7f-\x9f\u202a-\u202e\u2066-\u2069]", " ").Trim();
    }
    static string Fit(string value, int width)
    {
        value = Safe(value);
        return value.Length <= width ? value : width > 3 ? value[..(width - 3)] + "..." : value[..Math.Max(0, width)];
    }
    static int Width { get { try { return Console.WindowWidth > 0 ? Math.Clamp(Console.WindowWidth - 1, 24, 104) : 80; } catch (IOException) { return 80; } } }
    static int Height { get { try { return Console.WindowHeight > 0 ? Math.Max(10, Console.WindowHeight) : 30; } catch (IOException) { return 30; } } }
    internal static string Render(TerminalView view, int selected, int width, int height, bool color)
    {
        width = Math.Max(18, width); height = Math.Max(8, height);
        string Blue(string s) => color ? "\u001b[38;5;75m" + s + "\u001b[0m" : s;
        var lines = new List<string> { Blue(Fit("CLONEDIVERS  /  LINUX  v" + AppInfo.Version, width)), Fit(view.Title, width), new string('─', width) };
        lines.AddRange(view.Summary.Take(Math.Max(0, height - 9)).Select(s => Fit(s, width)));
        lines.Add("");
        if (view.Items.Count > 0)
        {
            selected = Math.Clamp(selected, 0, view.Items.Count - 1);
            var capacity = Math.Max(1, height - lines.Count - 4);
            var start = Math.Clamp(selected - capacity / 2, 0, Math.Max(0, view.Items.Count - capacity));
            for (var i = start; i < Math.Min(view.Items.Count, start + capacity); i++)
            {
                var item = view.Items[i];
                var text = Fit($"{(i == selected ? "›" : " ")} [{item.Key}] {item.Label}" + (item.Enabled ? "" : "  (unavailable)"), width);
                lines.Add(i == selected ? Blue(text) : item.Enabled || !color ? text : "\u001b[90m" + text + "\u001b[0m");
            }
            lines.Add(""); lines.Add(Fit(view.Items[selected].Detail, width));
        }
        lines.Add(Fit(view.Footer ?? "↑/↓ move · Enter select · number/letter jump · Esc back/quit", width));
        return string.Join('\n', lines.Take(height - 1)) + "\n";
    }
    void Draw(TerminalView view, int selected)
    {
        if (ansi) Console.Write("\u001b[H\u001b[2J" + Render(view, selected, Width, Height, color));
        else Console.WriteLine(Render(view with { Footer = view.Footer ?? "Type a number or letter, then Enter. 0 = back/quit." }, selected, 100, Math.Max(40, view.Items.Count + view.Summary.Count + 10), false).Replace('─', '-').Replace('›', '>'));
    }
    public async Task<string?> SelectAsync(Func<TerminalView> build, CancellationToken ct, string? selectedKey = null)
    {
        var view = build();
        if (view.Items.Count == 0) return null;
        var selected = Math.Max(0, view.Items.ToList().FindIndex(x => x.Key == selectedKey));
        Draw(view, selected);
        if (!ansi)
        {
            while (true)
            {
                Console.Write("Choose (0 = back/quit): ");
                var line = await Console.In.ReadLineAsync(ct); if (line is null) return null;
                line = line.Trim().ToLowerInvariant(); if (line is "q" or "escape") line = "0";
                if (line == "0") return null;
                if (line == "yes") line = "y"; else if (line == "no") line = "n";
                var item = view.Items.FirstOrDefault(x => x.Key.Equals(line, StringComparison.OrdinalIgnoreCase));
                if (item?.Enabled == true) return item.Key == "0" ? null : item.Key;
                Console.WriteLine(item is null ? "Choose a listed number or letter." : item.Detail);
            }
        }
        var refresh = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (keys.TryDequeue(out var key))
            {
                var redraw = true;
                if (key.Key == ConsoleKey.Escape) return null;
                if (key.Key == ConsoleKey.UpArrow) selected = (selected + view.Items.Count - 1) % view.Items.Count;
                else if (key.Key == ConsoleKey.DownArrow) selected = (selected + 1) % view.Items.Count;
                else if (key.Key == ConsoleKey.Home) selected = 0;
                else if (key.Key == ConsoleKey.End) selected = view.Items.Count - 1;
                else if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Spacebar && view.Items[selected].Toggle)
                {
                    if (view.Items[selected].Enabled) return view.Items[selected].Key == "0" ? null : view.Items[selected].Key;
                }
                else
                {
                    var jump = char.ToLowerInvariant(key.KeyChar) is 'q' ? "0" : key.KeyChar.ToString();
                    var index = view.Items.ToList().FindIndex(x => x.Key.Equals(jump, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) selected = index; else redraw = false;
                }
                if (redraw) Draw(view, selected);
            }
            if (refresh.ElapsedMilliseconds >= 1500)
            {
                var keep = view.Items[selected].Key; view = build();
                selected = Math.Max(0, view.Items.ToList().FindIndex(x => x.Key == keep)); Draw(view, selected); refresh.Restart();
            }
            await Task.Delay(35, ct);
        }
    }
    public async Task<bool> ConfirmAsync(string title, string body, CancellationToken ct)
    {
        var view = new TerminalView(title, body.Split('\n'), new[] { new TerminalItem("n", "Cancel", "Keep the current installation."), new TerminalItem("y", "Continue", "Apply the operation described above.") });
        return await SelectAsync(() => view, ct) == "y";
    }
    public async Task<string?> ReadPathAsync(string? current, CancellationToken ct)
    {
        Draw(new TerminalView("GAME FOLDER", new[] { "Steam → Helldivers 2 → Manage → Browse local files", "Paste the folder containing data and bin/helldivers2.exe.", "Current: " + (current ?? "not selected"), "Enter saves · Esc/blank cancels · ~ expands to your home folder" }, Array.Empty<TerminalItem>()), 0);
        Console.Write("Folder: ");
        if (!ansi) return await Console.In.ReadLineAsync(ct);
        var text = new StringBuilder();
        Console.Write("\u001b[?25h");
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (keys.TryDequeue(out var key))
                {
                    if (key.Key == ConsoleKey.Escape) return null;
                    if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return text.ToString(); }
                    if (key.Key == ConsoleKey.Backspace && text.Length > 0)
                    {
                        var remove = text.Length > 1 && char.IsLowSurrogate(text[^1]) && char.IsHighSurrogate(text[^2]) ? 2 : 1;
                        text.Length -= remove; Console.Write("\b \b");
                    }
                    else if (!char.IsControl(key.KeyChar) && text.Length < 4096) { text.Append(key.KeyChar); Console.Write(key.KeyChar); }
                }
                await Task.Delay(35, ct);
            }
        }
        finally { Console.Write("\u001b[?25l"); }
    }
    internal static string NormalizePathInput(string value, string home)
    {
        value = value.Replace("\u001b[200~", "", StringComparison.Ordinal).Replace("\u001b[201~", "", StringComparison.Ordinal)
            .Replace("^[[200~", "", StringComparison.Ordinal).Replace("^[[201~", "", StringComparison.Ordinal).Trim();
        if (value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0]) value = value[1..^1];
        if (value == "~" || value == "$HOME") return home;
        if (value.StartsWith("~/", StringComparison.Ordinal)) return Path.Combine(home, value[2..]);
        if (value.StartsWith("$HOME/", StringComparison.Ordinal)) return Path.Combine(home, value[6..]);
        return value;
    }
    public void Activity(string phase, long done = 0, long total = 0, string? extra = null)
    {
        if (phase == lastPhase && activityClock.ElapsedMilliseconds < (ansi ? 120 : 1000) && done < total) return;
        lastPhase = phase; activityClock.Restart();
        var ratio = total > 0 ? Math.Clamp(done / (double)total, 0, 1) : 0;
        var bar = "[" + new string('=', (int)(ratio * 24)) + new string(' ', 24 - (int)(ratio * 24)) + "]";
        var detail = total > 0 ? $"{bar} {ratio:P0}  {Pack.FormatBytes(done)} / {Pack.FormatBytes(total)}" : "Please wait…";
        if (ansi) Draw(new TerminalView(phase.ToUpperInvariant(), new[] { detail, extra ?? "", "Ctrl+C cancels preparation/downloads. File finalization completes safely." }, Array.Empty<TerminalItem>(), "Interrupted downloads are kept for resuming."), 0);
        else Console.WriteLine(phase + ": " + detail + (extra is null ? "" : "  " + extra));
    }
    public async Task ShowAsync(string title, string body, CancellationToken ct)
    {
        if (!ansi)
        {
            await SelectAsync(() => new TerminalView(title, body.Split('\n'), new[] { new TerminalItem("0", "Back", "Return to the launcher.") }), ct);
            return;
        }
        // Paths and Steam launch options must remain readable even on a small terminal.
        var offset = 0;
        while (true)
        {
            var count = Math.Max(1, Height - 13);
            var lines = body.Split('\n').SelectMany(line => Wrap(line, Width)).ToArray();
            offset = Math.Min(offset, Math.Max(0, lines.Length - 1));
            var view = new TerminalView(title, lines.Skip(offset).Take(count).ToArray(), new[]
            {
                new TerminalItem("n", "Next page", "Read the rest of this message.", offset + count < lines.Length),
                new TerminalItem("p", "Previous page", "Read the preceding lines.", offset > 0),
                new TerminalItem("0", "Back", "Return to the launcher.")
            }, $"Lines {offset + 1}–{Math.Min(offset + count, lines.Length)} of {lines.Length} · Enter select · Esc back");
            var choice = await SelectAsync(() => view, ct, "0");
            if (choice is null) return;
            offset = choice == "n" ? offset + count : Math.Max(0, offset - count);
        }
    }
    internal static IEnumerable<string> Wrap(string line, int width)
    {
        line = Safe(line); width = Math.Max(1, width);
        if (line.Length == 0) yield return "";
        for (var start = 0; start < line.Length; start += width)
            yield return line.Substring(start, Math.Min(width, line.Length - start));
    }
}

internal sealed class TerminalTransfer
{
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    long? initial;
    public string Detail(long done, long total)
    {
        if (initial is null) { initial = done; elapsed.Restart(); }
        var rate = elapsed.Elapsed.TotalSeconds > .2 ? Math.Max(0, done - initial.Value) / elapsed.Elapsed.TotalSeconds : 0;
        if (rate < 1) return "Resumable download · SHA-256 verified";
        var seconds = Math.Max(0, total - done) / rate;
        return $"{Pack.FormatBytes((long)rate)}/s  ·  ETA {(seconds < 3600 ? TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss") : ">1 hour")}";
    }
}
