using System.Drawing.Drawing2D;
using static Clonedivers.Palette;

namespace Clonedivers;

public enum LauncherMode { Helldivers, Clonedivers, CommandoDivers }

public static class LauncherModes
{
    public const string CommandoOption = "commandos";
    public static string Name(LauncherMode mode) => mode switch
    {
        LauncherMode.Helldivers => "Helldivers",
        LauncherMode.Clonedivers => "Clonedivers",
        _ => "Commandodivers",
    };

    public static LauncherMode? Current(ModState state, Settings settings, PackManifest? pack) => state switch
    {
        ModState.Off or ModState.NoModFiles => LauncherMode.Helldivers,
        ModState.On when pack?.CombinedRoster == true => LauncherMode.Clonedivers,
        ModState.On => (settings.Options.TryGetValue(CommandoOption, out var on) ? on :
            pack?.Options.FirstOrDefault(o => o.Id.Equals(CommandoOption, StringComparison.OrdinalIgnoreCase))?.Default ?? false)
            ? LauncherMode.CommandoDivers : LauncherMode.Clonedivers,
        _ => null,
    };

    public static Dictionary<string, bool> OptionsFor(Settings settings, PackManifest pack, LauncherMode mode)
    {
        if (mode == LauncherMode.Helldivers) throw new ArgumentException("Vanilla mode parks the pack; it does not change pack options.", nameof(mode));
        if (mode == LauncherMode.CommandoDivers && !pack.Options.Any(o => o.Id.Equals(CommandoOption, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This pack does not include Commandodivers yet.");
        if (pack.CombinedRoster && mode == LauncherMode.Clonedivers)
            return settings.OptionsFor(pack);
        return settings.OptionsFor(pack, CommandoOption, mode == LauncherMode.CommandoDivers);
    }
}

/// <summary>Original vector helmet marks. No external image files or licensed game artwork are embedded.</summary>
internal static class ModeHelmets
{
    public static void Draw(Graphics g, RectangleF bounds, LauncherMode mode, bool enabled)
    {
        var saved = g.Save();
        float scale = Math.Min(bounds.Width / 100, bounds.Height / 100);
        g.TranslateTransform(bounds.X + (bounds.Width - scale * 100) / 2, bounds.Y + (bounds.Height - scale * 100) / 2);
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var shell = new SolidBrush(enabled ? TextMain : TextDim);
        using var dark = new SolidBrush(Bg);
        using var accent = new SolidBrush(enabled ? mode == LauncherMode.Helldivers ? Warn : mode == LauncherMode.CommandoDivers ? Color.FromArgb(83, 200, 249) : BlueHot : Slate);
        using var trim = new SolidBrush(enabled ? Color.FromArgb(153, 168, 190) : Slate);
        using var outline = new GraphicsPath();
        outline.AddBezier(18, 45, 15, 0, 85, 0, 82, 45);
        outline.AddLines(new PointF[] { new(82, 45), new(86, 75), new(72, 92), new(28, 92), new(14, 75), new(18, 45) });
        outline.CloseFigure();
        g.FillPath(shell, outline);
        if (mode == LauncherMode.Helldivers)
        {
            g.FillPolygon(dark, new PointF[] { new(19, 36), new(81, 36), new(76, 56), new(61, 64), new(39, 64), new(24, 56) });
            g.FillPolygon(accent, new PointF[] { new(22, 39), new(78, 39), new(75, 46), new(25, 46) });
            g.FillPolygon(trim, new PointF[] { new(27, 66), new(38, 63), new(41, 84), new(29, 81) });
            g.FillPolygon(trim, new PointF[] { new(73, 66), new(62, 63), new(59, 84), new(71, 81) });
            g.FillRectangle(dark, 44, 72, 12, 15);
            g.FillRectangle(accent, 59, 13, 7, 19);
        }
        else if (mode == LauncherMode.Clonedivers)
        {
            g.FillRectangle(accent, 45, 11, 10, 24);
            g.FillPolygon(dark, new PointF[] { new(22, 40), new(78, 40), new(73, 51), new(56, 53), new(54, 74), new(46, 74), new(44, 53), new(27, 51) });
            g.FillEllipse(trim, 24, 67, 14, 11); g.FillEllipse(trim, 62, 67, 14, 11);
            g.FillRectangle(dark, 39, 82, 22, 4);
        }
        else
        {
            g.FillRectangle(accent, 39, 12, 22, 8);
            g.FillPolygon(dark, new PointF[] { new(17, 35), new(83, 35), new(78, 57), new(56, 61), new(55, 73), new(45, 73), new(44, 61), new(22, 57) });
            g.FillPolygon(accent, new PointF[] { new(23, 41), new(77, 41), new(73, 50), new(54, 54), new(52, 64), new(48, 64), new(46, 54), new(27, 50) });
            g.FillPolygon(trim, new PointF[] { new(20, 61), new(38, 67), new(38, 83), new(27, 78) });
            g.FillPolygon(trim, new PointF[] { new(80, 61), new(62, 67), new(62, 83), new(73, 78) });
            g.FillRectangle(dark, 42, 81, 16, 5);
        }
        g.Restore(saved);
    }
}

internal sealed class ModeButton : Button
{
    public LauncherMode Mode { get; init; }
    bool selected, hover, pressed;
    public bool Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }
    public ModeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        AccessibleRole = AccessibleRole.RadioButton;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Bg);
        if (Width < 2 || Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = DeviceDpi / 96f;
        using var path = RoundButton.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 12 * k);
        using var fill = new SolidBrush(selected ? Color.FromArgb(24, 47, 82) : hover && Enabled ? Disabled : Color.FromArgb(16, 23, 41));
        g.FillPath(fill, path);
        using var line = new Pen(selected ? BlueHot : hover && Enabled ? TextDim : Border, (selected ? 2 : 1) * k);
        g.DrawPath(line, path);
        float iconSize = Math.Min(82 * k, Height - 62 * k);
        ModeHelmets.Draw(g, new RectangleF((Width - iconSize) / 2, 13 * k + (pressed ? k : 0), iconSize, iconSize), Mode, Enabled);
        var textRect = new Rectangle(6, (int)(Height - 49 * k), Width - 12, (int)(24 * k));
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? TextMain : TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        using var small = new Font(Font.FontFamily, 8.5F);
        TextRenderer.DrawText(g, selected ? "SELECTED" : "", small, new Rectangle(0, (int)(Height - 25 * k), Width, (int)(18 * k)), Color.FromArgb(136, 193, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues)
        {
            using var focus = RoundButton.Rounded(new RectangleF(5 * k, 5 * k, Width - 10 * k, Height - 10 * k), 8 * k);
            using var pen = new Pen(TextDim, k); g.DrawPath(pen, focus);
        }
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ModeAccessibleObject(this);
    sealed class ModeAccessibleObject(ModeButton owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State | (owner.Selected ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => "Select mode";
        public override void DoDefaultAction() => owner.PerformClick();
    }
}
