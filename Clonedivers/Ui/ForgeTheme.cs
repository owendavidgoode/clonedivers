using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Brush = Avalonia.Media.Brush;
using Brushes = Avalonia.Media.Brushes;
using Button = Avalonia.Controls.Button;
using Color = Avalonia.Media.Color;
using Control = Avalonia.Controls.Control;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using ShapePath = Avalonia.Controls.Shapes.Path;
using SolidColorBrush = Avalonia.Media.SolidColorBrush;

namespace Clonedivers.Ui;

/// <summary>Modern dark Star Wars launcher visual system shared across Windows and Linux.</summary>
public static class ForgeTheme
{
    // Layered dark obsidian / republic-navy surfaces
    public static readonly Color BgCanvasColor = Color.Parse("#080C17");
    public static readonly Color BgSurfaceColor = Color.Parse("#101728");
    public static readonly Color BgElevatedColor = Color.Parse("#151F36");
    public static readonly Color BgSelectedColor = Color.Parse("#182744");
    public static readonly Color BgOverlayColor = Color.Parse("#CC04070F");

    public static readonly IBrush BgCanvas = new SolidColorBrush(BgCanvasColor);
    public static readonly IBrush BgSurface = new SolidColorBrush(BgSurfaceColor);
    public static readonly IBrush BgElevated = new SolidColorBrush(BgElevatedColor);
    public static readonly IBrush BgSelected = new SolidColorBrush(BgSelectedColor);
    public static readonly IBrush BgOverlay = new SolidColorBrush(BgOverlayColor);
    public static readonly IBrush BgInput = Brush.Parse("#0B1120");
    public static readonly IBrush BgPill = Brush.Parse("#1B2742");
    public static readonly IBrush BgTrack = Brush.Parse("#1D2842");

    // Borders
    public static readonly IBrush BorderSubtle = Brush.Parse("#22304D");
    public static readonly IBrush BorderStrong = Brush.Parse("#334770");

    // Typography hierarchy
    public static readonly IBrush TextPrimary = Brush.Parse("#F3F6FC");
    public static readonly IBrush TextSecondary = Brush.Parse("#9AA9C7");
    public static readonly IBrush TextMuted = Brush.Parse("#657594");
    public static readonly IBrush TextOnAccent = Brush.Parse("#080C17");

    // Faction & semantic accents
    public static readonly IBrush RegularAccent = Brush.Parse("#F59E0B");     // Helldivers Super Earth Gold
    public static readonly IBrush RegularGlow = Brush.Parse("#29F59E0B");
    public static readonly IBrush ClonesAccent = Brush.Parse("#38BDF8");      // 501st Republic Sky Blue
    public static readonly IBrush ClonesGlow = Brush.Parse("#2938BDF8");
    public static readonly IBrush EmpireAccent = Brush.Parse("#EF4444");      // Imperial Crimson
    public static readonly IBrush EmpireGlow = Brush.Parse("#29EF4444");

    public static readonly IBrush CtaPrimary = Brush.Parse("#F59E0B");
    public static readonly IBrush CtaPrimaryHover = Brush.Parse("#FBBF24");
    public static readonly IBrush CtaBlue = Brush.Parse("#2563EB");
    public static readonly IBrush CtaDanger = Brush.Parse("#DC2626");

    public static readonly IBrush Success = Brush.Parse("#22C55E");
    public static readonly IBrush SuccessBg = Brush.Parse("#163826");
    public static readonly IBrush SuccessBorder = Brush.Parse("#286B46");
    public static readonly IBrush SuccessText = Brush.Parse("#86EFAC");

    public static readonly IBrush Warning = Brush.Parse("#F59E0B");
    public static readonly IBrush WarningBg = Brush.Parse("#382812");
    public static readonly IBrush WarningBorder = Brush.Parse("#784E14");
    public static readonly IBrush WarningText = Brush.Parse("#FDE68A");

    public static readonly IBrush Danger = Brush.Parse("#EF4444");
    public static readonly IBrush DangerBg = Brush.Parse("#3B161C");
    public static readonly IBrush DangerBorder = Brush.Parse("#7F2330");
    public static readonly IBrush DangerText = Brush.Parse("#FCA5A5");

    public static readonly IBrush InfoBg = Brush.Parse("#13284A");
    public static readonly IBrush InfoBorder = Brush.Parse("#27569E");
    public static readonly IBrush InfoText = Brush.Parse("#93C5FD");

    public static IBrush AccentForMode(string mode) => mode switch
    {
        "regular" => RegularAccent,
        "empire" => EmpireAccent,
        _ => ClonesAccent
    };

    public static IBrush GlowForMode(string mode) => mode switch
    {
        "regular" => RegularGlow,
        "empire" => EmpireGlow,
        _ => ClonesGlow
    };

