using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.UpdateContent;

public record UpdateTemplateContentCommand(Guid Id, string BodyMarkdown) : IRequest<TemplateDto>;
