using MasterSTI.Wallet.Services;
using NSubstitute;

namespace MasterSTI.UnitTests.Wallet;

/// <summary>
/// The orchestrator is the testable seam ConsentPage + PinPage call into.
/// Verifies the Factor marker on the wire, the Prepare-failure short-circuit,
/// and the propagation of each <see cref="SignErrorKind"/> back to the page.
/// </summary>
public class WalletSigningOrchestratorTests
{
    private static (WalletSigningOrchestrator orch, IWalletApiClient api) Build()
    {
        var api = Substitute.For<IWalletApiClient>();
        return (new WalletSigningOrchestrator(api), api);
    }

    [Fact]
    public async Task SignWithBiometric_HappyPath_SendsBiometricMarker()
    {
        var (orch, api) = Build();
        var docId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var sigReqId = Guid.NewGuid();
        var signedDocId = Guid.NewGuid();

        api.PrepareSigningAsync(docId, recipientId, Arg.Any<RenderCommitmentDto?>(), Arg.Any<CancellationToken>())
            .Returns(new PrepareResult(sigReqId, "deadbeef"));
        api.SignAsync(sigReqId, "bio-attested", "biometric", Arg.Any<CancellationToken>())
            .Returns(SignResult.Success(signedDocId, "PAdES-B-LT"));

        var result = await orch.SignWithBiometricAsync(docId, recipientId);

        Assert.False(result.PrepareFailed);
        Assert.Equal(sigReqId, result.SigningRequestId);
        Assert.Equal("deadbeef", result.DocumentHashHex);
        Assert.NotNull(result.SignResult);
        Assert.True(result.SignResult!.Ok);
        Assert.Equal(signedDocId, result.SignResult.SignedDocumentId);

        // Wire contract: biometric path MUST send "bio-attested" + factor "biometric".
        // Audit + ADR-0007 depend on this.
        await api.Received(1).SignAsync(sigReqId, "bio-attested", "biometric", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignWithPin_HappyPath_SendsPinDigitsAndPinFactor()
    {
        var (orch, api) = Build();
        var docId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var sigReqId = Guid.NewGuid();
        var signedDocId = Guid.NewGuid();

        api.PrepareSigningAsync(docId, recipientId, Arg.Any<RenderCommitmentDto?>(), Arg.Any<CancellationToken>())
            .Returns(new PrepareResult(sigReqId, "cafef00d"));
        api.SignAsync(sigReqId, "123456", "pin", Arg.Any<CancellationToken>())
            .Returns(SignResult.Success(signedDocId, "PAdES-B-LT"));

        var result = await orch.SignWithPinAsync(docId, recipientId, "123456");

        Assert.True(result.SignResult!.Ok);
        await api.Received(1).SignAsync(sigReqId, "123456", "pin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrepareReturnsNull_ShortCircuits_NeverCallsSign()
    {
        var (orch, api) = Build();
        api.PrepareSigningAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<RenderCommitmentDto?>(), Arg.Any<CancellationToken>())
            .Returns((PrepareResult?)null);

        var result = await orch.SignWithBiometricAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.PrepareFailed);
        Assert.Null(result.SigningRequestId);
        Assert.Null(result.SignResult);
        await api.DidNotReceive().SignAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SignErrorKind.PinRejected)]
    [InlineData(SignErrorKind.QtspError)]
    [InlineData(SignErrorKind.Network)]
    [InlineData(SignErrorKind.Server)]
    public async Task SignFailure_Propagates_ErrorKind(SignErrorKind kind)
    {
        var (orch, api) = Build();
        var sigReqId = Guid.NewGuid();
        api.PrepareSigningAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<RenderCommitmentDto?>(), Arg.Any<CancellationToken>())
            .Returns(new PrepareResult(sigReqId, "h"));
        api.SignAsync(sigReqId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SignResult.Failure(kind, "test"));

        var result = await orch.SignWithPinAsync(Guid.NewGuid(), Guid.NewGuid(), "111111");

        Assert.False(result.PrepareFailed); // Prepare succeeded — failure is from Sign.
        Assert.NotNull(result.SignResult);
        Assert.False(result.SignResult!.Ok);
        Assert.Equal(kind, result.SignResult.Error!.Kind);
    }

