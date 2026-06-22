using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Detail;

public record GetDocumentDetailQuery(Guid DocumentId) : IRequest<DocumentDetailDto?>;
