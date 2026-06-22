using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MasterSTI.Api.Common.Eudiw;

/// <summary>
/// Resolves the trusted EUDIW issuer public key once at startup and pins it
/// against <see cref="EudiwOptions.IssuerPublicKeyPemSha256"/>.
///
/// Lifecycle:
///   * Reads inline PEM (<see cref="EudiwOptions.IssuerPublicKeyPem"/>) if set.
///   * Otherwise GETs <see cref="EudiwOptions.IssuerPublicKeyPemUrl"/> asynchronously.
///   * Computes SHA-256(hex) of the normalised PEM bytes.
///   * Compares to the configured pin. Mismatch throws and fails startup —
///     <c>SdJwtValidator</c> never sees an unpinned key.
///   * When no pin is configured, logs <c>Critical</c> with the observed hash
///     so an operator can paste it back into config. Loud TOFU, not silent.
///   * Stores the resolved <see cref="RsaSecurityKey"/> on the singleton
///     <see cref="IIssuerKeyHolder"/> consumed by <see cref="SdJwtValidator"/>.
/// </summary>
public sealed class IssuerPemLoader : IHostedService
{
    private readonly IOptionsMonitor<EudiwOptions> _options;
    private readonly IHttpClientFactory? _httpFactory;
    private readonly IIssuerKeyHolder _holder;
    private readonly ILogger<IssuerPemLoader> _logger;

    public IssuerPemLoader(
        IOptionsMonitor<EudiwOptions> options,
        IIssuerKeyHolder holder,
        ILogger<IssuerPemLoader> logger,
        IHttpClientFactory? httpFactory = null)
    {
        _options = options;
        _holder = holder;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        var pem = await LoadPemAsync(opts, cancellationToken).ConfigureAwait(false);
        if (pem is null)
        {
            _logger.LogInformation(
                "No EUDIW issuer key configured (neither inline PEM nor URL). " +
                "SdJwtValidator will reject all signed issuer JWTs.");
            _holder.Set(null, null);
            return;
        }

        var hash = ComputePemSha256Hex(pem);
        var pin = (opts.IssuerPublicKeyPemSha256 ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(pin))
        {
            // Loud TOFU — accepted but flagged so a deployer can copy the hash
            // into config and switch to enforcement on the next boot.
            _logger.LogCritical(
                "EUDIW issuer PEM pin (Eudiw:IssuerPublicKeyPemSha256) is not configured. " +
                "Accepting the loaded key on trust. Set the pin to the following hex value to enforce: {Sha256}",
                hash);
        }
        else if (!string.Equals(NormalizeHex(pin), hash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "EUDIW issuer PEM hash does not match the configured pin. " +
                $"Expected '{NormalizeHex(pin)}', got '{hash}'. " +
                "Either the configured PEM source changed or someone is impersonating the issuer.");
        }

        var key = BuildRsaKey(pem);
        _holder.Set(key, hash);
        _logger.LogInformation(
            "EUDIW issuer key loaded ({Bytes} bytes PEM, sha256 {Sha256}).",
            Encoding.UTF8.GetByteCount(pem), hash);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<string?> LoadPemAsync(EudiwOptions opts, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(opts.IssuerPublicKeyPem))
            return opts.IssuerPublicKeyPem;

        if (string.IsNullOrWhiteSpace(opts.IssuerPublicKeyPemUrl) || _httpFactory is null)
            return null;

        using var client = _httpFactory.CreateClient("eudiw-jwks");
        if (client.Timeout == Timeout.InfiniteTimeSpan)
            client.Timeout = TimeSpan.FromSeconds(5);

        // The Mock QTSP container may still be coming up while the API boots.
        // Short retry budget so the API doesn't end up with a null key just
        // because the issuer endpoint lost a startup race.
        const int maxAttempts = 5;
        var delay = TimeSpan.FromMilliseconds(500);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await client.GetStringAsync(opts.IssuerPublicKeyPemUrl, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "EUDIW issuer PEM fetch attempt {Attempt}/{Max} failed: {Reason}. Retrying in {Delay}.",
                    attempt, maxAttempts, ex.Message, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 4000));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to fetch EUDIW issuer PEM from {Url} after {Attempts} attempts. " +
                    "Validator will reject all signed issuer JWTs.",
                    opts.IssuerPublicKeyPemUrl, attempt);
                return null;
            }
        }
        return null;
    }

    private static SecurityKey BuildRsaKey(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa) { KeyId = "eudiw-issuer" };
    }

    /// <summary>
    /// SHA-256(hex, lowercase) of the PEM bytes after normalising line endings.
    /// Normalisation keeps the pin stable when the PEM crosses platforms (CRLF
    /// vs LF) or picks up trailing whitespace from a config patcher.
    /// </summary>
    public static string ComputePemSha256Hex(string pem)
    {
        var normalised = pem.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeHex(string s)
        => s.Trim().Replace(":", "").Replace(" ", "").ToLowerInvariant();
}
