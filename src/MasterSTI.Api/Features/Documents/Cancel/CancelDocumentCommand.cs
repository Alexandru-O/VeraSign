using MediatR;

namespace MasterSTI.Api.Features.Documents.Cancel;

public record CancelDocumentCommand(Guid DocumentId, string? Reason) : IRequest<CancelDocumentResponse>;

public record CancelDocumentResponse(Guid DocumentId, string Status);
