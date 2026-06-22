using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Eudiw.RequestPresentation;
using MasterSTI.UnitTests; // StaticOptionsMonitor<T> from SdJwtValidatorTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MasterSTI.UnitTests.Features.Eudiw;

/// <summary>
/// Locks in the issue-9 fix: <c>state</c> in <see cref="RequestPresentationHandler"/>
/// must be non-deterministic (≥96 bits of entropy) and non-derivable from the
/// <see cref="RequestPresentationCommand.SigningRequestId"/>. Two consecutive calls
/// for the same SigningRequest must produce two different states.
/// </summary>
public class OidVpStateEntropyTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RequestPresentationHandler _handler;

    public OidVpStateEntropyTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"OidVpStateEntropyTests_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Eudiw:VerifierId", "https://verifier.test"),
            new KeyValuePair<string, string?>("Eudiw:ResponseUri", "https://verifier.test/api/eudiw/response")
        }).Build();
        var openId4Vp = new OpenId4VpService(config, NullLogger<OpenId4VpService>.Instance);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new StaticOptionsMonitor<EudiwOptions>(new EudiwOptions
        {
            VerifierId = "https://verifier.test",
            ResponseUri = "https://verifier.test/api/eudiw/response",
            NonceCacheMinutes = 5
        });

        _handler = new RequestPresentationHandler(
            _db, openId4Vp, cache, opts, NullLogger<RequestPresentationHandler>.Instance);
    }

    private async Task<Guid> SeedSigningRequestAsync()
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
            Status = RecipientStatus.Pending
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
        return sigId;
    }

    /// <summary>
    /// Issue-9 acceptance criterion: state must NOT be derivable from the
    /// SigningRequestId. Before the fix, state was <c>signingRequestId.ToString("N")</c>
    /// and would equal the request ID. After the fix, state is independent random.
    /// </summary>
    [Fact]
    public async Task State_IsNotDerivableFromSigningRequestId()
    {
        var sigId = await SeedSigningRequestAsync();
        var result = await _handler.Handle(new RequestPresentationCommand(sigId), CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.QrPayload));
        var state = ExtractState(result.QrPayload);

        Assert.NotEqual(sigId.ToString("N"), state);
        Assert.NotEqual(sigId.ToString(), state);
    }

    /// <summary>
    /// 1000 calls for the SAME SigningRequestId. Every emitted state must be distinct.
    /// Per-base64url-char entropy ~ 6 bits; with 16 random bytes the encoded state is
    /// ~22 chars, giving ≥96 bits of entropy. Length × 6 ≥ 96 verified on each emission.
    /// </summary>
    [Fact]
    public async Task State_OneThousandCalls_AllDistinct_AndAtLeast96BitsEntropy()
    {
        var sigId = await SeedSigningRequestAsync();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 1000; i++)
        {
            var result = await _handler.Handle(new RequestPresentationCommand(sigId), CancellationToken.None);
            var state = ExtractState(result.QrPayload);

            Assert.True(seen.Add(state), $"Duplicate state emitted at iteration {i}");
            // Each base64url char encodes 6 bits; ≥16 chars ⇒ ≥96 bits.
            Assert.True(state.Length * 6 >= 96,
                $"State entropy too low: {state.Length} chars × 6 bits = {state.Length * 6} bits");
            // Sanity: state is base64url alphabet only.
            foreach (var c in state)
            {
                Assert.True(
                    (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '-' || c == '_',
                    $"Non-base64url char '{c}' in state");
            }
        }

        Assert.Equal(1000, seen.Count);
    }

    private static string ExtractState(string qrPayload)
    {
        // qrPayload is openid4vp://?...&state=<value>&...
        var idx = qrPayload.IndexOf("&state=", StringComparison.Ordinal);
        Assert.True(idx >= 0, "QR payload has no state parameter");
        var rest = qrPayload[(idx + "&state=".Length)..];
        var end = rest.IndexOf('&');
        var encoded = end >= 0 ? rest[..end] : rest;
        return Uri.UnescapeDataString(encoded);
    }

    public void Dispose() => _db.Dispose();
}
