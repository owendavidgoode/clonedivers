using System.Buffers.Binary;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Brush = Avalonia.Media.Brush;
using Brushes = Avalonia.Media.Brushes;
using Button = Avalonia.Controls.Button;
using Color = Avalonia.Media.Color;
using Control = Avalonia.Controls.Control;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using ScrollBar = Avalonia.Controls.Primitives.ScrollBar;
using ShapePath = Avalonia.Controls.Shapes.Path;
using SolidColorBrush = Avalonia.Media.SolidColorBrush;

namespace Clonedivers.Ui;

/// <summary>Modern dark Star Wars holoterminal visual system shared across Windows and Linux.</summary>
public static class ForgeTheme
{
    // Layered dark obsidian / republic-navy surfaces
    public static readonly Color BgCanvasColor = Color.Parse("#080C17");
    public static readonly Color BgSurfaceColor = Color.Parse("#101728");
    public static readonly Color BgElevatedColor = Color.Parse("#151F36");
    public static readonly Color BgSelectedColor = Color.Parse("#182744");
    public static readonly Color BgOverlayColor = Color.Parse("#D604070F");

    public static readonly IBrush BgCanvas = new SolidColorBrush(BgCanvasColor);
    public static readonly IBrush BgSurface = new SolidColorBrush(BgSurfaceColor);
    public static readonly IBrush BgSurfaceHover = Brush.Parse("#131D32");
    public static readonly IBrush BgElevated = new SolidColorBrush(BgElevatedColor);
    public static readonly IBrush BgElevatedHover = Brush.Parse("#1B2846");
    public static readonly IBrush BgSelected = new SolidColorBrush(BgSelectedColor);
    public static readonly IBrush BgOverlay = new SolidColorBrush(BgOverlayColor);
    public static readonly IBrush BgInput = Brush.Parse("#0B1120");
    public static readonly IBrush BgPill = Brush.Parse("#1B2742");
    public static readonly IBrush BgTrack = Brush.Parse("#1D2842");

    // Borders & holoterminal hairlines
    public static readonly IBrush BorderSubtle = Brush.Parse("#22304D");
    public static readonly IBrush BorderStrong = Brush.Parse("#334770");
    public static readonly IBrush HoloDivider = Brush.Parse("#1E2F4F");
    public static readonly IBrush ScrollThumb = Brush.Parse("#2C3E63");
    public static readonly IBrush ScrollThumbHover = Brush.Parse("#3E588A");

    // Typography hierarchy
    public static readonly IBrush TextPrimary = Brush.Parse("#F3F6FC");
    public static readonly IBrush TextSecondary = Brush.Parse("#9AA9C7");
    public static readonly IBrush TextMuted = Brush.Parse("#657594");
    public static readonly IBrush TextOnAccent = Brush.Parse("#080C17");

    // Faction & semantic accents (#F59E0B Super Earth Gold, #38BDF8 Republic Cyan, #EF4444 Imperial Crimson)
    public static readonly IBrush RegularAccent = Brush.Parse("#F59E0B");
    public static readonly IBrush RegularGlow = Brush.Parse("#29F59E0B");
    public static readonly IBrush RegularBarMuted = Brush.Parse("#59F59E0B");

    public static readonly IBrush ClonesAccent = Brush.Parse("#38BDF8");
    public static readonly IBrush ClonesGlow = Brush.Parse("#2938BDF8");
    public static readonly IBrush ClonesBarMuted = Brush.Parse("#5938BDF8");

    public static readonly IBrush EmpireAccent = Brush.Parse("#EF4444");
    public static readonly IBrush EmpireGlow = Brush.Parse("#29EF4444");
    public static readonly IBrush EmpireBarMuted = Brush.Parse("#59EF4444");

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

    public static IBrush MutedBarForMode(string mode) => mode switch
    {
        "regular" => RegularBarMuted,
        "empire" => EmpireBarMuted,
        _ => ClonesBarMuted
    };

    /// <summary>Smooth 150ms holoterminal brush transitions for cards and buttons.</summary>
    public static Transitions CreateSmoothTransitions(AvaloniaProperty bgProp, AvaloniaProperty borderProp) =>
    [
        new BrushTransition { Property = bgProp, Duration = TimeSpan.FromMilliseconds(150) },
        new BrushTransition { Property = borderProp, Duration = TimeSpan.FromMilliseconds(150) }
    ];

