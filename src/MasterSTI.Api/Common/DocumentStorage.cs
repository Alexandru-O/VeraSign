namespace MasterSTI.Api.Common;

public class DocumentStorage
{
    private readonly IWebHostEnvironment _env;

    public DocumentStorage(IWebHostEnvironment env)
    {
        _env = env;
    }

    public string UploadsRoot => EnsureDir("uploads");
    public string PreparedRoot => EnsureDir("prepared");
    public string SignedRoot => EnsureDir("signed");

    public async Task<string> SaveAsync(IFormFile file, Guid id, CancellationToken ct = default)
    {
        var path = Path.Combine(UploadsRoot, $"{id}.pdf");
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, ct);
        return path;
    }

    public Task<byte[]> ReadAsync(string storagePath, CancellationToken ct = default)
        => File.ReadAllBytesAsync(storagePath, ct);

    public string ResolvePrepared(string relativeOrAbsolute) => ResolveIn(PreparedRoot, relativeOrAbsolute);
    public string ResolveSigned(string relativeOrAbsolute) => ResolveIn(SignedRoot, relativeOrAbsolute);

    private string EnsureDir(string subfolder)
    {
        var root = Path.Combine(_env.ContentRootPath, "storage", subfolder);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ResolveIn(string root, string relativeOrAbsolute)
    {
        if (Path.IsPathRooted(relativeOrAbsolute))
            return relativeOrAbsolute;
        // Tolerate legacy DB rows that stored "prepared/xxx.pdf" or "signed/xxx.pdf"
        var fileName = Path.GetFileName(relativeOrAbsolute);
        return Path.Combine(root, fileName);
    }
}
