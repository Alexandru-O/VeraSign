namespace MasterSTI.Shared.DTOs;

public record DocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    string Sha256Hash,
    DateTime UploadedAt,
    string Status
);

