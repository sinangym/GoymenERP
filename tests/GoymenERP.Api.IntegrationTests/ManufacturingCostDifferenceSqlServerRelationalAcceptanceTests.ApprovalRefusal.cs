using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cost_difference_late_attribution_business_refusals_keep_native_422_after_owned_abort_and_same_key_valid_retry_once()
    {
        foreach (var fault in new[] { "before-plan", "missing-owner", "source", "domain", "amount" })
        {
            await using var run = await Run.StartAsync(); var source = await PrepareLineageSource(run); var id = source.CostDifferenceEventId!.Value;
            var before = await SafetyCounts(run); var save = new CostApprovalSave(id); var physical = new CostSafetyLineage(source) { Fault = fault };
            var money = new CostAttributionMoney { Active = fault != "amount" };
            await using (var scope = new CostApprovalScope(run, save, lineage: fault == "missing-owner" ? null : physical, attributionMoney: money))
            {
                scope.CurrentPlan.Missing = fault == "before-plan";
                var refused = await scope.Approve(id, "late-business-refusal");
                Assert.Equal(AccountingApiStatus.Unprocessable, refused.Status); Assert.Null(refused.Resource); Assert.False(refused.IsReplay);
                Assert.Equal(fault switch { "before-plan" => "cost_difference_account_plan_missing", "missing-owner" => "manufacturing_attribution_owner_unavailable", "source" => "manufacturing_attribution_posted_amount_mismatch",
                    "domain" => "cost_difference_posting_refused", _ => "cost_difference_amount_unaccepted" }, refused.Code);
                Assert.Null(refused.Field); Assert.Equal(fault == "before-plan" ? 0 : 2, save.Saves);
                // The four late attribution cases have saved both journal and proposal/OK-command inside the still-owned SQL transaction; the plan refusal precedes all saves.
                Assert.Equal(fault == "before-plan" ? 0 : 1, scope.Writer.Discards); scope.AssertClean();
            }
            Assert.Equal(before, await SafetyCounts(run)); Assert.Equal(0, await run.Count("accounting.ManufacturingCostAttributions"));
            Assert.Equal(0, await run.Count("accounting.ManufacturingCostAttributionShares"));
            await using (var read = run.Scope()) { var pending = await read.Store.FindAsync(id); Assert.Equal("Prepared", pending?.Status); Assert.Equal(2L, pending?.Version); }
            physical.Fault = null; money.Active = true;
            await using var retry = new CostApprovalScope(run, lineage: physical, attributionMoney: money);
            var accepted = await retry.Approve(id, "late-business-refusal"); var replay = await retry.Approve(id, "late-business-refusal");
            Assert.Equal(AccountingApiStatus.Ok, accepted.Status); Assert.Equal("Posted", accepted.Resource?.Status); Assert.Equal(3L, accepted.Resource?.Version);
            Assert.True(replay.IsReplay); Assert.Equal(accepted.Resource?.JournalId, replay.Resource?.JournalId); Assert.Equal(retry.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(before with { Journals = before.Journals + 1, Deliveries = before.Deliveries + 1, Commands = before.Commands + 1, Audits = before.Audits + 2 }, await SafetyCounts(run));
            Assert.Equal(1, await run.Count("accounting.ManufacturingCostAttributions")); Assert.Equal(1, await run.Count("accounting.ManufacturingCostAttributionShares"));
            Assert.Equal(600m, await run.Scalar<decimal>("SELECT Debit FROM accounting.JournalLines WHERE Debit>0"));
            Assert.Equal(600m, await run.Scalar<decimal>("SELECT Credit FROM accounting.JournalLines WHERE Credit>0")); retry.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Cost_difference_late_business_refusal_cleanup_fault_is_not_reported_as_a_completed_native_422()
    {
        foreach (var dispose in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var source = await PrepareLineageSource(run);
            var id = source.CostDifferenceEventId!.Value; var save = new CostApprovalSave(id); var cleanup = new CostApprovalCleanup(dispose);
            await using var scope = new CostApprovalScope(run, save, cleanup, lineage: new CostSafetyLineage(source) { Fault = "domain" });
            var failure = await Assert.ThrowsAsync<AggregateException>(() => scope.Approve(id, "late-cleanup-fault"));
            Assert.Contains("cleanup failed", failure.Message, StringComparison.Ordinal); Assert.True(cleanup.Failed);
            Assert.Equal(2, save.Saves); Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts); Assert.Equal(1, scope.Writer.Discards);
            // No clean-abort, business-answer, winner-read or real rollback acceptance claim after a cleanup fault.
        }
    }

    [SqlServerFact]
    public async Task Cost_difference_business_answer_release_rechecks_authority_and_completed_cancellation_after_owned_cleanup()
    {
        foreach (var cancel in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var source = await PrepareLineageSource(run); var id = source.CostDifferenceEventId!.Value;
            var before = await SafetyCounts(run); var save = new CostApprovalSave(id);
            await using var scope = new CostApprovalScope(run, save, lineage: new CostSafetyLineage(source) { Fault = "domain" });
            using var cancellation = new CancellationTokenSource(); var released = false;
            scope.Authority.Read = () =>
            {
                if (save.Saves != 2 || scope.Db.Database.CurrentTransaction is not null) return true;
                released = true; if (cancel) { cancellation.Cancel(); return true; } return false;
            };
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Approve(id, "late-release", cancellation.Token));
            else { var answer = await scope.Approve(id, "late-release"); Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource); }
            Assert.True(released); Assert.Equal(before, await SafetyCounts(run)); scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Cost_difference_saved_terminal_version_receipt_replays_original_versions_and_maker_refusal_is_unchanged()
    {
        await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var before = await SafetyCounts(run);
        await using var scope = new CostApprovalScope(run); scope.User.Actor = Maker;
        var maker = await scope.Approve(id, "maker"); Assert.Equal(AccountingApiStatus.Denied, maker.Status);
        Assert.Equal("cost_difference_approval_refused", maker.Code); Assert.Equal(before, await SafetyCounts(run)); scope.AssertClean();
        scope.User.Actor = Checker;
        var stale = await scope.Service.ApproveAsync(id, 99, "stale-terminal");
        Assert.Equal(AccountingApiStatus.Conflict, stale.Status); Assert.Equal(AccountingApiCodes.VersionConflict, stale.Code);
        Assert.Equal(99L, stale.ExpectedVersion); Assert.Equal(2L, stale.CurrentVersion);
        Assert.Equal(before with { Commands = before.Commands + 1 }, await SafetyCounts(run));
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Approve(id, "legitimate")).Status);
        var replay = await scope.Service.ApproveAsync(id, 99, "stale-terminal");
        Assert.True(replay.IsReplay); Assert.Equal(AccountingApiStatus.Conflict, replay.Status); Assert.Equal(stale.Code, replay.Code);
        Assert.Equal(stale.Field, replay.Field); Assert.Equal(99L, replay.ExpectedVersion); Assert.Equal(2L, replay.CurrentVersion);
        Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId); scope.AssertClean();
        Assert.Equal(before with { Journals = before.Journals + 1, Deliveries = before.Deliveries + 1, Commands = before.Commands + 2, Audits = before.Audits + 2 }, await SafetyCounts(run));
    }

    private static async Task<ManufacturingCostDifferenceEventV1> PrepareLineageSource(Run run)
    {
        var source = Source(); source = source with { ConsumedLineageReference = new("inventory.manufacturing-consumed-cost-lineage.v1",
            source.GenerationId, source.GenerationVersion, new string('a', 64)) };
        var period = await run.OpenPeriod(); await using var scope = run.Scope(); await scope.Store.AcceptAsync(Envelope(source), source);
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Service(Maker).PrepareAsync(Preparation(source.CostDifferenceEventId!.Value, period), "SENTETIK-lineage-prepare")).Status);
        return source;
    }
    // Physical owner and money-profile responses are controlled; all journal/source/attribution/command/Audit writes remain actual native SQL owners.
    private sealed class CostSafetyLineage(ManufacturingCostDifferenceEventV1 original) : IManufacturingAttributionLineageReaderV1
    {
        public string? Fault;
        public async Task<ManufacturingAttributionLineageV1> ReadAsync(ManufacturingCostDifferenceEventV1 source, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            await fence(token);
            if (Fault == "domain") throw new AccountingDomainException("SENTETIK-late-attribution", "SENTETIK controlled late domain refusal");
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(original, Json), System.Text.Json.JsonSerializer.Serialize(source, Json));
            var output = Assert.Single(source.Outputs);
            var shipment = new ManufacturingAttributionShipmentV1(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), output.LabelId,
                30m, new("1", "1"), new("30", "1"), At, null, false, "{\"syntheticPhysicalOwner\":true}");
            return new(source.CompanyId, source.CostDifferenceEventId!.Value, source.GenerationId, source.GenerationVersion,
                Fault == "source" ? new string('b', 64) : source.ConsumedLineageReference!.HistorySha256,
                [new(output.OutputId, output.ReceiptMovementId, output.LabelId, output.NetQuantityKg, new("30", "1"), new string('b', 64), [shipment])]);
        }
    }
    private sealed class CostAttributionMoney : ISettingsPrerequisiteProvider
    {
        public bool Active = true;
        public async Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currency, string operation, DateOnly date, CancellationToken token = default)
        { var actual = await new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(Company).GetMoneyProfileAsync(currency, operation, date, token); return actual with { IsActive = Active }; }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid id, DateOnly date, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly date, DateOnly? valueOn, CancellationToken token = default) => throw new NotSupportedException();
    }
}
