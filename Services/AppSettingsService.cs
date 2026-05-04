using System.IO;
using System.Text.Json;
using Supertech.AutoUploadVideo.Models;

namespace Supertech.AutoUploadVideo.Services;

public sealed class AppSettingsService
{
    private readonly string _baseDir;
    private readonly string _settingsPath;

    public AppSettingsService()
    {
        _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Supertech", "AutoUploadVideo");
        Directory.CreateDirectory(_baseDir);
        _settingsPath = Path.Combine(_baseDir, "settings.json");
    }

    public string DataDirectory => _baseDir;
    public string DatabasePath => Path.Combine(_baseDir, "queue.db");
    public string ResumeDirectory
    {
        get
        {
            var path = Path.Combine(_baseDir, "resume");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
