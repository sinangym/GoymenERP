using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Both_period_native_original_receipts_survive_later_close_and_another_pending_reopening_without_changing_native_wire()
    {
        foreach (var reopen in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var prepared = await PreparePeriodApproval(run, reopen);
            await using var scope = new PeriodApprovalScope(run);
            AccountingPeriodApprovalReceiptV1 proof;
            string original;
            if (prepared.Proposal is { } proposal)
            {
                var before = await scope.Service.ReadReopeningDecisionAsync(proposal, _ => Task.CompletedTask);
                Assert.Equal(AccountingApiStatus.Ok, before.Status);
                var first = await scope.Service.ApprovePeriodReopeningAsync(prepared.Period, proposal, new(1, 3), "period-immutable", default);
                Assert.Equal(AccountingApiStatus.Ok, first.Status); proof = Assert.IsType<AccountingPeriodApprovalReceiptV1>(first.Resource!.ApprovalReceipt);
                Assert.Equal(before.Resource!.SourceFactsDigest, proof.SourceFactsDigest);
                Assert.Equal((1L, 3L, 2L), (proof.ExpectedVersion, proof.ExpectedPeriodVersion!.Value, proof.ResultVersion));
                Assert.False(proof.LaterPeriodClosed);
                original = JsonSerializer.Serialize(first.Resource); Assert.Equal(11, JsonSerializer.SerializeToElement(first.Resource).EnumerateObject().Count());
            }
            else
            {
                var before = await scope.Service.ReadOpeningDecisionAsync(prepared.Period, _ => Task.CompletedTask);
                Assert.Equal(AccountingApiStatus.Ok, before.Status);
                var first = await scope.Service.ApprovePeriodAsync(prepared.Period, new(1), "period-immutable", default);
                Assert.Equal(AccountingApiStatus.Ok, first.Status); proof = Assert.IsType<AccountingPeriodApprovalReceiptV1>(first.Resource!.ApprovalReceipt);
                Assert.Equal(before.Resource!.SourceFactsDigest, proof.SourceFactsDigest);
                Assert.Equal((1L, 2L), (proof.ExpectedVersion, proof.ResultVersion)); Assert.Null(proof.ReopeningId);
                original = JsonSerializer.Serialize(first.Resource); Assert.Equal(13, JsonSerializer.SerializeToElement(first.Resource).EnumerateObject().Count());
                await AccountingPeriodCarrySqlFixture.PrepareAsync(run.Factory, CompanyA, prepared.Period, run.Actor);
            }
            scope.AssertClean();
            var currentVersion = reopen ? 4L : 2L;
            await AccountingPeriodCarrySqlFixture.CloseAsync(run.Factory, CompanyA, prepared.Period, currentVersion, run.Actor, Metadata(run));
            await using (var other = run.Store(CompanyA))
                await ((IAccountingPeriodReopeningStoreV1)other.Periods).ProposeReopeningAsync(prepared.Period,
                    new(currentVersion + 1, Guid.NewGuid(), "SENTETIK later pending proposal"), run.Actor,
                    Metadata(run) with { PermissionCode = AccountingPermissionManifestV1.PeriodsPropose }, default);
            // The current proposal maker is now this actor. Only the ORIGINAL command and proposal may supply its receipt.
            if (prepared.Proposal is { } originalProposal)
            {
                var replay = await scope.Service.ApprovePeriodReopeningAsync(prepared.Period, originalProposal, new(1, 3), "period-immutable", default);
                Assert.True(replay.IsReplay); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
                Assert.Equal(original, JsonSerializer.Serialize(replay.Resource)); Assert.Equal(proof, replay.Resource!.ApprovalReceipt);
            }
            else
            {
                var replay = await scope.Service.ApprovePeriodAsync(prepared.Period, new(1), "period-immutable", default);
                Assert.True(replay.IsReplay); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
                Assert.Equal(original, JsonSerializer.Serialize(replay.Resource)); Assert.Equal(proof, replay.Resource!.ApprovalReceipt);
            }
            var stored = await run.ScalarAsync<string>("SELECT ResultJson FROM accounting.Commands WHERE CommandName=@n AND IdempotencyKeyHash=@k",
                ("@n", prepared.Command), ("@k", proof.IdempotencyKeyHash));
            Assert.DoesNotContain("approvalReceipt", stored, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored))), proof.OriginalResultDigest);
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName=@n", ("@n", prepared.Command)));
            Assert.Equal(currentVersion + 1, await run.ScalarAsync<long>("SELECT Version FROM accounting.Periods WHERE Id=@id", ("@id", prepared.Period)));
            scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Period_receipt_command_and_exact_source_read_fences_roll_back_both_owners_before_releasing_the_new_receipt()
    {
        foreach (var reopen in new[] { false, true })
        foreach (var boundary in reopen ? new[] { 3, 5, 6, 7, 8, 9 } : new[] { 3, 5, 6 })
        {
            await using var run = await Run.StartAsync(); var prepared = await PreparePeriodApproval(run, reopen);
            var save = new PeriodApprovalSave(prepared.Period); await using var scope = new PeriodApprovalScope(run, save);
            var callbacks = 0; var observed = false;
            scope.Authority.Read = () =>
            {
                if (!save.Saved) return true;
                if (++callbacks != boundary) return true;
                observed = true; return false;
            };
            Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(prepared, "period-receipt-fence")).Status);
            Assert.True(observed); Assert.Equal(boundary, callbacks); scope.AssertClean();
            await AssertPeriodApprovalDurable(run, prepared, false);
            scope.Authority.Read = null; save.Saved = false;
            var first = await scope.Approve(prepared, "period-receipt-fence"); var replay = await scope.Approve(prepared, "period-receipt-fence");
            Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.True(replay.Replay); Assert.Equal(first.Json, replay.Json);
            scope.AssertClean(); await AssertPeriodApprovalDurable(run, prepared, true);
        }
    }
}
