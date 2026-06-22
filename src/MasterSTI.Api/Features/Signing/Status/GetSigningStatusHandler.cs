using MasterSTI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Signing.Status;

public sealed class GetSigningStatusHandler : IRequestHandler<GetSigningStatusQuery, SigningStatusResponse?>
{
    private readonly AppDbContext _db;

    public GetSigningStatusHandler(AppDbContext db) => _db = db;

    public async Task<SigningStatusResponse?> Handle(GetSigningStatusQuery request, CancellationToken cancellationToken)
    {
        var sigReq = await _db.SigningRequests
            .FirstOrDefaultAsync(s => s.Id == request.SigningRequestId, cancellationToken);

        if (sigReq is null) return null;

        // Surface SignedDocumentId only when embed completed so the wallet's
        // status poller can hand the final Guid to DonePage without a second
        // round-trip. Earlier states leave the field null by construction.
        Guid? signedDocumentId = null;
        if (sigReq.Status == SigningRequestStatus.Embedded)
        {
            signedDocumentId = await _db.SignedDocuments
                .Where(sd => sd.SigningRequestId == sigReq.Id)
                .Select(sd => (Guid?)sd.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new SigningStatusResponse(
            sigReq.Id,
            sigReq.Status.ToString(),
            sigReq.DocumentHash,
            sigReq.DocumentId,
            sigReq.CreatedAt,
            sigReq.UpdatedAt,
            sigReq.FailedAtStage,
            signedDocumentId);
    }
}
