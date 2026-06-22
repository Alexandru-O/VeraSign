using MediatR;

namespace MasterSTI.Api.Features.Signing.Sign;

/// <summary>
/// SAD is transient — never persisted to DB, logs, or response fields.
/// It exists only in RAM for the duration of this request.
/// </summary>
public record SignDocumentCommand(Guid SigningRequestId, string Pin, string Factor = "pin") : IRequest<SignDocumentResponse>;

public record SignDocumentResponse(Guid SignedDocumentId, string PadesLevel);
