using System.Text.Json;
using MasterSTI.Api.Common.Rendering;
using MasterSTI.RenderCommit.Fixtures;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 64; // EX_USAGE
    }

    try
    {
        switch (args[0])
        {
            case "render":
                return Render(args[1..]);
            case "generate-fixtures":
                return GenerateFixtures(args[1..]);
            case "--help":
            case "-h":
                PrintUsage();
                return 0;
            default:
                Console.Error.WriteLine($"unknown subcommand '{args[0]}'");
                PrintUsage();
                return 64;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"render-commit: {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
}

static int Render(string[] subArgs)
{
    if (subArgs.Length < 1)
    {
        Console.Error.WriteLine("usage: render-commit render <pdfPath> [--locale ro-RO] [--pdfium-root tools/pdfium-v1]");
        return 64;
    }

    var pdfPath = subArgs[0];
    var locale = "ro-RO";
    var pdfiumRoot = DefaultPdfiumRoot();

    for (var i = 1; i < subArgs.Length - 1; i += 2)
    {
        switch (subArgs[i])
        {
            case "--locale": locale = subArgs[i + 1]; break;
            case "--pdfium-root": pdfiumRoot = subArgs[i + 1]; break;
            default: Console.Error.WriteLine($"unknown flag '{subArgs[i]}'"); return 64;
        }
    }

    if (!File.Exists(pdfPath))
    {
        Console.Error.WriteLine($"pdf not found: {pdfPath}");
        return 66; // EX_NOINPUT
    }

    PdfiumLoader.Install(pdfiumRoot);

    var result = RenderCommitment.Compute(pdfPath, locale);

    var json = JsonSerializer.Serialize(new
    {
        profile = result.Profile,
        algo = result.Algo,
        dpi = result.Dpi,
        pageCount = result.PageCount,
        locale = result.Locale,
        root = result.RootHex,
        leaves = result.PageLeafHashesHex,
        pdfiumBinarySha256 = RenderProfiles.Sha256Hex(PdfiumLoader.PinnedBinaryPath),
    }, new JsonSerializerOptions { WriteIndented = false });

    Console.WriteLine(json);
    return 0;
}

static int GenerateFixtures(string[] subArgs)
{
    if (subArgs.Length < 1)
    {
        Console.Error.WriteLine("usage: render-commit generate-fixtures <outDir>");
        return 64;
    }

    var outDir = subArgs[0];
    Directory.CreateDirectory(outDir);

    FixtureAuthor.WriteAll(outDir);

    Console.WriteLine($"wrote fixtures to {Path.GetFullPath(outDir)}");
    return 0;
}

static string DefaultPdfiumRoot()
{
    // CWD-relative when launched from repo root; falls back to a path
    // alongside the exe for `dotnet publish` runs.
    var candidates = new[]
    {
        Path.Combine(Environment.CurrentDirectory, "tools", "pdfium-v1"),
        Path.Combine(AppContext.BaseDirectory, "pdfium-v1"),
    };
    foreach (var c in candidates)
    {
        if (Directory.Exists(c)) return c;
    }
    return candidates[0];
}

static void PrintUsage()
{
    Console.WriteLine("""
        render-commit -- WYSIWYS 2.0 spike CLI (ADR-0008)

        usage:
          render-commit render <pdfPath> [--locale ro-RO] [--pdfium-root tools/pdfium-v1]
              prints {profile, algo, dpi, pageCount, locale, root, leaves[], pdfiumBinarySha256}

          render-commit generate-fixtures <outDir>
              writes 01-romanian-diacritics.pdf, 02-ocg-hidden-amount.pdf,
                     03-transparent-overlay.pdf, 04a-lta-base.pdf, 04b-lta-refreshed.pdf

        exit codes: 0 OK | 1 runtime | 64 usage | 66 input missing
        """);
}
