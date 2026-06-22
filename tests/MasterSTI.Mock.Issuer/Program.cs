using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MasterSTI.Mock.Issuer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// ---------------------------------------------------------------------------
// MasterSTI.Mock.Issuer — standalone mock EUDIW Issuer (ADR-0005).
//
// Attests *identity*: mints PID SD-JWTs for identities present in its own
// registry database (MasterSTI_Issuer). Distinct from Mock.Qtsp, which attests
// *a signature* (CSC API v2). VeraSign is the Relying Party that consumes both.
// ---------------------------------------------------------------------------

// Issuer RS256 key — persisted across restarts so a wallet's stored SD-JWT keeps
// validating. Without this, every restart rotates the key and strands enrolled
// wallets at SD-JWT validation. File is local to the run folder, never checked in.
var issuerKeyPath = Path.Combine(AppContext.BaseDirectory, "issuer-key.pem");
var rsa = RSA.Create();
if (File.Exists(issuerKeyPath))
{
    rsa.ImportFromPem(File.ReadAllText(issuerKeyPath));
}
else
{
    rsa.Dispose();
    rsa = RSA.Create(2048);
    File.WriteAllText(issuerKeyPath, rsa.ExportPkcs8PrivateKeyPem());
}
var issuerPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

var builder = WebApplication.CreateBuilder(args);

// Bind localhost-only by default — TEST ONLY service. ASPNETCORE_URLS (Docker / -Public) wins.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("https://localhost:7112");

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddDbContext<IssuerDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Issuer")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

// Apply migrations on startup — the registry + IssuedCredential log are created
// and the Demo Personas seeded here. VeraSign API has no connection string to this DB.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IssuerDbContext>();
    db.Database.Migrate();
}

// ---------------------------------------------------------------------------
// Issuer public key — the Relying Party (VeraSign API) fetches this to verify
// SD-JWT signatures. Repointed there via Eudiw:IssuerPublicKeyPemUrl.
// ---------------------------------------------------------------------------
app.MapGet("/eudiw/issuer-public-key.pem", () =>
    Results.Text(issuerPublicKeyPem, "application/x-pem-file"))
    .WithTags("EUDIW Issuer");

// ---------------------------------------------------------------------------
// Registry-gated PID issuance.
//
// The wallet POSTs its EC P-256 public JWK plus the email of the identity it
// claims. Issuance is gated: the email must resolve to a seeded registry
// Identity, otherwise the request is rejected and no IssuedCredential is
// written. When it resolves, the PID is minted from the registry's canonical
// claims (the registry is the source of truth — request name fields are
// ignored) and an IssuedCredential row is logged.
// ---------------------------------------------------------------------------
app.MapPost("/eudiw/issue-pid", async (IssuePidRequest req, IssuerDbContext db) =>
{
    if (req?.Jwk is null
        || !string.Equals(req.Jwk.Kty, "EC", StringComparison.Ordinal)
        || !string.Equals(req.Jwk.Crv, "P-256", StringComparison.Ordinal)
        || string.IsNullOrEmpty(req.Jwk.X)
        || string.IsNullOrEmpty(req.Jwk.Y))
    {
        return Results.BadRequest(new { error = "jwk must be an EC P-256 public JWK with x and y" });
    }

    if (string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new { error = "email is required to resolve a registry identity" });

    var email = req.Email.Trim().ToLowerInvariant();
    var identity = await db.Identities.FirstOrDefaultAsync(i => i.Email == email);
    if (identity is null)
    {
        return Results.Json(
            new { error = $"identity '{email}' is not in the issuer registry" },
            statusCode: StatusCodes.Status404NotFound);
    }

    var disclosures = BuildPidDisclosures(
        identity.FamilyName, identity.GivenName, identity.BirthDate, identity.Email);

    var cnf = new Dictionary<string, object>
    {
        ["jwk"] = new Dictionary<string, string>
        {
            ["kty"] = req.Jwk.Kty,
            ["crv"] = req.Jwk.Crv,
            ["x"] = req.Jwk.X!,
            ["y"] = req.Jwk.Y!
        }
    };

    var issuedAt = DateTimeOffset.UtcNow;
    // Demo PID lifetime — real EUDIW PIDs are months/years. Previous 1h leftover dev default
    // caused wallets enrolled overnight to fail next-day login with SecurityTokenExpiredException
    // on the issuer JWT (see Inbox "Conexiune eșuată" symptom).
    var expiresAt = issuedAt.AddDays(365);
    var issuerJwt = BuildIssuerJwt(rsa, issuedAt, expiresAt, cnf, disclosures);
    var sdjwt = $"{issuerJwt}~{disclosures[0]}~{disclosures[1]}~{disclosures[2]}~{disclosures[3]}";

    db.IssuedCredentials.Add(new IssuedCredential
    {
        Id = Guid.NewGuid(),
        IdentityId = identity.Id,
        IssuedAt = issuedAt,
        ExpiresAt = expiresAt,
        CnfJwkThumbprint = ComputeJwkThumbprint(req.Jwk)
    });
    await db.SaveChangesAsync();

    return Results.Ok(new { sdjwt, disclosures });
}).WithTags("EUDIW Issuer");

