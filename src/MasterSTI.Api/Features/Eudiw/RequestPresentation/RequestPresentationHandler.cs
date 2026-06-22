using System.Security.Cryptography;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Features.Eudiw.RequestPresentation;

public sealed class RequestPresentationHandler : IRequestHandler<RequestPresentationCommand, EudiwRequestResult>
{
    private readonly AppDbContext _db;
    private readonly OpenId4VpService _openId4Vp;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<EudiwOptions> _options;
    private readonly ILogger<RequestPresentationHandler> _logger;

    public RequestPresentationHandler(
        AppDbContext db,
        OpenId4VpService openId4Vp,
        IMemoryCache cache,
        IOptionsMonitor<EudiwOptions> options,
        ILogger<RequestPresentationHandler> logger)
    {
        _db = db;
        _openId4Vp = openId4Vp;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<EudiwRequestResult> Handle(RequestPresentationCommand request, CancellationToken cancellationToken)
    {
        var sigReq = await _db.SigningRequests.FirstOrDefaultAsync(s => s.Id == request.SigningRequestId, cancellationToken)
            ?? throw new KeyNotFoundException($"SigningRequest {request.SigningRequestId} not found");

        var nonce = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        // Randomise state (was derived from SigningRequestId — predictable). 128-bit base64url
        // gives ~22 chars × 6 bits ≈ 132 bits of entropy, well above the ≥96 bit bar required
        // for OID4VP replay protection (ARF §6.5.3).
        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

        var ttl = _options.CurrentValue.NonceCacheMinutes;
        _cache.Set(
            NonceCacheKeys.ForState(state),
            new EudiwStateEntry(nonce, request.SigningRequestId),
            TimeSpan.FromMinutes(ttl));

        var authReq = _openId4Vp.CreateAuthorizationRequest(nonce, state);
        var qrPayload = _openId4Vp.BuildQrPayload(authReq);

        _logger.LogInformation("EUDIW presentation requested for SigningRequest {Id} (TTL {Ttl} min)",
            request.SigningRequestId, ttl);

        return new EudiwRequestResult(request.SigningRequestId, nonce, qrPayload);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

public sealed record EudiwStateEntry(string Nonce, Guid SigningRequestId);

public static class NonceCacheKeys
{
    public static string ForState(string state) => $"eudiw:state:{state}";
}
