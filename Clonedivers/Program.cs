// Clonedivers — Helldivers 2 mod on/off switch + Steam launcher.
//
// Core launcher responsibilities:
//   1. Moves every *.patch_* file between <game>\data\ (ON) and <game>\mods_off\ (OFF).
//      Move = rename on the same drive, so gigabyte mod packs flip instantly.
//   2. Opens steam://rungameid/553850 so Steam launches the game the normal way.
// Optional performance sharing lives in Telemetry*.cs and reads Windows counters
// and PresentMon ETW output. It never injects, hooks or reads game process memory.

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;
using static Clonedivers.Palette;

namespace Clonedivers;

/// <summary>Republic navy, clone-armor white, 501st blue, 212th orange. No green, no red, no gradients.
/// One static class so the custom controls below and MainForm share it (imported with `using static`).</summary>
internal static class Palette
{
    public static readonly Color Bg = Color.FromArgb(11, 16, 32);           // window, DWM caption, LAUNCH text
    public static readonly Color TextMain = Color.FromArgb(240, 243, 250);
    public static readonly Color TextDim = Color.FromArgb(150, 162, 190);   // hints, inert text
    public static readonly Color TextMute = Color.FromArgb(95, 105, 130);   // footer
    public static readonly Color Blue = Color.FromArgb(31, 95, 204);        // ON, progress fill
    public static readonly Color BlueHot = Color.FromArgb(52, 118, 230);
    public static readonly Color Slate = Color.FromArgb(62, 68, 82);        // OFF, secondary buttons, ghost strokes
    public static readonly Color SlateHot = Color.FromArgb(80, 87, 104);
    public static readonly Color Disabled = Color.FromArgb(30, 36, 52);     // inert fill, bar track, tooltip background
    public static readonly Color Orange = Color.FromArgb(226, 118, 28);     // LAUNCH
    public static readonly Color OrangeHot = Color.FromArgb(244, 140, 52);
    public static readonly Color Warn = Color.FromArgb(255, 200, 40);       // running warning, "not found"
    public static readonly Color Border = Color.FromArgb(38, 48, 77);       // DWM border, tooltip border
}

static class Dwm
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    const int DarkModeOld = 19, DarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;

    /// <summary>Dark caption on Windows 10 1809+; exact caption/text/border colours on Windows 11. HRESULTs are ignored; nothing throws.</summary>
    public static void ApplyDark(IntPtr hwnd, Color caption, Color text, Color border)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, DarkMode, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, DarkModeOld, ref on, 4);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;   // 34/35/36 return E_INVALIDARG on Win10
        int c = ColorTranslator.ToWin32(caption); DwmSetWindowAttribute(hwnd, CaptionColor, ref c, 4);   // COLORREF 0x00BBGGRR
        int t = ColorTranslator.ToWin32(text);    DwmSetWindowAttribute(hwnd, TextColor, ref t, 4);
        int b = ColorTranslator.ToWin32(border);  DwmSetWindowAttribute(hwnd, BorderColor, ref b, 4);
    }
}

static class Power
{
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    /// <summary>Keeps the PC from sleeping through a 9 GB download. Per-thread, so call it from the UI thread both ways.</summary>
    public static void KeepAwake(bool on) => SetThreadExecutionState(on ? 0x80000001u /* ES_CONTINUOUS|ES_SYSTEM_REQUIRED */ : 0x80000000u);
}

/// <summary>Flat Button with anti-aliased rounded corners and hover / press / keyboard-focus states, plus an optional
/// smaller second line (pack buttons), status dot (the toggle) and ghost outline look. It reads the same BackColor /
/// ForeColor / FlatAppearance that Style() and SetArmed() already set.</summary>
sealed class RoundButton : Button
{
    public int Radius { get; set; } = 8;                 // logical px
    public bool Ghost { get; set; }                      // outline only; BackColor is the stroke colour
    // Change-checked so a refresh that alters only the dot or the sub-line repaints (Text/BackColor setters are no-ops when
    // unchanged, so nothing else would), while the 1.5-s timer re-setting the same values costs no paint.
    string subText = ""; Color dotColor = Color.Empty; bool dotFilled = true;
    public string SubText { get => subText; set { value ??= ""; if (subText != value) { subText = value; Invalidate(); } } }   // smaller second line
    public Color DotColor { get => dotColor; set { if (dotColor != value) { dotColor = value; Invalidate(); } } }             // Color.Empty = no dot
    public bool DotFilled { get => dotFilled; set { if (dotFilled != value) { dotFilled = value; Invalidate(); } } }
    bool hover, down; Font? subFont;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnFontChanged(EventArgs e) { subFont?.Dispose(); subFont = null; base.OnFontChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { }   // OnPaint covers every pixel (no square-corner flash)
    Font Sub => subFont ??= new Font(Font.FontFamily, Font.Size * 0.8f, FontStyle.Regular);

    // The base OnPaint is deliberately not called: it would add the dotted focus rectangle and the mnemonic underline.
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;                        // TLP hands out 0-size cells mid-layout
        var outside = Parent?.BackColor ?? BackColor;
        using (var b = new SolidBrush(outside)) g.FillRectangle(b, r);     // corners show the parent's navy
        g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float rad = LogicalToDeviceUnits(Radius);
        var rf = new RectangleF(0.5f, 0.5f, r.Width - 1, r.Height - 1);
        Color fill, fore;
        if (!Enabled) { fill = Disabled; fore = TextDim; }                // we own the disabled look now
        else
        {
            fore = ForeColor;
            fill = Ghost ? (hover ? Color.FromArgb(48, BackColor) : outside)
                 : down  ? Blend(BackColor, Color.Black, 0.12f)
                 : hover ? FlatAppearance.MouseOverBackColor : BackColor;
        }
        using (var p = Rounded(rf, rad))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            if (Ghost) { using var pen = new Pen(Enabled ? BackColor : Disabled, LogicalToDeviceUnits(1)); g.DrawPath(pen, p); }
        }
        if (Focused && ShowFocusCues)                                      // keyboard focus only; mouse clicks show no ring
        {
            var fr = rf; fr.Inflate(-LogicalToDeviceUnits(4), -LogicalToDeviceUnits(4));
            using var fp = Rounded(fr, Math.Max(2, rad - LogicalToDeviceUnits(3)));
            using var pen = new Pen(Color.FromArgb(120, 255, 255, 255), LogicalToDeviceUnits(1));
            g.DrawPath(pen, fp);
        }

        var tr = r; if (down && Enabled) tr.Offset(0, LogicalToDeviceUnits(1));
        if (!string.IsNullOrEmpty(SubText))
        {
            const TextFormatFlags Line = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int h1 = TextRenderer.MeasureText(g, "Xg", Font, Size.Empty, TextFormatFlags.NoPadding).Height;
            int h2 = TextRenderer.MeasureText(g, "Xg", Sub, Size.Empty, TextFormatFlags.NoPadding).Height;
            int top = tr.Top + (tr.Height - h1 - h2) / 2;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tr.Left, top, tr.Width, h1), fore, Line);
            TextRenderer.DrawText(g, SubText, Sub, new Rectangle(tr.Left, top + h1, tr.Width, h2), Blend(fore, fill, 0.25f), Line);  // opaque: GDI text ignores alpha
        }
        else if (DotColor == Color.Empty || !DrawDotAndText(g, tr, fore))
            TextRenderer.DrawText(g, Text, Font, tr, fore,   // TextRenderer centres wrapped text vertically on its own
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>Status dot + label as one centred lockup. False when it would not fit, so the caller draws plain text instead.</summary>
    bool DrawDotAndText(Graphics g, Rectangle tr, Color fore)
    {
        const TextFormatFlags Line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int d = LogicalToDeviceUnits(12), gap = LogicalToDeviceUnits(14);
        int tw = TextRenderer.MeasureText(g, Text, Font, Size.Empty, Line).Width;
        int total = d + gap + tw;
        if (total > tr.Width - LogicalToDeviceUnits(16)) return false;
        int x = tr.Left + (tr.Width - total) / 2, cy = tr.Top + tr.Height / 2;
        var dot = new Rectangle(x, cy - d / 2, d, d);
        if (DotFilled)
        {
            var halo = dot; halo.Inflate(LogicalToDeviceUnits(4), LogicalToDeviceUnits(4));
            using (var hb = new SolidBrush(Color.FromArgb(70, DotColor))) g.FillEllipse(hb, halo);
            using var db = new SolidBrush(DotColor); g.FillEllipse(db, dot);
        }
        else
        {
            float w = LogicalToDeviceUnits(2);
            using var pen = new Pen(DotColor, w);
            g.DrawEllipse(pen, dot.X + w / 2, dot.Y + w / 2, dot.Width - w, dot.Height - w);
        }
        TextRenderer.DrawText(g, Text, Font, Rectangle.FromLTRB(x + d + gap, tr.Top, tr.Right, tr.Bottom), fore, Line);
        return true;
    }

    protected override void Dispose(bool disposing) { if (disposing) subFont?.Dispose(); base.Dispose(disposing); }

    internal static Color Blend(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    internal static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        if (d <= 0 || d > Math.Min(r.Width, r.Height)) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}

sealed class Grid : TableLayoutPanel { public Grid() { DoubleBuffered = true; } }   // DoubleBuffered is protected on TLP

/// <summary>Single-line label that ellipsises the middle of a path (Label.AutoEllipsis only cuts the end).</summary>
sealed class PathLabel : Label
{
    protected override void OnPaint(PaintEventArgs e) =>
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
}

/// <summary>6-px rounded progress bar. Idle it paints nothing but keeps its row, so the window never jumps when a download starts.</summary>
sealed class ThinBar : Control
{
    double fraction; bool marquee, active; float mx; int lastW = -1;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 33 };
    public Color Track { get; set; } = Disabled;
    public Color Fill { get; set; } = Blue;
    // Active / Marquee are set on every progress report; only a real change invalidates, so the lastW guard in Fraction does its job.
    public bool Active { get => active; set { if (active == value) return; active = value; if (!value) Marquee = false; Invalidate(); } }
    public double Fraction { get => fraction; set { fraction = Math.Clamp(value, 0, 1); int w = (int)(Width * fraction); if (w != lastW) { lastW = w; Invalidate(); } } }
    public bool Marquee
    {
        get => marquee;
        set
        {
            if (marquee == value) { if (value && Visible && !anim.Enabled) anim.Start(); return; }
            marquee = value; if (value && Visible) anim.Start(); else anim.Stop(); Invalidate();
        }
    }
    public ThinBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        anim.Tick += (_, _) => { mx = (mx + 0.02f) % 1.25f; Invalidate(); };
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) anim.Stop(); else if (marquee) anim.Start(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var r = ClientRectangle; if (r.Width <= 0 || r.Height <= 0) return;
        using (var b = new SolidBrush(Parent?.BackColor ?? BackColor)) g.FillRectangle(b, r);
        if (!active) return;                                             // idle: invisible but the row keeps its height
        g.SmoothingMode = SmoothingMode.AntiAlias; float rad = r.Height / 2f;
        var rf = new RectangleF(0, 0, r.Width, r.Height);
        using (var p = RoundButton.Rounded(rf, rad)) using (var tb = new SolidBrush(Track)) g.FillPath(tb, p);
        RectangleF f = marquee
            ? RectangleF.Intersect(rf, new RectangleF((mx - 0.25f) * r.Width, 0, r.Width / 4f, r.Height))
            : new RectangleF(0, 0, (float)Math.Max(r.Width * fraction, fraction > 0 ? r.Height : 0), r.Height);
        if (f.Width > 0) using (var p = RoundButton.Rounded(f, rad)) using (var fb = new SolidBrush(Fill)) g.FillPath(fb, p);
    }
    protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
}