    [Fact]
    public async Task Cancellation_Flows_Through()
    {
        var (orch, api) = Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        api.PrepareSigningAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<RenderCommitmentDto?>(), Arg.Any<CancellationToken>())
            .Returns<PrepareResult?>(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            orch.SignWithBiometricAsync(Guid.NewGuid(), Guid.NewGuid(), cts.Token));
    }

    // ADR-0008: when the Review-page prefetch populated a Render Commitment for
    // this document, the orchestrator MUST forward it on the Prepare call so the
    // /VeraSign.Render* dictionary keys land inside the signed ByteRange.
    [Fact]
    public async Task SignWithBiometric_ForwardsRenderCommitment_FromCarrier()
    {
        var api = Substitute.For<IWalletApiClient>();
        var carrier = new RenderCommitmentCarrier();
        var docId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var sigReqId = Guid.NewGuid();

        var commitment = new RenderCommitmentDto(
            Profile: "PdfiumPinned-v1", Algo: "SHA-256", Dpi: 150, PageCount: 3,
            Locale: "ro-RO",
            RootHex: "1c8255a7d1db21c4e9a140a1d8068dcd02d594977a188e2f38e33b734a6bee96",
            PdfiumBinarySha256: "8f67fac92554e4a6ab57f7d4f6a3d6974b1646373e0d314d90694738941c040c");
        carrier.Set(docId, commitment);

        api.PrepareSigningAsync(docId, recipientId, commitment, Arg.Any<CancellationToken>())
            .Returns(new PrepareResult(sigReqId, "deadbeef"));
        api.SignAsync(sigReqId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SignResult.Success(Guid.NewGuid(), "PAdES-B-LT"));

        var orch = new WalletSigningOrchestrator(api, carrier);
        var result = await orch.SignWithBiometricAsync(docId, recipientId);

        Assert.True(result.SignResult!.Ok);
        await api.Received(1).PrepareSigningAsync(
            docId, recipientId,
            Arg.Is<RenderCommitmentDto?>(c => c != null && c.RootHex == commitment.RootHex),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignWithBiometric_PassesNullCommitment_WhenCarrierEmpty()
    {
        var api = Substitute.For<IWalletApiClient>();
        var carrier = new RenderCommitmentCarrier();
        var docId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();

        api.PrepareSigningAsync(docId, recipientId, null, Arg.Any<CancellationToken>())
            .Returns(new PrepareResult(Guid.NewGuid(), "h"));
        api.SignAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SignResult.Success(Guid.NewGuid(), "PAdES-B-LT"));

        var orch = new WalletSigningOrchestrator(api, carrier);
        await orch.SignWithBiometricAsync(docId, recipientId);

        await api.Received(1).PrepareSigningAsync(
            docId, recipientId, null, Arg.Any<CancellationToken>());
    }
}

public class RenderCommitmentCarrierTests
{
    private static RenderCommitmentDto Sample(string root = "abc") => new(
        Profile: "PdfiumPinned-v1", Algo: "SHA-256", Dpi: 150, PageCount: 1,
        Locale: "ro-RO", RootHex: root, PdfiumBinarySha256: "x");

    [Fact]
    public void Get_ReturnsNull_BeforeSet()
    {
        var carrier = new RenderCommitmentCarrier();
        Assert.Null(carrier.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Set_Then_Get_RoundTrips_PerDocument()
    {
        var carrier = new RenderCommitmentCarrier();
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        carrier.Set(docA, Sample("aaa"));
        carrier.Set(docB, Sample("bbb"));

        Assert.Equal("aaa", carrier.Get(docA)!.RootHex);
        Assert.Equal("bbb", carrier.Get(docB)!.RootHex);
    }

    [Fact]
    public void Set_TwiceForSameDoc_OverwritesLastValueWins()
    {
        var carrier = new RenderCommitmentCarrier();
        var doc = Guid.NewGuid();
        carrier.Set(doc, Sample("first"));
        carrier.Set(doc, Sample("second"));
        Assert.Equal("second", carrier.Get(doc)!.RootHex);
    }

    [Fact]
    public void Clear_RemovesOnlyTargetedDoc()
    {
        var carrier = new RenderCommitmentCarrier();
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        carrier.Set(docA, Sample("aaa"));
        carrier.Set(docB, Sample("bbb"));
        carrier.Clear(docA);
        Assert.Null(carrier.Get(docA));
        Assert.NotNull(carrier.Get(docB));
    }
}
