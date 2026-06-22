using MasterSTI.Shared.DTOs.Audit;
using MediatR;

namespace MasterSTI.Api.Features.Audit;

public record GetAuditQuery(Guid DocumentId) : IRequest<IReadOnlyList<AuditEventDto>>;
