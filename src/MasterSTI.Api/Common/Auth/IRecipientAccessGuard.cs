namespace MasterSTI.Api.Common.Auth;

/// <summary>
/// Per-Document authorisation guard. Returns true if the current user is the
/// Document's sender (<c>Document.OwnerUserId</c>) or a Recipient currently
/// in <c>Notified</c> status on that Document (match by lowercased email).
/// Lets a wallet-Session caller read and sign the Document they were invited
/// to without elevating to org-wide access. See
/// <c>CONTEXT.md → Recipient Access Guard</c>.
/// </summary>
public interface IRecipientAccessGuard
{
    Task<bool> CanAccessDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
