using MasterSTI.Api.Common.Trust;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

/// <summary>
/// Locks in the EU Trust List ingestion + matching behaviour. The bundled
/// <c>Common/Trust/trust-list.json</c> is copied to the test output by the API project
/// reference; if the file is missing the provider returns an empty snapshot rather than throwing.
/// </summary>
public class TrustListProviderTests
{
    [Fact]
    public void BundledSnapshot_LoadsAtLeastTenTsps()
    {
        var provider = new TrustListProvider(NullLogger<TrustListProvider>.Instance);

        Assert.NotNull(provider.Snapshot);
        Assert.True(
            provider.Snapshot.Tsps.Count >= 10,
            $"Expected curated EUTL subset to have ≥10 TSPs but found {provider.Snapshot.Tsps.Count}");
        Assert.Contains("ec.europa.eu", provider.Snapshot.Source);
    }

    [Theory]
    [InlineData("CN=certSIGN Qualified CA Class 3 G2, O=certSIGN S.A., C=RO", "certSIGN S.A.", "RO")]
    [InlineData("CN=DigiSign Qualified CA Class 3 G3, O=DIGISIGN S.A., C=RO", "DIGISIGN S.A.", "RO")]
    [InlineData("CN=D-TRUST CA 2-1 2024, O=D-Trust GmbH, C=DE", "D-Trust GmbH", "DE")]
    public void Match_KnownIssuerDn_ReturnsTrustedHit(string issuerDn, string expectedTsp, string expectedCountry)
    {
        var provider = new TrustListProvider(NullLogger<TrustListProvider>.Instance);

        var match = provider.Match(issuerDn);

        Assert.True(match.IsTrusted);
        Assert.Equal(expectedTsp, match.TspName);
        Assert.Equal(expectedCountry, match.Country);
    }

    [Theory]
    [InlineData("CN=Some Random Self-Signed Cert, O=Acme Inc, C=US")]
    [InlineData("CN=Self-Test, O=local-dev")]
    [InlineData("")]
    [InlineData(null)]
    public void Match_UnknownOrEmptyIssuer_ReturnsNoTrust(string? issuerDn)
    {
        var provider = new TrustListProvider(NullLogger<TrustListProvider>.Instance);

        var match = provider.Match(issuerDn);

        Assert.False(match.IsTrusted);
        Assert.Null(match.TspName);
        Assert.Null(match.Country);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var provider = new TrustListProvider(NullLogger<TrustListProvider>.Instance);

        var lower = provider.Match("cn=cersign qualified, o=certsign s.a., c=ro");
        var upper = provider.Match("CN=CERTSIGN QUALIFIED, O=CERTSIGN S.A., C=RO");

        Assert.True(lower.IsTrusted);
        Assert.True(upper.IsTrusted);
        Assert.Equal(lower.TspName, upper.TspName);
    }
}
