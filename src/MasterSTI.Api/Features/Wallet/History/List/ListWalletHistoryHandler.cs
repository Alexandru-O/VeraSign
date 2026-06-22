using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Wallet;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Wallet.History.List;

/// <summary>
/// Lists SignedDocuments for which the wallet user was the signing Recipient.
/// Match is by <c>WalletEnrollment.PidEmail = Recipient.Email</c> (both
/// lowercased) scoped to the current Wallet Session user. Cross-organisation
/// by design — mirrors the Wallet Inbox slice. See
/// <c>CONTEXT.md → Wallet Inbox</c>.
/// </summary>
public sealed class ListWalletHistoryHandler : IRequestHandler<ListWalletHistoryQuery, List<WalletHistoryItemDto>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public ListWalletHistoryHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<List<WalletHistoryItemDto>> Handle(ListWalletHistoryQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId;
        if (userId is null)
            return new List<WalletHistoryItemDto>();

        var enrollment = await _db.WalletEnrollments.AsNoTracking()
            .Where(w => w.UserId == userId && w.PidEmail != null)
            .Select(w => new { w.PidEmail })
            .FirstOrDefaultAsync(cancellationToken);

        if (enrollment is null || string.IsNullOrEmpty(enrollment.PidEmail))
            return new List<WalletHistoryItemDto>();

        var pidEmail = enrollment.PidEmail;

        var rows = await _db.SignedDocuments.AsNoTracking()
            .Where(sd => sd.Recipient.Email.ToLower() == pidEmail)
            .OrderByDescending(sd => sd.SignedAt)
            .Select(sd => new HistoryRow(
                sd.Id,
                sd.OriginalDocumentId,
                sd.OriginalDocument.FileName,
                sd.OriginalDocument.OwnerUserId,
                sd.SignedAt,
                sd.Recipient.Level))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return new List<WalletHistoryItemDto>();

        var ownerIds = rows.Where(r => r.OwnerUserId.HasValue).Select(r => r.OwnerUserId!.Value).Distinct().ToArray();
        var owners = await _db.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.Email })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return rows.Select(r =>
        {
            var sender = r.OwnerUserId.HasValue && owners.TryGetValue(r.OwnerUserId.Value, out var u)
                ? (string.IsNullOrWhiteSpace(u.Name) ? u.Email : u.Name)
                : "—";
            return new WalletHistoryItemDto(
                r.DocumentId,
                r.FileName,
                sender,
                DateTime.SpecifyKind(r.SignedAt, DateTimeKind.Utc),
                r.Level,
                r.SignedDocumentId);
        }).ToList();
    }

    private sealed record HistoryRow(
        Guid SignedDocumentId,
        Guid DocumentId,
        string FileName,
        Guid? OwnerUserId,
        DateTime SignedAt,
        string Level);
}
