using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// All physical validation, count approval and refused-attempt recording take this Inventory-owned mutex
/// before count-boundary/attempt/physical rows. It follows any host Sales then Purchasing locks. One company
/// resource avoids reversed acquisition in multi-product commands. Its lifetime is exactly the caller's TX.
/// The small borrowed context writes ONLY the technical guard: it must not flush the caller's staged
/// movements, allocation changes or pending audit. No connection, transaction or outer commit is owned here.
/// An absent-row insertion race is a technical failure/retry, never successful physical validation.
/// </summary>
internal static class InventoryNegativeStockValidationGuard
{
    internal static async Task AcquireAsync(InventoryDbContext owner, Guid companyId, CancellationToken token)
    {
        var transaction = owner.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Physical validation requires the owner's transaction.");
        var company = new CompanyContext(); company.Set(companyId);
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(owner.Database.GetDbConnection(), contextOwnsConnection: false).Options;
        await using var guard = new InventoryDbContext(options, company);
        await guard.Database.UseTransactionAsync(transaction, token);
        var row = await guard.NegativeStockValidationGuards.SingleOrDefaultAsync(
            item => item.CompanyId == companyId, token);
        if (row is null)
            guard.NegativeStockValidationGuards.Add(new() { CompanyId = companyId, Version = 1 });
        else row.Version = checked(row.Version + 1);
        // A real UPDATE/INSERT holds the SQL write lock until the borrowed transaction ends, including RCSI.
        // Version is optimistic concurrency proof: a competing reader never overwrites a newer guard.
        await guard.SaveChangesAsync(token);
    }
}
