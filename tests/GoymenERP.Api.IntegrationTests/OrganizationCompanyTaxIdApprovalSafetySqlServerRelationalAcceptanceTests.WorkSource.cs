using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class OrganizationCompanyTaxIdApprovalSafetySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Tax_work_source_matches_pending_native_formatter_and_original204_after_later_pending_request()
    {
        await using var run = new Run(); var prepared = await run.Prepare();
        OrganizationCompanyTaxIdDecisionReceiptV1 receipt; CommandExecutionResult native; string key;
        await using (var scope = new Scope(run))
        {
            var owner = Assert.IsAssignableFrom<IOrganizationCompanyTaxIdDecisionReadV1>(scope.Service);
            var source = new OrganizationCompanyTaxIdChangeWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority), owner);
            var context = new WorkInboxSourceContextV1(CompanyId, Checker, "SENTETIK-work-tax", null);
            var pending = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, prepared.Id, default));
            var listed = Assert.Single((await source.ListPendingAsync(context, 5, default)).Items!);
            Assert.Equal(JsonSerializer.Serialize(listed), JsonSerializer.Serialize(pending.Item));
            Assert.Equal(prepared.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), pending.SourceVersion);
            Assert.True(pending.Pending); Assert.True(pending.OrganizationCompanyTaxIdProof!.IsCurrent);
            native = await scope.Decide(prepared, true); key = scope.Key;
            receipt = Assert.IsType<OrganizationCompanyTaxIdDecisionReceiptV1>(await owner.ReadDecisionReceiptAsync(prepared.Id,
                new(prepared.Version, "approve", null), key, native, _ => Task.CompletedTask));
            Assert.True(OrganizationCompanyTaxIdDecisionBindingV1.MatchesOriginalDecisionItem(pending.Item, pending.OrganizationCompanyTaxIdProof, receipt));
            scope.AssertClean();
        }
        var later = await run.Prepare(); Assert.NotEqual(prepared.Id, later.Id);
        await using var restarted = new Scope(run);
        var reader = Assert.IsAssignableFrom<IOrganizationCompanyTaxIdDecisionReadV1>(restarted.Service);
        var restartedSource = new OrganizationCompanyTaxIdChangeWorkInboxSourceV1(restarted.Db, new(restarted.Company, restarted.User, restarted.Authority), reader);
        var terminal = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await restartedSource.ReadDecisionAsync(
            new(CompanyId, Checker, "SENTETIK-old-tax", null), prepared.Id, default));
        Assert.False(terminal.Pending); Assert.False(terminal.OrganizationCompanyTaxIdProof!.IsCurrent);
        Assert.DoesNotContain(terminal.Item.Rules, x => x.RuleCode == OrganizationCompanyTaxIdChangeWorkInboxSourceV1.StaleRule);
        Assert.True(OrganizationCompanyTaxIdDecisionBindingV1.MatchesOriginalDecisionItem(terminal.Item, terminal.OrganizationCompanyTaxIdProof, receipt));
        Assert.Equal((prepared.Version + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), terminal.SourceVersion);
        var replay = await restarted.Decide(prepared, true, key); Assert.True(replay.IsReplay);
        Assert.Equal(native.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(receipt, await reader.ReadDecisionReceiptAsync(prepared.Id, new(prepared.Version, "approve", null), key, replay, _ => Task.CompletedTask));
        await run.AssertDurable(prepared, true, native.OriginalCorrelationId, true); restarted.AssertClean();
    }

    [SqlServerFact]
    public async Task Tax_work_source_late_scope_loss_never_releases_a_cached_purpose_snapshot_or_writes()
    {
        await using var run = new Run(); var prepared = await run.Prepare(); await using var scope = new Scope(run);
        var actual = Assert.IsAssignableFrom<IOrganizationCompanyTaxIdDecisionReadV1>(scope.Service);
        var owner = new LateTaxOwner(actual, () => scope.Authority.Granted = false);
        var source = new OrganizationCompanyTaxIdChangeWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority), owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(new(CompanyId, Checker, "SENTETIK-late-work", null), prepared.Id, default));
        scope.Authority.Granted = true;
        await run.AssertDurable(prepared, true, scope.OriginalCorrelation, false); scope.AssertClean();
    }
    private sealed class LateTaxOwner(IOrganizationCompanyTaxIdDecisionReadV1 actual, Action after) : IOrganizationCompanyTaxIdDecisionReadV1
    {
        public async Task<OrganizationCompanyTaxIdDecisionReadResultV1> ReadDecisionAsync(Guid requestId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
        { var answer = await actual.ReadDecisionAsync(requestId, freshConsumerFence, cancellationToken); after(); return answer; }
        public Task<OrganizationCompanyTaxIdDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid requestId, DecideCompanyTaxIdChange command, string key,
            CommandExecutionResult original, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Work source must not perform a native mutation or receipt read.");
    }
}
