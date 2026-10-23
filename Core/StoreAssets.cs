using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AutoShutdown.Core;

/// <summary>
/// Microsoft Store / MSIX artwork: the full logo battery for the package
/// manifest, a splash screen, Start-menu tile art, composed screenshots
/// (1368x920) and a 1920x1080 hero. Everything is drawn from the same
/// procedural mark as the runtime icon, captioned with the bundled Creato
/// Display font.
/// Generate with:  AutoShutdown.exe --export-store-assets &lt;dir&gt;
/// </summary>
public static class StoreAssets
{
    private static readonly Color Cream = Color.FromArgb(0xF8, 0xFA, 0xE5);
    private static readonly Color Teal = Color.FromArgb(0x43, 0x76, 0x6C);
    private static readonly Color Orange = Color.FromArgb(0xFF, 0x98, 0x00);
    private static readonly Color Ink = Color.FromArgb(0x30, 0x38, 0x41);
    private static readonly Color Muted = Color.FromArgb(0x8A, 0x93, 0x9C);

    private static FontFamily? _displayFamily;

    private static FontFamily Display => _displayFamily ??= ResolveDisplay();

    private static FontFamily ResolveDisplay()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fonts", "CreatoDisplay-Bold.otf"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fonts", "CreatoDisplay-Bold.otf")),
        };

        foreach (var candidate in candidates.Where(File.Exists))
        {
            try
            {
                using var fonts = new PrivateFontCollection();
                fonts.AddFontFile(candidate);
                if (fonts.Families.Length > 0)
                {
                    return fonts.Families[0];
                }
            }
            catch
            {
                // fall through to the system default
            }
        }

        return FontFamily.GenericSansSerif;
    }

    private static Font DisplayFont(float pixels)
        => new(Display, pixels, FontStyle.Bold, GraphicsUnit.Pixel);

    private static Font BodyFont(float pixels)
        => new(Display, pixels, FontStyle.Regular, GraphicsUnit.Pixel);

    public static void ExportAll(string directory)
    {
        Directory.CreateDirectory(directory);
        var assets = Path.Combine(directory, "assets");
        Directory.CreateDirectory(assets);

        // ---- opaque Start-menu tiles (package manifest) ---------------------
        Save(assets, "Square150x150Logo.png", 150, 150, DrawStandoutArt);
        Save(assets, "Square310x310Logo.png", 310, 310, DrawStandoutArt);
        Save(assets, "Wide310x150Logo.png", 310, 150, DrawStandoutArt);
        Save(assets, "Square44x44Logo.png", 44, 44, DrawMarkArt);
        Save(assets, "StoreLogo.png", 50, 50, DrawMarkArt);

        // ---- transparent plate/target variants (package manifest) -----------
        int[] targetSizes = { 16, 20, 24, 30, 32, 36, 48, 56, 64, 72, 96, 144, 256 };
        foreach (var size in targetSizes)
        {
            Save(assets, $"Square44x44Logo.targetsize-{size}.png", size, size, DrawMarkArt);
        }

        var scales = new (string suffix, int size)[]
        {
            ("scale-100", 44),
            ("scale-125", 55),
            ("scale-150", 66),
            ("scale-200", 88),
        };
        foreach (var (suffix, size) in scales)
        {
            Save(assets, $"Square44x44Logo.{suffix}.png", size, size, DrawMarkArt);
        }

        // ---- splash ----------------------------------------------------------
        Save(assets, "SplashScreen.png", 620, 300, DrawSplash);
        Log.Write($"Store package assets exported to {assets}");

        // ---- composed screenshots (from the raw 2x window shots) -------------
        var rawDir = Path.Combine(directory, "raw");
        if (Directory.Exists(rawDir))
        {
            var raws = Directory.GetFiles(rawDir, "*-raw.png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var index = 1;
            foreach (var raw in raws)
            {
                using var window = Image.FromFile(raw);
                var caption = Path.GetFileNameWithoutExtension(raw)
                    .Replace("-raw", string.Empty)
                    .Replace("-", " ");
                Save(
                    directory,
                    $"screenshot-desktop-{index++}.png",
                    1368, 920,
                    (g, w, h) => DrawComposition(g, w, h, window, caption, oversized: window.Width < window.Height));
            }
            Log.Write($"Store screenshots composed: {index - 1}");
        }

        // ---- hero -------------------------------------------------------------
        var heroRaw = Path.Combine(rawDir, "setup-raw.png");
        if (File.Exists(heroRaw))
        {
            using var window = Image.FromFile(heroRaw);
            Save(directory, "hero-still.png", 1920, 1080, (g, w, h) => DrawHero(g, w, h, window));
            Log.Write("Hero image composed");
        }
    }

    private static void Save(string dir, string name, int width, int height, Action<Graphics, int, int> painter)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CompositingQuality = CompositingQuality.HighQuality;
        painter(g, width, height);
        bitmap.Save(Path.Combine(dir, name), ImageFormat.Png);
    }

    // ----------------------------------------------------------------- painters

    /// <summary>The runtime mark, centred, on transparent ground.</summary>
    private static void DrawMarkArt(Graphics g, int w, int h)
    {
        var size = Math.Min(w, h);
        using var copy = new Bitmap(size, size);
        using (var mark = Graphics.FromImage(copy))
        {
            AppIcon.DrawMark(mark, size);
        }
        g.DrawImage(copy, (w - size) / 2f, (h - size) / 2f, size, size);
    }

    /// <summary>Full-bleed teal tile with the cream power glyph and orange bite.</summary>
    private static void DrawStandoutArt(Graphics g, int w, int h)
    {
        using (var teal = new SolidBrush(Teal))
        {
            g.FillRectangle(teal, 0, 0, w, h);
        }

        var s = (float)Math.Min(w, h);
        var c = new PointF(w / 2f, h / 2f);
        var glyphRadius = s * 0.26f;

        using (var orange = new SolidBrush(Orange))
        {
            g.FillPie(orange, c.X - s * 0.34f, c.Y - s * 0.34f, s * 0.68f, s * 0.68f, -78, 46);
        }

        using (var cream = new SolidBrush(Cream))
        using (var pen = new Pen(cream, Math.Max(2f, s * 0.055f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            g.DrawArc(pen, c.X - glyphRadius, c.Y - glyphRadius, glyphRadius * 2, glyphRadius * 2, -48, 276);
            g.DrawLine(pen, c.X, c.Y - glyphRadius * 0.06f, c.X, c.Y - s * 0.40f);
        }
    }

    private static void DrawSplash(Graphics g, int w, int h)
    {
        using (var cream = new SolidBrush(Cream))
        {
            g.FillRectangle(cream, 0, 0, w, h);
        }

        const int markSize = 110;
        using (var mark = new Bitmap(markSize, markSize))
        {
            using (var markG = Graphics.FromImage(mark))
            {
                AppIcon.DrawMark(markG, markSize);
            }
            g.DrawImage(mark, (w - markSize) / 2f, h / 2f - markSize, markSize, markSize);
        }

        TextCentered(g, "AutoShutdown", w, h / 2f + 42, DisplayFont(30), Ink);
        TextCentered(g, "Count down to shutdown · restart · sleep · hibernate · lock", w, h / 2f + 84, BodyFont(14), Muted);
    }

    /// <summary>
    /// Store screenshot frame: cream stage, floating window with a soft shadow,
    /// brand eyebrow and a big heading above.
    /// </summary>
    private static void DrawComposition(Graphics g, int w, int h, Image window, string caption, bool oversized)
    {
        using (var cream = new SolidBrush(Cream))
        {
            g.FillRectangle(cream, 0, 0, w, h);
        }

        // Faint oversized dial echoing the brand, bottom-right.
        var ghostRadius = w * 0.16f;
        using (var ghost = new Pen(Color.FromArgb(26, Teal), h * 0.03f))
        {
            g.DrawEllipse(ghost, w - ghostRadius * 1.2f, h - ghostRadius * 0.4f - h * 0.16f, ghostRadius * 2, ghostRadius * 2);
        }

        var titlePx = oversized ? 84 : 60;
        var eyebrowPx = oversized ? 16 : 13;
        var headingBand = oversized ? 170 : 120;
        var margin = (int)(h * 0.04f);

        var maxWidth = w - margin * 2;
        var maxHeight = h - headingBand - margin * 2;

        var scale = Math.Min(maxWidth / window.Width, (float)maxHeight / window.Height);
        var destW = (int)(window.Width * scale);
        var destH = (int)(window.Height * scale);
        var destX = (w - destW) / 2;
        var destY = headingBand + (maxHeight - destH) / 2;

        DrawShadowed(g, window, destX, destY, destW, destH);

        var heading = caption switch
        {
            "setup" => "Set a countdown, walk away",
            "countdown" => "The pie keeps the time",
            "finish" => "When time's up, it's up",
            _ => "AutoShutdown",
        };

        TextCentered(g, "✤  AUTOSHUTDOWN", w, eyebrowPx + 18, BodyFont(eyebrowPx), Teal);
        TextCentered(g, heading, w, headingBand / 2f + 24, DisplayFont(titlePx), Ink);
    }

    private static void DrawHero(Graphics g, int w, int h, Image window)
    {
        using (var cream = new SolidBrush(Cream))
        {
            g.FillRectangle(cream, 0, 0, w, h);
        }

        // Soft brand ring, top-left.
        using (var ghost = new Pen(Color.FromArgb(24, Teal), 26))
        {
            g.DrawEllipse(ghost, -180, -160, 640, 640);
        }

        const int markSize = 96;
        using (var mark = new Bitmap(markSize, markSize))
        {
            using (var markG = Graphics.FromImage(mark))
            {
                AppIcon.DrawMark(markG, markSize);
            }
            g.DrawImage(mark, 152, 150, markSize, markSize);
        }

        using var ink = new SolidBrush(Ink);
        using var teal = new SolidBrush(Teal);
        using var muted = new SolidBrush(Muted);

        g.DrawString("AUTOSHUTDOWN", BodyFont(20), teal, 264, 190);

        using (var title = DisplayFont(104))
        {
            g.DrawString("Set it once.", title, ink, 152, 280);
            g.DrawString("Walk away.", title, ink, 152, 400);
        }

        g.DrawString(
            "Pick a duration, choose what happens when it hits zero \u2014 the pie counts down,\nthe action fires on time, every time.",
            BodyFont(26), muted, 154, 540);

        // Window on the right with a soft shadow.
        var scale = Math.Min(0.62f * w / window.Width, 0.82f * h / window.Height);
        var destW = (int)(window.Width * scale);
        var destH = (int)(window.Height * scale);
        DrawShadowed(g, window, w - destW - 130, (h - destH) / 2, destW, destH);
    }

    private static void TextCentered(Graphics g, string text, float canvasWidth, float y, Font font, Color color)
    {
        using var brush = new SolidBrush(color);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (canvasWidth - size.Width) / 2f, y - size.Height / 2f);
    }

    /// <summary>Layered translucent rounds fake a soft shadow (flat design, no blurs).</summary>
    private static void DrawShadowed(Graphics g, Image image, int x, int y, int width, int height)
    {
        for (var grow = 15; grow >= 1; grow -= 3)
        {
            using var shadow = new SolidBrush(Color.FromArgb(10, 30, 38, 41));
            var rect = new Rectangle(x - grow + 6, y - grow + 12, width + grow * 2, height + grow * 2);
            using var path = new GraphicsPath();
            var radius = 18;
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            g.FillPath(shadow, path);
        }

        g.DrawImage(image, new Rectangle(x, y, width, height));
    }
}
