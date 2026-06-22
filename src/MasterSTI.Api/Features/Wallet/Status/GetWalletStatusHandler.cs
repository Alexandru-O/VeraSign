using System.Security.Cryptography;
using System.Text;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Wallet.Status;

/// <summary>
/// Returns the current user's EUDIW enrollment summary. Prefers the real
/// <see cref="WalletEnrollment"/> row (written when an SD-JWT with cnf.jwk was
/// validated). Falls back to deriving the payload from the most recent
/// SigningRequest carrying an EudiwSubject, and finally to a synthetic record
/// seeded from the User row so the dashboard banner still renders in demo mode.
/// </summary>
public sealed class GetWalletStatusHandler : IRequestHandler<GetWalletStatusQuery, WalletStatusDto>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public GetWalletStatusHandler(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task<WalletStatusDto> Handle(GetWalletStatusQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId;
        if (userId is null)
            return new WalletStatusDto(false, null, null, null, null);

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return new WalletStatusDto(false, null, null, null, null);

        // Real path: WalletEnrollment row written by HandleVpResponseHandler when
        // the SD-JWT carried a cnf.jwk.
        var enrollment = await _db.WalletEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);
        if (enrollment is not null)
        {
            return new WalletStatusDto(
                Enrolled: true,
                HolderName: user.Name,
                IssuedAt: enrollment.IssuedAt,
                ExpiresAt: enrollment.ExpiresAt,
                CnfThumbprint: enrollment.CnfJwkThumbprint);
        }

        // Fallback path (no real enrollment yet): synthesize from SigningRequests +
        // User so the banner still renders in demo mode.
        var lastEudiw = await _db.SigningRequests.AsNoTracking()
            .Where(s => s.EudiwSubject != null && s.Document.OrganizationId == user.OrganizationId)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new { s.EudiwSubject, s.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var issuedAt = (lastEudiw?.UpdatedAt ?? user.CreatedAt).ToUniversalTime();
        var expiresAt = issuedAt.AddDays(365);
        var subject = lastEudiw?.EudiwSubject ?? user.Email;
        var thumbprint = ComputeThumbprint(subject);

        return new WalletStatusDto(
            Enrolled: true,
            HolderName: user.Name,
            IssuedAt: issuedAt,
            ExpiresAt: expiresAt,
            CnfThumbprint: thumbprint);
    }

    /// <summary>SHA-256 over the canonical subject string, hex-encoded. Synthetic fallback only.</summary>
    private static string ComputeThumbprint(string subject)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(subject));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
