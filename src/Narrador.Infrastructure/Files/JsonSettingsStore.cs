using System.Text.Json;
using System.Text.Json.Nodes;
using Narrador.Application;

namespace Narrador.Infrastructure.Files;

public sealed class JsonSettingsStore(string directory) : ISettingsStore
{
    private readonly SettingsDocument _document = new(directory);

    public AppSettings Load() => _document.Read().Deserialize<AppSettings>() ?? new AppSettings();

    public void Save(AppSettings settings)
    {
        // Keep encrypted keys and other providers' settings when updating these preferences.
        var document = _document.Read();
        var preferences = JsonSerializer.SerializeToNode(settings)!.AsObject();
        foreach (var property in preferences) document[property.Key] = property.Value?.DeepClone();
        _document.Write(document);
    }
}
