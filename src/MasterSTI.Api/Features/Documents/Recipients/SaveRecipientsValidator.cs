using FluentValidation;

namespace MasterSTI.Api.Features.Documents.Recipients;

public sealed class SaveRecipientsValidator : AbstractValidator<SaveRecipientsCommand>
{
    private static readonly string[] AllowedLevels = ["SES", "AdES", "QES"];

    public SaveRecipientsValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Recipients).NotNull();

        RuleForEach(x => x.Recipients).ChildRules(rec =>
        {
            rec.RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
            rec.RuleFor(r => r.Name).NotEmpty().MaximumLength(256);
            rec.RuleFor(r => r.Order).GreaterThanOrEqualTo(0);
            rec.RuleFor(r => r.Level)
                .Must(l => AllowedLevels.Contains(l, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Level must be one of: {string.Join(", ", AllowedLevels)}");
        });
    }
}
