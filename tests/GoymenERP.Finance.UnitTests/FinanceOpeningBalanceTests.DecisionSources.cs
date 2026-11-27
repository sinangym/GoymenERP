using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using FixedAuthority = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceOpeningBalanceTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Purpose_source_reuses_native_queue_formatter_and_retains_the_exact_terminal_target_without_general_read(bool reversal)
    {
        await using var run = new Run(); var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!; var id = opening.Id;
        if (reversal) { await run.ApproveAsync(Manager, new(id, "1")); id = (await run.ProposeReversalAsync(Specialist, new(id, "2", Reason))).Resource!.Reversal!.Id; }
        await using var scope = new OpeningReadScope(run, Manager);
        IWorkApprovalDecisionSourceV1 source = reversal ? new FinanceOpeningBalanceReversalWorkInboxSourceV1(scope.Database, new(scope.Company, new OpeningDecisionUser(scope), new FixedAuthority(true, true)), scope.Service.WithApprovalSafety())
            : new FinanceOpeningBalanceWorkInboxSourceV1(scope.Database, new(scope.Company, new OpeningDecisionUser(scope), new FixedAuthority(true, true)), scope.Service.WithApprovalSafety());
        var context = new WorkInboxSourceContextV1(CompanyA, Manager, "SENTETIK-source", "SENTETIK-cause");
        var queue = Assert.Single((await source.ListPendingAsync(context, 10, default)).Items!);
        var snapshot = await source.ReadDecisionAsync(context, id, default); Assert.NotNull(snapshot); Assert.Equal(queue.DocumentLabel, snapshot.Item.DocumentLabel); Assert.Equal(queue.PreparedBy, snapshot.Item.PreparedBy);
        Assert.Equal(queue.Rules.ToArray(), snapshot.Item.Rules.ToArray()); Assert.Equal(queue.References.ToArray(), snapshot.Item.References.ToArray());
        Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion); Assert.Equal("/finans/acilis-bakiyeleri", snapshot.Item.Link);
        var detail = reversal ? await scope.Service.ReadReversalDecisionAsync(scope.Context, id, _ => Task.CompletedTask, default)
            : await scope.Service.ReadOpeningDecisionAsync(scope.Context, id, _ => Task.CompletedTask, default);
        Assert.True(FinanceOpeningBalanceService.MatchesDecisionItem(source.Kind, snapshot.Item, detail.Resource));
        Assert.False(FinanceOpeningBalanceService.MatchesDecisionItem(source.Kind, snapshot.Item with { Link = "/other" }, detail.Resource));
        Assert.False(FinanceOpeningBalanceService.MatchesDecisionItem(source.Kind, snapshot.Item with { References = snapshot.Item.References.Reverse().ToArray() }, detail.Resource));
        if (reversal) await run.RejectReversalAsync(Manager, new(opening.Id, "1", Reason)); else await run.RejectAsync(Manager, new(opening.Id, "1", Reason));
        var terminal = await source.ReadDecisionAsync(context, id, default); Assert.NotNull(terminal); Assert.False(terminal.Pending); Assert.Equal("2", terminal.SourceVersion);
        Assert.Equal(id, terminal.Item.ItemId); Assert.Equal("Rejected", terminal.OpeningBalanceProof!.Status);
        Assert.Equal(reversal ? (Guid?)id : null, terminal.OpeningBalanceProof.ReversalId);
        Assert.All(scope.Permissions.Actions, x => Assert.Equal(FinanceAction.ApproveOpeningBalance, x));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Purpose_source_without_the_native_owner_fails_closed_and_native_maker_is_denied(bool reversal)
    {
        await using var run = new Run(); var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!; var id = opening.Id;
        if (reversal) { await run.ApproveAsync(Manager, new(id, "1")); id = (await run.ProposeReversalAsync(Specialist, new(id, "2", Reason))).Resource!.Reversal!.Id; }
        await using var scope = new OpeningReadScope(run, Specialist);
        var fence = new FinanceWorkInboxFence(scope.Company, new OpeningDecisionUser(scope), new FixedAuthority(true, true));
        IWorkApprovalDecisionSourceV1 missing = reversal ? new FinanceOpeningBalanceReversalWorkInboxSourceV1(scope.Database, fence) : new FinanceOpeningBalanceWorkInboxSourceV1(scope.Database, fence);
        var context = new WorkInboxSourceContextV1(CompanyA, Specialist, "SENTETIK-source", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => missing.ReadDecisionAsync(context, id, default));
        IWorkApprovalDecisionSourceV1 source = reversal ? new FinanceOpeningBalanceReversalWorkInboxSourceV1(scope.Database, fence, scope.Service.WithApprovalSafety()) : new FinanceOpeningBalanceWorkInboxSourceV1(scope.Database, fence, scope.Service.WithApprovalSafety());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, id, default));
        Assert.False(scope.Database.ChangeTracker.HasChanges());
    }
}
