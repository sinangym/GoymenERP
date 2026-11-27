using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// W1-A test doubles of the Manufacturing Work inbox source tests: in-memory rows (the source's queries run over LINQ to
/// objects, so the explicit company and state filters are what is proven — no global query filter helps), a rows double that
/// only translates the queries for SQL Server offline (no connection is opened), and the fence's user and authority. SENTETIK.
/// </summary>
internal sealed class ManufacturingInMemoryWorkInboxRows : ManufacturingWorkInboxRows
{
    public List<PlanRow> PlanRows { get; } = [];
    public List<PlanInputRow> InputRows { get; } = [];
    public List<PlanRevisionInputRow> RevisionInputRows { get; } = [];
    public List<PlanRevisionRow> RevisionRows { get; } = [];
    public List<PlanRejectionRow> RejectionRows { get; } = [];
    public List<PlanReapprovalRow> ReapprovalRows { get; } = [];
    public List<OrderRow> OrderRows { get; } = [];
    public bool FailReads { get; set; }
    public int Reads { get; private set; }

    public override IQueryable<PlanRow> Plans => PlanRows.AsQueryable();
    public override IQueryable<PlanRevisionRow> PlanRevisions => RevisionRows.AsQueryable();
    public override IQueryable<PlanInputRow> PlanInputs => InputRows.AsQueryable();
    public override IQueryable<PlanRevisionInputRow> PlanRevisionInputs => RevisionInputRows.AsQueryable();
    public override IQueryable<PlanRejectionRow> PlanRejections => RejectionRows.AsQueryable();
    public override IQueryable<PlanReapprovalRow> PlanReapprovals => ReapprovalRows.AsQueryable();
    public override IQueryable<OrderRow> Orders => OrderRows.AsQueryable();

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Reads++;
        return FailReads ? throw new InvalidOperationException("SENTETIK read failure") : Task.FromResult(query.ToList());
    }
}

/// <summary>Translates each query with the module's own SQL Server model; returns nothing and never connects.</summary>
internal sealed class ManufacturingSqlTranslatingWorkInboxRows : ManufacturingWorkInboxRows, IDisposable
{
    private readonly ManufacturingDbContext dbContext;
    private readonly ManufacturingWorkInboxDbRows inner;

    public ManufacturingSqlTranslatingWorkInboxRows(Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        dbContext = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer("Server=unopened.invalid;Database=synthetic;Connect Timeout=1;").Options, company);
        inner = new ManufacturingWorkInboxDbRows(dbContext);
    }

    public List<string> Sql { get; } = [];

    public override IQueryable<PlanRow> Plans => inner.Plans;
    public override IQueryable<PlanRevisionRow> PlanRevisions => inner.PlanRevisions;
    public override IQueryable<PlanInputRow> PlanInputs => inner.PlanInputs;
    public override IQueryable<PlanRevisionInputRow> PlanRevisionInputs => inner.PlanRevisionInputs;
    public override IQueryable<PlanRejectionRow> PlanRejections => inner.PlanRejections;
    public override IQueryable<PlanReapprovalRow> PlanReapprovals => inner.PlanReapprovals;
    public override IQueryable<OrderRow> Orders => inner.Orders;

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Sql.Add(query.ToQueryString());
        return Task.FromResult(new List<T>());
    }

    public void Dispose() => dbContext.Dispose();
}

internal sealed class ManufacturingWorkInboxTestUser(string userId, params Guid[] companies) : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;
    public string? UserId { get; set; } = userId;
    public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(companies);
    public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
}

internal sealed class ManufacturingWorkInboxTestAuthority : IErpAuthorityRevalidationService
{
    public static readonly PermissionScope CompanyWide = new(true, true, new HashSet<string>(StringComparer.Ordinal),
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    public static readonly PermissionScope WorkCenterOnly = CompanyWide with { IsCompanyWide = false };

    public Func<string, Guid, PermissionScope> Answer { get; set; } = (_, _) => CompanyWide;
    public List<(string Permission, Guid CompanyId)> Calls { get; } = [];

    public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((permission, companyId));
        return Task.FromResult(Answer(permission, companyId));
    }
}

/// <summary>The one host setting the module registration reads (no configuration package in this test project).</summary>
internal sealed class ManufacturingWorkInboxTestSettings : IConfiguration
{
    public string? this[string key]
    {
        get => key == "DB_CONNECTION_STRING" ? "Server=unopened.invalid;Database=synthetic;" : null;
        set => throw new NotSupportedException();
    }

    public IEnumerable<IConfigurationSection> GetChildren() => [];

    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
}
