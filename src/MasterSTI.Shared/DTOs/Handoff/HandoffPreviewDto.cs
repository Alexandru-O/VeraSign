namespace MasterSTI.Shared.DTOs.Handoff;

public record HandoffPreviewRequest(string Token);

public record HandoffPreviewResponse(
    Guid DocumentId,
    string DocumentName,
    string SenderName,
    string RecipientName,
    string RecipientEmail,
    string RecipientStatus,
    string Level,
    bool TokenValid,
    bool StatusActive,
    DateTime ExpiresAt);
