using MasterSTI.Api.Common.Eudiw;
using MediatR;

namespace MasterSTI.Api.Features.Eudiw.HandleResponse;

public record HandleVpResponseCommand(string VpToken, string State) : IRequest<HandleVpResponseResult>;

public record HandleVpResponseResult(bool Success, Guid? SigningRequestId, string? Error);
