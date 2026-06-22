using FluentValidation;

namespace MasterSTI.Api.Features.Templates.UpdateContent;

public sealed class UpdateTemplateContentValidator : AbstractValidator<UpdateTemplateContentCommand>
{
    public UpdateTemplateContentValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.BodyMarkdown)
            .NotNull()
            .MaximumLength(64_000)
            .WithMessage("BodyMarkdown must not exceed 64.000 characters.");
    }
}
