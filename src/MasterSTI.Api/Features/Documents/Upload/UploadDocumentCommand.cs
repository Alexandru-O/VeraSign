using MediatR;

namespace MasterSTI.Api.Features.Documents.Upload;

public record UploadDocumentCommand(IFormFile File) : IRequest<UploadDocumentResponse>;

public record UploadDocumentResponse(Guid DocumentId, string FileName, string Sha256Hash);
