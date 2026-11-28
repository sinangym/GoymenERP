using System.Data.Common;
using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesRemainderChangeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Remainder_owner_binds_genuine_native_decision_and_retains_original_N_to_N_plus_one_when_current_draft_and_requester_change()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedRemainder(run); var key = "receipt-" + id.ToString("N");
            await using var originalScope = RemainderScope(run);
            var preview = await originalScope.Service.ReadRemainderDecisionAsync(id, _ => Task.CompletedTask);
            Assert.Equal(SalesRemainderDecisionReadStateV1.Found, preview.State); Assert.NotNull(preview.Detail); Assert.Equal("5", preview.Detail.Version);
            var answer = await DecideOwnedRemainder(originalScope, id, key, approve);
            var receipt = Assert.IsType<SalesRemainderOriginalReceiptV1>(answer.OriginalRemainderDecisionReceipt);
            Assert.Equal((CompanyA, id, 1, 5L, 6L, "SENTETIK-remainder-independent", "SENTETIK-remainder-original"),
                (receipt.CompanyId, receipt.DraftId, receipt.ChangeNo, receipt.ExpectedVersion, receipt.ResultVersion, receipt.ActorId, receipt.OriginalCorrelationId));
            Assert.Equal(preview.Detail.SourceFactsDigest, receipt.SourceFactsDigest);
            Assert.Equal(SalesRemainderDecisionDigestsV1.OriginalDecisionDigest(receipt), receipt.OriginalDecisionDigest);
            Assert.Equal(SalesRemainderDecisionDigestsV1.CurrentResponseDigest(answer), receipt.CurrentResponseDigest);
            var wire = JsonSerializer.SerializeToElement(answer, SalesDraftJson.Options); Assert.False(wire.TryGetProperty("originalRemainderDecisionReceipt", out _));

            // An ACTUAL second request is authored by the original checker. D2 restricts the current pending decision,
            // while the immutable first decision and the native old-key replay remain independent of that new maker.
            await using (var raw = run.SalesStore(CompanyA))
                await raw.Store.ChangeRemainderAsync(run.SalesEnvelope("later-" + id.ToString("N"), receipt.ActorId), Hash("later-" + id), SalesDraftCommands.RequestRemainderChangeV1, id,
                    (order, now) => order.RequestRemainderChange(6, receipt.LineId, RemainderChangeKind.Revise, "7", 7m, "SENTETIK yeni kalan", new(6m, 4m, 2.5m), receipt.ActorId, now),
                    new("sales.order-draft.remainder-change-requested.v1", "RequestRemainderChange", "SENTETIK yeni kalan"), (_, _) => Task.CompletedTask);
            var durable = await run.EffectsAsync(id); Assert.Equal(7, durable.DraftVersion);
            await using var retry = RemainderScope(run); retry.Roles.Support = true;
            var current = await retry.Service.ReadRemainderDecisionAsync(id, _ => Task.CompletedTask); Assert.Equal(SalesRemainderDecisionReadStateV1.Denied, current.State); Assert.Null(current.Detail);
            var historical = await retry.Service.ReadRemainderOriginalVersionAsync(id, "5", _ => Task.CompletedTask);
            Assert.Equal(SalesRemainderDecisionReadStateV1.Found, historical.State); Assert.Equal(7, historical.CurrentVersion);
            Assert.NotNull(historical.Receipt); Assert.Equal(receipt.OriginalDecisionDigest, historical.Receipt.OriginalDecisionDigest);
            var replay = await DecideOwnedRemainder(retry, id, key, approve); Assert.True(replay.IsReplay); Assert.Equal("7", replay.Detail.Draft.Version);
            var replayReceipt = Assert.IsType<SalesRemainderOriginalReceiptV1>(replay.OriginalRemainderDecisionReceipt);
            Assert.Equal((5L, 6L, 1), (replayReceipt.ExpectedVersion, replayReceipt.ResultVersion, replayReceipt.ChangeNo));
            Assert.Equal(receipt.OriginalDecisionDigest, replayReceipt.OriginalDecisionDigest); Assert.Equal(SalesRemainderDecisionDigestsV1.CurrentResponseDigest(replay), replayReceipt.CurrentResponseDigest);
            Assert.Equal(durable, await run.EffectsAsync(id)); Assert.Equal(1, await run.AttemptsAsync(key));
        }
    }

    [SqlServerFact]
    public async Task Remainder_original_artifact_read_revalidates_after_Commands_before_the_next_owner_query_and_honors_completed_cancellation()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRemainder(run); var key = "read-fence-" + id.ToString("N");
        await using (var first = RemainderScope(run)) Assert.NotNull((await DecideOwnedRemainder(first, id, key, true)).OriginalRemainderDecisionReceipt);
        var before = await run.EffectsAsync(id);
        foreach (var cancel in new[] { true, false })
        {
            var probe = new RemainderArtifactReadProbe(); await using var safe = RemainderScope(run, probe); using var cts = new CancellationTokenSource();
            probe.AfterArtifact = () => { if (cancel) cts.Cancel(); else safe.Authority.Granted = false; };
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ReadRemainderOriginalVersionAsync(id, "5", _ => Task.CompletedTask, cts.Token));
            else { var result = await safe.Service.ReadRemainderOriginalVersionAsync(id, "5", _ => Task.CompletedTask); Assert.Equal(SalesRemainderDecisionReadStateV1.Denied, result.State); Assert.Null(result.Receipt); }
            Assert.Equal(1, probe.ArtifactReads); Assert.Equal(0, probe.OtherOwnerQueriesAfterArtifact); Assert.Equal(before, await run.EffectsAsync(id)); AssertOwnedRemainderClean(safe);
        }
    }
    private sealed class RemainderArtifactReadProbe : DbCommandInterceptor
    {
        internal int ArtifactReads, OtherOwnerQueriesAfterArtifact; internal Action? AfterArtifact;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        {
            if (command.CommandText.Contains("DraftAttempts", StringComparison.Ordinal)) { ArtifactReads++; AfterArtifact?.Invoke(); }
            else if (ArtifactReads > 0) OtherOwnerQueriesAfterArtifact++;
            return ValueTask.FromResult(result);
        }
    }
}