// ---------------------------------------------------------------------------
// Browser-based EUDIW wallet simulators.
//
// These stand in for the MAUI wallet in the web demo flow — they mint SD-JWT
// presentations and POST them to the VeraSign API. They are co-hosted with the
// Issuer purely because they need the issuer signing key; the simulator is a
// wallet stand-in, not an Issuer concern.
//
//   ApiBaseUrl  — server-to-server URL the simulator POSTs the VP response to
//   VerifierId  — `aud` claim baked into the simulator KB-JWT (matches API Eudiw:VerifierId)
// ---------------------------------------------------------------------------

app.MapPost("/simulate/eudiw-response", async (
    EudiwSimulateRequest req,
    IHttpClientFactory httpClientFactory,
    IConfiguration config) =>
{
    var apiBaseUrl = config["ApiBaseUrl"] ?? "https://localhost:7001";
    var nonce = req.Nonce ?? "simulated-nonce";
    var aud = config["VerifierId"] ?? apiBaseUrl;
    var state = req.SigningRequestId.ToString("N");

    var vpToken = BuildSignedSdJwt(rsa, nonce, aud);

    using var client = httpClientFactory.CreateClient();
    client.BaseAddress = new Uri(apiBaseUrl);

    var response = await client.PostAsJsonAsync("/api/eudiw/response", new
    {
        vp_token = vpToken,
        state
    });

    return response.IsSuccessStatusCode
        ? Results.Ok(new { message = "EUDIW response simulated successfully" })
        : Results.BadRequest(new { error = await response.Content.ReadAsStringAsync() });
}).WithTags("Simulator");

// ---------------------------------------------------------------------------
// Desktop login simulator — no phone needed
// ---------------------------------------------------------------------------

app.MapGet("/simulate/eudiw-login", (string? state, string? nonce) =>
{
    var safeState = state ?? string.Empty;
    var safeNonce = nonce ?? string.Empty;
    var encodedState = safeState.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    var encodedNonce = safeNonce.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    var html = $$"""
        <!doctype html>
        <html lang="ro">
        <head>
          <meta charset="utf-8"/>
          <meta name="viewport" content="width=device-width, initial-scale=1"/>
          <title>Simulator EU Wallet - autentificare</title>
          <style>
            body {font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;background:#f0f2f5;}
            .card {background:#fff;border-radius:12px;padding:2rem;max-width:400px;width:100%;box-shadow:0 4px 24px rgba(0,0,0,.1);text-align:center;}
            h1 {font-size:1.25rem;margin:0 0 .5rem;}
            p {color:#555;font-size:.9rem;margin:0 0 1.5rem;}
            button {background:#1a3a6b;color:#fff;border:none;border-radius:8px;padding:.75rem 2rem;font-size:1rem;cursor:pointer;width:100%;}
            button:hover {background:#14305a;}
            .warn {color:#e06c00;font-size:.8rem;margin-top:1rem;}
          </style>
        </head>
        <body>
          <div class="card">
            <h1>Simulator EU Wallet</h1>
            <p>Apasa butonul pentru a prezenta un PID fictiv si a finaliza autentificarea.</p>
            <form method="POST" action="/simulate/eudiw-login">
              <input type="hidden" name="state" value="{{encodedState}}"/>
              <input type="hidden" name="nonce" value="{{encodedNonce}}"/>
              <button type="submit">Trimite PID</button>
            </form>
            <p class="warn">SIMULATOR - nu pentru productie</p>
          </div>
        </body>
        </html>
        """;
    return Results.Content(html, "text/html; charset=utf-8");
}).WithTags("Simulator");

