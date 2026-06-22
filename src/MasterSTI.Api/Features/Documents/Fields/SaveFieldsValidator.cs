using FluentValidation;

namespace MasterSTI.Api.Features.Documents.Fields;

public sealed class SaveFieldsValidator : AbstractValidator<SaveFieldsCommand>
{
    private static readonly string[] AllowedTypes = ["Signature", "Initial", "Date", "Text"];

    public SaveFieldsValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Fields).NotNull();

        RuleForEach(x => x.Fields).ChildRules(field =>
        {
            field.RuleFor(f => f.Type)
                .Must(t => AllowedTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Field type must be one of: {string.Join(", ", AllowedTypes)}");
            field.RuleFor(f => f.Page).GreaterThanOrEqualTo(1);
            field.RuleFor(f => f.X).GreaterThanOrEqualTo(0);
            field.RuleFor(f => f.Y).GreaterThanOrEqualTo(0);
            field.RuleFor(f => f.Width).GreaterThan(0);
            field.RuleFor(f => f.Height).GreaterThan(0);
        });
    }
}
