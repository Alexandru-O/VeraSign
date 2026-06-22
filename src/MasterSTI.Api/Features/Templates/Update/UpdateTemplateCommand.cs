using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.Update;

public record UpdateTemplateCommand(
    Guid Id,
    string Title,
    string? Description,
    string Category,
    string? FieldsJson,
    string DefaultLevel) : IRequest<TemplateDto>;
