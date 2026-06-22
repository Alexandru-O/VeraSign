namespace MasterSTI.Wallet.Services;

/// <summary>
/// Process-wide AppLock flag. Set true while the wallet is locked (cold start
/// or after a background resume) so the resume hook does not stack a second
/// AppLock screen, and cleared by <c>AppLockPage</c> once unlocked.
/// </summary>
public static class AppLockState
{
    /// <summary>True while the AppLock screen is the active gate.</summary>
    public static bool IsLocked { get; set; }
}
