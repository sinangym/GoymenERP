using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Host protocol tests with owner doubles. SQL callback/rollback evidence is kept in separate SqlServerFact sources.</summary>
public sealed class SalesFutureDemandReductionHostParticipantTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Order = Id(100);
    private static readonly Guid Product = Id(101);
    private static readonly Guid Unit = Id(102);
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    [Fact]
    public async Task All_sales_locks_then_the_global_purchase_union_precede_reduction_on_the_exact_caller_transaction()
    {
        var world = new World();
        world.Active[Id(1)] = [View(Id(1), Id(30), Id(201)), View(Id(1), Id(20), Id(202))];
        world.Active[Id(2)] = [View(Id(2), Id(10), Id(203)), View(Id(2), Id(20), Id(204))];
        await world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(3), Id(2), Id(1)));
        Assert.Equal(["sales:1", "sales:2", "sales:3", "enlist", "purchase:10", "purchase:20", "purchase:30",
            "reduce:1", "reduce:2", "reduce:3", "dispose"], world.Events);
        Assert.All(world.Reductions, reduction =>
        {
            Assert.Null(reduction.Request.Selections);
            Assert.Equal((Order, Product, "v1", Unit, "4.125", SalesOrderFutureDemandReductionTextsV1.AcceptedRevision),
                (reduction.Request.SalesOrderId, reduction.Request.ProductId, reduction.Request.ProductVersion,
                    reduction.Request.DemandUnitId, reduction.Request.NewOpenQuantityText, reduction.Request.ReasonText));
            Assert.Equal(("SENTETIK-actor", SalesPermissionManifestV1.OrdersApprove, "SENTETIK-correlation", "SENTETIK-causation",
                "SENTETIK-key", "Web", "127.0.0.1", "SENTETIK-agent"), (reduction.Metadata.ActorId, reduction.Metadata.PermissionCode,
                reduction.Metadata.CorrelationId, reduction.Metadata.CausationId, reduction.Metadata.IdempotencyKey,
                reduction.Metadata.SourceChannel, reduction.Metadata.ClientIpAddress, reduction.Metadata.UserAgent));
            Assert.Equal(["SENTETIK-role"], reduction.Metadata.RoleCodes);
        });
        Assert.All(world.PermissionAsks, ask => Assert.Equal((SalesPermissionManifestV1.OrdersApprove, Company), ask));
        Assert.False(world.Transaction.Ended);
        Assert.Equal(ConnectionState.Open, world.Connection.State);
    }

    [Fact]
    public async Task A_physical_only_line_still_reaches_the_owner_and_its_refusal_is_terminal()
    {
        var world = new World { OnReduce = _ => new InventoryAllocationException(409, InventoryFutureAllocationCodesV1.PhysicalReleaseUnavailable) };
        var refused = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(1))));
        Assert.Equal((409, InventoryFutureAllocationCodesV1.PhysicalReleaseUnavailable, true),
            (refused.Status, refused.Code, refused.Terminal));
        Assert.Single(world.Reductions);
    }

    [Theory]
    [InlineData(503, InventoryFutureAllocationCodesV1.ServiceUnavailable)]
    [InlineData(409, InventoryFutureAllocationCodesV1.ConcurrentChange)]
    public async Task A_later_transient_refusal_dominates_an_earlier_terminal_refusal(int status, string code)
    {
        var world = new World { OnReduce = line => line == Id(1)
            ? new InventoryAllocationException(422, InventoryFutureAllocationCodesV1.UnitConversionUnavailable)
            : new InventoryAllocationException(status, code) };
        var refused = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(2), Id(1), Id(3))));
        Assert.Equal((status, code, false), (refused.Status, refused.Code, refused.Terminal));
        Assert.Equal([Id(1), Id(2)], world.Reductions.Select(item => item.Request.SalesOrderLineId));
    }

    [Fact]
    public async Task Terminal_refusals_choose_the_first_sales_line_in_fixed_order_but_do_not_stop_source_evaluation()
    {
        var world = new World { OnReduce = line => new InventoryAllocationException(line == Id(1) ? 422 : 409,
            line == Id(1) ? InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid : InventoryFutureAllocationCodesV1.VersionConflict) };
        var refused = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(2), Id(1))));
        Assert.Equal((422, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid, true), (refused.Status, refused.Code, refused.Terminal));
        Assert.Equal(2, world.Reductions.Count);
    }

    [Theory]
    [InlineData(503, InventoryFutureAllocationCodesV1.VersionConflict, 503)]
    [InlineData(400, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid, 503)]
    [InlineData(404, InventoryFutureAllocationCodesV1.InvalidReference, 503)]
    public async Task A_code_without_its_terminal_status_never_becomes_a_recorded_business_refusal(int status, string code, int expectedStatus)
    {
        var world = new World { OnReduce = _ => new InventoryAllocationException(status, code) };
        var refused = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(1))));
        Assert.Equal(expectedStatus, refused.Status);
        Assert.False(refused.Terminal);
    }

    [Fact]
    public async Task A_raw_purchase_lock_refusal_inside_reduction_is_retryable_and_dominates_a_terminal()
    {
        var world = new World { OnReduce = line => line == Id(1)
            ? new InventoryAllocationException(409, InventoryFutureAllocationCodesV1.StateConflict)
            : new PurchaseLineBusyException(Id(20), -1) };
        var refused = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(2), Id(1))));
        Assert.Equal((409, PurchaseLineSerializationLockContractV1.BusyCode, false), (refused.Status, refused.Code, refused.Terminal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Foreign_company_or_order_views_refuse_before_any_reduction(bool foreignCompany)
    {
        var world = new World();
        var view = View(Id(1), Id(10), Id(201));
        world.Active[Id(1)] = [foreignCompany ? view with { CompanyId = TestCompanyIds.CompanyB } : view with { SalesOrderId = Id(999) }];
        var refused = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(1))));
        Assert.Equal((503, false), (refused.Status, refused.Terminal));
        Assert.Empty(world.Reductions);
    }

    [Fact]
    public async Task A_new_purchase_line_after_the_prelock_read_is_retryable_without_taking_an_out_of_order_lock()
    {
        var world = new World { GrowOnReread = true };
        world.Active[Id(1)] = [View(Id(1), Id(20), Id(201))];
        var refused = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(1))));
        Assert.Equal((503, false), (refused.Status, refused.Terminal));
        Assert.DoesNotContain("purchase:10", world.Events);
        Assert.Empty(world.Reductions);
    }

    [Fact]
    public async Task Authority_loss_while_waiting_for_a_sales_lock_stages_no_inventory_work()
    {
        var world = new World { RevokeAfterSalesLock = true };
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(1))));
        Assert.Equal(403, refused.Status);
        Assert.DoesNotContain("enlist", world.Events);
        Assert.Empty(world.Reductions);
    }

    [Fact]
    public async Task Duplicate_demand_identities_and_caller_cancellation_are_not_silently_accepted()
    {
        var world = new World();
        var refused = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(1), Id(1))));
        Assert.Equal((503, false), (refused.Status, refused.Terminal));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            world.Host().ReduceForAcceptedRevisionAsync(world.Transaction, Request(Id(1)), cancellation.Token));
        Assert.Empty(world.Events);
    }

    private static SalesOrderFutureDemandReductionRequestV1 Request(params Guid[] lineIds) => new(Order, 1,
        lineIds.Select(lineId => new SalesOrderFutureDemandReductionLineV1(lineId, Product, "v1", Unit, "4.125")).ToArray(),
        SalesOrderFutureDemandReductionTextsV1.AcceptedRevision,
        new("SENTETIK-actor", SalesPermissionManifestV1.OrdersApprove, ["SENTETIK-role"], "SENTETIK-correlation",
            "SENTETIK-causation", "SENTETIK-key", "Web", "127.0.0.1", "SENTETIK-agent"));

    private static InventoryFutureAllocationViewV1 View(Guid lineId, Guid purchaseLineId, Guid allocationId) =>
        new(allocationId, Company, "1", "Active", Order, lineId, Id(150), purchaseLineId, Product, "v1", Unit,
            "10", "10", "0", "0", null, "SENTETIK", null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private sealed class World : ICurrentUser, IErpAuthorityRevalidationService,
        ISalesOrderLineSerializationLockBinderV1, ISalesOrderLineSerializationLockV1,
        IPurchaseLineSerializationLockBinderV1, IPurchaseLineSerializationLockV1,
        IInventoryFutureAllocationReleasePortV1, IInventoryFutureAllocationReleaseSessionV1
    {
        public Connection Connection { get; } = new();
        public Transaction Transaction { get; }
        public List<string> Events { get; } = [];
        public List<(InventoryFutureDemandReductionRequestV1 Request, InventoryCommandMetadata Metadata)> Reductions { get; } = [];
        public List<(string, Guid)> PermissionAsks { get; } = [];
        public Dictionary<Guid, IReadOnlyList<InventoryFutureAllocationViewV1>> Active { get; } = [];
        public Func<Guid, Exception?> OnReduce { get; init; } = _ => null;
        public bool RevokeAfterSalesLock { get; init; }
        public bool GrowOnReread { get; init; }
        private readonly Dictionary<Guid, int> reads = [];
        private bool granted = true;
        public World() { Transaction = new(Connection); }
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
        public SalesFutureDemandReductionHostParticipant Host()
        {
            var company = new CompanyContext(); company.Set(Company);
            return new(this, this, this, company, this, this);
        }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            PermissionAsks.Add((permission, companyId));
            return Task.FromResult(new PermissionScope(granted, true, new HashSet<string> { "SENTETIK-role" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
        ISalesOrderLineSerializationLockV1 ISalesOrderLineSerializationLockBinderV1.Bind(DbConnection connection, DbTransaction transaction)
        { Bound(connection, transaction); return this; }
        IPurchaseLineSerializationLockV1 IPurchaseLineSerializationLockBinderV1.Bind(DbConnection connection, DbTransaction transaction)
        { Bound(connection, transaction); return this; }
        Task ISalesOrderLineSerializationLockV1.AcquireAsync(Guid lineId, CancellationToken cancellationToken)
        { Events.Add($"sales:{Number(lineId)}"); if (RevokeAfterSalesLock) granted = false; return Task.CompletedTask; }
        Task IPurchaseLineSerializationLockV1.AcquireAsync(Guid lineId, CancellationToken cancellationToken)
        { Events.Add($"purchase:{Number(lineId)}"); return Task.CompletedTask; }
        public Task AcquireAllAsync(IEnumerable<Guid> lineIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IInventoryFutureAllocationReleaseSessionV1> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken = default)
        { Bound(connection, transaction); Events.Add("enlist"); return Task.FromResult<IInventoryFutureAllocationReleaseSessionV1>(this); }
        public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByDemandLineAsync(Guid salesOrderLineId, CancellationToken cancellationToken = default)
        {
            reads[salesOrderLineId] = reads.GetValueOrDefault(salesOrderLineId) + 1;
            IReadOnlyList<InventoryFutureAllocationViewV1> views = Active.GetValueOrDefault(salesOrderLineId) ?? [];
            if (GrowOnReread && reads[salesOrderLineId] > 1) views = views.Append(View(salesOrderLineId, Id(10), Id(299))).ToArray();
            return Task.FromResult(views);
        }
        public Task<InventoryFutureDemandReductionResultV1> ReduceDemandAsync(InventoryFutureDemandReductionRequestV1 request,
            InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Events.Add($"reduce:{Number(request.SalesOrderLineId)}"); Reductions.Add((request, metadata));
            if (OnReduce(request.SalesOrderLineId) is { } exception) throw exception;
            return Task.FromResult(new InventoryFutureDemandReductionResultV1([]));
        }
        public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByPurchaseLineAsync(Guid purchaseOrderLineId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryFutureAllocationViewV1> ReleaseAsync(InventoryFutureAllocationReleaseRequestV1 request, InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Events.Add("dispose"); return ValueTask.CompletedTask; }
        private void Bound(DbConnection connection, DbTransaction transaction)
        { Assert.Same(Connection, connection); Assert.Same(Transaction, transaction); }
        private static int Number(Guid value) => BitConverter.ToInt32(value.ToByteArray(), 0);
    }

    private sealed class Connection : DbConnection
    {
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "SENTETIK";
        public override string DataSource => "SENTETIK";
        public override string ServerVersion => "0";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() => throw new NotSupportedException();
        public override void Close() => throw new InvalidOperationException("The caller owns the connection.");
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
    private sealed class Transaction(DbConnection connection) : DbTransaction
    {
        public bool Ended { get; private set; }
        protected override DbConnection DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override void Commit() { Ended = true; throw new InvalidOperationException("The caller owns commit."); }
        public override void Rollback() { Ended = true; throw new InvalidOperationException("The caller owns rollback."); }
    }
}
