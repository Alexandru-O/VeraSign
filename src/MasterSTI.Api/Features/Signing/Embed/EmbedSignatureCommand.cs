using MediatR;

namespace MasterSTI.Api.Features.Signing.Embed;

public record EmbedSignatureCommand(Guid SigningRequestId, string CmsSignatureBase64) : IRequest<EmbedSignatureResponse>;

public record EmbedSignatureResponse(Guid SignedDocumentId, string PadesLevel);
