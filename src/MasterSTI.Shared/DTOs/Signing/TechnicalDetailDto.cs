namespace MasterSTI.Shared.DTOs.Signing;

/// <summary>
/// Read-only technical metadata for an in-flight or completed signing request,
/// surfaced by the wallet's StatusPage "Detaliat" toggle. Replaces the formerly
/// hardcoded labels (TSP / algorithm / level strings) with real values derived
/// from the prepared hash and the CSC credential's certificate.
/// </summary>
/// <param name="HashPrefix">
/// First 16 hex chars of the byte-range document hash that is being signed. Lets
/// the user visually confirm WYSIWYS without leaking the full digest.
/// </param>
/// <param name="CertificateFingerprint">
/// SHA-256 thumbprint of the signer's X.509 certificate, formatted as 32 uppercase
/// hex pairs separated by colons (e.g. <c>AB:CD:...</c>). Empty when CSC creds are
/// not configured or the credential is unavailable.
/// </param>
/// <param name="TspName">
/// Trust Service Provider name extracted from the signing certificate's IssuerDN
/// (<c>CN</c> attribute when present, raw DN otherwise). Empty when unavailable.
/// </param>
/// <param name="Algorithm">
/// Signing algorithm display string (e.g. <c>SHA-256 + RSA-2048</c>) derived from
/// the CSC credential's <c>key.algo</c> + <c>key.len</c>. Falls back to a sane
/// default (SHA-256 + RSA-2048) when the credential is unavailable.
/// </param>
/// <param name="Level">
/// Planned PAdES level for this signing request (e.g. <c>PAdES-B-LT</c>).
/// </param>
/// <param name="DocumentName">Original file name of the PDF being signed.</param>
/// <param name="Pages">PDF page count of the chain-head PDF (0 when unavailable).</param>
/// <param name="SizeBytes">On-disk size of the chain-head PDF in bytes (0 when unavailable).</param>
public sealed record TechnicalDetailDto(
    string HashPrefix,
    string CertificateFingerprint,
    string TspName,
    string Algorithm,
    string Level,
    string DocumentName,
    int Pages,
    long SizeBytes);
