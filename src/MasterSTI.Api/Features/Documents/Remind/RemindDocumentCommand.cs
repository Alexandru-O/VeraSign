using MediatR;

namespace MasterSTI.Api.Features.Documents.Remind;

public record RemindDocumentCommand(Guid DocumentId) : IRequest<RemindDocumentResponse>;

public record RemindDocumentResponse(Guid DocumentId, int RecipientsNudged, DateTime NudgedAtUtc);
