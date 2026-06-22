using System.Text.Json.Serialization;

namespace MasterSTI.Api.Common.Eudiw;

public record AuthorizationRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("response_type")] string ResponseType,
    [property: JsonPropertyName("response_mode")] string ResponseMode,
    [property: JsonPropertyName("response_uri")] string ResponseUri,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("presentation_definition")] PresentationDefinitionModel PresentationDefinition);

public record PresentationDefinitionModel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("input_descriptors")] InputDescriptor[] InputDescriptors);

public record InputDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("constraints")] ConstraintsModel Constraints);

public record ConstraintsModel(
    [property: JsonPropertyName("fields")] FieldConstraint[] Fields);

public record FieldConstraint(
    [property: JsonPropertyName("path")] string[] Path,
    [property: JsonPropertyName("intent_to_retain")] bool IntentToRetain = false);

public record VpTokenResponse(
    [property: JsonPropertyName("vp_token")] string VpToken,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("presentation_submission")] object? PresentationSubmission = null);

public record PidClaims(
    string FamilyName,
    string GivenName,
    DateOnly? BirthDate,
    string? Subject,
    string? Email = null,
    string? CnfJwkThumbprint = null,
    DateTime? IssuedAt = null,
    DateTime? ExpiresAt = null);

public record EudiwRequestResult(
    Guid SigningRequestId,
    string Nonce,
    string QrPayload);

public record EudiwStatusResult(
    Guid SigningRequestId,
    string Status,
    string? EudiwSubject);
