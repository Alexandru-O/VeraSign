namespace MasterSTI.Shared.DTOs.Documents;

/// <summary>
/// Read-only metadata for a finalised <c>SignedDocument</c>, sized for the
/// wallet's Done screen. Source breakdown:
/// <list type="bullet">
///   <item><c>SignedAtUtc</c>, <c>Level</c>, <c>TxnId</c> — direct from the row.</item>
///   <item><c>RequestedLevel</c> — legal level the sender asked for on the
///   <c>Recipient</c> (e.g., <c>QES</c>/<c>AdES</c>/<c>SES</c>); the technical
///   PAdES profile in <c>Level</c> is what actually landed and may diverge
///   (e.g., requested QES, embedded as <c>PAdES-B-T</c> when LTV failed).</item>
///   <item><c>TsaTime</c> — RFC 3161 <c>genTime</c> decoded from
///   <c>SignedDocument.TimestampToken</c>; null when the column is empty.</item>
///   <item><c>TspName</c>, <c>SubjectCn</c>, <c>CertificateSerial</c> —
///   extracted from the signed PDF's signer certificate via iText; null when
///   the PDF cannot be opened.</item>
/// </list>
/// </summary>
public record SignedDocumentInfoDto(
    Guid Id,
    DateTime SignedAtUtc,
    string Level,
    string? TspName,
    string? SubjectCn,
    DateTime? TsaTime,
    string TxnId,
    string? RequestedLevel = null,
    string? CertificateSerial = null);
