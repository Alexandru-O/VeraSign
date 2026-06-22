using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Recipients;

public record GetRecipientsQuery(Guid DocumentId) : IRequest<IReadOnlyList<RecipientDto>>;
