namespace MasterSTI.Shared.DTOs.Templates;

public record TemplateDto(
    Guid Id,
    Guid OrganizationId,
    string Title,
    string? Description,
    string Category,
    string? PdfPath,
    string? FieldsJson,
    string? BodyMarkdown,
    string DefaultLevel,
    int UsageCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateTemplateRequest(
    string Title,
    string? Description,
    string Category,
    Guid? FromDocumentId,
    string? FieldsJson,
    string DefaultLevel);

public record UpdateTemplateRequest(
    string Title,
    string? Description,
    string Category,
    string? FieldsJson,
    string DefaultLevel);

public record UpdateTemplateContentRequest(string BodyMarkdown);
