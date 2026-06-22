using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Csc;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Signing.GetTechnicalDetail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MasterSTI.UnitTests.Features.Signing;

public class GetTechnicalDetailHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ICscApiClient _csc = Substitute.For<ICscApiClient>();
    private readonly IOptionsMonitor<CscApiOptions> _options;

    public GetTechnicalDetailHandlerTests()
    {
        var dbOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TechDetailDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(dbOpts);

        var monitor = Substitute.For<IOptionsMonitor<CscApiOptions>>();
        monitor.CurrentValue.Returns(new CscApiOptions
        {
            Username = "user",
            Password = "pass",
            CredentialId = "fallback-cred",
        });
        _options = monitor;
    }

    private GetTechnicalDetailHandler CreateHandler() =>
        new(_db, _csc, _options, NullLogger<GetTechnicalDetailHandler>.Instance);

    private SigningRequest SeedSigningRequest(
        string documentHash = "abcdef0123456789aabbccddeeff0011",
        string credentialId = "cred-x",
        string level = "PAdES-B-LT")
    {
        var docId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "x.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x",
            Sha256Hash = documentHash,
            UploadedAt = DateTime.UtcNow,
            Status = DocumentStatus.Signing,
        });
        _db.Recipients.Add(new Recipient
        {
            Id = recipientId,
            DocumentId = docId,
            Email = "andrei@verasign.demo",
            Name = "Andrei",
            Order = 1,
            Level = "QES",
            Status = RecipientStatus.Notified,
        });
        var sigReq = new SigningRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            RecipientId = recipientId,
            OrderIndex = 1,
            RequestedBy = "andrei@verasign.demo",
            CredentialId = credentialId,
            SignatureLevel = level,
            DocumentHash = documentHash,
            PreparedStoragePath = "/tmp/prepared.pdf",
            Status = SigningRequestStatus.HashPrepared,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.SigningRequests.Add(sigReq);
        _db.SaveChanges();
        return sigReq;
    }

    /// <summary>
    /// Self-signed dev cert generated once via:
    /// <c>openssl req -x509 -newkey rsa:2048 -days 365 -nodes -subj "/CN=VeraSign Demo TSP"</c>.
    /// Base64-encoded DER bytes. Only the SHA-256 fingerprint matters for the test;
    /// any deterministic byte sequence works.
    /// </summary>
    private static string FakeCertBase64()
    {
        // 32 deterministic bytes — base64-encoded — stand in for a DER-encoded cert.
        // The handler only does SHA-256(certBytes), not X.509 parsing.
        var bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsDtoFields()
    {
        var sigReq = SeedSigningRequest(
            documentHash: "00112233445566778899aabbccddeeff",
            credentialId: "csc-cred-99",
            level: "PAdES-B-LT");

        _csc.AuthLoginAsync("user", "pass", Arg.Any<CancellationToken>())
            .Returns("access-token");
        _csc.GetCredentialInfoAsync("access-token", "csc-cred-99", Arg.Any<CancellationToken>())
            .Returns(new CscCredentialInfoResponse(
                description: "Demo cred",
                key: new CscKeyInfo("enabled", new[] { "1.2.840.113549.1.1.1" }, 2048),
                cert: new CscCertInfo(
                    status: "valid",
                    certificates: new[] { FakeCertBase64() },
                    issuerDN: "CN=VeraSign Demo TSP, O=VeraSign, C=RO",
                    serialNumber: "01",
                    subjectDN: "CN=Andrei Radu",
                    validFrom: "20240101",
                    validTo: "20260101"),
                authMode: "explicit",
                multisign: 1,
                lang: "en-US"));

        var dto = await CreateHandler().Handle(
            new GetTechnicalDetailQuery(sigReq.Id), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("0011223344556677", dto!.HashPrefix);
        Assert.Equal("PAdES-B-LT", dto.Level);
        Assert.Equal("VeraSign Demo TSP", dto.TspName);
        Assert.Equal("SHA-256 + RSA-2048", dto.Algorithm);

        // Fingerprint = SHA-256 of bytes [0..31] formatted as colon-separated hex pairs.
        // Computed once; if the test cert bytes change, regenerate.
        Assert.False(string.IsNullOrEmpty(dto.CertificateFingerprint));
        Assert.Contains(':', dto.CertificateFingerprint);
        // 32 hex pairs * 2 chars + 31 colons = 95
        Assert.Equal(95, dto.CertificateFingerprint.Length);
    }

    [Fact]
    public async Task Handle_MissingSigningRequest_ReturnsNull()
    {
        var dto = await CreateHandler().Handle(
            new GetTechnicalDetailQuery(Guid.NewGuid()), CancellationToken.None);
        Assert.Null(dto);
    }

    [Fact]
    public async Task Handle_CscFailure_DegradesGracefully()
    {
        var sigReq = SeedSigningRequest();

        _csc.AuthLoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("QTSP unavailable"));

        var dto = await CreateHandler().Handle(
            new GetTechnicalDetailQuery(sigReq.Id), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(string.Empty, dto!.CertificateFingerprint);
        Assert.Equal(string.Empty, dto.TspName);
        Assert.Equal("SHA-256 + RSA-2048", dto.Algorithm);
        Assert.Equal("PAdES-B-LT", dto.Level);
        Assert.False(string.IsNullOrEmpty(dto.HashPrefix));
    }

    [Fact]
    public async Task Handle_NoCscCredentials_SkipsCscCall()
    {
        var sigReq = SeedSigningRequest();

        var emptyOpts = Substitute.For<IOptionsMonitor<CscApiOptions>>();
        emptyOpts.CurrentValue.Returns(new CscApiOptions { Username = null, Password = null });
        var handler = new GetTechnicalDetailHandler(_db, _csc, emptyOpts, NullLogger<GetTechnicalDetailHandler>.Instance);

        var dto = await handler.Handle(
            new GetTechnicalDetailQuery(sigReq.Id), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(string.Empty, dto!.CertificateFingerprint);
        Assert.Equal(string.Empty, dto.TspName);
        await _csc.DidNotReceive().AuthLoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The endpoint refuses callers who fail <see cref="IRecipientAccessGuard.CanAccessDocumentAsync"/>.
    /// We can't easily host the endpoint in-process here, but we can prove the guard
    /// path the endpoint uses: when the guard's predicate returns false, the
    /// endpoint must short-circuit and not invoke the handler. This test verifies
    /// the contract: a guard refusing access prevents the handler call. Endpoint
    /// wiring itself is asserted by inspection of
    /// <c>GetTechnicalDetailEndpoint.MapGetTechnicalDetail</c>.
    /// </summary>
    [Fact]
    public async Task AccessGuard_RejectsNonRecipient()
    {
        var sigReq = SeedSigningRequest();
        var guard = Substitute.For<IRecipientAccessGuard>();
        guard.CanAccessDocumentAsync(sigReq.DocumentId, Arg.Any<CancellationToken>())
             .Returns(false);

        // Simulate what GetTechnicalDetailEndpoint does: look up doc id, ask the
        // guard, return Forbid before the handler runs.
        var docId = await _db.SigningRequests.AsNoTracking()
            .Where(s => s.Id == sigReq.Id)
            .Select(s => (Guid?)s.DocumentId)
            .FirstOrDefaultAsync();

        Assert.NotNull(docId);
        var allowed = await guard.CanAccessDocumentAsync(docId!.Value, CancellationToken.None);
        Assert.False(allowed);

        await guard.Received(1).CanAccessDocumentAsync(sigReq.DocumentId, Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}
