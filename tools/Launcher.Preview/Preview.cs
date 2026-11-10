namespace Clonedivers;

static class LauncherPreview
{
    [STAThread]
    static void Main(string[] args)
    {
        var requestedScale = Array.IndexOf(args, "--scale");
        var dpiScale = requestedScale >= 0 && requestedScale + 1 < args.Length ? args[requestedScale + 1] : null;
        Application.SetHighDpiMode(dpiScale == "1" ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 10F));
        string? render = null;
        string? icons = null;
        string? forge = null;
        float? scale = null;
        Manifest? manifest = null;
        var mode = LauncherMode.Clonedivers;
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length) throw new ArgumentException("Preview options require a value.");
            if (args[i] == "--render") render = args[i + 1];
            else if (args[i] == "--icons") icons = args[i + 1];
            else if (args[i] == "--manifest") manifest = Manifest.Parse(File.ReadAllText(args[i + 1]));
            else if (args[i] == "--mode") mode = Enum.Parse<LauncherMode>(args[i + 1], true);
            else if (args[i] == "--forge") forge = args[i + 1];
            else if (args[i] == "--scale") scale = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            else throw new ArgumentException("Unknown preview option: " + args[i]);
        }
        if (icons is not null)
        {
            using var bitmap = new Bitmap(740, 450);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Palette.Bg);
            using var font = new Font("Segoe UI", 10F);
            using var text = new SolidBrush(Palette.TextMain);
            int x = 20;
            foreach (var size in new[] { 32, 48, 64, 82, 123, 164 })
            {
                g.DrawString($"{size} px", font, text, x, 16);
                ModeHelmets.Draw(g, new RectangleF(x, 48, size, size), mode, true);
                ModeHelmets.Draw(g, new RectangleF(x, 252, size, size), mode, false);
                x += size + 25;
            }
            bitmap.Save(Path.GetFullPath(icons), System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        if (scale is not (null or 1 or 1.5F)) throw new ArgumentException("Supported preview scales are 1 and 1.5, or omit --scale for native DPI.");
        if (forge == "all")
        {
            if (render == null) throw new ArgumentException("The Forge review loop requires --render <directory>.");
            Directory.CreateDirectory(render);
            foreach (var state in HellForgeForm.PreviewStates)
            foreach (var selected in state == "ready" ? new[] { LauncherMode.Clonedivers, LauncherMode.Helldivers, LauncherMode.EmpireDivers } : new[] { LauncherMode.Clonedivers })
            {
                var name = selected == LauncherMode.Helldivers ? "regular" : selected == LauncherMode.EmpireDivers ? "empire" : "clones";
                using var sample = CreateForm(state, selected);
                Render(sample, Path.Combine(render, $"{name}-{state}-{scale?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "native"}.png"), scale);
            }
            return;
        }
        using var form = CreateForm(forge, mode);
        if (render != null) Render(form, render, scale);
        else Application.Run(form);

        Form CreateForm(string? state, LauncherMode selected)
        {
            Form window = state != null
            ? new HellForgeForm(new HellForgePreferences { Mode = selected == LauncherMode.Helldivers ? "regular" : selected == LauncherMode.EmpireDivers ? "empire" : "clonedivers" }, preview: true)
            : new MainForm(new Settings(), null, preview: true, previewManifest: manifest);
            if (window is HellForgeForm forgeForm) forgeForm.SetPreviewState(state!);
            else typeof(MainForm).GetMethod("OnMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, new object[] { selected });
            return window;
        }
    }
    static void Render(Form form, string render, float? scale)
    {
        // Render our own controls without displaying a window or accessing the game/profile.
        _ = form.Handle;
        void Prepare(Control control)
        {
            _ = control.Handle;
            foreach (Control child in control.Controls) Prepare(child);
            control.PerformLayout();
        }
        Prepare(form);
        if (scale is { } requested && form.DeviceDpi != (int)(96 * requested)) throw new InvalidOperationException($"Requested {requested * 100}% but this desktop renders at {form.DeviceDpi} DPI. Set the desktop scaling to 150% for --scale 1.5.");
        Console.WriteLine($"Preview DPI {form.DeviceDpi}, scale {scale ?? form.DeviceDpi / 96f}, client {form.ClientSize}");
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.GetFullPath(render), System.Drawing.Imaging.ImageFormat.Png);
    }
}
