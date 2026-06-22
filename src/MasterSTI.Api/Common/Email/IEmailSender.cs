namespace MasterSTI.Api.Common.Email;

/// <summary>
/// Transport-agnostic seam for outbound notifications. The dissertation
/// prototype wires <c>MockEmailSender</c>, which logs and writes an
/// <c>AuditEvent EventType=EmailSent</c>; a real deployment would back this
/// with an SMTP / SendGrid / Postmark client.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
    Guid DocumentId,
    string To,
    string Subject,
    string BodyMarkdown,
    string DeepLink);
