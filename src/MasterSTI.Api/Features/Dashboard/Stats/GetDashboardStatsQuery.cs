using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Dashboard.Stats;

public record GetDashboardStatsQuery(string Range) : IRequest<DashboardStatsDto>;