app.MapPost("/simulate/eudiw-login", async (
    HttpContext ctx,
    IHttpClientFactory httpClientFactory,
    IConfiguration config) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var state = form["state"].FirstOrDefault() ?? string.Empty;
    var nonce = form["nonce"].FirstOrDefault() ?? "simulated-nonce";

    var apiBaseUrl = config["ApiBaseUrl"] ?? "https://localhost:7001";
    var aud = config["VerifierId"] ?? apiBaseUrl;

    // Canned PID claims for the login demo persona
    var disclosures = BuildPidDisclosures("Radu", "Andrei", "1990-05-12", "andrei.radu@verasign.demo");

    // Legacy simulator path — no cnf.jwk (issuer-key KB-JWT verification, WARN logged by API)
    var issuerJwt = BuildSimulatorIssuerJwt(rsa, nonce, disclosures);
    var sdHash = ComputeSdHash(issuerJwt, disclosures);
    var kbJwt = BuildKbJwt(rsa, aud, nonce, sdHash);
    var vpToken = $"{issuerJwt}~{disclosures[0]}~{disclosures[1]}~{disclosures[2]}~{disclosures[3]}~{kbJwt}";

    using var client = httpClientFactory.CreateClient();
    client.BaseAddress = new Uri(apiBaseUrl);

    var response = await client.PostAsJsonAsync("/api/eudiw/response", new
    {
        vp_token = vpToken,
        state
    });

    var resultHtml = response.IsSuccessStatusCode
        ? """
          <!doctype html><html lang="ro"><head><meta charset="utf-8"/><title>Autentificat</title>
          <style>body{font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;background:#f0f2f5;}
          .card{background:#fff;border-radius:12px;padding:2rem;max-width:400px;width:100%;box-shadow:0 4px 24px rgba(0,0,0,.1);text-align:center;}
          h1{color:#1a7a4a;font-size:1.25rem;margin:0 0 .5rem;} p{color:#555;font-size:.9rem;}</style></head>
          <body><div class="card"><h1>Autentificat cu succes!</h1><p>Trimis. Te poți întoarce în browser.</p></div></body></html>
          """
        : BuildErrorHtml(await response.Content.ReadAsStringAsync());

    return Results.Content(resultHtml, "text/html; charset=utf-8");
}).WithTags("Simulator");

app.Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static string[] BuildPidDisclosures(string familyName, string givenName, string birthDate, string email)
{
    string Disclosure(string name, string value)
    {
        var salt = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        return Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new object[] { salt, name, value }));
    }

    return
    [
        Disclosure("family_name", familyName),
        Disclosure("given_name", givenName),
        Disclosure("birth_date", birthDate),
        Disclosure("email", email)
    ];
}

static string BuildIssuerJwt(
    RSA signingKey,
    DateTimeOffset issuedAt,
    DateTimeOffset expiresAt,
    IDictionary<string, object> cnf,
    string[] disclosures)
{
    var payload = new Dictionary<string, object>
    {
        ["iss"] = "https://mock-issuer.eudiw.eu",
        ["sub"] = "mock-subject-001",
        ["iat"] = issuedAt.ToUnixTimeSeconds(),
        ["nbf"] = issuedAt.AddMinutes(-1).ToUnixTimeSeconds(),
        ["exp"] = expiresAt.ToUnixTimeSeconds(),
        ["_sd_alg"] = "sha-256",
        ["_sd"] = ComputeSdDigests(disclosures),
        ["cnf"] = cnf
    };
    return EncodeRsaJwt(signingKey, "mock-issuer", payload);
}

// RFC 7638 EC JWK thumbprint — SHA-256 of canonical lex-ordered JSON, base64url.
static string ComputeJwkThumbprint(IssuePidJwk jwk)
{
    var canonical = $"{{\"crv\":\"{jwk.Crv}\",\"kty\":\"EC\",\"x\":\"{jwk.X}\",\"y\":\"{jwk.Y}\"}}";
    return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
}

static string Base64UrlEncode(byte[] bytes)
    => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

// ---------------------------------------------------------------------------
// Simulator helpers — used only by the browser-based wallet stand-ins below.
// The simulator SD-JWT carries NO cnf.jwk (verifier falls back to issuer-key
// verification on the KB-JWT — see SdJwtValidator).
// ---------------------------------------------------------------------------