    /// <summary>Shared dark-slate custom scrollbar and smooth button styles across Windows and Linux.</summary>
    public static Styles BuildGlobalStyles()
    {
        var styles = new Styles();

        var scrollBarStyle = new Style(x => x.OfType<ScrollBar>());
        scrollBarStyle.Setters.Add(new Setter(ScrollBar.AllowAutoHideProperty, false));
        scrollBarStyle.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent));
        scrollBarStyle.Setters.Add(new Setter(Layoutable.WidthProperty, 6d));
        scrollBarStyle.Setters.Add(new Setter(Layoutable.MinWidthProperty, 6d));
        styles.Add(scrollBarStyle);

        var repeatBtnStyle = new Style(x => x.OfType<ScrollBar>().Descendant().OfType<RepeatButton>());
        repeatBtnStyle.Setters.Add(new Setter(Visual.IsVisibleProperty, false));
        styles.Add(repeatBtnStyle);

        var thumbStyle = new Style(x => x.OfType<ScrollBar>().Descendant().OfType<Thumb>());
        thumbStyle.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, ScrollThumb));
        thumbStyle.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)));
        thumbStyle.Setters.Add(new Setter(Layoutable.MinHeightProperty, 28d));
        thumbStyle.Setters.Add(new Setter(Layoutable.WidthProperty, 6d));
        styles.Add(thumbStyle);

        var thumbHover = new Style(x => x.OfType<ScrollBar>().Descendant().OfType<Thumb>().Class(":pointerover"));
        thumbHover.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, ScrollThumbHover));
        styles.Add(thumbHover);

        return styles;
    }

    /// <summary>Subtle 1px Star Wars holoterminal horizontal accent rule.</summary>
    public static Border HoloAccentLine(IBrush? brush = null, Thickness? margin = null) => new()
    {
        Height = 1,
        Background = brush ?? HoloDivider,
        Margin = margin ?? new Thickness(28, 0, 28, 8)
    };

    /// <summary>Loads the cross-platform Clone helmet WindowIcon for Windows, X11, Wayland, and TrayIcon.</summary>
    public static WindowIcon CreateWindowIcon()
    {
        try
        {
            using var icoStream = typeof(ForgeTheme).Assembly.GetManifestResourceStream("Clonedivers.Icon.ico");
            if (icoStream != null)
            {
                using var ms = new MemoryStream();
                icoStream.CopyTo(ms);
                var bytes = ms.ToArray();

                // On Linux X11/Wayland, Skia decodes pure 256x256 PNG slices cleanly across all window managers.
                if (OperatingSystem.IsLinux() && TryExtractLargestPngFromIco(bytes, out var linuxPng))
                    return new WindowIcon(new MemoryStream(linuxPng, writable: false));

                try
                {
                    return new WindowIcon(new MemoryStream(bytes, writable: false));
                }
                catch
                {
                    if (TryExtractLargestPngFromIco(bytes, out var fallbackPng))
                        return new WindowIcon(new MemoryStream(fallbackPng, writable: false));
                }
            }
        }
        catch { }

        return new WindowIcon(CreateFallbackHelmetBitmap(64));
    }

    internal static bool TryExtractLargestPngFromIco(byte[] icoBytes, out byte[] pngBytes)
    {
        pngBytes = [];
        if (icoBytes.Length < 22 ||
            BinaryPrimitives.ReadUInt16LittleEndian(icoBytes.AsSpan(0, 2)) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(icoBytes.AsSpan(2, 2)) != 1)
            return false;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(icoBytes.AsSpan(4, 2));
        if (count <= 0 || 6 + count * 16 > icoBytes.Length) return false;

        int bestScore = -1;
        int bestOffset = 0;
        int bestLen = 0;

        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            int w = icoBytes[entry] == 0 ? 256 : icoBytes[entry];
            int h = icoBytes[entry + 1] == 0 ? 256 : icoBytes[entry + 1];
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(icoBytes.AsSpan(entry + 8, 4));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(icoBytes.AsSpan(entry + 12, 4));
            if (size < 8 || offset > (uint)icoBytes.Length || size > (uint)icoBytes.Length - offset) continue;

            // PNG signature: 89 50 4E 47 0D 0A 1A 0A
            if (icoBytes[offset] == 0x89 && icoBytes[offset + 1] == 0x50 && icoBytes[offset + 2] == 0x4E && icoBytes[offset + 3] == 0x47)
            {
                int score = w * h;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = (int)offset;
                    bestLen = (int)size;
                }
            }
        }

        if (bestLen <= 0) return false;
        pngBytes = new byte[bestLen];
        Buffer.BlockCopy(icoBytes, bestOffset, pngBytes, 0, bestLen);
        return true;
    }

    static WriteableBitmap CreateFallbackHelmetBitmap(int dim)
    {
        var bmp = new WriteableBitmap(new PixelSize(dim, dim), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        var stride = fb.RowBytes / 4;
        var buf = new int[stride * dim];
        for (int y = 0; y < dim; y++)
        {
            double ny = (y + 0.5) / dim * 100.0;
            for (int x = 0; x < dim; x++)
            {
                double nx = (x + 0.5) / dim * 100.0;
                uint px = 0x00000000u;
                double dx = (nx - 50.0) / 36.0;
                double dy = (ny - 50.0) / 42.0;
                if (dx * dx + dy * dy <= 1.0)
                {
                    px = 0xFFF3F6FCu; // Clone white dome
                    if (nx is >= 45 and <= 55 && ny is >= 10 and <= 35)
                        px = 0xFF38BDF8u; // 501st cyan fin (ARGB in BGRA8888 LE: 0xAARRGGBB -> wait: Bgra8888 int LE is 0xAARRGGBB!)
                    else if ((ny is >= 40 and <= 50 && nx is >= 24 and <= 76) || (nx is >= 45 and <= 55 && ny is >= 48 and <= 76))
                        px = 0xFF0B1020u; // T-visor
                }
                buf[y * stride + x] = unchecked((int)px);
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, fb.Address, buf.Length);
        return bmp;
    }

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
        Transitions = CreateSmoothTransitions(TemplatedControl.BackgroundProperty, TemplatedControl.BorderBrushProperty),
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
        Transitions = CreateSmoothTransitions(TemplatedControl.BackgroundProperty, TemplatedControl.BorderBrushProperty),
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
        Transitions = CreateSmoothTransitions(TemplatedControl.BackgroundProperty, TemplatedControl.BorderBrushProperty),
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

    /// <summary>Exports crisp 512x512 transparent RGBA Discord Rich Presence helmet icons matching ForgeTheme vector emblems.</summary>
    public static void ExportDiscordHelmetIcons(string outputDir, int pixels = 512)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        if (pixels < 64 || pixels > 2048) throw new ArgumentOutOfRangeException(nameof(pixels));
        Directory.CreateDirectory(outputDir);

        foreach (var (mode, fileName) in new[] { ("clonedivers", "clone-helmet.png"), ("empire", "empire-helmet.png") })
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
            else
            {
                Shape("M18 45 C15 0 85 0 82 45 L86 75 72 92 28 92 14 75 Z", "#F3F6FC");
                Shape("M45 10 H55 V35 H45 Z", "#38BDF8");
                Shape("M22 40 L78 40 73 51 56 53 54 74 46 74 44 53 27 51 Z M39 82 H61 V86 H39 Z", "#0B1020");
                Shape("M20 62 L32 62 34 73 22 73 Z M68 62 L80 62 78 73 66 73 Z", "#9AA9C7");
            }

            double badgeSize = Math.Round(pixels * 0.90);
            double helmetSize = Math.Round(pixels * 0.72);
            double stroke = Math.Max(2.0, Math.Round(pixels * 0.014));

            var badge = new Border
            {
                Width = badgeSize,
                Height = badgeSize,
                CornerRadius = new CornerRadius(badgeSize / 2.0),
                Background = Brush.Parse(mode == "empire" ? "#16111F" : "#0E1C36"),
                BorderBrush = AccentForMode(mode),
                BorderThickness = new Thickness(stroke),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Border
                {
                    CornerRadius = new CornerRadius(badgeSize / 2.0),
                    Background = GlowForMode(mode),
                    Child = new Viewbox
                    {
                        Width = helmetSize,
                        Height = helmetSize,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = canvas
                    }
                }
            };

            var root = new Avalonia.Controls.Grid
            {
                Width = pixels,
                Height = pixels,
                Background = Brushes.Transparent,
                Children = { badge }
            };
            root.Measure(new Avalonia.Size(pixels, pixels));
            root.Arrange(new Rect(0, 0, pixels, pixels));
            root.UpdateLayout();

            using var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
            bitmap.Render(root);
            bitmap.Save(Path.Combine(outputDir, fileName));
        }
    }
}
