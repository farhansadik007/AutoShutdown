using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace AutoShutdown.Core;

/// <summary>
/// Writes an MSIX package (OPC container) straight from a package layout
/// folder - no Windows SDK required. Layout must contain AppxManifest.xml.
///   AutoShutdown.exe --pack-msix &lt;layoutDir&gt; &lt;output.msix&gt;
/// </summary>
public static class MsixPacker
{
    public static void Pack(string layoutRoot, string outputPath)
    {
        layoutRoot = Path.GetFullPath(layoutRoot);
        if (!File.Exists(Path.Combine(layoutRoot, "AppxManifest.xml")))
        {
            throw new FileNotFoundException("Layout has no AppxManifest.xml", Path.Combine(layoutRoot, "AppxManifest.xml"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        // [Content_Types].xml physically first (many OPC readers expect that).
        Write(archive, "[Content_Types].xml", Encoding.UTF8.GetBytes(BuildContentTypes(layoutRoot)));
        Write(archive, "_rels/.rels", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
            "  <Relationship Id=\"AppxManifest\" Type=\"http://schemas.microsoft.com/appx/2010/manifest\" Target=\"/AppxManifest.xml\" />\r\n" +
            "</Relationships>"));

        Write(archive, "AppxManifest.xml", File.ReadAllBytes(Path.Combine(layoutRoot, "AppxManifest.xml")));

        foreach (var relative in EnumeratePayload(layoutRoot))
        {
            Write(archive, relative, File.ReadAllBytes(Path.Combine(layoutRoot, relative)));
        }
    }

    private static void Write(ZipArchive archive, string name, byte[] data)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(data, 0, data.Length);
    }

    // ---------------------------------------------------------------- payload

    private static IEnumerable<string> EnumeratePayload(string root)
    {
        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(name => !name.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase))
            .Where(name => !name.StartsWith("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
            .Where(name => !name.StartsWith("publish/", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildContentTypes(string layoutRoot)
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["xml"] = "application/octet-stream",
            ["png"] = "image/png",
            ["ico"] = "image/x-icon",
            ["json"] = "application/json",
            ["txt"] = "text/plain",
            ["config"] = "application/octet-stream",
            ["exe"] = "application/octet-stream",
            ["dll"] = "application/octet-stream",
        };

        foreach (var file in EnumeratePayload(layoutRoot))
        {
            var ext = Path.GetExtension(file).TrimStart('.');
            if (ext.Length > 0 && !defaults.ContainsKey(ext))
            {
                defaults[ext] = "application/octet-stream";
            }
        }

        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        foreach (var pair in defaults)
        {
            sb.Append($"<Default Extension=\"{pair.Key}\" ContentType=\"{pair.Value}\" />");
        }

        sb.Append("<Override PartName=\"/AppxManifest.xml\" ContentType=\"application/vnd.ms-appx.manifest+xml\" />");
        sb.Append("</Types>");
        return sb.ToString();
    }
}
