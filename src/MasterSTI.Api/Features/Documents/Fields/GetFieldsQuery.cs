using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Fields;

public record GetFieldsQuery(Guid DocumentId) : IRequest<IReadOnlyList<SignatureFieldDto>>;
