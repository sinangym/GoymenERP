using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOfficialSalesInvoiceSqlServerRelationalAcceptanceTests
{
    // Real migrated Finance SQL, native commands and scoped Audit writer from ApprovalSafety. External
    // document/customer/currency and authority/delegation timing remain controlled ports, not HTTP/Identity E2E.
    [SqlServerFact]
    public async Task Official_decision_provenance_binds_fresh_and_original_key_replay_without_remapping_a_replacement_record()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run);
            await using var scope = new OfficialSafetyScope(run);
            var answer = await scope.Decide(id, approve, "official-purpose-original");
            Assert.Equal(CommandOutcome.Succeeded, answer.Outcome);
            var proof = Assert.IsType<FinanceOfficialSalesInvoiceDecisionReceiptV1>(answer.DecisionReceipt);
            Assert.Equal((Company, id, SafetyChecker, "1", "2", scope.OriginalCorrelation),
                (proof.CompanyId, proof.InvoiceId, proof.ActorId, proof.ExpectedVersion, proof.ResultVersion, proof.OriginalCorrelationId));
            Assert.Equal(approve ? FinanceOfficialSalesInvoiceServiceV1.ApproveCommand : FinanceOfficialSalesInvoiceServiceV1.RejectCommand, proof.Command);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            if (!approve)
            {
                var old = await scope.Database.OfficialSalesInvoices.AsNoTracking().SingleAsync(x => x.CompanyId == Company && x.Id == id);
                var replacement = await ReserveAsync(run, SafetyMaker);
                await using var maker = run.Service(SafetyMaker, pdfSha: Sha("SENTETIK replacement " + replacement));
                var recorded = await maker.Service.RecordAsync(maker.Context, new("official-purpose-replacement"),
                    Request(replacement) with { OfficialNo = old.OfficialNo, Ettn = old.Ettn, PdfDocumentId = Guid.NewGuid() }, default);
                Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome); Assert.Equal("1", recorded.Resource!.Version);
                Assert.NotEqual(id, recorded.Resource.InvoiceId);
            }
            scope.Correlation = "official-purpose-retry";
            var replay = await scope.Decide(id, approve, "official-purpose-original");
            Assert.True(replay.IsReplay); Assert.Equal(JsonSerializer.Serialize(answer.Resource, FinanceJson.CreateOptions()), JsonSerializer.Serialize(replay.Resource, FinanceJson.CreateOptions()));
            Assert.Equal(proof, replay.DecisionReceipt); Assert.Equal(answer.OriginalCorrelationId, replay.OriginalCorrelationId);
            using var wire = JsonDocument.Parse(JsonSerializer.Serialize(replay, FinanceJson.CreateOptions()));
            Assert.Equal(6, wire.RootElement.EnumerateObject().Count()); Assert.False(wire.RootElement.TryGetProperty("decisionReceipt", out _));
            var stored = await scope.Database.OfficialSalesInvoiceCommands.AsNoTracking().SingleAsync(x =>
                x.CompanyId == Company && x.InvoiceId == id && x.CommandName == proof.Command);
            Assert.Equal(proof.PayloadHash, stored.PayloadHash); Assert.Equal(proof.KeyHash, stored.IdempotencyKeyHash);
            Assert.Null(JsonSerializer.Deserialize<FinanceOfficialSalesInvoiceResultV1>(stored.ResultJson, FinanceJson.CreateOptions())!.DecisionReceipt);
            var conflict = await scope.Decide(id, approve, "official-purpose-terminal", version: "2");
            Assert.Equal(CommandOutcome.VersionConflict, conflict.Outcome); Assert.Null(conflict.DecisionReceipt);
            var terminalReplay = await scope.Decide(id, approve, "official-purpose-terminal", version: "2");
            Assert.True(terminalReplay.IsReplay); Assert.Equal(CommandOutcome.VersionConflict, terminalReplay.Outcome); Assert.Null(terminalReplay.DecisionReceipt);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 2, 1);
        }
    }

    [SqlServerFact]
    public async Task Official_purpose_read_is_approve_only_maker_excluding_and_refuses_context_loss_after_native_detail_without_writes()
    {
        await using var run = await Run.StartAsync(); var id = await OfficialSafetyPrepare(run);
        await using var scope = new OfficialSafetyScope(run);
        var authority = new OfficialPurposeAuthority();
        var envelope = new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, [], "Web");
        var guarded = new FinanceOfficialSalesInvoiceServiceV1(scope.Database, scope.Company, scope.User, authority,
            new OfficialSafetyDocuments(scope.Database), new Candidates(), scope.Collector,
            new(envelope, envelope, envelope, envelope), TimeProvider.System, new OfficialSafetyCustomer()).WithApprovalSafety(scope.Writer);
        var reader = Assert.IsAssignableFrom<IFinanceOfficialSalesInvoiceDecisionReadV1>(guarded);
        var context = new FinanceContext(Company, SafetyChecker, "official-purpose-read", null);
        var calls = 0;
        var pending = await reader.ReadDecisionAsync(context, id, _ => { calls++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, pending.Outcome); Assert.Equal(4, calls);
        Assert.Equal((id, Company, "1", SafetyMaker), (pending.Resource!.InvoiceId, pending.Resource.CompanyId, pending.Resource.Version, pending.Resource.RecordedBy));
        Assert.NotEqual(Guid.Empty, pending.Resource.PdfDocumentId); Assert.Equal(64, pending.Resource.PdfSha256.Length);
        scope.User.Actor = SafetyMaker;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionAsync(context with { ActorId = SafetyMaker }, id, _ => Task.CompletedTask, default)).Outcome);
        scope.User.Actor = SafetyChecker;
        authority.Approve = PermissionDecision.Denied;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionAsync(context, id, _ => Task.CompletedTask, default)).Outcome);
        authority.Approve = PermissionDecision.Allowed;
        calls = 0;
        var revoked = await reader.ReadDecisionAsync(context, id, _ => { calls++; if (calls == 3) scope.User.Member = false; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Denied, revoked.Outcome); Assert.Null(revoked.Resource); scope.User.Member = true;
        Assert.Equal(ReadOutcome.NotFound, (await reader.ReadDecisionAsync(context, Guid.NewGuid(), _ => Task.CompletedTask, default)).Outcome);
        scope.AssertClean(); await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
    }

    [SqlServerFact]
    public async Task Official_receipt_read_authority_loss_rolls_back_before_release_and_the_same_key_later_saves_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var probe = new OfficialSafetySaveProbe(id);
            await using var scope = new OfficialSafetyScope(run, probe: probe);
            var afterSaveReads = 0; var deny = true;
            scope.Authority.BeforeRead = () =>
            {
                // First fresh check is in SaveOwnedDecision; the next two bracket the stored-receipt query.
                if (probe.Saved && ++afterSaveReads == 3 && deny) return PermissionDecision.Denied;
                return PermissionDecision.Allowed;
            };
            var refused = await scope.Decide(id, approve, "official-purpose-receipt-fence");
            Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.Null(refused.DecisionReceipt);
            Assert.True(probe.Saved); Assert.Equal(3, afterSaveReads);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
            deny = false; probe.Saved = false; afterSaveReads = 0;
            var accepted = await scope.Decide(id, approve, "official-purpose-receipt-fence");
            Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome); Assert.NotNull(accepted.DecisionReceipt);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    private sealed class OfficialPurposeAuthority : IFinanceOfficialSalesInvoiceAuthorityV1
    {
        public PermissionDecision Approve = PermissionDecision.Allowed;
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid id, CancellationToken token)
            => Task.FromResult(action == FinanceOfficialSalesInvoiceAuthorityActionV1.Approve ? Approve : PermissionDecision.Allowed);
    }
}
