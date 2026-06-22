using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Dashboard.Upcoming;

/// <summary>
/// Produces the top items that need the user's attention on the Dashboard sidebar.
/// Three signals are combined, in priority order, capped at three returned rows:
///
/// 1. <b>Overdue documents</b> — <see cref="Document"/> in <see cref="DocumentStatus.Awaiting"/>
///    older than 7 days. Surfaced as a danger reminder.
/// 2. <b>SLA today</b> — <see cref="Document"/> in <see cref="DocumentStatus.Awaiting"/>
///    older than 3 days but not yet overdue. Surfaced as a warning "termen scadent".
/// 3. <b>Idle signers</b> — <see cref="Recipient"/> with <see cref="RecipientStatus.Notified"/>
///    older than 2 days. Surfaced as an info "semnatar nu a deschis" hint.
///
/// All buckets are scoped by the current user's organization. The legacy mock card
/// "Wallet expira in 14 zile" stays out of this slice — once a WalletEnrollment entity
/// lands we will surface it from the user's own enrollment row.
/// </summary>
public sealed class GetUpcomingHandler : IRequestHandler<GetUpcomingQuery, IReadOnlyList<UpcomingItemDto>>
{
    private const int MaxItems = 3;

    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public GetUpcomingHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<IReadOnlyList<UpcomingItemDto>> Handle(GetUpcomingQuery request, CancellationToken cancellationToken)
    {
        var orgId = _user.OrganizationId;
        var now = DateTime.UtcNow;
        var overdueCutoff = now.AddDays(-7);
        var slaCutoff = now.AddDays(-3);
        var idleSignerCutoff = now.AddDays(-2);

        var awaiting = _db.Documents.AsNoTracking().Where(d => d.Status == DocumentStatus.Awaiting);
        if (orgId is not null) awaiting = awaiting.Where(d => d.OrganizationId == orgId);

        var overdue = await awaiting
            .Where(d => d.UploadedAt <= overdueCutoff)
            .OrderBy(d => d.UploadedAt)
            .Take(MaxItems)
            .Select(d => new { d.Id, d.FileName, d.UploadedAt })
            .ToListAsync(cancellationToken);

        var slaToday = await awaiting
            .Where(d => d.UploadedAt > overdueCutoff && d.UploadedAt <= slaCutoff)
            .OrderBy(d => d.UploadedAt)
            .Take(MaxItems)
            .Select(d => new { d.Id, d.FileName, d.UploadedAt })
            .ToListAsync(cancellationToken);

        var idleRecipientsQuery = _db.Recipients.AsNoTracking()
            .Where(r => r.Status == RecipientStatus.Notified
                     && r.NotifiedAt != null
                     && r.NotifiedAt <= idleSignerCutoff);
        if (orgId is not null)
            idleRecipientsQuery = idleRecipientsQuery.Where(r => r.Document.OrganizationId == orgId);

        var idleRecipients = await idleRecipientsQuery
            .OrderBy(r => r.NotifiedAt)
            .Take(MaxItems)
            .Select(r => new { r.DocumentId, r.Name, r.Document.FileName, r.NotifiedAt })
            .ToListAsync(cancellationToken);

        var items = new List<UpcomingItemDto>(MaxItems);

        foreach (var o in overdue)
        {
            if (items.Count >= MaxItems) break;
            var days = (int)Math.Max(1, (now - o.UploadedAt).TotalDays);
            items.Add(new UpcomingItemDto(
                Title: o.FileName,
                Subtitle: $"Restant de {days} zile · reminder necesar",
                Icon: "mail",
                Tone: "danger",
                Badge: "Reminder",
                DocumentId: o.Id));
        }

        foreach (var s in slaToday)
        {
            if (items.Count >= MaxItems) break;
            items.Add(new UpcomingItemDto(
                Title: s.FileName,
                Subtitle: "Termen scadent · semnatar nu a finalizat",
                Icon: "clock",
                Tone: "warning",
                Badge: "Azi",
                DocumentId: s.Id));
        }

        foreach (var r in idleRecipients)
        {
            if (items.Count >= MaxItems) break;
            var days = r.NotifiedAt is null ? 0 : (int)Math.Max(1, (now - r.NotifiedAt.Value).TotalDays);
            items.Add(new UpcomingItemDto(
                Title: r.FileName,
                Subtitle: $"{r.Name} nu a deschis e-mailul · {days} zile",
                Icon: "mail",
                Tone: "info",
                Badge: "Idle",
                DocumentId: r.DocumentId));
        }

        return items;
    }
}