/// <summary>The clone helmet from tools\make-icon.ps1, vector-drawn straight onto the navy (no tile) so it is crisp at any DPI.</summary>
sealed class HelmetMark : Control
{
    public HelmetMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var r = ClientRectangle; if (r.Width <= 0 || r.Height <= 0) return;
        using (var b = new SolidBrush(Parent?.BackColor ?? BackColor)) g.FillRectangle(b, r);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Same 256-unit design space as the icon. The helmet itself spans x 50..206, y 34..210; fit that box, not the tile.
        float k = Math.Min(r.Width / 156f, r.Height / 176f);
        g.TranslateTransform((r.Width - 156 * k) / 2 - 50 * k, (r.Height - 176 * k) / 2 - 34 * k);
        g.ScaleTransform(k, k);

        using (var helm = new GraphicsPath())
        {
            helm.AddArc(50, 34, 156, 150, 180, 180);     // dome
            helm.AddLine(206, 109, 206, 176);            // right cheek
            helm.AddArc(172, 176, 34, 34, 0, 90);        // jaw right
            helm.AddLine(189, 210, 67, 210);             // chin
            helm.AddArc(50, 176, 34, 34, 90, 90);        // jaw left
            helm.CloseFigure();
            using var shell = new SolidBrush(TextMain);
            g.FillPath(shell, helm);
        }
        var visor = Color.FromArgb(14, 17, 26); var vent = Color.FromArgb(150, 160, 185);
        Rounded(g, 118, 34, 20, 46, 6, Blue);        // 501st fin down the crown
        Rounded(g, 72, 104, 112, 24, 10, visor);     // T-visor
        Rounded(g, 116, 122, 24, 60, 8, visor);
        Rounded(g, 70, 166, 30, 20, 6, vent);        // breather vents
        Rounded(g, 156, 166, 30, 20, 6, vent);
        Rounded(g, 106, 190, 44, 6, 3, visor);       // mouth grille
    }
    static void Rounded(Graphics g, float x, float y, float w, float h, float rad, Color c)
    {
        using var p = RoundButton.Rounded(new RectangleF(x, y, w, h), rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }
}

public sealed partial class MainForm : Form
{
    readonly Settings settings;
    string? gameDir;
    DateTime? launchRequested;
    readonly string? updatedTo;   // "--updated 1.3.1": the new exe announcing itself after a self-update

    // Manifest / pack state
    Manifest? manifest;
    string? manifestError;
    string? lastOperationError;
    bool metadataOffline;
    bool loadingManifest;
    DateTime lastManifestTry;
    readonly HashCache hashCache = HashCache.Load(Settings.HashCachePath);

    enum BusyKind { None, Inventory, Download, Apply, AppUpdate }
    BusyKind busy;
    bool Busy => busy != BusyKind.None;
    CancellationTokenSource? opCts;
    bool installArmed, downloadArmed, launchArmed = true, offerArmed;
    bool exitingForUpdate;   // the new exe has been started; this instance must close without asking anything
    DateTime lastProgressMessage = DateTime.MinValue;
    readonly Stopwatch rateWatch = new();
    long rateBytes;
    string rateText = "", lastWhat = "";

