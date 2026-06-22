using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<SigningRequest> SigningRequests => Set<SigningRequest>();
    public DbSet<SignedDocument> SignedDocuments => Set<SignedDocument>();

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<SignatureField> SignatureFields => Set<SignatureField>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<WalletEnrollment> WalletEnrollments => Set<WalletEnrollment>();
    public DbSet<ProbeResult> ProbeResults => Set<ProbeResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.FileName).HasMaxLength(512).IsRequired();
            e.Property(d => d.ContentType).HasMaxLength(128).IsRequired();
            e.Property(d => d.StoragePath).HasMaxLength(1024).IsRequired();
            e.Property(d => d.Sha256Hash).HasMaxLength(64).IsRequired();
            e.HasIndex(d => d.UploadedAt);
            e.HasIndex(d => d.OrganizationId);
        });

        modelBuilder.Entity<SigningRequest>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Document)
             .WithMany()
             .HasForeignKey(s => s.DocumentId);
            e.HasOne(s => s.Recipient)
             .WithMany()
             .HasForeignKey(s => s.RecipientId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => s.DocumentId);
            e.HasIndex(s => s.RecipientId);
            e.HasIndex(s => new { s.DocumentId, s.OrderIndex });
            e.Property(s => s.RequestedBy).HasMaxLength(256);
            e.Property(s => s.CredentialId).HasMaxLength(256);
            e.Property(s => s.SignatureLevel).HasMaxLength(64);
            e.Property(s => s.DocumentHash).HasMaxLength(128);
            e.Property(s => s.PreparedStoragePath).HasMaxLength(1024);
            e.Property(s => s.EudiwSubject).HasMaxLength(512);
        });

        modelBuilder.Entity<SignedDocument>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.OriginalDocument)
             .WithMany()
             .HasForeignKey(s => s.OriginalDocumentId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.SigningRequest)
             .WithMany()
             .HasForeignKey(s => s.SigningRequestId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Recipient)
             .WithMany()
             .HasForeignKey(s => s.RecipientId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.PreviousSignedDocument)
             .WithMany()
             .HasForeignKey(s => s.PreviousSignedDocumentId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => s.SigningRequestId);
            e.HasIndex(s => s.RecipientId);
            e.HasIndex(s => s.PreviousSignedDocumentId);
            e.HasIndex(s => new { s.OriginalDocumentId, s.IsFinal });
            e.Property(s => s.StoragePath).HasMaxLength(1024);
            e.Property(s => s.PadesLevel).HasMaxLength(64);
        });

        modelBuilder.Entity<Organization>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Name).HasMaxLength(256).IsRequired();
            e.HasIndex(o => o.Name);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.Name).HasMaxLength(256).IsRequired();
            e.Property(u => u.Role).HasMaxLength(64).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
            e.HasOne(u => u.Organization)
             .WithMany()
             .HasForeignKey(u => u.OrganizationId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(u => u.OrganizationId);
        });

        modelBuilder.Entity<Template>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Title).HasMaxLength(256).IsRequired();
            e.Property(t => t.Description).HasMaxLength(2048);
            e.Property(t => t.PdfPath).HasMaxLength(1024);
            e.Property(t => t.FieldsJson).HasColumnType("nvarchar(max)");
            e.Property(t => t.BodyMarkdown).HasColumnType("nvarchar(max)");
            e.Property(t => t.DefaultLevel).HasMaxLength(32).IsRequired();
            e.HasOne(t => t.Organization)
             .WithMany()
             .HasForeignKey(t => t.OrganizationId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.OrganizationId);
            e.HasIndex(t => t.Category);
            e.HasIndex(t => t.IsDeleted);
        });

        modelBuilder.Entity<SignatureField>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Type).HasMaxLength(32).IsRequired();
            e.HasOne(f => f.Document)
             .WithMany()
             .HasForeignKey(f => f.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => f.DocumentId);
            e.HasIndex(f => f.RecipientId);
        });

        modelBuilder.Entity<Recipient>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Email).HasMaxLength(256).IsRequired();
            e.Property(r => r.Name).HasMaxLength(256).IsRequired();
            e.Property(r => r.Level).HasMaxLength(32).IsRequired();
            e.HasOne(r => r.Document)
             .WithMany()
             .HasForeignKey(r => r.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.DocumentId);
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.EventType).HasMaxLength(64).IsRequired();
            e.Property(a => a.Actor).HasMaxLength(256).IsRequired();
            e.Property(a => a.IpAddress).HasMaxLength(64);
            e.Property(a => a.UserAgent).HasMaxLength(512);
            e.Property(a => a.Metadata).HasColumnType("nvarchar(max)");
            e.HasOne(a => a.Document)
             .WithMany()
             .HasForeignKey(a => a.DocumentId)
             .OnDelete(DeleteBehavior.Cascade)
             .IsRequired(false);
            e.HasIndex(a => a.DocumentId);
            e.HasIndex(a => a.Timestamp);
        });

        modelBuilder.Entity<WalletEnrollment>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.CnfJwkThumbprint).HasMaxLength(128).IsRequired();
            e.Property(w => w.PidClaimsJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(w => w.PidEmail).HasMaxLength(254);
            e.HasOne(w => w.User)
             .WithMany()
             .HasForeignKey(w => w.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(w => w.UserId).IsUnique();
            e.HasIndex(w => w.CnfJwkThumbprint).IsUnique();
            e.HasIndex(w => w.PidEmail).HasFilter("[PidEmail] IS NOT NULL");
        });

        modelBuilder.Entity<ProbeResult>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Node).HasMaxLength(32).IsRequired();
            e.Property(p => p.Health).HasMaxLength(8).IsRequired();
            e.HasIndex(p => new { p.Node, p.Timestamp });
            e.HasIndex(p => p.Timestamp);
        });
    }
}

