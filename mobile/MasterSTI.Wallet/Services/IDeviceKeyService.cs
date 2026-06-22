using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Manages the device-bound EC P-256 signing key stored in the platform secure enclave
/// (AndroidKeyStore on Android, Secure Enclave on iOS).
/// </summary>
public interface IDeviceKeyService
{
    /// <summary>Returns the EC P-256 public key as a JWK. Generates the key pair on first call.</summary>
    Task<JsonWebKey> GenerateOrLoadPublicJwkAsync();

    /// <summary>
    /// Signs <paramref name="data"/> with ES256. Returns the raw r||s (64 bytes) JOSE format,
    /// NOT a DER-encoded signature.
    /// </summary>
    Task<byte[]> SignAsync(byte[] data);

    /// <summary>Returns a stable key identifier (JWK SHA-256 thumbprint, base64url).</summary>
    Task<string> GetKeyIdAsync();

    /// <summary>
    /// Returns <see langword="true"/> if the key is backed by hardware security
    /// (StrongBox on Android, Secure Enclave on iOS).
    /// </summary>
    Task<bool> IsHardwareBackedAsync();
}
