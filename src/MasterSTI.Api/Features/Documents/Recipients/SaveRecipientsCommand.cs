using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.Recipients;

public record SaveRecipientsCommand(Guid DocumentId, IReadOnlyList<RecipientInput> Recipients) : IRequest<IReadOnlyList<RecipientDto>>;
