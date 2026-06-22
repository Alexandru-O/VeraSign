using FluentValidation;

namespace MasterSTI.Api.Features.Documents.Upload;

public class UploadDocumentValidator : AbstractValidator<UploadDocumentCommand>
{
    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    public UploadDocumentValidator()
    {
        RuleFor(x => x.File)
            .NotNull()
            .WithMessage("File must not be null.");

        When(x => x.File != null, () =>
        {
            RuleFor(x => x.File.FileName)
                .Must(name => !string.IsNullOrWhiteSpace(name)
                              && Path.GetExtension(name).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                .WithMessage("Only files with a .pdf extension are accepted.");

            RuleFor(x => x.File.Length)
                .GreaterThan(0)
                .WithMessage("File must not be empty.")
                .LessThanOrEqualTo(MaxFileSizeBytes)
                .WithMessage("File size must not exceed 50 MB.");
        });
    }
}
