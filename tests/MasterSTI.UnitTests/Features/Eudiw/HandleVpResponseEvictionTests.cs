using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Eudiw.HandleResponse;
using MasterSTI.Api.Features.Eudiw.RequestPresentation;
using MasterSTI.Api.Features.Wallet.Auth;
using MasterSTI.Shared.DTOs.Auth;
using MasterSTI.Shared.DTOs.Wallet;
using MasterSTI.UnitTests; // StaticOptionsMonitor<T> from SdJwtValidatorTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace MasterSTI.UnitTests.Features.Eudiw;

/// <summary>
/// Issue-9 replay-eviction contract:
///   * Login success: live <c>wallet-auth:</c> entry is evicted (replay-protected). A
///     short-lived <c>wallet-auth-completed:</c> marker is left for the polling client.
///   * Sign success: <c>eudiw:state:</c> entry is evicted.
///   * Failure (SD-JWT validation): cache entry is preserved so the wallet can retry
///     within the original TTL window.
/// </summary>
public class HandleVpResponseEvictionTests : IDisposable
{
    private const string VerifierId = "https://verifier.test";
    private const string Nonce = "test-nonce-eviction";

    private readonly RSA _issuerRsa = RSA.Create(2048);
    private readonly ECDsa _walletEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly EudiwOptions _opts;
    private readonly SdJwtValidator _validator;
    private readonly HandleVpResponseHandler _handler;

    public HandleVpResponseEvictionTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"EvictionTests_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(dbOptions);

        _opts = new EudiwOptions
        {
            VerifierId = VerifierId,
            ResponseUri = $"{VerifierId}/api/eudiw/response",
            IssuerPublicKeyPem = _issuerRsa.ExportSubjectPublicKeyInfoPem(),
            NonceCacheMinutes = 5,
            KbJwtIatSkewSeconds = 60
        };
        var monitor = new StaticOptionsMonitor<EudiwOptions>(_opts);
        _validator = new SdJwtValidator(monitor, NullLogger<SdJwtValidator>.Instance, httpFactory: null);

        _cache = new MemoryCache(new MemoryCacheOptions());

        // JWT issuer mock for Login flow (issues session JWT on success).
        var tokens = Substitute.For<IJwtTokenService>();
        tokens.Issue(Arg.Any<User>()).Returns(call =>
            ("issued-token-stub", DateTime.UtcNow.AddHours(1)));

        // Seed demo organization so login-created users have a parent org.
        _db.Organizations.Add(new Organization
        {
            Id = DbInitializer.SeedOrganizationId,
            Name = "Test Org",
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        _handler = new HandleVpResponseHandler(
            _db, _validator, tokens, _cache, monitor,
            NullLogger<HandleVpResponseHandler>.Instance);
    }

    [Fact]
    public async Task LoginSuccess_EvictsLiveEntry_WritesCompletionMarker()
    {
        var state = "login-state-success";
        var walletKey = WalletAuthCacheKeys.ForState(state);
        var completionKey = WalletAuthCacheKeys.ForCompletion(state);

        _cache.Set(walletKey, new WalletAuthEntry(
            State: state,
            Nonce: Nonce,
            SigningRequestId: Guid.Empty,
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(5),
            Status: "pending",
            Subject: null,
            CompletedAtUtc: null,
            Purpose: WalletAuthPurpose.Login,
            Login: null), TimeSpan.FromMinutes(5));

        var vpToken = SdJwtFixture.BuildKeyBound(
            _issuerRsa, _walletEc, VerifierId, Nonce, includeEmail: true);

        var result = await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);

        Assert.True(result.Success);
        // Live entry evicted.
        Assert.False(_cache.TryGetValue(walletKey, out _));
        // Completion marker present so polling client can still pick up the LoginResponse.
        Assert.True(_cache.TryGetValue(completionKey, out WalletAuthEntry? completed));
        Assert.NotNull(completed);
        Assert.Equal("complete", completed!.Status);
        Assert.NotNull(completed.Login);
    }

    [Fact]
    public async Task LoginFailure_PreservesLiveEntry_NoCompletionMarker()
    {
        var state = "login-state-failure";
        var walletKey = WalletAuthCacheKeys.ForState(state);
        var completionKey = WalletAuthCacheKeys.ForCompletion(state);

        _cache.Set(walletKey, new WalletAuthEntry(
            State: state,
            Nonce: Nonce,
            SigningRequestId: Guid.Empty,
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(5),
            Status: "pending",
            Subject: null,
            CompletedAtUtc: null,
            Purpose: WalletAuthPurpose.Login,
            Login: null), TimeSpan.FromMinutes(5));

        // Tampered VP token: wrong nonce ⇒ SD-JWT validation fails.
        var vpToken = SdJwtFixture.BuildKeyBound(
            _issuerRsa, _walletEc, VerifierId, "wrong-nonce", includeEmail: true);

        var result = await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);

