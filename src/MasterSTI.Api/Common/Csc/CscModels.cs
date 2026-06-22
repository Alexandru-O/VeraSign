namespace MasterSTI.Api.Common.Csc;

public record CscLoginRequest(string username, string password, string grant_type = "password");
public record CscLoginResponse(string access_token, string token_type, int expires_in);

public record CscCredentialsListRequest(string? userCode = null);
public record CscCredentialsListResponse(string[] credentialIDs);

public record CscCredentialInfoRequest(
    string credentialID,
    string? certificates = "chain",
    bool? certInfo = true,
    bool? authInfo = true,
    bool? info = true,
    string? lang = "en-US");

public record CscCertInfo(
    string status,
    string[] certificates,
    string issuerDN,
    string serialNumber,
    string subjectDN,
    string validFrom,
    string validTo);

public record CscKeyInfo(string status, string[] algo, int len);

public record CscCredentialInfoResponse(
    string? description,
    CscKeyInfo key,
    CscCertInfo cert,
    string authMode,
    int multisign,
    string? lang);

/// <summary>
/// CSC API v2 §11.5 authData entry. <c>id</c> identifies the factor
/// (canonical demo values: <c>PIN</c>, <c>BIO</c>); <c>value</c> carries
/// the factor's evidence (digits for PIN, the <c>bio-attested</c> marker
/// for BIO). Real QTSPs publish accepted IDs via <c>/info</c> — see ADR-0010.
/// </summary>
public record CscAuthData(string id, string value);

/// <summary>
/// CSC API v2 §11.5 <c>/credentials/authorize</c> request. Migrated from
/// the legacy scalar <c>PIN</c> shape per ADR-0010. Field names follow the
/// spec literally: singular <c>hash</c>, explicit <c>hashAlgorithmOID</c>,
/// factor array <c>authData</c>.
/// </summary>
public record CscAuthorizeRequest(
    string credentialID,
    int numSignatures,
    string[] hash,
    string hashAlgorithmOID,
    CscAuthData[] authData,
    string? description);

public record CscAuthorizeResponse(string SAD, int expiresIn);

/// <summary>
/// CSC API v2 §11.6 <c>/signatures/signHash</c> request. Field names match
/// spec: singular <c>hash</c>, explicit <c>hashAlgorithmOID</c>.
/// </summary>
public record CscSignHashRequest(
    string credentialID,
    string SAD,
    string[] hash,
    string hashAlgorithmOID = "2.16.840.1.101.3.4.2.1",
    string signAlgo = "1.2.840.113549.1.1.11");

public record CscSignHashResponse(string[] signatures);
