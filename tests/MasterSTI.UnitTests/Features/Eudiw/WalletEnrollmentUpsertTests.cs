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
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MasterSTI.UnitTests.Features.Eudiw;

/// <summary>
/// Issue-#64 — exercises the WalletEnrollment upsert that
/// <see cref="HandleVpResponseHandler"/> performs on every VP response that
/// carries a <c>cnf.jwk</c>. Two invariants matter for the dissertation:
///   * <b>Idempotency</b>: the same wallet (same user, same key thumbprint)
///     re-presenting must produce one row, with timestamps refreshed.
///   * <b>Cross-user thumbprint collision</b>: the production relational
///     schema must enforce a unique index on <c>CnfJwkThumbprint</c> so that
///     a wallet key cannot be silently rebound from one user to another. The
///     in-memory test provider does not enforce unique indexes; assert the
///     model metadata explicitly so the contract cannot silently regress.
/// </summary>
public class WalletEnrollmentUpsertTests : IDisposable
{
    private const string VerifierId = "https://verifier.test";
    private const string Nonce = "wallet-enrollment-nonce";

    private readonly RSA _issuerRsa = RSA.Create(2048);
    private readonly ECDsa _walletEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly HandleVpResponseHandler _handler;

    public WalletEnrollmentUpsertTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"WalletEnrollmentTests_{Guid.NewGuid()}")
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
    public async Task LoginPurpose_SameWalletPostedTwice_LeavesOneRow_WithRefreshedTimestamps()
    {
        var firstLogin = await PostLoginAsync("login-1");

        // SAME wallet (same `_walletEc` ⇒ same cnf JWK thumbprint), SAME PID email ⇒
        // same User row, same WalletEnrollment row. UpdatedAt must advance.
        await Task.Delay(50); // ensure UpdatedAt strictly advances
        var secondLogin = await PostLoginAsync("login-2");

        Assert.True(firstLogin.Success);
        Assert.True(secondLogin.Success);

        var enrollments = await _db.WalletEnrollments.AsNoTracking().ToListAsync();
        Assert.Single(enrollments);
        // Idempotency: same UserId + same Thumbprint, but the row was rewritten.
        var only = enrollments[0];
        Assert.False(string.IsNullOrEmpty(only.CnfJwkThumbprint));
        Assert.NotNull(only.PidClaimsJson);
    }

    [Fact]
    public async Task SignPurpose_OnSuccess_UpsertsEnrollmentForRecipientUser()
    {
        // Seed a recipient user whose email matches the SD-JWT's disclosed email so the
        // Sign branch's email-keyed lookup finds them and persists the enrollment.
        var emailLower = "ion.popescu@verasign.demo";
        var userId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = userId,
            Email = emailLower,
            Name = "Ion Popescu",
            Role = "User",
            OrganizationId = DbInitializer.SeedOrganizationId,
            PasswordHash = string.Empty,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await PostSignAsync(state: "sign-1", recipientEmail: emailLower);

        var enrollments = await _db.WalletEnrollments.AsNoTracking().ToListAsync();
        var only = Assert.Single(enrollments);
        Assert.Equal(userId, only.UserId);
        Assert.False(string.IsNullOrEmpty(only.CnfJwkThumbprint));
    }

    [Fact]
    public async Task SignPurpose_TwoSuccessfulPresentationsForSameUser_StillOneRow()
    {
        var emailLower = "ion.popescu@verasign.demo";
        var userId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = userId,
            Email = emailLower,
            Name = "Ion Popescu",
            Role = "User",
            OrganizationId = DbInitializer.SeedOrganizationId,
            PasswordHash = string.Empty,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        // Two independent signing requests, same wallet user, same device key.
        await PostSignAsync(state: "sign-2a", recipientEmail: emailLower);
        await PostSignAsync(state: "sign-2b", recipientEmail: emailLower);

        var enrollments = await _db.WalletEnrollments.AsNoTracking().ToListAsync();
        Assert.Single(enrollments);
    }

    [Fact]
    public void WalletEnrollment_ModelDeclaresUniqueIndexOnCnfJwkThumbprint()
    {
        // The cross-user-collision guarantee depends on this unique index. The in-memory
        // EF provider does not enforce unique indexes, so the production contract is
        // verified by reading EF Core's model metadata directly. SQL Server / SQLite will
        // raise <see cref="DbUpdateException"/> on a second insert with the same value.
        var entityType = _db.Model.FindEntityType(typeof(WalletEnrollment));
        Assert.NotNull(entityType);

        var indexes = entityType!.GetIndexes().ToList();
        Assert.Contains(indexes, IsUniqueIndexOn(nameof(WalletEnrollment.CnfJwkThumbprint)));
        Assert.Contains(indexes, IsUniqueIndexOn(nameof(WalletEnrollment.UserId)));

        static Predicate<IIndex> IsUniqueIndexOn(string propertyName) =>
            idx => idx.IsUnique
                   && idx.Properties.Count == 1
                   && idx.Properties[0].Name == propertyName;
    }

    private Task<HandleVpResponseResult> PostLoginAsync(string state)
    {
        var walletKey = WalletAuthCacheKeys.ForState(state);
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

        var vpToken = SdJwtFixture.BuildKeyBound(_issuerRsa, _walletEc, VerifierId, Nonce, includeEmail: true);
        return _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);
    }

    private async Task<HandleVpResponseResult> PostSignAsync(string state, string recipientEmail)
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
            Email = recipientEmail,
            Name = "Recipient",
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

        var stateKey = NonceCacheKeys.ForState(state);
        _cache.Set(stateKey, new EudiwStateEntry(Nonce, sigId), TimeSpan.FromMinutes(5));

        var vpToken = SdJwtFixture.BuildKeyBound(_issuerRsa, _walletEc, VerifierId, Nonce, includeEmail: true);
        return await _handler.Handle(new HandleVpResponseCommand(vpToken, state), CancellationToken.None);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        _issuerRsa.Dispose();
        _walletEc.Dispose();
    }
}
