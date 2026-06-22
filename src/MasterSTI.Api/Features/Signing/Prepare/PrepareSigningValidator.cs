using System.Text.RegularExpressions;
using FluentValidation;

namespace MasterSTI.Api.Features.Signing.Prepare;

/// <summary>
/// Enforces ADR-0008 frozen v1 schema on the Render commitment carried by
/// <see cref="PrepareSigningCommand.RenderCommitment"/>. Validator is silent
/// when the wallet did not supply a commitment (all-null shape); fails
/// fast and explicit when partial / wrong-value shape arrives so the
/// downstream PadesService dictionary writer never sees ambiguous input.
/// </summary>
public sealed partial class PrepareSigningValidator : AbstractValidator<PrepareSigningCommand>
{
    public const string FrozenAlgo = "SHA-256";
    public const int FrozenDpi = 150;
    public const string FrozenProfile = "PdfiumPinned-v1";

    public PrepareSigningValidator()
    {
        RuleFor(x => x.DocumentId).NotEqual(Guid.Empty);
        RuleFor(x => x.RecipientId).NotEqual(Guid.Empty);

        When(x => x.RenderCommitment is { IsPresent: true }, () =>
        {
            RuleFor(x => x.RenderCommitment!.RenderRootHex)
                .NotEmpty()
                .Must(BeLowercaseSha256Hex)
                .WithMessage("RenderRootHex must be 64 lowercase hex chars (SHA-256).");

            RuleFor(x => x.RenderCommitment!.RenderAlgo)
                .NotEmpty()
                .Equal(FrozenAlgo)
                .WithMessage($"RenderAlgo must be '{FrozenAlgo}' in v1.");

            RuleFor(x => x.RenderCommitment!.RenderDpi)
                .NotNull()
                .Equal(FrozenDpi)
                .WithMessage($"RenderDpi must be {FrozenDpi} in v1.");

            RuleFor(x => x.RenderCommitment!.RenderPageCount)
                .NotNull()
                .GreaterThan(0)
                .WithMessage("RenderPageCount must be a positive integer.");

            RuleFor(x => x.RenderCommitment!.RenderLocale)
                .NotEmpty()
                .Must(BePlausibleBcp47)
                .WithMessage("RenderLocale must be a BCP-47 tag (e.g. 'ro-RO').");

            RuleFor(x => x.RenderCommitment!.RenderProfile)
                .NotEmpty()
                .Equal(FrozenProfile)
                .WithMessage($"RenderProfile must be '{FrozenProfile}' in v1.");
        });

        // When the wallet did not supply a commitment we still reject the
        // half-populated shape -- any non-null render field without
        // RenderRootHex is malformed input.
        When(x => x.RenderCommitment is { IsPresent: false } rc &&
                  (rc.RenderAlgo is not null ||
                   rc.RenderDpi is not null ||
                   rc.RenderPageCount is not null ||
                   rc.RenderLocale is not null ||
                   rc.RenderProfile is not null), () =>
        {
            RuleFor(x => x.RenderCommitment!.RenderRootHex)
                .NotNull()
                .WithMessage("RenderRootHex is required when any other Render* field is supplied.");
        });
    }

    private static bool BeLowercaseSha256Hex(string? value) =>
        value is not null && Sha256HexRegex().IsMatch(value);

    private static bool BePlausibleBcp47(string? value) =>
        value is not null && Bcp47Regex().IsMatch(value);

    [GeneratedRegex(@"^[0-9a-f]{64}$")]
    private static partial Regex Sha256HexRegex();

    // Plausibility check only: primary language + optional region / script /
    // private-use subtags. Strict BCP-47 parsing is the verifier's problem;
    // here we just reject obvious garbage like spaces, underscores, empty
    // subtags, mixed-case primary tag.
    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Z][a-z]{3})?(-[A-Z]{2}|-\d{3})?(-[A-Za-z0-9]{1,8})*$")]
    private static partial Regex Bcp47Regex();
}
