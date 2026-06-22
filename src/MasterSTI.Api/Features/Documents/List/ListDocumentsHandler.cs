using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Documents.List;

public sealed class ListDocumentsHandler : IRequestHandler<ListDocumentsQuery, PagedResultDto<DocumentListItemDto>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public ListDocumentsHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PagedResultDto<DocumentListItemDto>> Handle(ListDocumentsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _db.Documents.AsNoTracking().AsQueryable();
        var orgId = _user.OrganizationId;
        if (orgId is not null)
            query = query.Where(d => d.OrganizationId == orgId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            // "declined" lives on Recipient, not Document. Treat it as "any recipient declined".
            if (string.Equals(request.Status, "declined", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(d => _db.Recipients
                    .Any(r => r.DocumentId == d.Id && r.Status == RecipientStatus.Declined));
            }
            else if (Enum.TryParse<DocumentStatus>(request.Status, ignoreCase: true, out var parsed))
            {
                query = query.Where(d => d.Status == parsed);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var needle = request.Search.Trim();
            query = query.Where(d => EF.Functions.Like(d.FileName, $"%{needle}%"));
        }

        if (!string.IsNullOrWhiteSpace(request.Level))
        {
            // Match against the first recipient's planned level. Signed-level differentiation
            // (PAdES-B-LT vs B-T) is a polish item; current filter is good enough for demo.
            var lvl = request.Level.Trim();
            query = query.Where(d => _db.Recipients
                .Where(r => r.DocumentId == d.Id)
                .OrderBy(r => r.Order)
                .Select(r => r.Level)
                .FirstOrDefault() == lvl);
        }

        if (!string.IsNullOrWhiteSpace(request.Period))
        {
            var days = request.Period.Trim().ToLowerInvariant() switch
            {
                "7d"  => 7,
                "30d" => 30,
                "90d" => 90,
                _     => 0
            };
            if (days > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-days);
                query = query.Where(d => d.UploadedAt >= cutoff);
            }
        }

        var total = await query.CountAsync(cancellationToken);

        // Project to a tuple with primary recipient + signing level via subqueries so we
        // round-trip in a single DB call. EF Core translates this to LEFT JOIN LATERAL on
        // SQL Server (FIRST/TOP 1 subselect).
        var rows = await query
            .OrderByDescending(d => d.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.Status,
                d.UploadedAt,
                PrimaryRecipient = _db.Recipients
                    .Where(r => r.DocumentId == d.Id)
                    .OrderBy(r => r.Order)
                    .Select(r => r.Name)
                    .FirstOrDefault(),
                FirstRecipientLevel = _db.Recipients
                    .Where(r => r.DocumentId == d.Id)
                    .OrderBy(r => r.Order)
                    .Select(r => r.Level)
                    .FirstOrDefault(),
                SignedDocumentId = (Guid?)_db.SignedDocuments
                    .Where(s => s.OriginalDocumentId == d.Id)
                    .OrderByDescending(s => s.SignedAt)
                    .Select(s => s.Id)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new DocumentListItemDto(
            Id: r.Id,
            Name: r.FileName,
            RecipientPrimary: r.PrimaryRecipient,
            Level: string.IsNullOrWhiteSpace(r.FirstRecipientLevel) ? "AdES" : r.FirstRecipientLevel!,
            Status: r.Status.ToString(),
            UpdatedAt: r.UploadedAt,
            SignedDocumentId: r.SignedDocumentId == Guid.Empty ? null : r.SignedDocumentId))
            .ToList();

        return new PagedResultDto<DocumentListItemDto>(items, page, pageSize, total);
    }
}
