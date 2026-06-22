using MasterSTI.Shared.DTOs.Signing;
using MediatR;

namespace MasterSTI.Api.Features.Signing.GetTechnicalDetail;

public record GetTechnicalDetailQuery(Guid SigningRequestId) : IRequest<TechnicalDetailDto?>;
