using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesContractSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Contract_selected_read_and_source_keep_full_native_human_facts_optional_prices_and_original_version_after_decision()
    {
        await using var run = await Run.StartAsync(); var native = await run.PendingContractAsync("contract-selected-read");
        foreach (var prices in new[] { true, false })
        {
            await using var safe = ContractSafetyScope(run); safe.Authority.PriceGranted = prices;
            var answer = await safe.Service.ReadContractDiscountDecisionAsync(native.Id, _ => Task.CompletedTask); Assert.Equal(SalesContractDiscountDecisionReadStateV1.Found, answer.State);
            var detail = answer.Detail!; Assert.Equal(2, detail.Contract.Installments.Count); Assert.Single(detail.Contract.DiscountRules);
            Assert.Equal((native.ValidFrom, native.ValidThrough, native.Clauses, "1"), (detail.Contract.ValidFrom, detail.Contract.ValidThrough, detail.Contract.Clauses, detail.Contract.Version));
            Assert.Equal(prices ? "3000.25" : null, detail.Contract.PlannedTotalAmount); Assert.DoesNotContain(SalesPermissionManifestV1.ContractsRead, safe.Authority.Asked);
            var source = new SalesContractDiscountWorkInboxSourceV1(null!, run.Company(CompanyA), safe.User, safe.Authority, safe.Service);
            var snapshot = await source.ReadDecisionAsync(new(CompanyA, Manager, "SENTETIK selected-contract", null), native.Id, default); Assert.NotNull(snapshot);
            Assert.True(SalesContractDiscountDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail)); Assert.True(snapshot.Pending);
        }
        await using var decide = ContractSafetyScope(run); const string key = "contract-selected-decision";
        var result = await ContractDecision(decide, native.Id, key, true, new("1")); var receipt = result.OriginalDecisionReceipt; Assert.NotNull(receipt);
        var terminalSource = new SalesContractDiscountWorkInboxSourceV1(null!, run.Company(CompanyA), decide.User, decide.Authority, decide.Service);
        var current = await terminalSource.ReadDecisionAsync(new(CompanyA, Manager, "SENTETIK current", null), native.Id, default); Assert.NotNull(current);
        var retained = await terminalSource.ReadDecisionVersionAsync(new(CompanyA, Manager, "SENTETIK retained", null), native.Id, "1", default); Assert.NotNull(retained);
        Assert.Equal(("2", false), (current.SourceVersion, current.Pending)); Assert.Equal(("1", false), (retained.SourceVersion, retained.Pending));
        Assert.Equal("2", retained.SalesContractDiscountProof!.CurrentVersion); Assert.True(retained.SalesContractDiscountProof.Historical);
        var replay = await ContractDecision(decide, native.Id, key, true, new("1"), correlation: "SENTETIK later-correlation");
        Assert.True(replay.IsReplay); Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.True(SalesContractDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, replay, SalesContractCommands.ApproveDiscountV1, new("1"), key, Manager));
        Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, native.Id));
    }
    [SqlServerFact]
    public async Task Contract_genuine_audit_attempt_receipt_preserves_both_native_results_and_unprovable_legacy_raw_replays_stay_native()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var legacy in new[] { true, false })
        {
            var pending = await run.PendingContractAsync("contract-receipt-create-" + Guid.NewGuid().ToString("N")); var key = "contract-receipt-" + pending.Id.ToString("N");
            await using var safe = ContractSafetyScope(run); var body = new SalesDraftStateCommandRequest(legacy ? "01" : "1", approve ? null : legacy ? "  SENTETIK ret  " : "SENTETIK ret");
            var result = await ContractDecision(safe, pending.Id, key, approve, body); Assert.False(result.IsReplay);
            Assert.Equal(legacy, result.OriginalDecisionReceipt is null);
            if (!legacy)
            {
                var receipt = result.OriginalDecisionReceipt!; Assert.Equal((CompanyA, pending.Id, Manager, "1", "2"), (receipt.CompanyId, receipt.ContractId, receipt.ActorId, receipt.ExpectedVersion, receipt.ResultVersion));
                Assert.Equal(SalesContractHash.Compute(approve ? SalesContractCommands.ApproveDiscountV1 : SalesContractCommands.RejectDiscountV1, pending.Id, body), receipt.PayloadHash);
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, SalesDraftJson.Options)); Assert.Equal(3, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt", out _));
            }
            var replay = await ContractDecision(safe, pending.Id, key, approve, body, correlation: "SENTETIK replay"); Assert.True(replay.IsReplay); Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(legacy, replay.OriginalDecisionReceipt is null); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
            var history = await safe.Service.ReadContractDiscountOriginalVersionAsync(pending.Id, "1", _ => Task.CompletedTask);
            Assert.Equal(legacy ? SalesContractDiscountDecisionReadStateV1.Unavailable : SalesContractDiscountDecisionReadStateV1.Found, history.State);
        }
    }
    [SqlServerFact]
    public async Task Contract_receipt_actual_attempt_audit_and_source_read_fence_revocation_rolls_back_effect_audit_and_key_before_retry_once()
    {
        await using var run = await Run.StartAsync();
        // These are receipt-local boundaries after native Save, not a guessed final precommit ordinal:
        // 2=before Attempt SELECT,3=after Attempt,4=after Audit,5=before current source SELECT,6=after current source.
        foreach (var phase in new[] { 2, 3, 4, 5, 6 }) foreach (var cancel in new[] { false, true })
        {
            var pending = await run.PendingContractAsync("contract-receipt-fence-" + Guid.NewGuid().ToString("N")); var key = "receipt-fence-" + pending.Id.ToString("N");
            var save = new ContractSaveProbe(); using var cts = new CancellationTokenSource(); await using var safe = ContractSafetyScope(run, save); var observed = 0; var fired = false;
            safe.Authority.AfterRead = permission => { if (save.Saved && permission == SalesPermissionManifestV1.ContractsDiscountApprove && ++observed == phase)
                { fired = true; if (cancel) cts.Cancel(); else safe.Authority.Granted = false; } return Task.CompletedTask; };
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContractDecision(safe, pending.Id, key, true, new("1"), cts.Token));
            else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, true, new("1")))).Status);
            Assert.True(fired); Assert.Equal(phase, observed); Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
            safe.Authority.Granted = true; safe.Authority.AfterRead = null;
            var first = await ContractDecision(safe, pending.Id, key, true, new("1")); Assert.False(first.IsReplay); Assert.NotNull(first.OriginalDecisionReceipt);
            var replay = await ContractDecision(safe, pending.Id, key, true, new("1")); Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
        }
    }
    [SqlServerFact]
    public async Task Contract_selected_read_completed_consumers_cancel_or_revoke_and_late_price_loss_cannot_release_authorized_numbers()
    {
        await using var run = await Run.StartAsync(); var pending = await run.PendingContractAsync("contract-read-consumers");
        foreach (var phase in new[] { 1, 2, 3, 4, 5 }) foreach (var cancel in new[] { false, true })
        {
            using var cts = new CancellationTokenSource(); await using var safe = ContractSafetyScope(run); var count = 0;
            Task Fence(CancellationToken _) { if (++count == phase) { if (cancel) cts.Cancel(); else safe.Authority.Granted = false; } return Task.CompletedTask; }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ReadContractDiscountDecisionAsync(pending.Id, Fence, cts.Token));
            else { var answer = await safe.Service.ReadContractDiscountDecisionAsync(pending.Id, Fence); Assert.Equal(SalesContractDiscountDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Detail); }
            Assert.Equal(phase, count); Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
        }
        await using var late = ContractSafetyScope(run); var consumers = 0;
        late.Authority.AfterRead = permission => { if (consumers == 5 && permission == SalesPermissionManifestV1.ContractsDiscountApprove) late.Authority.PriceGranted = false; return Task.CompletedTask; };
        var result = await late.Service.ReadContractDiscountDecisionAsync(pending.Id, _ => { consumers++; return Task.CompletedTask; });
        var view = result.Detail!.Contract; Assert.Equal(SalesPriceDisclosure.Withheld, view.AmountDisclosure); Assert.Null(view.PlannedTotalAmount); Assert.Null(view.DiscountLimitPercent);
        Assert.All(view.Installments, x => Assert.Null(x.Amount)); Assert.All(view.DiscountRules, x => Assert.Null(x.RatePercent));
    }
    [SqlServerFact]
    public async Task Contract_selected_read_marker_rejects_reentrant_native_decision_without_adopting_or_discarding_caller_work()
    {
        await using var run = await Run.StartAsync(); var pending = await run.PendingContractAsync("contract-read-reentrant"); var save = new ContractSaveProbe(); await using var safe = ContractSafetyScope(run, save);
        var calls = 0; var nested = 0;
        var result = await safe.Service.ReadContractDiscountDecisionAsync(pending.Id, async _ =>
        {
            // The second consumer is inside the store's single read marker, before its first actual SELECT.
            if (++calls != 2) return;
            var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, "reentrant-" + pending.Id.ToString("N"), true, new("1")));
            Assert.Equal(503, failure.Status); nested++;
        });
        Assert.Equal(1, nested); Assert.Equal(SalesContractDiscountDecisionReadStateV1.Found, result.State); Assert.Equal(0, save.Saves); Assert.Equal(0, safe.Writer.Discards);
        Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
    }
    [SqlServerFact]
    public async Task Contract_owner_read_and_history_are_company_scoped_maker_denied_and_do_not_expose_another_actors_original_receipt()
    {
        await using var run = await Run.StartAsync(); var pending = await run.PendingContractAsync("contract-read-company");
        await using (var maker = ContractSafetyScope(run, actor: Seller))
        { var answer = await maker.Service.ReadContractDiscountDecisionAsync(pending.Id, _ => Task.CompletedTask); Assert.Equal(SalesContractDiscountDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Detail); }
        await using (var other = run.SalesStore(CompanyB))
        { var answer = await other.Store.ReadContractDiscountDecisionSourceAsync(pending.Id, _ => Task.CompletedTask); Assert.Null(answer); }
        await using var checker = ContractSafetyScope(run); var first = await ContractDecision(checker, pending.Id, "contract-private-original", true, new("1")); Assert.NotNull(first.OriginalDecisionReceipt);
        await using (var maker = ContractSafetyScope(run, actor: Seller))
        { var denied = await maker.Service.ReadContractDiscountOriginalVersionAsync(pending.Id, "1", _ => Task.CompletedTask); Assert.Equal(SalesContractDiscountDecisionReadStateV1.Denied, denied.State); Assert.Null(denied.Receipt); }
        await using var otherActor = ContractSafetyScope(run, actor: "SENTETIK-another-checker");
        var current = await otherActor.Service.ReadContractDiscountDecisionAsync(pending.Id, _ => Task.CompletedTask); Assert.Equal(SalesContractDiscountDecisionReadStateV1.Found, current.State);
        var history = await otherActor.Service.ReadContractDiscountOriginalVersionAsync(pending.Id, "1", _ => Task.CompletedTask); Assert.Equal(SalesContractDiscountDecisionReadStateV1.NotFound, history.State); Assert.Null(history.Receipt);
        Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
    }
    private static async Task<int> ContractPrecommitFenceNumber(Run run, bool approve)
    {
        var pending = await run.PendingContractAsync("contract-precommit-calibration-" + Guid.NewGuid().ToString("N")); var save = new ContractSaveProbe(); var reads = 0; var captured = 0;
        var commit = new ContractCommittingCalibration(() => captured = reads); await using var safe = ContractSafetyScope(run, save, commit);
        safe.Authority.AfterRead = permission => { if (save.Saved && permission == SalesPermissionManifestV1.ContractsDiscountApprove) reads++; return Task.CompletedTask; };
        Assert.False((await ContractDecision(safe, pending.Id, "contract-calibrate-" + pending.Id.ToString("N"), approve, new("1", approve ? null : "SENTETIK ret"))).IsReplay);
        Assert.True(captured > 1); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id)); return captured;
    }
    private sealed class ContractCommittingCalibration(Action observe) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { observe(); return ValueTask.FromResult(result); }
    }
}
