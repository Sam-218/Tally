using System.Text.Json;

namespace Tally.Services;

/// <summary>
/// Small app settings (data folder, theme, window size).
/// Deliberately NOT stored in the data folder, but under %LOCALAPPDATA%\Tally\settings.json,
/// because the program needs to know where the data folder is before it can even find it.
/// </summary>
public sealed class AppSettings
{
    public string? DataFolder { get; set; }
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "de";
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string SettingsDirectory { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tally");

    public static string SettingsPath => System.IO.Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>Default storage location: Documents\Tally</summary>
    public static string DefaultDataFolder()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(docs)
            ? FallbackDataFolder()
            : System.IO.Path.Combine(docs, "Tally");
    }

    /// <summary>Emergency location if Documents isn't writable.</summary>
    public static string FallbackDataFolder() => System.IO.Path.Combine(SettingsDirectory, "Daten");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Write("settings.json could not be read – using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            FileUtil.WriteAtomic(SettingsPath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Log.Write("settings.json could not be saved", ex);
        }
    }
}
