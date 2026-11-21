using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Media;

namespace Clonedivers;

internal static class DesktopProgram
{
    internal static string[] Arguments = Array.Empty<string>();

    [STAThread]
    public static int Main(string[] args)
    {
        args = ForgeEntryPolicy.Normalize(args, ForgeEntryPolicy.ForgeEdition);
        var display = !OperatingSystem.IsLinux() || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var cli = UseTerminal(args, display);
        if (cli) return args.Contains("--forge") ? LinuxForgeTerminal.RunAsync(args).GetAwaiter().GetResult() : LinuxProgram.RunAsync(args).GetAwaiter().GetResult();
        Arguments = args;
        try { return AppBuilder.Configure<LauncherApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex)
        {
            LinuxSupport.Record("error", "Desktop startup: " + ex.Message);
            Console.Error.WriteLine("The desktop window could not open: " + ex.Message + "\nRun from a graphical Linux desktop, or use --terminal for the optional console menu.");
            return 1;
        }
    }

    internal static bool UseTerminal(string[] args, bool hasDisplay)
    {
        var gui = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--game" or "--manifest" or "--ui-smoke" or "--forge-root" or "--forge-resources" or "--forge-preview" or "--forge-preview-size" or "--forge-preview-mode" or "--forge-render") { i++; continue; }
            if (args[i] == "--gui") { gui = true; continue; }
            if (args[i] is "--tui" or "--terminal" or "--plain" or "--help" or "-h" or "--version" or "status" or "install" or "vanilla" or "repair" or "finish" or "launch" or "report" or "record" or "record-setup" or "record-status" or "record-cancel") return true;
            if (!args[i].StartsWith('-')) return true;
        }
        return !gui && !hasDisplay;
    }
}

internal sealed class LauncherApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        var theme = new FluentTheme();
        if (DesktopProgram.Arguments.Contains("--forge") || DesktopProgram.Arguments.Contains("--forge-preview"))
            theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse("#3476E6") };
        Styles.Add(theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (DesktopProgram.Arguments.Contains("--forge") || DesktopProgram.Arguments.Contains("--forge-preview"))
            {
                var host = new LinuxForgeHost(DesktopProgram.Arguments);
                var window = new Clonedivers.Ui.ForgeShellWindow(host, DesktopProgram.Arguments.Contains("details") ? "settings" : null)
                {
                    Width = host.PreviewWidth,
                    Height = host.PreviewHeight
                };
                if (host.RenderOutput is { } outDir)
                {
                    window.Opened += async (_, _) =>
                    {
                        var code = 0;
                        try
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Loaded);
                            await Task.Delay(200);
                            window.UpdateLayout();
                            Directory.CreateDirectory(outDir);
                            foreach (var scale in new[] { 1d, 1.5d })
                            {
                                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                                    new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale)),
                                    new Vector(96 * scale, 96 * scale));
                                bitmap.Render(window);
                                var path = Path.Combine(outDir, $"forge-{host.PreviewName}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png");
                                bitmap.Save(path);
                                Console.WriteLine(path);
                            }
                        }
                        catch (Exception ex) { code = 1; Console.Error.WriteLine(ex); }
                        desktop.Shutdown(code);
                    };
                }
                desktop.MainWindow = window;
            }
            else
            {
                desktop.MainWindow = new LauncherWindow(DesktopProgram.Arguments);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
