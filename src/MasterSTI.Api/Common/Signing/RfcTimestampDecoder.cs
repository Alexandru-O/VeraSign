using Org.BouncyCastle.Tsp;

namespace MasterSTI.Api.Common.Signing;

/// <summary>
/// Decodes an RFC 3161 <c>TimeStampToken</c> stored as base64 in
/// <c>SignedDocument.TimestampToken</c>. Currently only the TSA's
/// <c>genTime</c> is surfaced; other fields can be added the same way.
/// </summary>
public static class RfcTimestampDecoder
{
    /// <summary>
    /// Returns the TSA <c>genTime</c> (UTC) decoded from a base64-encoded RFC
    /// 3161 token, or <c>null</c> when the input is empty or unparsable.
    /// Never throws — callers treat null as "TSA time unavailable".
    /// </summary>
    public static DateTime? TryDecodeGenTime(string? tokenBase64)
    {
        if (string.IsNullOrWhiteSpace(tokenBase64))
            return null;

        try
        {
            var raw = Convert.FromBase64String(tokenBase64);
            var token = new TimeStampToken(new Org.BouncyCastle.Cms.CmsSignedData(raw));
            var genTime = token.TimeStampInfo.GenTime;
            return DateTime.SpecifyKind(genTime, DateTimeKind.Utc);
        }
        catch
        {
            return null;
        }
    }
}
