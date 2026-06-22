using System.Net;
using MasterSTI.Wallet.Services;

namespace MasterSTI.UnitTests.Wallet;

/// <summary>
/// SignResultMapper is the single place that decides which <see cref="SignErrorKind"/>
/// surfaces for each HTTP / exception condition. PinPage's lockout counter only
/// increments on <see cref="SignErrorKind.PinRejected"/>, so the mapping is
/// security-relevant — a mis-mapping (e.g. classifying QTSP 5xx as PinRejected)
/// would let transport jitter lock the user out.
/// </summary>
public class SignResultMapperTests
{
    [Fact]
    public void MapHttp_Success_ReturnsOk()
    {
        var body = new SignedDocResponse(Guid.NewGuid(), "PAdES-B-LT");

        var result = SignResultMapper.MapHttp(HttpStatusCode.OK, body);

        Assert.True(result.Ok);
        Assert.Equal(body.SignedDocumentId, result.SignedDocumentId);
        Assert.Equal("PAdES-B-LT", result.PadesLevel);
        Assert.Null(result.Error);
    }

    [Fact]
    public void MapHttp_SuccessWithNullBody_ReturnsServerError()
    {
        var result = SignResultMapper.MapHttp(HttpStatusCode.OK, body: null);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal(SignErrorKind.Server, result.Error!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void MapHttp_AuthFailure_ReturnsPinRejected(HttpStatusCode status)
    {
        var result = SignResultMapper.MapHttp(status, body: null);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal(SignErrorKind.PinRejected, result.Error!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void MapHttp_5xx_ReturnsServer(HttpStatusCode status)
    {
        var result = SignResultMapper.MapHttp(status, body: null);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal(SignErrorKind.Server, result.Error!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public void MapHttp_OtherNon2xx_ReturnsQtspError(HttpStatusCode status)
    {
        // Anything that isn't 2xx, 401/403, or 5xx falls to QtspError — covers
        // 400 (server-rejected payload, e.g. SigningRequest in wrong state),
        // 404 (id mismatch), 409 (chain conflict). None should burn a PIN attempt.
        var result = SignResultMapper.MapHttp(status, body: null);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal(SignErrorKind.QtspError, result.Error!.Kind);
    }

    [Fact]
    public void MapException_HttpRequestException_ReturnsNetwork()
    {
        var ex = new HttpRequestException("Connection refused");

        var result = SignResultMapper.MapException(ex);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal(SignErrorKind.Network, result.Error!.Kind);
    }

    [Fact]
    public void MapException_TaskCanceled_ReturnsNetwork()
    {
        // HttpClient surfaces a TaskCanceledException for timeout; the wallet
        // treats it as a Network failure (transport-level), not a server error.
        var ex = new TaskCanceledException("Request timed out");

        var result = SignResultMapper.MapException(ex);

        Assert.False(result.Ok);
        Assert.Equal(SignErrorKind.Network, result.Error!.Kind);
    }

    [Fact]
    public void MapException_GenericException_ReturnsQtspError()
    {
        // Anything else (JsonException, InvalidOperationException, etc.) is
        // bucketed as QtspError. Doesn't burn PIN attempts.
        var ex = new InvalidOperationException("malformed JSON");

        var result = SignResultMapper.MapException(ex);

        Assert.False(result.Ok);
        Assert.Equal(SignErrorKind.QtspError, result.Error!.Kind);
    }
}
