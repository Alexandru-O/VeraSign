using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Common.Signing;

/// <summary>
/// Applies the Sign Order Rule mutations for an embed completing on one
/// <see cref="SigningRequest"/>. Adds the new <see cref="SignedDocument"/> row,
/// flips the prior chain tail's <see cref="SignedDocument.IsFinal"/>, marks the
/// current <see cref="Recipient"/> Signed, and either transitions the next-Order
/// Recipient to Notified with a fresh Pending SigningRequest, or finalises
/// <see cref="Document.Status"/> to <see cref="DocumentStatus.Signed"/> when no
/// next recipient exists. Caller owns the surrounding transaction +
/// <c>SaveChangesAsync</c>. See <c>docs/adr/0006-sign-order-rule-in-embed-handler.md</c>.
/// </summary>
public static class SignedDocumentChainHandover
{
    public static async Task ApplyAsync(
        AppDbContext db,
        SigningRequest sigReq,
        Guid signedDocId,
        string signedPath,
        string padesLevel,
        string? manifestJson,
        CancellationToken cancellationToken = default)
    {
        var prevTail = await db.SignedDocuments
            .Where(sd => sd.OriginalDocumentId == sigReq.DocumentId && sd.IsFinal)
            .OrderByDescending(sd => sd.SignedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var currentOrder = sigReq.Recipient?.Order ?? 0;
        var nextRecipient = await db.Recipients
            .Where(r => r.DocumentId == sigReq.DocumentId && r.Order > currentOrder)
            .OrderBy(r => r.Order)
            .FirstOrDefaultAsync(cancellationToken);

        var isLastSigner = nextRecipient is null;
        var nowUtc = DateTime.UtcNow;

        db.SignedDocuments.Add(new SignedDocument
        {
            Id = signedDocId,
            OriginalDocumentId = sigReq.DocumentId,
            SigningRequestId = sigReq.Id,
            RecipientId = sigReq.RecipientId,
            PreviousSignedDocumentId = prevTail?.Id,
            IsFinal = isLastSigner,
            StoragePath = signedPath,
            SignedAt = nowUtc,
            PadesLevel = padesLevel,
            PageManifestJson = manifestJson
        });

        if (prevTail is not null)
            prevTail.IsFinal = false;

        sigReq.Status = SigningRequestStatus.Embedded;
        sigReq.UpdatedAt = nowUtc;

        if (sigReq.Recipient is not null)
        {
            sigReq.Recipient.Status = RecipientStatus.Signed;
            sigReq.Recipient.SignedAt = nowUtc;
        }

        if (isLastSigner)
        {
            sigReq.Document.Status = DocumentStatus.Signed;
        }
        else
        {
            nextRecipient!.Status = RecipientStatus.Notified;
            nextRecipient.NotifiedAt = nowUtc;
            db.SigningRequests.Add(new SigningRequest
            {
                Id = Guid.NewGuid(),
                DocumentId = sigReq.DocumentId,
                RecipientId = nextRecipient.Id,
                OrderIndex = nextRecipient.Order,
                RequestedBy = "system",
                CredentialId = string.Empty,
                SignatureLevel = "PAdES-B-LT",
                DocumentHash = string.Empty,
                Status = SigningRequestStatus.Pending,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
            sigReq.Document.Status = DocumentStatus.Awaiting;
        }
    }
}
