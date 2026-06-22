using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.Documents.FromTemplate;

public record CreateFromTemplateCommand(Guid TemplateId) : IRequest<DocumentFromTemplateResponse>;
