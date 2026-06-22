namespace MasterSTI.Shared.DTOs.Audit;

public record AuditEventDto(
    Guid Id,
    Guid? DocumentId,
    string EventType,
    string Actor,
    string? IpAddress,
    string? UserAgent,
    DateTime Timestamp,
    string? Metadata);

public record AuditEventListItemDto(
    Guid Id,
    Guid? DocumentId,
    string? DocumentFileName,
    string EventType,
    string Actor,
    DateTime Timestamp,
    string? Metadata);
