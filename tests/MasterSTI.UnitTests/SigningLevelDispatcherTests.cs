using MasterSTI.Api.Common.Signing;
using MasterSTI.Shared.Enums;

namespace MasterSTI.UnitTests;

public class SigningLevelDispatcherTests
{
    private static SigningLevelDispatcher CreateDispatcher() => new(new CscQesSigner());

    [Fact]
    public void Resolve_QesCsc_ReturnsCscSigner()
    {
        var d = CreateDispatcher();
        var signer = d.Resolve(SigningLevel.QES_CSC);
        Assert.IsType<CscQesSigner>(signer);
        Assert.Equal(SigningLevel.QES_CSC, signer.Level);
    }

    [Fact]
    public void Resolve_AdesWallet_Throws()
    {
        var d = CreateDispatcher();
        var ex = Assert.Throws<NotImplementedException>(() => d.Resolve(SigningLevel.AdES_Wallet));
        Assert.Contains("AdES_Wallet", ex.Message);
    }

    [Fact]
    public void Resolve_Ses_Throws()
    {
        var d = CreateDispatcher();
        Assert.Throws<NotImplementedException>(() => d.Resolve(SigningLevel.SES));
    }

    [Theory]
    [InlineData("QES")]
    [InlineData("QES_CSC")]
    public void Resolve_LegacyQesString_RoutesToCsc(string input)
    {
        var d = CreateDispatcher();
        var signer = d.Resolve(input);
        Assert.IsType<CscQesSigner>(signer);
    }

    [Theory]
    [InlineData("AdES")]
    [InlineData("AdES_Wallet")]
    public void Resolve_LegacyAdesString_Throws(string input)
    {
        var d = CreateDispatcher();
        Assert.Throws<NotImplementedException>(() => d.Resolve(input));
    }

    [Fact]
    public void Parse_UnknownLevel_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ISigningLevelDispatcher.Parse("bogus"));
    }

    [Theory]
    [InlineData("QES", SigningLevel.QES_CSC)]
    [InlineData("AdES", SigningLevel.AdES_Wallet)]
    [InlineData("SES", SigningLevel.SES)]
    [InlineData("QES_CSC", SigningLevel.QES_CSC)]
    [InlineData("AdES_Wallet", SigningLevel.AdES_Wallet)]
    public void Parse_NormalisesLegacyStrings(string input, SigningLevel expected)
    {
        Assert.Equal(expected, ISigningLevelDispatcher.Parse(input));
    }
}
