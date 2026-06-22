using System.Security.Cryptography;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Eudiw.HandleResponse;
using MasterSTI.Api.Features.Eudiw.RequestPresentation;
using MasterSTI.Api.Features.Wallet.Auth;
using MasterSTI.Shared.DTOs.Wallet;
using MasterSTI.UnitTests; // StaticOptionsMonitor<T>
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests.Features.Eudiw;

/// <summary>
/// Issue-#65 — replay-rejection contract on top of the eviction contract
/// covered by <see cref="HandleVpResponseEvictionTests"/>:
///   * Same <c>(state, vp_token)</c> resubmitted after a first success is rejected
///     because the cache entry was evicted by the first call.
///   * A captured <c>vp_token</c> bound to state A's nonce, replayed against a
///     newly-issued state B (different nonce), is rejected at the KB-JWT layer
///     because the nonce inside the KB-JWT does not match state B's nonce.
/// Together these defend against both same-state replay (RFC 7800 nonce-reuse)
/// and cross-state replay (SD-JWT VC §3.x).
/// </summary>
public class KbJwtReplayRejectionTests : IDisposable
{
    private const string VerifierId = "https://verifier.test";

    private readonly RSA _issuerRsa = RSA.Create(2048);
    private readonly ECDsa _walletEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly HandleVpResponseHandler _handler;

    public KbJwtReplayRejectionTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"KbJwtReplayTests_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(dbOptions);

        var opts = new EudiwOptions
        {
            VerifierId = VerifierId,
            ResponseUri = $"{VerifierId}/api/eudiw/response",
            IssuerPublicKeyPem = _issuerRsa.ExportSubjectPublicKeyInfoPem(),
            NonceCacheMinutes = 5,
            KbJwtIatSkewSeconds = 60,
        };
        var monitor = new StaticOptionsMonitor<EudiwOptions>(opts);
        var validator = new SdJwtValidator(monitor, NullLogger<SdJwtValidator>.Instance, httpFactory: null);

        _cache = new MemoryCache(new MemoryCacheOptions());

        var tokens = Substitute.For<IJwtTokenService>();
        tokens.Issue(Arg.Any<User>()).Returns(_ => ("issued-token-stub", DateTime.UtcNow.AddHours(1)));

