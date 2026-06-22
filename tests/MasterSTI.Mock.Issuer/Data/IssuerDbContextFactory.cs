using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MasterSTI.Mock.Issuer.Data;

/// <summary>
/// Design-time factory used only by <c>dotnet ef</c> (migrations). Keeps the EF tooling
/// off Program.cs so scaffolding never triggers the migrate-on-startup path.
/// </summary>
public sealed class IssuerDbContextFactory : IDesignTimeDbContextFactory<IssuerDbContext>
{
    public IssuerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IssuerDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=MasterSTI_Issuer;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True")
            .Options;
        return new IssuerDbContext(options);
    }
}
