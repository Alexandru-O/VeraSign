using FluentValidation;

namespace MasterSTI.Api.Features.Templates.Update;

public sealed class UpdateTemplateValidator : AbstractValidator<UpdateTemplateCommand>
{
    private static readonly string[] AllowedLevels = ["SES", "AdES", "QES"];

    public UpdateTemplateValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(2048);
        RuleFor(x => x.Category).NotEmpty();
        RuleFor(x => x.DefaultLevel)
            .Must(l => AllowedLevels.Contains(l, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"DefaultLevel must be one of: {string.Join(", ", AllowedLevels)}");
    }
}
