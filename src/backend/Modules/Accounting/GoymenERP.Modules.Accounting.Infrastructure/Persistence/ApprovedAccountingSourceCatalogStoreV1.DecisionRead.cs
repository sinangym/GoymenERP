using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class ApprovedAccountingSourceCatalogStoreV1 : IApprovedAccountingSourceCatalogDecisionStoreV1
{
    public async Task<ApprovedAccountingSourceCatalogDecisionSourceV1?> ReadActivationDecisionAsync(Guid packageId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default)
    {
        var record = await ReadActivationReceiptSourceAsync(packageId, freshReadFence, cancellationToken);
        if (record is null) return null;
        RequireCompany(); var company = companyContext.CompanyId;
        try
        {
            var active = await dbContext.ApprovedSourceCatalogs.AsNoTracking()
                .Where(row => row.CompanyId == company && row.Status == Active).Select(row => new { row.CatalogVersion }).SingleOrDefaultAsync(cancellationToken);
            await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (active is not null && active.CatalogVersion is not > 0) throw DecisionUnavailable();
            return new(record, active?.CatalogVersion ?? 0);
        }
        catch (SqlException error) { throw DecisionUnavailable(error); }
    }

    public async Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActivationReceiptSourceAsync(Guid packageId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence);
        cancellationToken.ThrowIfCancellationRequested(); await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(); var company = companyContext.CompanyId;
        try
        {
            var row = await dbContext.ApprovedSourceCatalogs.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.CompanyId == company && candidate.Id == packageId, cancellationToken);
            await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            return row is null ? null : RestoreDecision(row);
        }
        catch (SqlException error) { throw DecisionUnavailable(error); }
    }
    private ApprovedAccountingSourceCatalogRecordV1 RestoreDecision(ApprovedAccountingSourceCatalogRow row)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<PackageSnapshot>(row.PackageJson, Json);
            if (snapshot is null || snapshot.Accounts is null || snapshot.Bindings is null
                || snapshot.Accounts.Any(account => account is null)
                || snapshot.Bindings.Any(binding => binding is null || binding.Mappings is null || binding.Mappings.Any(mapping => mapping is null))
                || !JsonNode.DeepEquals(JsonNode.Parse(row.PackageJson), JsonSerializer.SerializeToNode(snapshot, Json))) throw DecisionUnavailable();
            return Restore(row);
        }
        catch (JsonException error) { throw DecisionUnavailable(error); }
        catch (AccountingDomainException error) { throw DecisionUnavailable(error); }
        catch (ArgumentException error) { throw DecisionUnavailable(error); }
    }
    private static AccountingStoreException DecisionUnavailable(Exception? inner = null) => new(AccountingStoreCodes.ProviderUnavailable,
        "The activation-purpose source package cannot be established.", inner);
}
