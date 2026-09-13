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
        using var form = new MainForm(new Settings(), null, preview: true);
        if (args.Length == 2 && args[0] == "--render")
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
            bitmap.Save(Path.GetFullPath(args[1]), System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        Application.Run(form);
    }
}
