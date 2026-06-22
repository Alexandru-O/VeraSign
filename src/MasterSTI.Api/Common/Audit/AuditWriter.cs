using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;

namespace MasterSTI.Api.Common.Audit;

public interface IAuditWriter
{
    Task WriteAsync(Guid? documentId, string eventType, string? metadata = null, CancellationToken cancellationToken = default);
}

public sealed class AuditWriter : IAuditWriter
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditWriter> _logger;

    public AuditWriter(
        AppDbContext db,
        ICurrentUserAccessor user,
        IHttpContextAccessor http,
        ILogger<AuditWriter> logger)
    {
        _db = db;
        _user = user;
        _http = http;
        _logger = logger;
    }

    public async Task WriteAsync(Guid? documentId, string eventType, string? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var ctx = _http.HttpContext;
            var ipAddress = ctx?.Connection.RemoteIpAddress?.ToString();
            var userAgent = ctx?.Request.Headers.UserAgent.ToString();

            var entry = new AuditEvent
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                EventType = eventType,
                Actor = _user.DisplayActor,
                IpAddress = string.IsNullOrEmpty(ipAddress) ? null : ipAddress,
                UserAgent = string.IsNullOrEmpty(userAgent) ? null : Truncate(userAgent, 512),
                Timestamp = DateTime.UtcNow,
                Metadata = metadata
            };

            _db.AuditEvents.Add(entry);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit must never throw upward — log and swallow.
            _logger.LogWarning(ex, "Failed to write audit event {EventType} for {DocumentId}", eventType, documentId);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
