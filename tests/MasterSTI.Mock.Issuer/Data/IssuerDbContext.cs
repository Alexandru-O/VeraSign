using Microsoft.EntityFrameworkCore;

namespace MasterSTI.Mock.Issuer.Data;

/// <summary>
/// The Mock EUDIW Issuer's own database (<c>MasterSTI_Issuer</c>). VeraSign holds no
/// connection string to it — the trust boundary between Issuer and Relying Party is a
/// process/database boundary, not a nominal one. See ADR-0005.
/// </summary>
public sealed class IssuerDbContext(DbContextOptions<IssuerDbContext> options) : DbContext(options)
{
    public DbSet<Identity> Identities => Set<Identity>();
    public DbSet<IssuedCredential> IssuedCredentials => Set<IssuedCredential>();

    // Fixed keys so the Demo Persona seed is stable across migrations.
    public static readonly Guid TomaId = new("a1a1a1a1-0000-0000-0000-000000000001");
    public static readonly Guid TheaId = new("a2a2a2a2-0000-0000-0000-000000000002");

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Identity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FamilyName).HasMaxLength(128).IsRequired();
            e.Property(x => x.GivenName).HasMaxLength(128).IsRequired();
            e.Property(x => x.BirthDate).HasMaxLength(10).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();

            e.HasMany(x => x.IssuedCredentials)
                .WithOne(x => x.Identity!)
                .HasForeignKey(x => x.IdentityId)
                .OnDelete(DeleteBehavior.Cascade);

            // Demo Personas — must match mobile/MasterSTI.Wallet/Models/DemoPersona.cs.
            e.HasData(
                new Identity
                {
                    Id = TomaId,
                    FamilyName = "Iliescu",
                    GivenName = "Toma",
                    BirthDate = "1985-03-04",
                    Email = "toma.iliescu@verasign.demo"
                },
                new Identity
                {
                    Id = TheaId,
                    FamilyName = "Popescu",
                    GivenName = "Thea",
                    BirthDate = "1992-07-19",
                    Email = "thea.popescu@verasign.demo"
                });
        });

        model.Entity<IssuedCredential>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CnfJwkThumbprint).HasMaxLength(64);
            e.HasIndex(x => x.IdentityId);
        });
    }
}
