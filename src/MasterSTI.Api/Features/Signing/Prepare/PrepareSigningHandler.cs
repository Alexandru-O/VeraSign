using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Signing.Prepare;

public sealed class PrepareSigningHandler : IRequestHandler<PrepareSigningCommand, PrepareSigningResponse>
{
    private readonly AppDbContext _db;
    private readonly DocumentStorage _storage;
    private readonly PadesService _pades;
    private readonly IAuditWriter? _audit;
    private readonly ICurrentUserAccessor? _currentUser;
    private readonly ILogger<PrepareSigningHandler> _logger;

    public PrepareSigningHandler(
        AppDbContext db,
        DocumentStorage storage,
        PadesService pades,
        ILogger<PrepareSigningHandler> logger,
        IAuditWriter? audit = null,
        ICurrentUserAccessor? currentUser = null)
    {
        _db = db;
        _storage = storage;
        _pades = pades;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<PrepareSigningResponse> Handle(PrepareSigningCommand request, CancellationToken cancellationToken)
    {
        if (request.RecipientId == Guid.Empty)
            throw new ArgumentException("RecipientId is required.", nameof(request));

        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found");

        if (document.Status is DocumentStatus.Failed)
            throw new InvalidOperationException($"Document {request.DocumentId} is in Failed status.");

        var recipient = await _db.Recipients
            .FirstOrDefaultAsync(r => r.Id == request.RecipientId, cancellationToken)
            ?? throw new KeyNotFoundException($"Recipient {request.RecipientId} not found");

        if (recipient.DocumentId != request.DocumentId)
            throw new InvalidOperationException(
                $"Recipient {request.RecipientId} does not belong to document {request.DocumentId}.");

        // Recipient-identity guard: only the invited signer may prepare their own
        // SigningRequest. Sender (document owner) and other users in the org must
        // not be able to ghost-sign on a recipient's behalf. Skipped when no
        // current-user context (legacy unit tests / anonymous dev mode).
        var callerEmail = _currentUser?.Email;
        if (!string.IsNullOrWhiteSpace(callerEmail))
        {
            if (!string.Equals(callerEmail, recipient.Email, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "PrepareSigning blocked: caller {CallerEmail} is not recipient {RecipientEmail} on document {DocumentId}",
                    callerEmail, recipient.Email, request.DocumentId);
                throw new UnauthorizedAccessException(
                    "Only the invited recipient may prepare this signing request.");
            }

            if (recipient.Status != RecipientStatus.Notified)
            {
                _logger.LogWarning(
                    "PrepareSigning blocked: recipient {RecipientId} on document {DocumentId} is not in Notified status (current: {Status})",
                    recipient.Id, request.DocumentId, recipient.Status);
                throw new UnauthorizedAccessException(
                    "Recipient is not currently active for signing on this document.");
            }
        }

        // Signed Document Chain — when a prior signer has embedded, the source
        // bytes for this Prepare must be the most-recently-signed SignedDocument
        // PDF, not the original upload. IsFinal flips true only on the LAST
        // signer (see SignedDocumentChainHandover), so we cannot filter by it
        // for intermediate links — order by SignedAt desc instead. Skip the
        // Sha256 anti-tamper check on the chained branch: PAdES incremental
        // signatures rewrite the file, so Document.Sha256Hash never matches the
        // post-embed PDF — trust for stacked signatures comes from PAdES
        // validation of the chain itself.
        var chainHead = await _db.SignedDocuments
            .AsNoTracking()
            .Where(sd => sd.OriginalDocumentId == request.DocumentId)
            .OrderByDescending(sd => sd.SignedAt)
            .Select(sd => sd.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        byte[] sourcePdfBytes;
        if (!string.IsNullOrWhiteSpace(chainHead) && File.Exists(chainHead))
        {
            sourcePdfBytes = await File.ReadAllBytesAsync(chainHead, cancellationToken);
        }
        else
        {
            sourcePdfBytes = await _storage.ReadAsync(document.StoragePath, cancellationToken);
            var currentHash = HashingService.ComputeSha256(sourcePdfBytes);

            if (!string.Equals(currentHash, document.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Anti-tamper check FAILED for document {DocumentId}", request.DocumentId);
                throw new InvalidOperationException(
                    $"Document integrity check failed for {request.DocumentId}. The file has been tampered with.");
            }
        }

        // Build a recipient-unique AcroForm field name so stacked signers do
        // not collide on the default "Signature1". Sanitisation inside
        // PadesService keeps it AcroForm-safe.
        var fieldName = $"sig_r{recipient.Order}_{recipient.Id:N}";

        // Use the editor-placed widget for this recipient when available.
        // Editor stores % of page (origin top-left); PadesService converts to
        // PDF user space. Fall back to the legacy top-left page-1 stamp when
        // the sender skipped the Place Fields step.
        var placement = await _db.SignatureFields
            .AsNoTracking()
            .Where(f => f.DocumentId == request.DocumentId
                     && f.RecipientId == recipient.Id
                     && f.Type == "Signature")
            .OrderBy(f => f.Page)
            .Select(f => new PadesFieldPlacement(f.Page, f.X, f.Y, f.Width, f.Height))
            .FirstOrDefaultAsync(cancellationToken);

        var appearance = new PadesAppearance(
            SignerName: recipient.Name,
            SignerEmail: recipient.Email,
            SignedAtUtc: DateTime.UtcNow);

        // ADR-0008: wallet-supplied Render commitment (when present) lands in
        // the PAdES signature dictionary inside the signed ByteRange. The
        // bang-asserts are safe -- PrepareSigningValidator guarantees every
        // field is populated when RenderCommitment.IsPresent is true.
        var padesCommitment = request.RenderCommitment is { IsPresent: true } rc
            ? new PadesRenderCommitment(
                RootHex: rc.RenderRootHex!,
                Algo: rc.RenderAlgo!,
                Dpi: rc.RenderDpi!.Value,
                PageCount: rc.RenderPageCount!.Value,
                Locale: rc.RenderLocale!,
                Profile: rc.RenderProfile!)
            : null;

        var prepareResult = _pades.Prepare(sourcePdfBytes, fieldName, placement, appearance, padesCommitment);

        // The Send flow seeds a Pending SigningRequest for Order=1 (and embeds
        // do the same for downstream recipients). Reuse it so the pipeline shows
        // one row per signer; only fall through to creation for legacy callers
        // that bypass Send (tests + the older self-sign QR path).
        var signingRequest = await _db.SigningRequests
            .Where(sr => sr.DocumentId == request.DocumentId
                      && sr.RecipientId == recipient.Id
                      && sr.Status == SigningRequestStatus.Pending)
            .OrderByDescending(sr => sr.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var signingRequestId = signingRequest?.Id ?? Guid.NewGuid();
        var preparedPath = Path.Combine(_storage.PreparedRoot, $"{signingRequestId}.pdf");
        await File.WriteAllBytesAsync(preparedPath, prepareResult.PreparedPdfBytes, cancellationToken);

        var tx = await DbTransactionScope.BeginIfRelationalAsync(_db, cancellationToken);
        try
        {
            document.Status = DocumentStatus.Preparing;

            if (signingRequest is null)
            {
                signingRequest = new SigningRequest
                {
                    Id = signingRequestId,
                    DocumentId = request.DocumentId,
                    RecipientId = recipient.Id,
                    OrderIndex = recipient.Order,
                    RequestedBy = request.RequestedBy,
                    CredentialId = request.CredentialId,
                    SignatureLevel = "PAdES-B-LT",
                    DocumentHash = prepareResult.ByteRangeHashHex,
                    PreparedStoragePath = preparedPath,
                    PreparedFieldName = prepareResult.SignatureFieldName,
                    Status = SigningRequestStatus.HashPrepared,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.SigningRequests.Add(signingRequest);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(request.CredentialId))
                    signingRequest.CredentialId = request.CredentialId;
                if (!string.IsNullOrWhiteSpace(request.RequestedBy))
                    signingRequest.RequestedBy = request.RequestedBy;
                signingRequest.DocumentHash = prepareResult.ByteRangeHashHex;
                signingRequest.PreparedStoragePath = preparedPath;
                signingRequest.PreparedFieldName = prepareResult.SignatureFieldName;
                signingRequest.Status = SigningRequestStatus.HashPrepared;
                signingRequest.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await DbTransactionScope.CommitIfAsync(tx, cancellationToken);
        }
        catch
        {
            await DbTransactionScope.RollbackIfAsync(tx, cancellationToken);
            try { File.Delete(preparedPath); } catch { }
            throw;
        }

        if (_audit is not null)
            await _audit.WriteAsync(request.DocumentId, "PrepareSigning",
                $"{{\"signingRequestId\":\"{signingRequestId}\",\"recipientId\":\"{recipient.Id}\"}}",
                cancellationToken);

        _logger.LogInformation("Signing prepared: {DocumentId} / Recipient {RecipientId} → SigningRequest {SigningRequestId}",
            request.DocumentId, recipient.Id, signingRequestId);

        return new PrepareSigningResponse(signingRequestId, prepareResult.ByteRangeHashHex);
    }
}
