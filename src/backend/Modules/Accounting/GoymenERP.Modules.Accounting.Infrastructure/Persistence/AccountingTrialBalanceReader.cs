using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// SQL-backed aggregate of posted journal lines. The company is both an explicit join/filter and
/// the DbContext query-filter tenant; a context change after this read is caught by the service's
/// second authority fence before any result is released.
/// </summary>
public sealed class AccountingTrialBalanceReader(
    AccountingDbContext dbContext,
    ICompanyContext companyContext) : IAccountingTrialBalanceReadStore
{
    public async Task<IReadOnlyList<TrialBalanceReadRowV1>> ReadAsync(
        DateOnly fromAccountingDate,
        DateOnly throughAccountingDate,
        Guid? currencyId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new AccountingStoreException(
                AccountingStoreCodes.CompanyContextMismatch,
                "A resolved company is required to read the trial balance.");
        }

        var companyId = companyContext.CompanyId;
        var query =
            from line in dbContext.JournalLines.AsNoTracking()
            join journal in dbContext.Journals.AsNoTracking()
                on new { line.CompanyId, Id = line.JournalId }
                equals new { journal.CompanyId, journal.Id }
            where line.CompanyId == companyId
                  && journal.AccountingDate >= fromAccountingDate
                  && journal.AccountingDate <= throughAccountingDate
                  && (!currencyId.HasValue || journal.CurrencyId == currencyId.Value)
            select new
            {
                line.AccountId,
                journal.CurrencyId,
                line.Debit,
                line.Credit,
            };

        var rows = await query
            .GroupBy(row => new { row.AccountId, row.CurrencyId })
            .Select(group => new
            {
                group.Key.AccountId,
                group.Key.CurrencyId,
                Debit = group.Sum(row => row.Debit),
                Credit = group.Sum(row => row.Credit),
            })
            .OrderBy(row => row.CurrencyId)
            .ThenBy(row => row.AccountId)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row => new TrialBalanceReadRowV1(row.AccountId, row.CurrencyId, row.Debit, row.Credit))
            .ToArray();
    }
}
