using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;

namespace AutoShutdown.Core;

/// <summary>
/// The AutoShutdown mark: a teal clock dial with an eaten orange pie slice and
/// a cream power glyph. Drawn at runtime (no binary in the repo) and exportable
/// as a real multi-resolution .ico via --export-icon.
/// </summary>
public static class AppIcon
{
    private static readonly System.Drawing.Color DialColor = System.Drawing.Color.FromArgb(0x43, 0x76, 0x6C);
    private static readonly System.Drawing.Color WedgeAccent = System.Drawing.Color.FromArgb(0xFF, 0x98, 0x00);
    private static readonly System.Drawing.Color GlyphColor = System.Drawing.Color.FromArgb(0xF8, 0xFA, 0xE5);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static BitmapSource WindowIcon => ToBitmapSource(Create(64));

    private static BitmapSource ToBitmapSource(DrawingIcon icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        source.Freeze();
        return source;
    }

    private static DrawingIcon Create(int size)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            DrawMark(g, size);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = DrawingIcon.FromHandle(handle);
            return (DrawingIcon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    // ---------------------------------------------------------------- artwork

    internal static void DrawMark(Graphics g, int size)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        var s = (float)size;
        var c = s / 2f;
        var radius = s * 0.46f;

        // Teal dial.
        using (var dial = new SolidBrush(DialColor))
        {
            g.FillEllipse(dial, c - radius, c - radius, radius * 2, radius * 2);
        }

        // The countdown's first bite: an orange slice eaten clockwise from 12 o'clock.
        using (var wedge = new SolidBrush(WedgeAccent))
        {
            g.FillPie(wedge, c - radius, c - radius, radius * 2, radius * 2, -90, 50);
        }

        // Cream power glyph: ring with a top gap plus the vertical stem.
        var ringRadius = radius * 0.52f;
        var penWidth = Math.Max(1.0f, s * 0.07f);
        using (var cream = new SolidBrush(GlyphColor))
        using (var pen = new Pen(cream, penWidth))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            // Arc starts at 300° (-60°), sweeps 300° clockwise → gap at top (240°–300°).
            g.DrawArc(pen, c - ringRadius, c - ringRadius, ringRadius * 2, ringRadius * 2, -60, 300);
            // Stem from just above the ring centre up through the gap.
            g.DrawLine(pen, c, c - ringRadius * 0.25f, c, c - ringRadius * 1.35f);
        }
    }

    // ---------------------------------------------------------------- export

    /// <summary>
    /// Writes a full multi-resolution .ico (256→16) so the exe and shortcuts
    /// carry the same mark the taskbar shows.
    /// </summary>
    public static void ExportIcon(string path)
    {
        // Largest first; 256 is stored with a 0 dimension byte in the directory.
        int[] sizes = { 256, 128, 64, 48, 32, 24, 16 };

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        WriteU16(stream, 0);                   // reserved
        WriteU16(stream, 1);                   // type: icon
        WriteU16(stream, (ushort)sizes.Length);

        var frames = new byte[sizes.Length][];
        for (var i = 0; i < sizes.Length; i++)
        {
            frames[i] = EncodeDib(sizes[i]);
        }

        var offset = 6 + 16 * frames.Length;
        for (var i = 0; i < frames.Length; i++)
        {
            var size = sizes[i];
            stream.WriteByte((byte)(size >= 256 ? 0 : size));
            stream.WriteByte((byte)(size >= 256 ? 0 : size));
            stream.WriteByte(0);               // palette size
            stream.WriteByte(0);               // reserved
            WriteU16(stream, 1);               // colour planes
            WriteU16(stream, 32);              // bits per pixel
            WriteU32(stream, (uint)frames[i].Length);
            WriteU32(stream, (uint)offset);
            offset += frames[i].Length;
        }

        foreach (var frame in frames)
        {
            stream.Write(frame, 0, frame.Length);
        }
    }

    /// <summary>Packs 32bpp ARGB pixels as a classic ICO DIB entry (alpha keeps transparency).</summary>
    private static byte[] EncodeDib(int size)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            DrawMark(g, size);
        }

        var rect = new Rectangle(0, 0, size, size);
        var bits = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var xorRow = size * 4;
            var maskRow = ((size + 31) / 32) * 4;
            using var ms = new MemoryStream();
            WriteU32(ms, 40);                  // BITMAPINFOHEADER length
            WriteU32(ms, (uint)size);          // width
            WriteU32(ms, (uint)size * 2);      // height (XOR + mask)
            WriteU16(ms, 1);                   // planes
            WriteU16(ms, 32);                  // bpp
            WriteU32(ms, 0);                   // BI_RGB
            WriteU32(ms, (uint)(xorRow * size + maskRow * size));
            WriteU32(ms, 0);
            WriteU32(ms, 0);
            WriteU32(ms, 0);
            WriteU32(ms, 0);

            var row = new byte[xorRow];
            for (var y = size - 1; y >= 0; y--)
            {
                Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, xorRow);
                ms.Write(row, 0, xorRow);
            }

            ms.Write(new byte[maskRow * size], 0, maskRow * size); // AND mask: all transparent
            return ms.ToArray();
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
    }

    private static void WriteU32(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }
}
