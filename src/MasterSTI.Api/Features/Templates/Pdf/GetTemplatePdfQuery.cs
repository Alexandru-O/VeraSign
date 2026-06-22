using MediatR;

namespace MasterSTI.Api.Features.Templates.Pdf;

public record GetTemplatePdfQuery(Guid Id) : IRequest<GetTemplatePdfResult>;

public record GetTemplatePdfResult(byte[] Bytes, string FileName);
