using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// WP6 test doubles shared by the Inventory Work inbox source tests: in-memory rows (the sources' queries run over LINQ to
/// objects, so the explicit company and state filters are what is proven — no global query filter helps), a rows double that
/// only translates the queries for SQL Server offline (no connection is opened), and the fence's user and authority. SENTETIK.
/// </summary>
internal sealed class InventoryInMemoryWorkInboxRows : InventoryWorkInboxRows
{
    public List<InventoryTransferRow> TransferRows { get; } = [];
    public List<InventoryTransferLineRow> LineRows { get; } = [];
    public List<InventoryTransferDispatchLineRow> DispatchLineRows { get; } = [];
    public List<InventoryTransferReceiptRow> ReceiptRows { get; } = [];
    public List<InventoryTransferOverageDecisionRow> OverageDecisionRows { get; } = [];
    public List<InventoryTransferOverageRenewalRow> OverageRenewalRows { get; } = [];
    public List<InventoryTransferLossRequestRow> LossRequestRows { get; } = [];
    public List<InventoryTransferLossDecisionRow> LossDecisionRows { get; } = [];
    public List<InventoryTransferLossWithdrawalRow> LossWithdrawalRows { get; } = [];
    public List<LabelRow> LabelRows { get; } = [];
    public bool FailReads { get; set; }
    public int Reads { get; private set; }

    public override IQueryable<InventoryTransferRow> Transfers => TransferRows.AsQueryable();
    public override IQueryable<InventoryTransferLineRow> TransferLines => LineRows.AsQueryable();
    public override IQueryable<InventoryTransferDispatchLineRow> TransferDispatchLines => DispatchLineRows.AsQueryable();
    public override IQueryable<InventoryTransferReceiptRow> TransferReceipts => ReceiptRows.AsQueryable();
    public override IQueryable<InventoryTransferOverageDecisionRow> TransferOverageDecisions => OverageDecisionRows.AsQueryable();
    public override IQueryable<InventoryTransferOverageRenewalRow> TransferOverageRenewals => OverageRenewalRows.AsQueryable();
    public override IQueryable<InventoryTransferLossRequestRow> TransferLossRequests => LossRequestRows.AsQueryable();
    public override IQueryable<InventoryTransferLossDecisionRow> TransferLossDecisions => LossDecisionRows.AsQueryable();
    public override IQueryable<InventoryTransferLossWithdrawalRow> TransferLossWithdrawals => LossWithdrawalRows.AsQueryable();
    public override IQueryable<LabelRow> Labels => LabelRows.AsQueryable();

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Reads++;
        return FailReads ? throw new InvalidOperationException("SENTETIK read failure") : Task.FromResult(query.ToList());
    }
}

/// <summary>Translates each query with the module's own SQL Server model; returns nothing and never connects.</summary>
internal sealed class InventorySqlTranslatingWorkInboxRows : InventoryWorkInboxRows, IDisposable
{
    private readonly InventoryDbContext dbContext;
    private readonly InventoryWorkInboxDbRows inner;

    public InventorySqlTranslatingWorkInboxRows(Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        dbContext = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        inner = new InventoryWorkInboxDbRows(dbContext);
    }

    public List<string> Sql { get; } = [];

    public override IQueryable<InventoryTransferRow> Transfers => inner.Transfers;
    public override IQueryable<InventoryTransferLineRow> TransferLines => inner.TransferLines;
    public override IQueryable<InventoryTransferDispatchLineRow> TransferDispatchLines => inner.TransferDispatchLines;
    public override IQueryable<InventoryTransferReceiptRow> TransferReceipts => inner.TransferReceipts;
    public override IQueryable<InventoryTransferOverageDecisionRow> TransferOverageDecisions => inner.TransferOverageDecisions;
    public override IQueryable<InventoryTransferOverageRenewalRow> TransferOverageRenewals => inner.TransferOverageRenewals;
    public override IQueryable<InventoryTransferLossRequestRow> TransferLossRequests => inner.TransferLossRequests;
    public override IQueryable<InventoryTransferLossDecisionRow> TransferLossDecisions => inner.TransferLossDecisions;
    public override IQueryable<InventoryTransferLossWithdrawalRow> TransferLossWithdrawals => inner.TransferLossWithdrawals;
    public override IQueryable<LabelRow> Labels => inner.Labels;

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Sql.Add(query.ToQueryString());
        return Task.FromResult(new List<T>());
    }

    public void Dispose() => dbContext.Dispose();
}

