using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Fields;

public record SaveFieldsCommand(Guid DocumentId, IReadOnlyList<SignatureFieldDto> Fields) : IRequest<IReadOnlyList<SignatureFieldDto>>;
