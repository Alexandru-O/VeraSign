namespace MasterSTI.Shared.DTOs.Wallet;

/// <summary>
/// One signed document visible to the wallet's current user in the History tab.
/// Cross-organisation: the wallet user is matched to the Recipient row by PID
/// email, irrespective of which org's sender created the Document.
/// </summary>
public record WalletHistoryItemDto(
    Guid DocumentId,
    string DocumentName,
    string SenderName,
    DateTime SignedAtUtc,
    string Level,
    Guid SignedDocumentId);
