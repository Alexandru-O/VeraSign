using System.Text.Json;
using MasterSTI.Api.Common.Audit;

namespace MasterSTI.Api.Common.Email;

/// <summary>
/// Demo-grade email transport. Writes an <c>AuditEvent EventType=EmailSent</c>
/// with a body preview and the deep link, plus a Serilog info line. No actual
/// SMTP traffic — recipients in the dissertation prototype "receive" emails
/// only by reading the audit log in Settings · Audit.
/// </summary>
public sealed class MockEmailSender : IEmailSender
{
    private const int BodyPreviewMaxChars = 240;

    private readonly IAuditWriter _audit;
    private readonly ILogger<MockEmailSender> _logger;

    public MockEmailSender(IAuditWriter audit, ILogger<MockEmailSender> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var preview = Truncate(message.BodyMarkdown, BodyPreviewMaxChars);

        var metadata = JsonSerializer.Serialize(new
        {
            to = message.To,
            subject = message.Subject,
            bodyPreview = preview,
            deepLink = message.DeepLink
        });

        await _audit.WriteAsync(message.DocumentId, "EmailSent", metadata, cancellationToken);

        _logger.LogInformation(
            "[MockEmail] To={To} Subject=\"{Subject}\" DeepLink={DeepLink}",
            message.To, message.Subject, message.DeepLink);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty
         : s.Length <= max ? s
         : s.Substring(0, max);
}
