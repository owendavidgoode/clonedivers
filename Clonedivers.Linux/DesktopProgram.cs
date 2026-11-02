using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;

namespace Clonedivers;

internal static class DesktopProgram
{
    internal static string[] Arguments = Array.Empty<string>();

    [STAThread]
    public static int Main(string[] args)
    {
        var cli = args.Any(a => a is "--terminal" or "--help" or "-h" or "--version" or "status" or "install" or "vanilla" or "repair" or "finish" or "launch" or "report" or "record" or "record-setup" or "record-status" or "record-cancel");
        if (cli) return LinuxProgram.RunAsync(args).GetAwaiter().GetResult();
        Arguments = args;
        try { return AppBuilder.Configure<LauncherApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex)
        {
            LinuxSupport.Record("error", "Desktop startup: " + ex.Message);
            Console.Error.WriteLine("The desktop window could not open: " + ex.Message + "\nRun from a graphical Linux desktop, or use --terminal for the optional console menu.");
            return 1;
        }
    }
}

internal sealed class LauncherApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new LauncherWindow(DesktopProgram.Arguments);
        base.OnFrameworkInitializationCompleted();
    }
}
