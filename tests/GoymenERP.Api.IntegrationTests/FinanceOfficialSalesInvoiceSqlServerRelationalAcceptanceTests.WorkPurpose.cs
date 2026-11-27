using GoymenERP.Api.Shell;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOfficialSalesInvoiceSqlServerRelationalAcceptanceTests
{
    // Real owner SQL, native command and actual same-scope Audit writer. Documents/customer/Settings and
    // delegation reader remain controlled existing fixture ports; this is not normal HTTP or real Identity acceptance.
    [SqlServerFact]
    public async Task Work_official_sales_invoice_decisions_reuse_native_receipt_and_old_version_key_after_terminal_state()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); await using var scope = new OfficialSafetyScope(run);
            var delegations = new OfficialWorkDelegations(); var host = OfficialWorkHost(scope, delegations);
            var preview = await host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, id, default);
            Assert.Equal("1", preview.SourceVersion); Assert.True(preview.Pending); Assert.True(preview.RejectionSupported);
            Assert.Equal(id, preview.OfficialSalesInvoice!.InvoiceId);
            var body = new WorkDecisionRequestV1("1", approve, approve ? null : new string('x', 1000)); var key = "official-work-native-" + id;
            var first = await host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, id, body, key, default);
            Assert.Equal(approve ? "Approved" : "Rejected", first.State); Assert.False(first.Replayed);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            var terminal = await host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, id, default);
            Assert.Equal("2", terminal.SourceVersion); Assert.False(terminal.Pending);
            var replay = await host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, id, body, key, default);
            Assert.True(replay.Replayed); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(first.State, replay.State);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Work_official_sales_invoice_delegation_revoked_after_real_Save_rolls_back_then_original_key_executes_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var probe = new OfficialSafetySaveProbe(id);
            await using var scope = new OfficialSafetyScope(run, probe: probe);
            var delegations = new OfficialWorkDelegations(); var host = OfficialWorkHost(scope, delegations);
            probe.AfterSave = () => delegations.Live = false;
            var body = new WorkDecisionRequestV1("1", approve, approve ? null : SafetyReason, Guid.NewGuid());
            var key = "official-work-delegation-" + id;
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, id, body, key, default))).Status);
            Assert.True(probe.Saved); scope.AssertClean(); await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
            probe.AfterSave = null; delegations.Live = true;
            var accepted = await host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, id, body, key, default);
            Assert.Equal(approve ? "Approved" : "Rejected", accepted.State); Assert.False(accepted.Replayed);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    private static WorkDecisionHostV1 OfficialWorkHost(OfficialSafetyScope scope, OfficialWorkDelegations delegations)
    {
        var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(new OfficialWorkPermissions(), execution);
        // Actual runtime authority adapter delegates every owner check through the same Work execution fence.
        var nativeAuthority = new FinanceOfficialSalesInvoiceAuthority(authority, scope.Company, scope.User);
        var envelope = new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, [], "Web");
        var owner = new FinanceOfficialSalesInvoiceServiceV1(scope.Database, scope.Company, scope.User, nativeAuthority,
            new OfficialSafetyDocuments(scope.Database), new Candidates(), scope.Collector,
            new(envelope, envelope, envelope, envelope), TimeProvider.System, new OfficialSafetyCustomer()).WithApprovalSafety(scope.Writer);
        var source = new FinanceOfficialSalesInvoiceWorkInboxSourceV1(scope.Database, new(scope.Company, scope.User, authority), owner);
        var correlation = new CorrelationContext(); correlation.Set(scope.OriginalCorrelation, null, "Web", null, null);
        return new(scope.Company, scope.User, correlation, authority, [source], delegations, execution, null!, null!, officialSalesInvoices: owner);
    }
    private sealed class OfficialWorkPermissions : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) =>
            Task.FromResult(new PermissionScope(company == Company && permission == FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
                true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class OfficialWorkDelegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null);
    }
}
