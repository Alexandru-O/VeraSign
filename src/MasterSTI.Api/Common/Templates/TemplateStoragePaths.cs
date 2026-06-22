using SysPath = System.IO.Path;

namespace MasterSTI.Api.Common.Templates;

/// <summary>
/// Resolves and validates filesystem paths for template PDFs.
/// All template PDFs MUST live under {ContentRootPath}/storage/templates/.
/// </summary>
public sealed class TemplateStoragePaths
{
    private readonly IWebHostEnvironment _env;

    public TemplateStoragePaths(IWebHostEnvironment env)
    {
        _env = env;
    }

    public string TemplatesRoot
    {
        get
        {
            var root = SysPath.Combine(_env.ContentRootPath, "storage", "templates");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>Default canonical path for a freshly-content-created template.</summary>
    public string DefaultPathForId(Guid templateId)
        => SysPath.Combine(TemplatesRoot, $"{templateId}.pdf");

    /// <summary>
    /// Validates that <paramref name="path"/> is inside <see cref="TemplatesRoot"/>.
    /// Throws <see cref="InvalidOperationException"/> on path traversal attempts.
    /// Returns the canonical absolute path.
    /// </summary>
    public string ValidateInsideTemplatesRoot(string path)
    {
        var fullPath = SysPath.GetFullPath(path);
        var rootFull = SysPath.GetFullPath(TemplatesRoot);
        var rootWithSep = rootFull.EndsWith(SysPath.DirectorySeparatorChar)
            ? rootFull
            : rootFull + SysPath.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Template PDF path is outside the templates storage root.");

        return fullPath;
    }
}
