using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Narrador.Application;
using Narrador.Infrastructure.Files;

namespace Narrador.Infrastructure.Security;

public sealed class WindowsSecretStore(string directory) : ISecretStore
{
    private readonly SettingsDocument _document = new(directory);

    public string? ReadApiKey(string providerId)
    {
        var legacyPath = GetLegacyPath(providerId);
        var document = _document.Read();
        if (document["ProtectedApiKeys"] is not null and not JsonObject)
            throw new JsonException("El almacén de claves no es válido.");
        byte[] encrypted;
        if (document["ProtectedApiKeys"] is JsonObject keys && keys.TryGetPropertyValue(providerId, out var value))
        {
            if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var encoded))
                throw new JsonException("La clave cifrada no es válida.");
            try { encrypted = Convert.FromBase64String(encoded); }
            catch (FormatException ex) { throw new CryptographicException("La clave cifrada no es válida.", ex); }
        }
        else if (File.Exists(legacyPath)) encrypted = File.ReadAllBytes(legacyPath);
        else return null;
        var clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void SaveApiKey(string providerId, string? apiKey)
    {
        var legacyPath = GetLegacyPath(providerId);
        var document = _document.Read();
        if (document["ProtectedApiKeys"] is not null and not JsonObject)
            throw new JsonException("El almacén de claves no es válido.");
        var keys = document["ProtectedApiKeys"] as JsonObject ?? new JsonObject();
        if (string.IsNullOrWhiteSpace(apiKey)) keys.Remove(providerId);
        else
        {
            var clear = Encoding.UTF8.GetBytes(apiKey);
            try { keys[providerId] = Convert.ToBase64String(ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        if (keys.Parent is null) document["ProtectedApiKeys"] = keys;
        _document.Write(document);
        // Migrate after the new document is safely written, so existing keys keep working.
        if (File.Exists(legacyPath)) File.Delete(legacyPath);
    }

    private string GetLegacyPath(string providerId)
    {
        if (string.IsNullOrEmpty(providerId) || providerId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Identificador de proveedor no válido.", nameof(providerId));
        return Path.Combine(directory, providerId + ".key");
    }
}
