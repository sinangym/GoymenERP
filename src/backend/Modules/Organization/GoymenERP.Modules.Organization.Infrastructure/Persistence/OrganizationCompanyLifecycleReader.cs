using System.Diagnostics.Metrics;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>Organization-owned directory: a system read, never an actor membership or a grant.</summary>
public sealed class OrganizationCompanyLifecycleReader(OrganizationDbContext database)
    : ICompanyLifecycleReadPortV1, IOutboxCompanyDirectoryV1
{
    private static readonly Meter Meter = new("GoymenERP.Organization.CompanySetup", "1.0");
    private static int pendingCount;
    private static readonly ObservableGauge<int> PendingGauge = Meter.CreateObservableGauge(
        "organization.company.setup_pending", () => Volatile.Read(ref pendingCount), "companies");

    public async Task<CompanyLifecycleViewV1?> ReadAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) return null;
        var company = await database.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        if (company is null) return null;
        var state = company.LifecycleState switch
        {
            "Active" => CompanyLifecycleStateV1.Active,
            "SetupPending" => CompanyLifecycleStateV1.SetupPending,
            _ => throw new InvalidOperationException("Company lifecycle cannot be read."),
        };
        return new(company.Id, state, company.IsActive, company.Version);
    }

    public async Task<IReadOnlyList<Guid>> ListCompanyIdsAsync(CancellationToken cancellationToken)
    {
        // Include pending companies: otherwise the events which complete setup could never be delivered.
        // Companies is the unfiltered Organization root directory; this does not bypass a tenant query filter.
        var rows = await database.Companies.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.LifecycleState }).ToListAsync(cancellationToken);
        Volatile.Write(ref pendingCount, rows.Count(x => x.LifecycleState == "SetupPending"));
        return rows.Select(x => x.Id).ToArray();
    }
}
