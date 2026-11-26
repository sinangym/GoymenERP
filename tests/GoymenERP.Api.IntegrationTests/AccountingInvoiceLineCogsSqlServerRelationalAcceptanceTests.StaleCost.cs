using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stale_sales_preparation_closure_audit_conflict_receipt_and_owner_outbox_commit_once_and_replay_together()
        => await StaleClosure(false);

    [SqlServerFact]
    public async Task Stale_purchase_preparation_closure_audit_conflict_receipt_and_owner_outbox_commit_once_and_replay_together()
        => await StaleClosure(true);

    private static async Task StaleClosure(bool purchase)
    {
        // Existing actual Accounting/audit SQL harness. Cost source, account plan, money profile and authority are synthetic owners.
        await using var run = await Run.Start(); var period = await run.Period(); var profiles = new MutableCostProfiles();
        var reader = new RoundedCostReader(run.Source.Facts, new("1", "3"), purchase);
        var downGuard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
            new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddStaleCostProposalClosure().DownOperations[0]).Sql;
        await run.Execute(downGuard);
        await using var scope = run.Open(); var store = Costs(scope, reader, profiles);
        await store.AcceptAsync(reader.Envelope);
        var kind = purchase ? AccountingInventoryCostKindV1.PurchaseInvoiceDifference : AccountingInventoryCostKindV1.SalesInvoicedShipment;
        var source = Assert.Single(await store.ListAsync(kind));
        var prepared = await store.PrepareAsync(source.ProposalId, kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var history = await scope.Db.InventoryCostProposals.AsNoTracking().OrderBy(x => x.Version).Select(x => x.ViewJson).ToArrayAsync();
        var auditBefore = await run.Count("audit.AuditEvents"); profiles.Version = 2;
        var outbox = new AccountingCostProposalStaleOutboxSourceV1(scope.Db, scope.CompanyContext);
        Task RevokeAfterClosure(CancellationToken _) => scope.Db.ChangeTracker.Entries<AccountingInventoryCostProposalRow>().Any(x => x.Entity.Status == 7)
            ? throw new InvalidOperationException("SENTETIK checker authority lost after stale append") : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(source.ProposalId, kind, 2, Checker, RevokeAfterClosure, Metadata(Checker)));
        Assert.Equal(2, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(auditBefore, await run.Count("audit.AuditEvents")); Assert.Empty(await outbox.ReadAfterAsync(null, 10, default));
        Assert.Equal(0, await run.Count("accounting.Commands"));
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-stale-approval", "SENTETIK-source", SourceChannels.Web, null, null);
        var service = new AccountingInventoryCostServiceV1(store, scope.Posting, new StaleAuthority(), scope.CompanyContext, new StaleUser(Checker), correlation);
        var result = await service.ApproveAsync(source.ProposalId, kind, 2, "SENTETIK-stale-key");
        Assert.Equal(AccountingApiStatus.Conflict, result.Status); Assert.Equal(AccountingCostProposalStaleEventV1.ReasonCode, result.Code);
        var closed = Assert.IsType<AccountingInventoryCostProposalV1>(result.Resource);
        Assert.Equal("Stale", closed.Status); Assert.Equal(3, closed.Version); Assert.Equal(prepared.Preparation, closed.Preparation);
        Assert.Equal(Maker, closed.StaleClosure!.PreparedBy); Assert.Equal(2, closed.StaleClosure.PreparedVersion);
        var replay = await service.ApproveAsync(source.ProposalId, kind, 2, "SENTETIK-stale-key");
        Assert.True(replay.IsReplay); Assert.Equal(AccountingApiStatus.Conflict, replay.Status);
        Assert.Equal(closed.StaleClosure, replay.Resource!.StaleClosure);
        Assert.Equal(1, await run.Count("accounting.Commands")); Assert.Equal(auditBefore + 1, await run.Count("audit.AuditEvents"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        var rows = await scope.Db.InventoryCostProposals.AsNoTracking().OrderBy(x => x.Version).ToArrayAsync();
        Assert.Equal(history, rows.Take(2).Select(x => x.ViewJson)); Assert.Equal(3, rows.Length);
        var message = Assert.Single(await outbox.ReadAfterAsync(null, 10, default));
        Assert.Equal(closed.StaleClosure.EventId, message.EventId);
        Assert.Equal(kind, System.Text.Json.JsonSerializer.Deserialize<AccountingCostProposalStalePayloadV1>(message.PayloadJson, Json)!.CostKind);
        Assert.Equal(52140, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => run.Execute(downGuard))).Number);
        Assert.DoesNotContain("amount", message.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await outbox.ReadAfterAsync(message.Position, 10, default));
        Assert.Equal(message, Assert.Single(await outbox.ReadAfterAsync(null, 10, default)));
        var fresh = await service.PrepareAsync(closed.ProposalId, kind, CostPreparation(period) with { ExpectedVersion = 3 }, "SENTETIK-fresh-key");
        Assert.Equal(AccountingApiStatus.Ok, fresh.Status); Assert.NotEqual(closed.ProposalId, fresh.Resource!.ProposalId);
        Assert.Null(fresh.Resource.StaleClosure); Assert.Equal(2, fresh.Resource.Preparation!.MoneyProfileVersion);
        Assert.Equal(closed.ProposalId, fresh.Resource.RenewedFrom!.PreviousProposalId);
        var freshReplay = await service.PrepareAsync(closed.ProposalId, kind, CostPreparation(period) with { ExpectedVersion = 3 }, "SENTETIK-fresh-key");
        Assert.True(freshReplay.IsReplay); Assert.Equal(fresh.Resource.ProposalId, freshReplay.Resource!.ProposalId);
        var maker = new AccountingInventoryCostServiceV1(store, scope.Posting, new StaleAuthority(), scope.CompanyContext, new StaleUser(Maker), correlation);
        var approved = await maker.ApproveAsync(fresh.Resource.ProposalId, kind, fresh.Resource.Version, "SENTETIK-fresh-approve");
        Assert.Equal(AccountingApiStatus.Ok, approved.Status); Assert.Equal("Posted", approved.Resource!.Status);
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(5, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(3, await run.Count("accounting.Commands"));
        Assert.Equal(message, Assert.Single(await outbox.ReadAfterAsync(null, 10, default)));
        await Assert.ThrowsAsync<AccountingVersionConflictException>(() => store.PrepareAsync(closed.ProposalId, kind,
            CostPreparation(period) with { ExpectedVersion = 3 }, Maker, Fence, Metadata(Maker)));
        scope.CompanyContext.Set(TestCompanyIds.CompanyB); Assert.Empty(await outbox.ReadAfterAsync(null, 10, default));
    }
    private sealed class StaleUser(Guid actor) : ICurrentUser
    {
        public string UserId => actor.ToString("D"); public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class StaleAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == Company && permission is NormalJournalPermissionNamesV1.DraftApprove or AccountingJournalReversalPermissionNamesV1.Approve
                    or NormalJournalPermissionNamesV1.DraftCreate or AccountingJournalReversalPermissionNamesV1.Propose
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
}
