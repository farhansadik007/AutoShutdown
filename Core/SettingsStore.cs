using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoShutdown.Core;

public static class SettingsStore
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoShutdown");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Write("Could not read settings, using defaults: " + ex.Message);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            // Write-then-replace so a crash mid-save can't truncate the file.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write("Could not save settings: " + ex.Message);
        }
    }
}
