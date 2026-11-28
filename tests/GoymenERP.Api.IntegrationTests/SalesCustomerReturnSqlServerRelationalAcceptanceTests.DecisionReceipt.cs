using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesCustomerReturnSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Owned_return_receipt_and_selected_source_use_genuine_native_attempt_audit_and_full_immutable_commercial_facts()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-receipt");
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedReturn(run, invoice); var key = "receipt-return-" + id.ToString("N"); await using var safe = ReturnScope(run);
            var read = await safe.Service.ReadCustomerReturnDecisionAsync(id, _ => Task.CompletedTask);
            Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Found, read.State); var detail = Assert.IsType<SalesCustomerReturnDecisionDetailV1>(read.Detail);
            var source = new SalesCustomerReturnWorkInboxSourceV1(null!, ReturnSourceCompany(), new FixedUser(Approver, CompanyA), safe.Authority, safe.Service);
            var snapshot = Assert.IsType<GoymenERP.BuildingBlocks.Work.WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(new(CompanyA, Approver, "SENTETIK-work", null), id, default));
            Assert.True(SalesCustomerReturnDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail));
            var first = await DecideReturn(safe, id, key, approve, version: "2"); var receipt = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(first.OriginalDecisionReceipt);
            Assert.Equal((2L, 3L, CompanyA, id, Approver, Metadata(key).CorrelationId), (receipt.ExpectedVersion, receipt.ResultVersion, receipt.CompanyId, receipt.ReturnId, receipt.ActorId, receipt.OriginalCorrelationId));
            Assert.True(SalesCustomerReturnDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, first, new("2", approve ? null : "  SENTETIK ret  "), key, Approver, approve));
            Assert.Equal(JsonSerializer.Serialize(first with { OriginalDecisionReceipt = null }, SalesDraftJson.Options), JsonSerializer.Serialize(first, SalesDraftJson.Options));
            var durable = await ReturnEffects(run, id); await using var restart = ReturnScope(run);
            var replay = await DecideReturn(restart, id, key, approve, version: "2"); Assert.True(replay.IsReplay); Assert.NotNull(replay.OriginalDecisionReceipt);
            Assert.Equal(receipt.OriginalDecisionDigest, replay.OriginalDecisionReceipt.OriginalDecisionDigest); Assert.Equal(durable, await ReturnEffects(run, id));
            var old = await restart.Service.ReadCustomerReturnOriginalVersionAsync(id, "2", _ => Task.CompletedTask);
            Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Found, old.State); Assert.NotNull(old.Receipt); Assert.Equal(3L, old.CurrentVersion);
            Assert.Equal(receipt.OriginalDecisionDigest, old.Receipt.OriginalDecisionDigest); AssertReturnClean(restart);
        }
    }
    [SqlServerFact]
    public async Task Owned_return_original_approval_receipt_survives_real_native_finalization_and_current_replay_without_second_effect()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-receipt-finalized"); var id = await SeedOwnedReturn(run, invoice);
        var key = "original-return-" + id.ToString("N"); await using var safe = ReturnScope(run); var original = await DecideReturn(safe, id, key, true, version: "2");
        var receipt = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(original.OriginalDecisionReceipt);
        await using (var raw = run.SalesStore(CompanyA))
        {
            // Existing SENTETIK Finance acceptance port only; this is native Sales provenance evidence, not cross-module Finance acceptance.
            var finalized = await run.Service(raw.Store, CompanyA, Finalizer, new FinancePort()).FinalizeCustomerReturnAsync(id, new("3"), Metadata("finalize-" + id.ToString("N")));
            Assert.Equal(("Finalized", "4"), (finalized.CustomerReturn.State, finalized.CustomerReturn.Version));
        }
        var durable = await ReturnEffects(run, id); await using var restart = ReturnScope(run);
        var source = new SalesCustomerReturnWorkInboxSourceV1(null!, ReturnSourceCompany(), new FixedUser(Approver, CompanyA), restart.Authority, restart.Service);
        var snapshot = Assert.IsType<GoymenERP.BuildingBlocks.Work.WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionVersionAsync(new(CompanyA, Approver, "SENTETIK-work", null), id, "2", default));
        Assert.False(snapshot.Pending); Assert.Equal(4L, snapshot.SalesCustomerReturnProof!.CurrentVersion);
        var replay = await DecideReturn(restart, id, key, true, version: "2"); var r = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(replay.OriginalDecisionReceipt);
        Assert.Equal(("Finalized", "4", "Approved", "3"), (replay.CustomerReturn.State, replay.CustomerReturn.Version, r.OriginalDecision.State, r.OriginalDecision.Version));
        Assert.Equal(receipt.OriginalDecisionDigest, r.OriginalDecisionDigest); Assert.True(SalesCustomerReturnDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, replay, new("2"), key, Approver, true)); Assert.Equal(durable, await ReturnEffects(run, id));
        Assert.Equal(1, await ReturnAttempts(run, key)); AssertReturnClean(restart);
    }
    [SqlServerFact]
    public async Task Return_purpose_fences_after_the_actual_current_or_attempt_audit_query_and_never_reads_the_next_source_after_revocation()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-read-fences"); var id = await SeedOwnedReturn(run, invoice);
        await using (var prepare = ReturnScope(run)) Assert.NotNull((await DecideReturn(prepare, id, "read-fence-" + id.ToString("N"), true)).OriginalDecisionReceipt);
        foreach (var historical in new[] { false, true }) foreach (var cancel in new[] { false, true }) foreach (var permission in new[] { SalesPermissionManifestV1.ReturnsApprove, SalesPermissionManifestV1.OrdersPriceRead })
        {
            var probe = new ReturnPurposeQueryProbe(); await using var safe = ReturnScope(run, Approver, probe); using var cts = new CancellationTokenSource(); var fired = false;
            Task Consumer(CancellationToken _) { if (probe.Reads > 0 && !fired) { fired = true; if (cancel) cts.Cancel(); else safe.Authority.Denied.Add(permission); } return Task.CompletedTask; }
            if (cancel)
            {
                if (historical) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ReadCustomerReturnOriginalVersionAsync(id, "2", Consumer, cts.Token));
                else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ReadCustomerReturnDecisionAsync(id, Consumer, cts.Token));
            }
            else if (historical) { var result = await safe.Service.ReadCustomerReturnOriginalVersionAsync(id, "2", Consumer); Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Denied, result.State); Assert.Null(result.Receipt); }
            else { var result = await safe.Service.ReadCustomerReturnDecisionAsync(id, Consumer); Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Denied, result.State); Assert.Null(result.Detail); }
            Assert.True(fired); Assert.Equal(1, probe.Reads); AssertReturnClean(safe);
        }
    }
    private static ICompanyContext ReturnSourceCompany() { var company = new CompanyContext(); company.Set(CompanyA); return company; }
    private sealed class ReturnPurposeQueryProbe : DbCommandInterceptor
    {
        internal int Reads;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        { if (command.CommandText.Contains("CustomerReturns", StringComparison.Ordinal) || command.CommandText.Contains("CustomerReturnAttempts", StringComparison.Ordinal)) Reads++; return ValueTask.FromResult(result); }
    }
}
