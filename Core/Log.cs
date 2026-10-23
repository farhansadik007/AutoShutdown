using System;
using System.IO;
using System.Text;

namespace AutoShutdown.Core;

/// <summary>
/// Append-only diagnostics. The app never talks to the network; this file is
/// the only way a crash gets explained.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoShutdown");

    private static readonly string Path_ = Path.Combine(Folder, "autoshutdown.log");
    private const long MaxBytes = 512 * 1024;

    public static string FilePath => Path_;

    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(Path_) && new FileInfo(Path_).Length > MaxBytes)
            {
                File.Delete(Path_);
            }
        }
        catch
        {
            // Diagnostics must never take the app down.
        }
    }

    public static void Write(string message)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append("  ")
                .Append(message)
                .AppendLine()
                .ToString();

            lock (Gate)
            {
                File.AppendAllText(Path_, line);
            }
        }
        catch
        {
            // Ignored on purpose.
        }
    }
}
