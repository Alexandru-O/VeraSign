using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// ---------------------------------------------------------------------------
// MasterSTI.Mock.Qtsp — pure CSC API v2 mock QTSP (ADR-0005).
//
// Attests *a signature* (CSC v2 /credentials/* and /signatures/signHash).
// The EUDIW Issuer role (PID issuance, simulator endpoints, issuer key)
// lives in MasterSTI.Mock.Issuer and is no longer hosted here.
// ---------------------------------------------------------------------------

// RSA 2048 key for the mock signing certificate, persisted across restarts
// so the self-signed cert stays stable across runs. Used both as the credential
// key surfaced via /csc/v2/credentials/info and as the signing key for
// /csc/v2/signatures/signHash. File is local to the run folder, never checked in.
var signingKeyPath = Path.Combine(AppContext.BaseDirectory, "qtsp-signing-key.pem");
var rsa = RSA.Create();
if (File.Exists(signingKeyPath))
{
    rsa.ImportFromPem(File.ReadAllText(signingKeyPath));
}
else
{
    rsa.Dispose();
    rsa = RSA.Create(2048);
    File.WriteAllText(signingKeyPath, rsa.ExportPkcs8PrivateKeyPem());
}

X509Certificate2 MintCert(string cn)
{
    var req = new CertificateRequest(
        $"CN={cn},O=Mock QTSP SA,C=EU",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    req.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature, true));
    return req.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddYears(2));
}

var defaultCert = MintCert("MockQtsp");
var certCache = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase)
{
    ["MockQtsp"] = defaultCert
};
var certCacheLock = new object();

X509Certificate2 ResolveCert(HttpRequest httpReq)
{
    var cn = httpReq.Headers["X-Mock-Signer-Cn"].FirstOrDefault()?.Trim();
    if (string.IsNullOrWhiteSpace(cn)) return defaultCert;
    lock (certCacheLock)
    {
        if (!certCache.TryGetValue(cn, out var c))
        {
            c = MintCert(cn);
            certCache[cn] = c;
        }
        return c;
    }
}

const string MockCredentialId = "mock-credential-001";
const string MockAccessToken = "mock-access-token-abc123";
const string MockSadToken = "mock-sad-token-xyz789";

// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// Bind only to localhost by default — this is a TEST ONLY service. When
// ASPNETCORE_URLS is set (e.g. inside Docker we pass http://+:8080), respect it.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("https://localhost:7111");
}

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

// Liveness probe for container orchestration (docker-compose healthcheck).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Infra");

// ---------------------------------------------------------------------------
// CSC v2 endpoints
// ---------------------------------------------------------------------------

app.MapPost("/csc/v2/auth/login", (LoginRequest req) => Results.Ok(new
{
    access_token = MockAccessToken,
    token_type = "Bearer",
    expires_in = 3600
})).WithTags("CSC v2");

app.MapPost("/csc/v2/credentials/list", () => Results.Ok(new
{
    credentialIDs = new[] { MockCredentialId }
})).WithTags("CSC v2");

app.MapPost("/csc/v2/credentials/info", (CredentialsInfoRequest req, HttpRequest http) =>
{
    var c = ResolveCert(http);
    return Results.Ok(new
    {
        description = "Mock signing certificate — TEST ONLY",
        key = new { status = "enabled", algo = new[] { "1.2.840.113549.1.1.11" }, len = 2048 },
        cert = new
        {
            status = "valid",
            certificates = new[] { Convert.ToBase64String(c.Export(X509ContentType.Cert)) },
            issuerDN = c.Issuer,
            serialNumber = c.SerialNumber,
            subjectDN = c.Subject,
            validFrom = c.NotBefore.ToString("yyyyMMddHHmmss") + "Z",
            validTo = c.NotAfter.ToString("yyyyMMddHHmmss") + "Z"
        },
        authMode = "explicit",
        multisign = 1,
        lang = "en-US"
    });
}).WithTags("CSC v2");

app.MapPost("/csc/v2/credentials/authorize", (CredentialsAuthorizeRequest req) => Results.Ok(new
{
    SAD = MockSadToken,
    expiresIn = 3600
})).WithTags("CSC v2");

app.MapPost("/csc/v2/signatures/signHash", (SignHashRequest req) =>
{
    // ADR-0010: prefer CSC v2 singular `hash` field; fall back to legacy `hashes`.
    var hashes = req.hash ?? req.hashes ?? Array.Empty<string>();
    var signatures = hashes.Select(hashBase64 =>
    {
        byte[] hashBytes;
        try { hashBytes = Convert.FromBase64String(hashBase64); }
        catch { hashBytes = Convert.FromHexString(hashBase64); }

        var signature = rsa.SignHash(hashBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }).ToArray();

    return Results.Ok(new { signatures });
}).WithTags("CSC v2");

app.Run();

// ---------------------------------------------------------------------------
record LoginRequest(string? username, string? password, string? grant_type);
record CredentialsInfoRequest(string credentialID, string? certificates, bool? certInfo, bool? authInfo, bool? info, string? lang);
// ADR-0010: accept both legacy (PIN scalar + plural `hashes`) and CSC v2 §11.5
// (authData[] + singular `hash` + hashAlgorithmOID) wire shapes. Existing
// CscApiClientTests fixtures post the legacy shape; live API/Wallet calls post v2.
record CredentialsAuthorizeRequest(
    string credentialID,
    int numSignatures,
    string[]? hashes,
    string[]? hash,
    string? hashAlgorithmOID,
    AuthDataEntry[]? authData,
    string? PIN,
    string? OTP,
    string? description);
record AuthDataEntry(string id, string value);
record SignHashRequest(
    string credentialID,
    string SAD,
    string[]? hashes,
    string[]? hash,
    string? hashAlgo,
    string? hashAlgorithmOID,
    string? signAlgo,
    string? signAlgoParams,
    string? operationMode,
    int? validity_period,
    string? response_uri);
