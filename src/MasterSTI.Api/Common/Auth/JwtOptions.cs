namespace MasterSTI.Api.Common.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "https://localhost:7001";
    public string Audience { get; set; } = "https://localhost:7001";
    public int ExpiryMinutes { get; set; } = 480; // 8h
    public JwtSigningOptions Signing { get; set; } = new();
}

public sealed class JwtSigningOptions
{
    public string Key { get; set; } = string.Empty; // dev only — must be set via env / appsettings.Development.json
}
