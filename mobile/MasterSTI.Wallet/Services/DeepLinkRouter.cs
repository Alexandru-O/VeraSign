using System.Text.Json;

namespace MasterSTI.Wallet.Services;

/// <summary>
/// Singleton sink for <c>verasign://sign?token=…</c> deep links captured by the
/// Android <c>MainActivity</c>. Sets <see cref="PendingDocumentId"/> when a
/// handoff JWT is received; <c>AppShell.OnAppearing</c> consumes it and
/// routes to <c>review</c>.
/// </summary>
public interface IDeepLinkRouter
{
    Guid? PendingDocumentId { get; }
    Guid? PendingRecipientId { get; }
    string? PendingHandoffToken { get; }
    void Capture(string deepLink);
    void Consume();
}

public sealed class DeepLinkRouter : IDeepLinkRouter
{
    public Guid? PendingDocumentId { get; private set; }
    public Guid? PendingRecipientId { get; private set; }
    public string? PendingHandoffToken { get; private set; }

    public void Capture(string deepLink)
    {
        try
        {
            var uri = new Uri(deepLink);
            var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var token = qs["token"];
            if (string.IsNullOrWhiteSpace(token))
                return;

            var docId = ExtractGuidClaim(token, "doc");
            if (docId is null)
                return;

            PendingHandoffToken = token;
            PendingDocumentId = docId;
            PendingRecipientId = ExtractGuidClaim(token, "sub");
        }
        catch
        {
            // Silent — malformed deep link is a no-op.
        }
    }

    public void Consume()
    {
        PendingDocumentId = null;
        PendingRecipientId = null;
        PendingHandoffToken = null;
    }

    /// <summary>
    /// Decodes the JWT payload (no signature verification — server is the
    /// authority) and extracts a string-Guid claim. Returns null on any failure.
    /// </summary>
    private static Guid? ExtractGuidClaim(string jwt, string claim)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty(claim, out var prop)) return null;
            return Guid.TryParse(prop.GetString(), out var g) ? g : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
