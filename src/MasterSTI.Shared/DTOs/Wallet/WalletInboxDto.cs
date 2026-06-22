namespace MasterSTI.Shared.DTOs.Wallet;

/// <summary>
/// One pending signing invitation visible to the wallet's current user.
/// Cross-organisation: the wallet user is matched to the Recipient row by
/// PID email, irrespective of which org's sender created the Document.
/// </summary>
public record WalletInboxItemDto(
    Guid DocumentId,
    Guid RecipientId,
    string DocumentName,
    string SenderName,
    DateTime NotifiedAt,
    string Level,
    string DeepLink);

public record WalletInboxResponse(IReadOnlyList<WalletInboxItemDto> Items);

/// <summary>
/// Per-Recipient metadata surfaced on the wallet's Review screen. Sourced from
/// the real Document + Sender so the wallet stops rendering hardcoded fixtures.
/// <c>Hash</c> is the full 64-char hex SHA-256 of the uploaded PDF; the wallet
/// formats it for display. <c>SizeBytes</c> is the on-disk PDF size; <c>Pages</c>
/// is the PDF page count extracted at request time (0 when unavailable).
/// </summary>
public record WalletInboxItemMetaDto(
    string DocumentName,
    string SenderName,
    int Pages,
    string Level,
    string Hash,
    long SizeBytes);
