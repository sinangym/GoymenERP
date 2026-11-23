using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Reporting.Domain;
using GoymenERP.Modules.Reporting.Infrastructure;
using GoymenERP.Modules.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Reporting.UnitTests;

/// <summary>SENTETIK test doubles: in-memory Reporting store; owners, authority and audit are fakes.</summary>
internal sealed class ReportingHarness
{
    public static readonly Guid Company = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    public static readonly Guid OtherCompany = Guid.Parse("c0000000-0000-4000-8000-000000000002");
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly string name = "reporting-" + Guid.NewGuid().ToString("N");

    public CollectingAudit Audit { get; } = new();
    public FakeStockAging StockAging { get; } = new();
    public FakeRecall Recall { get; } = new();
    public FakeTrialBalance TrialBalance { get; } = new();
    public FakePartyAging PartyAging { get; } = new();
    public ReportingNotConnectedSourcesV1 NotConnected { get; } = new();
    public FakeAuthority? LastAuthority { get; private set; }

    public ReportingServiceV1 Service(Guid actor, params string[] permissions) => Service(actor, Company, new FakeAuthority(permissions));

    public ReportingServiceV1 Service(Guid actor, Guid companyId, FakeAuthority authority)
    {
        LastAuthority = authority;
        var company = new FixedCompany(companyId);
        return new ReportingServiceV1(Database(company), company, new FakeUser(actor, companyId), authority, StockAging, NotConnected, Recall,
            NotConnected, TrialBalance, PartyAging, Audit, new FakeCorrelation(), new FixedTime());
    }

    public ReportingDbContext Database(ICompanyContext? company = null) => new(
        new DbContextOptionsBuilder<ReportingDbContext>().UseInMemoryDatabase(name).Options, company ?? new FixedCompany(Company));
}

internal sealed class FixedCompany(Guid companyId) : ICompanyContext
{
    public bool IsResolved => true;
    public Guid CompanyId { get; private set; } = companyId;
    public void Set(Guid value) => CompanyId = value;
}

internal sealed class FakeUser(Guid userId, Guid companyId) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public string? UserId => userId.ToString("D");
    public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
    public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
}

/// <summary>Grants exactly the listed permissions company-wide; <see cref="RevokeAfter"/> takes everything away after N measurements.</summary>
internal sealed class FakeAuthority(params string[] granted) : IErpAuthorityRevalidationService
{
    private readonly HashSet<string> permissions = new(granted, StringComparer.Ordinal);
    public int Calls { get; private set; }
    public int? RevokeAfter { get; set; }

    public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (RevokeAfter is { } limit && Calls > limit) return Task.FromResult(PermissionScope.Denied);
        return Task.FromResult(permissions.Contains(permission)
            ? new PermissionScope(true, true, new HashSet<string>(["ROLE-TEST"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(),
                new HashSet<Guid>())
            : PermissionScope.Denied);
    }
}

internal sealed class CollectingAudit : IAuditEventCollector
{
    public List<AuditEventDraft> Events { get; } = [];
    public void Enqueue(AuditEventDraft auditEvent) => Events.Add(auditEvent);
}

internal sealed class FakeCorrelation : ICorrelationContext
{
    public bool IsResolved => true;
    public string CorrelationId => "corr-test";
    public string? CausationId => "cause-test";
    public string SourceChannel => "api";
    public string? ClientIpAddress => null;
    public string? UserAgent => null;
    public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
}

internal sealed class FixedTime : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ReportingHarness.Now;
}

internal sealed class FakeStockAging : IReportingStockAgingSourceV1
{
    public List<Guid> AskedCompanies { get; } = [];
    public Guid? AnswerAs { get; set; }
    public ReportingSourceOutcomeV1 Outcome { get; set; } = ReportingSourceOutcomeV1.Found;
    public List<ReportingStockAgingSourceRowV1> Rows { get; } = [];

    public Task<ReportingStockAgingSourcePageV1> ReadAsync(Guid companyId, int page, int pageSize, Guid? productId, Guid? locationId,
        CancellationToken cancellationToken = default)
    {
        AskedCompanies.Add(companyId);
        var slice = Rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Task.FromResult(new ReportingStockAgingSourcePageV1(Outcome, AnswerAs ?? companyId, slice, Rows.Count > page * pageSize));
    }
}

internal sealed class FakeRecall : IReportingRecallSourceV1
{
    public ReportingSourceOutcomeV1 Outcome { get; set; } = ReportingSourceOutcomeV1.Found;
    public List<ReportingRecallSourceRowV1> Rows { get; } = [];

    public Task<ReportingRecallSourceV1> ReadAsync(Guid companyId, Guid labelId, bool forward, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportingRecallSourceV1(Outcome, companyId, Rows, false, [new("partyStatus", "Answered")]));
}

internal sealed class FakeTrialBalance : IReportingTrialBalanceSourceV1
{
    public Task<ReportingTrialBalanceSourceV1> ReadAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportingTrialBalanceSourceV1(ReportingSourceOutcomeV1.Found, companyId,
            [new(Guid.Parse("a1000000-0000-4000-8000-000000000001"), Guid.Parse("e1000000-0000-4000-8000-000000000001"), "100", "40", "Debit", "60")],
            "sha256:test"));
}

internal sealed class FakePartyAging : IReportingPartyAgingSourceV1
{
    public List<(Guid Company, bool Receivable)> Asked { get; } = [];
    public bool WindowFull { get; set; }
    public List<ReportingPartyAgingItemV1> Receivables { get; } = [];
    public List<ReportingPartyAgingItemV1> Payables { get; } = [];

    public Task<ReportingPartyAgingSourceV1> ReadAsync(Guid companyId, bool receivable, CancellationToken cancellationToken = default)
    {
        Asked.Add((companyId, receivable));
        return Task.FromResult(new ReportingPartyAgingSourceV1(ReportingSourceOutcomeV1.Found, companyId,
            receivable ? Receivables : Payables, WindowFull));
    }
}
