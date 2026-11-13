using System.Text.Json;
using System.Text.Json.Nodes;

namespace Narrador.Infrastructure.Files;

internal sealed class SettingsDocument(string directory)
{
    private readonly string _path = Path.Combine(directory, "settings.json");

    public JsonObject Read() => File.Exists(_path)
        ? JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? throw new JsonException("La configuración no es un objeto JSON.")
        : new JsonObject();

    public void Write(JsonObject document)
    {
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
