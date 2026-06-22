namespace MasterSTI.Api.Common.Csc;

public sealed class CscApiOptions
{
    public const string Section = "CscApi";

    public string BaseUrl { get; set; } = "https://localhost:7111";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? CredentialId { get; set; }
    public string? ClientId { get; set; } = "mastersti-client";
}
