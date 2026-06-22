using MasterSTI.Shared.DTOs.Auth;
using MasterSTI.Shared.DTOs.Wallet;

namespace MasterSTI.Api.Features.Wallet.Auth;

/// <summary>
/// In-memory cache entry for the QR-based wallet flow. The same entry shape
/// covers both the self-sign handoff (<see cref="WalletAuthPurpose.Sign"/>)
/// and password-less login (<see cref="WalletAuthPurpose.Login"/>) flows.
/// <see cref="Login"/> is populated only when a login flow completes.
/// </summary>
public sealed record WalletAuthEntry(
    string State,
    string Nonce,
    Guid SigningRequestId,
    DateTime ExpiresAtUtc,
    string Status,
    string? Subject,
    DateTime? CompletedAtUtc,
    WalletAuthPurpose Purpose = WalletAuthPurpose.Sign,
    LoginResponse? Login = null);

public static class WalletAuthCacheKeys
{
    public const int TtlSeconds = 5 * 60;
    public static string ForState(string state) => $"wallet-auth:{state}";

    /// <summary>
    /// Short-lived completion marker written after the active <see cref="ForState"/>
    /// entry is evicted on Login success. The polling endpoint falls back to this
    /// key so the client can still pick up the <see cref="LoginResponse"/> after
    /// replay-eviction wipes the live entry. TTL is 30 s — long enough for the
    /// 1.5 s polling client, short enough to bound retention of bearer tokens
    /// in memory.
    /// </summary>
    public const int CompletionTtlSeconds = 30;
    public static string ForCompletion(string state) => $"wallet-auth-completed:{state}";
}
