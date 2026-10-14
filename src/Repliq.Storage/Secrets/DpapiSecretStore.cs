using System.Security.Cryptography;
using System.Text;

namespace Repliq.Storage.Secrets;

public interface ISecretStore
{
    Task SetSecretAsync(string key, string secret);
    Task<string?> GetSecretAsync(string key);
    Task DeleteSecretAsync(string key);
    Task<bool> HasSecretAsync(string key);
}

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _secretsDir;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Repliq.DPAPI.ProtectedStore.v1");

    public DpapiSecretStore(string? customDir = null)
    {
        _secretsDir = customDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Repliq",
            "Vault");

        Directory.CreateDirectory(_secretsDir);
    }

    public async Task SetSecretAsync(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        var cleanKey = SanitizeKey(key);
        var path = Path.Combine(_secretsDir, $"{cleanKey}.enc");

        byte[] plainBytes = Encoding.UTF8.GetBytes(secret ?? string.Empty);
        try
        {
            byte[] encryptedBytes = ProtectedData.Protect(
                plainBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            await File.WriteAllBytesAsync(path, encryptedBytes);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public async Task<string?> GetSecretAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var cleanKey = SanitizeKey(key);
        var path = Path.Combine(_secretsDir, $"{cleanKey}.enc");

        if (!File.Exists(path))
            return null;

        byte[] encryptedBytes = await File.ReadAllBytesAsync(path);
        byte[] decryptedBytes;
        try
        {
            decryptedBytes = ProtectedData.Unprotect(
                encryptedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (CryptographicException)
        {
            // Fail closed: if DPAPI decryption fails (e.g. key changed or file moved), return null
            return null;
        }
        finally
        {
            Array.Clear(encryptedBytes, 0, encryptedBytes.Length);
        }
    }

    public Task DeleteSecretAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Task.CompletedTask;

        var cleanKey = SanitizeKey(key);
        var path = Path.Combine(_secretsDir, $"{cleanKey}.enc");

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> HasSecretAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Task.FromResult(false);

        var cleanKey = SanitizeKey(key);
        var path = Path.Combine(_secretsDir, $"{cleanKey}.enc");

        return Task.FromResult(File.Exists(path));
    }

    private static string SanitizeKey(string key)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in key)
        {
            if (invalid.Contains(c) || c == '.' || c == '/')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
