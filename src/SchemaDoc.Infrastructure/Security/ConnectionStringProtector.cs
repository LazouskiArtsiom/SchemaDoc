using System.Text;

namespace SchemaDoc.Infrastructure.Security;

/// <summary>
/// Encrypts/decrypts connection strings using DPAPI on Windows,
/// or Base64 (with a warning) on non-Windows platforms.
/// </summary>
public static class ConnectionStringProtector
{
    public static string Protect(string plainText)
    {
        if (OperatingSystem.IsWindows())
        {
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        // On non-Windows: store as base64 (security relies on file permissions)
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    }

    public static string Unprotect(string cipherText)
    {
        if (OperatingSystem.IsWindows())
        {
            var encrypted = Convert.FromBase64String(cipherText);
            var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                encrypted, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
    }
}
