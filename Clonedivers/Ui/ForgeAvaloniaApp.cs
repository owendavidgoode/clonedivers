using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Application = Avalonia.Application;
using Color = Avalonia.Media.Color;

namespace Clonedivers.Ui;

public sealed class ForgeAvaloniaApp : Application
{
    internal static Func<ForgeShellWindow>? WindowFactory;
    internal static string? RenderAllOutputDir;
    internal static Action<ForgeShellWindow>? OnWindowCreated;
    internal static ForgeShellWindow? ActiveShellWindow;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        var theme = new FluentTheme();
        theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources
        {
            Accent = Color.Parse("#38BDF8"),
            RegionColor = ForgeTheme.BgCanvasColor
        };
        Styles.Add(theme);
        Styles.Add(ForgeTheme.BuildGlobalStyles());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine("Unhandled UI exception: " + e.Exception);
            try { ActiveShellWindow?.ShowUnhandledErrorToast(e.Exception); }
            catch { }
            e.Handled = true;
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!string.IsNullOrWhiteSpace(RenderAllOutputDir))
            {
                UiSoundEffects.Enabled = false;
                var outDir = RenderAllOutputDir!;
                var firstHost = new PreviewForgeHost(PreviewForgeHost.AllPresets[0]);
                var window = new ForgeShellWindow(firstHost, firstHost.InitialOverlay);
                ActiveShellWindow = window;
                desktop.MainWindow = window;
                window.Opened += async (_, _) =>
                {
                    var code = 0;
                    try
                    {
                        await window.RenderSnapshotAsync(outDir, PreviewForgeHost.AllPresets[0]);
                        for (int i = 1; i < PreviewForgeHost.AllPresets.Length; i++)
                        {
                            var preset = PreviewForgeHost.AllPresets[i];
                            var h = new PreviewForgeHost(preset);
                            var w = new ForgeShellWindow(h, h.InitialOverlay);
                            ActiveShellWindow = w;
                            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                            w.Opened += (_, _) => shown.TrySetResult();
                            w.Show();
                            await shown.Task;
                            await w.RenderSnapshotAsync(outDir, preset);
                            w.Close();
                        }
                        Console.WriteLine($"Rendered {PreviewForgeHost.AllPresets.Length * 2} screenshots to {outDir}");
                    }
                    catch (Exception ex)
                    {
                        code = 1;
                        Console.Error.WriteLine(ex);
                    }
                    desktop.Shutdown(code);
                };
            }
            else if (WindowFactory != null)
            {
                var window = WindowFactory();
                ActiveShellWindow = window;
                desktop.MainWindow = window;
                OnWindowCreated?.Invoke(window);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static int RunDesktop(string[] args, Func<ForgeShellWindow> windowFactory, Action<ForgeShellWindow>? onWindowCreated = null)
    {
        RenderAllOutputDir = null;
        WindowFactory = windowFactory;
        OnWindowCreated = onWindowCreated;
        return AppBuilder.Configure<ForgeAvaloniaApp>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    public static int RunRenderAll(string[] args, string outputDir)
    {
        RenderAllOutputDir = outputDir;
        WindowFactory = null;
        OnWindowCreated = null;
        return AppBuilder.Configure<ForgeAvaloniaApp>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
