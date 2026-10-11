using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Pending_stock_count_appears_once_and_approved_or_foreign_counts_do_not_leak_or_create_read_effects()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-D05-COUNT-A", 100m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-D05-COUNT-B", 100m);
        var countId = Guid.NewGuid();
        var foreignCountId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(
                new(countId, own.LabelId, own.LabelVersion.ToString(), "97", At.AddHours(2), "D05 sayım farkı"),
                Metadata(run), static _ => Task.CompletedTask);
        }
        await using (var context = run.Context(CompanyB))
        {
            context.StockCounts.Add(new StockCountRow
            {
                Id = foreignCountId,
                CompanyId = CompanyB,
                LabelId = foreign.LabelId,
                LabelVersion = foreign.LabelVersion,
                OwnerKind = (int)StockPartyKind.Company,
                OwnerId = CompanyB,
                CustodianKind = (int)StockPartyKind.Company,
                CustodianId = CompanyB,
                LocationId = await run.LocationOfAsync(CompanyB),
                ProductId = foreign.ProductId,
                Block = (int)BlockState.Free,
                BookKg = 100m,
                CountedKg = 96m,
                CountedAtUtc = At.AddHours(2),
                Reason = "D05 yabancı şirket sayımı",
                PayloadHash = new string('f', 64),
                RecordedAtUtc = At.AddHours(2),
                RecordedBy = run.Actor.ToString("D"),
                CorrelationId = $"d05-foreign-{Guid.NewGuid():N}",
            });
            await context.SaveChangesAsync();
        }

        await using var readScope = run.Store(CompanyA);
        var approver = Guid.NewGuid().ToString("D");
        var company = new PendingCompany(CompanyA);
        var user = new PendingUser(approver, CompanyA);
        var authority = new PendingAuthority();
        var service = (IInventoryStockCountPendingApprovalReadServiceV1)new InventoryPendingApprovalReadServiceV1(
            readScope.Store, company, user, authority);
        var before = await PendingReadEffects(run);

        var page = await service.ListAsync(new(CompanyA, approver), null, default);
        var detail = await service.GetAsync(new(CompanyA, approver), countId, default);

        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Found, page.Outcome);
        var item = Assert.Single(page.Resource!.Items);
        Assert.Equal((countId, own.LabelId, "SYN-D05-COUNT-A", "-3", run.Actor.ToString("D")),
            (item.CountId, item.LabelId, item.LabelCode, item.DifferenceKg, item.SubmittedBy));
        Assert.Equal((countId, "SYN-D05-COUNT-A"),
            (detail.Resource!.Item.CountId, detail.Resource.Item.LabelCode));
        Assert.DoesNotContain(page.Resource.Items, candidate => candidate.CountId == foreignCountId);
        Assert.Equal(before, await PendingReadEffects(run));

        await using (var decisionScope = run.Store(CompanyA))
        {
            await decisionScope.Store.DecideStockCountAsync(countId,
                new(InventoryStockCountDecisionKindV1.Approve, "D05 onay"),
                Metadata(run) with { ActorId = approver }, static _ => Task.CompletedTask);
        }
        var afterDecision = await PendingReadEffects(run);
        var closed = await service.ListAsync(new(CompanyA, approver), null, default);
        Assert.Empty(closed.Resource!.Items);
        Assert.Equal(afterDecision, await PendingReadEffects(run));
    }

    [SqlServerFact]
    public async Task Pending_stock_scrap_appears_once_but_own_and_rejected_rows_disappear_without_read_effects()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-D05-SCRAP", 100m);
        var recorder = Metadata(run);
        var ownActor = Guid.NewGuid().ToString("D");
        var visibleId = Guid.NewGuid();
        var ownId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(
                new(visibleId, source.LabelId, source.LabelVersion.ToString(), "6", At.AddHours(2), "D05 hurda"),
                recorder, static _ => Task.CompletedTask);
            await scope.Store.RecordStockScrapAsync(
                new(ownId, source.LabelId, source.LabelVersion.ToString(), "5", At.AddHours(3), "D05 kendi hurda"),
                recorder with
                {
                    ActorId = ownActor,
                    IdempotencyKey = $"d05-own-scrap-{Guid.NewGuid():N}",
                    CorrelationId = $"d05-own-scrap-{Guid.NewGuid():N}",
                }, static _ => Task.CompletedTask);
        }

        await using var readScope = run.Store(CompanyA);
        var company = new PendingCompany(CompanyA);
        var user = new PendingUser(ownActor, CompanyA);
        var service = (IInventoryStockScrapPendingApprovalReadServiceV1)new InventoryPendingApprovalReadServiceV1(
            readScope.Store, company, user, new PendingAuthority());
        var before = await PendingReadEffects(run);

        var pending = await service.ListAsync(new(CompanyA, ownActor), null, default);

        var item = Assert.Single(pending.Resource!.Items);
        Assert.Equal((visibleId, "SYN-D05-SCRAP", "6", recorder.ActorId),
            (item.ScrapId, item.LabelCode, item.ScrapKg, item.SubmittedBy));
        Assert.DoesNotContain(pending.Resource.Items, candidate => candidate.ScrapId == ownId);
        Assert.Equal(before, await PendingReadEffects(run));

        await using (var decisionScope = run.Store(CompanyA))
        {
            await decisionScope.Store.DecideStockScrapAsync(visibleId,
                new(InventoryStockScrapDecisionKindV1.Reject, "D05 red"),
                Metadata(run) with { ActorId = ownActor }, static _ => Task.CompletedTask);
        }
        var afterDecision = await PendingReadEffects(run);
        Assert.Empty((await service.ListAsync(new(CompanyA, ownActor), null, default)).Resource!.Items);
        Assert.Equal(afterDecision, await PendingReadEffects(run));
    }

    [SqlServerFact]
    public async Task Real_pending_read_fails_closed_for_malformed_result_and_post_await_context_drift()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-D05-FAIL", 100m);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(
                new(Guid.NewGuid(), source.LabelId, source.LabelVersion.ToString(), "98", At.AddHours(2),
                    "D05 fail closed"), Metadata(run), static _ => Task.CompletedTask);
        }

        await using var readScope = run.Store(CompanyA);
        var actor = Guid.NewGuid().ToString("D");
        var malformedCompany = new PendingCompany(CompanyA);
        var malformedReader = new PendingReaderDecorator(readScope.Store)
        {
            MutateCounts = rows => [.. rows.Select(item => item with { CompanyId = CompanyB })],
        };
        var malformed = (IInventoryStockCountPendingApprovalReadServiceV1)new InventoryPendingApprovalReadServiceV1(
            malformedReader, malformedCompany, new PendingUser(actor, CompanyA), new PendingAuthority());
        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Unavailable,
            (await malformed.ListAsync(new(CompanyA, actor), null, default)).Outcome);

        var driftCompany = new PendingCompany(CompanyA);
        var driftReader = new PendingReaderDecorator(readScope.Store)
        {
            AfterCountRead = () => driftCompany.Set(CompanyB),
        };
        var drifting = (IInventoryStockCountPendingApprovalReadServiceV1)new InventoryPendingApprovalReadServiceV1(
            driftReader, driftCompany, new PendingUser(actor, CompanyA, CompanyB), new PendingAuthority());
        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Denied,
            (await drifting.ListAsync(new(CompanyA, actor), null, default)).Outcome);
    }

    private static async Task<(int Movements, int CountDecisions, int ScrapDecisions, int Commands, int Audit)>
        PendingReadEffects(Run run) => (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCountDecisions"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScrapDecisions"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

    private sealed class PendingCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class PendingUser(string actorId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class PendingAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(new PermissionScope(
            true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class PendingReaderDecorator(IInventoryPendingApprovalReaderV1 inner)
        : IInventoryPendingApprovalReaderV1
    {
        public Func<IReadOnlyList<InventoryStockCountPendingApprovalItemV1>,
            IReadOnlyList<InventoryStockCountPendingApprovalItemV1>>? MutateCounts { get; init; }
        public Action? AfterCountRead { get; init; }

        public async Task<IReadOnlyList<InventoryStockCountPendingApprovalItemV1>> ReadStockCountsAsync(
            string excludingActorId,
            DateTimeOffset? afterSubmittedAtUtc,
            Guid? afterCountId,
            int limit,
            CancellationToken cancellationToken)
        {
            var rows = await inner.ReadStockCountsAsync(
                excludingActorId, afterSubmittedAtUtc, afterCountId, limit, cancellationToken);
            AfterCountRead?.Invoke();
            return MutateCounts?.Invoke(rows) ?? rows;
        }

        public Task<InventoryStockCountPendingApprovalItemV1?> ReadStockCountAsync(
            Guid countId, string excludingActorId, CancellationToken cancellationToken) =>
            inner.ReadStockCountAsync(countId, excludingActorId, cancellationToken);

        public Task<IReadOnlyList<InventoryStockScrapPendingApprovalItemV1>> ReadStockScrapsAsync(
            string excludingActorId,
            DateTimeOffset? afterSubmittedAtUtc,
            Guid? afterScrapId,
            int limit,
            CancellationToken cancellationToken) =>
            inner.ReadStockScrapsAsync(excludingActorId, afterSubmittedAtUtc, afterScrapId, limit, cancellationToken);

        public Task<InventoryStockScrapPendingApprovalItemV1?> ReadStockScrapAsync(
            Guid scrapId, string excludingActorId, CancellationToken cancellationToken) =>
            inner.ReadStockScrapAsync(scrapId, excludingActorId, cancellationToken);
    }
}
