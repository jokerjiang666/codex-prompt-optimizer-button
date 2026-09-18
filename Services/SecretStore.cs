using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexInputEnhancer.Services;

public sealed class SecretStore
{
    private readonly string _path;

    public SecretStore(SettingsStore settingsStore)
    {
        _path = Path.Combine(settingsStore.DataDirectory, "secret.dat");
    }

    public string Read()
    {
        if (!File.Exists(_path)) return string.Empty;
        try
        {
            var encrypted = File.ReadAllBytes(_path);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch { return string.Empty; }
    }

    public void Write(string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (File.Exists(_path)) File.Delete(_path);
            return;
        }

        var plain = Encoding.UTF8.GetBytes(value);
        try
        {
            File.WriteAllBytes(_path, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
