using System.Text.Json;

namespace WinterTheme;

public sealed class Settings
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "WinterTheme");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Current { get; } = Load();

    /// <summary>0 = light, 1 = normal, 2 = blizzard.</summary>
    public int Intensity { get; set; } = 1;
    public bool SnowOnWindows { get; set; } = true;
    public bool Garland { get; set; } = true;
    public bool CursorPush { get; set; } = true;
    public bool Melt { get; set; }
    public bool RandomGrinch { get; set; } = true;
    public bool RandomSanta { get; set; } = true;
    public bool AutoTractor { get; set; } = true;
    public bool ShowInScreenshots { get; set; }
    public bool Paused { get; set; }
    /// <summary>Old "smooth" switch; kept so that existing settings files load as 60 fps.</summary>
    public bool Smooth60 { get; set; }

    /// <summary>Frames per second: 20 (saves battery), 30 or 60.</summary>
    public int Fps { get; set; } = 30;

    public int FrameRate => Smooth60 ? 60 : Fps is 20 or 30 or 60 ? Fps : 30;
    public bool NewYearShow { get; set; } = true;
    public bool ShootingStars { get; set; } = true;
    public bool Chatter { get; set; } = true;
    public bool LinkScreens { get; set; } = true;
    public bool Icicles { get; set; } = true;
    public bool Birds { get; set; } = true;
    public bool Aurora { get; set; } = true;

    public float FlakeFactor => Intensity switch { 0 => 0.45f, 2 => 2.6f, _ => 1f };

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // broken file: start with defaults
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // non-critical
        }
    }
}
