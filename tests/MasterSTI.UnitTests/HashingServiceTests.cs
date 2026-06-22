using MasterSTI.Api.Common;

namespace MasterSTI.UnitTests;

public class HashingServiceTests
{
    [Fact]
    public void ComputeSha256_EmptyArray_ReturnsKnownHash()
    {
        // SHA-256 of empty byte array is a well-known value
        var result = HashingService.ComputeSha256(Array.Empty<byte>());
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", result);
    }

    [Fact]
    public void ComputeSha256_KnownInput_MatchesSystemCrypto()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("abc");
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var result = HashingService.ComputeSha256(bytes);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeSha256_ReturnsLowercase()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var result = HashingService.ComputeSha256(bytes);
        Assert.Equal(result, result.ToLowerInvariant());
    }

    [Fact]
    public void ComputeSha256_Returns64CharHexString()
    {
        var bytes = new byte[] { 0xFF, 0x00, 0xAB };
        var result = HashingService.ComputeSha256(bytes);
        Assert.Equal(64, result.Length);
        Assert.All(result, c => Assert.Contains(c, "0123456789abcdef"));
    }
}
