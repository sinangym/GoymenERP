using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;

namespace GoymenERP.Accounting.UnitTests;

public sealed class ManufacturingCostDifferenceV1Tests
{
    private static readonly Guid Company = Guid.Parse("c0620000-0000-4000-8000-000000000001");
    private static readonly Guid Maker = Guid.Parse("c0620000-0000-4000-8000-000000000002");
    private static readonly Guid Checker = Guid.Parse("c0620000-0000-4000-8000-000000000003");
    private static readonly Guid EventId = Guid.Parse("c0620000-0000-4000-8000-000000000004");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Actual_source_identity_and_signed_TL_survive_without_using_USD_or_rewriting_issues()
    {
        var source = Source();
        var accepted = ManufacturingCostDifferenceInboxConsumerV1.Read(Envelope(source), Context());
        Assert.Equal(600m, Assert.Single(accepted.Outputs).ConsumedTlDifference);
        Assert.Equal(2000m, Assert.Single(accepted.Outputs).OriginalTlAmount);
        Assert.Equal(4m, Assert.Single(accepted.Outputs).UsdUnitCostPerKg);
        var reversal = source with { State = 4, GenerationVersion = "3",
            Outputs = [source.Outputs[0] with { ConsumedTlDifference = -600m }] };
        Assert.Equal(-600m, Assert.Single(ManufacturingCostDifferenceInboxConsumerV1.Read(Envelope(reversal), Context()).Outputs).ConsumedTlDifference);
    }

    [Fact]
    public void Inventory_rounded_consumed_share_is_carried_exactly_without_rederiving_it_from_unit_cost()
    {
        var source = Source();
        source = source with { Outputs = [source.Outputs[0] with
        {
            NetQuantityKg = 3m, RemainingQuantityKg = 2m, ConsumedQuantityKg = 1m,
            TlUnitCostPerKg = 33.336666666666666666666666667m,
            OriginalTlAmount = 100.01m, RemainingTlValue = 66.67m, ConsumedTlDifference = 33.34m,
        }] };
        var accepted = ManufacturingCostDifferenceInboxConsumerV1.Read(Envelope(source), Context());
        var output = Assert.Single(accepted.Outputs);
        Assert.Equal(33.34m, output.ConsumedTlDifference);
        Assert.Equal(100.01m, output.RemainingTlValue + output.ConsumedTlDifference);
    }

    [Fact]
    public void Exact_nonterminating_quantity_ratios_preserve_owner_mass_without_decimal_approximation()
    {
        var source = Source();
        source = source with { Outputs = [source.Outputs[0] with
        {
            NetQuantityKg = 1m, RemainingQuantityKg = null, ConsumedQuantityKg = null,
            RemainingQuantityKgRatio = new("2", "3"), ConsumedQuantityKgRatio = new("1", "3"),
            Positions = [new(Guid.NewGuid(), null, null, true, new("2", "3"), new("10", "3"))],
        }], ConsumedLineageReference = new("inventory.manufacturing-consumed-cost-lineage.v1",
            source.GenerationId, source.GenerationVersion, new('a', 64)) };
        var accepted = ManufacturingCostDifferenceInboxConsumerV1.Read(Envelope(source), Context());
        Assert.Equal(source, accepted with { Outputs = source.Outputs });
        var output = Assert.Single(accepted.Outputs);
        Assert.Null(output.ConsumedQuantityKg);
        Assert.Equal(new AccountingInventoryCostRatioV1("1", "3"), output.ConsumedQuantityKgRatio);
        Assert.Equal("1 / 3", ManufacturingCostDifferenceQuantityV1.Display(output.ConsumedQuantityKg, output.ConsumedQuantityKgRatio));
        Assert.Equal(600m, output.ConsumedTlDifference);
        Assert.Equal(source.ConsumedLineageReference, accepted.ConsumedLineageReference);
    }

