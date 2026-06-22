namespace MasterSTI.Shared.DTOs.Documents;

public record DocumentDetailDto(
    Guid Id,
    string FileName,
    string Status,
    string Level,
    DateTime UploadedAt,
    string? SenderName,
    Guid? SignedDocumentId,
    IReadOnlyList<RecipientDto> Recipients,
    IReadOnlyList<SignedStageDto> Stages);

public record SignedStageDto(
    Guid Id,
    int Stage,
    string SignerName,
    DateTime SignedAt,
    string PadesLevel,
    bool IsFinal);
