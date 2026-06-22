using MasterSTI.Shared.Enums;

namespace MasterSTI.Api.Common.Signing;

/// <summary>
/// Marker for a level-specific signer pipeline. Phase 1 ships only
/// <see cref="CscQesSigner"/>; AdES (wallet device key) and SES wire in
/// later epics. Kept intentionally empty — the real signing logic still
/// lives in <c>SignDocumentHandler</c> + <c>EmbedSignatureHandler</c>;
/// this interface exists so the dispatcher contract is testable today.
/// </summary>
public interface ILevelSigner
{
    SigningLevel Level { get; }
}

public sealed class CscQesSigner : ILevelSigner
{
    public SigningLevel Level => SigningLevel.QES_CSC;
}

/// <summary>
/// Routes a <see cref="SigningLevel"/> to the implementation that handles it.
/// Phase 1: only <see cref="SigningLevel.QES_CSC"/> resolves. The others throw
/// <see cref="NotImplementedException"/> so callers fail loudly until the
/// AdES/SES epics land.
/// </summary>
public interface ISigningLevelDispatcher
{
    ILevelSigner Resolve(SigningLevel level);

    /// <summary>
    /// Accepts the legacy <c>Recipient.Level</c> string ("QES"/"AdES"/"SES" or
    /// the canonical enum names) and routes via <see cref="Resolve(SigningLevel)"/>.
    /// </summary>
    ILevelSigner Resolve(string levelString);

    /// <summary>
    /// Normalises legacy short strings to the canonical enum without throwing
    /// when the level isn't implemented yet. Used by handlers that only need
    /// the level identity (e.g. for early guards or audit metadata).
    /// </summary>
    static SigningLevel Parse(string levelString) => levelString switch
    {
        "QES" or "QES_CSC" => SigningLevel.QES_CSC,
        "AdES" or "AdES_Wallet" => SigningLevel.AdES_Wallet,
        "SES" => SigningLevel.SES,
        _ => throw new ArgumentOutOfRangeException(nameof(levelString),
                $"Unknown signing level '{levelString}'.")
    };
}

public sealed class SigningLevelDispatcher : ISigningLevelDispatcher
{
    private readonly CscQesSigner _csc;

    public SigningLevelDispatcher(CscQesSigner csc) => _csc = csc;

    public ILevelSigner Resolve(SigningLevel level) => level switch
    {
        SigningLevel.QES_CSC => _csc,
        SigningLevel.AdES_Wallet => throw new NotImplementedException(
            "AdES_Wallet wired in next epic"),
        SigningLevel.SES => throw new NotImplementedException(
            "SES wired in next epic"),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };

    public ILevelSigner Resolve(string levelString)
        => Resolve(ISigningLevelDispatcher.Parse(levelString));
}
