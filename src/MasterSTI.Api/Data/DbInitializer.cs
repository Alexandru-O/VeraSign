using MasterSTI.Api.Common.Templates;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SysPath = System.IO.Path;

namespace MasterSTI.Api.Data;

public static class DbInitializer
{
    public static readonly Guid SeedOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SeedAdminUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public const string SeedAdminEmail = "admin@verasign.demo";
    public const string SeedAdminPassword = "Demo!2025";
    public const string SeedOrganizationName = "VeraSign Demo SRL";

    public static async Task SeedAsync(AppDbContext db, ILogger logger, string contentRootPath, CancellationToken cancellationToken = default)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == SeedOrganizationId, cancellationToken);
        if (org is null)
        {
            org = new Organization
            {
                Id = SeedOrganizationId,
                Name = SeedOrganizationName,
                CreatedAt = DateTime.UtcNow
            };
            db.Organizations.Add(org);
        }

        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Id == SeedAdminUserId, cancellationToken);
        if (existingUser is null)
        {
            var hasher = new PasswordHasher<User>();
            var user = new User
            {
                Id = SeedAdminUserId,
                Email = SeedAdminEmail,
                Name = "VeraSign Demo Admin",
                Role = "Admin",
                OrganizationId = SeedOrganizationId,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, SeedAdminPassword);
            db.Users.Add(user);
            logger.LogInformation("Seeded demo user {Email}", SeedAdminEmail);
        }

        await db.SaveChangesAsync(cancellationToken);

        await SeedTemplatesAsync(db, logger, contentRootPath, cancellationToken);
        await SeedProbeResultsAsync(db, logger, cancellationToken);
    }

    private sealed record TemplateSeed(
        string Title,
        string Description,
        TemplateCategory Category,
        string DefaultLevel,
        int UsageCount,
        string FileSlug,
        string Eyebrow);

    /// <summary>
    /// Per-template Romanian markdown body. Each seed slug maps to a distinct
    /// content set that matches the template's purpose (contract, NDA, CIM
    /// etc.), so the seed PDFs are usable demo references rather than six
    /// copies of the same generic boilerplate.
    /// Generic fallback covers any future slug not explicitly mapped.
    /// </summary>
    private static string BuildSeedBody(TemplateSeed seed)
    {
        var body = seed.FileSlug switch
        {
            "contract-inchiriere-apartament" => RealEstateLeaseBody,
            "nda-confidentialitate"          => NdaBody,
            "contract-individual-munca"      => EmploymentBody,
            "acord-servicii-sla"             => SlaBody,
            "procura-notariala"              => PowerOfAttorneyBody,
            "anexa-servicii-termeni"         => AddendumBody,
            _                                => GenericBody,
        };

        return $"> {seed.Eyebrow}\n\n{body}";
    }

    // Unique substring from the V1 generic body — used to detect pristine
    // (un-edited) seeded rows so they get refreshed without clobbering
    // anything the user has customised through the template editor.
    private const string V1GenericSentinel =
        "Pentru orice neclarități sau personalizări suplimentare";

    private const string GenericBody =
        "## Clauze contractuale\n\n" +
        "Părțile semnatare convin asupra următoarelor clauze contractuale, redactate în limba română și aplicabile în mod corespunzător.\n\n" +
        "Obiectul prezentului document model VeraSign este de a ilustra forma și structura unui contract semnabil prin platforma VeraSign.\n\n" +
        "Toate datele cu caracter personal incluse în acest formular sunt fictive, fiind utilizate exclusiv în scop demonstrativ.\n\n" +
        "Părțile recunosc valoarea juridică a semnăturii electronice calificate (QES) emise de un prestator de servicii calificat, conform Regulamentului (UE) 910/2014 (eIDAS).\n\n" +
        "Documentul poate fi semnat la distanță, prin intermediul aplicației VeraSign și a portofelului EUDIW, asigurând legarea criptografică a semnăturii la titularul certificatului.\n\n" +
        "Modificările aduse acestui document model trebuie efectuate înainte de inițierea fluxului de semnare, ulterior fiind blocate prin amprentă SHA-256.\n\n" +
        "Pentru orice neclarități sau personalizări suplimentare, vă rugăm să contactați echipa VeraSign Demo SRL.\n\n" +
        "---\n\n[SIGNATURE]\n\n[SIGNATURE]\n";

    private const string RealEstateLeaseBody =
        "## Părțile contractante\n\n" +
        "Subsemnații, în calitate de **Locator** și **Locatar**, încheie prezentul contract de închiriere având ca obiect imobilul-apartament descris la articolul următor, situat pe teritoriul României.\n\n" +
        "## Obiectul contractului\n\n" +
        "Locatorul predă spre folosință Locatarului apartamentul situat la adresa convenită între părți, compus din camerele, dependințele și dotările enumerate în procesul-verbal de predare-primire, anexă a prezentului contract.\n\n" +
        "## Durata locațiunii\n\n" +
        "Contractul se încheie pe o perioadă determinată, începând cu data semnării și expirând la termenul stabilit de părți. Reînnoirea se poate face prin act adițional semnat electronic.\n\n" +
        "## Chiria și modalități de plată\n\n" +
        "Chiria lunară se achită până în data de 5 a fiecărei luni, prin transfer bancar în contul indicat de Locator. Întârzierile peste 10 zile generează penalități de 0,1% pe zi din suma restantă.\n\n" +
        "## Garanția\n\n" +
        "La semnare, Locatarul depune o garanție echivalentă cu o chirie lunară, restituibilă la încetarea contractului, după constatarea integrității bunurilor și achitarea utilităților.\n\n" +
        "## Obligațiile părților\n\n" +
        "- Locatorul predă imobilul în stare corespunzătoare folosinței și asigură liniștita posesie.\n" +
        "- Locatarul folosește bunul cu prudența unui bun proprietar și suportă reparațiile de întreținere curentă.\n" +
        "- Subînchirierea este permisă doar cu acordul scris al Locatorului.\n\n" +
        "## Încetarea contractului\n\n" +
        "Contractul încetează prin expirarea termenului, prin acordul părților sau prin reziliere unilaterală cu preaviz de 30 de zile, conform legislației în vigoare.\n\n" +
        "---\n\nLocator [SIGNATURE]\n\nLocatar [SIGNATURE]\n";

    private const string NdaBody =
        "## Părțile acordului\n\n" +
        "Prezentul **Acord de confidențialitate** se încheie între cele două părți semnatare, denumite în continuare **Partea Dezvăluitoare** și **Partea Primitoare**, în vederea protejării informațiilor confidențiale schimbate în contextul unei colaborări comerciale.\n\n" +
        "## Obiectul acordului\n\n" +
        "Părțile convin să trateze ca strict confidențiale toate informațiile tehnice, financiare, comerciale, strategice sau de orice altă natură, comunicate reciproc în formă scrisă, verbală sau electronică.\n\n" +
        "## Definiția informațiilor confidențiale\n\n" +
        "Sunt considerate informații confidențiale, fără a se limita la:\n\n" +
        "- date tehnice, planuri, scheme, cod-sursă, arhitecturi software;\n" +
        "- planuri de afaceri, prognoze financiare, liste de clienți și furnizori;\n" +
        "- know-how, metodologii, procese interne;\n" +
        "- orice altă informație marcată explicit ca fiind confidențială.\n\n" +
        "## Obligațiile Părții Primitoare\n\n" +
        "Partea Primitoare se obligă să păstreze confidențialitatea informațiilor, să le folosească exclusiv în scopul colaborării și să nu le dezvăluie unor terți fără acordul scris prealabil al Părții Dezvăluitoare.\n\n" +
        "## Durata\n\n" +
        "Obligația de confidențialitate se menține pe toată durata colaborării și pentru o perioadă de 5 ani după încetarea acesteia, indiferent de motiv.\n\n" +
        "## Excepții\n\n" +
        "Nu constituie informație confidențială cea care: (i) este sau devine publică fără culpa Părții Primitoare; (ii) era deja cunoscută legal anterior dezvăluirii; (iii) trebuie comunicată în baza unei obligații legale sau a unei hotărâri judecătorești.\n\n" +
        "## Sancțiuni\n\n" +
        "Încălcarea obligațiilor de confidențialitate atrage răspunderea pentru prejudiciile cauzate, precum și obligația de plată a unei penalități contractuale convenite separat de părți.\n\n" +
        "---\n\nPartea Dezvăluitoare [SIGNATURE]\n\nPartea Primitoare [SIGNATURE]\n";

    private const string EmploymentBody =
        "## Părțile contractante\n\n" +
        "Prezentul **contract individual de muncă** se încheie între **Angajator**, persoană juridică română, și **Salariat**, persoană fizică, în temeiul Legii nr. 53/2003 — Codul Muncii, republicat, cu modificările și completările ulterioare.\n\n" +
        "## Obiectul contractului\n\n" +
        "Salariatul se obligă să presteze munca pe postul convenit, în beneficiul și sub autoritatea Angajatorului, în schimbul unei remunerații denumită salariu.\n\n" +
        "## Durata contractului\n\n" +
        "Contractul se încheie pe durată nedeterminată, începând cu data prevăzută în ordinul de încadrare. Perioada de probă este de cel mult 90 de zile calendaristice, conform legii.\n\n" +
        "## Timpul de muncă\n\n" +
        "Durata normală a timpului de muncă este de 8 ore/zi, 40 ore/săptămână, repartizată de luni până vineri. Orele suplimentare se compensează cu timp liber corespunzător sau spor salarial.\n\n" +
        "## Drepturile salariale\n\n" +
        "Salariul brut lunar și sporurile aplicabile sunt cele convenite în anexa salarială. Plata se face lunar, până în data de 15 a lunii următoare celei pentru care se face plata, prin virament bancar.\n\n" +
        "## Concediul de odihnă\n\n" +
        "Salariatul are dreptul la un concediu anual de odihnă plătit de minim 21 de zile lucrătoare, conform Codului Muncii și regulamentului intern al Angajatorului.\n\n" +
        "## Obligații specifice\n\n" +
        "- Salariatul respectă regulamentul intern, normele SSM și PSI.\n" +
        "- Angajatorul asigură condiții corespunzătoare de muncă și plata la timp a drepturilor salariale.\n\n" +
        "## Încetarea contractului\n\n" +
        "Contractul încetează în condițiile prevăzute de Codul Muncii (acord, demisie, concediere etc.), cu respectarea termenelor de preaviz legale.\n\n" +
        "---\n\nAngajator [SIGNATURE]\n\nSalariat [SIGNATURE]\n";

    private const string SlaBody =
        "## Părțile\n\n" +
        "Prezentul **acord de prestări servicii** se încheie între **Prestator**, furnizorul serviciilor, și **Beneficiar**, achizitorul acestora, în vederea reglementării nivelului de servicii (SLA) agreat.\n\n" +
        "## Obiectul acordului\n\n" +
        "Prestatorul se obligă să furnizeze Beneficiarului serviciile descrise în anexa tehnică, cu respectarea indicatorilor de performanță (KPI) și a nivelelor de disponibilitate (SLA) stabilite mai jos.\n\n" +
        "## Indicatori de nivel de serviciu (SLA)\n\n" +
        "- Disponibilitate platformă: minim 99,5% pe lună calendaristică.\n" +
        "- Timp de răspuns la incidente critice (P1): maxim 1 oră în intervalul 09:00–18:00.\n" +
        "- Timp de rezolvare incidente majore (P2): maxim 8 ore lucrătoare.\n" +
        "- Fereastra de mentenanță planificată: duminica, între 02:00 și 06:00.\n\n" +
        "## Preț și modalități de plată\n\n" +
        "Prețul abonamentului lunar este cel stabilit în anexa comercială. Facturarea se face în prima zi a lunii pentru luna în curs, cu termen de plată de 15 zile.\n\n" +
        "## Penalități pentru neîndeplinirea SLA\n\n" +
        "În cazul în care disponibilitatea lunară scade sub pragul convenit, Beneficiarul primește un credit de serviciu calculat proporțional cu durata indisponibilității, dedus din factura lunii următoare.\n\n" +
        "## Durata și încetarea\n\n" +
        "Acordul se încheie pe perioadă de 12 luni, cu reînnoire tacită pentru perioade succesive de 12 luni, dacă niciuna dintre părți nu denunță contractul cu 30 de zile înainte de expirare.\n\n" +
        "## Confidențialitate și protecția datelor\n\n" +
        "Părțile respectă obligațiile GDPR (Regulamentul UE 2016/679) cu privire la datele cu caracter personal procesate în executarea acordului.\n\n" +
        "---\n\nPrestator [SIGNATURE]\n\nBeneficiar [SIGNATURE]\n";

    private const string PowerOfAttorneyBody =
        "## Date de identificare\n\n" +
        "Subsemnatul **Mandant**, persoană fizică majoră, cu capacitate deplină de exercițiu, identificat conform datelor din actul de identitate atașat, împuternicesc prin prezenta procură pe **Mandatar**, persoană fizică, să mă reprezinte în condițiile descrise mai jos.\n\n" +
        "## Scopul împuternicirii\n\n" +
        "Mandatarul este împuternicit să acționeze în numele și pe seama Mandantului în vederea îndeplinirii formalităților administrative, juridice și financiare descrise în prezentul act, în fața autorităților publice și a oricăror persoane fizice sau juridice competente.\n\n" +
        "## Puterile conferite\n\n" +
        "Mandatarul are dreptul să:\n\n" +
        "- depună și ridice acte, cereri, adeverințe și documente oficiale;\n" +
        "- semneze cereri, declarații și formulare în numele Mandantului;\n" +
        "- efectueze plăți și să încaseze sume, cu obligația de a justifica utilizarea acestora;\n" +
        "- reprezinte Mandantul în relația cu autoritățile fiscale, instituțiile bancare și birourile notariale.\n\n" +
        "## Limite\n\n" +
        "Mandatarul nu poate înstrăina bunuri imobile ale Mandantului, nu poate contracta credite în numele acestuia și nu poate renunța la drepturi succesorale fără un mandat special, autentificat separat.\n\n" +
        "## Durata și revocarea\n\n" +
        "Prezenta procură produce efecte de la data semnării și este valabilă până la data expirării prevăzute în act sau până la momentul revocării exprese, comunicate Mandatarului prin orice mijloc care permite confirmarea recepției.\n\n" +
        "## Declarații\n\n" +
        "Mandantul declară pe propria răspundere că este de acord cu prezenta împuternicire și că informațiile furnizate sunt corecte și complete.\n\n" +
        "---\n\nMandant [SIGNATURE]\n\nMandatar [SIGNATURE]\n";

    private const string AddendumBody =
        "## Preambul\n\n" +
        "Prezenta **Anexă** face parte integrantă din contractul-cadru de prestări servicii încheiat anterior între părți, completând și modificând termenii acestuia cu privire la aspectele descrise mai jos.\n\n" +
        "## Obiectul anexei\n\n" +
        "Părțile convin asupra unor termeni și condiții particulare aplicabile pachetului suplimentar de servicii, fără a aduce atingere clauzelor generale din contractul-cadru, care rămân pe deplin valabile.\n\n" +
        "## Servicii suplimentare\n\n" +
        "Prestatorul va furniza Beneficiarului următoarele servicii adiționale:\n\n" +
        "- consultanță tehnică dedicată, în limita orelor convenite lunar;\n" +
        "- raportare extinsă privind indicatorii de performanță;\n" +
        "- suport prioritar prin canal de comunicare dedicat.\n\n" +
        "## Modificări tarifare\n\n" +
        "Tarifele aplicabile serviciilor suplimentare sunt cele prevăzute în anexa comercială, exprimate în lei, fără TVA. Orice ajustare se comunică în scris cu minim 30 de zile înainte de aplicare.\n\n" +
        "## Valabilitate\n\n" +
        "Prezenta anexă intră în vigoare la data semnării de către ambele părți și își produce efectele pe toată durata contractului-cadru, dacă părțile nu convin altfel.\n\n" +
        "## Dispoziții finale\n\n" +
        "Orice modificare ulterioară a prezentei anexe se face prin act adițional, semnat electronic de către reprezentanții autorizați ai părților.\n\n" +
        "---\n\nPrestator [SIGNATURE]\n\nBeneficiar [SIGNATURE]\n";

    private static readonly TemplateSeed[] _templateSeeds =
    [
        new("Contract de închiriere · Apartament", "Model standard pentru locațiune rezidențială, conform legislației RO.", TemplateCategory.RealEstate, "QES", 87, "contract-inchiriere-apartament", "Imobiliare · locatiune"),
        new("NDA · Acord de confidențialitate", "Acord de confidențialitate bilateral, clauze comerciale uzuale.", TemplateCategory.Legal, "AdES", 145, "nda-confidentialitate", "Legal · NDA"),
        new("Contract individual de muncă", "Conform Codului Muncii al României, normă întreagă, durată nedeterminată.", TemplateCategory.HR, "QES", 62, "contract-individual-munca", "HR · CIM"),
        new("Acord servicii / SLA", "Acord-cadru de prestări servicii cu indicatori de performanță (SLA).", TemplateCategory.Business, "AdES", 34, "acord-servicii-sla", "Business · SLA"),
        new("Procură notarială", "Model de procură pentru reprezentare în fața autorităților publice.", TemplateCategory.Legal, "QES", 28, "procura-notariala", "Legal · procura"),
        new("Anexă servicii · Termeni", "Anexă cu termeni și condiții particulare la un contract-cadru.", TemplateCategory.Business, "SES", 18, "anexa-servicii-termeni", "Business · anexa"),
    ];

    private static async Task SeedTemplatesAsync(AppDbContext db, ILogger logger, string contentRootPath, CancellationToken cancellationToken)
    {
        // Must use the SAME root as TemplateStoragePaths (IWebHostEnvironment.ContentRootPath).
        // Using Directory.GetCurrentDirectory() diverges when CWD != ContentRootPath
        // (e.g. `dotnet run --project src/MasterSTI.Api` from repo root) — seeded
        // absolute paths then fail ValidateInsideTemplatesRoot at read time.
        var templatesRoot = SysPath.Combine(contentRootPath, "storage", "templates");
        Directory.CreateDirectory(templatesRoot);

        var renderer = new TemplatePdfRenderer();
        var now = DateTime.UtcNow;

        var existing = await db.Templates
            .Where(t => t.OrganizationId == SeedOrganizationId)
            .ToListAsync(cancellationToken);

        var inserted = 0;
        var refreshed = 0;

        foreach (var seed in _templateSeeds)
        {
            var pdfPath = SysPath.Combine(templatesRoot, $"{seed.FileSlug}.pdf");
            var bodyMarkdown = BuildSeedBody(seed);
            var row = existing.FirstOrDefault(t => t.Title == seed.Title);

            // New row -> always render. Existing row -> only render when the
            // current body still matches the legacy V1 seed text (i.e. the
            // user hasn't edited it via the content editor or replaced the
            // PDF). This preserves user customisations across restarts.
            // Also re-render when the PDF file is missing on disk (e.g. the
            // storage folder was wiped or never created) OR when the stored
            // absolute path lies outside the current TemplatesRoot — that
            // happens when ContentRootPath changed between runs (e.g. publish
            // vs. VS debug from src), and the read path validates against the
            // current root only.
            var rootWithSep = templatesRoot.EndsWith(SysPath.DirectorySeparatorChar)
                ? templatesRoot
                : templatesRoot + SysPath.DirectorySeparatorChar;
            var pathOutsideRoot = row is not null
                                  && !string.IsNullOrEmpty(row.PdfPath)
                                  && !SysPath.GetFullPath(row.PdfPath)
                                            .StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
            var pdfMissing = row is not null
                             && (string.IsNullOrEmpty(row.PdfPath) || !File.Exists(row.PdfPath));
            var shouldRender = row is null
                               || pdfMissing
                               || pathOutsideRoot
                               || (!string.IsNullOrEmpty(row.BodyMarkdown)
                                   && row.BodyMarkdown!.Contains(V1GenericSentinel, StringComparison.Ordinal));

            string? finalPdfPath = row?.PdfPath;
            // When body is customised but PDF gone, re-render from existing
            // body so we don't lose user edits. Otherwise use seed body.
            var renderFromSeedBody = row is null
                                     || (!string.IsNullOrEmpty(row.BodyMarkdown)
                                         && row.BodyMarkdown!.Contains(V1GenericSentinel, StringComparison.Ordinal));
            if (shouldRender)
            {
                finalPdfPath = pdfPath;
                try
                {
                    var sourceBody = renderFromSeedBody ? bodyMarkdown : row!.BodyMarkdown!;
                    var bytes = renderer.Render(seed.Title, sourceBody);
                    await File.WriteAllBytesAsync(pdfPath, bytes, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to generate seed PDF for template {Title}; row will be (re)inserted with null PdfPath.", seed.Title);
                    finalPdfPath = null;
                }
            }

            if (row is null)
            {
                db.Templates.Add(new Template
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = SeedOrganizationId,
                    Title = seed.Title,
                    Description = seed.Description,
                    Category = seed.Category,
                    PdfPath = finalPdfPath,
                    FieldsJson = null,
                    BodyMarkdown = bodyMarkdown,
                    DefaultLevel = seed.DefaultLevel,
                    UsageCount = seed.UsageCount,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDeleted = false
                });
                inserted++;
            }
            else
            {
                if (shouldRender)
                {
                    if (renderFromSeedBody)
                        row.BodyMarkdown = bodyMarkdown;
                    row.PdfPath = finalPdfPath;
                }
                row.Description = seed.Description;
                row.Category = seed.Category;
                row.DefaultLevel = seed.DefaultLevel;
                row.UpdatedAt = now;
                row.IsDeleted = false;
                refreshed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Seeded templates for org {Org}: inserted={Inserted}, refreshed={Refreshed}",
            SeedOrganizationId, inserted, refreshed);
    }

    /// <summary>
    /// Synthetic 7-day probe history per InfraHealthBar node so the dashboard
    /// sparkline renders meaningfully on a cold demo before the live
    /// <c>ProbeWriterService</c> has had a chance to accumulate samples.
    /// One row per ~2 h bucket per node, 84 buckets × 6 nodes = 504 rows.
    /// Health is weighted per node (most "ok", occasional "warn", rare "err");
    /// the deterministic seed keeps the visual reproducible across rebuilds.
    /// Skipped when the table already has any rows so live data never gets
    /// overwritten.
    /// </summary>
    public static async Task SeedProbeResultsAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var any = await db.ProbeResults.AsNoTracking().AnyAsync(cancellationToken);
        if (any) return;

        const int Buckets = 84;
        var window = TimeSpan.FromDays(7);
        var bucketSpan = window / Buckets;
        var now = DateTime.UtcNow;
        var start = now - window;

        // Per node: cumulative weights for {ok, warn, err}. Sum=100.
        var nodes = new (string Key, int OkPct, int WarnPct, int BaseRtt, int RttJitter)[]
        {
            ("qtsp",   95,  4,  140, 60),
            ("tsa",    97,  3,   95, 40),
            ("ocsp",   99,  1,   60, 25),
            ("ltv",   100,  0,    0,  0),  // filesystem probe, no rtt
            ("issuer", 90,  8,  120, 80),
            ("api",    99,  1,   12,  8),
        };

        var rng = new Random(20260514);
        var rows = new List<ProbeResult>(capacity: Buckets * nodes.Length);

        foreach (var node in nodes)
        {
            for (var i = 0; i < Buckets; i++)
            {
                var ts = start + bucketSpan * (i + 0.5);
                var roll = rng.Next(100);
                var health = roll < node.OkPct ? "ok"
                           : roll < node.OkPct + node.WarnPct ? "warn"
                           : "err";

                long? rtt = node.BaseRtt == 0
                    ? null
                    : node.BaseRtt + rng.Next(-node.RttJitter, node.RttJitter + 1);

                rows.Add(new ProbeResult
                {
                    Node = node.Key,
                    Timestamp = ts,
                    Health = health,
                    RttMs = rtt
                });
            }
        }

        db.ProbeResults.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} synthetic ProbeResult rows across {Nodes} nodes", rows.Count, nodes.Length);
    }
}
