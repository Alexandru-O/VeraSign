using System.Security.Cryptography;
using iText.Kernel.Pdf;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Data;
using MasterSTI.Shared.DTOs.Wallet;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Features.Wallet.InboxItem.Get;

/// <summary>
/// Returns the per-Recipient metadata the wallet's Review screen needs
/// (real document name, sender, page count, level, hash, size). Authorisation:
/// reuses <see cref="IRecipientAccessGuard"/> so only the Wallet Session user
/// whose JWT email matches the Recipient's email (case-insensitive) sees it,
/// and only while the Recipient is <see cref="RecipientStatus.Notified"/>.
/// </summary>
public sealed class GetInboxItemHandler : IRequestHandler<GetInboxItemQuery, WalletInboxItemMetaDto>
{
    private readonly AppDbContext _db;
    private readonly IRecipientAccessGuard _guard;

    public GetInboxItemHandler(AppDbContext db, IRecipientAccessGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    public async Task<WalletInboxItemMetaDto> Handle(GetInboxItemQuery request, CancellationToken cancellationToken)
    {
        if (request.RecipientId == Guid.Empty)
            throw new KeyNotFoundException("Recipient not found.");

        var row = await _db.Recipients.AsNoTracking()
            .Where(r => r.Id == request.RecipientId)
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.Level,
                Document = new
                {
                    r.Document.Id,
                    r.Document.FileName,
                    r.Document.StoragePath,
                    r.Document.Sha256Hash,
                    r.Document.OwnerUserId
                }
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            throw new KeyNotFoundException("Recipient not found.");

        if (row.Status != RecipientStatus.Notified)
            throw new UnauthorizedAccessException("Recipient is not currently awaiting signature.");

        if (!await _guard.CanAccessDocumentAsync(row.Document.Id, cancellationToken))
            throw new UnauthorizedAccessException("Caller is not the assigned recipient.");

        var senderName = "—";
        if (row.Document.OwnerUserId is Guid ownerId)
        {
            var sender = await _db.Users.AsNoTracking()
                .Where(u => u.Id == ownerId)
                .Select(u => new { u.Name, u.Email })
                .FirstOrDefaultAsync(cancellationToken);
            if (sender is not null)
                senderName = string.IsNullOrWhiteSpace(sender.Name) ? sender.Email : sender.Name;
        }

        // Signed Document Chain — when a prior signer has embedded, the wallet's
        // Review screen must reflect the chain-head PDF (size, page count, hash)
        // since that is what this Recipient will pin and counter-sign. PAdES is
        // incremental, so the original upload hash no longer matches what is on
        // disk after Toma's signature.
        var chainHeadPath = await _db.SignedDocuments.AsNoTracking()
            .Where(sd => sd.OriginalDocumentId == row.Document.Id && sd.IsFinal)
            .Select(sd => sd.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        var sourcePath = !string.IsNullOrWhiteSpace(chainHeadPath) && File.Exists(chainHeadPath)
            ? chainHeadPath
            : row.Document.StoragePath;

        var pages = TryGetPageCount(sourcePath);
        var sizeBytes = TryGetFileSize(sourcePath);
        var hash = sourcePath == row.Document.StoragePath
            ? row.Document.Sha256Hash
            : TryComputeSha256Hex(sourcePath) ?? row.Document.Sha256Hash;

        return new WalletInboxItemMetaDto(
            DocumentName: row.Document.FileName,
            SenderName: senderName,
            Pages: pages,
            Level: row.Level,
            Hash: hash,
            SizeBytes: sizeBytes);
    }

    private static string? TryComputeSha256Hex(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static int TryGetPageCount(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
        try
        {
            using var reader = new PdfReader(path);
            using var pdf = new PdfDocument(reader);
            return pdf.GetNumberOfPages();
        }
        catch
        {
            return 0;
        }
    }

    private static long TryGetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
        try { return new FileInfo(path).Length; } catch { return 0; }
    }
}
