using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.Create;

public record CreateTemplateCommand(
    string Title,
    string? Description,
    string Category,
    Guid? FromDocumentId,
    string? FieldsJson,
    string DefaultLevel) : IRequest<TemplateDto>;