        Assert.False(result.Success);
        // Failure does NOT evict — wallet must be able to retry within TTL.
        Assert.True(_cache.TryGetValue(walletKey, out WalletAuthEntry? entry));
        Assert.NotNull(entry);
        Assert.Equal("failed", entry!.Status);
        Assert.False(_cache.TryGetValue(completionKey, out _));
    }

    [Fact]
    public async Task SignSuccess_EvictsStateEntry()
    {
        var state = "sign-state-success";
        var stateKey = NonceCacheKeys.ForState(state);

        var (docId, recipientId, sigId) = await SeedSigningRequestAsync();

        _cache.Set(stateKey, new EudiwStateEntry(Nonce, sigId), TimeSpan.FromMinutes(5));

        var vpToken = SdJwtFixture.BuildKeyBound(
            _issuerRsa, _walletEc, VerifierId, Nonce, includeEmail: true);

        var result = await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);

        Assert.True(result.Success);
        // Replay protection: state entry is gone after successful authorisation.
        Assert.False(_cache.TryGetValue(stateKey, out _));

        var sigReq = await _db.SigningRequests.FindAsync(sigId);
        Assert.NotNull(sigReq);
        Assert.Equal(SigningRequestStatus.EudiwAuthorized, sigReq!.Status);
    }

    [Fact]
    public async Task SignFailure_PreservesStateEntry()
    {
        var state = "sign-state-failure";
        var stateKey = NonceCacheKeys.ForState(state);

        var (_, _, sigId) = await SeedSigningRequestAsync();

        _cache.Set(stateKey, new EudiwStateEntry(Nonce, sigId), TimeSpan.FromMinutes(5));

        // Wrong nonce in KB-JWT triggers validation failure.
        var vpToken = SdJwtFixture.BuildKeyBound(
            _issuerRsa, _walletEc, VerifierId, "wrong-nonce", includeEmail: true);

        var result = await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);

        Assert.False(result.Success);
        // Failure must NOT evict — wallet should be able to resubmit a correct VP for the same state.
        Assert.True(_cache.TryGetValue(stateKey, out EudiwStateEntry? preserved));
        Assert.NotNull(preserved);
        Assert.Equal(Nonce, preserved!.Nonce);
    }

    private async Task<(Guid docId, Guid recipientId, Guid sigId)> SeedSigningRequestAsync()
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "test.pdf",
            ContentType = "application/pdf",
            StoragePath = "x",
            Sha256Hash = "h",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Uploaded
        });
        var recipientId = Guid.NewGuid();
        _db.Recipients.Add(new Recipient
        {
            Id = recipientId,
            DocumentId = docId,
            Email = "x@test",
            Name = "x",
            Order = 1,
            Level = "QES",
            Status = RecipientStatus.Notified
        });
        var sigId = Guid.NewGuid();
        _db.SigningRequests.Add(new SigningRequest
        {
            Id = sigId,
            DocumentId = docId,
            RecipientId = recipientId,
            OrderIndex = 1,
            RequestedBy = "tester",
            CredentialId = "cred-1",
            DocumentHash = "h",
            Status = SigningRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return (docId, recipientId, sigId);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        _issuerRsa.Dispose();
        _walletEc.Dispose();
    }
}

/// <summary>
/// Minimal key-bound SD-JWT builder for handler-level integration tests in this folder.
/// Mirrors <c>SdJwtKeyBindingTests.BuildKeyBoundSdJwt</c> but is self-contained so the
/// Eudiw test folder doesn't reach into the flat-tests namespace.
/// </summary>
internal static class SdJwtFixture
{
    public static string BuildKeyBound(
        RSA issuerRsa, ECDsa walletEc, string aud, string nonce, bool includeEmail)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var now = DateTimeOffset.UtcNow;

        var p = walletEc.ExportParameters(includePrivateParameters: false);
        var cnfJwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url(p.Q.X!),
            ["y"] = Base64Url(p.Q.Y!)
        };

        var disc1 = Disclosure("family_name", "Popescu");
        var disc2 = Disclosure("given_name", "Ion");
        var discList = new List<string> { disc1, disc2 };
        if (includeEmail)
            discList.Add(Disclosure("email", "ion.popescu@verasign.demo"));
        var disclosures = discList.ToArray();
        var sdDigests = disclosures
            .Select(d => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(d))))
            .ToArray();

        var payload = new Dictionary<string, object>
        {
            ["iss"] = "https://test-issuer",
            ["sub"] = "test-subject-eviction",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["nonce"] = nonce,
            ["_sd_alg"] = "sha-256",
            ["_sd"] = sdDigests,
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = cnfJwk }
        };
        var headerB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = "test-issuer" }));
        var payloadB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var input = headerB64 + "." + payloadB64;
        var sig = issuerRsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var issuerJwt = input + "." + Base64Url(sig);

        var sdHash = ComputeSdHash(issuerJwt, disclosures);

        var kbCreds = new SigningCredentials(
            new ECDsaSecurityKey(walletEc) { KeyId = "wallet-ec" },
            SecurityAlgorithms.EcdsaSha256);

        // Use the SAME nonce that the wallet was challenged with — i.e. the cache entry's
        // nonce. Tests that want a "wrong nonce failure" pass a mismatched nonce here.
        var kbClaims = new List<Claim>
        {
            new("nonce", nonce),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("sd_hash", sdHash)
        };

        var kbToken = new JwtSecurityToken(
            issuer: null,
            audience: aud,
            claims: kbClaims,
            notBefore: now.AddMinutes(-1).UtcDateTime,
            expires: now.AddMinutes(5).UtcDateTime,
            signingCredentials: kbCreds);
        kbToken.Header["typ"] = "kb+jwt";

        var kbJwt = handler.WriteToken(kbToken);

        return string.Join('~', new[] { issuerJwt }.Concat(disclosures).Concat(new[] { kbJwt }));
    }

    private static string Disclosure(string name, string value)
    {
        var salt = Base64Url(RandomNumberGenerator.GetBytes(16));
        var arr = JsonSerializer.SerializeToUtf8Bytes(new object[] { salt, name, value });
        return Base64Url(arr);
    }

    private static string ComputeSdHash(string issuerJwt, string[] disclosures)
    {
        var sb = new StringBuilder(issuerJwt);
        foreach (var d in disclosures)
            sb.Append('~').Append(d);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(sb.ToString())));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
