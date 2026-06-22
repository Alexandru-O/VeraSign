namespace MasterSTI.Mock.Issuer.Data;

/// <summary>
/// A registry identity the Mock EUDIW Issuer is willing to attest. PID issuance is
/// gated on a row existing here — an unknown identity is rejected. Seeded with the
/// two Demo Personas (Toma Iliescu, Thea Popescu); the seed values MUST match the
/// wallet's compile-time <c>DemoPersona</c> or enrollment is refused.
/// </summary>
public sealed class Identity
{
    public Guid Id { get; set; }
    public string FamilyName { get; set; } = string.Empty;
    public string GivenName { get; set; } = string.Empty;

    /// <summary>ISO-8601 date (yyyy-MM-dd) — stored as text to match the PID disclosure shape.</summary>
    public string BirthDate { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public ICollection<IssuedCredential> IssuedCredentials { get; set; } = new List<IssuedCredential>();
}
