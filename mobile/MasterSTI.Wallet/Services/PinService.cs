using System.Security.Cryptography;
using System.Text;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Wallet unlock PIN. The PIN is the AppLock fallback when biometric auth is
/// unavailable or declined. Only a salted hash is persisted in
/// <see cref="SecureStorage"/> — the plaintext PIN is never stored or logged.
/// </summary>
public interface IPinService
{
    /// <summary>Minimum PIN length (matches the demo 6-digit convention).</summary>
    int MinLength { get; }

    /// <summary>True once a PIN hash has been stored (i.e. PIN setup completed).</summary>
    Task<bool> HasPinAsync();

    /// <summary>Hashes and stores the PIN. Overwrites any existing PIN.</summary>
    Task SetPinAsync(string pin);

    /// <summary>Constant-time-ish compare of <paramref name="pin"/> against the stored hash.</summary>
    Task<bool> VerifyAsync(string pin);
}

public sealed class PinService : IPinService
{
    private const string HashKey = "wallet.pin.hash";
    private const string SaltKey = "wallet.pin.salt";
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public int MinLength => 6;

    public async Task<bool> HasPinAsync()
    {
        try
        {
            var hash = await SecureStorage.GetAsync(HashKey);
            return !string.IsNullOrEmpty(hash);
        }
        catch
        {
            return false;
        }
    }

    public async Task SetPinAsync(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        // PBKDF2 100k iterations is ~5s on x86 emulator Debug build — push off the
        // UI thread to avoid Android ANR (5s input-dispatcher timeout).
        var hash = await Task.Run(() => Derive(pin, salt)).ConfigureAwait(false);
        await SecureStorage.SetAsync(SaltKey, Convert.ToBase64String(salt));
        await SecureStorage.SetAsync(HashKey, Convert.ToBase64String(hash));
    }

    public async Task<bool> VerifyAsync(string pin)
    {
        try
        {
            var saltB64 = await SecureStorage.GetAsync(SaltKey);
            var hashB64 = await SecureStorage.GetAsync(HashKey);
            if (string.IsNullOrEmpty(saltB64) || string.IsNullOrEmpty(hashB64))
                return false;

            var salt = Convert.FromBase64String(saltB64);
            var stored = Convert.FromBase64String(hashB64);
            var candidate = await Task.Run(() => Derive(pin, salt)).ConfigureAwait(false);
            return CryptographicOperations.FixedTimeEquals(candidate, stored);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
}
