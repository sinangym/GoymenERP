using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesDiscountExceptionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Selected_discount_genuine_receipt_and_original_version_survive_later_listed_self_prepared_prices()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeDiscount(run, listed: true); var body = SafeDiscountRequest(draft);
        const string key = "SENTETIK-discount-purpose-original"; await using var safe = SafeDiscountScope(run);
        var source = DiscountSource(safe); var context = DiscountContext(safe);
        var selected = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, draft.Id, default));
        var human = await safe.Service.ReadDiscountDecisionAsync(draft.Id, _ => Task.CompletedTask);
        Assert.Equal(SalesDiscountDecisionReadStateV1.Found, human.State); Assert.True(human.Detail!.ApprovalSupported);
        Assert.Equal("85", Assert.Single(human.Detail.Lines).Pricing!.NetUnitPrice);
        Assert.True(SalesDiscountDecisionBindingV1.MatchesDecisionSnapshot(selected, human.Detail));
        var first = await safe.Service.ApproveDiscountExceptionsAsync(draft.Id, body, SafeDiscountMetadata(key));
        var original = Assert.IsType<SalesDiscountOriginalReceiptV1>(first.OriginalDiscountApprovalReceipt);
        Assert.True(SalesDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, first, body, key, DiscountApprover));
        Assert.Equal((draft.Version, draft.Version + 1, SalesDraftRequestHash.Compute(SalesDraftCommands.ApproveDiscountExceptionsV1, draft.Id, body)),
            (original.ExpectedVersion, original.ResultVersion, original.PayloadHash));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(first, SalesDraftJson.Options));
        Assert.Equal(3, wire.RootElement.EnumerateObject().Count()); Assert.False(wire.RootElement.TryGetProperty("originalDiscountApprovalReceipt", out _));
        await using (var raw = run.SalesStore(CompanyA))
        {
            var current = await raw.Store.FindDraftAsync(draft.Id); Assert.NotNull(current); var line = Assert.Single(current.Lines);
            await raw.Store.ReviseDraftCommercialAsync(run.SalesEnvelope("SENTETIK-discount-purpose-later", DiscountApprover), Hash("discount-purpose-later"),
                SalesDraftCommands.ReviseCommercialV1, draft.Id, (actual, now) => actual.ReviseCommercial(actual.Version,
                    [new LineCommercialInput(line.Id, line.Commercial with { UnitPrice = "90", DiscountPercent = "5" })], DiscountApprover, now,
                    SalesDiscountLimit.Defined(10m, MoneyProfile, 1)),
                new SalesDraftStateAudit("sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK sonraki ticari revizyon"), _ => Task.CompletedTask);
        }
        Assert.Equal(SalesDiscountDecisionReadStateV1.Denied, (await safe.Service.ReadDiscountDecisionAsync(draft.Id, _ => Task.CompletedTask)).State);
        var historical = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionVersionAsync(context, draft.Id, body.ExpectedVersion, default));
        Assert.False(historical.Pending); Assert.True(historical.SalesDiscountProof!.Historical); Assert.False(historical.Item.Excludes(DiscountApprover));
        safe.Authority.PriceGranted = false;
        var replay = await safe.Service.ApproveDiscountExceptionsAsync(draft.Id, body, SafeDiscountMetadata(key, "SENTETIK-discount-later-replay"));
        Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(original.ResultVersion.ToString(CultureInfo.InvariantCulture), replay.Detail.Draft.Version);
        var retained = Assert.IsType<SalesDiscountOriginalReceiptV1>(replay.OriginalDiscountApprovalReceipt);
        Assert.Equal(original.OriginalDecisionDigest, retained.OriginalDecisionDigest); Assert.Equal(original.OriginalResultDigest, retained.OriginalResultDigest);
        Assert.Equal(SalesDiscountDecisionDigestsV1.CurrentResponseDigest(replay), retained.CurrentResponseDigest);
        Assert.True(SalesDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(historical, replay, body, key, DiscountApprover));
        Assert.All(replay.Detail.Lines, x => { Assert.Equal(SalesPriceDisclosure.Withheld, x.PriceDisclosure); Assert.Null(x.UnitPrice); Assert.Null(x.DiscountPercent); });
        Assert.Equal(draft.Version + 2, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
        Assert.Equal(1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(1, await DiscountAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Selected_discount_each_owned_receipt_read_fence_rolls_back_accepted_effects_then_same_key_records_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var boundary in new[] { 2, 3, 4 }) foreach (var cancel in new[] { false, true })
        {
            var draft = await SeedSafeDiscount(run); var key = "SENTETIK-discount-receipt-" + draft.Id.ToString("N"); var save = new DiscountSaveProbe();
            using var cancellation = new CancellationTokenSource();
            await using (var safe = SafeDiscountScope(run, save))
            {
                var savedFences = 0; var fired = false;
                safe.Authority.AfterRead = permission =>
                {
                    // Post-Save=1, beforeAttempt=2, afterAttempt=3, afterAudit=4; all are actual owned approval fences.
                    if (save.Saved && permission == SalesPermissionManifestV1.OrdersDiscountExceptionApprove && ++savedFences == boundary)
                    { fired = true; if (cancel) cancellation.Cancel(); else safe.Authority.Granted = false; }
                    return Task.CompletedTask;
                };
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key), cancellation.Token));
                else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key)))).Status);
                Assert.True(fired); Assert.True(save.Saved); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
            Assert.Equal(draft.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
            await using var retry = SafeDiscountScope(run);
            var answer = await retry.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key));
            Assert.False(answer.IsReplay); Assert.NotNull(answer.OriginalDiscountApprovalReceipt);
            Assert.True((await retry.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key))).IsReplay);
            Assert.Equal(1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(1, await DiscountAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Selected_discount_actual_listed_and_unlisted_read_contract_keeps_price_disclosure_and_self_exception_separate()
    {
        await using var run = await Run.StartAsync();
        foreach (var listed in new[] { false, true })
        {
            var draft = await SeedSafeDiscount(run, listed); await using var safe = SafeDiscountScope(run, actor: run.Actor);
            safe.Authority.PriceGranted = false;
            var read = await safe.Service.ReadDiscountDecisionAsync(draft.Id, _ => Task.CompletedTask);
            Assert.Equal(listed ? SalesDiscountDecisionReadStateV1.Denied : SalesDiscountDecisionReadStateV1.Found, read.State);
            if (listed) Assert.Null(read.Detail);
            else
            {
                Assert.NotNull(read.Detail); Assert.True(read.Detail.ApprovalSupported); Assert.Equal(SalesPriceDisclosure.Withheld, read.Detail.PriceDisclosure);
                Assert.All(read.Detail.Lines, x => { Assert.Null(x.Pricing); Assert.Null(x.DiscountCheck!.ListUnitPrice); });
                var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await DiscountSource(safe).ReadDecisionAsync(DiscountContext(safe), draft.Id, default));
                Assert.Null(snapshot.Item.PreparedBy); Assert.False(snapshot.Item.Excludes(run.Actor)); Assert.False(snapshot.SalesDiscountProof!.PriceDisclosed);
                Assert.False((await safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("SENTETIK-unlisted-self"))).IsReplay);
            }
            Assert.Equal(listed ? 0 : 1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(listed ? 0 : 1, await DiscountAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Selected_discount_final_completed_consumer_revocation_and_cancellation_release_no_private_facts()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeDiscount(run);
        foreach (var cancel in new[] { false, true })
        {
            await using var safe = SafeDiscountScope(run); using var cancellation = new CancellationTokenSource(); var callbacks = 0;
            Task Fence(CancellationToken _)
            { if (++callbacks == 5) { if (cancel) cancellation.Cancel(); else safe.Authority.Granted = false; } return Task.CompletedTask; }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ReadDiscountDecisionAsync(draft.Id, Fence, cancellation.Token));
            else { var read = await safe.Service.ReadDiscountDecisionAsync(draft.Id, Fence); Assert.Equal(SalesDiscountDecisionReadStateV1.Denied, read.State); Assert.Null(read.Detail); }
            Assert.Equal(5, callbacks); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
        }
        Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
    }

    private static SalesDiscountExceptionWorkInboxSourceV1 DiscountSource(DiscountScope safe) => new(null!, safe.Company, safe.User, safe.Authority, safe.Service);
    private static WorkInboxSourceContextV1 DiscountContext(DiscountScope safe) => new(CompanyA, safe.User.UserId, "SENTETIK-discount-original", null);
}
