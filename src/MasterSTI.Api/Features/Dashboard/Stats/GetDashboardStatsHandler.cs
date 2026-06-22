using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MasterSTI.Api.Features.Dashboard.Stats;

/// <summary>
/// Aggregates the dashboard KPIs from Documents, SigningRequests, SignedDocuments
/// and Recipients. Results are memoized for 30 seconds per (org, range) so the
/// dashboard does not hammer the DB on quick refreshes / circuit reconnects.
/// </summary>
public sealed class GetDashboardStatsHandler : IRequestHandler<GetDashboardStatsQuery, DashboardStatsDto>
{
    private static readonly string[] WeekLabelsRo = { "L", "M", "M", "J", "V", "S", "D" };
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IMemoryCache _cache;

    public GetDashboardStatsHandler(AppDbContext db, ICurrentUserAccessor user, IMemoryCache cache)
    {
        _db = db;
        _user = user;
        _cache = cache;
    }

    public async Task<DashboardStatsDto> Handle(GetDashboardStatsQuery request, CancellationToken cancellationToken)
    {
        var range = NormalizeRange(request.Range);
        var orgId = _user.OrganizationId;
        var key = DashboardCacheKeys.Stats(orgId, range);

        if (_cache.TryGetValue<DashboardStatsDto>(key, out var cached) && cached is not null)
            return cached;

        var stats = await ComputeAsync(range, orgId, cancellationToken);
        _cache.Set(key, stats, CacheTtl);
        return stats;
    }

    private async Task<DashboardStatsDto> ComputeAsync(string range, Guid? orgId, CancellationToken ct)
    {
        var (windowDays, _) = ParseRange(range);
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-windowDays);
        var prevWindowStart = windowStart.AddDays(-windowDays);

        var docs = _db.Documents.AsNoTracking();
        if (orgId is not null) docs = docs.Where(d => d.OrganizationId == orgId);

        var inWindow      = docs.Where(d => d.UploadedAt >= windowStart);
        var inPrevWindow  = docs.Where(d => d.UploadedAt >= prevWindowStart && d.UploadedAt < windowStart);

        var sent          = await inWindow.CountAsync(ct);
        var prevSent      = await inPrevWindow.CountAsync(ct);
        var sentDelta     = sent - prevSent;
        var pending       = await inWindow.CountAsync(d => d.Status == DocumentStatus.Awaiting, ct);
        var failed        = await inWindow.CountAsync(d => d.Status == DocumentStatus.Failed, ct);

        // "QES via Wallet" KPI must match the filter card it deep-links to
        // (/documents?status=signed&level=QES), which is document-grained.
        // Counting Recipients overcounts multi-signer documents (one doc with two
        // QES signers contributes 2). Count distinct Documents that have at least
        // one in-window QES signature and have reached Signed status, so KPI value
        // and filtered-list row count stay in sync.
        var walletQesQuery = _db.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Signed
                     && _db.Recipients.Any(r => r.DocumentId == d.Id
                                             && r.Level == "QES"
                                             && r.Status == RecipientStatus.Signed
                                             && r.SignedAt != null
                                             && r.SignedAt >= windowStart));

        if (orgId is not null)
            walletQesQuery = walletQesQuery.Where(d => d.OrganizationId == orgId);

        var walletQesCount = await walletQesQuery.CountAsync(ct);

        var declinedRecipients = _db.Recipients.AsNoTracking()
            .Where(r => r.Status == RecipientStatus.Declined
                     && r.Document.UploadedAt >= windowStart);
        if (orgId is not null)
            declinedRecipients = declinedRecipients.Where(r => r.Document.OrganizationId == orgId);

        var declined = await declinedRecipients.CountAsync(ct) + failed;
        var rejectionRate = sent == 0 ? 0.0 : Math.Round((double)declined / sent * 100.0, 1);

        // Urgent today: pending documents older than 5 days (proxy for SLA-due-today
        // until we have a real DueAt column).
        var fiveDaysAgo = now.AddDays(-5);
        var urgentToday = await inWindow.CountAsync(d =>
            d.Status == DocumentStatus.Awaiting && d.UploadedAt <= fiveDaysAgo, ct);

        var weekValues = await BuildLast7DaysSeriesAsync(orgId, now, ct);

        var prevTotal = prevSent == 0 ? (int?)null : prevSent;

        return new DashboardStatsDto(
            Range: range,
            Sent: sent,
            WalletQesSignatures: walletQesCount,
            WalletKeyBindingRate: walletQesCount == 0 ? 0.0 : 100.0,
            Pending: pending,
            UrgentToday: urgentToday,
            Declined: declined,
            RejectionRate: rejectionRate,
            SentDeltaCount: sentDelta,
            WeekValues: weekValues,
            WeekLabels: WeekLabelsRo,
            PreviousPeriodTotal: prevTotal);
    }

    private async Task<int[]> BuildLast7DaysSeriesAsync(Guid? orgId, DateTime now, CancellationToken ct)
    {
        var start = now.Date.AddDays(-6);
        var query = _db.SignedDocuments.AsNoTracking().Where(s => s.SignedAt >= start);
        if (orgId is not null)
            query = query.Where(s => s.OriginalDocument.OrganizationId == orgId);

        var groups = await query
            .GroupBy(s => s.SignedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byDay = groups.ToDictionary(g => g.Day, g => g.Count);

        var bars = new int[7];
        for (var i = 0; i < 7; i++)
        {
            var day = start.AddDays(i).Date;
            bars[i] = byDay.TryGetValue(day, out var c) ? c : 0;
        }
        return bars;
    }

    private static string NormalizeRange(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "30d";
        var r = raw.Trim().ToLowerInvariant();
        return r is "7d" or "30d" or "90d" ? r : "30d";
    }

    private static (int Days, string Label) ParseRange(string range) => range switch
    {
        "7d"  => (7,  "Ultimele 7 zile"),
        "90d" => (90, "Ultimele 90 zile"),
        _     => (30, "Ultimele 30 zile"),
    };
}
