using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesPriceFloorApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Selected_floor_genuine_original_receipt_replays_after_commercial_revision_with_separate_price_withholding()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run);
        var request = SafeFloorRequest(draft); const string key = "SENTETIK-floor-purpose-original";
        SalesPriceFloorOriginalReceiptV1 original;
        await using (var safe = SafeFloorScope(run))
        {
            var authority = new FloorDecisionAuthority(); var service = FloorDecisionService(safe, authority);
            var read = await service.ReadPriceFloorDecisionAsync(draft.Id, _ => Task.CompletedTask);
            Assert.Equal(SalesPriceFloorDecisionReadStateV1.Found, read.State); Assert.True(read.Detail!.ApprovalSupported);
            Assert.Equal(draft.Version.ToString(CultureInfo.InvariantCulture), read.Detail.Version);
            Assert.Equal("90", Assert.Single(read.Detail.Lines).Pricing!.NetUnitPrice);
            Assert.DoesNotContain(SalesPermissionManifestV1.DraftsRead, authority.Measured);
            var first = await service.ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata(key));
            original = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(first.OriginalFloorApprovalReceipt);
            Assert.Equal((draft.Version, draft.Version + 1, SalesDraftRequestHash.Compute(SalesDraftCommands.ApprovePriceFloorExceptionsV1, draft.Id, request)),
                (original.ExpectedVersion, original.ResultVersion, original.PayloadHash));
            Assert.Equal(SalesPriceFloorDecisionDigestsV1.CurrentResponseDigest(first), original.CurrentResponseDigest);
            using var wire = JsonDocument.Parse(JsonSerializer.Serialize(first, SalesDraftJson.Options));
            Assert.Equal(3, wire.RootElement.EnumerateObject().Count()); Assert.False(wire.RootElement.TryGetProperty("originalFloorApprovalReceipt", out _));
        }
        await using (var raw = run.Store(Company))
        {
            var current = await raw.Store.FindDraftAsync(draft.Id); Assert.NotNull(current); var line = Assert.Single(current.Lines);
            await raw.Store.ReviseDraftCommercialAsync(Envelope("SENTETIK-purpose-reprice", Requester), Hash("purpose-reprice"), SalesDraftCommands.ReviseCommercialV1,
                draft.Id, (actual, now) => actual.ReviseCommercial(actual.Version,
                    [new LineCommercialInput(line.Id, line.Commercial with { UnitPrice = "91" })], Requester, now,
                    SalesDiscountLimit.Defined(10m, Guid.Parse("4d15c0a1-0000-4000-8000-0000000000c1"), 1)), Priced, _ => Task.CompletedTask);
        }
        await using (var safe = SafeFloorScope(run))
        {
            var authority = new FloorDecisionAuthority { PriceGranted = false }; var service = FloorDecisionService(safe, authority);
            var current = await service.ReadPriceFloorDecisionAsync(draft.Id, _ => Task.CompletedTask);
            Assert.Equal((draft.Version + 2).ToString(CultureInfo.InvariantCulture), current.Detail!.Version);
            Assert.All(current.Detail.Lines, x => Assert.Null(x.Pricing));
            var replay = await service.ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata(key, "later"));
            Assert.True(replay.IsReplay); Assert.All(replay.Detail.Lines, x => { Assert.Equal(SalesPriceDisclosure.Withheld, x.PriceDisclosure); Assert.Null(x.UnitPrice); Assert.Null(x.PriceUnitId); Assert.Null(x.DiscountPercent); });
            var receipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(replay.OriginalFloorApprovalReceipt);
            Assert.Equal(original.OriginalDecisionDigest, receipt.OriginalDecisionDigest); Assert.Equal(original.OriginalResultDigest, receipt.OriginalResultDigest);
            Assert.Equal(original.ResultVersion.ToString(CultureInfo.InvariantCulture), replay.Detail.Draft.Version);
            Assert.Equal("90", Assert.Single(receipt.Lines).After.UnitPrice);
            Assert.Equal(SalesPriceFloorDecisionDigestsV1.CurrentResponseDigest(replay), receipt.CurrentResponseDigest);
        }
        Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Selected_floor_receipt_attempt_and_Audit_read_losses_abort_before_commit_and_same_key_retries_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "attempt", "audit" }) foreach (var cancel in new[] { false, true })
        {
            var draft = await SeedSafeFloor(run); var key = $"SENTETIK-floor-read-{phase}-{cancel}";
            var saved = new FloorSaveProbe(); var probe = new FloorReceiptReadProbe();
            await using (var safe = SafeFloorScope(run, saved, extraInterceptor: probe))
            {
                using var cancellation = new CancellationTokenSource(); var authority = new FloorDecisionAuthority(); var fired = false;
                probe.AfterRead = table =>
                {
                    // Match only receipt reads AFTER the real Sales Save accepted the attempt. Native early replay reads are excluded.
                    if (!saved.Saved || table != phase) return;
                    fired = true; if (cancel) cancellation.Cancel(); else authority.ApproveGranted = false;
                };
                var service = FloorDecisionService(safe, authority);
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key), cancellation.Token));
                else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key)))).Status);
                Assert.True(fired); Assert.True(saved.Saved); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
                Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await FloorDecisionCount(run, draft.Id)); Assert.Equal(0, await FloorAuditCount(run, draft.Id));
            Assert.Equal(draft.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
            await using var retry = SafeFloorScope(run); var allowed = FloorDecisionService(retry, new FloorDecisionAuthority());
            Assert.NotNull((await allowed.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key))).OriginalFloorApprovalReceipt);
            Assert.True((await allowed.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key))).IsReplay);
            Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Selected_floor_final_release_withholds_newly_lost_Prices_and_never_upgrades_the_first_withheld_answer()
    {
        await using var run = await Run.StartAsync();
        foreach (var firstWithheld in new[] { false, true })
        {
            var draft = await SeedSafeFloor(run); var commit = new FloorCommitProbe();
            await using var safe = SafeFloorScope(run, commit);
            var authority = new FloorDecisionAuthority { PriceGranted = !firstWithheld };
            commit.AfterCommit = () => authority.PriceGranted = firstWithheld;
            var response = await FloorDecisionService(safe, authority).ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("SENTETIK-price-release-" + firstWithheld));
            Assert.All(response.Detail.Lines, x => { Assert.Equal(SalesPriceDisclosure.Withheld, x.PriceDisclosure); Assert.Null(x.UnitPrice); Assert.Null(x.PriceUnitId); Assert.Null(x.DiscountPercent); });
            var receipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(response.OriginalFloorApprovalReceipt);
            Assert.Equal(SalesPriceFloorDecisionDigestsV1.CurrentResponseDigest(response), receipt.CurrentResponseDigest);
            Assert.Equal(1, commit.Commits); Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Selected_floor_final_owner_consumer_cancellation_or_revocation_releases_no_human_facts_and_writes_nothing()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run);
        foreach (var cancel in new[] { false, true })
        {
            await using var safe = SafeFloorScope(run); var authority = new FloorDecisionAuthority(); var callbacks = 0;
            using var cancellation = new CancellationTokenSource();
            Task Fence(CancellationToken _)
            { if (++callbacks == 5) { if (cancel) cancellation.Cancel(); else authority.ApproveGranted = false; } return Task.CompletedTask; }
            var service = FloorDecisionService(safe, authority);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadPriceFloorDecisionAsync(draft.Id, Fence, cancellation.Token));
            else { var read = await service.ReadPriceFloorDecisionAsync(draft.Id, Fence); Assert.Equal(SalesPriceFloorDecisionReadStateV1.Denied, read.State); Assert.Null(read.Detail); }
            Assert.Equal(5, callbacks); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
        }
        Assert.Equal(0, await FloorDecisionCount(run, draft.Id)); Assert.Equal(0, await FloorAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Selected_floor_last_price_await_identity_drift_denies_release_after_commit_and_retries_only_the_original_answer()
    {
        await using var run = await Run.StartAsync();
        foreach (var drift in new[] { "actor", "company" })
        {
            var draft = await SeedSafeFloor(run); var request = SafeFloorRequest(draft);
            var key = "SENTETIK-final-floor-identity-" + drift; var commit = new FloorCommitProbe();
            await using var safe = SafeFloorScope(run, commit);
            var authority = new FloorDecisionAuthority(); var service = FloorDecisionService(safe, authority);
            var postCommitPriceReads = 0; var fired = false;
            authority.AfterRead = permission =>
            {
                // Actual commit happened. ReplayDisclosure's price read is first; final DiscloseAsync is second.
                if (commit.Commits == 0 || permission != SalesPermissionManifestV1.OrdersPriceRead || ++postCommitPriceReads != 2) return;
                fired = true;
                if (drift == "actor") safe.User.Actor = "SENTETIK-other-actor";
                else safe.Company.Set(Guid.Parse("8e34d0a9-0000-4000-8000-0000000000b2"));
                // Normal completion, without throwing: DiscloseAsync returns Withheld on identity drift.
            };
            var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.ApprovePriceFloorExceptionsAsync(
                draft.Id, request, SafeFloorMetadata(key)));
            Assert.Equal((403, "forbidden"), (denied.Status, denied.Code)); Assert.True(fired);
            Assert.Equal(2, postCommitPriceReads); Assert.Equal(1, commit.Commits);
            Assert.Null(safe.Db.Database.CurrentTransaction); Assert.False(safe.Db.ChangeTracker.HasChanges()); Assert.False(safe.Writer.HasPendingDrafts);
            // Release failed after commit; the native decision was not rolled back and the key remains consumed.
            Assert.Equal(draft.Version + 1, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
            Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
            authority.AfterRead = null; safe.User.Actor = Approver; safe.Company.Set(Company);
            var replay = await service.ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata(key, "SENTETIK-restored-identity"));
            Assert.True(replay.IsReplay); Assert.Equal((draft.Version + 1).ToString(CultureInfo.InvariantCulture), replay.Detail.Draft.Version);
            Assert.Equal("SENTETIK-floor-original", replay.OriginalCorrelationId); Assert.NotNull(replay.OriginalFloorApprovalReceipt);
            Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
        }
    }

    private static SalesDraftService FloorDecisionService(FloorScope safe, FloorDecisionAuthority authority) =>
        new SalesDraftService(safe.Company, safe.User, authority, new FloorRoles(), new FloorCorrelation(), null!, null!, null,
            new SalesStore(safe.Db, safe.Company, safe.Collector, TimeProvider.System, officialCancellationAuditWriter: safe.Writer),
            new SalesDraftServiceBindings(), TimeProvider.System).WithPriceFloorApprovalSafety();
    private sealed class FloorDecisionAuthority : IErpAuthorityRevalidationService
    {
        internal bool ApproveGranted = true; internal bool PriceGranted = true; internal List<string> Measured = [];
        internal Action<string>? AfterRead;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.Equal(Company, companyId); Measured.Add(permission);
            var granted = permission == SalesPermissionManifestV1.OrdersPriceRead ? PriceGranted
                : permission == SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove && ApproveGranted;
            AfterRead?.Invoke(permission);
            return Task.FromResult(new PermissionScope(granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
    private sealed class FloorReceiptReadProbe : DbCommandInterceptor
    {
        internal Action<string>? AfterRead;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT", StringComparison.Ordinal)
                && !command.CommandText.Contains("INSERT", StringComparison.Ordinal))
            {
                if (command.CommandText.Contains("DraftAttempts", StringComparison.Ordinal)) AfterRead?.Invoke("attempt");
                else if (command.CommandText.Contains("AuditEvents", StringComparison.Ordinal)) AfterRead?.Invoke("audit");
            }
            return ValueTask.FromResult(result);
        }
    }
}