static string BuildSignedSdJwt(RSA signingKey, string nonce, string aud)
{
    var disclosures = BuildPidDisclosures("Popescu", "Ion", "1990-01-15", "ion.popescu@verasign.demo");

    var issuerJwt = BuildSimulatorIssuerJwt(signingKey, nonce, disclosures);
    var sdHash = ComputeSdHash(issuerJwt, disclosures);
    var kbJwt = BuildKbJwt(signingKey, aud, nonce, sdHash);

    return $"{issuerJwt}~{disclosures[0]}~{disclosures[1]}~{disclosures[2]}~{disclosures[3]}~{kbJwt}";
}

static string BuildSimulatorIssuerJwt(RSA signingKey, string nonce, string[] disclosures)
{
    var now = DateTimeOffset.UtcNow;
    var payload = new Dictionary<string, object>
    {
        ["iss"] = "https://mock-issuer.eudiw.eu",
        ["sub"] = "mock-subject-001",
        ["iat"] = now.ToUnixTimeSeconds(),
        ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
        ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
        ["_sd_alg"] = "sha-256",
        ["_sd"] = ComputeSdDigests(disclosures),
        ["nonce"] = nonce
    };
    return EncodeRsaJwt(signingKey, "mock-issuer", payload);
}

static string[] ComputeSdDigests(string[] disclosures)
{
    var digests = new string[disclosures.Length];
    for (var i = 0; i < disclosures.Length; i++)
        digests[i] = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(disclosures[i])));
    return digests;
}

// JwtSecurityToken's claim-list ctor flattens JSON-array claims back to individual
// string entries, so a top-level `_sd` set would emit as repeated string properties
// instead of an array. Hand-crafting the payload keeps the SD-JWT shape that
// production validators (and real wallets) expect.
static string EncodeRsaJwt(RSA signingKey, string kid, Dictionary<string, object> payload)
{
    var header = new Dictionary<string, object>
    {
        ["alg"] = "RS256",
        ["typ"] = "JWT",
        ["kid"] = kid
    };
    var headerB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
    var payloadB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
    var signingInput = headerB64 + "." + payloadB64;
    var sig = signingKey.SignData(Encoding.ASCII.GetBytes(signingInput),
        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return signingInput + "." + Base64UrlEncode(sig);
}

static string BuildKbJwt(RSA signingKey, string aud, string nonce, string sdHash)
{
    var now = DateTimeOffset.UtcNow;
    var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
    var securityKey = new RsaSecurityKey(signingKey) { KeyId = "mock-issuer" };
    var creds = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

    var token = new JwtSecurityToken(
        issuer: null,
        audience: aud,
        claims: new[]
        {
            new Claim("nonce", nonce),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("sd_hash", sdHash)
        },
        notBefore: now.AddMinutes(-1).UtcDateTime,
        expires: now.AddMinutes(5).UtcDateTime,
        signingCredentials: creds);

    // Per SD-JWT: KB-JWT header typ MUST be "kb+jwt".
    token.Header["typ"] = "kb+jwt";

    return handler.WriteToken(token);
}

static string ComputeSdHash(string issuerJwt, string[] disclosures)
{
    var sb = new StringBuilder(issuerJwt);
    foreach (var d in disclosures)
        sb.Append('~').Append(d);
    var hash = SHA256.HashData(Encoding.ASCII.GetBytes(sb.ToString()));
    return Base64UrlEncode(hash);
}

static string BuildErrorHtml(string errorText)
{
    var escaped = errorText.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    return $$"""
        <!doctype html><html lang="ro"><head><meta charset="utf-8"/><title>Eroare</title>
        <style>body{font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;background:#f0f2f5;}
        .card{background:#fff;border-radius:12px;padding:2rem;max-width:400px;width:100%;box-shadow:0 4px 24px rgba(0,0,0,.1);text-align:center;}
        h1{color:#c0392b;font-size:1.25rem;margin:0 0 .5rem;} p{color:#555;font-size:.9rem;}</style></head>
        <body><div class="card"><h1>Eroare</h1><p>{{escaped}}</p></div></body></html>
        """;
}

// ---------------------------------------------------------------------------
record EudiwSimulateRequest(Guid SigningRequestId, string? Nonce);

record IssuePidRequest(
    [property: JsonPropertyName("jwk")] IssuePidJwk? Jwk,
    [property: JsonPropertyName("family_name")] string? FamilyName,
    [property: JsonPropertyName("given_name")] string? GivenName,
    [property: JsonPropertyName("birth_date")] string? BirthDate,
    [property: JsonPropertyName("email")] string? Email);

record IssuePidJwk(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("crv")] string Crv,
    [property: JsonPropertyName("x")] string? X,
    [property: JsonPropertyName("y")] string? Y);
