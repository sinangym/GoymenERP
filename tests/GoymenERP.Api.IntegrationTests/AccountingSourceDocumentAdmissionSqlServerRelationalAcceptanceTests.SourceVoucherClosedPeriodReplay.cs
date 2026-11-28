using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSourceDocumentAdmissionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Source_voucher_original_receipt_survives_actual_period_close_and_fresh_scope_without_another_effect()
    {
        await using var run = await Run.StartAsync(true);
        await OpenVoucherPeriod(run);
        var admission = await run.AdmitAsync(CompanyA, Request(CompanyA));
        var id = admission.VoucherDraftId!.Value;
        AccountingSourceVoucherApprovalReceiptV1 receipt;
        string originalCorrelation;
        const string key = "SENTETIK-source-voucher-closed-period-original";
        await using (var initial = new VoucherApprovalScope(run))
        {
            var accepted = await initial.Approve(id, key);
            Assert.Equal(AccountingApiStatus.Ok, accepted.Status);
            Assert.False(accepted.IsReplay);
            receipt = Assert.IsType<AccountingSourceVoucherApprovalReceiptV1>(accepted.Resource?.ApprovalReceipt);
            originalCorrelation = initial.CorrelationId;
            var period = accepted.Resource!.PeriodId!.Value;
            var version = await run.ScalarAsync<long>("SELECT Version FROM accounting.Periods WHERE CompanyId=@c AND Id=@p",
                ("@c", CompanyA), ("@p", period));
            // Existing real Accounting + Finance carry fixture, no direct status update or fake period flag.
            await AccountingPeriodCarrySqlFixture.CloseAsync(run.Factory, CompanyA, period, version,
                ReversalChecker, VoucherMetadata(ReversalChecker));
            initial.AssertClean();
        }
        var afterClose = await VoucherSafetyCounts(run);
        await using var restarted = new VoucherApprovalScope(run);
        var current = await restarted.Service.ReadDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, current.Status);
        Assert.NotNull(current.Resource);
        Assert.False(current.Resource.PeriodOpen);
        Assert.False(current.Resource.SourceCancelled);
        Assert.Equal("Posted", current.Resource.Voucher.Status);
        Assert.Equal(receipt.SourceFactsDigest, current.Resource.SourceFactsDigest);
        var source = new AccountingSourceVoucherWorkInboxSourceV1(restarted.Db,
            new(restarted.Company, restarted.User, restarted.Authority), restarted.Service);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(
            new(CompanyA, VoucherChecker.ToString("D"), "SENTETIK-restarted-work", null), id, default));
        Assert.False(snapshot.Pending);
        Assert.Equal("2", snapshot.SourceVersion);
        Assert.False(snapshot.SourceVoucherProof!.PeriodOpen);
        Assert.Equal(2, snapshot.Item.Rules.Count);
        var replay = await restarted.Approve(id, key);
        Assert.Equal(AccountingApiStatus.Ok, replay.Status);
        Assert.True(replay.IsReplay);
        Assert.Equal("Posted", replay.Resource?.Status);
        Assert.Equal(2L, replay.Resource?.Version);
        Assert.Equal(originalCorrelation, replay.OriginalCorrelationId);
        Assert.Equal(receipt, replay.Resource?.ApprovalReceipt);
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesOriginalDecisionItem(snapshot.Item,
            replay.Resource, replay.Resource?.ApprovalReceipt));
        Assert.Equal(afterClose, await VoucherSafetyCounts(run));
        Assert.Equal(0, restarted.Writer.Writes);
        restarted.AssertClean();
    }
}
