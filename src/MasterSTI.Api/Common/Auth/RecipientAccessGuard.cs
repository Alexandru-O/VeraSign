using MasterSTI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Common.Auth;

public sealed class RecipientAccessGuard : IRecipientAccessGuard
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public RecipientAccessGuard(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<bool> CanAccessDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var userId = _user.UserId;
        if (userId is null)
            return false;

        var doc = await _db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.Id, d.OwnerUserId })
            .FirstOrDefaultAsync(cancellationToken);
        if (doc is null)
            return false;

        if (doc.OwnerUserId == userId)
            return true;

        var email = _user.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;
        var emailLower = email.ToLowerInvariant();

        return await _db.Recipients.AsNoTracking()
            .AnyAsync(r => r.DocumentId == documentId
                        && r.Status == RecipientStatus.Notified
                        && r.Email.ToLower() == emailLower,
                cancellationToken);
    }
}
