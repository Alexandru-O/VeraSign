using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.List;

public record ListTemplatesQuery(string? Category) : IRequest<IReadOnlyList<TemplateDto>>;
