using MasterSTI.Shared.DTOs.Audit;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Audit;

public record ListAuditQuery(string? Period = "7d", string? EventType = null, int Page = 1, int PageSize = 50)
    : IRequest<PagedResultDto<AuditEventListItemDto>>;
