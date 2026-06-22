using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.SignedDocuments.GetInfo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.X509;

namespace MasterSTI.UnitTests;

public class GetSignedDocumentInfoHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();

    public GetSignedDocumentInfoHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SignedInfoDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
    }

    private GetSignedDocumentInfoHandler Create() =>
        new(_db, _user, NullLogger<GetSignedDocumentInfoHandler>.Instance);

    private async Task<(Guid signedDocId, Guid docId, Guid senderId, Guid recipientUserId, DateTime genTime, DateTime signedAt)>
        SeedAsync(string? timestampTokenBase64, string recipientEmail = "andrei@verasign.demo", DateTime? genTime = null)
    {
        var senderId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var sigReqId = Guid.NewGuid();
        var signedDocId = Guid.NewGuid();
        var signedAt = DateTime.UtcNow.AddMinutes(-2);
        var effectiveGenTime = genTime ?? signedAt;

        _db.Users.AddRange(
            new User { Id = senderId, Email = "sender@verasign.demo", Name = "Sender", Role = "Admin", PasswordHash = "", CreatedAt = DateTime.UtcNow },
            new User { Id = recipientUserId, Email = recipientEmail, Name = "Andrei", Role = "User", PasswordHash = "", CreatedAt = DateTime.UtcNow });
        _db.Documents.Add(new Document
        {
            Id = docId,
            FileName = "contract.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/x",
            Sha256Hash = "abc",
            UploadedAt = DateTime.UtcNow.AddHours(-2),
            Status = DocumentStatus.Signed,
            OwnerUserId = senderId,
        });
        _db.Recipients.Add(new Recipient
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Email = recipientEmail,
            Name = "Andrei",
            Order = 1,
            Level = "QES",
            Status = RecipientStatus.Signed,
            NotifiedAt = DateTime.UtcNow.AddHours(-1),
            SignedAt = signedAt,
        });
        _db.SigningRequests.Add(new SigningRequest
        {
            Id = sigReqId,
            DocumentId = docId,
            RecipientId = _db.Recipients.Local.First().Id,
            OrderIndex = 1,
            RequestedBy = "sender",
            CredentialId = "cred-1",
            DocumentHash = "h",
            Status = SigningRequestStatus.Embedded,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        _db.SignedDocuments.Add(new SignedDocument
        {
            Id = signedDocId,
            OriginalDocumentId = docId,
            SigningRequestId = sigReqId,
            RecipientId = _db.Recipients.Local.First().Id,
            StoragePath = $"/tmp/{signedDocId}.pdf", // file does not exist; PDF-parse path degrades to null
            SignedAt = signedAt,
            PadesLevel = "PAdES-B-LTA",
            TimestampToken = timestampTokenBase64,
            IsFinal = true,
        });
        await _db.SaveChangesAsync();
        return (signedDocId, docId, senderId, recipientUserId, effectiveGenTime, signedAt);
    }

    [Fact]
    public async Task Handle_MissingSignedDoc_ReturnsNotFound()
    {
        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns("anyone@x.com");

        var result = await Create().Handle(
            new GetSignedDocumentInfoQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(GetSignedDocumentInfoStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Handle_SenderCaller_ReturnsInfoWithDecodedTsaTime()
    {
        var genTime = DateTime.UtcNow.AddMinutes(-1);
        var tokenBase64 = TimestampFixture.MakeTokenBase64(genTime);
        var seeded = await SeedAsync(tokenBase64, genTime: genTime);
        _user.UserId.Returns(seeded.senderId);
        _user.Email.Returns("sender@verasign.demo");

        var result = await Create().Handle(
            new GetSignedDocumentInfoQuery(seeded.signedDocId), CancellationToken.None);

        Assert.Equal(GetSignedDocumentInfoStatus.Ok, result.Status);
        Assert.NotNull(result.Info);
        Assert.Equal(seeded.signedDocId, result.Info!.Id);
        Assert.Equal("PAdES-B-LTA", result.Info.Level);
        Assert.Equal(seeded.signedDocId.ToString("N"), result.Info.TxnId);
        Assert.NotNull(result.Info.TsaTime);
        // BC TimeStampToken serialises genTime at second precision.
        Assert.True(Math.Abs((result.Info.TsaTime!.Value - genTime).TotalSeconds) < 2);
        Assert.Equal(DateTimeKind.Utc, result.Info.SignedAtUtc.Kind);
    }

    [Fact]
    public async Task Handle_RecipientCaller_Allowed()
    {
        var seeded = await SeedAsync(timestampTokenBase64: null);
        _user.UserId.Returns(seeded.recipientUserId);
        _user.Email.Returns("ANDREI@VeraSign.demo"); // case-insensitive match

        var result = await Create().Handle(
            new GetSignedDocumentInfoQuery(seeded.signedDocId), CancellationToken.None);

        Assert.Equal(GetSignedDocumentInfoStatus.Ok, result.Status);
        Assert.Null(result.Info!.TsaTime); // no token, no PDF → null
    }

    [Fact]
    public async Task Handle_UnrelatedCaller_Forbidden()
    {
        var seeded = await SeedAsync(timestampTokenBase64: null);
        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns("stranger@example.com");

        var result = await Create().Handle(
            new GetSignedDocumentInfoQuery(seeded.signedDocId), CancellationToken.None);

        Assert.Equal(GetSignedDocumentInfoStatus.Forbidden, result.Status);
        Assert.Null(result.Info);
    }

    [Fact]
    public async Task Handle_AnonymousCaller_Forbidden()
    {
        var seeded = await SeedAsync(timestampTokenBase64: null);
        _user.UserId.Returns((Guid?)null);

        var result = await Create().Handle(
            new GetSignedDocumentInfoQuery(seeded.signedDocId), CancellationToken.None);

        Assert.Equal(GetSignedDocumentInfoStatus.Forbidden, result.Status);
    }

    public void Dispose() => _db.Dispose();
}

internal static class TimestampFixture
{
    public static string MakeTokenBase64(DateTime genTimeUtc)
    {
        var random = new SecureRandom();
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(random, 2048));
        var pair = keyGen.GenerateKeyPair();

        var certGen = new X509V3CertificateGenerator();
        certGen.SetSerialNumber(BigInteger.ValueOf(1));
        certGen.SetIssuerDN(new X509Name("CN=Fixture TSA"));
        certGen.SetSubjectDN(new X509Name("CN=Fixture TSA"));
        certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
        certGen.SetPublicKey(pair.Public);
        certGen.AddExtension(X509Extensions.ExtendedKeyUsage, true,
            new ExtendedKeyUsage(KeyPurposeID.id_kp_timeStamping));
        var sigFactory = new Asn1SignatureFactory("SHA256WITHRSA", pair.Private, random);
        var cert = certGen.Generate(sigFactory);

        var tsTokenGen = new TimeStampTokenGenerator(
            (RsaPrivateCrtKeyParameters)pair.Private,
            cert,
            TspAlgorithms.Sha256,
            "1.2.3.4.5");

        var reqGen = new TimeStampRequestGenerator();
        reqGen.SetCertReq(false);
        var imprint = new byte[32];
        random.NextBytes(imprint);
        var req = reqGen.Generate(TspAlgorithms.Sha256, imprint, BigInteger.ValueOf(100));

        var respGen = new TimeStampResponseGenerator(tsTokenGen, TspAlgorithms.Allowed);
        var resp = respGen.Generate(req, BigInteger.ValueOf(1), genTimeUtc);
        var encoded = resp.TimeStampToken.GetEncoded();
        return Convert.ToBase64String(encoded);
    }
}