    [Fact]
    public void Missing_noncanonical_conflicting_ratios_and_forged_lineage_references_are_refused()
    {
        var source = Source();
        var reference = new ManufacturingConsumedLineageReferenceV1("inventory.manufacturing-consumed-cost-lineage.v1",
            source.GenerationId, source.GenerationVersion, new('a', 64));
        foreach (var output in new[]
        {
            source.Outputs[0] with { ConsumedQuantityKg = null },
            source.Outputs[0] with { ConsumedQuantityKgRatio = new("300", "10") },
            source.Outputs[0] with { ConsumedQuantityKgRatio = new("31", "1") },
            source.Outputs[0] with { ConsumedQuantityKg = null, ConsumedQuantityKgRatio = new("-30", "1") },
        }) Assert.Throws<OutboxPermanentFailureException>(() => ManufacturingCostDifferenceInboxConsumerV1.Read(
            Envelope(source with { Outputs = [output] }), Context()));
        foreach (var invalid in new[]
        {
            reference with { SourceVersion = "unsupported" }, reference with { GenerationId = Guid.NewGuid() },
            reference with { GenerationVersion = "3" }, reference with { HistorySha256 = new('A', 64) },
            reference with { HistorySha256 = "" },
        }) Assert.Throws<OutboxPermanentFailureException>(() => ManufacturingCostDifferenceInboxConsumerV1.Read(
            Envelope(source with { ConsumedLineageReference = invalid }), Context()));
        Assert.DoesNotContain("consumedLineageReference", Envelope(source).PayloadJson);
        Assert.Equal(source.GenerationId, ManufacturingCostDifferenceInboxConsumerV1.Read(Envelope(source), Context()).GenerationId);
    }

    [Fact]
    public void Missing_cost_mass_mismatch_cross_company_and_forged_event_are_not_zero_or_success()
    {
        var source = Source();
        foreach (var invalid in new[]
        {
            source with { CompanyId = Guid.NewGuid() }, source with { CostDifferenceEventId = Guid.NewGuid() },
            source with { GenerationVersion = "03" }, source with { State = 1 },
            source with { Outputs = [source.Outputs[0] with { ConsumedTlDifference = null }] },
            source with { Outputs = [source.Outputs[0] with { OriginalTlAmount = null }] },
            source with { Outputs = [source.Outputs[0] with { ConsumedQuantityKg = 31m }] },
        }) Assert.Throws<OutboxPermanentFailureException>(() => ManufacturingCostDifferenceInboxConsumerV1.Read(Envelope(invalid), Context()));
        Assert.Throws<OutboxPermanentFailureException>(() => ManufacturingCostDifferenceInboxConsumerV1.Read(
            Envelope(source) with { SourceName = "manufacturing" }, Context()));
    }

