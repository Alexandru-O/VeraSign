using MediatR;

namespace MasterSTI.Api.Features.Templates.Delete;

public record DeleteTemplateCommand(Guid Id) : IRequest;
