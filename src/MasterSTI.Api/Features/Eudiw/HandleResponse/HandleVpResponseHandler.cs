using System.Text.Json;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Eudiw.RequestPresentation;
using MasterSTI.Api.Features.Wallet.Auth;
using MasterSTI.Shared.DTOs.Auth;
using MasterSTI.Shared.DTOs.Wallet;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Features.Eudiw.HandleResponse;

public sealed class HandleVpResponseHandler : IRequestHandler<HandleVpResponseCommand, HandleVpResponseResult>
{
    // Verifier-side selective-disclosure allowlists. The wallet may carry more claims than the
    // verifier asked for; we reject anything outside the allowlist to honour GDPR data
    // minimisation (SD-JWT VC §6.2). Birth date is never needed for either flow.
    private static readonly HashSet<string> LoginDisclosureAllowlist = new(StringComparer.Ordinal)
    {
        "family_name", "given_name", "email"
    };

    private static readonly HashSet<string> SignDisclosureAllowlist = new(StringComparer.Ordinal)
    {
        "family_name", "given_name", "email"
    };

    private readonly AppDbContext _db;
    private readonly SdJwtValidator _sdJwtValidator;
    private readonly IJwtTokenService _tokens;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<EudiwOptions> _options;
    private readonly ILogger<HandleVpResponseHandler> _logger;
    private readonly IAuditWriter? _audit;

    public HandleVpResponseHandler(
        AppDbContext db,
        SdJwtValidator sdJwtValidator,
        IJwtTokenService tokens,
        IMemoryCache cache,
        IOptionsMonitor<EudiwOptions> options,
        ILogger<HandleVpResponseHandler> logger,
        IAuditWriter? audit = null)
    {
        _db = db;
        _sdJwtValidator = sdJwtValidator;
        _tokens = tokens;
        _cache = cache;
        _options = options;
        _logger = logger;
        _audit = audit;
    }

