// iOS Secure Enclave implementation — placeholder.
// Will be implemented in Phase C when a Mac build agent is available.
// The build is gated by the net10.0-ios TFM which is not yet active.

using Microsoft.IdentityModel.Tokens;
using MasterSTI.Wallet.Services;

namespace MasterSTI.Wallet.Platforms.iOS.Services;

internal sealed class iOSDeviceKeyService : IDeviceKeyService
{
    public Task<JsonWebKey> GenerateOrLoadPublicJwkAsync() =>
        throw new PlatformNotSupportedException("iOS implementation not yet available.");

    public Task<byte[]> SignAsync(byte[] data) =>
        throw new PlatformNotSupportedException("iOS implementation not yet available.");

    public Task<string> GetKeyIdAsync() =>
        throw new PlatformNotSupportedException("iOS implementation not yet available.");

    public Task<bool> IsHardwareBackedAsync() =>
        throw new PlatformNotSupportedException("iOS implementation not yet available.");
}
