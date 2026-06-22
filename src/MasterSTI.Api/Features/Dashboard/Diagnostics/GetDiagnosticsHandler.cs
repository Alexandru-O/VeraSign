using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using iText.Signatures;
using MasterSTI.Api.Common.Csc;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Features.Dashboard.Diagnostics;

/// <summary>
/// Runs lightweight health probes against the six infrastructure dependencies of
/// the eIDAS 2.0 + CSC v2 signing pipeline. Probes run in parallel with a 1.2s
/// budget each so the dashboard call stays snappy even when an upstream is slow.
/// The result is memoized for 30 seconds per process (no per-user scope — the
/// infrastructure state is shared across the org).
/// </summary>
public sealed class GetDiagnosticsHandler : IRequestHandler<GetDiagnosticsQuery, DiagnosticsDto>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// 7-day sparkline window split into 84 ~2h buckets. Trades resolution for
    /// JSON payload size and SVG width — fine for an at-a-glance trend.
    /// </summary>
    public const int SparklineBuckets = 84;
    public static readonly TimeSpan SparklineWindow = TimeSpan.FromDays(7);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly CscApiOptions _csc;
    private readonly EudiwOptions _eudiw;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;

    public GetDiagnosticsHandler(
        IHttpClientFactory httpFactory,
        IMemoryCache cache,
        IOptions<CscApiOptions> csc,
        IOptions<EudiwOptions> eudiw,
        IHostEnvironment env,
        IConfiguration config,
        AppDbContext db)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _csc = csc.Value;
        _eudiw = eudiw.Value;
        _env = env;
        _config = config;
        _db = db;
    }

    public async Task<DiagnosticsDto> Handle(GetDiagnosticsQuery request, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<DiagnosticsDto>("dash:diagnostics", out var cached) && cached is not null)
            return cached;

        var qtspTask     = ProbeQtspAsync(cancellationToken);
        var tsaTask      = ProbeTsaAsync(cancellationToken);
        var ocspTask     = ProbeOcspAsync(cancellationToken);
        var ltvTask      = Task.FromResult(ProbeLtvArchive());
        var issuerTask   = ProbeEudiwIssuerAsync(cancellationToken);
        var selfTask     = ProbeSelfAsync(cancellationToken);

        await Task.WhenAll(qtspTask, tsaTask, ocspTask, ltvTask, issuerTask, selfTask);

        var rawNodes = new[]
        {
            qtspTask.Result,
            tsaTask.Result,
            ocspTask.Result,
            ltvTask.Result,
            issuerTask.Result,
            selfTask.Result
        };

        var probedAt = DateTime.UtcNow;
        var sparks = await BuildSparklinesAsync(probedAt, cancellationToken);

        var nodes = rawNodes
            .Select(n => n with
            {
                Sparkline = sparks.TryGetValue(n.Key, out var trail) ? trail : null
            })
            .ToArray();

        var dto = new DiagnosticsDto(probedAt, nodes);
        _cache.Set("dash:diagnostics", dto, CacheTtl);
        return dto;
    }

    /// <summary>
    /// Reads the last 7 days of <see cref="ProbeResult"/> rows for every node
    /// and folds them into <see cref="SparklineBuckets"/> equal-width buckets,
    /// oldest to newest. Per bucket the worst observed health wins
    /// (<c>err &gt; warn &gt; ok</c>); buckets with no sample are <c>"na"</c>.
    /// Returns an empty dictionary on DB failure so the dashboard still
    /// renders.
    /// </summary>
    private async Task<Dictionary<string, string[]>> BuildSparklinesAsync(DateTime probedAt, CancellationToken ct)
    {
        try
        {
            var from = probedAt - SparklineWindow;

            // Pull only the columns we need; ProbeResults can grow to ~60k rows
            // over 7 days (6 nodes * 60 ticks/h * 168 h).
            var samples = await _db.ProbeResults
                .Where(p => p.Timestamp >= from && p.Timestamp <= probedAt)
                .Select(p => new { p.Node, p.Timestamp, p.Health })
                .ToListAsync(ct);

            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (samples.Count == 0) return result;

            var bucketTicks = SparklineWindow.Ticks / SparklineBuckets;

            foreach (var group in samples.GroupBy(s => s.Node))
            {
                var trail = new string[SparklineBuckets];
                for (var i = 0; i < SparklineBuckets; i++) trail[i] = "na";

                foreach (var s in group)
                {
                    var offset = (s.Timestamp - from).Ticks;
                    var idx = (int)(offset / bucketTicks);
                    if (idx < 0 || idx >= SparklineBuckets) continue;

                    var existing = trail[idx];
                    trail[idx] = WorseHealth(existing, s.Health);
                }

                result[group.Key] = trail;
            }

            return result;
        }
        catch (Exception)
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal);
        }
    }

    private static string WorseHealth(string current, string incoming)
    {
        // Severity ladder: err > warn > ok > na.
        static int Rank(string h) => h switch
        {
            "err"  => 3,
            "warn" => 2,
            "ok"   => 1,
            _      => 0,
        };
        return Rank(incoming) > Rank(current) ? incoming : current;
    }

    // ---- Individual probes ----

    private async Task<DiagnosticNodeDto> ProbeQtspAsync(CancellationToken ct)
    {
        const string name = "QTSP CSC v2";
        var detail = $"{Host(_csc.BaseUrl)} · /signHash";

        if (string.IsNullOrWhiteSpace(_csc.BaseUrl))
            return new DiagnosticNodeDto("qtsp", name, "neconfigurat", "warn", null);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            var client = _httpFactory.CreateClient("diagnostics");
            client.Timeout = ProbeTimeout;
            var sw = Stopwatch.StartNew();
            var resp = await client.GetAsync(new Uri(new Uri(_csc.BaseUrl), "/csc/v2/info"), cts.Token);
            sw.Stop();

            // /csc/v2/info is the public CSC discovery endpoint. Both 2xx and 401 mean
            // the host is alive; only network failures count as outage.
            var ok = (int)resp.StatusCode is >= 200 and < 500;
            return new DiagnosticNodeDto("qtsp", name, detail, ok ? "ok" : "warn", sw.ElapsedMilliseconds);
        }
        catch (Exception)
        {
            return new DiagnosticNodeDto("qtsp", name, detail, "err", null);
        }
    }

    /// <summary>
    /// Real RFC 3161 round-trip against the configured TSA. Signs a SHA-256 imprint
    /// of a fixed diagnostic payload via iText's <see cref="TSAClientBouncyCastle"/>;
    /// a successful token (non-empty DER) marks the node "ok" with measured rtt.
    /// </summary>
    private async Task<DiagnosticNodeDto> ProbeTsaAsync(CancellationToken ct)
    {
        const string name = "TSA";
        var tsaUrl = _config["TsaUrl"];
        var detail = string.IsNullOrWhiteSpace(tsaUrl)
            ? "RFC 3161 · neconfigurat"
            : $"RFC 3161 · {Host(tsaUrl)}";

        if (string.IsNullOrWhiteSpace(tsaUrl))
            return new DiagnosticNodeDto("tsa", name, detail, "warn", null);

        try
        {
            var imprint = SHA256.HashData(Encoding.UTF8.GetBytes("verasign-diagnostic-probe"));
            var sw = Stopwatch.StartNew();
            var token = await Task.Run(() =>
            {
                var tsa = new TSAClientBouncyCastle(tsaUrl);
                return tsa.GetTimeStampToken(imprint);
            }, ct).WaitAsync(ProbeTimeout, ct);
            sw.Stop();

            return token is { Length: > 0 }
                ? new DiagnosticNodeDto("tsa", name, detail, "ok", sw.ElapsedMilliseconds)
                : new DiagnosticNodeDto("tsa", name, detail, "warn", sw.ElapsedMilliseconds);
        }
        catch (Exception)
        {
            return new DiagnosticNodeDto("tsa", name, detail, "err", null);
        }
    }

    /// <summary>
    /// Liveness probe against the OCSP responder. A full RFC 6960 OCSPRequest would
    /// require a real subject certificate to query — we don't have one outside the
    /// signing pipeline. A HEAD request is enough to know the responder is reachable;
    /// any status &lt; 500 means "alive" (400 is the common answer to a HEAD without
    /// a request body, and still proves the host is up).
    /// </summary>
    private async Task<DiagnosticNodeDto> ProbeOcspAsync(CancellationToken ct)
    {
        const string name = "OCSP";
        var ocspUrl = _config["OcspUrl"];
        var detail = string.IsNullOrWhiteSpace(ocspUrl)
            ? "RFC 6960 · neconfigurat"
            : $"RFC 6960 · {Host(ocspUrl)}";

        if (string.IsNullOrWhiteSpace(ocspUrl))
            return new DiagnosticNodeDto("ocsp", name, detail, "warn", null);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            var client = _httpFactory.CreateClient("diagnostics");
            client.Timeout = ProbeTimeout;
            var sw = Stopwatch.StartNew();
            using var req = new HttpRequestMessage(HttpMethod.Head, ocspUrl);
            var resp = await client.SendAsync(req, cts.Token);
            sw.Stop();

            var ok = (int)resp.StatusCode is >= 200 and < 500;
            return new DiagnosticNodeDto("ocsp", name, detail, ok ? "ok" : "warn", sw.ElapsedMilliseconds);
        }
        catch (Exception)
        {
            return new DiagnosticNodeDto("ocsp", name, detail, "err", null);
        }
    }

    /// <summary>
    /// Loopback HEAD against the API's own /healthz endpoint. /healthz is wired
    /// to ASP.NET HealthChecks (DbContext ping + self), so this surfaces a real
    /// status — degraded DB will flip the node to "warn" / "err" automatically.
    /// </summary>
    private async Task<DiagnosticNodeDto> ProbeSelfAsync(CancellationToken ct)
    {
        const string name = "API self";
        const string detail = "/healthz · DbContext + self";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            var client = _httpFactory.CreateClient("diagnostics");
            client.Timeout = ProbeTimeout;
            var sw = Stopwatch.StartNew();
            // Loopback. Kestrel binds 0.0.0.0:8080 inside the container and
            // https://localhost:7001 outside; pick scheme + port from the
            // first URL entry so an HTTPS-only host doesn't get probed over
            // plain HTTP (which previously flipped this node to "warn" on
            // start-all.ps1 / dotnet run). CreateBuilder maps both
            // ASPNETCORE_URLS (env) and --urls (CLI) into the "urls" key.
            var urls = _config["ASPNETCORE_URLS"]
                    ?? _config["urls"]
                    ?? string.Empty;
            var firstUrl = urls.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var scheme = "http";
            var port = 8080;
            if (!string.IsNullOrEmpty(firstUrl) && Uri.TryCreate(firstUrl.Replace("0.0.0.0", "localhost").Replace("*", "localhost"), UriKind.Absolute, out var parsed))
            {
                scheme = parsed.Scheme;
                port = parsed.IsDefaultPort ? (scheme == "https" ? 443 : 80) : parsed.Port;
            }
            var url = $"{scheme}://localhost:{port}/healthz";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var resp = await client.SendAsync(req, cts.Token);
            sw.Stop();

            var health = resp.IsSuccessStatusCode ? "ok" : "warn";
            return new DiagnosticNodeDto("api", name, detail, health, sw.ElapsedMilliseconds);
        }
        catch (Exception)
        {
            return new DiagnosticNodeDto("api", name, detail, "err", null);
        }
    }

    private DiagnosticNodeDto ProbeLtvArchive()
    {
        const string name = "LTV Archive";
        const string detail = "storage/signed · 10 ani";
        try
        {
            var dir = Path.Combine(_env.ContentRootPath, "storage", "signed");
            Directory.CreateDirectory(dir);

            var probePath = Path.Combine(dir, ".diag-probe");
            File.WriteAllBytes(probePath, Array.Empty<byte>());
            File.Delete(probePath);

            return new DiagnosticNodeDto("ltv", name, detail, "ok", null);
        }
        catch (Exception)
        {
            return new DiagnosticNodeDto("ltv", name, detail, "err", null);
        }
    }

    private async Task<DiagnosticNodeDto> ProbeEudiwIssuerAsync(CancellationToken ct)
    {
        const string name = "EUDIW Issuer";

        // Inline PEM trumps URL.
        if (!string.IsNullOrWhiteSpace(_eudiw.IssuerPublicKeyPem))
            return new DiagnosticNodeDto("issuer", name, "PEM inline · RS256", "ok", null);

        if (string.IsNullOrWhiteSpace(_eudiw.IssuerPublicKeyPemUrl))
            return new DiagnosticNodeDto("issuer", name, "PEM neconfigurat", "warn", null);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            var client = _httpFactory.CreateClient("diagnostics");
            client.Timeout = ProbeTimeout;
            var sw = Stopwatch.StartNew();
            var resp = await client.GetAsync(_eudiw.IssuerPublicKeyPemUrl, cts.Token);
            sw.Stop();

            var ok = resp.IsSuccessStatusCode;
            var detail = $"{Host(_eudiw.IssuerPublicKeyPemUrl!)} · RS256";
            return new DiagnosticNodeDto("issuer", name, detail, ok ? "ok" : "warn", sw.ElapsedMilliseconds);
        }
        catch (Exception)
        {
            return new DiagnosticNodeDto("issuer", name, "fetch eșuat · RS256", "err", null);
        }
    }

    private static string Host(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) return u.Host;
        return url;
    }
}
