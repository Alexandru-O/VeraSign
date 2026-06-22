using Microsoft.Extensions.Caching.Memory;

namespace MasterSTI.Api.Common.Caching;

/// <summary>
/// Single source of truth for the IMemoryCache keys used by the Dashboard handlers.
/// Read sites (Stats/Pipeline) and the invalidator must agree on the format —
/// breaking changes here ripple to every cache hit/miss path.
/// </summary>
public static class DashboardCacheKeys
{
    public static readonly string[] StatsRanges = { "7d", "30d", "90d" };

    public static string Stats(Guid? orgId, string range) => $"dash:stats:{Scope(orgId)}:{range}";
    public static string Pipeline(Guid? orgId) => $"dash:pipeline:{Scope(orgId)}";

    private static string Scope(Guid? orgId) => orgId?.ToString() ?? "global";
}

/// <summary>
/// Removes the per-org Dashboard cache entries so the next read recomputes from
/// the DB. Used by mutation handlers (Upload/Send/Sign/Embed) so users do not
/// have to wait for the 30s TTL after acting.
/// </summary>
public interface IDashboardCacheInvalidator
{
    void InvalidateOrg(Guid? orgId);
}

public sealed class DashboardCacheInvalidator : IDashboardCacheInvalidator
{
    private readonly IMemoryCache _cache;

    public DashboardCacheInvalidator(IMemoryCache cache) => _cache = cache;

    public void InvalidateOrg(Guid? orgId)
    {
        foreach (var range in DashboardCacheKeys.StatsRanges)
            _cache.Remove(DashboardCacheKeys.Stats(orgId, range));
        _cache.Remove(DashboardCacheKeys.Pipeline(orgId));
    }
}
