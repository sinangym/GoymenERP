using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesPriceFloorApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Floor_historical_source_recovers_original_key_after_the_same_approver_becomes_the_current_requester()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run); var request = SafeFloorRequest(draft);
        const string key = "SENTETIK-floor-history"; SalesPriceFloorOriginalReceiptV1 original;
        await using (var safe = SafeFloorScope(run))
        {
            var answer = await FloorDecisionService(safe, new FloorDecisionAuthority()).ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata(key));
            original = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(answer.OriginalFloorApprovalReceipt);
        }
        await using (var raw = run.Store(Company))
        {
            var current = await raw.Store.FindDraftAsync(draft.Id); Assert.NotNull(current); var line = Assert.Single(current.Lines);
            await raw.Store.ReviseDraftCommercialAsync(Envelope("SENTETIK-floor-self-reprice", Approver), Hash("self-reprice"), SalesDraftCommands.ReviseCommercialV1,
                draft.Id, (actual, now) => actual.ReviseCommercial(actual.Version,
                    [new LineCommercialInput(line.Id, line.Commercial with { UnitPrice = "91" })], Approver, now,
                    SalesDiscountLimit.Defined(10m, Guid.Parse("4d15c0a1-0000-4000-8000-0000000000c1"), 1)), Priced, _ => Task.CompletedTask);
        }
        await using (var currentScope = SafeFloorScope(run))
        {
            var current = await FloorDecisionService(currentScope, new FloorDecisionAuthority()).ReadPriceFloorDecisionAsync(draft.Id, _ => Task.CompletedTask);
            Assert.Equal(SalesPriceFloorDecisionReadStateV1.Denied, current.State); Assert.Null(current.Detail);
        }
        var reads = new FloorHistoryReadProbe(); await using (var safe = SafeFloorScope(run, reads))
        {
            var authority = new FloorDecisionAuthority(); var service = FloorDecisionService(safe, authority);
            var source = FloorRelationalSource(safe, authority, service); var context = new WorkInboxSourceContextV1(Company, Approver, "SENTETIK-history", null);
            var selected = await source.ReadDecisionVersionAsync(context, draft.Id, request.ExpectedVersion, default);
            Assert.NotNull(selected); Assert.True(selected.SalesPriceFloorProof!.Historical); Assert.False(selected.Pending);
            Assert.False(selected.Item.Excludes(Approver)); Assert.True(selected.Item.Excludes(Requester));
            Assert.Equal(0, reads.CurrentDraftReads); Assert.Equal(1, reads.HistoryReads);
            Assert.All(selected.Item.Rules, x => { Assert.Null(x.Actual); Assert.Null(x.Threshold); Assert.Null(x.Deviation); Assert.Null(x.Unit); });
            var replay = await service.ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata(key, "SENTETIK-history-retry"));
            Assert.True(replay.IsReplay); Assert.Equal(original.OriginalDecisionDigest, replay.OriginalFloorApprovalReceipt!.OriginalDecisionDigest);
            Assert.True(SalesPriceFloorDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, replay, request, key, Approver));
            Assert.Equal(original.ResultVersion.ToString(CultureInfo.InvariantCulture), replay.Detail.Draft.Version);
            Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Floor_history_filters_exact_actor_and_version_before_Take2_and_rejects_duplicate_immutable_audit_artifacts()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run); var request = SafeFloorRequest(draft);
        const string key = "SENTETIK-floor-immutable-candidates";
        await using (var safe = SafeFloorScope(run)) await FloorDecisionService(safe, new FloorDecisionAuthority())
            .ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata(key));
        // Other successful drafts at the same native version do not consume this exact candidate probe's bound.
        for (var n = 0; n < 3; n++)
        {
            var other = await SeedSafeFloor(run); await using var safe = SafeFloorScope(run);
            await FloorDecisionService(safe, new FloorDecisionAuthority()).ApprovePriceFloorExceptionsAsync(other.Id, SafeFloorRequest(other), SafeFloorMetadata("SENTETIK-other-floor-" + n));
        }
        await using (var wrongActor = SafeFloorScope(run, actor: Requester))
        {
            var read = await FloorDecisionService(wrongActor, new FloorDecisionAuthority()).ReadPriceFloorOriginalVersionAsync(draft.Id, request.ExpectedVersion, _ => Task.CompletedTask);
            Assert.Equal(SalesPriceFloorDecisionReadStateV1.NotFound, read.State); Assert.Null(read.Receipt); Assert.Equal(draft.Version + 1, read.CurrentVersion);
        }
        await using (var safe = SafeFloorScope(run))
        {
            var service = FloorDecisionService(safe, new FloorDecisionAuthority());
            var first = await service.ReadPriceFloorOriginalVersionAsync(draft.Id, request.ExpectedVersion, _ => Task.CompletedTask);
            Assert.Equal(SalesPriceFloorDecisionReadStateV1.Found, first.State); Assert.NotNull(first.Receipt); Assert.Null(first.CurrentVersion);
            var native = await safe.Db.Set<AuditEvent>().AsNoTracking().SingleAsync(x => x.CompanyId == Company && x.EntityId == draft.Id.ToString("D")
                && x.EventType == "sales.order-draft.price-floor-exceptions-approved.v1");
            var beforeJson = await safe.Db.DraftAttempts.AsNoTracking().Where(x => x.CompanyId == Company && x.DraftId == draft.Id
                && x.CommandName == SalesDraftCommands.ApprovePriceFloorExceptionsV1).Select(x => x.ResultJson).SingleAsync();
            // Controlled corruption fixture: append a second immutable, integrity-valid artifact through the ACTUAL Audit writer.
            // No UPDATE/trigger bypass, no second native business decision and no claim this duplicate is normal production behavior.
            var a = native.ToIntegrityEnvelope();
            await using (var transaction = await safe.Db.Database.BeginTransactionAsync())
            {
                safe.Collector.Enqueue(new AuditEventDraft(Company, a.EventType, a.EntityType, a.EntityId, a.Operation,
                    a.EntityVersionBefore, a.EntityVersionAfter, a.ActorType, a.ActorId, a.PermissionCode!,
                    JsonSerializer.Deserialize<string[]>(a.RoleCodesJson)!, a.BeforeJson, a.AfterJson, a.Reason, a.ApprovalContextJson,
                    a.AiInvolvement, a.AiContextJson, a.SourceChannel, a.ClientIpAddress, a.UserAgent, a.CorrelationId, a.CausationId, key));
                await safe.Writer.WritePendingAsync(transaction.GetDbTransaction()); await transaction.CommitAsync();
            }
            Assert.False(safe.Writer.HasPendingDrafts);
            var ambiguous = await service.ReadPriceFloorOriginalVersionAsync(draft.Id, request.ExpectedVersion, _ => Task.CompletedTask);
            Assert.Equal(SalesPriceFloorDecisionReadStateV1.Unavailable, ambiguous.State); Assert.Null(ambiguous.Receipt);
            Assert.Equal(beforeJson, await safe.Db.DraftAttempts.AsNoTracking().Where(x => x.CompanyId == Company && x.DraftId == draft.Id
                && x.CommandName == SalesDraftCommands.ApprovePriceFloorExceptionsV1).Select(x => x.ResultJson).SingleAsync());
        }
        Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(2, await FloorAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Floor_history_completed_query_revocation_and_cancellation_stop_before_current_fallback_without_mutation()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run); var request = SafeFloorRequest(draft);
        await using (var first = SafeFloorScope(run)) await FloorDecisionService(first, new FloorDecisionAuthority())
            .ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata("SENTETIK-floor-query-fence"));
        foreach (var cancel in new[] { false, true })
        {
            var probe = new FloorHistoryReadProbe(); await using var safe = SafeFloorScope(run, probe);
            using var cancellation = new CancellationTokenSource(); var authority = new FloorDecisionAuthority(); var fired = false;
            probe.AfterHistory = () => { fired = true; if (cancel) cancellation.Cancel(); else authority.ApproveGranted = false; };
            var service = FloorDecisionService(safe, authority);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadPriceFloorOriginalVersionAsync(draft.Id, request.ExpectedVersion, _ => Task.CompletedTask, cancellation.Token));
            else { var read = await service.ReadPriceFloorOriginalVersionAsync(draft.Id, request.ExpectedVersion, _ => Task.CompletedTask); Assert.Equal(SalesPriceFloorDecisionReadStateV1.Denied, read.State); Assert.Null(read.Receipt); }
            Assert.True(fired); Assert.Equal(1, probe.HistoryReads); Assert.Equal(0, probe.CurrentDraftReads);
            Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        }
        Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
    }

    private static SalesPriceFloorExceptionWorkInboxSourceV1 FloorRelationalSource(FloorScope scope, FloorDecisionAuthority authority,
        GoymenERP.Modules.Sales.Application.SalesDraftService service) => new(new SalesWorkInboxStoreV1(scope.Db), scope.Company, scope.User, authority, service);
    private sealed class FloorHistoryReadProbe : DbCommandInterceptor
    {
        internal int HistoryReads; internal int CurrentDraftReads; internal Action? AfterHistory;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DraftAttempts", StringComparison.Ordinal) && command.CommandText.Contains("AuditEvents", StringComparison.Ordinal))
            { HistoryReads++; AfterHistory?.Invoke(); }
            else if (command.CommandText.Contains("FROM [sales].[Drafts]", StringComparison.Ordinal)) CurrentDraftReads++;
            return ValueTask.FromResult(result);
        }
    }
}
