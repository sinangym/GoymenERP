using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Normal_draft_approval_decision_read_and_original_receipt_keep_explicit_period_and_native_wire_without_another_posting()
    {
        await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run);
        await using var scope = new NormalApprovalScope(run);
        var before = await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, before.Status); Assert.NotNull(before.Resource);
        Assert.Null(before.Resource.Draft.PeriodId); Assert.Null(before.Resource.Draft.PostedJournalId);
        var first = await scope.Approve(id, period, "normal-receipt-period");
        var proof = Assert.IsType<NormalJournalDraftApprovalReceiptV1>(first.Resource!.ApprovalReceipt);
        Assert.Equal(id, proof.DraftId); Assert.Equal(CompanyA, proof.CompanyId); Assert.Equal(run.Actor, proof.ActorId);
        Assert.Equal(1, proof.ExpectedVersion); Assert.Equal(2, proof.ResultVersion); Assert.Equal(period, proof.PeriodId);
        Assert.Equal(id, proof.PostedJournalId); Assert.Equal(run.Correlation, proof.OriginalCorrelationId);
        Assert.Equal(before.Resource.SourceFactsDigest, proof.SourceFactsDigest);
        var after = await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, after.Status); Assert.Equal(before.Resource.SourceFactsDigest, after.Resource!.SourceFactsDigest);
        Assert.NotEqual(before.Resource.PreviewDigest, after.Resource.PreviewDigest);
        var replay = await scope.Approve(id, period, "normal-receipt-period");
        Assert.True(replay.IsReplay); Assert.Equal(proof, replay.Resource!.ApprovalReceipt);
        Assert.Equal(20, JsonSerializer.SerializeToElement(replay.Resource).EnumerateObject().Count());
        Assert.DoesNotContain("approvalReceipt", JsonSerializer.Serialize(replay), StringComparison.OrdinalIgnoreCase);
        var stored = await run.ScalarAsync<string>("SELECT ResultJson FROM accounting.Commands WHERE CommandName=@n AND IdempotencyKeyHash=@k",
            ("@n", NormalJournalCommandNamesV1.ApproveDraft), ("@k", proof.IdempotencyKeyHash));
        Assert.Equal(proof.OriginalResultDigest, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stored))));
        Assert.DoesNotContain("approvalReceipt", stored, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AccountingApiStatus.Conflict, (await scope.Service.ApproveAsync(id, 1, Guid.NewGuid(), "normal-receipt-period")).Status);
        await NormalSafetyDurable(run, id, true); scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Normal_draft_receipt_command_and_retained_source_await_refusals_roll_back_actual_saved_posting_and_reuse_the_original_key_once()
    {
        // Actual migrated SQL and actual scoped Audit writer; authority answers at new read boundaries are controlled.
        foreach (var boundary in new[] { 4, 6, 7, 8, 9 }) // saved result: command, source header, source lines, exact credit membership, release.
        {
            await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run);
            var save = new NormalApprovalSave(id); await using var scope = new NormalApprovalScope(run, save);
            var seen = 0; var denied = false;
            scope.Authority.Read = () =>
            {
                if (!save.FinalSaved) return true;
                if (++seen != boundary) return true;
                denied = true; return false;
            };
            Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(id, period, "normal-receipt-fence")).Status);
            Assert.True(denied); Assert.Equal(boundary, seen); scope.AssertClean(); await NormalSafetyDurable(run, id, false);
            scope.Authority.Read = null; save.FinalSaved = false;
            var first = await scope.Approve(id, period, "normal-receipt-fence");
            var replay = await scope.Approve(id, period, "normal-receipt-fence");
            Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.NotNull(first.Resource!.ApprovalReceipt);
            Assert.True(replay.IsReplay); Assert.Equal(first.Resource.ApprovalReceipt, replay.Resource!.ApprovalReceipt);
            scope.AssertClean(); await NormalSafetyDurable(run, id, true);
        }
    }
}
