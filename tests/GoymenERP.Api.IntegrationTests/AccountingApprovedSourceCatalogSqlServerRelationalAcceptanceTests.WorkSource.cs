using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingApprovedSourceCatalogSqlServerRelationalAcceptanceTests
{
    // Existing actual SQL/native owned fixture; this source was not executed by its author.
    [SqlServerFact]
    public async Task Catalog_work_source_preserves_bounded_pending_self_activation_and_original_receipt_after_supersession()
    {
        await using var run = await Run.StartAsync();
        var first = Package(Guid.NewGuid(), "SENTETIK-work-first"); var second = Package(Guid.NewGuid(), "SENTETIK-work-second");
        var third = Package(Guid.NewGuid(), "SENTETIK-work-third");
        await CatalogSafetyPrepare(run, first); await CatalogSafetyPrepare(run, second); await CatalogSafetyPrepare(run, third);
        await using var scope = new CatalogActivationScope(run);
        var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority), scope.Service);
        var context = new WorkInboxSourceContextV1(CompanyA, CatalogSafetyActor.ToString("D"), scope.CorrelationId, null);
        var pending = await source.ListPendingAsync(context, 1, default);
        var item = Assert.Single(pending.Items!); Assert.True(pending.HasMore); Assert.Equal(first.Id, item.ItemId);
        Assert.Equal(CatalogSafetyActor.ToString("D"), item.PreparedBy); Assert.False(item.Excludes(context.UserId)); Assert.Null(item.ExcludedDeciders);
        Assert.Equal("/onaylarim/accounting.approved-source-catalog.v1/" + first.Id.ToString("D"), item.Link);
        Assert.Null(Assert.Single(item.References).Link);
        var before = (await source.ReadDecisionAsync(context, first.Id, default))!;
        Assert.True(before.Pending); Assert.Equal("1", before.SourceVersion); Assert.Equal(0L, before.ApprovedSourceCatalogProof!.ActiveCatalogVersion);
        Assert.True(AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionProof(item, before.ApprovedSourceCatalogProof));
        var approved = await scope.Service.ActivateAsync(first.Id, new(1, 0), "work-source-original");
        Assert.Equal(AccountingApiStatus.Ok, approved.Status); Assert.NotNull(approved.Resource?.ActivationReceipt);
        var afterOriginal = await run.CountsAsync(first.Id);
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Service.ActivateAsync(second.Id, new(1, 1), "work-source-replacement")).Status);
        var current = (await source.ReadDecisionAsync(context, first.Id, default))!;
        Assert.False(current.Pending); Assert.Equal("3", current.SourceVersion); Assert.Equal("Superseded", current.ApprovedSourceCatalogProof!.Status);
        Assert.Equal(2L, current.ApprovedSourceCatalogProof.ActiveCatalogVersion);
        Assert.Equal(before.ApprovedSourceCatalogProof.SourceFactsDigest, current.ApprovedSourceCatalogProof.SourceFactsDigest);
        var replay = await scope.Service.ActivateAsync(first.Id, new(1, 0), "work-source-original");
        Assert.True(replay.IsReplay); Assert.Equal("Active", replay.Resource?.Status); Assert.Equal(2L, replay.Resource?.Version);
        Assert.Equal(approved.Resource?.ActivationReceipt, replay.Resource?.ActivationReceipt);
        Assert.True(AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionItem(current.Item, replay.Resource, replay.Resource?.ActivationReceipt));
        var remaining = await source.ListPendingAsync(context, 1, default);
        Assert.False(remaining.HasMore); Assert.Equal(third.Id, Assert.Single(remaining.Items!).ItemId);
        var wire = JsonSerializer.Serialize(current, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("approvedSourceCatalogProof", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(current.ApprovedSourceCatalogProof.SourceFactsDigest, wire, StringComparison.Ordinal);
        Assert.Equal(afterOriginal, await run.CountsAsync(first.Id)); scope.AssertClean();
        scope.Authority.Read = () => false;
        var denied = await source.ListPendingAsync(context, 1, default); Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, denied.Outcome); Assert.Null(denied.Items);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, first.Id, default));
        Assert.Equal(afterOriginal, await run.CountsAsync(first.Id)); scope.AssertClean();
    }
}
