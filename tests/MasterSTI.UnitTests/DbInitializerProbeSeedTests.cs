using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasterSTI.UnitTests;

public class DbInitializerProbeSeedTests : IDisposable
{
    private readonly AppDbContext _db;

    public DbInitializerProbeSeedTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ProbeSeedTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Seed_inserts_84_buckets_per_node()
    {
        await DbInitializer.SeedProbeResultsAsync(_db, NullLogger.Instance, default);

        var rows = await _db.ProbeResults.AsNoTracking().ToListAsync();

        Assert.Equal(504, rows.Count);

        var expectedNodes = new[] { "qtsp", "tsa", "ocsp", "ltv", "issuer", "api" };
        foreach (var node in expectedNodes)
        {
            var nodeRows = rows.Where(r => r.Node == node).ToList();
            Assert.Equal(84, nodeRows.Count);
        }
    }

    [Fact]
    public async Task Seed_spans_seven_days_ending_near_now()
    {
        var now = DateTime.UtcNow;

        await DbInitializer.SeedProbeResultsAsync(_db, NullLogger.Instance, default);

        var rows = await _db.ProbeResults.AsNoTracking().ToListAsync();
        var min = rows.Min(r => r.Timestamp);
        var max = rows.Max(r => r.Timestamp);

        // First bucket midpoint = start + 1h, last = end - 1h. Allow small slop
        // for the now() drift between seed-time and assertion-time.
        Assert.True(min >= now - TimeSpan.FromDays(7));
        Assert.True(min <= now - TimeSpan.FromDays(7) + TimeSpan.FromHours(3));
        Assert.True(max <= now);
        Assert.True(max >= now - TimeSpan.FromHours(3));
    }

    [Fact]
    public async Task Seed_is_idempotent_when_rows_exist()
    {
        await DbInitializer.SeedProbeResultsAsync(_db, NullLogger.Instance, default);
        var firstCount = await _db.ProbeResults.CountAsync();

        // Second invocation must be a no-op so live ProbeWriter data is never
        // overwritten by a re-seed on container restart.
        await DbInitializer.SeedProbeResultsAsync(_db, NullLogger.Instance, default);
        var secondCount = await _db.ProbeResults.CountAsync();

        Assert.Equal(firstCount, secondCount);
    }

    [Fact]
    public async Task Seed_health_is_mostly_ok_with_some_warn_and_err()
    {
        await DbInitializer.SeedProbeResultsAsync(_db, NullLogger.Instance, default);

        var rows = await _db.ProbeResults.AsNoTracking().ToListAsync();

        // Weighted seed produces majority ok overall (per-node weights start
        // at 90% ok). 504 rows × ≥90% = ≥453, well above 80% threshold.
        var ok = rows.Count(r => r.Health == "ok");
        Assert.True(ok > rows.Count * 0.80, $"Expected >80% ok, got {ok}/{rows.Count}");

        // Every recognised health value must be a known token.
        foreach (var r in rows)
            Assert.Contains(r.Health, new[] { "ok", "warn", "err" });
    }
}
