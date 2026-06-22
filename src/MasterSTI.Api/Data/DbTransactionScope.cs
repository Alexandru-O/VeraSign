using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MasterSTI.Api.Data;

/// <summary>Begins a transaction only when the provider is relational (in-memory/test provider is a no-op).</summary>
public static class DbTransactionScope
{
    public static Task<IDbContextTransaction?> BeginIfRelationalAsync(DbContext db, CancellationToken ct)
    {
        return db.Database.IsRelational()
            ? BeginAsync(db, ct)
            : Task.FromResult<IDbContextTransaction?>(null);

        static async Task<IDbContextTransaction?> BeginAsync(DbContext db, CancellationToken ct)
            => await db.Database.BeginTransactionAsync(ct);
    }

    public static async Task CommitIfAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is null) return;
        await tx.CommitAsync(ct);
        await tx.DisposeAsync();
    }

    public static async Task RollbackIfAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is null) return;
        try { await tx.RollbackAsync(ct); } catch { }
        await tx.DisposeAsync();
    }
}
