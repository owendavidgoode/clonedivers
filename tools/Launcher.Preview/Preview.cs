namespace Clonedivers;

static class LauncherPreview
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 10F));
        string? render = null;
        Manifest? manifest = null;
        var mode = LauncherMode.Clonedivers;
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length) throw new ArgumentException("Preview options require a value.");
            if (args[i] == "--render") render = args[i + 1];
            else if (args[i] == "--manifest") manifest = Manifest.Parse(File.ReadAllText(args[i + 1]));
            else if (args[i] == "--mode") mode = Enum.Parse<LauncherMode>(args[i + 1], true);
            else throw new ArgumentException("Unknown preview option: " + args[i]);
        }
        using var form = new MainForm(new Settings(), null, preview: true, previewManifest: manifest);
        typeof(MainForm).GetMethod("OnMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, new object[] { mode });
        if (render is not null)
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
            form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.GetFullPath(render), System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        Application.Run(form);
    }
}
