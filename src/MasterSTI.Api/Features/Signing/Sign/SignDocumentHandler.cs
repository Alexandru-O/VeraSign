using System.Security.Cryptography;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Common.Csc;
using MasterSTI.Api.Common.Realtime;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Signing.Embed;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MasterSTI.Api.Features.Signing.Sign;

public sealed class SignDocumentHandler : IRequestHandler<SignDocumentCommand, SignDocumentResponse>
{
    private readonly AppDbContext _db;
    private readonly PadesService _pades;
    private readonly ICscApiClient _csc;
    private readonly IOptionsMonitor<CscApiOptions> _options;
    private readonly IMediator _mediator;
    private readonly IConfiguration _config;
    private readonly IAuditWriter? _audit;
    private readonly ILogger<SignDocumentHandler> _logger;
    private readonly IDashboardCacheInvalidator? _dashCache;
    private readonly IDashboardNotifier? _notifier;

    public SignDocumentHandler(
        AppDbContext db,
        PadesService pades,
        ICscApiClient csc,
        IOptionsMonitor<CscApiOptions> options,
        IMediator mediator,
        IConfiguration config,
        ILogger<SignDocumentHandler> logger,
        IAuditWriter? audit = null,
        IDashboardCacheInvalidator? dashCache = null,
        IDashboardNotifier? notifier = null)
    {
        _db = db;
        _pades = pades;
        _csc = csc;
        _options = options;
        _mediator = mediator;
        _config = config;
        _audit = audit;
        _logger = logger;
        _dashCache = dashCache;
        _notifier = notifier;
    }

    public async Task<SignDocumentResponse> Handle(SignDocumentCommand request, CancellationToken cancellationToken)
    {
        var sigReq = await _db.SigningRequests
            .Include(s => s.Document)
            .Include(s => s.Recipient)
            .FirstOrDefaultAsync(s => s.Id == request.SigningRequestId, cancellationToken)
            ?? throw new KeyNotFoundException($"SigningRequest {request.SigningRequestId} not found");

        if (sigReq.Status is not (SigningRequestStatus.HashPrepared or SigningRequestStatus.EudiwAuthorized))
            throw new InvalidOperationException($"SigningRequest {sigReq.Id} is in status {sigReq.Status} — cannot sign.");

        if (sigReq.PreparedStoragePath is null)
            throw new InvalidOperationException("SigningRequest has no prepared PDF.");

        var opts = _options.CurrentValue;
        var username = Require(opts.Username, "CscApi:Username");
        var password = Require(opts.Password, "CscApi:Password");
        var credentialId = !string.IsNullOrEmpty(sigReq.CredentialId)
            ? sigReq.CredentialId
            : Require(opts.CredentialId, "CscApi:CredentialId");

        try
        {
            var accessToken = await _csc.AuthLoginAsync(username, password, cancellationToken);

            var signerCn = SanitizeCn(sigReq.Recipient?.Name);
            var credInfo = await _csc.GetCredentialInfoAsync(accessToken, credentialId, cancellationToken, signerCn);
            var certDerBytes = Convert.FromBase64String(credInfo.cert.certificates[0]);

            var docHashBytes = Convert.FromHexString(sigReq.DocumentHash);
            var signedAttrBytes = _pades.GetSignedAttributesBytes(docHashBytes, certDerBytes);
            var signedAttrHashBase64 = Convert.ToBase64String(SHA256.HashData(signedAttrBytes));

            var (factorId, factorValue) = MapFactor(request.Factor, request.Pin);
            var sad = await _csc.AuthorizeCredentialAsync(
                accessToken, credentialId,
                new[] { signedAttrHashBase64 }, factorId, factorValue, cancellationToken);

            var signatures = await _csc.SignHashAsync(
                accessToken, credentialId, sad,
                new[] { signedAttrHashBase64 }, cancellationToken, signerCn);

            var rawSigBytes = Convert.FromBase64String(signatures[0]);
            // PAdES B-T requires a signature-timestamp-token attribute inside the CMS.
            // Without TsaUrl the CMS is actually B-B; EmbedSignatureHandler then mis-labels
            // the output as B-T via the LTV-absence heuristic. Pass the configured TSA so
            // BuildCms attaches TSAClientBouncyCastle and embeds the timestamp token.
            var tsaUrl = _config["TsaUrl"];
            var cmsBytes = _pades.BuildCms(docHashBytes, certDerBytes, rawSigBytes, tsaUrl);
            var cmsBase64 = Convert.ToBase64String(cmsBytes);

            sigReq.Status = SigningRequestStatus.CredentialAuthorized;
            sigReq.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            var embedResult = await _mediator.Send(
                new EmbedSignatureCommand(request.SigningRequestId, cmsBase64),
                cancellationToken);

            if (_audit is not null)
                await _audit.WriteAsync(sigReq.DocumentId, "Signed",
                    $"{{\"signedDocumentId\":\"{embedResult.SignedDocumentId}\",\"level\":\"{embedResult.PadesLevel}\",\"factor\":\"{request.Factor}\"}}",
                    cancellationToken);

            _logger.LogInformation("Document signed: {SignedDocId}, Level: {Level}",
                embedResult.SignedDocumentId, embedResult.PadesLevel);

            _dashCache?.InvalidateOrg(sigReq.Document?.OrganizationId);
            if (_notifier is not null)
                await _notifier.NotifyOrgAsync(sigReq.Document?.OrganizationId, cancellationToken);

            return new SignDocumentResponse(embedResult.SignedDocumentId, embedResult.PadesLevel);
        }
        catch
        {
            sigReq.Status = SigningRequestStatus.Failed;
            // Only claim CSC-signHash as the failure stage when no downstream stage
            // already attributed it (e.g. EmbedSignatureHandler writes stage 5 from
            // its own catch). Otherwise stage 5 failures get stomped to stage 4 and
            // the dashboard pipeline mis-attributes the break.
            sigReq.FailedAtStage ??= 4;
            sigReq.UpdatedAt = DateTime.UtcNow;
            try { await _db.SaveChangesAsync(cancellationToken); } catch { }
            _dashCache?.InvalidateOrg(sigReq.Document?.OrganizationId);
            if (_notifier is not null)
                try { await _notifier.NotifyOrgAsync(sigReq.Document?.OrganizationId, cancellationToken); } catch { }
            throw;
        }
    }

    private static string Require(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Configuration '{name}' is required but not set.")
            : value;

    // ADR-0010: wallet factor → CSC v2 §11.5 authData[].id. Hardcoded against
    // the Mock QTSP; real QTSPs publish accepted IDs via /info (deferred).
    private static (string Id, string Value) MapFactor(string factor, string pin) => factor switch
    {
        "biometric" => ("BIO", pin),
        _           => ("PIN", pin),
    };

    // Strip DN-reserved chars from a display name so it can land in CN= safely.
    // Real CSC issues the cert; this hint is Mock-only.
    private static string? SanitizeCn(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();
        foreach (var bad in new[] { ',', '=', '+', '<', '>', '#', ';', '"', '\\', '\r', '\n' })
            trimmed = trimmed.Replace(bad, '-');
        return trimmed.Length == 0 ? null : trimmed;
    }
}
