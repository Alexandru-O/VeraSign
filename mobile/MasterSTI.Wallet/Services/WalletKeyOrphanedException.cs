namespace MasterSTI.Wallet.Services;

/// <summary>
/// Thrown when the AndroidKeyStore alias exists but the private key cannot be
/// loaded — typically because the OS evicted the key material across an
/// uninstall/reinstall while the alias entry survived. Recovery: wipe the
/// stored SD-JWT and force the wallet back through Onboarding so a fresh
/// device key + key-bound PID can be issued.
/// </summary>
public sealed class WalletKeyOrphanedException : InvalidOperationException
{
    public WalletKeyOrphanedException(string message) : base(message) { }
}
