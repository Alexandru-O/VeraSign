using System.Security.Cryptography;

namespace MasterSTI.Api.Common;

public static class HashingService
{
    public static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
