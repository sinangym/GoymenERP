using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesCustomerReturnSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Return_current_and_original_read_callbacks_refuse_contract_and_draft_mutations_before_IO_without_discarding_caller_work()
    {
        await using var run = await Run.StartAsync();
        var invoice = await run.FinalizedInvoiceAsync("return-read-foreign-contract");
        var contract = await SeedReadForeignContract(run);
        foreach (var historical in new[] { false, true })
        {
            var id = await SeedOwnedReturn(run, invoice);
            if (historical)
            {
                await using var prepare = ReturnScope(run);
                Assert.NotNull((await DecideReturn(prepare, id, "read-foreign-prepare-" + id.ToString("N"), true, version: "2")).OriginalDecisionReceipt);
            }
            var before = await ReadForeignMutationCounts(run); var returnBefore = await ReturnEffects(run, id);
            var probe = new ForeignMutationCommandProbe(); var save = new ReturnSaveProbe();
            await using var safe = ReturnScope(run, Approver, probe, save);
            var callerRow = await safe.Db.CustomerReturns.SingleAsync(x => x.Id == id);
            safe.Db.Entry(callerRow).State = EntityState.Modified; safe.Collector.Enqueue(ReturnCallerAudit());
            var entryCommands = probe.Commands; var fired = false;
            async Task Consumer(CancellationToken ct)
            {
                // The owner has completed a real current-row or Attempt/Audit SELECT: its readonly marker is now installed.
                // Initial consumer fences before the store entry are deliberately not used as a synthetic marker substitute.
                if (fired || probe.Commands == entryCommands) return;
                fired = true;
                await AssertReadForeignMutationsBlocked(run, safe, contract.Id, probe, ct);
            }
            if (historical)
            {
                var original = await safe.Service.ReadCustomerReturnOriginalVersionAsync(id, "2", Consumer);
                Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Found, original.State);
                var receipt = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(original.Receipt);
                Assert.Equal((id, 2L, 3L, Approver), (receipt.ReturnId, receipt.ExpectedVersion, receipt.ResultVersion, receipt.ActorId));
                Assert.Equal(3L, original.CurrentVersion);
            }
            else
            {
                var current = await safe.Service.ReadCustomerReturnDecisionAsync(id, Consumer);
                Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Found, current.State);
                Assert.Equal((id, "2", "PendingApproval"), (current.Detail!.CustomerReturn.Id, current.Detail.CustomerReturn.Version, current.Detail.CustomerReturn.State));
            }
            Assert.True(fired); Assert.Equal(0, save.Saves); Assert.Null(safe.Db.Database.CurrentTransaction);
            Assert.Same(callerRow, Assert.Single(safe.Db.ChangeTracker.Entries()).Entity);
            Assert.Equal(EntityState.Modified, safe.Db.Entry(callerRow).State); Assert.True(safe.Writer.HasPendingDrafts);
            Assert.Equal(before, await ReadForeignMutationCounts(run)); Assert.Equal(returnBefore, await ReturnEffects(run, id));
            // Only the test disposes its deliberately pending caller state, after proving the read did not clear or save it.
        }
    }

    [SqlServerFact]
    public async Task Completed_return_read_releases_its_marker_and_native_contract_refusal_original_key_replay_stays_unchanged()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-read-contract-release");
        var id = await SeedOwnedReturn(run, invoice); var contract = await SeedReadForeignContract(run);
        await using var safe = ReturnScope(run);
        Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Found,
            (await safe.Service.ReadCustomerReturnDecisionAsync(id, _ => Task.CompletedTask)).State);
        var before = await ReadForeignMutationCounts(run); var returnBefore = await ReturnEffects(run, id);
        var envelope = run.Envelope("return-read-contract-refusal", Approver, SalesPermissionManifestV1.ContractsDiscountApprove);
        var refusal = new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", "SENTETIK stale")]);
        await safe.Store.RecordContractRefusalAsync(envelope, Hash('d'), SalesContractCommands.ApproveDiscountV1, contract.Id, refusal);
        var replay = await safe.Store.FindContractReplayAsync(envelope, Hash('d'), SalesContractCommands.ApproveDiscountV1);
        Assert.NotNull(replay); Assert.Null(replay.Contract); Assert.NotNull(replay.Refusal);
        Assert.Equal((refusal.Status, refusal.Code, envelope.CorrelationId), (replay.Refusal.Status, replay.Refusal.Code, replay.OriginalCorrelationId));
        Assert.Equal(refusal.Errors, replay.Refusal.Errors);
        Assert.Equal(before with { ContractAttempts = before.ContractAttempts + 1 }, await ReadForeignMutationCounts(run));
        Assert.Equal(returnBefore, await ReturnEffects(run, id)); AssertReturnClean(safe);
    }

    private static async Task AssertReadForeignMutationsBlocked(Run run, ReturnSafetyScope safe, Guid contractId,
        ForeignMutationCommandProbe probe, CancellationToken token)
    {
        var commands = probe.Commands; var callbacks = 0; var key = Guid.NewGuid().ToString("N");
        var contractEnvelope = run.Envelope("read-contract-" + key, Approver, SalesPermissionManifestV1.ContractsDiscountApprove);
        var draftEnvelope = run.Envelope("read-draft-" + key, Approver, SalesPermissionManifestV1.OrdersApprove);
        var audit = new SalesDraftStateAudit("sales.contract.discount-approved.v1", "ApproveContractDiscount", "SENTETIK canary");
        var refusal = new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.StoreContractAsync(contractEnvelope with { PermissionCode = SalesPermissionManifestV1.ContractsWrite }, Hash('d'),
            (_, _) => { callbacks++; throw new NotSupportedException("Foreign contract builder must not run"); }, audit, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.RecordContractRefusalAsync(contractEnvelope, Hash('d'),
            SalesContractCommands.ApproveDiscountV1, contractId, refusal, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.DecideContractAsync(contractEnvelope, Hash('d'),
            SalesContractCommands.ApproveDiscountV1, contractId, (_, _) => callbacks++, audit, token));
        // These four Draft cores were already guarded. Keep that coverage while closing the missing Contract entries.
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.StoreDraftAsync(draftEnvelope with { PermissionCode = SalesPermissionManifestV1.DraftsCreate }, Hash('e'),
            _ => { callbacks++; throw new NotSupportedException("Foreign draft builder must not run"); }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.RecordRefusalAsync(draftEnvelope, Hash('e'), refusal, token,
            SalesDraftCommands.ApproveV1, contractId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.UpdateDraftStateAsync(draftEnvelope, Hash('e'),
            SalesDraftCommands.ApproveV1, contractId, (_, _) => callbacks++, audit, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.ReviseDraftCommercialAsync(draftEnvelope with { PermissionCode = SalesPermissionManifestV1.OrdersPriceWrite }, Hash('e'),
            SalesDraftCommands.ReviseCommercialV1, contractId, (_, _) => { callbacks++; return null; }, audit,
            _ => { callbacks++; return Task.CompletedTask; }, token));
        Assert.Equal(0, callbacks); Assert.Equal(commands, probe.Commands);
        Assert.True(safe.Db.ChangeTracker.HasChanges()); Assert.True(safe.Writer.HasPendingDrafts);
    }

    private static async Task<SalesContract> SeedReadForeignContract(Run run)
    {
        await using var safe = ReturnScope(run); var currency = Guid.NewGuid();
        var money = new MoneyProfileSnapshot(CompanyA, currency, SalesOrderDraft.MoneyOperation, Guid.NewGuid(), "3", Day, true, Day.AddYears(-1), null, 2);
        var stored = await safe.Store.StoreContractAsync(run.Envelope("read-contract-seed-" + Guid.NewGuid().ToString("N"), Preparer, SalesPermissionManifestV1.ContractsWrite), Hash('d'),
            (number, now) => SalesContract.Create(Guid.NewGuid(), CompanyA, number, Guid.NewGuid(), Guid.NewGuid(), money, Day, null,
                [new(Day.AddMonths(1), 1m)], [new(SalesContractDiscountKind.Line, "SENTETIK", 5m)], null, null, Preparer, now),
            new("sales.contract.created.v1", "CreateContract", "SENTETIK kaydedildi"));
        Assert.Equal(SalesContractState.PendingDiscountApproval, stored.Contract.State); AssertReturnClean(safe);
        return stored.Contract;
    }

    private sealed record ReadForeignMutationEffect(int Contracts, int ContractAttempts, int Drafts, int DraftAttempts, int Audits);
    private static async Task<ReadForeignMutationEffect> ReadForeignMutationCounts(Run run) => new(
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Contracts]"), await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ContractAttempts]"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Drafts]"), await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"));
}
