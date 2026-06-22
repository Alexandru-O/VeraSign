using System.Security.Cryptography;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Eudiw.HandleResponse;
using MasterSTI.Api.Features.Eudiw.RequestPresentation;
using MasterSTI.Api.Features.Wallet.Auth;
using MasterSTI.Shared.DTOs.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests.Features.Audit;

/// <summary>
/// Locks in that <see cref="HandleVpResponseHandler"/> emits a
/// <c>WalletEnrolled</c> audit event exactly once per new <c>cnf.jwk</c>
/// thumbprint upsert, and never for a repeat presentation with the same
/// thumbprint already on file. Exercises the Login branch — the Sign branch
/// uses the same <c>UpsertWalletEnrollmentAsync</c> helper, so the same
/// guard applies there.
/// </summary>
public class WalletEnrolledEventTests : IDisposable
{
    private const string VerifierId = "https://verifier.test";
    private const string Nonce = "wallet-enroll-nonce";

    private readonly RSA _issuerRsa = RSA.Create(2048);
    private readonly ECDsa _walletEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public WalletEnrolledEventTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"WalletEnrolledTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Login auto-create needs the seed org to back OrganizationId.
        _db.Organizations.Add(new Organization
        {
            Id = DbInitializer.SeedOrganizationId,
            Name = "Test Org"
        });
        _db.SaveChanges();
    }

    private HandleVpResponseHandler CreateHandler(IAuditWriter audit)
    {
        var opts = new EudiwOptions
        {
            VerifierId = VerifierId,
            IssuerPublicKeyPem = _issuerRsa.ExportSubjectPublicKeyInfoPem()
        };
        var validator = new SdJwtValidator(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            NullLogger<SdJwtValidator>.Instance,
            httpFactory: null);

        var tokens = Substitute.For<IJwtTokenService>();
        tokens.Issue(Arg.Any<User>()).Returns(("test-jwt", DateTime.UtcNow.AddHours(1)));

        return new HandleVpResponseHandler(
            _db,
            validator,
            tokens,
            _cache,
            new StaticOptionsMonitor<EudiwOptions>(opts),
            NullLogger<HandleVpResponseHandler>.Instance,
            audit: audit);
    }

    private async Task<(string state, string vpToken)> SetupLoginAsync(string email)
    {
        var state = Guid.NewGuid().ToString();
        _cache.Set(
            WalletAuthCacheKeys.ForState(state),
            new WalletAuthEntry(
                State: state,
                Nonce: Nonce,
                SigningRequestId: Guid.Empty,
                ExpiresAtUtc: DateTime.UtcNow.AddMinutes(5),
                Status: "pending",
                Subject: null,
                CompletedAtUtc: null,
                Purpose: WalletAuthPurpose.Login));

        var vpToken = SdJwtTestBuilder.BuildKeyBoundSdJwt(
            _issuerRsa, _walletEc, VerifierId, Nonce, email: email);
        await Task.CompletedTask;
        return (state, vpToken);
    }

    [Fact]
    public async Task Login_FirstTimeThumbprint_EmitsWalletEnrolled()
    {
        var audit = Substitute.For<IAuditWriter>();
        var handler = CreateHandler(audit);

        var (state, vpToken) = await SetupLoginAsync("first.user@verasign.demo");

        var result = await handler.Handle(
            new HandleVpResponseCommand(vpToken, state),
            CancellationToken.None);

        Assert.True(result.Success);
        await audit.Received(1).WriteAsync(
            Arg.Any<Guid?>(),
            "WalletEnrolled",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_DuplicateThumbprint_DoesNotEmitWalletEnrolled()
    {
        var audit = Substitute.For<IAuditWriter>();
        var handler = CreateHandler(audit);

        // First login — establishes the enrollment row.
        var (state1, vpToken1) = await SetupLoginAsync("repeat.user@verasign.demo");
        await handler.Handle(new HandleVpResponseCommand(vpToken1, state1), CancellationToken.None);

        audit.ClearReceivedCalls();

        // Second login with the SAME wallet key (same thumbprint).
        var (state2, vpToken2) = await SetupLoginAsync("repeat.user@verasign.demo");
        await handler.Handle(new HandleVpResponseCommand(vpToken2, state2), CancellationToken.None);

        await audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(),
            "WalletEnrolled",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_FailedSdJwt_DoesNotEmitWalletEnrolled()
    {
        var audit = Substitute.For<IAuditWriter>();
        var handler = CreateHandler(audit);

        var (state, _) = await SetupLoginAsync("rejected.user@verasign.demo");

        var result = await handler.Handle(
            new HandleVpResponseCommand("not.a.valid.token", state),
            CancellationToken.None);

        Assert.False(result.Success);
        await audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(),
            "WalletEnrolled",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sign_NewThumbprintForExistingRecipient_EmitsWalletEnrolledBoundToDocument()
    {
        var audit = Substitute.For<IAuditWriter>();
        var handler = CreateHandler(audit);

        var email = "signer@verasign.demo";

        // Seed the recipient user, document, and a Pending SigningRequest.
        var userId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = userId,
            Email = email,
            Name = "Recipient",
            Role = "User",
            OrganizationId = DbInitializer.SeedOrganizationId,
            PasswordHash = string.Empty,
            CreatedAt = DateTime.UtcNow
        });

        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "sign-target.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/sign-target.pdf",
            Sha256Hash = "deadbeef",
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Awaiting
        });

        var sigReqId = Guid.NewGuid();
        _db.SigningRequests.Add(new SigningRequest
        {
            Id = sigReqId,
            DocumentId = docId,
            RequestedBy = "test",
            CredentialId = "cred-1",
            DocumentHash = "deadbeef",
            Status = SigningRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Seed the Sign-flow cache entry.
        var state = Guid.NewGuid().ToString();
        _cache.Set(
            NonceCacheKeys.ForState(state),
            new EudiwStateEntry(Nonce, sigReqId));

        var vpToken = SdJwtTestBuilder.BuildKeyBoundSdJwt(
            _issuerRsa, _walletEc, VerifierId, Nonce, email: email);

        var result = await handler.Handle(
            new HandleVpResponseCommand(vpToken, state),
            CancellationToken.None);

        Assert.True(result.Success);
        await audit.Received(1).WriteAsync(
            docId,
            "WalletEnrolled",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        _issuerRsa.Dispose();
        _walletEc.Dispose();
    }
}