        _db.Organizations.Add(new Organization
        {
            Id = DbInitializer.SeedOrganizationId,
            Name = "Test Org",
            CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        _handler = new HandleVpResponseHandler(
            _db, validator, tokens, _cache, monitor,
            NullLogger<HandleVpResponseHandler>.Instance);
    }

    [Fact]
    public async Task LoginBranch_SameStateAndVpReplayed_RejectedOnSecondCall()
    {
        var state = "replay-state-login";
        var nonce = "replay-nonce-login-1";
        SeedLoginCache(state, nonce);

        var vpToken = SdJwtFixture.BuildKeyBound(_issuerRsa, _walletEc, VerifierId, nonce, includeEmail: true);

        // First submission — accepted.
        var first = await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);
        Assert.True(first.Success);

        // Second submission — same state, same vp_token, no fresh nonce in cache. The
        // wallet-auth entry was evicted by the first success, so the handler now sees
        // no live state for this key and rejects.
        var replay = await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);
        Assert.False(replay.Success);
        Assert.NotNull(replay.Error);
    }

    [Fact]
    public async Task SignBranch_SameStateAndVpReplayed_RejectedOnSecondCall()
    {
        var state = "replay-state-sign";
        var nonce = "replay-nonce-sign-1";
        var sigId = await SeedSigningRequestAsync();
        _cache.Set(NonceCacheKeys.ForState(state), new EudiwStateEntry(nonce, sigId), TimeSpan.FromMinutes(5));

        var vpToken = SdJwtFixture.BuildKeyBound(_issuerRsa, _walletEc, VerifierId, nonce, includeEmail: true);

        var first = await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);
        Assert.True(first.Success);

        var replay = await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);
        Assert.False(replay.Success);
        Assert.NotNull(replay.Error);
        Assert.Contains("expired or already used", replay.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginBranch_CrossStateReplay_RejectedBecauseKbNonceMismatch()
    {
        // State A: live, nonce A. Wallet authorises against A with vp_token_A.
        // State B: live, fresh nonce B. Attacker captures vp_token_A and POSTs it
        // against state B. KB-JWT inside vp_token_A is bound to nonce A — the
        // validator's nonce check must reject because the cache entry for B carries
        // nonce B.
        var stateA = "replay-state-A";
        var nonceA = "nonce-A";
        SeedLoginCache(stateA, nonceA);

        var stateB = "replay-state-B";
        var nonceB = "nonce-B";
        SeedLoginCache(stateB, nonceB);

        var vpToken = SdJwtFixture.BuildKeyBound(_issuerRsa, _walletEc, VerifierId, nonceA, includeEmail: true);

        // Vp bound to A's nonce, posted against B's state.
        var crossReplay = await _handler.Handle(new HandleVpResponseCommand(vpToken, stateB), CancellationToken.None);

        Assert.False(crossReplay.Success);
        Assert.Equal("SD-JWT validation failed", crossReplay.Error);

        // Replay protection works in both directions: state B's entry is preserved
        // (validation failure must NOT evict), and state A's entry is also preserved
        // because the cross-state POST never touched it.
        Assert.True(_cache.TryGetValue(WalletAuthCacheKeys.ForState(stateA), out _));
        Assert.True(_cache.TryGetValue(WalletAuthCacheKeys.ForState(stateB), out _));
    }

    [Fact]
    public async Task SignBranch_CrossStateReplay_RejectedBecauseKbNonceMismatch()
    {
        var stateA = "sign-replay-A";
        var nonceA = "sign-nonce-A";
        var sigIdA = await SeedSigningRequestAsync();
        _cache.Set(NonceCacheKeys.ForState(stateA), new EudiwStateEntry(nonceA, sigIdA), TimeSpan.FromMinutes(5));

        var stateB = "sign-replay-B";
        var nonceB = "sign-nonce-B";
        var sigIdB = await SeedSigningRequestAsync();
        _cache.Set(NonceCacheKeys.ForState(stateB), new EudiwStateEntry(nonceB, sigIdB), TimeSpan.FromMinutes(5));

        // Capture VP_A (signed against nonceA).
        var vpTokenA = SdJwtFixture.BuildKeyBound(_issuerRsa, _walletEc, VerifierId, nonceA, includeEmail: true);

        // Replay VP_A against state B.
        var crossReplay = await _handler.Handle(new HandleVpResponseCommand(vpTokenA, stateB), CancellationToken.None);

        Assert.False(crossReplay.Success);
        Assert.Equal("SD-JWT validation failed", crossReplay.Error);

        // SigningRequest for stateB must NOT have been advanced.
        var sigReqB = await _db.SigningRequests.FindAsync(sigIdB);
        Assert.NotNull(sigReqB);
        Assert.NotEqual(SigningRequestStatus.EudiwAuthorized, sigReqB!.Status);
        // Validation failure attributes to stage 2 per the handler's catch path.
        Assert.Equal(SigningRequestStatus.Failed, sigReqB.Status);
        Assert.Equal(2, sigReqB.FailedAtStage);
    }

    private void SeedLoginCache(string state, string nonce)
    {
        _cache.Set(WalletAuthCacheKeys.ForState(state), new WalletAuthEntry(
            State: state,
            Nonce: nonce,
            SigningRequestId: Guid.Empty,
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(5),
            Status: "pending",
            Subject: null,
            CompletedAtUtc: null,
            Purpose: WalletAuthPurpose.Login,
            Login: null), TimeSpan.FromMinutes(5));
    }

    private async Task<Guid> SeedSigningRequestAsync()
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            StoragePath = "x",
            Sha256Hash = "h",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Uploaded,
        });
        var recipientId = Guid.NewGuid();
        _db.Recipients.Add(new Recipient
        {
            Id = recipientId,
            DocumentId = docId,
            Email = "ion.popescu@verasign.demo",
            Name = "Ion Popescu",
            Order = 1,
            Level = "QES",
            Status = RecipientStatus.Notified,
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
            UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return sigId;
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        _issuerRsa.Dispose();
        _walletEc.Dispose();
    }
}
