using System.IO;
using System.Text.Json;
using Sunshine.Models;

namespace Sunshine.Services;

public sealed class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sunshine", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LaunchProfile Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<LaunchProfile>(json);
                if (loaded != null)
                    return loaded;
            }
        }
        catch
        {
            // Corrupt or unreadable settings file - fall back to defaults.
        }
        return new LaunchProfile();
    }

    public void Save(LaunchProfile profile)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(profile, JsonOptions));
    }
}
