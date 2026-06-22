using MasterSTI.Shared.DTOs.Templates;
using MediatR;

namespace MasterSTI.Api.Features.Templates.ReplacePdf;

public record ReplaceTemplatePdfCommand(Guid Id, IFormFile File) : IRequest<TemplateDto>;
