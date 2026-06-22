using MediatR;

namespace MasterSTI.Api.Features.Signing.Status;

public record GetSigningStatusQuery(Guid SigningRequestId) : IRequest<SigningStatusResponse?>;

public record SigningStatusResponse(
    Guid SigningRequestId,
    string Status,
    string DocumentHash,
    Guid DocumentId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int? FailedAtStage = null,
    Guid? SignedDocumentId = null);
