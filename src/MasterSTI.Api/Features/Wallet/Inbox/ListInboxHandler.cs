using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Wallet;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Wallet.Inbox;

/// <summary>
/// Lists Recipients currently awaiting the wallet user's signature. Match is
/// by <c>WalletEnrollment.PidEmail = Recipient.Email</c> (both lowercased)
/// scoped to the current Wallet Session user. Cross-organisation by design —
/// see <c>CONTEXT.md → Wallet Inbox</c>.
/// </summary>
public sealed class ListInboxHandler : IRequestHandler<ListInboxQuery, WalletInboxResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IHandoffTokenService _handoff;

    public ListInboxHandler(AppDbContext db, ICurrentUserAccessor user, IHandoffTokenService handoff)
    {
        _db = db;
        _user = user;
        _handoff = handoff;
    }

    public async Task<WalletInboxResponse> Handle(ListInboxQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId;
        if (userId is null)
            return new WalletInboxResponse(Array.Empty<WalletInboxItemDto>());

        var enrollment = await _db.WalletEnrollments.AsNoTracking()
            .Where(w => w.UserId == userId && w.PidEmail != null)
            .Select(w => new { w.PidEmail })
            .FirstOrDefaultAsync(cancellationToken);

        if (enrollment is null || string.IsNullOrEmpty(enrollment.PidEmail))
            return new WalletInboxResponse(Array.Empty<WalletInboxItemDto>());

        var pidEmail = enrollment.PidEmail;

        var rows = await _db.Recipients.AsNoTracking()
            .Where(r => r.Status == RecipientStatus.Notified
                     && r.Document.Status == DocumentStatus.Awaiting
                     && r.Email.ToLower() == pidEmail)
            .OrderByDescending(r => r.NotifiedAt)
            .Select(r => new InboxRow(
                r.Id,
                r.DocumentId,
                r.Document.FileName,
                r.Document.OwnerUserId,
                r.NotifiedAt!.Value,
                r.Level))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return new WalletInboxResponse(Array.Empty<WalletInboxItemDto>());

        var ownerIds = rows.Where(r => r.OwnerUserId.HasValue).Select(r => r.OwnerUserId!.Value).Distinct().ToArray();
        var owners = await _db.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.Email })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var items = rows.Select(r =>
        {
            var sender = r.OwnerUserId.HasValue && owners.TryGetValue(r.OwnerUserId.Value, out var u)
                ? (string.IsNullOrWhiteSpace(u.Name) ? u.Email : u.Name)
                : "—";
            var token = _handoff.Issue(r.RecipientId, r.DocumentId);
            var deepLink = $"verasign://sign?token={token}";
            return new WalletInboxItemDto(
                r.DocumentId,
                r.RecipientId,
                r.FileName,
                sender,
                r.NotifiedAt,
                r.Level,
                deepLink);
        }).ToList();

        return new WalletInboxResponse(items);
    }

    private sealed record InboxRow(
        Guid RecipientId,
        Guid DocumentId,
        string FileName,
        Guid? OwnerUserId,
        DateTime NotifiedAt,
        string Level);
}
