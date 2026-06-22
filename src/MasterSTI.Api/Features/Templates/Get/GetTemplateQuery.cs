using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.Get;

public record GetTemplateQuery(Guid Id) : IRequest<TemplateDto>;
