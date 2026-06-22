using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Dashboard.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Common.Diagnostics;

/// <summary>
/// Periodic infrastructure probe writer. Every <see cref="Interval"/> the service
/// resolves the same <see cref="GetDiagnosticsQuery"/> handler the dashboard
/// uses, appends one row per node to <c>ProbeResults</c>, and prunes samples
/// older than <see cref="Retention"/> so the sparkline never grows unbounded.
/// The dashboard sparkline reads from this table.
/// </summary>
public sealed class ProbeWriterService : BackgroundService
{
    public static readonly TimeSpan Interval  = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly IServiceProvider _services;
    private readonly ILogger<ProbeWriterService> _log;

    public ProbeWriterService(IServiceProvider services, ILogger<ProbeWriterService> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First sample on a small delay so the host has finished startup
        // (migrations, seeding) before the first DB write.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);

        // Fire one tick immediately so the sparkline starts populating without
        // waiting a full interval after host start.
        await TickAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TickAsync(stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var dto = await mediator.Send(new GetDiagnosticsQuery(), ct);

            foreach (var node in dto.Nodes)
            {
                db.ProbeResults.Add(new ProbeResult
                {
                    Node = node.Key,
                    Timestamp = dto.ProbedAt,
                    Health = node.Health,
                    RttMs = node.RttMs
                });
            }

            var cutoff = DateTime.UtcNow - Retention;
            await db.ProbeResults
                .Where(p => p.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);

            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { /* host shutdown */ }
        catch (Exception ex)
        {
            // Swallow + log — never let a transient probe failure crash the host.
            _log.LogWarning(ex, "ProbeWriterService tick failed");
        }
    }
}