internal sealed class InventoryWorkInboxTestUser(string userId, params Guid[] companies) : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;
    public string? UserId { get; set; } = userId;
    public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(companies);
    public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
}

internal sealed class InventoryWorkInboxTestAuthority : IErpAuthorityRevalidationService
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

/// <summary>The fixture every Inventory source test starts from: company A resolved, the approver signed in and granted.</summary>
internal sealed class InventoryWorkInboxTestFixture
{
    public static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid SourceLocation = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public static readonly Guid DestinationLocation = Guid.Parse("44444444-4444-4444-8444-444444444444");
    public const string Approver = "SENTETIK-approver";
    public const string Receiver = "SENTETIK-receiver";
    public const string Requester = "SENTETIK-requester";
    public static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.FromHours(3));

    public InventoryWorkInboxTestFixture()
    {
        Company.Set(CompanyA);
        User = new InventoryWorkInboxTestUser(Approver, CompanyA);
        Fence = new InventoryWorkInboxFence(Company, User, Authority);
    }

    public CompanyContext Company { get; } = new();
    public InventoryWorkInboxTestUser User { get; }
    public InventoryWorkInboxTestAuthority Authority { get; } = new();
    public InventoryWorkInboxFence Fence { get; }
    public InventoryInMemoryWorkInboxRows Rows { get; } = new();

    public WorkInboxSourceContextV1 Context(string? userId = null, Guid? companyId = null) =>
        new(companyId ?? CompanyA, userId ?? Approver, "SENTETIK-correlation", null);

    public static Guid Id(int n) => Guid.Parse($"{n:D8}-0000-4000-8000-000000000000");

    /// <summary>A transfer with one line on a label coded <c>ETK-{labelNumber}</c>.</summary>
    public void TransferLine(Guid transferId, Guid lineId, Guid labelId, int labelNumber, Guid? companyId = null)
    {
        var company = companyId ?? CompanyA;
        if (!Rows.TransferRows.Any(row => row.Id == transferId))
        {
            Rows.TransferRows.Add(new InventoryTransferRow
            {
                Id = transferId, CompanyId = company, Version = 3, Status = 3, SourceLocationId = SourceLocation,
                DestinationLocationId = DestinationLocation, PlannedAtUtc = T0, PayloadHash = "SENTETIK", RecordedAtUtc = T0,
                UpdatedAtUtc = T0, RecordedBy = "SENTETIK-planner", CorrelationId = "SENTETIK",
            });
        }

        Rows.LineRows.Add(new InventoryTransferLineRow
        {
            CompanyId = company, TransferId = transferId, LineId = lineId, Sequence = Rows.LineRows.Count + 1, LabelId = labelId,
            ExpectedLabelVersion = 1, PlannedKg = 100m,
        });
        Rows.LabelRows.Add(new LabelRow
        {
            Id = labelId, CompanyId = company, Code = $"ETK-{labelNumber}", ProductId = Id(9001), LotId = Id(9002), Version = 2,
            CreatedAtUtc = T0, UpdatedAtUtc = T0,
        });
    }

    /// <summary>Every item satisfies the contract's shape rules (the host enforces them too).</summary>
    public static void AssertContractShape(WorkInboxSourceResultV1 result, string kind)
    {
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.NotNull(result.Items);
        foreach (var item in result.Items!)
        {
            Assert.Equal(("inventory", kind), (item.Module, item.Kind));
            Assert.True(NativeApprovalQueueRulesV1.IsValidKind("inventory", kind));
            Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link), item.Link);
            Assert.NotEqual(Guid.Empty, item.ItemId);
            Assert.InRange(item.DocumentLabel.Length, 1, WorkInboxRulesV1.MaxDocumentLabelLength);
            Assert.Equal(TimeSpan.Zero, item.WaitingSinceUtc.Offset);
            Assert.False(string.IsNullOrWhiteSpace(item.PreparedBy));
            Assert.InRange(item.Rules.Count, 1, WorkInboxRulesV1.MaxRulesPerItem);
            Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("inventory", rule), rule.RuleCode));
            Assert.InRange(item.References.Count, 0, WorkInboxRulesV1.MaxReferencesPerItem);
            Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference), reference.Role));
            Assert.Equal(new WorkInboxItemScopeV1(null, DestinationLocation, null), item.Scope);
        }
    }
}
