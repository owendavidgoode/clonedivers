namespace Clonedivers;

public static class DesktopShortcut
{
    public static string QuoteArgument(string value)
    {
        if (value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new InvalidDataException("Invalid desktop shortcut path.");
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal) + "\"";
    }

    public static string Install()
    {
        var root = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var apps = Path.Combine(root, "applications"); var icons = Path.Combine(root, "icons", "hicolor", "scalable", "apps");
        FileSafety.RejectLink(apps); FileSafety.RejectLink(icons); Directory.CreateDirectory(apps); Directory.CreateDirectory(icons);
        using var icon = typeof(DesktopShortcut).Assembly.GetManifestResourceStream("Clonedivers.desktop.svg")!;
        using var reader = new StreamReader(icon);
        LinuxSupport.WritePrivate(FileSafety.Child(icons, "clonedivers.svg"), reader.ReadToEnd());
        var exe = Environment.ProcessPath ?? throw new IOException("Launcher executable path is unavailable.");
        var command = QuoteArgument(exe);
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) command += " " + QuoteArgument(Path.Combine(AppContext.BaseDirectory, "clonedivers.dll"));
        var path = FileSafety.Child(apps, "clonedivers.desktop");
        LinuxSupport.WritePrivate(path, $"[Desktop Entry]\nType=Application\nName=Clonedivers\nComment=Choose your universe and launch through Steam\nExec={command} --gui\nIcon=clonedivers\nTerminal=false\nCategories=Game;\nStartupWMClass=Clonedivers\n");
        return path;
    }
}
