using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class OrganizationCompanyTaxIdApprovalSafetySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Tax_purpose_original204_receipt_survives_restart_and_a_later_pending_request_without_reexecution()
    {
        await using var run = new Run(); var prepared = await run.Prepare();
        CommandExecutionResult first; string key;
        OrganizationCompanyTaxIdDecisionReceiptV1 receipt;
        await using (var initial = new Scope(run))
        {
            var reader = Assert.IsAssignableFrom<IOrganizationCompanyTaxIdDecisionReadV1>(initial.Service);
            var pending = await reader.ReadDecisionAsync(prepared.Id, _ => Task.CompletedTask);
            Assert.Equal(OrganizationCompanyTaxIdDecisionReadStatusV1.Found, pending.Status); Assert.True(pending.Detail!.IsCurrent);
            first = await initial.Decide(prepared, true); key = initial.Key;
            Assert.Equal(204, first.Response.StatusCode); Assert.False(first.IsReplay);
            receipt = Assert.IsType<OrganizationCompanyTaxIdDecisionReceiptV1>(await reader.ReadDecisionReceiptAsync(prepared.Id,
                new(prepared.Version, "approve", null), key, first, _ => Task.CompletedTask));
            Assert.Equal(pending.Detail.SourceFactsDigest, receipt.SourceFactsDigest); initial.AssertClean();
        }
        var later = await run.Prepare(); Assert.NotEqual(prepared.Id, later.Id);
        await using var restarted = new Scope(run);
        var owner = Assert.IsAssignableFrom<IOrganizationCompanyTaxIdDecisionReadV1>(restarted.Service);
        var oldDetail = await owner.ReadDecisionAsync(prepared.Id, _ => Task.CompletedTask);
        Assert.Equal(prepared.Id, oldDetail.Detail!.Request.Id); Assert.Equal("approved", oldDetail.Detail.Request.Status);
        var replay = await restarted.Decide(prepared, true, key);
        Assert.True(replay.IsReplay); Assert.Equal(first.Response, replay.Response); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(receipt, await owner.ReadDecisionReceiptAsync(prepared.Id, new(prepared.Version, "approve", null), key, replay, _ => Task.CompletedTask));
        Assert.Null(await owner.ReadDecisionReceiptAsync(later.Id, new(prepared.Version, "approve", null), key, replay, _ => Task.CompletedTask));
        await run.AssertDurable(prepared, true, first.OriginalCorrelationId, true); restarted.AssertClean();
        Assert.Equal("pending", (await owner.ReadDecisionAsync(later.Id, _ => Task.CompletedTask)).Detail!.Request.Status);
    }

    [SqlServerFact]
    public async Task Tax_purpose_stale_reject_binds_frozen_company_version_and_final_consumer_loss_never_releases_receipt()
    {
        await using var run = new Run(); var prepared = await run.Prepare();
        // Existing native-domain fixture setup, matching the native stale-reject regression; not a fake decision.
        await using (var setup = run.Factory.Services.CreateAsyncScope())
        {
            setup.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyId);
            var db = setup.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var company = await db.Companies.SingleAsync(x => x.Id == CompanyId);
            company.ApplyTaxIdCorrection(company.Vkn!, "9000000041", DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        }
        await using var scope = new Scope(run); var owner = Assert.IsAssignableFrom<IOrganizationCompanyTaxIdDecisionReadV1>(scope.Service);
        var stale = await owner.ReadDecisionAsync(prepared.Id, _ => Task.CompletedTask);
        Assert.False(stale.Detail!.IsCurrent); Assert.Equal(prepared.Version + 1, stale.Detail.CompanyVersion);
        Assert.Equal(prepared.Version, stale.Detail.CompanyVersionAtRequest);
        var current = prepared with { Version = stale.Detail.CompanyVersion, Vkn = "9000000041" };
        Assert.Equal(CommandResult.TaxIdChangeStale, (await scope.Decide(current, true, "SENTETIK-stale-purpose")).Result);
        scope.Correlation = "SENTETIK-purpose-reject-" + Guid.NewGuid().ToString("N");
        var answer = await scope.Decide(current, false);
        var command = new DecideCompanyTaxIdChange(current.Version, "reject", null);
        var proof = Assert.IsType<OrganizationCompanyTaxIdDecisionReceiptV1>(await owner.ReadDecisionReceiptAsync(prepared.Id, command, scope.Key, answer, _ => Task.CompletedTask));
        Assert.Equal(current.Version, proof.ExpectedCompanyVersion); Assert.Equal(prepared.Version, proof.CompanyVersionAtRequest);
        Assert.Null(await owner.ReadDecisionReceiptAsync(prepared.Id, command with { ExpectedVersion = prepared.Version }, scope.Key, answer, _ => Task.CompletedTask));
        var calls = 0;
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => owner.ReadDecisionReceiptAsync(prepared.Id, command, scope.Key, answer, _ =>
        { if (++calls == 3) scope.Authority.Granted = false; return Task.CompletedTask; }));
        Assert.Equal(3, calls); scope.Authority.Granted = true; using var cancel = new CancellationTokenSource(); calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.ReadDecisionReceiptAsync(prepared.Id, command, scope.Key, answer, _ =>
        { if (++calls == 3) cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Equal(3, calls); Assert.Equal(proof, await owner.ReadDecisionReceiptAsync(prepared.Id, command, scope.Key, answer, _ => Task.CompletedTask));
        await run.AssertDurable(current, false, answer.OriginalCorrelationId, true); scope.AssertClean();
    }
}
