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
/// Locks in that <see cref="HandleVpResponseHandler"/> emits a <c>WalletLogin</c>
/// audit event when the Login-purpose branch reaches a successful
/// <c>LoginResponse</c> mint, and NEVER on the Sign branch or on failure paths
/// (SD-JWT validation reject, missing state).
/// </summary>
public class WalletLoginEventTests : IDisposable
{
    private const string VerifierId = "https://verifier.test";
    private const string Nonce = "wallet-login-nonce";

    private readonly RSA _issuerRsa = RSA.Create(2048);
    private readonly ECDsa _walletEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public WalletLoginEventTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"WalletLoginTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Login flow needs at least the seed organization so the auto-create
        // user path resolves OrganizationId.
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

    private (string state, string vpToken) SetupLogin(string email)
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
        return (state, vpToken);
    }

    [Fact]
    public async Task LoginSuccess_EmitsWalletLogin()
    {
        var audit = Substitute.For<IAuditWriter>();
        var handler = CreateHandler(audit);

        var (state, vpToken) = SetupLogin("login.success@verasign.demo");

        var result = await handler.Handle(
            new HandleVpResponseCommand(vpToken, state),
            CancellationToken.None);

        Assert.True(result.Success);
        await audit.Received(1).WriteAsync(
            Arg.Any<Guid?>(),
            "WalletLogin",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginFailed_SdJwtRejected_DoesNotEmitWalletLogin()
    {
        var audit = Substitute.For<IAuditWriter>();
        var handler = CreateHandler(audit);

        var (state, _) = SetupLogin("login.fail@verasign.demo");

        var result = await handler.Handle(
            new HandleVpResponseCommand("malformed.token", state),
            CancellationToken.None);

        Assert.False(result.Success);
        await audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(),
            "WalletLogin",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingState_DoesNotEmitWalletLogin()
    {
        var audit = Substitute.For<IAuditWriter>();
        var handler = CreateHandler(audit);

        var (_, vpToken) = SetupLogin("login.no-state@verasign.demo");

        var result = await handler.Handle(
            new HandleVpResponseCommand(vpToken, string.Empty),
            CancellationToken.None);

        Assert.False(result.Success);
        await audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(),
            "WalletLogin",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignBranch_DoesNotEmitWalletLogin()
    {
        // Even when the Sign-flow path succeeds (and triggers WalletEnrolled),
        // it MUST NOT fire WalletLogin — that event is exclusive to the
        // password-less login flow.
        var audit = Substitute.For<IAuditWriter>();
        var handler = CreateHandler(audit);

        var email = "sign.only@verasign.demo";
        var userId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = userId,
            Email = email,
            Name = "Sign Only",
            Role = "User",
            OrganizationId = DbInitializer.SeedOrganizationId,
            PasswordHash = string.Empty,
            CreatedAt = DateTime.UtcNow
        });

        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "sign-only.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/sign-only.pdf",
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
        await audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(),
            "WalletLogin",
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
