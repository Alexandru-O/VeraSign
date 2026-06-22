using System.Text.Json.Serialization;

namespace MasterSTI.Shared.Enums;

/// <summary>
/// Signing assurance level requested per recipient. Drives
/// <c>ISigningLevelDispatcher</c> in the API to route to the right signer
/// pipeline. String values are stable across the wire — entity column
/// <c>Recipient.Level</c> stays string so legacy rows ("QES"/"AdES"/"SES")
/// still bind via the converter alias table.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SigningLevel>))]
public enum SigningLevel
{
    /// <summary>Qualified Electronic Signature via CSC API v2 remote credential.</summary>
    QES_CSC = 0,

    /// <summary>Advanced Electronic Signature backed by an EUDIW wallet device key.</summary>
    AdES_Wallet = 1,

    /// <summary>Simple Electronic Signature (click-to-sign, no certificate).</summary>
    SES = 2
}
