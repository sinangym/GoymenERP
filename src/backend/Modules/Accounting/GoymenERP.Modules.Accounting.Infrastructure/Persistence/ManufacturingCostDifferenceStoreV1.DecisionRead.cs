using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class ManufacturingCostDifferenceStoreV1 : IManufacturingCostDifferenceDecisionStoreV1
{
    public async Task<ManufacturingCostDifferenceDecisionSourceV1?> ReadCostDifferenceDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence); cancellationToken.ThrowIfCancellationRequested();
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested(); var companyId = Company();
        var row = await db.ManufacturingCostDifferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == proposalId, cancellationToken);
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested(); RequireCompany(companyId);
        if (row is null) return null;
        ManufacturingCostDifferenceViewV1 view;
        try
        {
            var source = JsonSerializer.Deserialize<ManufacturingCostDifferenceEventV1>(row.SourceJson, Json);
            if (source is null || !ManufacturingCostDifferenceServiceV1.ReadableCostDifferenceOutputs(source)
                || row.Status is not (1 or 2 or 3 or 4)) return Malformed();
            view = View(row);
        }
        catch (JsonException) { return Malformed(); }
        // Retained native SourceJson/AccountPlanProofJson provide the full original human facts; no live plan lookup.
        var bound = false; string? journalCorrelation = null;
        if (row.JournalId is { } journalId)
        {
            var journal = await db.Journals.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == journalId)
                .Select(x => new { x.SourceId, x.PeriodId, x.CurrencyId, x.TransactionDate, x.AccountingDate, x.StoredBy, x.CorrelationId })
                .SingleOrDefaultAsync(cancellationToken);
            await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested(); RequireCompany(companyId);
            bound = journal is not null && journal.SourceId == row.Id && journal.PeriodId == row.PeriodId
                && journal.CurrencyId == row.CurrencyId && journal.TransactionDate == row.TransactionDate
                && journal.AccountingDate == row.AccountingDate && Guid.TryParse(journal.StoredBy, out var checker) && checker == row.ApprovedBy;
            journalCorrelation = bound ? journal!.CorrelationId : null;
        }
        return new(view, row.SourceJson, row.SourceHash, row.SourceCorrelationId, row.CurrencyId,
            row.PreparedAtUtc, row.ApprovedAtUtc, bound, journalCorrelation);
        ManufacturingCostDifferenceDecisionSourceV1 Malformed() => new(null!, row.SourceJson, row.SourceHash,
            row.SourceCorrelationId, row.CurrencyId, row.PreparedAtUtc, row.ApprovedAtUtc, false, null);
    }
}
