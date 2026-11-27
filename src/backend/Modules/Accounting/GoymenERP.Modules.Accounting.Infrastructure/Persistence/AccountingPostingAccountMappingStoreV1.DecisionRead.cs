using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingPostingAccountMappingStoreV1 : IAccountingPostingAccountMappingDecisionStoreV1
{
    public async Task<AccountingPostingAccountMappingDecisionSourceV1?> ReadActivationDecisionAsync(long mappingVersion,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence); cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(); var company = companyContext.CompanyId;
        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await freshReadFence(token); token.ThrowIfCancellationRequested();
            RequireCompany(); if (companyContext.CompanyId != company) throw new NormalJournalAuthorityDeniedException();
        }
        await Fence(cancellationToken);
        var record = await ReadActivationReceiptSourceAsync(mappingVersion, Fence, cancellationToken);
        if (record is null) return null;
        try
        {
            var active = await dbContext.PostingAccountMappingVersions.AsNoTracking()
                .Where(row => row.CompanyId == company && row.Status == Active).Select(row => (long?)row.MappingVersion).SingleOrDefaultAsync(cancellationToken);
            await Fence(cancellationToken);
            if (active is not null && active <= 0) throw MappingDecisionUnavailable();
            var plan = await ActivePlanAsync(cancellationToken, Fence);
            await Fence(cancellationToken);
            if (plan is null || plan.CompanyId != company) throw MappingDecisionUnavailable();
            return new(record, active ?? 0, new(plan.CompanyId, new(plan.SourceVersion, plan.SourceSha256),
                Array.AsReadOnly(plan.Rows.OrderBy(row => row.Code, StringComparer.Ordinal)
                    .Select(row => new AccountingPostingMappingAccountV1(row.Account.AccountId, row.Code, row.Name)).ToArray())));
        }
        catch (SqlException error) { throw MappingDecisionUnavailable(error); }
        catch (JsonException error) { throw MappingDecisionUnavailable(error); }
        catch (AccountingDomainException error) { throw MappingDecisionUnavailable(error); }
        catch (ArgumentException error) { throw MappingDecisionUnavailable(error); }
    }

    public async Task<AccountingPostingAccountMappingRecordV1?> ReadActivationReceiptSourceAsync(long mappingVersion,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence); cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(); var company = companyContext.CompanyId;
        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await freshReadFence(token); token.ThrowIfCancellationRequested();
            RequireCompany(); if (companyContext.CompanyId != company) throw new NormalJournalAuthorityDeniedException();
        }
        try
        {
            await Fence(cancellationToken);
            var row = await dbContext.PostingAccountMappingVersions.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.CompanyId == company && candidate.MappingVersion == mappingVersion, cancellationToken);
            await Fence(cancellationToken);
            if (row is null) return null;
            var entries = await dbContext.PostingAccountMappingRows.AsNoTracking()
                .Where(candidate => candidate.CompanyId == company && candidate.MappingVersion == mappingVersion)
                .OrderBy(candidate => candidate.LineNumber).ToListAsync(cancellationToken);
            await Fence(cancellationToken);
            return Restore(row, entries);
        }
        catch (SqlException error) { throw MappingDecisionUnavailable(error); }
    }
    private static AccountingStoreException MappingDecisionUnavailable(Exception? inner = null) => new(AccountingStoreCodes.ProviderUnavailable,
        "The activation-purpose mapping facts cannot be established.", inner);
}
