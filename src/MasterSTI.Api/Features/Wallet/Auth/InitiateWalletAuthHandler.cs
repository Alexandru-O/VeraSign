using System.Security.Cryptography;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Shared.DTOs.Wallet;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace MasterSTI.Api.Features.Wallet.Auth;

public sealed class InitiateWalletAuthHandler : IRequestHandler<InitiateWalletAuthCommand, InitiateWalletAuthResponse>
{
    private readonly OpenId4VpService _openId4Vp;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InitiateWalletAuthHandler> _logger;

    public InitiateWalletAuthHandler(
        OpenId4VpService openId4Vp,
        IMemoryCache cache,
        ILogger<InitiateWalletAuthHandler> logger)
    {
        _openId4Vp = openId4Vp;
        _cache = cache;
        _logger = logger;
    }

    public Task<InitiateWalletAuthResponse> Handle(InitiateWalletAuthCommand request, CancellationToken cancellationToken)
    {
        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var nonce = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

        // Both purposes ask for the same PID claims today (family_name, given_name, birth_date),
        // so the existing OID4VP authorization request fits both. Login simply derives the
        // user identity from the disclosed claims after KB-JWT verification succeeds.
        var authReq = _openId4Vp.CreateAuthorizationRequest(nonce, state);
        // Login flow uses request_uri indirection so the QR stays small (~120 bytes).
        // Sign flow keeps the inline payload because the existing MAUI wallet parses params directly from the QR.
        var qrPayload = request.Purpose == WalletAuthPurpose.Login
            ? _openId4Vp.BuildQrPayloadShort(state)
            : _openId4Vp.BuildQrPayload(authReq);

        var ttl = TimeSpan.FromSeconds(WalletAuthCacheKeys.TtlSeconds);
        var entry = new WalletAuthEntry(
            State: state,
            Nonce: nonce,
            SigningRequestId: Guid.Empty,
            ExpiresAtUtc: DateTime.UtcNow.Add(ttl),
            Status: "pending",
            Subject: null,
            CompletedAtUtc: null,
            Purpose: request.Purpose,
            Login: null);

        _cache.Set(WalletAuthCacheKeys.ForState(state), entry, ttl);

        _logger.LogInformation("Wallet auth initiated state={State} purpose={Purpose}", state, request.Purpose);

        return Task.FromResult(new InitiateWalletAuthResponse(
            state,
            authReq.ResponseUri,
            qrPayload,
            WalletAuthCacheKeys.TtlSeconds,
            nonce));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
