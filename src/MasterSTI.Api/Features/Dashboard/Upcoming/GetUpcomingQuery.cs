using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Dashboard.Upcoming;

public record GetUpcomingQuery() : IRequest<IReadOnlyList<UpcomingItemDto>>;
