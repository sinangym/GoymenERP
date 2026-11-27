using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class CounterpartyIdentityDecisionSafetySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Work_tax_identity_approval_rolls_back_late_delegation_loss_then_replays_after_later_change() => WorkIdentityDecisionAsync(true, true);
    [SqlServerFact]
    public Task Work_tax_identity_rejection_rolls_back_late_delegation_loss_then_replays_after_later_change() => WorkIdentityDecisionAsync(true, false);
    [SqlServerFact]
    public Task Work_legal_name_approval_rolls_back_late_delegation_loss_then_replays_after_later_change() => WorkIdentityDecisionAsync(false, true);
    [SqlServerFact]
    public Task Work_legal_name_rejection_rolls_back_late_delegation_loss_then_replays_after_later_change() => WorkIdentityDecisionAsync(false, false);

    private async Task WorkIdentityDecisionAsync(bool tax, bool approve)
    {
        await using var data = await Case.CreateAsync(tax);
        var probe = new SaveProbe(); var delegation = new WorkIdentityDelegation(data);
        await using var harness = new Harness(data, probe, workFence: true, workDelegations: delegation);
        var kind = tax ? WorkDecisionHostV1.CounterpartyTaxIdentity : WorkDecisionHostV1.CounterpartyLegalName;
        var version = data.Version.ToString(CultureInfo.InvariantCulture); var delegationId = Guid.NewGuid();
        // Real native SaveChanges/audit inside the owned transaction; the Work lease is revoked at that late boundary.
        probe.AfterSaved = () => delegation.Allowed = false;
        var refused = await Assert.ThrowsAsync<WorkDecisionException>(() => Decide("SENTETIK-work-abort"));
        Assert.Equal(403, refused.Status); Assert.Equal(0, await data.CommandCountAsync());
        Assert.Equal(0, await data.DecisionAuditCountAsync()); Assert.Equal("Pending", await data.StatusAsync());
        Assert.Equal(data.Version, await data.VersionAsync());
        delegation.Allowed = true; probe.AfterSaved = null;
        var success = await Decide("SENTETIK-work-first");
        Assert.Equal((approve ? "Approved" : "Rejected", false, "SENTETIK-work-first"), (success.State, success.Replayed, success.OriginalCorrelationId));
        Assert.Equal(approve ? data.Version + 1 : data.Version, await data.VersionAsync());
        Assert.Equal(1, await data.CommandCountAsync()); Assert.Equal(1, await data.DecisionAuditCountAsync());
        using (var maker = data.MakerClient())
        {
            var laterId = await data.LaterProposalAsync(maker, approve ? data.Version + 1 : data.Version);
            await using var native = harness.Scope();
            Assert.Equal(204, (await native.DecideAsync(true, "later-native", "SENTETIK-later-native", requestId: laterId,
                expectedVersion: approve ? data.Version + 1 : data.Version)).Response.StatusCode);
        }
        var replay = await Decide("SENTETIK-work-replay");
        Assert.Equal((success.State, true, "SENTETIK-work-first"), (replay.State, replay.Replayed, replay.OriginalCorrelationId));
        Assert.Equal(2, await data.CommandCountAsync()); Assert.Equal(2, await data.DecisionAuditCountAsync());
        delegation.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => Decide("SENTETIK-revoked-replay"))).Status);
        Assert.Equal(2, await data.CommandCountAsync()); Assert.Equal(2, await data.DecisionAuditCountAsync());

        async Task<WorkDecisionResultV1> Decide(string correlation)
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            services.GetRequiredService<ICorrelationContext>().Set(correlation, null, "Web", null, null);
            var host = services.GetRequiredService<WorkDecisionHostV1>();
            var preview = await host.PreviewAsync(kind, data.RequestId, default);
            Assert.Equal(data.RequestId, preview.ItemId); Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
            Assert.Equal(data.PartyId, Assert.Single(preview.References).RecordId);
            var wire = JsonSerializer.Serialize(preview);
            Assert.DoesNotContain("1234567890", wire); Assert.DoesNotContain("10000000146", wire); Assert.DoesNotContain("4567890128", wire);
            return await host.DecideAsync(kind, data.RequestId, new(version, approve, new string('n', 1000), delegationId), "work-original-key", default);
        }
    }

    [SqlServerFact]
    public Task Work_tax_identity_preserves_foreign_account_maker_and_native_permission_gates() => WorkIdentityPurposeRefusalsAsync(true);
    [SqlServerFact]
    public Task Work_legal_name_preserves_foreign_account_maker_and_native_permission_gates() => WorkIdentityPurposeRefusalsAsync(false);

    private async Task WorkIdentityPurposeRefusalsAsync(bool tax)
    {
        await using var data = await Case.CreateAsync(tax); await using var harness = new Harness(data, new(), workFence: true);
        var kind = tax ? WorkDecisionHostV1.CounterpartyTaxIdentity : WorkDecisionHostV1.CounterpartyLegalName;
        await using (var foreign = harness.Factory.Services.CreateAsyncScope())
        {
            foreign.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyB);
            foreign.ServiceProvider.GetRequiredService<ICorrelationContext>().Set("SENTETIK-foreign-work", null, "Web", null, null);
            Assert.Equal(404, (await Assert.ThrowsAsync<WorkDecisionException>(() => foreign.ServiceProvider.GetRequiredService<WorkDecisionHostV1>()
                .PreviewAsync(kind, data.RequestId, default))).Status);
        }
        harness.User.UserId = data.RequestedBy;
        await AssertRefusal(403); harness.Restore();
        harness.Authority.Granted = false; await AssertRefusal(403); harness.Restore();
        Assert.Equal(0, await data.CommandCountAsync()); Assert.Equal("Pending", await data.StatusAsync());
        Assert.True(ErpPermissions.IsCritical(CounterpartyTaxIdentityPermissionsV1.Approve));
        async Task AssertRefusal(int status)
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set("SENTETIK-refused-work", null, "Web", null, null);
            Assert.Equal(status, (await Assert.ThrowsAsync<WorkDecisionException>(() => scope.ServiceProvider.GetRequiredService<WorkDecisionHostV1>()
                .DecideAsync(kind, data.RequestId, new(data.Version.ToString(CultureInfo.InvariantCulture), false, null), "refused-key", default))).Status);
        }
    }

    private sealed class WorkIdentityDelegation(Case data) : IWorkDelegationReaderV1
    {
        public bool Allowed = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Allowed ? new(id, TestCompanyIds.CompanyA, item.Kind,
                data.Tax ? CounterpartyTaxIdentityPermissionsV1.Approve : CounterpartyLegalNamePermissionsV1.Approve,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1",
                Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null);
    }
}
