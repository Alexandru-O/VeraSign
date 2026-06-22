using System.Text.Json.Serialization;
using MasterSTI.Shared.DTOs.Auth;

namespace MasterSTI.Shared.DTOs.Wallet;

/// <summary>
/// Discriminator for the QR-based wallet flow. <see cref="Sign"/> is the legacy
/// self-sign handoff (existing callers default to this). <see cref="Login"/>
/// drives the password-less authentication flow on the login screen.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WalletAuthPurpose>))]
public enum WalletAuthPurpose
{
    Sign = 0,
    Login = 1
}

/// <summary>
/// Optional body for <c>POST /api/wallet/auth</c>. When omitted or null,
/// purpose defaults to <see cref="WalletAuthPurpose.Sign"/> (backward compat).
/// </summary>
public record WalletAuthInitRequest(WalletAuthPurpose Purpose = WalletAuthPurpose.Sign);

public record InitiateWalletAuthResponse(
    string State,
    string RequestUri,
    string QrCode,
    int ExpiresInSeconds,
    string Nonce);

/// <summary>
/// Polled by the web UI. <see cref="Login"/> is populated only when the
/// wallet completed a login flow successfully (token + user info ready to adopt).
/// For sign-flow consumers, <see cref="Login"/> stays null.
/// </summary>
public record WalletAuthStatusResponse(
    string Status,
    string? Subject,
    DateTime? CompletedAt,
    LoginResponse? Login = null);
