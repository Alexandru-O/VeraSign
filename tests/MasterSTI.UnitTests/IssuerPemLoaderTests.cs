using System.Net;
using System.Security.Cryptography;
using System.Text;
using MasterSTI.Api.Common.Eudiw;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MasterSTI.UnitTests;

/// <summary>
/// Locks in startup-time pin enforcement: the loader must reject a PEM whose
/// SHA-256 does not match the configured <c>IssuerPublicKeyPemSha256</c>, and
/// must NOT silently accept an unpinned PEM (logs a Critical instead).
/// </summary>
public sealed class IssuerPemLoaderTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private string Pem => _rsa.ExportSubjectPublicKeyInfoPem();
    private string PemSha256 => IssuerPemLoader.ComputePemSha256Hex(Pem);

    [Fact]
    public async Task StartAsync_InlinePemMatchesPin_PopulatesHolder()
    {
        var opts = new EudiwOptions
        {
            IssuerPublicKeyPem = Pem,
            IssuerPublicKeyPemSha256 = PemSha256
        };
        var holder = new IssuerKeyHolder();
        var loader = new IssuerPemLoader(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            holder,
            NullLogger<IssuerPemLoader>.Instance);

        await loader.StartAsync(CancellationToken.None);

        Assert.NotNull(holder.Current);
        Assert.Equal(PemSha256, holder.CurrentPemSha256);
    }

    [Fact]
    public async Task StartAsync_InlinePemPinMismatch_Throws()
    {
        var opts = new EudiwOptions
        {
            IssuerPublicKeyPem = Pem,
            // Pin is a syntactically-valid SHA-256 hex but belongs to a different PEM.
            IssuerPublicKeyPemSha256 = new string('0', 64)
        };
        var holder = new IssuerKeyHolder();
        var loader = new IssuerPemLoader(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            holder,
            NullLogger<IssuerPemLoader>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.StartAsync(CancellationToken.None));

        Assert.Contains("pin", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(holder.Current);
    }

    [Fact]
    public async Task StartAsync_PinMissing_LogsCriticalAndAcceptsButFlags()
    {
        var opts = new EudiwOptions
        {
            IssuerPublicKeyPem = Pem,
            IssuerPublicKeyPemSha256 = null
        };
        var holder = new IssuerKeyHolder();
        var capturing = new CapturingLogger<IssuerPemLoader>();
        var loader = new IssuerPemLoader(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            holder,
            capturing);

        await loader.StartAsync(CancellationToken.None);

        // Key is still loaded (loud TOFU rather than silent rejection)…
        Assert.NotNull(holder.Current);
        // …but a Critical entry must surface the observed hash so an operator
        // can copy it back into config to switch to enforcement.
        Assert.Contains(
            capturing.Entries,
            e => e.Level == Microsoft.Extensions.Logging.LogLevel.Critical
                 && e.Message.Contains(PemSha256, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartAsync_NoPemConfigured_LeavesHolderEmpty()
    {
        var opts = new EudiwOptions
        {
            IssuerPublicKeyPem = null,
            IssuerPublicKeyPemUrl = null,
            IssuerPublicKeyPemSha256 = null
        };
        var holder = new IssuerKeyHolder();
        var loader = new IssuerPemLoader(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            holder,
            NullLogger<IssuerPemLoader>.Instance);

        await loader.StartAsync(CancellationToken.None);

        Assert.Null(holder.Current);
    }

    [Fact]
    public async Task StartAsync_FetchedPemMatchesPin_PopulatesHolder()
    {
        var pem = Pem;
        var pin = IssuerPemLoader.ComputePemSha256Hex(pem);

        var opts = new EudiwOptions
        {
            IssuerPublicKeyPem = null,
            IssuerPublicKeyPemUrl = "https://issuer.test/pem",
            IssuerPublicKeyPemSha256 = pin
        };

        var holder = new IssuerKeyHolder();
        var factory = new StubHttpClientFactory(pem);
        var loader = new IssuerPemLoader(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            holder,
            NullLogger<IssuerPemLoader>.Instance,
            factory);

        await loader.StartAsync(CancellationToken.None);

        Assert.NotNull(holder.Current);
        Assert.Equal(pin, holder.CurrentPemSha256);
    }

    [Fact]
    public async Task StartAsync_FetchedPemPinMismatch_Throws()
    {
        // Issuer endpoint serves a PEM, but the deployer pinned a different hash.
        // This is the impersonation-defence scenario the pin exists for.
        using var attackerRsa = RSA.Create(2048);
        var servedPem = attackerRsa.ExportSubjectPublicKeyInfoPem();
        var expectedPin = IssuerPemLoader.ComputePemSha256Hex(Pem); // legitimate key's pin

        var opts = new EudiwOptions
        {
            IssuerPublicKeyPem = null,
            IssuerPublicKeyPemUrl = "https://issuer.test/pem",
            IssuerPublicKeyPemSha256 = expectedPin
        };

        var holder = new IssuerKeyHolder();
        var factory = new StubHttpClientFactory(servedPem);
        var loader = new IssuerPemLoader(
            new StaticOptionsMonitor<EudiwOptions>(opts),
            holder,
            NullLogger<IssuerPemLoader>.Instance,
            factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.StartAsync(CancellationToken.None));

        Assert.Null(holder.Current);
    }

    [Fact]
    public void ComputePemSha256Hex_IsLineEndingInsensitive()
    {
        // The pin must survive CRLF/LF normalisation so configs work the same on
        // Windows and Linux without operator-visible diff.
        var crlf = Pem.Replace("\n", "\r\n");
        var lf = Pem.Replace("\r\n", "\n");

        Assert.Equal(
            IssuerPemLoader.ComputePemSha256Hex(crlf),
            IssuerPemLoader.ComputePemSha256Hex(lf));
    }

    public void Dispose() => _rsa.Dispose();

    // --- helpers ----------------------------------------------------------

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly string _body;
        public StubHttpClientFactory(string body) => _body = body;

        public HttpClient CreateClient(string name)
            => new(new StubHandler(_body)) { Timeout = TimeSpan.FromSeconds(2) };

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _body;
            public StubHandler(string body) => _body = body;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/x-pem-file")
                });
        }
    }
}
