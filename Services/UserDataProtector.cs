using System.Security.Cryptography;

namespace CodexInputEnhancer.Services;

public static class UserDataProtector
{
    public static byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
