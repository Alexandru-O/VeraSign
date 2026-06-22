using MediatR;
using MasterSTI.Api.Common.Eudiw;

namespace MasterSTI.Api.Features.Eudiw.RequestPresentation;

public record RequestPresentationCommand(Guid SigningRequestId) : IRequest<EudiwRequestResult>;
