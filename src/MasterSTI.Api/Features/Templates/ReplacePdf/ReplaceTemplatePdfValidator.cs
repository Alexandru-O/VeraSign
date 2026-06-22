using FluentValidation;

namespace MasterSTI.Api.Features.Templates.ReplacePdf;

public sealed class ReplaceTemplatePdfValidator : AbstractValidator<ReplaceTemplatePdfCommand>
{
    private const long MaxSize = 50L * 1024 * 1024;

    public ReplaceTemplatePdfValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.File).NotNull().WithMessage("File is required.");
        RuleFor(x => x.File!.Length)
            .GreaterThan(0).WithMessage("File is empty.")
            .LessThanOrEqualTo(MaxSize).WithMessage("File exceeds 50 MB.")
            .When(x => x.File is not null);
        RuleFor(x => x.File!.FileName)
            .Must(name => !string.IsNullOrEmpty(name) && name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only .pdf files are accepted.")
            .When(x => x.File is not null);
    }
}