    [Fact]
    public async Task Inbox_only_admits_the_suggestion_and_never_approves_a_journal()
    {
        var store = new Store();
        var consumer = new ManufacturingCostDifferenceInboxConsumerV1(store, Context());
        await consumer.HandleAsync(Envelope(Source()), CancellationToken.None);
        Assert.Equal(1, store.Accepted);
        Assert.Equal(0, store.Writes);
        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => consumer.HandleAsync(
            Envelope(Source() with { CompanyId = Guid.NewGuid() }), CancellationToken.None));
        Assert.Equal(1, store.Accepted);
    }

    [Fact]
    public async Task Commands_replay_only_for_the_same_actor_and_payload_and_revalidate_authority_first()
    {
        var store = new Store();
        var authority = new Authority();
        var service = Service(store, Maker, authority);
        var first = await service.PrepareAsync(Preparation(), "prepare");
        var replay = await service.PrepareAsync(Preparation(), "prepare");
        Assert.Equal(AccountingApiStatus.Ok, first.Status);
        Assert.True(replay.IsReplay);
        Assert.Equal(1, store.Writes);
        var conflict = await Service(store, Checker, authority).PrepareAsync(Preparation(), "prepare");
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, conflict.Code);
        authority.Granted = false;
        Assert.Equal(AccountingApiStatus.Denied, (await service.PrepareAsync(Preparation(), "prepare")).Status);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task Final_fence_and_company_change_discard_access_without_releasing_the_other_companys_source()
    {
        var store = new Store();
        var authority = new Authority { RevokeAtCall = 4 };
        Assert.Equal(AccountingApiStatus.Denied, (await Service(store, Maker, authority).PrepareAsync(Preparation(), "revoked")).Status);
        Assert.Equal(0, store.Writes);
        var context = new MutableCompanyContextForFenceTests(Company);
        store.DuringList = () => context.Set(Guid.NewGuid());
        var service = new ManufacturingCostDifferenceServiceV1(store, store, new Authority(), context, new User(Maker), new Correlation());
        Assert.Equal(AccountingApiStatus.Denied, (await service.ListAsync()).Status);
    }

    [Fact]
    public async Task Invalid_versions_do_not_reach_store_and_source_gaps_are_not_posted_successes()
    {
        var store = new Store();
        var service = Service(store, Maker, new Authority());
        Assert.Equal(AccountingApiStatus.Invalid, (await service.PrepareAsync(Preparation() with { ExpectedVersion = 0 }, "bad")).Status);
        Assert.Equal(AccountingApiStatus.Invalid, (await service.ApproveAsync(EventId, -1, "bad2")).Status);
        store.Failure = "cost_difference_account_plan_missing";
        Assert.Equal(AccountingApiStatus.Unprocessable, (await service.PrepareAsync(Preparation(), "no-plan")).Status);
        store.Failure = "cost_difference_tl_currency_unavailable";
        Assert.Equal(AccountingApiStatus.Unavailable, (await service.PrepareAsync(Preparation(), "no-currency")).Status);
        Assert.Equal(0, store.Writes);
    }

    private static ManufacturingCostDifferenceServiceV1 Service(Store store, Guid actor, Authority authority) =>
        new(store, store, authority, Context(), new User(actor), new Correlation());
    private static CompanyContext Context() { var context = new CompanyContext(); context.Set(Company); return context; }
    private static ManufacturingCostDifferenceEventV1 Source() => new(Company, Guid.NewGuid(), Guid.NewGuid(), "2", "4", 3,
        "synthetic-fingerprint", [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, 70m, 30m,
            20m, 1400m, 600m, 4m, [], 2000m)], EventId, false, "synthetic-cost");
    private static WorkInboxEventV1 Envelope(ManufacturingCostDifferenceEventV1 source) => new(
        ManufacturingCostDifferenceInboxV1.SourceName, EventId, Company, ManufacturingCostDifferenceInboxV1.EventType, 1,
        JsonSerializer.Serialize(source, Json), DateTimeOffset.Parse("2026-11-17T09:00:00Z"), "synthetic-cost", null);
    private static PrepareManufacturingCostDifferenceV1 Preparation() => new(EventId, 1,
        Guid.Parse("c0620000-0000-4000-8000-000000000005"), new(2026, 11, 17), new(2026, 11, 16),
        Guid.Parse("c0620000-0000-4000-8000-000000000006"), Guid.Parse("c0620000-0000-4000-8000-000000000007"), "SENTETIK gerekçe");
    private static ManufacturingCostDifferenceViewV1 View() => new(EventId, Company, Guid.NewGuid(), Guid.NewGuid(), "2",
        "Prepared", 2, "600", [], DateTimeOffset.Parse("2026-11-17T09:00:00Z"), Preparation().PeriodId,
        Preparation().AccountingDate, Preparation().TransactionDate, Preparation().DebitAccountId, Preparation().CreditAccountId,
        Maker, null, null, Preparation().Reason);

    private sealed class Store : IManufacturingCostDifferenceStoreV1, IAccountingCommandLog
    {
        private readonly List<AccountingCommandRecord> commands = [];
        public int Accepted { get; private set; }
        public int Writes { get; private set; }
        public string? Failure { get; set; }
        public Action? DuringList { get; set; }
        public Task AcceptAsync(WorkInboxEventV1 envelope, ManufacturingCostDifferenceEventV1 source, CancellationToken cancellationToken = default)
        { Accepted++; return Task.CompletedTask; }
        public Task<IReadOnlyList<ManufacturingCostDifferenceViewV1>> ListAsync(CancellationToken cancellationToken = default)
        { DuringList?.Invoke(); return Task.FromResult<IReadOnlyList<ManufacturingCostDifferenceViewV1>>([View()]); }
        public Task<ManufacturingCostDifferenceViewV1?> FindAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ManufacturingCostDifferenceViewV1?>(View());
        public Task<ManufacturingCostDifferenceViewV1> PrepareAsync(PrepareManufacturingCostDifferenceV1 request, Guid actor,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) => Write(fence, metadata, cancellationToken);
        public Task<ManufacturingCostDifferenceViewV1> ApproveAsync(Guid id, long version, Guid actor,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) => Write(fence, metadata, cancellationToken);
        private async Task<ManufacturingCostDifferenceViewV1> Write(Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token)
        {
            await fence(token);
            if (Failure is { } failure) throw new AccountingStoreException(failure, "Synthetic refusal");
            Writes++;
            var value = View();
            if (metadata.Completion?.Invoke(value) is { } command) commands.Add(command);
            return value;
        }
        public Task<AccountingCommandRecord?> FindCommandAsync(string commandName, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(commands.SingleOrDefault(x => x.CommandName == commandName && x.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default)
        { commands.Add(record); return Task.CompletedTask; }
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public int RevokeAtCall { get; init; } = int.MaxValue;
        private int calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Assert.Contains(permission, new[] { AccountingJournalReversalPermissionNamesV1.Propose, AccountingJournalReversalPermissionNamesV1.Approve });
            return Task.FromResult(Granted && ++calls < RevokeAtCall
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }
    private sealed class User(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid value) => value == Company;
    }
    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "synthetic-cost";
        public string? CausationId => "synthetic-source";
        public string SourceChannel => "UnitTest";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string a, string? b, string c, string? d, string? e) => throw new NotSupportedException();
    }
}
