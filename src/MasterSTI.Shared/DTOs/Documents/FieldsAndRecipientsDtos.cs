namespace MasterSTI.Shared.DTOs.Documents;

public record SignatureFieldDto(
    Guid Id,
    string Type,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    Guid? RecipientId,
    int? RecipientOrder = null);

public record SaveFieldsRequest(IReadOnlyList<SignatureFieldDto> Fields);

public record RecipientDto(
    Guid Id,
    string Email,
    string Name,
    int Order,
    string Level,
    string Status,
    DateTime? NotifiedAt,
    DateTime? SignedAt);

public record SaveRecipientsRequest(IReadOnlyList<RecipientInput> Recipients);

public record RecipientInput(
    Guid? Id,
    string Email,
    string Name,
    int Order,
    string Level);

public record SendDocumentResponse(
    Guid DocumentId,
    string Status,
    int RecipientCount,
    bool AutoStart = false,
    Guid? SigningRequestId = null);
