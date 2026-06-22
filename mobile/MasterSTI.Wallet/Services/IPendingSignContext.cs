namespace MasterSTI.Wallet.Services;

/// <summary>
/// One-shot handoff slot from <c>ConsentPage</c> to <c>StatusPage</c>. Set by
/// ConsentPage after the biometric prompt succeeds; consumed by StatusPage on
/// appearance so it can run Prepare + Sign in the background while the user
/// already sees the progress ring. Decouples the slow API chain from the
/// navigation transition.
/// </summary>
public interface IPendingSignContext
{
    void SetBiometric(Guid documentId, Guid recipientId);
    PendingSign? Consume();
}

public sealed record PendingSign(Guid DocumentId, Guid RecipientId, string Sad, string Factor);

public sealed class PendingSignContext : IPendingSignContext
{
    private readonly object _lock = new();
    private PendingSign? _pending;

    public void SetBiometric(Guid documentId, Guid recipientId)
    {
        lock (_lock)
            _pending = new PendingSign(documentId, recipientId, "bio-attested", "biometric");
    }

    public PendingSign? Consume()
    {
        lock (_lock)
        {
            var p = _pending;
            _pending = null;
            return p;
        }
    }
}
