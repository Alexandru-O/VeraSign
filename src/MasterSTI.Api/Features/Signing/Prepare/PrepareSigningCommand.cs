using MediatR;

namespace MasterSTI.Api.Features.Signing.Prepare;

public record PrepareSigningCommand(
    Guid DocumentId,
    Guid RecipientId,
    string RequestedBy,
    string CredentialId,
    RenderCommitmentInputs? RenderCommitment = null) : IRequest<PrepareSigningResponse>;

public record PrepareSigningResponse(Guid SigningRequestId, string DocumentHash);