    readonly Grid root = new();
    readonly Grid modeRow = new();
    readonly List<ModeButton> modeButtons = new();
    readonly bool preview;
    LauncherMode previewMode = LauncherMode.CommandoDivers;
    readonly RoundButton launchButton = new();
    readonly RoundButton pathButton = new();
    readonly RoundButton openButton = new();
    readonly RoundButton installFileButton = new();
    readonly RoundButton diagnosticsButton = new();
    readonly RoundButton downloadButton = new();
    readonly Grid optionsRow = new();
    readonly List<RoundButton> optionButtons = new();
    readonly List<RoundButton> extraButtons = new();
    readonly ThinBar progress = new();
    readonly Label progressLabel = new();
    readonly Label statusLabel = new();
    readonly Label hintLabel = new();
    readonly Label pathCaption = new();
    readonly PathLabel pathValue = new();
    readonly Label footer = new();
    readonly ToolTip tips = new();
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1500 };

    readonly bool allowAppUpdates;
    internal bool CanUpdateApp(AppRelease? app) => allowAppUpdates && app?.CanSelfUpdate(settings.ManifestUrl, AppInfo.Version) == true;

    public MainForm(Settings settings, string? gameDir, string? updatedTo = null, bool preview = false, Manifest? previewManifest = null, bool allowAppUpdates = true)
    {
        this.settings = settings;
        this.gameDir = gameDir;
        this.updatedTo = updatedTo;
        this.allowAppUpdates = allowAppUpdates;
        this.preview = preview;
        if (preview) manifest = previewManifest;

        Text = "Clonedivers";
        BackColor = Bg;
        ForeColor = TextMain;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 660);
        MinimumSize = Size;
        MaximizeBox = false;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { /* no icon resource: fine */ }

        BuildTooltips();
        BuildLayout();

        // Scale every pixel value above for the monitor's DPI (100–150%+); fonts scale on their own.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        refresh.Tick += (_, _) =>
        {
            ObserveSession(); // Continue low-frequency observations while minimized.
            // Also discover releases while the launcher stays open.
            var interval = manifestError is not null ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5);
            if (!preview && !Busy && !loadingManifest && DateTime.UtcNow - lastManifestTry > interval)
                _ = LoadManifestAsync();
            if (WindowState == FormWindowState.Minimized && !Busy) return;
            RefreshState();
        };
        Activated += (_, _) =>
        {
            RefreshState();
            if (!preview && !Busy && !loadingManifest && DateTime.UtcNow - lastManifestTry > TimeSpan.FromMinutes(1))
                _ = LoadManifestAsync();
        };
        Shown += (_, _) =>
        {
            if (preview) { RefreshPreview(); return; }
            manifest = ManifestStore.Load(ManifestStore.CachePath(settings.ManifestUrl));
            metadataOffline = manifest is not null;
            RestoreInstallationMetadata();
            StartSessionObservation();
            UpdateMetadataFooter();
            if (updatedTo is not null) { ShowProgressDone($"Updated to {updatedTo}."); Activate(); }
            RefreshState();
            refresh.Start();
            _ = LoadManifestAsync();                 // find out whether a pack is published (non-blocking)
            if (this.gameDir is null) OnLocate();   // first-run "pathing wizard": auto-detect failed, so ask.
        };
        if (preview) RefreshPreview();
    }

    // Runs before the first paint (no white caption flash) and again on every handle recreation.
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dwm.ApplyDark(Handle, Bg, TextMain, Border); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Invalidate(true); }

    void BuildTooltips()
    {
        tips.InitialDelay = 350; tips.AutoPopDelay = 30000; tips.ReshowDelay = 100;
        // Dark tooltip; the system one is a white box on our navy window.
        tips.OwnerDraw = true; tips.BackColor = Disabled; tips.ForeColor = TextMain;
        // Measure with word-wrap at a 460-px cap: WinForms lets a native tip grow to the screen width, so the pack notes
        // (~380 characters) would otherwise run off a 1920-px monitor as one line. Draw wraps with the same flags.
        tips.Popup += (_, e) =>
        {
            using var f = SystemFonts.StatusFont;   // the tooltip window's own font (Segoe UI 9); each call returns a new Font
            var s = TextRenderer.MeasureText(tips.GetToolTip(e.AssociatedControl), f, new Size(LogicalToDeviceUnits(460), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(s.Width + LogicalToDeviceUnits(10), s.Height + LogicalToDeviceUnits(6));
        };
        tips.Draw += (_, e) =>
        {
            e.Graphics.Clear(Disabled);
            using var pen = new Pen(Border); e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, Rectangle.Inflate(e.Bounds, -LogicalToDeviceUnits(5), -LogicalToDeviceUnits(3)), TextMain,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        };
    }

    void BuildLayout()
    {
        root.Dock = DockStyle.Fill;
        root.ColumnCount = 1;
        root.BackColor = Bg;
        root.Padding = new Padding(24, 20, 24, 16);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Header lockup: helmet mark + wordmark, centred as one block (AutoSize grid anchored None in a full-width cell).
        var header = new Grid { AutoSize = true, ColumnCount = 2, RowCount = 2, Anchor = AnchorStyles.None, BackColor = Bg, Margin = new Padding(0, 0, 0, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var mark = new HelmetMark { Width = 44, Height = 44, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 14, 0) };
        var title = new Label
        {
            Text = "CLONEDIVERS",
            Font = new Font("Segoe UI", 26F, FontStyle.Bold),
            ForeColor = TextMain,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        };
        var subtitle = new Label
        {
            Text = "Choose your universe",
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            ForeColor = TextDim,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2, 0, 0, 0),
        };
        header.Controls.Add(mark, 0, 0);
        header.SetRowSpan(mark, 2);
        header.Controls.Add(title, 1, 0);
        header.Controls.Add(subtitle, 1, 1);

        modeRow.Dock = DockStyle.Fill;
        modeRow.ColumnCount = Enum.GetValues<LauncherMode>().Length;
        modeRow.RowCount = 1;
        modeRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        modeRow.BackColor = Bg;
        modeRow.Margin = new Padding(0, 8, 0, 12);
        foreach (var mode in Enum.GetValues<LauncherMode>())
        {
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            var button = new ModeButton { Mode = mode, Text = LauncherModes.Name(mode),
                AccessibleName = LauncherModes.Name(mode), Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold), Cursor = Cursors.Hand,
                Margin = new Padding(mode == LauncherMode.Helldivers ? 0 : 5, 0, mode == LauncherMode.CommandoDivers ? 0 : 5, 0) };
            button.Click += (_, _) => OnMode(mode);
            modeButtons.Add(button);
            modeRow.Controls.Add(button, (int)mode, 0);
        }

        // Under the switch: one bright status line, one dim hint. The hint reserves three lines so ON / OFF / running never move the toggle.
        statusLabel.AutoSize = true;
        statusLabel.UseMnemonic = false;   // paths with '&' must not turn into underlines
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = TextMain;
        statusLabel.Font = new Font("Segoe UI", 10F);   // 10.5 wraps the running warning at 125% / 175%, which would shrink the toggle only while the game runs
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.Margin = new Padding(0, 0, 0, 2);
        hintLabel.AutoSize = true;
        hintLabel.UseMnemonic = false;
        hintLabel.Dock = DockStyle.Fill;
        hintLabel.ForeColor = TextDim;
        hintLabel.Font = new Font("Segoe UI", 9F);
        hintLabel.TextAlign = ContentAlignment.MiddleCenter;
        hintLabel.Margin = new Padding(0, 0, 0, 12);
        hintLabel.MinimumSize = new Size(0, 56);

        Style(launchButton, Orange, OrangeHot, Bg, 13F);   // navy on orange reads at 6.1:1; the old white was 2.8:1
        launchButton.Radius = 10;
        launchButton.Text = "LAUNCH HELLDIVERS 2";
        launchButton.Dock = DockStyle.Fill;
        launchButton.Margin = new Padding(0, 0, 0, 12);
        launchButton.Click += (_, _) => OnLaunch();

        // Pack row: ghost "Install pack from file…"  |  filled "Download pack" with "version · size" under it
        var packRow = new Grid { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0), BackColor = Bg };
        packRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        packRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        packRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        Style(installFileButton, Slate, SlateHot, TextMain, 10F);
        installFileButton.Radius = 8;
        installFileButton.Ghost = true;
        installFileButton.Text = "Check and repair";
        installFileButton.Dock = DockStyle.Fill;
        installFileButton.MinimumSize = new Size(0, 50);
        installFileButton.Margin = new Padding(0, 0, 6, 0);
        installFileButton.Click += (_, _) => OnRepair();
        Style(diagnosticsButton, Slate, SlateHot, TextMain, 9.5F);
        diagnosticsButton.Radius = 8;
        diagnosticsButton.Ghost = true;
        diagnosticsButton.Text = "Help & diagnostics";
        diagnosticsButton.UseMnemonic = false;
        diagnosticsButton.Dock = DockStyle.Fill;
        diagnosticsButton.MinimumSize = new Size(0, 50);
        diagnosticsButton.Margin = new Padding(6, 0, 0, 0);
        diagnosticsButton.Click += async (_, _) => await ShowDiagnosticsAsync();
        Style(downloadButton, Slate, SlateHot, TextMain, 10F);
        downloadButton.Radius = 8;
        downloadButton.Text = "Checking for pack…";
        downloadButton.Dock = DockStyle.Fill;
        downloadButton.MinimumSize = new Size(0, 50);
        downloadButton.Margin = new Padding(6, 0, 6, 0);
        downloadButton.Click += (_, _) => OnDownloadOrCancel();
        packRow.Controls.Add(installFileButton, 0, 0);
        packRow.Controls.Add(downloadButton, 1, 0);
        packRow.Controls.Add(diagnosticsButton, 2, 0);

        // Options row: one ghost toggle per optional group in the manifest (empty and flat until a manifest with options arrives).
        optionsRow.Dock = DockStyle.Fill;
        optionsRow.AutoSize = true;
        optionsRow.Margin = new Padding(0);
        optionsRow.BackColor = Bg;

        // Progress: label above a thin bar. Both stay Visible for life — hiding a child collapses its AutoSize row and the window jumps.
        progressLabel.AutoSize = true;
        progressLabel.UseMnemonic = false;
        progressLabel.Dock = DockStyle.Fill;
        progressLabel.ForeColor = TextDim;
        progressLabel.Font = new Font("Segoe UI", 9F);
        progressLabel.TextAlign = ContentAlignment.MiddleLeft;
        progressLabel.Margin = new Padding(0, 12, 0, 4);
        progressLabel.MinimumSize = new Size(0, 36);   // two 9-pt lines at every scale: the "Pack updated … parked in mods_old\" message wraps, and this row must never move
        progressLabel.Text = "";
        progressLabel.Click += (_, _) => { if (offerArmed) OnAppUpdate(); };
        progress.Height = 6;   // a plain Control has no preferred size; without this the AutoSize row collapses
        progress.MinimumSize = new Size(0, 6);
        progress.Dock = DockStyle.Fill;
        progress.Margin = new Padding(0, 0, 0, 2);

        // Game-folder row: "Found via Steam:  C:\…\Helldivers 2   [Open] [Change…]" — one 30-px line, path middle-ellipsised.
        var pathRow = new Grid { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 6, 0, 0), BackColor = Bg };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathCaption.Text = "Game folder:";
        pathCaption.AutoSize = true;
        pathCaption.ForeColor = TextDim;
        pathCaption.Font = new Font("Segoe UI", 9.5F);
        pathCaption.Anchor = AnchorStyles.Left;
        pathCaption.Margin = new Padding(0, 0, 8, 0);
        pathValue.AutoSize = false;
        pathValue.UseMnemonic = false;
        pathValue.Anchor = AnchorStyles.Left | AnchorStyles.Right;   // stretched across the cell and centred on the buttons; Dock=Fill pins a height-capped label to the top
        pathValue.ForeColor = TextMain;
        pathValue.Font = new Font("Segoe UI", 9.5F);
        pathValue.Margin = new Padding(0);
        pathValue.MinimumSize = new Size(0, 30);
        pathValue.MaximumSize = new Size(0, 30);   // 0 = width unconstrained; the height cap keeps the row at one line
        Style(openButton, Slate, SlateHot, TextMain, 9.5F);
        openButton.Radius = 6;
        openButton.Ghost = true;
        openButton.Text = "Open";
        openButton.AutoSize = true;
        openButton.Padding = new Padding(10, 0, 10, 0);
        openButton.MinimumSize = new Size(0, 30);
        openButton.Anchor = AnchorStyles.Right;
        openButton.Margin = new Padding(8, 0, 0, 0);
        openButton.Click += (_, _) => OpenFolder(Path.Combine(gameDir ?? "", ModFiles.DataFolder));
        Style(pathButton, Slate, SlateHot, TextMain, 9.5F);
        pathButton.Radius = 6;
        pathButton.Ghost = true;
        pathButton.Text = "Change…";
        pathButton.AutoSize = true;
        pathButton.Padding = new Padding(10, 0, 10, 0);
        pathButton.MinimumSize = new Size(0, 30);
        pathButton.Anchor = AnchorStyles.Right;
        pathButton.Margin = new Padding(8, 0, 0, 0);
        pathButton.Click += (_, _) => OnLocate();
        pathRow.Controls.Add(pathCaption, 0, 0);
        pathRow.Controls.Add(pathValue, 1, 0);
        pathRow.Controls.Add(openButton, 2, 0);
        pathRow.Controls.Add(pathButton, 3, 0);

        var testNote = string.IsNullOrWhiteSpace(settings.ManifestUrl) ? "" : $"  ·  test manifest: {HostOf(settings.ManifestUrl)}";
        footer.Text = $"v{AppInfo.Version}{testNote}  ·  For the Republic.";
        footer.Font = new Font("Segoe UI", 8.25F, FontStyle.Italic);
        footer.ForeColor = string.IsNullOrWhiteSpace(settings.ManifestUrl) ? TextMute : Warn;
        footer.AutoSize = true;
        footer.Dock = DockStyle.Fill;
        footer.TextAlign = ContentAlignment.MiddleCenter;
        footer.Margin = new Padding(0, 10, 0, 0);

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header lockup
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // three mode cards
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // hint
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));  // launch: 50-px button + 12 margin, level with the pack buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // pack buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // options (empty until the manifest has some)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progress text
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progress bar
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // path row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // footer
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(modeRow, 0, 1);
        root.Controls.Add(statusLabel, 0, 2);
        root.Controls.Add(hintLabel, 0, 3);
        root.Controls.Add(launchButton, 0, 4);
        root.Controls.Add(packRow, 0, 5);
        root.Controls.Add(optionsRow, 0, 6);
        root.Controls.Add(progressLabel, 0, 7);
        root.Controls.Add(progress, 0, 8);
        root.Controls.Add(pathRow, 0, 9);
        root.Controls.Add(footer, 0, 10);
        Controls.Add(root);
    }

    static string HostOf(string url)
    {
        try { return Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.IsFile ? "local file" : u.Host) : url; } catch { return url; }
    }

    /// <summary>Independent texture preference, available before downloading the first pack.</summary>
    void RebuildOptions()
    {
        var pack = manifest?.Pack;
        var options = new List<PackTextureProfile>();
        if (PackProfiles.Potato(pack ?? new PackManifest()) is {} potato) options.Add(potato);
        var extras = PackExtras.Visible(pack).ToList();
        if (optionButtons.Count == options.Count && optionButtons.Select(b => (string)b.Tag!).SequenceEqual(options.Select(o => o.Id)) &&
            extraButtons.Select(b => (string)b.Tag!).SequenceEqual(extras.Select(o => o.Id))) return;
        optionsRow.SuspendLayout();
        optionsRow.Controls.Clear();
        optionsRow.ColumnStyles.Clear();
        optionsRow.RowStyles.Clear();
        foreach (var b in optionButtons) b.Dispose();
        foreach (var b in extraButtons) b.Dispose();
        optionButtons.Clear();
        extraButtons.Clear();
        optionsRow.ColumnCount = Math.Clamp(options.Count + extras.Count, 1, 2);
        optionsRow.RowCount = Math.Max(1, (options.Count + extras.Count + optionsRow.ColumnCount - 1) / optionsRow.ColumnCount);
        for (int row = 0; row < optionsRow.RowCount; row++) optionsRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int column = 0; column < optionsRow.ColumnCount; column++) optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / optionsRow.ColumnCount));
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            var b = new RoundButton { Radius = 8, Ghost = true, Dock = DockStyle.Fill, Tag = o.Id, MinimumSize = new Size(0, 36),
                                      Margin = new Padding(i == 0 ? 0 : 6, 8, i == options.Count - 1 ? 0 : 6, 0) };
            Style(b, Slate, SlateHot, TextMain, 9.5F);
            b.Click += (_, _) => OnPotatoToggle();
            optionsRow.Controls.Add(b, i, 0);
            optionButtons.Add(b);
        }
        for (int i = 0; i < extras.Count; i++)
        {
            var extra = extras[i];
            int column = (options.Count + i) % optionsRow.ColumnCount;
            var button = new RoundButton { Radius = 8, Ghost = true, Dock = DockStyle.Fill, Tag = extra.Id,
                MinimumSize = new Size(0, 36), Margin = new Padding(column == 0 ? 0 : 6, 8, column == optionsRow.ColumnCount - 1 ? 0 : 6, 0) };
            Style(button, Slate, SlateHot, TextMain, 9.5F);
            button.Click += (_, _) => OnOptionToggle(manifest!.Pack!.Options.Single(o => o.Id == (string)button.Tag!));
            optionsRow.Controls.Add(button, column, (options.Count + i) / optionsRow.ColumnCount);
            extraButtons.Add(button);
        }
        optionsRow.ResumeLayout(true);
    }

    bool OptionEnabled(PackOption o) => manifest?.Pack is {} p && PackExtras.AlwaysOn(p, o.Id) || (settings.Options.TryGetValue(o.Id, out var v) ? v : o.Default);
    bool OptionEnabledById(string id)
    {
        if (manifest?.Pack is {} pack && PackExtras.AlwaysOn(pack, id)) return true;
        if (settings.Options.TryGetValue(id, out var v)) return v;
        return manifest?.Pack?.Options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase))?.Default ?? true;
    }
    List<PackFile> WantedFiles(PackManifest pack) => AvailableTextureProfile(pack) is { } profile ? Pack.EffectiveFiles(pack, OptionEnabledById, profile) : new();

    /// <summary>Secondary buttons that may be inert: styled dim when not armed (a truly disabled flat button is unreadable here).</summary>
    static void SetArmed(Button b, string text, bool armed)
    {
        b.Text = text;
        b.BackColor = armed ? Slate : Disabled;
        b.ForeColor = armed ? TextMain : TextDim;
        b.FlatAppearance.MouseOverBackColor = armed ? SlateHot : Disabled;
        b.FlatAppearance.MouseDownBackColor = armed ? Slate : Disabled;
        b.Cursor = armed ? Cursors.Hand : Cursors.Default;
    }

    /// <summary>Inert colours but still clickable ("Pack up to date", "Can't reach GitHub"): hand cursor and a hover so it reads as a button.</summary>
    static void SetQuiet(Button b, string text) { SetArmed(b, text, armed: false); b.FlatAppearance.MouseOverBackColor = SlateHot; b.Cursor = Cursors.Hand; }

    static void Style(Button b, Color back, Color hot, Color fore, float pt)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hot;
        b.FlatAppearance.MouseDownBackColor = back;
        b.BackColor = back;
        b.ForeColor = fore;
        b.Font = new Font("Segoe UI", pt, FontStyle.Bold);
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
    }

    void Tip(Control c, string text) { if (tips.GetToolTip(c) != text) tips.SetToolTip(c, text); }   // SetToolTip on a showing tip resets it

    /// <summary>Re-derives everything from disk. Cheap, so it runs on a timer and on focus. One layout pass per call.</summary>
    void RefreshState()
    {
        if (preview) { RefreshPreview(); return; }
        root.SuspendLayout();
        try { ApplyState(); }
        finally { root.ResumeLayout(true); }
    }

    // The three "something changed under the pack" signals, in the order they are shown.
    enum Signal { None, Broken, Pending, BuildChanged }

    Signal CurrentSignal(ModState state, PackManifest? pack, SteamAcf.Info? acf)
    {
        if (pack?.IsBroken == true) return Signal.Broken;
        if (state != ModState.On || acf is null) return Signal.None;
        if (acf.UpdatePending) return Signal.Pending;
        if (settings.BuildChanged(pack, acf)) return Signal.BuildChanged;
        return Signal.None;
    }

    static string SignalSentence(Signal s, PackManifest? pack) => s switch
    {
        Signal.Broken => string.IsNullOrWhiteSpace(pack?.StatusNotes) ? "Owen marked this pack broken. Choose Helldivers until an updated pack ships." : "Owen marked this pack broken: " + pack!.StatusNotes.Trim(),
        Signal.Pending => "Steam has a Helldivers 2 update waiting; it installs when you launch.",
        Signal.BuildChanged => "The installed pack has not been verified for this Helldivers 2 build.",
        _ => "",
    };

    void ApplyState()
    {
        var state = ModFiles.GetState(gameDir);
        var running = Game.IsRunning();
        var staged = !Busy && Pack.HasPendingUpdate(gameDir, Settings.PlanPath);
        var pack = manifest?.Pack;
        var acf = SteamAcf.Read(gameDir);
        RefreshModeLayout();
        var dataDir = gameDir is null ? "" : Path.Combine(gameDir, ModFiles.DataFolder);
        var active = gameDir is null ? 0 : ModFiles.ListPatchFiles(dataDir).Length;
        var parked = gameDir is null ? 0 : ModFiles.ListPatchFiles(Path.Combine(gameDir, ModFiles.OffFolder)).Length;
        var profileAvailable = AvailableTextureProfile(pack ?? new PackManifest()) is not null;
        var usable = pack is not null && pack.IsPublished && pack.IsPerFile && profileAvailable;

        var selected = LauncherModes.Current(state, settings, pack);
        if (state == ModState.On && settings.InstalledPackVersion is null) selected = null;
        string detail = selected is { } mode
            ? $"{LauncherModes.Name(mode)} selected.\n{ModeDescription(mode)}"
            : state == ModState.On ? "Unverified mod files found.\nUse Check and repair to identify and verify the pack." : "Game not found.\nChoose Change… to find your Helldivers 2 folder.";
        if (state == ModState.NoModFiles) detail += "\nChoose a modded mode to install its pack.";
        foreach (var button in modeButtons)
        {
            var supported = button.Mode == LauncherMode.EmpireDivers ? LauncherModes.SupportsEmpire(pack) :
                button.Mode != LauncherMode.CommandoDivers || pack?.Options.Any(o => o.Id.Equals(LauncherModes.CommandoOption, StringComparison.OrdinalIgnoreCase)) == true;
            button.Enabled = !Busy && !staged && !running && state != ModState.GameNotFound &&
                (button.Mode == LauncherMode.Helldivers || usable && supported);
            button.Selected = !staged && button.Mode == selected;
            button.AccessibleDescription = button.Selected ? "Selected. " + ModeDescription(button.Mode) : ModeDescription(button.Mode);
            Tip(button, !supported ? $"This pack does not include {LauncherModes.Name(button.Mode)} yet." :
                running ? "Close Helldivers 2 before changing modes." : ModeDescription(button.Mode));
        }
        // Warnings under the switch, in precedence order: game running > pack broken > Steam update pending > game build changed.
        var warn = running && state is ModState.On or ModState.Off;
        var signal = CurrentSignal(state, pack, acf);
        if (warn) detail = "Helldivers 2 is running — close it before changing modes. Mods are only read at startup.\n" + detail;
        else if (signal != Signal.None && !staged)
        {
            var lines = detail.Split('\n');
            var keep = string.Join("\n", lines.Skip(1).Take(2));
            var advice = signal == Signal.Broken ? "" : "If the game crashes or looks wrong, choose Helldivers until Owen confirms the pack.\n";
            detail = SignalSentence(signal, pack) + "\n" + advice + keep;
            warn = true;
        }

        if (staged)
        {
            detail = "A pack update was interrupted.\nClick Finish update to check the files and complete it.\nKeep the game closed until recovery finishes.";
            warn = false;
        }
        if (Busy)
        {
            var (t, d) = busy switch
            {
                BusyKind.Inventory => ("CHECKING FILES…", "Checking which installed files can be kept.\nThe first check reads every file once (a few minutes on a hard drive). Nothing moves yet."),
                BusyKind.Download => ("DOWNLOADING PACK…", "Downloading only what changed — leave this window open.\nYour selected mode is ready when it finishes. Cancel keeps whatever has downloaded so far."),
                BusyKind.Apply => ("FINISHING UPDATE…", "Checking and putting the files in place.\nThis step cannot be cancelled."),
                _ => ("UPDATING CLONEDIVERS…", "Downloading the new Clonedivers — leave this window open.\nIt restarts by itself when done."),
            };
            detail = d; warn = false;
        }
        int nl = detail.IndexOf('\n');
        statusLabel.Text = nl < 0 ? detail : detail[..nl];
        hintLabel.Text = nl < 0 ? "" : detail[(nl + 1)..];
        statusLabel.ForeColor = warn ? Warn : TextMain;

        // Launch feedback: Steam can take a minute to open before the game process exists.
        if (running) launchRequested = null;
        var starting = !running && launchRequested is DateTime lt && DateTime.UtcNow - lt < TimeSpan.FromSeconds(90);
        launchArmed = !running && !starting && !Busy && !staged;
        launchButton.Enabled = true;
        launchButton.Text = running ? "HELLDIVERS 2 IS RUNNING" : starting ? "STARTING VIA STEAM…" : "LAUNCH " + (selected is { } launchMode ? LauncherModes.Name(launchMode).ToUpperInvariant() : "HELLDIVERS 2");
        launchButton.BackColor = launchArmed ? Orange : Disabled;
        launchButton.ForeColor = launchArmed ? Bg : TextDim;
        launchButton.FlatAppearance.MouseOverBackColor = launchArmed ? OrangeHot : Disabled;
        launchButton.FlatAppearance.MouseDownBackColor = launchArmed ? Orange : Disabled;
        launchButton.Cursor = launchArmed || running ? Cursors.Hand : Cursors.Default;
        Tip(launchButton, running ? "Helldivers 2 is already running."
                        : starting ? "Starting through Steam. Give it a minute."
                        : "Launch through Steam.");

        // Game-folder row. Only manual picks are saved to settings.GamePath, so the caption check is exact.
        var manual = gameDir is not null && string.Equals(settings.GamePath, gameDir, StringComparison.OrdinalIgnoreCase);
        pathCaption.Text = gameDir is not null && !manual ? "Found via Steam:" : "Game folder:";
        pathValue.Text = gameDir ?? "not found";
        pathValue.ForeColor = gameDir is null ? Warn : TextMain;
        Tip(pathValue, gameDir ?? "");
        openButton.Visible = gameDir is not null;
        Tip(openButton, "Open the mod folder.");
        pathButton.Text = gameDir is null ? "Locate…" : "Change…";
        Tip(pathButton, gameDir is null ? "Pick the Helldivers 2 folder (it contains data\\ and bin\\helldivers2.exe)." : "Pick a different Helldivers 2 folder.");

        // Pack buttons
        var canInstall = gameDir is not null && !running && !Busy;
        installArmed = canInstall && !staged && (active + parked > 0 || (gameDir is not null && File.Exists(Settings.ReceiptPathFor(gameDir))));
        SetArmed(installFileButton, "Check and repair", installArmed);
        Tip(installFileButton, staged ? "Finish the interrupted update first." : "Repair missing or damaged files.\n" +
            (settings.LastVerifiedUtc is { } checkedAt ? $"Last full check: {checkedAt.ToLocalTime():g}" : "No full check recorded yet."));
        diagnosticsButton.Enabled = !Busy && !collectingDiagnostics;
        downloadButton.SubText = "";
        if (Busy)
        {
            downloadArmed = busy != BusyKind.Apply;
            if (busy == BusyKind.Apply) { SetArmed(downloadButton, "Finishing…", false); Tip(downloadButton, "Checking and putting files in place; this step cannot be cancelled."); }
            else { SetArmed(downloadButton, "Cancel", true); Tip(downloadButton, "Stops the check or download. Downloaded files are kept and resume next time."); }
        }
        else if (staged)
        {
            downloadArmed = canInstall;
            SetArmed(downloadButton, "Finish update", downloadArmed);
            downloadButton.SubText = "check and recover files";
            Tip(downloadButton, "Continue the interrupted update.");
        }
        else if (manifest is null && manifestError is null)
        {
            downloadArmed = false;
            SetArmed(downloadButton, "Checking for pack…", false);
            Tip(downloadButton, "Checking for updates.");
        }
        else if (manifest is null)
        {
            downloadArmed = false;
            SetQuiet(downloadButton, manifestError!.StartsWith("manifest.json", StringComparison.OrdinalIgnoreCase) ? "Can't read manifest" : "Can't reach GitHub");
            downloadButton.SubText = "click to retry";
            Tip(downloadButton, manifestError!);
        }
        else if (!profileAvailable)
        {
            downloadArmed = false;
            SetQuiet(downloadButton, "Choose textures");
            downloadButton.SubText = "Saved choice unavailable";
            Tip(downloadButton, "Use the texture toggle below to choose a supported setting.");
        }
        else if (!usable)
        {
            downloadArmed = false;
            SetArmed(downloadButton, "Pack not published yet", false);
            Tip(downloadButton, "No pack is available yet.");
        }
        else
        {
            downloadArmed = canInstall;
            var installed = settings.InstalledPackVersion;
            var size = Pack.FormatBytes(WantedFiles(pack!).Sum(f => f.Size));   // what THIS friend would install with their toggles, not every variant in the manifest
            var notes = string.IsNullOrWhiteSpace(pack!.Notes) ? pack.Name : pack.Name + "\n" + pack.Notes;
            var anyFiles = active + parked > 0;
            if (!anyFiles)
            {
                SetArmed(downloadButton, "Download pack", downloadArmed);
                downloadButton.SubText = $"{pack.Version}  ·  {size}";
                Tip(downloadButton, notes + $"\n\nAbout {size} the first time; later updates fetch only what changed. Resumes if interrupted.");
            }
            else if (installed == pack.Version)
            {
                SetQuiet(downloadButton, "Pack installed");
                downloadButton.Cursor = canInstall ? Cursors.Hand : Cursors.Default;
                downloadButton.SubText = pack.Version;
                Tip(downloadButton, $"Installed version: {pack.Version}. Use Check and repair to verify its files.\n\n{notes}");
            }
            else
            {
                SetArmed(downloadButton, "Update pack", downloadArmed);
                downloadButton.SubText = $"{pack.Version}  ·  downloads only what changed";
                Tip(downloadButton, $"Installed: {installed ?? "unknown"}\nAvailable: {pack.Version}\n\n{notes}\n\nDownloads only changed files. Resumes if interrupted.");
            }
        }

        // Option toggles
        RebuildOptions();
        optionsRow.Visible = selected is LauncherMode.Clonedivers or LauncherMode.CommandoDivers or LauncherMode.EmpireDivers;
        foreach (var b in optionButtons)
        {
            var o = PackProfiles.Supported(pack ?? new PackManifest()).FirstOrDefault(x => string.Equals(x.Id, (string)b.Tag!, StringComparison.OrdinalIgnoreCase));
            if (o is null) continue;
            var on = AvailableTextureProfile(pack ?? new PackManifest()) is {} textureChoice && textureChoice != "full";
            var armed = !Busy && !running && !staged;
            b.Text = PackProfiles.PotatoName + (on ? ": On" : ": Off");
            b.AccessibleDescription = "Use lighter textures. " + (on ? "On." : "Off.");
            b.Enabled = armed;
            b.BackColor = armed ? (on ? Blue : Slate) : Disabled;      // ghost: BackColor is the outline colour
            b.ForeColor = armed ? TextMain : TextDim;
            b.Cursor = armed ? Cursors.Hand : Cursors.Default;
            Tip(b, o.Id == "reduced512" ? "Smaller textures for slower PCs. Less detail." : "Use lighter textures.");
        }

        // Self-update offer lives in the idle progress row.
        RefreshExtras(!Busy && !running && !staged);
        var app = manifest?.App;
        var offer = !Busy && !staged && app is not null && CanUpdateApp(app)
                    && DateTime.UtcNow - lastProgressMessage > TimeSpan.FromSeconds(5);
        if (offer)
        {
            offerArmed = true;
            progressLabel.Text = $"Clonedivers {app!.Version} is available  ·  click to update";
            progressLabel.ForeColor = OrangeHot;
            progressLabel.Cursor = Cursors.Hand;
            Tip(progressLabel, $"Downloads Clonedivers {app.Version} ({Pack.FormatBytes(app.Size)}) and restarts by itself. Your game folder, pack and settings carry over.");
        }
        else if (offerArmed)
        {
            offerArmed = false;
            if (progressLabel.ForeColor == OrangeHot) { progressLabel.Text = ""; progressLabel.ForeColor = TextDim; }
            progressLabel.Cursor = Cursors.Default;
            Tip(progressLabel, "");
        }
    }

    async Task LoadManifestAsync()
    {
        if (loadingManifest) return;
        loadingManifest = true; lastManifestTry = DateTime.UtcNow;
        try
        {
            var fresh = await ManifestStore.FetchAsync(settings.ManifestUrl, CancellationToken.None);
            ManifestStore.ValidateForUse(fresh);
            var usingSavedPack = manifest?.Format == 3 && fresh.Format < 3;
            manifest = ManifestStore.PreserveProfiles(manifest, fresh);
            try { ManifestStore.Save(ManifestStore.CachePath(settings.ManifestUrl), manifest); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Online use still works when caching is unavailable. */ }
            metadataOffline = usingSavedPack;
            manifestError = usingSavedPack ? "The profile feed is unavailable. Using the saved pack information." : null;
        }
        catch (Exception ex) { metadataOffline = manifest is not null; manifestError = ex.Message; }
        finally { loadingManifest = false; }
        if (!IsDisposed) { UpdateMetadataFooter(); RefreshState(); }
    }

    void ShowRunningWarning() => MessageBox.Show(this,
        "Helldivers 2 is running.\n\nClose the game first. The engine only reads mod files at startup, " +
        "and moving them out from under a running game is asking for trouble.\n\n" +
        "No game window open? It may have crashed and left helldivers2.exe behind — end it in Task Manager (Ctrl+Shift+Esc).",
        "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    void OnInstallFromFile()
    {
        if (!installArmed || gameDir is null) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        using var dlg = new OpenFileDialog
        {
            Title = "Pick the Clonedivers pack zip (select all parts if there are several)",
            Filter = "Pack zip files (*.zip)|*.zip|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.FileNames.Length == 0) return;
        _ = InstallFromFilesAsync(dlg.FileNames);
    }

    void OnDownloadOrCancel()
    {
        if (Busy) { if (busy != BusyKind.Apply) opCts?.Cancel(); return; }
        if (gameDir is null) return;
        if (Pack.HasPendingUpdate(gameDir, Settings.PlanPath))
        {
            if (!downloadArmed) return;
            if (Game.IsRunning()) { ShowRunningWarning(); return; }
            _ = FinishUpdateAsync();
            return;
        }
        if (manifest is null)
        {
            // "Can't reach GitHub · click to retry"
            if (manifestError is not null && !loadingManifest) { manifestError = null; _ = LoadManifestAsync(); RefreshState(); }
            return;
        }
        var pack = manifest.Pack;
        if (!downloadArmed || pack is null || !pack.IsPublished || !pack.IsPerFile) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        var verify = settings.InstalledPackVersion == pack.Version;
        if (verify) { OnRepair(); return; }
        _ = RunPackAsync(pack, verify, optionChange: null);
    }

    void OnOptionToggle(PackOption o)
    {
        var pack = manifest?.Pack;
        if (Busy || pack is null || (!preview && (!pack.IsPublished || !pack.IsPerFile || Pack.HasPendingUpdate(gameDir, Settings.PlanPath)))) return;
        if (!preview && Game.IsRunning()) { ShowRunningWarning(); return; }
        var turnOn = !OptionEnabled(o);
        if (preview || gameDir is null || ModFiles.GetState(gameDir) == ModState.NoModFiles)
        {
            settings.Options[o.Id] = turnOn;
            if (!preview) settings.Save();
            RefreshState();
            return;
        }
        if (AvailableTextureProfile(pack) is null) { ShowProgressDone("Choose an available texture profile first."); return; }
        _ = RunPackAsync(pack, verify: false, optionChange: (o, turnOn));
    }

    bool ConfirmInstall(string headline)
    {
        var text = headline + "\n\nMod files already in data\\ that are not part of the pack are moved to mods_old\\ " +
                   "(delete that folder later to free space). Nothing is deleted.";
        return MessageBox.Show(this, text, "Clonedivers", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    async Task InstallFromFilesAsync(IReadOnlyList<string> zips)
    {
        var names = string.Join(", ", zips.Select(Path.GetFileName));
        if (!ConfirmInstall($"Install {names} into data\\?")) return;
        SetBusy(BusyKind.Download);
        var cts = opCts = new CancellationTokenSource();
        try
        {
            // Patch bytes per zip come from the zip directories (no decompression), so the bar can span all parts instead of restarting per zip.
            var sizes = await Task.Run(() => zips.Select(Pack.PatchBytes).ToArray(), cts.Token);
            long grand = sizes.Sum();
            var starts = new long[sizes.Length];
            for (int i = 1; i < starts.Length; i++) starts[i] = starts[i - 1] + sizes[i - 1];
            var reporters = zips.Select((_, i) => new Progress<(long done, long total)>(p => ShowProgress("Installing pack", starts[i] + p.done, grand))).ToList();
            var result = await Task.Run(() =>
            {
                if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 started while installing. Close it and try again.");
                return Pack.Install(gameDir!, zips, i => reporters[i], cts.Token);
            }, cts.Token);
            settings.InstalledPackVersion = null;   // a zip install is of unknown version; Update pack will verify and record it
            settings.InstalledGameBuild = null;
            settings.LastVerifiedUtc = null;
            InstallReceipt.Invalidate(Settings.ReceiptPathFor(gameDir!));
            settings.Save();
            ShowProgressDone($"Pack installed: {result.installed:N0} files in data\\"
                + (result.parked > 0 ? $", {result.parked:N0} old file{(result.parked == 1 ? "" : "s")} parked in mods_old\\" : "")
                + ". Ready to launch.");
        }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. The mode selection shows what is installed now."); }
        catch (Exception ex) { ShowProgressDone("Install failed."); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(BusyKind.None); }
    }

    /// <summary>The per-file flow: inventory → plan → confirm → download what is missing → apply. <paramref name="verify"/> re-hashes
    /// everything (the "Pack up to date" click); <paramref name="optionChange"/> names the toggle that triggered it, for the dialog.</summary>
    async Task RunPackAsync(PackManifest pack, bool verify, (PackOption option, bool on)? optionChange, LauncherMode? modeChange = null,
        string? textureProfileChange = null, RecoveryTarget? savedTarget = null)
    {
        var dlDir = Path.Combine(gameDir!, Pack.DownloadFolder);
        var targetOptions = savedTarget?.Options ?? (modeChange is { } mode ? LauncherModes.OptionsFor(settings, pack, mode)
            : settings.OptionsFor(pack, optionChange?.option.Id, optionChange?.on ?? false));
        var targetProfile = savedTarget is not null ? savedTarget.TextureProfile ?? (savedTarget.Options.TryGetValue("skinny", out var legacyLighter) && legacyLighter ? "lighter" : "full")
            : textureProfileChange ?? settings.ProfileFor(pack);
        if (savedTarget is null) targetOptions["skinny"] = targetProfile != "full";
        var targetActive = savedTarget?.TargetActive ?? (modeChange is not null || ModFiles.GetState(gameDir) != ModState.Off);
        var targetVersion = savedTarget?.PackVersion ?? pack.Version;
        var targetBuild = savedTarget?.GameBuild ?? pack.GameBuild;
        var receiptPath = Settings.ReceiptPathFor(gameDir!);
        List<PackFile> wanted;
        var planPath = Settings.PlanPath;
        SetBusy(BusyKind.Inventory);
        var cts = opCts = new CancellationTokenSource();
        UpdatePlan plan;
        try
        {
            wanted = savedTarget?.Files.Select(f => f.Clone()).ToList() ?? Pack.EffectiveFiles(pack, id => targetOptions[id], targetProfile);
            // Switching profiles must not throw away a cancelled download for another supported choice.
            if (savedTarget is null)
                Pack.CleanDownloadFolder(dlDir, pack.Files.Concat(wanted).Concat(manifest?.Pack?.Files ?? new()).Select(f => f.Sha256));
            var complete = await Pack.VerifiedDownloadsAsync(dlDir, wanted, cts.Token);
            var reporter = new Progress<(long done, long total)>(p => ShowProgress("Checking installed files", p.done, p.total));
            var local = await Task.Run(() => Pack.InventoryAsync(gameDir!, wanted, hashCache, verify, reporter, cts.Token), cts.Token);
            hashCache.Save(Settings.HashCachePath);
            plan = Pack.Plan(gameDir!, wanted, local, complete, targetActive);
        }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. Nothing was changed."); SetBusy(BusyKind.None); return; }
        catch (Exception ex) { lastOperationError = ex.Message; SupportHistory.Record("error", targetVersion, targetProfile, ex.Message); ShowProgressDone("Update failed."); SetBusy(BusyKind.None); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        SetBusy(BusyKind.None);

        if (plan.IsNoOp)
        {
            try
            {
                SetBusy(BusyKind.Apply);
                // An old recovery record may remain even when all target files are already in place.
                var result = await Task.Run(() => Pack.Apply(gameDir!, plan, planPath, targetVersion, hashCache, Settings.HashCachePath, targetBuild, targetOptions, targetProfile, verify, receiptPath));
                if (result.Interrupted) { ReportApply(result, 0); return; }
                settings.RecordInstall(result, preserveTexturePreference: !targetActive && textureProfileChange is null);
                settings.Save();
                FpvDefaults.EnsureForInstalledPack(gameDir!);
                lastOperationError = null;
                SupportHistory.Record(verify ? "verified" : "installed", targetVersion, targetProfile);
                ShowProgressDone(verify ? $"Verified: all {wanted.Count:N0} files match {targetVersion}." : optionChange is { } oc ? $"{oc.option.Name} is now {(oc.on ? "on" : "off")}; nothing to change." : "Ready — nothing to download.");
            }
            catch (Exception ex) { lastOperationError = ex.Message; SupportHistory.Record("error", targetVersion, targetProfile, ex.Message); ShowProgressDone("Update failed."); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetBusy(BusyKind.None); }
            return;
        }

        // A mode click authorizes a local switch. Confirm downloads and low disk space;
        // keep the existing confirmation for repairs and other pack operations.
        var fresh = !ModFiles.ListPatchFiles(Path.Combine(gameDir!, ModFiles.DataFolder)).Any() && !ModFiles.ListPatchFiles(Path.Combine(gameDir!, ModFiles.OffFolder)).Any();
        var dlText = plan.BytesToDownload > 0 ? $"Download {Pack.FormatBytes(plan.BytesToDownload)} ({plan.Downloads.Count:N0} file{(plan.Downloads.Count == 1 ? "" : "s")})." : "Nothing to download.";
        string headline;
        if (modeChange is { } chosen)
            headline = $"Download files for {LauncherModes.Name(chosen)}?\n\n{dlText}";
        else if (textureProfileChange is not null)
            headline = $"Change texture profile?\n\n{dlText}";
        else if (optionChange is { } o)
            headline = $"Turn {o.option.Name} {(o.on ? "on" : "off")}?\n\n{dlText}" + (o.on ? "" : " The rest is renumbered in place.");
        else if (fresh)
            headline = $"Download the Clone Wars pack {pack.Version}?\n\n{Pack.FormatBytes(plan.BytesToDownload)} ({plan.Downloads.Count:N0} files) — start it before dinner. Leave this window open; it resumes if interrupted.";
        else if (verify)
            headline = $"Repair pack {targetVersion}?\n\n{dlText} Those files are missing or damaged; everything else checks out.";
        else
            headline = $"Update pack to {pack.Version}?\n\n{dlText} Everything else you already have.";
        if (modeChange is null && plan.ParkCount > 0 && !fresh) headline += $"\n{plan.ParkCount:N0} old file{(plan.ParkCount == 1 ? "" : "s")} go to mods_old\\ (nothing is deleted).";
        if (!fresh) headline += "\nLeave this window open and keep the game closed.";
        if (!string.IsNullOrWhiteSpace(pack.Notes)) headline += "\n\n" + pack.Notes;
        var drive = Path.GetPathRoot(gameDir!) ?? "";
        long free = 0;
        try { free = new DriveInfo(drive).AvailableFreeSpace; } catch { /* unknown drive type: skip the warning */ }
        var need = plan.BytesToDownload + plan.BytesToCopy + (256L << 20);
        var lowDiskSpace = free > 0 && free < need;
        if (lowDiskSpace)
        {
            if (modeChange is { } diskMode && plan.Downloads.Count == 0)
                headline = $"Switch to {LauncherModes.Name(diskMode)}?";
            headline += $"\n\nWarning: only {Pack.FormatBytes(free)} free on {drive} — this needs about {Pack.FormatBytes(need)}.";
        }
        if (modeChange is null && textureProfileChange is null && optionChange is null || plan.Downloads.Count > 0 || lowDiskSpace)
        {
            if (Form.ActiveForm != this) FlashTaskbar();
            if (MessageBox.Show(this, headline, "Clonedivers", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                RefreshState();
                return;
            }
        }

        try
        {
            if (plan.Downloads.Count > 0)
            {
                SetBusy(BusyKind.Download);
                cts = opCts = new CancellationTokenSource();
                var dl = new Progress<(long done, long total, int file, int files)>(p => ShowProgress($"Downloading pack ({p.file} of {p.files})", p.done, p.total));
                await Pack.DownloadPlanAsync(plan, dlDir, dl, cts.Token);
            }
            SetBusy(BusyKind.Apply);
            var result = await Task.Run(() => Pack.Apply(gameDir!, plan, planPath, targetVersion, hashCache, Settings.HashCachePath, targetBuild, targetOptions, targetProfile, verify, receiptPath));
            ReportApply(result, plan.Downloads.Count, preserveTexturePreference: !targetActive && textureProfileChange is null);
        }
        catch (OperationCanceledException) { ShowProgressDone("Cancelled. Downloaded files stay in mods_download\\ and resume next time."); }
        catch (Exception ex) { lastOperationError = ex.Message; SupportHistory.Record("error", targetVersion, targetProfile, ex.Message); ShowProgressDone("Update failed."); MessageBox.Show(this, ex.Message, "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(BusyKind.None); }
    }

    void ReportApply(ApplyResult result, int downloaded, bool preserveTexturePreference = false)
    {
        if (result.Interrupted)
        {
            var names = string.Join(", ", result.Failed.Take(3)) + (result.Failed.Count > 3 ? ", …" : "");
            lastOperationError = result.Reason ?? names;
            SupportHistory.Record("error", result.PackVersion, result.TextureProfile, lastOperationError);
            ShowProgressDone(result.Reason is not null && result.Failed.Count == 0
                ? $"Update interrupted: {result.Reason}. Click Finish update to continue."
                : $"Couldn't move {result.Failed.Count} file(s) (antivirus?): {names}. Close whatever has them open and click Finish update.");
            return;
        }
        settings.RecordInstall(result, preserveTexturePreference);
        settings.Save();
        FpvDefaults.EnsureForInstalledPack(gameDir!);
        lastOperationError = null;
        SupportHistory.Record("installed", result.PackVersion, result.TextureProfile);
        var parts = new List<string>();
        if (downloaded > 0) parts.Add($"{downloaded:N0} downloaded");
        if (result.Renamed > 0) parts.Add($"{result.Renamed:N0} renamed");
        if (result.Copied + result.Created > 0) parts.Add($"{result.Copied + result.Created:N0} created");
        if (result.Parked > 0) parts.Add($"{result.Parked:N0} parked in mods_old\\");
        ShowProgressDone($"Pack {result.PackVersion} ready." + (result.TargetActive ? " Ready to launch." : " Helldivers remains selected."));
    }

    /// <summary>Finish the exact saved target, including missing downloads. The current feed cannot replace a pending choice.</summary>
    async Task FinishUpdateAsync()
    {
        var target = RecoveryTarget.Read(gameDir!, Settings.PlanPath);
        if (target is not null)
        {
            var savedPack = new PackManifest { Version = target.PackVersion, GameBuild = target.GameBuild,
                Files = target.Files.Select(f => f.Clone()).ToList(),
                Options = target.Options.Select(o => new PackOption { Id = o.Key, Default = o.Value }).ToList() };
            await RunPackAsync(savedPack, verify: true, optionChange: null, savedTarget: target);
            return;
        }
        // Old action-only plans did not record a trustworthy target. Only these require a
        // manifest-based repair; valid modern plans never lose their requested settings.
        var pack = manifest?.Pack;
        if (pack is null || !pack.IsPublished || !pack.IsPerFile)
        {
            lastOperationError = "The interrupted update has no usable saved target or available manifest.";
            SupportHistory.Record("error", settings.InstalledPackVersion, settings.InstalledTextureProfile, lastOperationError);
            ShowProgressDone("Can't finish offline: the saved plan is unusable and the manifest is not available. Connect and click Finish update again.");
            return;
        }
        await RunPackAsync(pack, verify: true, optionChange: null);
    }

    // ---- self-update

    void OnAppUpdate()
    {
        var app = manifest?.App;
        if (Busy || app is null || !CanUpdateApp(app)) return;
        var exePath = Environment.ProcessPath;
        if (exePath is null) return;
        var (exe, update, old) = SelfUpdate.Paths(exePath);
        if (MessageBox.Show(this, $"Update Clonedivers to {app.Version}?\n\nIt downloads {Pack.FormatBytes(app.Size)} and restarts by itself.",
                "Clonedivers", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { SelfUpdate.ProbeWritable(update); }
        catch (Exception ex) { BrowserFallback(Path.GetDirectoryName(exe)!, ex.Message); return; }
        _ = AppUpdateAsync(app, exe, update, old);
    }

    void BrowserFallback(string dir, string reason)
    {
        MessageBox.Show(this, $"Clonedivers can't replace itself in this folder:\n{dir}\n{reason}\n\nThe release page opens in your browser — download Clonedivers.exe there and replace this file.",
            "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        try { Process.Start(new ProcessStartInfo("https://github.com/owendavidgoode/clonedivers/releases/latest") { UseShellExecute = true }); } catch { }
    }

    async Task AppUpdateAsync(AppRelease app, string exe, string update, string old)
    {
        if (!CanUpdateApp(app)) return;
        SetBusy(BusyKind.AppUpdate);
        var cts = opCts = new CancellationTokenSource();
        try
        {
            var file = new PackFile { Url = app.Url, Size = app.Size, Sha256 = app.Sha256, Name = "" };
            var reporter = new Progress<(long done, long total)>(p => ShowProgress($"Downloading Clonedivers {app.Version}", p.done, p.total));
            await Pack.DownloadAsync(file, update, reporter, cts.Token);
            // It sat on disk for a moment (OneDrive / Defender may have touched it): check again before trusting it.
            var sha = await Pack.Sha256Async(update, cts.Token);
            if (!sha.Equals(app.Sha256.Trim(), StringComparison.OrdinalIgnoreCase)) { File.Delete(update); throw new InvalidDataException("The downloaded Clonedivers failed its integrity check. Deleted it; try again."); }
            if (!CanUpdateApp(app)) throw new InvalidOperationException("Self-update is disabled for this manifest source.");
            ShowProgressDone("Restarting…");
            await Task.Run(() => SelfUpdate.Swap(exe, update, old, TimeSpan.FromSeconds(30)));
            Program.ReleaseSingleInstance();
            Process.Start(new ProcessStartInfo(exe, "--updated " + app.Version) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
            exitingForUpdate = true;
            busy = BusyKind.None;   // OnFormClosing must not ask "cancel the update and quit?" — the update is done
            Close();
        }
        catch (OperationCanceledException) { ShowProgressDone("Update cancelled. The partial download resumes next time."); }
        catch (UnauthorizedAccessException ex) { ShowProgressDone("Update failed."); BrowserFallback(Path.GetDirectoryName(exe)!, ex.Message); }
        catch (Exception ex)
        {
            ShowProgressDone("Update failed.");
            MessageBox.Show(this, $"Clonedivers couldn't replace itself: {ex.Message}\n\nIf the new version is saved as {Path.GetFileName(update)} next to it, try again later or rename it by hand.",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
            OpenFolder(Path.GetDirectoryName(exe)!);
        }
        finally { SetBusy(BusyKind.None); }
    }

    // ---- progress plumbing

    void SetBusy(BusyKind kind)
    {
        busy = kind;
        if (kind != BusyKind.None) { rateWatch.Reset(); rateText = ""; lastWhat = ""; progress.Fraction = 0; }
        else { opCts?.Dispose(); opCts = null; Text = "Clonedivers"; }
        Power.KeepAwake(kind != BusyKind.None);
        RefreshState();
    }

    void ShowProgress(string what, long done, long total)
    {
        progress.Active = true;
        if (what != lastWhat) { lastWhat = what; rateWatch.Reset(); rateText = ""; }
        if (total > 0) { progress.Marquee = false; progress.Fraction = done / (double)total; }
        else progress.Marquee = true;

        if (!rateWatch.IsRunning) { rateWatch.Start(); rateBytes = done; }
        else if (rateWatch.ElapsedMilliseconds >= 1000)
        {
            var mbps = (done - rateBytes) / 1048576.0 / (rateWatch.ElapsedMilliseconds / 1000.0);
            rateText = $"  ·  {mbps:0.0} MB/s";
            rateWatch.Restart();
            rateBytes = done;
        }
        var pct = total > 0 ? (int)Math.Clamp(done * 100 / total, 0, 100) : -1;
        progressLabel.ForeColor = TextDim;
        progressLabel.Text = total > 0
            ? $"{what}  ·  {Pack.FormatBytes(done)} / {Pack.FormatBytes(total)}{rateText}  ·  {pct}%"
            : $"{what}  ·  {Pack.FormatBytes(done)}";
        Text = pct >= 0 ? $"Clonedivers  ·  {pct}%" : "Clonedivers";   // the taskbar shows progress while you alt-tab
        lastProgressMessage = DateTime.UtcNow;
    }

    void ShowProgressDone(string text)
    {
        progress.Active = false;
        progressLabel.ForeColor = TextDim;
        progressLabel.Text = text;
        lastProgressMessage = DateTime.UtcNow;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    /// <summary>Flashes the taskbar button until the window is focused: the inventory takes a while and friends alt-tab away.</summary>
    void FlashTaskbar()
    {
        try
        {
            var f = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = Handle, dwFlags = 2 | 12 /* FLASHW_TRAY | FLASHW_TIMERNOFG */, uCount = 0, dwTimeout = 0 };
            FlashWindowEx(ref f);
        }
        catch { }
    }

    // ---- toggle / launch / locate

    bool TryToggle()
    {
        try
        {
            ModFiles.Toggle(gameDir!);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this,
                "Windows refused to move files inside the game folder:\n" + ex.Message +
                "\n\nTry running Clonedivers as administrator (right-click → Run as administrator).",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this,
                ex.Message +
                "\n\nUsually another program has that file open — an antivirus scan, a download still finishing, or the game itself. " +
                "The other files moved. Once it's free, select Helldivers again to park the remaining files. " +
                "The selected mode reflects the files currently installed.",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return false;
    }

    void OnMode(LauncherMode mode)
    {
        if (preview) { previewMode = mode; RefreshPreview(); return; }
        if (gameDir is null || Busy) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }
        if (Pack.HasPendingUpdate(gameDir, Settings.PlanPath)) { RefreshState(); return; }
        var state = ModFiles.GetState(gameDir);
        if (mode == LauncherMode.Helldivers)
        {
            if (state == ModState.On) TryToggle();
            RefreshState();
            return;
        }
        var pack = manifest?.Pack;
        if (pack is null || !pack.IsPublished || !pack.IsPerFile) return;
        if (mode == LauncherMode.CommandoDivers && !pack.Options.Any(o => o.Id.Equals(LauncherModes.CommandoOption, StringComparison.OrdinalIgnoreCase))) return;
        if (mode == LauncherMode.EmpireDivers && !LauncherModes.SupportsEmpire(pack)) return;
        // Always plan the requested file set, including when an old pack is parked. Restoring it
        // with Toggle would silently enable RC content after choosing ordinary clonedivers.
        _ = RunPackAsync(pack, verify: false, optionChange: null, modeChange: mode,
            textureProfileChange: AvailableTextureProfile(pack) is null ? "full" : null);
    }

    void RefreshPreview()
    {
        manifest ??= new Manifest { Pack = new PackManifest { Options = new()
        {
            new() { Id = "commandos", Name = "RC mode", Default = true },
            new() { Id = "skinny", Name = "Lighter textures", Default = false },
        } } };
        RebuildOptions();
        RefreshModeLayout();
        if (manifest.Pack?.CombinedRoster == true && previewMode == LauncherMode.CommandoDivers)
            previewMode = LauncherMode.Clonedivers;
        optionsRow.Visible = previewMode != LauncherMode.Helldivers;
        foreach (var button in optionButtons)
        {
            var option = PackProfiles.Supported(manifest.Pack!).Single(o => o.Id == (string)button.Tag!);
            var on = AvailableTextureProfile(manifest.Pack!) is {} selected && selected != "full";
            button.Text = PackProfiles.PotatoName + (on ? ": On" : ": Off");
            button.BackColor = on ? Blue : Slate;
            button.Enabled = true;
        }
        foreach (var button in modeButtons) { button.Enabled = true; button.Selected = button.Mode == previewMode; }
        RefreshExtras(true);
        statusLabel.Text = $"{LauncherModes.Name(previewMode)} selected";
        hintLabel.Text = ModeDescription(previewMode);
        launchButton.Text = "LAUNCH " + LauncherModes.Name(previewMode).ToUpperInvariant();
        foreach (var control in new Control[] { launchButton, pathButton, openButton, installFileButton, downloadButton }) control.Enabled = false;
        downloadButton.Text = "Pack installed";
        diagnosticsButton.Enabled = false;
        pathCaption.Text = "Preview";
        pathValue.Text = "No game files or settings are changed";
        footer.Text = "Launcher preview  ·  " + (previewMode == LauncherMode.EmpireDivers ? "For the Empire." : "For the Republic.");
    }

    string ModeDescription(LauncherMode mode) => mode switch
    {
        LauncherMode.Helldivers => "Standard Helldivers 2.",
        LauncherMode.Clonedivers when manifest?.Pack?.CombinedRoster == true => "For the Republic.\nCommando armor needs Brawny body type.",
        LauncherMode.Clonedivers => "For the Republic.",
        LauncherMode.EmpireDivers => "For the Empire.\nB-01: Brawny · Bloodhound: Lean.",
        _ => "Delta Squad is elite.\nRequires Brawny body type.",
    };

    void OnLaunch()
    {
        if (busy == BusyKind.Apply) return;
        if (Game.IsRunning()) { ShowRunningWarning(); return; }   // the dim button still explains itself when clicked
        if (!launchArmed) return;
        var state = ModFiles.GetState(gameDir);
        var signal = CurrentSignal(state, manifest?.Pack, SteamAcf.Read(gameDir));
        if (state == ModState.On && signal != Signal.None && !Busy)
        {
            var answer = MessageBox.Show(this,
                SignalSentence(signal, manifest?.Pack) + "\n\nChoose Helldivers before launching?\n\n" +
                "Yes — park the mods, then launch\nNo — launch the current mode\nCancel — don't launch",
                "Clonedivers", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes && !TryToggle()) { RefreshState(); return; }
        }
        try { FpvDefaults.EnsureForInstalledPack(gameDir); }
        catch (Exception ex)
        {
            lastOperationError = ex.Message;
            SupportHistory.Record("error", settings.InstalledPackVersion, settings.InstalledTextureProfile, ex.Message);
            ShowProgressDone("FPV options need attention. Launch stopped.");
            MessageBox.Show(this, ex.Message, "FPV options", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        try
        {
            sessionObserver?.Request(DateTimeOffset.UtcNow);
            Game.LaunchViaSteam();
            launchRequested = DateTime.UtcNow;
            RefreshState();
        }
        catch (Exception ex)
        {
            sessionObserver?.HandoffFailed();
            MessageBox.Show(this, "Couldn't hand off to Steam:\n" + ex.Message + "\n\nIs Steam installed?",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void OnLocate()
    {
        if (Busy) return;
        var picked = PromptForGameDir();
        if (picked is null) return;
        if (!string.Equals(gameDir, picked, StringComparison.OrdinalIgnoreCase))
        {
            settings.InstalledPackVersion = null;
            settings.InstalledGameBuild = null;
            settings.InstalledTextureProfile = null;
            settings.LastVerifiedUtc = null;
        }
        gameDir = picked;
        settings.GamePath = picked;
        RestoreInstallationMetadata();
        settings.Save();
        RefreshState();
    }

    string? PromptForGameDir()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Where is Helldivers 2?  Pick the game folder (it contains data and bin)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        var steam = GameLocator.SteamPath();
        var common = steam is null ? null : Path.Combine(steam, "steamapps", "common");
        if (common is not null && Directory.Exists(common)) dlg.InitialDirectory = common;

        while (true)
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return null;
            var normalized = GameLocator.NormalizeGameDir(dlg.SelectedPath);
            if (normalized is not null) return normalized;
            MessageBox.Show(this,
                "That doesn't look like the Helldivers 2 folder:\n" + dlg.SelectedPath +
                "\n\nPick the folder that contains data\\ and bin\\helldivers2.exe — usually\n" +
                @"C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2" +
                "\n\nNot sure? In Steam: right-click Helldivers 2 → Manage → Browse local files. That window is the folder to pick.",
                "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    static void OpenFolder(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); }
        catch { /* explorer missing? nothing useful to do */ }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (exitingForUpdate) { base.OnFormClosing(e); return; }
        if (busy == BusyKind.Apply)
        {
            MessageBox.Show(this, "Checking and finishing the update. Please keep this window open until it completes.", "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }
        if (Busy && MessageBox.Show(this, "A pack update is in progress. Cancel it and quit?", "Clonedivers",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        opCts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RecordSession("launcher-closed");
        telemetryRecorder?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { refresh.Dispose(); tips.Dispose(); opCts?.Dispose(); }
        base.Dispose(disposing);
    }
}

static class Program
{
    static Mutex? single;

    /// <summary>Lets the freshly swapped exe start while this one is still shutting down (the mutex is held for the process lifetime otherwise).</summary>
    public static void ReleaseSingleInstance()
    {
        try { single?.ReleaseMutex(); single?.Dispose(); } catch { }
        single = null;
    }

    [STAThread]
    static void Main(string[] args)
    {
        // The 60 MB self-contained exe takes a few seconds to unpack on first launch; a second double-click
        // during that pause should not open a second window.
        single = new Mutex(true, @"Local\Clonedivers", out bool firstInstance);
        if (!firstInstance) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 10F));

        string? updatedTo = null;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--updated") updatedTo = args[i + 1];
        string? migrationNotice = null;
        if (Environment.ProcessPath is { } exe)
        {
            try
            {
                var backup = ForgeLauncherUpdate.PreservePrevious(exe);
                SelfUpdate.CleanupOld(exe);
                if (backup != null) migrationNotice = "Forge is now the default launcher. Your previous launcher is retained at " + backup +
                    ". Steam files and saved settings were not changed. Set up the separate HellForge installation and matching gameplay mods before playing.";
                else if (updatedTo != null) migrationNotice = $"Updated to {updatedTo}.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            { migrationNotice = "The previous launcher remains beside this file because its backup could not be completed: " + error.Message; }
        }

        if (args.Contains("--steam", StringComparer.OrdinalIgnoreCase))
        {
            var settings = Settings.Load();
            var gameDir = GameLocator.IsGameDir(settings.GamePath) ? settings.GamePath : GameLocator.AutoDetect();
            Application.Run(new MainForm(settings, gameDir, updatedTo, allowAppUpdates: false));
        }
        else
        {
            try { Application.Run(new HellForgeForm(HellForgePreferences.Load(), migrationNotice: migrationNotice)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                MessageBox.Show("Couldn't open the HellForge launcher:\n" + e.Message + "\n\nYour Steam setup is preserved. Start this launcher with --steam to open it.",
                    "Clonedivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        ReleaseSingleInstance();
    }
}
