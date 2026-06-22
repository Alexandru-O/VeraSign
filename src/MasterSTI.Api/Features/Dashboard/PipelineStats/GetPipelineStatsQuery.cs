using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Dashboard.PipelineStats;

public record GetPipelineStatsQuery() : IRequest<PipelineStatsDto>;
