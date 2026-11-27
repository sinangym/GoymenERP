using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingPostingAccountMappingSqlServerRelationalAcceptanceTests
{
    // Actual migrated SQL and native scoped owner/writer fixture; source only, not actual SQL/HTTP acceptance.
    [SqlServerFact]
    public async Task Mapping_Work_source_lists_one_scalar_query_and_binds_original_activation_after_later_supersession()
    {
        await using var run = await Run.StartAsync(); var maker = await MappingSafetyPrepare(run); await MappingSafetyPrepare(run, 1);
        var query = new MappingWorkQueryProbe(); await using var scope = new MappingActivationScope(run, extra: query);
        var context = new WorkInboxSourceContextV1(CompanyA, MappingSafetyActor.ToString("D"), scope.CorrelationId, null);
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority), scope.Service);
        var before = await run.CountsAsync(); var page = await source.ListPendingAsync(context, 1, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, page.Outcome); Assert.True(page.HasMore); var item = Assert.Single(page.Items!);
        var statement = Assert.Single(query.Commands);
        Assert.Contains("PostingAccountMappingVersions", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("PostingAccountMappingRows", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("ApprovedSourceCatalogs", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountPlanSourceVersion", statement, StringComparison.Ordinal);
        Assert.True(AccountingPostingAccountMappingDecisionBindingV1.TryMappingVersion(item.ItemId, out var mappingVersion)); Assert.Equal(1L, mappingVersion);
        Assert.Equal("1", item.DocumentLabel); Assert.True(item.Excludes(maker.ToString("D"))); Assert.False(item.Excludes(context.UserId));
        Assert.Equal(AccountingPostingAccountMappingDecisionBindingV1.PurposePath + "/" + item.ItemId.ToString("D"), item.Link);
        Assert.Equal(AccountingPostingAccountMappingDecisionBindingV1.NativeScreenPath, Assert.Single(item.References).Link);
        var snapshot = (await source.ReadDecisionAsync(context, item.ItemId, default))!;
        Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion); var proof = Assert.IsType<WorkPostingAccountMappingProofV1>(snapshot.PostingAccountMappingProof);
        Assert.Equal(1L, proof.MappingVersion); Assert.Equal(1L, proof.Version); Assert.Equal(0L, proof.ActiveMappingVersion);
        Assert.True(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionProof(item, proof));
        var detail = (await scope.Service.ReadDecisionAsync(1, _ => Task.CompletedTask)).Resource!;
        Assert.True(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionItem(item, detail)); Assert.Equal(3, detail.Mapping.Rows.Count);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web); var wire = JsonSerializer.Serialize(snapshot, json);
        Assert.DoesNotContain("postingAccountMappingProof", wire, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(proof.SourceFactsDigest, wire, StringComparison.Ordinal);
        Assert.DoesNotContain(detail.Mapping.AccountPlanSourceVersion, wire, StringComparison.Ordinal); Assert.DoesNotContain("currentActiveAccountPlan", wire, StringComparison.Ordinal);
        Assert.Equal(before, await run.CountsAsync()); scope.AssertClean();
        var original = await scope.Service.ActivateAsync(1, new(1, 0), "mapping-work-original");
        Assert.Equal(AccountingApiStatus.Ok, original.Status); Assert.NotNull(original.Resource?.ActivationReceipt);
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Service.ActivateAsync(2, new(1, 1), "mapping-work-next")).Status);
        var current = (await source.ReadDecisionAsync(context, item.ItemId, default))!;
        Assert.False(current.Pending); Assert.Equal("3", current.SourceVersion); Assert.Equal(2L, current.PostingAccountMappingProof?.ActiveMappingVersion);
        Assert.Equal(proof.SourceFactsDigest, current.PostingAccountMappingProof?.SourceFactsDigest);
        var replay = await scope.Service.ActivateAsync(1, new(1, 0), "mapping-work-original");
        Assert.True(replay.IsReplay); Assert.Equal("Active", replay.Resource?.Status); Assert.Equal(2L, replay.Resource?.Version);
        Assert.Equal(original.Resource?.ActivationReceipt, replay.Resource?.ActivationReceipt);
        Assert.True(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionItem(current.Item, replay.Resource, replay.Resource?.ActivationReceipt));
        var settled = await run.CountsAsync(); Assert.Empty((await source.ListPendingAsync(context, 10, default)).Items!);
        Assert.Equal(settled, await run.CountsAsync()); scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Mapping_Work_source_rechecks_fresh_authority_after_real_owner_read_without_releasing_or_mutating_facts()
    {
        foreach (var fault in new[] { "permission", "company", "actor", "member", "cancel" })
        {
            await using var run = await Run.StartAsync(); await MappingSafetyPrepare(run); var before = await run.CountsAsync();
            await using var scope = new MappingActivationScope(run); var allowed = true; scope.Authority.Read = () => allowed;
            using var cancellation = new CancellationTokenSource();
            var owner = new MappingWorkOwnerAfterRead(scope.Service, () =>
            {
                if (fault == "permission") allowed = false; if (fault == "company") scope.Company.Set(CompanyB);
                if (fault == "actor") scope.User.Actor = Guid.NewGuid(); if (fault == "member") scope.User.Member = false;
                if (fault == "cancel") cancellation.Cancel();
            });
            var source = new AccountingPostingAccountMappingWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority), owner);
            var context = new WorkInboxSourceContextV1(CompanyA, MappingSafetyActor.ToString("D"), scope.CorrelationId, null);
            var id = AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(1);
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadDecisionAsync(context, id, cancellation.Token));
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, id, default));
            Assert.Equal(1, owner.Reads); Assert.Equal(before, await run.CountsAsync()); scope.AssertClean();
        }
    }

    private sealed class MappingWorkOwnerAfterRead(IAccountingPostingAccountMappingDecisionReadV1 actual, Action after) : IAccountingPostingAccountMappingDecisionReadV1
    {
        public int Reads;
        public async Task<AccountingApiResult<AccountingPostingAccountMappingDecisionDetailV1>> ReadDecisionAsync(long version,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        { Reads++; var result = await actual.ReadDecisionAsync(version, fence, token); after(); return result; }
    }
    private sealed class MappingWorkQueryProbe : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
