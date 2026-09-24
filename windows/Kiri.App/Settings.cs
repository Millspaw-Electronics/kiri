using System.Text.Json;

namespace Kiri.App;

/// <summary>User settings, kept in %LOCALAPPDATA%\kiri\settings.json.</summary>
public sealed class Settings
{
    private const int MaxRecent = 10;

    public List<string> RecentProjects { get; set; } = new();
    public string? GitPath { get; set; }
    public string? KiCadCliPath { get; set; }
    public bool Maximized { get; set; }

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kiri");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Start over with defaults
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
            // Settings are a convenience; don't fail over them
        }
    }

    public void AddRecent(string projectFile)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, projectFile, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, projectFile);
        if (RecentProjects.Count > MaxRecent)
            RecentProjects.RemoveRange(MaxRecent, RecentProjects.Count - MaxRecent);
        Save();
    }

    public void RemoveRecent(string projectFile)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, projectFile, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
