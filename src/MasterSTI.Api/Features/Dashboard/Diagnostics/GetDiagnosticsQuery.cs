using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Dashboard.Diagnostics;

public record GetDiagnosticsQuery() : IRequest<DiagnosticsDto>;
