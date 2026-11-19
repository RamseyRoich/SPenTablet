using System.Text.Json;
using System.Text.Json.Serialization;
using SPenTablet.Host.Mapping;

namespace SPenTablet.Host;

public sealed class AppSettings
{
    public int MonitorIndex { get; set; }
    public float Gamma { get; set; } = 1.0f;
    public float MinPressure { get; set; } = 0.02f;
    public bool KeepAspect { get; set; } = true;
    public string? AdbPath { get; set; }

    [JsonIgnore] public PressureCurve Curve => new(Gamma, MinPressure);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SPenTablet", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); } // missing or corrupt file: use defaults
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