/// <summary>
/// Time-series probe sample for the dashboard InfraHealthBar sparkline. One row
/// per node per probe tick (~60 s). Retention: 7 days, pruned by the background
/// <c>ProbeWriterService</c>. Aggregated into hourly buckets by
/// <c>GetDiagnosticsHandler</c>.
/// </summary>
public class ProbeResult
{
    public long Id { get; set; }
    public string Node { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Health { get; set; } = string.Empty;
    public long? RttMs { get; set; }
}

public class Document
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public DocumentStatus Status { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? OwnerUserId { get; set; }
}

// Explicit ordinals so the persisted column survives if a future slot is added
// or retired. `Signing` is reserved — no handler writes it today, but it is
// still read by the DeleteDocumentHandler in-flight guard so removing it would
// silently drop the mid-CSC-roundtrip safety net. Wire writes when a real
// transition is needed (see TODO.md).
public enum DocumentStatus
{
    Uploaded = 0,
    Preparing = 1,
    Signing = 2,
    Signed = 3,
    Failed = 4,
    Awaiting = 5,
    Cancelled = 6
}

public class SigningRequest
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    /// <summary>
    /// The recipient this request is signing for. NOT NULL in the schema —
    /// every <c>SigningRequest</c> is bound to exactly one recipient so the
    /// pipeline can drill down per-signer in a multi-signer document.
    /// </summary>
    public Guid RecipientId { get; set; }
    public Recipient Recipient { get; set; } = null!;
    /// <summary>
    /// Denormalised from <see cref="Recipient.Order"/> at creation time so
    /// pipeline queries can sort/group without joining Recipients.
    /// </summary>
    public int OrderIndex { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string CredentialId { get; set; } = string.Empty;
    public string SignatureLevel { get; set; } = "PAdES-B-LT";
    public string DocumentHash { get; set; } = string.Empty;
    public string? PreparedStoragePath { get; set; }
    /// <summary>
    /// AcroForm signature-field name written into the prepared PDF by
    /// <c>PadesService.Prepare</c>. Embed must reuse the exact same name when
    /// calling <c>PdfSigner.SignDeferred</c> so the right widget gets the CMS
    /// container. Unique per-recipient — collision with prior signers in the
    /// chain would otherwise corrupt earlier signatures.
    /// </summary>
    public string? PreparedFieldName { get; set; }
    public string? EudiwSubject { get; set; }
    public SigningRequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// When <see cref="Status"/> is <see cref="SigningRequestStatus.Failed"/>,
    /// names the pipeline stage that caused the abort. 1=EUDIW auth init,
    /// 2=SD-JWT verify, 3=SAD consent, 4=CSC signHash, 5=PAdES embed. Null when
    /// not failed or when the failure pre-dates this column.
    /// </summary>
    public int? FailedAtStage { get; set; }
}

public enum SigningRequestStatus { Pending, HashPrepared, EudiwAuthorized, CredentialAuthorized, Signed, Embedded, Failed }

public class SignedDocument
{
    public Guid Id { get; set; }
    public Guid OriginalDocumentId { get; set; }
    public Document OriginalDocument { get; set; } = null!;
    public Guid SigningRequestId { get; set; }
    public SigningRequest SigningRequest { get; set; } = null!;
    /// <summary>
    /// The recipient whose signature this row carries. NOT NULL — denormalised
    /// from <see cref="SigningRequest.RecipientId"/> for fast per-recipient
    /// drill-downs without joining SigningRequests.
    /// </summary>
    public Guid RecipientId { get; set; }
    public Recipient Recipient { get; set; } = null!;
    /// <summary>
    /// For multi-signer chains, points at the previous <see cref="SignedDocument"/>
    /// in the stacked-PAdES chain. NULL for the first signer.
    /// </summary>
    public Guid? PreviousSignedDocumentId { get; set; }
    public SignedDocument? PreviousSignedDocument { get; set; }
    /// <summary>
    /// True when this is the last <see cref="SignedDocument"/> in its chain —
    /// the one carrying the archive timestamp (PAdES-B-LTA). Set on the final
    /// signer's row, or earlier if the flow was cancelled mid-chain.
    /// </summary>
    public bool IsFinal { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string PadesLevel { get; set; } = string.Empty;
    public string? TimestampToken { get; set; }
    public string? ValidationReport { get; set; }
    /// <summary>
    /// JSON-serialised <see cref="MasterSTI.Api.Common.Wysiwys.PageManifest"/> captured from the
    /// prepared PDF at embed time. Re-computed at validate time to detect post-signature visual
    /// tampering (shadow-attack class) that <c>SignatureCoversWholeDocument</c> can miss.
    /// </summary>
    public string? PageManifestJson { get; set; }
}

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public string PasswordHash { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public enum TemplateCategory { RealEstate, Legal, HR, Business, Custom }

public class Template
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TemplateCategory Category { get; set; }
    public string? PdfPath { get; set; }
    public string? FieldsJson { get; set; }
    /// <summary>
    /// Editable plain-text/markdown source for the template body. When present,
    /// authoritative for re-rendering the PDF via TemplatePdfRenderer.
    /// Cleared (set to NULL) when the user uploads a replacement PDF.
    /// </summary>
    public string? BodyMarkdown { get; set; }
    public string DefaultLevel { get; set; } = "AdES";
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public enum SignatureFieldType { Signature, Initial, Date, Text }

public class SignatureField
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public string Type { get; set; } = "Signature";
    public int Page { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Guid? RecipientId { get; set; }
}

public enum RecipientStatus { Pending, Notified, Signed, Declined }

public class Recipient
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Level { get; set; } = "AdES";
    public RecipientStatus Status { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public DateTime? SignedAt { get; set; }
}

public class AuditEvent
{
    public Guid Id { get; set; }
    public Guid? DocumentId { get; set; }
    public Document? Document { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Actor { get; set; } = "system";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Metadata { get; set; }
}

/// <summary>
/// EUDIW wallet enrollment per user. Written when a real SD-JWT carrying a
/// <c>cnf.jwk</c> is validated for a login/sign flow. One row per user (unique
/// on UserId), thumbprint unique across the table so two users cannot share a
/// device key. <see cref="PidClaimsJson"/> stores the disclosed PID payload
/// verbatim for audit + dashboard banner rendering.
/// </summary>
public class WalletEnrollment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string CnfJwkThumbprint { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string PidClaimsJson { get; set; } = string.Empty;
    /// <summary>
    /// Lowercased copy of the PID <c>email</c> disclosure extracted at upsert
    /// time, indexed for the wallet-inbox lookup. Null when the PID schema
    /// did not disclose email — those enrollments cannot participate in
    /// inbox matching.
    /// </summary>
    public string? PidEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
