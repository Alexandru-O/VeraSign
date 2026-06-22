using System.Net.Http.Headers;

namespace MasterSTI.Web.Services;

/// <summary>
/// DelegatingHandler that attaches the JWT held by <see cref="IAuthService"/> to outgoing
/// API requests, unless the caller has already set an Authorization header for that
/// request. Lives on the named <c>MasterSTI.Api</c> HttpClient.
/// </summary>
public sealed class ApiBearerHandler : DelegatingHandler
{
    private readonly IAuthService _auth;

    public ApiBearerHandler(IAuthService auth)
    {
        _auth = auth;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = _auth.Token;
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
