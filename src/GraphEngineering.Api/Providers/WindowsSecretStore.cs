using System.Security.Cryptography;
using System.Text;

namespace GraphEngineering.Api.Providers;

public interface ISecretStore
{
    byte[] Protect(string plaintext);
    string Unprotect(byte[] ciphertext);
}

public sealed class SecretStoreException : Exception
{
    public SecretStoreException() : base("Windows could not access this credential. Replace it under the current Windows account.") { }
}

public sealed class WindowsSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GraphEngineering/provider-credential/v1");

    public byte[] Protect(string plaintext)
    {
        if (!OperatingSystem.IsWindows()) throw new SecretStoreException();
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try { return ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser); }
        catch (Exception error) when (error is CryptographicException or ArgumentException) { throw new SecretStoreException(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public string Unprotect(byte[] ciphertext)
    {
        if (!OperatingSystem.IsWindows()) throw new SecretStoreException();
        byte[]? bytes = null;
        try
        {
            bytes = ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException) { throw new SecretStoreException(); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }
}