    public async Task<HandleVpResponseResult> Handle(HandleVpResponseCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.State))
            return new HandleVpResponseResult(false, null, "Missing state parameter");

        // --- Login flow: state keyed in WalletAuthCache ---
        var walletKey = WalletAuthCacheKeys.ForState(request.State);
        if (_cache.TryGetValue(walletKey, out WalletAuthEntry? walletEntry) && walletEntry is not null
            && walletEntry.Purpose == WalletAuthPurpose.Login)
        {
            if (DateTime.UtcNow > walletEntry.ExpiresAtUtc)
                return new HandleVpResponseResult(false, null, "State expired");

            var expectedAudLogin = !string.IsNullOrWhiteSpace(_options.CurrentValue.PublicBaseUrl)
                ? _options.CurrentValue.PublicBaseUrl!.TrimEnd('/')
                : _options.CurrentValue.VerifierId;
            var loginClaims = _sdJwtValidator.ValidateAndExtract(
                request.VpToken,
                walletEntry.Nonce,
                expectedAudLogin,
                LoginDisclosureAllowlist);
            if (loginClaims is null)
            {
                // Mark failed in cache so poller can surface the error
                var failed = walletEntry with { Status = "failed" };
                _cache.Set(walletKey, failed, walletEntry.ExpiresAtUtc - DateTime.UtcNow);
                return new HandleVpResponseResult(false, null, "SD-JWT validation failed");
            }

            // Derive email: prefer disclosed "email" claim, else fall back to sub
            var email = loginClaims.Email ?? loginClaims.Subject ?? string.Empty;

            // Find or create user. Two concurrent first-logins for the same PID
            // email would otherwise both pass the check and both INSERT — the
            // unique index on Users.Email lets us recover by re-reading the
            // winner row when the loser's SaveChanges throws DbUpdateException.
            var emailLower = email.ToLowerInvariant();
            var loginUser = await _db.Users
                .Include(u => u.Organization)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower, cancellationToken);

            if (loginUser is null)
            {
                var displayName = $"{loginClaims.GivenName} {loginClaims.FamilyName}".Trim();
                if (string.IsNullOrWhiteSpace(displayName)) displayName = email;

                loginUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = emailLower,
                    Name = displayName,
                    Role = "User",
                    OrganizationId = DbInitializer.SeedOrganizationId,
                    PasswordHash = string.Empty,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Users.Add(loginUser);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Lost the race — detach the duplicate we tried to insert and
                    // adopt the winning row keyed by email.
                    _db.Entry(loginUser).State = EntityState.Detached;
                    var winner = await _db.Users
                        .Include(u => u.Organization)
                        .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower, cancellationToken);
                    if (winner is null) throw;
                    loginUser = winner;
                }

                // Reload with org for the create-path winner (loser already reloaded above).
                if (loginUser.Organization is null)
                    loginUser = await _db.Users
                        .Include(u => u.Organization)
                        .FirstAsync(u => u.Id == loginUser.Id, cancellationToken);
            }

            var loginIsNewThumbprint = await UpsertWalletEnrollmentAsync(loginUser.Id, loginClaims, cancellationToken);

            var (token, expiresAt) = _tokens.Issue(loginUser);
            var loginResponse = new LoginResponse(
                token,
                expiresAt,
                new UserInfo(
                    loginUser.Id,
                    loginUser.Email,
                    loginUser.Name,
                    loginUser.OrganizationId,
                    loginUser.Organization?.Name ?? string.Empty,
                    loginUser.Role));

            var completed = walletEntry with
            {
                Status = "complete",
                Subject = $"{loginClaims.GivenName} {loginClaims.FamilyName}".Trim(),
                CompletedAtUtc = DateTime.UtcNow,
                Login = loginResponse
            };
            // Replay eviction: drop the live entry so a second VP submission for the same
            // state cannot be authorised. The polling client still needs the LoginResponse,
            // so we write a short-lived completion marker under a separate key — see
            // WalletAuthCacheKeys.ForCompletion / WalletAuthEndpoint fall-through.
            _cache.Remove(walletKey);
            _cache.Set(
                WalletAuthCacheKeys.ForCompletion(request.State),
                completed,
                TimeSpan.FromSeconds(WalletAuthCacheKeys.CompletionTtlSeconds));

            _logger.LogInformation("Wallet login successful for subject={Subject}", completed.Subject);

            if (_audit is not null)
            {
                if (loginIsNewThumbprint)
                {
                    var enrollMeta =
                        $"{{\"userId\":\"{loginUser.Id}\"," +
                        $"\"email\":{JsonSerializer.Serialize(loginUser.Email)}," +
                        $"\"thumbprint\":{JsonSerializer.Serialize(loginClaims.CnfJwkThumbprint)}}}";
                    await _audit.WriteAsync(null, "WalletEnrolled", enrollMeta, cancellationToken);
                }

                var loginMeta =
                    $"{{\"userId\":\"{loginUser.Id}\"," +
                    $"\"email\":{JsonSerializer.Serialize(loginUser.Email)}}}";
                await _audit.WriteAsync(null, "WalletLogin", loginMeta, cancellationToken);
            }

            return new HandleVpResponseResult(true, null, null);
        }

        // --- Sign flow: state keyed in NonceCacheKeys (existing, unchanged) ---
        var cacheKey = NonceCacheKeys.ForState(request.State);
        if (!_cache.TryGetValue(cacheKey, out EudiwStateEntry? entry) || entry is null)
            return new HandleVpResponseResult(false, null, "State expired or already used");

        // Eviction moved to AFTER successful SD-JWT validation so a failed presentation
        // (bad signature, bad nonce, …) leaves the cache entry intact and the wallet can
        // retry within the original TTL window. Replay of an accepted vp_token is still
        // blocked because we Remove() on success below.

        var expectedAud = !string.IsNullOrWhiteSpace(_options.CurrentValue.PublicBaseUrl)
            ? _options.CurrentValue.PublicBaseUrl!.TrimEnd('/')
            : _options.CurrentValue.VerifierId;

        var claims = _sdJwtValidator.ValidateAndExtract(
            request.VpToken,
            entry.Nonce,
            expectedAud,
            SignDisclosureAllowlist);
        if (claims is null)
        {
            // Attribute the failure to stage 2 (SD-JWT verify) on the linked SigningRequest
            // so the Dashboard's SigningPipeline modal lights up the offending stage.
            var failedReq = await _db.SigningRequests.FirstOrDefaultAsync(s => s.Id == entry.SigningRequestId, cancellationToken);
            if (failedReq is not null)
            {
                failedReq.Status = SigningRequestStatus.Failed;
                failedReq.FailedAtStage = 2;
                failedReq.UpdatedAt = DateTime.UtcNow;
                try { await _db.SaveChangesAsync(cancellationToken); } catch { }
            }
            return new HandleVpResponseResult(false, null, "SD-JWT validation failed");
        }

        var sigReq = await _db.SigningRequests.FirstOrDefaultAsync(s => s.Id == entry.SigningRequestId, cancellationToken);
        if (sigReq is null)
            return new HandleVpResponseResult(false, null, "SigningRequest not found");

        if (sigReq.Status is not (SigningRequestStatus.Pending or SigningRequestStatus.HashPrepared))
            return new HandleVpResponseResult(false, null,
                $"SigningRequest in status {sigReq.Status} — cannot accept EUDIW authorization");

        sigReq.EudiwSubject = $"{claims.GivenName} {claims.FamilyName}".Trim();
        sigReq.Status = SigningRequestStatus.EudiwAuthorized;
        sigReq.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Replay eviction on success: same state cannot be reused for another VP submission.
        _cache.Remove(cacheKey);

        // Real wallet path: persist enrollment when SD-JWT carried a cnf.jwk.
        // sigReq.Document.OwnerUserId is the sender; the wallet user is the recipient
        // identified by claims.Email. Look them up by email when present.
        if (claims.CnfJwkThumbprint is not null && !string.IsNullOrWhiteSpace(claims.Email))
        {
            var walletUser = await _db.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == claims.Email!.ToLower(), cancellationToken);
            if (walletUser is not null)
            {
                var isNewThumbprint = await UpsertWalletEnrollmentAsync(walletUser.Id, claims, cancellationToken);
                if (isNewThumbprint && _audit is not null)
                {
                    var enrollMeta =
                        $"{{\"userId\":\"{walletUser.Id}\"," +
                        $"\"email\":{JsonSerializer.Serialize(walletUser.Email)}," +
                        $"\"thumbprint\":{JsonSerializer.Serialize(claims.CnfJwkThumbprint)}}}";
                    // Bind to the document being signed so the event surfaces on that
                    // document's audit trail in addition to the wallet history.
                    await _audit.WriteAsync(sigReq.DocumentId, "WalletEnrolled", enrollMeta, cancellationToken);
                }
            }
        }

        _logger.LogInformation("EUDIW authorization successful for SigningRequest {Id}", entry.SigningRequestId);

        return new HandleVpResponseResult(true, entry.SigningRequestId, null);
    }

    /// <summary>
    /// Upserts a WalletEnrollment row for the given user. One row per user (unique
    /// on UserId), so the same user re-enrolling with a new device replaces the
    /// existing thumbprint. Skips silently when the SD-JWT carried no cnf.jwk
    /// (legacy simulator path). Returns <c>true</c> when this call registered a
    /// previously-unseen thumbprint for the user (insert, or update with a
    /// different thumbprint) — the caller uses that signal to emit a
    /// <c>WalletEnrolled</c> audit event exactly once per device.
    /// </summary>
    private async Task<bool> UpsertWalletEnrollmentAsync(Guid userId, PidClaims claims, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(claims.CnfJwkThumbprint))
            return false;

        var now = DateTime.UtcNow;
        var issuedAt = (claims.IssuedAt ?? now).ToUniversalTime();
        var expiresAt = (claims.ExpiresAt ?? issuedAt.AddDays(365)).ToUniversalTime();
        var payload = JsonSerializer.Serialize(new
        {
            family_name = claims.FamilyName,
            given_name = claims.GivenName,
            birth_date = claims.BirthDate?.ToString("yyyy-MM-dd"),
            subject = claims.Subject,
            email = claims.Email
        });

        var pidEmail = string.IsNullOrWhiteSpace(claims.Email) ? null : claims.Email!.ToLowerInvariant();

        var existing = await _db.WalletEnrollments.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        bool isNewThumbprint;
        if (existing is null)
        {
            _db.WalletEnrollments.Add(new WalletEnrollment
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CnfJwkThumbprint = claims.CnfJwkThumbprint!,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                PidClaimsJson = payload,
                PidEmail = pidEmail,
                CreatedAt = now,
                UpdatedAt = now
            });
            isNewThumbprint = true;
        }
        else
        {
            isNewThumbprint = !string.Equals(existing.CnfJwkThumbprint, claims.CnfJwkThumbprint, StringComparison.Ordinal);
            existing.CnfJwkThumbprint = claims.CnfJwkThumbprint!;
            existing.IssuedAt = issuedAt;
            existing.ExpiresAt = expiresAt;
            existing.PidClaimsJson = payload;
            existing.PidEmail = pidEmail;
            existing.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return isNewThumbprint;
    }
}
