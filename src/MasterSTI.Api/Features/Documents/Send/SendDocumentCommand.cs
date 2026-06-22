using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Send;

public record SendDocumentCommand(Guid DocumentId) : IRequest<SendDocumentResponse>;
