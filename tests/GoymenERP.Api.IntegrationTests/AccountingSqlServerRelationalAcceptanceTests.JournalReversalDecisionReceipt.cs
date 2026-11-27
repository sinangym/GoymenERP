using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Journal_reversal_terminal_version_receipt_rolls_back_on_late_refusal_and_remains_original_after_later_approval()
    {
        await using var run = await Run.StartAsync(); var id = await ReversalSafetyPrepare(run);
        var saved = new NormalApprovalCommandSave(); await using var scope = new ReversalApprovalScope(run, extra: saved);
        var deny = true; scope.Authority.Read = () => !deny || !saved.Saved;
        var refused = await scope.Service.ApproveAsync(id, 9, "reversal-stale-original");
        Assert.Equal(AccountingApiStatus.Denied, refused.Status); scope.AssertClean(); await ReversalSafetyDurable(run, id, false);
        deny = false;
        var conflict = await scope.Service.ApproveAsync(id, 9, "reversal-stale-original");
        Assert.Equal(AccountingApiStatus.Conflict, conflict.Status); Assert.Equal(AccountingApiCodes.VersionConflict, conflict.Code);
        Assert.Equal(9, conflict.ExpectedVersion); Assert.Equal(1, conflict.CurrentVersion);
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Approve(id, "reversal-valid-later")).Status);
        var replay = await scope.Service.ApproveAsync(id, 9, "reversal-stale-original");
        Assert.True(replay.IsReplay); Assert.Equal(conflict.Code, replay.Code); Assert.Equal(conflict.ExpectedVersion, replay.ExpectedVersion);
        Assert.Equal(conflict.CurrentVersion, replay.CurrentVersion); Assert.Null(replay.Resource);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName=@n", ("@n", AccountingJournalReversalCommandNamesV1.Approve)));
        scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Journal_reversal_owner_read_and_original_receipt_bind_real_posted_journal_without_general_journal_grants()
    {
        await using var run = await Run.StartAsync(); var id = await ReversalSafetyPrepare(run);
        await using var scope = new ReversalApprovalScope(run);
        var before = await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, before.Status); Assert.Equal("Pending", before.Resource!.Proposal.Status);
        Assert.Equal(16, JsonSerializer.SerializeToElement(before.Resource.Proposal).EnumerateObject().Count());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName=@n", ("@n", AccountingJournalReversalCommandNamesV1.Approve)));
        scope.AssertClean();
        var approved = await scope.Approve(id, "reversal-real-receipt"); var replay = await scope.Approve(id, "reversal-real-receipt");
        Assert.Equal(AccountingApiStatus.Ok, approved.Status); var proof = Assert.IsType<AccountingJournalReversalApprovalReceiptV1>(approved.Resource!.ApprovalReceipt);
        Assert.Equal(proof, replay.Resource!.ApprovalReceipt); Assert.True(replay.IsReplay);
        Assert.Equal(id, proof.ProposalId); Assert.Equal(approved.Resource.ReversalJournalId, proof.ReversalJournalId);
        Assert.Equal(before.Resource.SourceFactsDigest, proof.SourceFactsDigest); Assert.Equal(run.Correlation, proof.OriginalCorrelationId);
        var actual = await run.ScalarAsync<string>("SELECT ResultJson FROM accounting.Commands WHERE CommandName=@n AND IdempotencyKeyHash=@h", ("@n", proof.CommandName), ("@h", proof.IdempotencyKeyHash));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(actual))), proof.OriginalResultDigest);
        Assert.Equal(actual, JsonSerializer.Serialize(approved, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.DoesNotContain("approvalReceipt", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(16, JsonSerializer.SerializeToElement(approved.Resource).EnumerateObject().Count());
        scope.AssertClean(); await ReversalSafetyDurable(run, id, true);
        var consumers = 0;
        var denied = await scope.Service.ReadDecisionAsync(id, _ =>
        {
            consumers++; if (consumers == 6) scope.Authority.Read = () => false; return Task.CompletedTask;
        });
        Assert.Equal(6, consumers); Assert.Equal(AccountingApiStatus.Denied, denied.Status); Assert.Null(denied.Resource);
        scope.AssertClean(); await ReversalSafetyDurable(run, id, true);
        scope.Authority.Read = () => true;
        Assert.True((await scope.Approve(id, "reversal-real-receipt")).IsReplay);
        scope.AssertClean(); await ReversalSafetyDurable(run, id, true);
    }
}
