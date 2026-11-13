using System.Security.Cryptography;
using System.Text;
using Narrador.Application;

namespace Narrador.Infrastructure.Security;

public sealed class WindowsSecretStore(string directory) : ISecretStore
{
    public string? ReadApiKey(string providerId)
    {
        var path = GetPath(providerId);
        if (!File.Exists(path)) return null;
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void SaveApiKey(string providerId, string? apiKey)
    {
        var path = GetPath(providerId);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(directory);
        var clear = Encoding.UTF8.GetBytes(apiKey);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string GetPath(string providerId)
    {
        if (string.IsNullOrEmpty(providerId) || providerId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Identificador de proveedor no válido.", nameof(providerId));
        return Path.Combine(directory, providerId + ".key");
    }
}
