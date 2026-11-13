using System.Text.Json;
using Narrador.Application;

namespace Narrador.Infrastructure.Files;

public sealed class JsonSettingsStore(string directory) : ISettingsStore
{
    private readonly string _path = Path.Combine(directory, "settings.json");

    public AppSettings Load() => File.Exists(_path)
        ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings()
        : new AppSettings();

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
