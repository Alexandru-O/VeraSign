using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MasterSTI.Api.Features.Dashboard.PipelineStats;

/// <summary>
/// Aggregates the 24h counters for the 5-stage signing pipeline by looking at
/// <see cref="SigningRequest"/> rows whose <see cref="SigningRequest.UpdatedAt"/>
/// falls inside the window and bucketing them by the highest <see cref="SigningRequestStatus"/>
/// they have reached. Stage 1 = EUDIW auth ... Stage 5 = PAdES embed.
///
/// Health is computed per stage:
///   ok   when the stage has at least one request and no failure is attributed to it,
///   warn when the stage has zero requests in the window,
///   err  when the global failure ratio for the window is &gt;= 5% (signals an unstable pipeline).
/// </summary>
public sealed class GetPipelineStatsHandler : IRequestHandler<GetPipelineStatsQuery, PipelineStatsDto>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IMemoryCache _cache;

    public GetPipelineStatsHandler(AppDbContext db, ICurrentUserAccessor user, IMemoryCache cache)
    {
        _db = db;
        _user = user;
        _cache = cache;
    }

    public async Task<PipelineStatsDto> Handle(GetPipelineStatsQuery request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;
        var key = DashboardCacheKeys.Pipeline(orgId);

        if (_cache.TryGetValue<PipelineStatsDto>(key, out var cached) && cached is not null)
            return cached;

        var dto = await ComputeAsync(orgId, cancellationToken);
        _cache.Set(key, dto, CacheTtl);
        return dto;
    }

    private async Task<PipelineStatsDto> ComputeAsync(Guid? orgId, CancellationToken ct)
    {
        var to = DateTime.UtcNow;
        var from = to.AddHours(-24);

        var query = _db.SigningRequests.AsNoTracking()
            .Where(s => s.UpdatedAt >= from);
        if (orgId is not null)
            query = query.Where(s => s.Document.OrganizationId == orgId);

        // Bring just the columns we need across the wire.
        var rows = await query
            .Select(s => new { s.Status, s.EudiwSubject, s.FailedAtStage })
            .ToListAsync(ct);

        // Cumulative counters per stage. A request "reached" a stage if its terminal
        // status implies that stage's work completed. We treat EUDIW auth (1) and
        // SD-JWT verify (2) identically because the schema collapses both into the
        // EudiwAuthorized transition — KB-JWT verification is what flips that status.
        var s1 = 0; var s2 = 0; var s3 = 0; var s4 = 0; var s5 = 0;
        var failed = 0;
        var f1 = 0; var f2 = 0; var f3 = 0; var f4 = 0; var f5 = 0;

        foreach (var r in rows)
        {
            switch (r.Status)
            {
                case SigningRequestStatus.Pending:
                case SigningRequestStatus.HashPrepared:
                    // Hash prepared without an EUDIW presentation. Counted under stage 1
                    // only if a wallet interaction already produced an EudiwSubject.
                    if (r.EudiwSubject is not null) { s1++; s2++; }
                    break;
                case SigningRequestStatus.EudiwAuthorized:
                    s1++; s2++;
                    break;
                case SigningRequestStatus.CredentialAuthorized:
                    s1++; s2++; s3++;
                    break;
                case SigningRequestStatus.Signed:
                    s1++; s2++; s3++; s4++;
                    break;
                case SigningRequestStatus.Embedded:
                    s1++; s2++; s3++; s4++; s5++;
                    break;
                case SigningRequestStatus.Failed:
                    failed++;
                    switch (r.FailedAtStage)
                    {
                        case 1: f1++; break;
                        case 2: f2++; break;
                        case 3: f3++; break;
                        case 4: f4++; break;
                        case 5: f5++; break;
                    }
                    break;
            }
        }

        // Global health-degrade threshold: warn the user when >= 5% of in-window
        // signing requests ended in Failed.
        var total = rows.Count;
        var globallyDegraded = total > 0 && (double)failed / total >= 0.05;

        var stages = new[]
        {
            new PipelineStageDto(1, "auth",    s1, Health(s1, f1, globallyDegraded), f1),
            new PipelineStageDto(2, "sdjwt",   s2, Health(s2, f2, globallyDegraded), f2),
            new PipelineStageDto(3, "consent", s3, Health(s3, f3, globallyDegraded), f3),
            new PipelineStageDto(4, "sign",    s4, Health(s4, f4, globallyDegraded), f4),
            new PipelineStageDto(5, "embed",   s5, Health(s5, f5, globallyDegraded), f5),
        };

        return new PipelineStatsDto(
            Window: "24h",
            From: from,
            To: to,
            Stages: stages,
            FailedTotal: failed);
    }

    /// <summary>
    /// Per-stage health: "err" if at least one Failed row was attributed here OR
    /// the global pipeline is degraded (≥5% failures); "warn" if no traffic in the
    /// window; "ok" otherwise.
    /// </summary>
    private static string Health(int count, int failedHere, bool globallyDegraded)
    {
        if (failedHere > 0) return "err";
        if (globallyDegraded) return "err";
        return count == 0 ? "warn" : "ok";
    }
}
