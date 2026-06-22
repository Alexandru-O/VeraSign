using MasterSTI.Web;
using MasterSTI.Web.Components;
using MasterSTI.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Singleton auth (demo only — survives HTTP/circuit boundaries). Real auth would use cookies.
builder.Services.AddSingleton<IAuthService, ApiAuthService>();

// Per-circuit page header state (each page sets title/breadcrumb via <PageHeader/>).
builder.Services.AddScoped<MasterSTI.Web.Components.Layout.PageContext>();

// Per-circuit in-memory i18n (demo scope — sidebar + topbar + a few labels).
builder.Services.AddScoped<LanguageService>();

var apiBase = builder.Configuration.GetValue<string>("ApiBaseUrl") ?? "https://localhost:7001";
// Mock.Issuer hosts the browser-based EUDIW wallet simulators (ADR-0005).
var issuerBase = builder.Configuration.GetValue<string>("MockIssuerBaseUrl") ?? "https://localhost:7112";
// Browser-facing Issuer URL (for the EU Wallet simulator link). Defaults to the internal value
// but can be overridden in container deployments where the internal hostname is unreachable from the browser.
var issuerPublicBase = builder.Configuration.GetValue<string>("MockIssuerPublicBaseUrl") ?? issuerBase;

builder.Services.AddTransient<ApiBearerHandler>();
builder.Services.AddHttpClient("MasterSTI.Api", client => client.BaseAddress = new Uri(apiBase))
    .AddHttpMessageHandler<ApiBearerHandler>();
builder.Services.AddHttpClient("MasterSTI.Mock.Issuer", client => client.BaseAddress = new Uri(issuerBase));

builder.Services.AddSingleton(new UiConfig(apiBase, issuerBase, issuerPublicBase));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Skip HTTPS redirect when no HTTPS listener is configured (docker compose
// runs plain HTTP on :8080). In that case UseHttpsRedirection can't determine
// the target port, logs WARN, and — in .NET 10 — terminates the connection,
// which surfaces as "Empty reply from server" on every request.
var webUrls = app.Configuration["ASPNETCORE_URLS"] ?? string.Empty;
if (webUrls.Contains("https://", StringComparison.OrdinalIgnoreCase))
    app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// Same-origin PDF proxy for iframe rendering.
// Browser iframes can't carry the JWT Bearer; routing through Web (which holds
// the singleton IAuthService) lets ApiBearerHandler stamp Authorization before
// the request reaches the API. Token stays server-side — never on the URL.
static async Task ProxyPdfAsync(HttpResponse response, IHttpClientFactory factory, string upstreamPath, CancellationToken cancellationToken)
{
    var client = factory.CreateClient("MasterSTI.Api");
    using var upstream = await client.GetAsync(upstreamPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    response.StatusCode = (int)upstream.StatusCode;
    if (!upstream.IsSuccessStatusCode)
        return;
    response.ContentType = "application/pdf";
    response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    await using var stream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
    await stream.CopyToAsync(response.Body, cancellationToken);
}

app.MapGet("/pdf/documents/{docId:guid}", (Guid docId, IHttpClientFactory factory, HttpResponse response, CancellationToken cancellationToken)
    => ProxyPdfAsync(response, factory, $"/api/documents/{docId}/render", cancellationToken));

app.MapGet("/pdf/templates/{tplId:guid}", (Guid tplId, string? v, IHttpClientFactory factory, HttpResponse response, CancellationToken cancellationToken)
    => ProxyPdfAsync(response, factory, $"/api/templates/{tplId}/pdf", cancellationToken));

app.MapGet("/pdf/signed/{signedId:guid}", (Guid signedId, IHttpClientFactory factory, HttpResponse response, CancellationToken cancellationToken)
    => ProxyPdfAsync(response, factory, $"/api/signed-documents/{signedId}/download", cancellationToken));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

