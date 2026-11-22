using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// W1-A test doubles shared by the Logistics Work inbox source tests: in-memory rows (the sources' queries run over LINQ to
/// objects, so the explicit company and state filters are what is proven — no global query filter helps), a rows double that
/// only translates the queries for SQL Server offline (no connection is opened), and the fence's user and authority. SENTETIK.
/// </summary>
internal sealed class LogisticsInMemoryWorkInboxRows : LogisticsWorkInboxRows
{
    public List<LoadingInstructionRow> InstructionRows { get; } = [];
    public List<LoadingInstructionLineRow> LineRows { get; } = [];
    public List<DispatchNoteRow> DispatchNoteRows { get; } = [];
    public List<LogisticsPlanExcessRow> PlanExcessRows { get; } = [];
    public List<WeighingDifferenceRecordRow> WeighingDifferenceRows { get; } = [];
    public bool FailReads { get; set; }
    public int Reads { get; private set; }

    public override IQueryable<LoadingInstructionRow> Instructions => InstructionRows.AsQueryable();
    public override IQueryable<LoadingInstructionLineRow> Lines => LineRows.AsQueryable();
    public override IQueryable<DispatchNoteRow> DispatchNotes => DispatchNoteRows.AsQueryable();
    public override IQueryable<LogisticsPlanExcessRow> PlanExcessExceptions => PlanExcessRows.AsQueryable();
    public override IQueryable<WeighingDifferenceRecordRow> WeighingDifferences => WeighingDifferenceRows.AsQueryable();

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Reads++;
        return FailReads ? throw new InvalidOperationException("SENTETIK read failure") : Task.FromResult(query.ToList());
    }
}

/// <summary>Translates each query with the module's own SQL Server model; returns nothing and never connects.</summary>
internal sealed class LogisticsSqlTranslatingWorkInboxRows : LogisticsWorkInboxRows, IDisposable
{
    private readonly LogisticsDbContext dbContext;
    private readonly LogisticsWorkInboxDbRows inner;

    public LogisticsSqlTranslatingWorkInboxRows(Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        inner = new LogisticsWorkInboxDbRows(dbContext);
    }

    public List<string> Sql { get; } = [];

    public override IQueryable<LoadingInstructionRow> Instructions => inner.Instructions;
    public override IQueryable<LoadingInstructionLineRow> Lines => inner.Lines;
    public override IQueryable<DispatchNoteRow> DispatchNotes => inner.DispatchNotes;
    public override IQueryable<LogisticsPlanExcessRow> PlanExcessExceptions => inner.PlanExcessExceptions;
    public override IQueryable<WeighingDifferenceRecordRow> WeighingDifferences => inner.WeighingDifferences;

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Sql.Add(query.ToQueryString());
        return Task.FromResult(new List<T>());
    }

    public void Dispose() => dbContext.Dispose();
}

internal sealed class LogisticsWorkInboxTestUser(string userId, params Guid[] companies) : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;
    public string? UserId { get; set; } = userId;
    public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(companies);
    public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
}

internal sealed class LogisticsWorkInboxTestAuthority : IErpAuthorityRevalidationService
{
    public static readonly PermissionScope CompanyWide = new(true, true, new HashSet<string>(StringComparer.Ordinal),
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    public static readonly PermissionScope SiteOnly = CompanyWide with { IsCompanyWide = false };

    public Func<string, Guid, PermissionScope> Answer { get; set; } = (_, _) => CompanyWide;
    public List<(string Permission, Guid CompanyId)> Calls { get; } = [];

    public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((permission, companyId));
        return Task.FromResult(Answer(permission, companyId));
    }
}

/// <summary>The fixture every Logistics source test starts from: company A resolved, the approver signed in and granted.</summary>
internal sealed class LogisticsWorkInboxTestFixture
{
    public static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public const string Approver = "SENTETIK-approver";
    public const string Maker = "SENTETIK-maker";
    public static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.FromHours(3));

    public LogisticsWorkInboxTestFixture()
    {
        Company.Set(CompanyA);
        User = new LogisticsWorkInboxTestUser(Approver, CompanyA);
        Fence = new LogisticsWorkInboxFence(Company, User, Authority);
    }

    public CompanyContext Company { get; } = new();
    public LogisticsWorkInboxTestUser User { get; }
    public LogisticsWorkInboxTestAuthority Authority { get; } = new();
    public LogisticsWorkInboxFence Fence { get; }
    public LogisticsInMemoryWorkInboxRows Rows { get; } = new();

    public WorkInboxSourceContextV1 Context(string? userId = null, Guid? companyId = null) =>
        new(companyId ?? CompanyA, userId ?? Approver, "SENTETIK-correlation", null);

    public static Guid Id(int n) => Guid.Parse($"{n:D8}-0000-4000-8000-000000000000");

    public LoadingInstructionRow Instruction(Guid id, Guid? companyId = null, int status = 2)
    {
        var row = new LoadingInstructionRow
        {
            Id = id, CompanyId = companyId ?? CompanyA, CustomerPartyId = Id(9001), CompanyAccountId = Id(9002), Status = status,
            Version = 3, CreatedBy = Maker, CreatedAtUtc = T0, UpdatedAtUtc = T0, CorrelationId = "SENTETIK",
        };
        Rows.InstructionRows.Add(row);
        return row;
    }

    /// <summary>Every item satisfies the contract's shape rules (the host enforces them too).</summary>
    public static void AssertContractShape(WorkInboxSourceResultV1 result, string module, string kind)
    {
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.NotNull(result.Items);
        foreach (var item in result.Items!)
        {
            Assert.Equal((module, kind), (item.Module, item.Kind));
            Assert.True(NativeApprovalQueueRulesV1.IsValidKind(module, kind));
            Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link), item.Link);
            Assert.InRange(item.DocumentLabel.Length, 1, WorkInboxRulesV1.MaxDocumentLabelLength);
            Assert.Equal(TimeSpan.Zero, item.WaitingSinceUtc.Offset);
            Assert.False(string.IsNullOrWhiteSpace(item.PreparedBy));
            Assert.InRange(item.Rules.Count, 1, WorkInboxRulesV1.MaxRulesPerItem);
            Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule(module, rule), rule.RuleCode));
            Assert.InRange(item.References.Count, 0, WorkInboxRulesV1.MaxReferencesPerItem);
            Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference), reference.Role));
        }
    }
}

/// <summary>The one host setting the module registration reads (no configuration package in this test project).</summary>
internal sealed class LogisticsWorkInboxTestSettings : IConfiguration
{
    public string? this[string key]
    {
        get => key == "DB_CONNECTION_STRING" ? "Server=unused.invalid;Database=unused;" : null;
        set => throw new NotSupportedException();
    }

    public IEnumerable<IConfigurationSection> GetChildren() => [];

    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
}