    public static TextBlock Label(
        string text,
        double size = 13,
        IBrush? foreground = null,
        FontWeight weight = FontWeight.Normal,
        TextAlignment alignment = TextAlignment.Left,
        bool wrap = true) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = foreground ?? TextPrimary,
        FontWeight = weight,
        TextAlignment = alignment,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    public static Border Pill(string text, IBrush bg, IBrush fg, IBrush? border = null, double fontSize = 11)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new Border
        {
            Background = bg,
            BorderBrush = border ?? Brushes.Transparent,
            BorderThickness = border != null ? new Thickness(1) : new Thickness(0),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 3),
            Child = label
        };
    }

    public static Button GhostButton(string text, double fontSize = 12.5, Thickness? padding = null) => new()
    {
        Content = text,
        FontSize = fontSize,
        FontWeight = FontWeight.SemiBold,
        Foreground = TextPrimary,
        Background = BgElevated,
        BorderBrush = BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding ?? new Thickness(14, 8),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
    };

    public static Button LinkStyleButton(string text, IBrush? fg = null, double fontSize = 12) => new()
    {
        Content = text,
        FontSize = fontSize,
        FontWeight = FontWeight.Medium,
        Foreground = fg ?? TextSecondary,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(4, 2),
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
    };

    public static Button PrimaryButton(string text, double height = 50, double fontSize = 15) => new()
    {
        Content = text,
        Height = height,
        FontSize = fontSize,
        FontWeight = FontWeight.Bold,
        Foreground = TextOnAccent,
        Background = CtaPrimary,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(10),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
    };

    /// <summary>Vector faction helmet badge rendered cleanly at any DPI.</summary>
    public static Control FactionEmblem(string mode, double size = 64)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };
        void Shape(string geometry, string color) =>
            canvas.Children.Add(new ShapePath { Data = Geometry.Parse(geometry), Fill = Brush.Parse(color) });

        if (mode == "empire")
        {
            Shape("M14 40 C13 -1 87 -1 86 40 L90 51 87 65 94 81 81 93 66 94 58 88 42 88 34 94 19 93 6 81 13 65 10 51 Z", "#F3F6FC");
            Shape("M14 34 L32 31 68 31 86 34 87 40 67 37 33 37 13 40 Z M19 43 L31 40 46 46 42 55 25 53 Z M81 43 L69 40 54 46 58 55 75 53 Z M31 73 L42 61 50 57 58 61 69 73 59 69 50 64 41 69 Z", "#0B1020");
            Shape("M11 77 L20 71 30 76 35 87 29 90 20 86 Z M89 77 L80 71 70 76 65 87 71 90 80 86 Z", "#9AA9C7");
            Shape("M46 13 H54 V27 H46 Z", "#EF4444");
        }
        else if (mode == "regular")
        {
            Shape("M18 45 C15 0 85 0 82 45 L86 75 72 92 28 92 14 75 Z", "#E2E8F0");
            Shape("M19 36 L81 36 76 56 61 64 39 64 24 56 Z M44 72 H56 V87 H44 Z", "#0B1020");
            Shape("M22 39 L78 39 75 46 25 46 Z M59 13 H66 V32 H59 Z", "#F59E0B");
        }
        else
        {
            Shape("M18 45 C15 0 85 0 82 45 L86 75 72 92 28 92 14 75 Z", "#F3F6FC");
            Shape("M45 10 H55 V35 H45 Z", "#38BDF8");
            Shape("M22 40 L78 40 73 51 56 53 54 74 46 74 44 53 27 51 Z M39 82 H61 V86 H39 Z", "#0B1020");
            Shape("M20 62 L32 62 34 73 22 73 Z M68 62 L80 62 78 73 66 73 Z", "#9AA9C7");
        }

        return new Border
        {
            Width = size + 18,
            Height = size + 18,
            CornerRadius = new CornerRadius((size + 18) / 2),
            Background = GlowForMode(mode),
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Viewbox
            {
                Width = size,
                Height = size,
                Child = canvas
            }
        };
    }

    /// <summary>Compact header brand mark.</summary>
    public static Control HeaderMark(double size = 34)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };
        void Shape(string geometry, string color) =>
            canvas.Children.Add(new ShapePath { Data = Geometry.Parse(geometry), Fill = Brush.Parse(color) });
        Shape("M18 45 C15 0 85 0 82 45 L86 75 72 92 28 92 14 75 Z", "#F3F6FC");
        Shape("M45 10 H55 V35 H45 Z", "#38BDF8");
        Shape("M22 40 L78 40 73 51 56 53 54 74 46 74 44 53 27 51 Z M39 82 H61 V86 H39 Z", "#0B1020");
        return new Border
        {
            Width = size + 10,
            Height = size + 10,
            CornerRadius = new CornerRadius(10),
            Background = Brush.Parse("#132342"),
            BorderBrush = Brush.Parse("#2A4B7C"),
            BorderThickness = new Thickness(1),
            Child = new Viewbox { Width = size, Height = size, Child = canvas }
        };
    }

    /// <summary>Cross-platform QR bitmap reconstructed from HellForge CLI ANSI half-block rows.</summary>
    public static WriteableBitmap RenderQrBitmap(string[] rows, int maxPixels = 230)
    {
        var size = rows[0].Length;
        var scale = Math.Max(1, maxPixels / size);
        var dim = size * scale;
        var bitmap = new WriteableBitmap(new PixelSize(dim, dim), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = bitmap.Lock();
        var stride = fb.RowBytes / 4;
        var buffer = new int[stride * dim];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var ch = rows[y / 2][x];
                var light = y % 2 == 0 ? ch is '\u2588' or '\u2580' : ch is '\u2588' or '\u2584';
                int pixel = unchecked((int)(light ? 0xFFFFFFFFu : 0xFF000000u));
                for (var dy = 0; dy < scale; dy++)
                {
                    var rowOffset = (y * scale + dy) * stride + x * scale;
                    for (var dx = 0; dx < scale; dx++)
                        buffer[rowOffset + dx] = pixel;
                }
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, fb.Address, buffer.Length);
        return bitmap;
    }
}
