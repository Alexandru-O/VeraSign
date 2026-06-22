using MediatR;

namespace MasterSTI.Api.Features.Documents.Delete;

public record DeleteDocumentCommand(Guid DocumentId) : IRequest<DeleteDocumentResponse>;

public record DeleteDocumentResponse(Guid DocumentId);
