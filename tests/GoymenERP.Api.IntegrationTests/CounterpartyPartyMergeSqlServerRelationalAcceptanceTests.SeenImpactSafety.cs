using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class CounterpartyPartyMergeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Seen_merge_holding_account_change_refuses_without_consuming_key_then_refresh_merges_once_with_native_hash_and_audit()
    {
        var probe = new MergePurposeProbe();
        await using var test = new MergeTest(true, services => ConfigureMergePurpose(services, probe));
        using var client = test.Client(); using var otherCompany = test.Client(TestCompanyIds.CompanyB);
        var survivor = await CreatePartyAsync(client, "SENTETIK seen survivor"); var merged = await CreatePartyAsync(client, "SENTETIK seen duplicate");
        probe.Actor = await MergeActorAsync(test, survivor);
        await using var scope = test.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var service = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>());
        var command = new ApproveCounterpartyPartyMerge(merged, await VersionAsync(client, survivor), await VersionAsync(client, merged), "SENTETIK seen merge");
        var preview = await service.PreviewPurposeAsync(survivor, new(merged, command.ExpectedVersion, command.MergedPartyExpectedVersion), _ => Task.CompletedTask);
        Assert.NotNull(preview.SeenImpactDigest);
        // Actual native account-holding writer, not a fake membership flag or changed party version.
        await LinkAsync(otherCompany, merged);
        var metadata = MergePurposeMetadata(probe.Actor!, Guid.NewGuid().ToString("N"));
        var stale = await service.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest, metadata, probe.Consumer);
        Assert.Equal(CounterpartyPartyMergePurposeStatusV1.Changed, stale.Status); Assert.Null(stale.Execution);
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", merged));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]='counterparty.party.merge.approve.v1';", merged));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [Operation]='ApproveMerge';", merged));
        var current = await service.PreviewPurposeAsync(survivor, new(merged, command.ExpectedVersion, command.MergedPartyExpectedVersion), _ => Task.CompletedTask);
        Assert.NotNull(current.SeenImpactDigest); Assert.NotEqual(preview.SeenImpactDigest, current.SeenImpactDigest);
        var result = await service.ApprovePurposeAsync(survivor, command, current.SeenImpactDigest, metadata, probe.Consumer);
        Assert.Equal(204, result.Execution!.Response.StatusCode);
        var stored = await scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>().CommandExecutions.AsNoTracking()
            .SingleAsync(x => x.OriginalCorrelationId == metadata.CorrelationId);
        Assert.Equal(IdempotencyHash.ComputePayload(new
        {
            SurvivorPartyId = survivor, MergedPartyId = merged,
            ExpectedVersion = long.Parse(command.ExpectedVersion, System.Globalization.CultureInfo.InvariantCulture),
            MergedPartyExpectedVersion = long.Parse(command.MergedPartyExpectedVersion, System.Globalization.CultureInfo.InvariantCulture),
            Reason = command.Reason.Trim(),
        }), stored.RequestHash);
        Assert.DoesNotContain("SeenImpact", stored.ResponseJson, StringComparison.Ordinal);
        var nativeReplay = await scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>().ApproveMergeAsync(survivor, command, metadata with { CorrelationId = "SENTETIK-native-replay" }, default);
        Assert.True(nativeReplay.IsReplay); Assert.Equal(metadata.CorrelationId, nativeReplay.OriginalCorrelationId);
        var replay = await service.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest, metadata with { CorrelationId = "SENTETIK-purpose-replay" }, probe.Consumer);
        Assert.Equal(CounterpartyPartyMergePurposeStatusV1.Unavailable, replay.Status); Assert.Null(replay.Execution);
        var provenReplay = await service.ApprovePurposeAsync(survivor, command, current.SeenImpactDigest, metadata with { CorrelationId = "SENTETIK-proven-replay" }, probe.Consumer);
        Assert.True(provenReplay.Execution!.IsReplay); Assert.Equal(metadata.CorrelationId, provenReplay.Execution.OriginalCorrelationId);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", merged));
        Assert.Equal(2, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event;", merged));
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var row = Assert.Single(await audit.AuditEvents.AsNoTracking().Where(x => x.Operation == "ApproveMerge").ToListAsync());
        using var json = System.Text.Json.JsonDocument.Parse(row.AfterJson);
        Assert.Equal(current.SeenImpactDigest, json.RootElement.GetProperty("SeenImpactDigest").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("SeenImpact").GetProperty("HoldingAccounts").GetArrayLength());
    }

    [SqlServerFact]
    public async Task Seen_merge_actual_audit_save_and_precommit_authority_loss_roll_back_then_original_key_changes_once()
    {
        var probe = new MergePurposeProbe();
        await using var test = new MergeTest(true, services => ConfigureMergePurpose(services, probe)); using var client = test.Client();
        foreach (var phase in new[] { "afterAudit", "afterSave", "preCommit" })
        {
            probe.Actor = null;
            var survivor = await CreatePartyAsync(client, "SENTETIK phase survivor"); var merged = await CreatePartyAsync(client, "SENTETIK phase duplicate");
            probe.Actor = await MergeActorAsync(test, survivor);
            await using var scope = test.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var service = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>());
            var command = new ApproveCounterpartyPartyMerge(merged, "1", "1", "SENTETIK phase");
            var preview = await service.PreviewPurposeAsync(survivor, new(merged, "1", "1"), _ => Task.CompletedTask); Assert.NotNull(preview.SeenImpactDigest);
            var metadata = MergePurposeMetadata(probe.Actor!, Guid.NewGuid().ToString("N"));
            probe.Arm(phase);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest, metadata, probe.Consumer));
            Assert.True(probe.Fired); Assert.Equal(phase, probe.FiredPhase); Assert.True(probe.Discards > 0);
            Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
            Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [MergedIntoPartyId] IS NOT NULL;", merged));
            Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [Operation]='ApproveMerge' AND [EntityId]=CONVERT(nvarchar(36),@Party);", merged));
            var db = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            Assert.False(await db.CommandExecutions.AsNoTracking().AnyAsync(x => x.OriginalCorrelationId == metadata.CorrelationId));
            Assert.False(await db.OutboxMessages.AsNoTracking().AnyAsync(x => x.CorrelationId == metadata.CorrelationId));
            Assert.False(db.ChangeTracker.HasChanges());
            probe.Arm(null);
            var retry = await service.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest, metadata, probe.Consumer);
            Assert.Equal(204, retry.Execution!.Response.StatusCode);
            var replay = await service.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest, metadata, probe.Consumer);
            Assert.True(replay.Execution!.IsReplay);
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
        }
    }

    [SqlServerFact]
    public async Task Seen_merge_postcommit_authority_loss_is_unknown_release_not_a_rollback_and_old_key_replays_without_second_effect()
    {
        var probe = new MergePurposeProbe(); await using var test = new MergeTest(true, services => ConfigureMergePurpose(services, probe)); using var client = test.Client();
        var survivor = await CreatePartyAsync(client, "SENTETIK durable survivor"); var merged = await CreatePartyAsync(client, "SENTETIK durable duplicate");
        probe.Actor = await MergeActorAsync(test, survivor);
        await using var scope = test.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var service = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>());
        var preview = await service.PreviewPurposeAsync(survivor, new(merged, "1", "1"), _ => Task.CompletedTask); Assert.NotNull(preview.SeenImpactDigest);
        var metadata = MergePurposeMetadata(probe.Actor!, Guid.NewGuid().ToString("N")); var command = new ApproveCounterpartyPartyMerge(merged, "1", "1", "SENTETIK durable");
        probe.Arm("afterCommit");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest, metadata, probe.Consumer));
        Assert.True(probe.Fired); Assert.True(probe.Committed);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
        probe.Arm(null);
        var replay = await service.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest, metadata with { CorrelationId = "SENTETIK late reply" }, probe.Consumer);
        Assert.True(replay.Execution!.IsReplay); Assert.Equal(metadata.CorrelationId, replay.Execution.OriginalCorrelationId);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
    }

    [SqlServerFact]
    public async Task Seen_merge_serializable_holding_range_blocks_a_real_other_session_native_account_link_until_release()
    {
        var probe = new MergePurposeProbe(); await using var test = new MergeTest(true, services => ConfigureMergePurpose(services, probe));
        using var client = test.Client(); using var otherCompany = test.Client(TestCompanyIds.CompanyB);
        var survivor = await CreatePartyAsync(client, "SENTETIK range survivor"); var merged = await CreatePartyAsync(client, "SENTETIK range duplicate");
        probe.Actor = await MergeActorAsync(test, survivor);
        await using var scope = test.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var service = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>());
        var preview = await service.PreviewPurposeAsync(survivor, new(merged, "1", "1"), _ => Task.CompletedTask); Assert.NotNull(preview.SeenImpactDigest);
        probe.Arm("range");
        var first = service.ApprovePurposeAsync(survivor, new(merged, "1", "1", "SENTETIK range"), preview.SeenImpactDigest,
            MergePurposeMetadata(probe.Actor!, Guid.NewGuid().ToString("N")), probe.Consumer);
        await probe.RangeHeld.Task.WaitAsync(TimeSpan.FromSeconds(20));
        // This second request uses the real native account-link writer and a distinct SQL session.
        // Do not copy the purpose current-user override into that request.
        var purposeActor = probe.Actor;
        probe.Actor = null;
        var link = LinkAsync(otherCompany, merged);
        try
        {
            await probe.LinkInsertStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.NotSame(link, await Task.WhenAny(link, Task.Delay(TimeSpan.FromMilliseconds(250))));
        }
        finally
        {
            probe.Actor = purposeActor;
            probe.ReleaseRange.TrySetResult();
        }
        var mergedResult = await first;
        Assert.Equal(204, mergedResult.Execution!.Response.StatusCode);
        // The independent link passed its native active-party check before blocking at INSERT. It executes
        // after the merge TX releases; it is not retroactively part of that merge's seen membership.
        await link;
        Assert.Equal(3, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [PartyId]=@Party OR [PartyId]=@Other;", survivor, merged));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event;", merged));
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var seen = Assert.Single(await audit.AuditEvents.AsNoTracking().Where(x => x.Operation == "ApproveMerge").ToListAsync());
        using var seenJson = System.Text.Json.JsonDocument.Parse(seen.AfterJson);
        Assert.Equal(2, seenJson.RootElement.GetProperty("SeenImpact").GetProperty("HoldingAccounts").GetArrayLength());
    }

    [SqlServerFact]
    public async Task Legacy_native_merge_receipt_cannot_certify_seen_impact_but_its_original_native_replay_remains_204()
    {
        var probe = new MergePurposeProbe(); await using var test = new MergeTest(true, services => ConfigureMergePurpose(services, probe)); using var client = test.Client();
        var survivor = await CreatePartyAsync(client, "SENTETIK legacy survivor"); var merged = await CreatePartyAsync(client, "SENTETIK legacy duplicate");
        probe.Actor = await MergeActorAsync(test, survivor);
        await using var scope = test.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var native = scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>();
        var purpose = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(native);
        var before = await purpose.PreviewPurposeAsync(survivor, new(merged, "1", "1"), _ => Task.CompletedTask); Assert.NotNull(before.SeenImpactDigest);
        var command = new ApproveCounterpartyPartyMerge(merged, "1", "1", "SENTETIK legacy"); var metadata = MergePurposeMetadata(probe.Actor!, Guid.NewGuid().ToString("N"));
        var first = await native.ApproveMergeAsync(survivor, command, metadata, default); Assert.Equal(204, first.Response.StatusCode);
        var unproven = await purpose.ApprovePurposeAsync(survivor, command, before.SeenImpactDigest, metadata, probe.Consumer);
        Assert.Equal(CounterpartyPartyMergePurposeStatusV1.Unavailable, unproven.Status); Assert.Null(unproven.Execution);
        var replay = await native.ApproveMergeAsync(survivor, command, metadata with { CorrelationId = "SENTETIK legacy again" }, default);
        Assert.Equal(204, replay.Response.StatusCode); Assert.True(replay.IsReplay); Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [Operation]='ApproveMerge' AND [EntityId]=CONVERT(nvarchar(36),@Party);", merged));
    }

    [SqlServerFact]
    public async Task Seen_merge_refuses_borrowed_dirty_and_pending_audit_callers_without_owning_or_discarding_their_state()
    {
        var probe = new MergePurposeProbe(); await using var test = new MergeTest(true, services => ConfigureMergePurpose(services, probe)); using var client = test.Client();
        var survivor = await CreatePartyAsync(client, "SENTETIK caller survivor"); var merged = await CreatePartyAsync(client, "SENTETIK caller duplicate");
        probe.Actor = await MergeActorAsync(test, survivor);
        foreach (var entry in new[] { "borrowed", "dirty", "audit", "afterFence-dirty", "afterFence-audit" })
        {
            await using var scope = test.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var service = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>());
            var db = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var preview = await service.PreviewPurposeAsync(survivor, new(merged, "1", "1"), _ => Task.CompletedTask); Assert.NotNull(preview.SeenImpactDigest);
            var metadata = MergePurposeMetadata(probe.Actor!, Guid.NewGuid().ToString("N"));
            await using var borrowed = entry == "borrowed" ? await db.Database.BeginTransactionAsync() : null;
            var pending = new Party(Guid.NewGuid(), PartyCode.FromSequence(999999), PartyKind.LegalEntity, "SENTETIK caller owned", null);
            if (entry == "dirty") db.Parties.Add(pending);
            if (entry == "audit") scope.ServiceProvider.GetRequiredService<IAuditEventCollector>().Enqueue(new AuditEventDraft(
                metadata.CompanyId, "SENTETIK.caller.v1", "Counterparty.Party", survivor.ToString("D"), "Read", null, null,
                AuditActorTypes.User, metadata.ActorId, ErpPermissions.CounterpartyPartiesRead, [], "{}", "{}", "SENTETIK caller draft",
                null, AuditAiInvolvement.None, null, "Web", null, null, metadata.CorrelationId, null, metadata.IdempotencyKey));
            var discards = probe.Discards;
            var enteredConsumer = false;
            var result = await service.ApprovePurposeAsync(survivor, new(merged, "1", "1", "SENTETIK caller"), preview.SeenImpactDigest, metadata, token =>
            {
                if (!enteredConsumer && entry == "afterFence-dirty") db.Parties.Add(pending);
                if (!enteredConsumer && entry == "afterFence-audit") scope.ServiceProvider.GetRequiredService<IAuditEventCollector>().Enqueue(new AuditEventDraft(
                    metadata.CompanyId, "SENTETIK.caller.v1", "Counterparty.Party", survivor.ToString("D"), "Read", null, null,
                    AuditActorTypes.User, metadata.ActorId, ErpPermissions.CounterpartyPartiesRead, [], "{}", "{}", "SENTETIK caller draft",
                    null, AuditAiInvolvement.None, null, "Web", null, null, metadata.CorrelationId, null, metadata.IdempotencyKey));
                enteredConsumer = true; return probe.Consumer(token);
            });
            Assert.Equal(CounterpartyPartyMergePurposeStatusV1.Unavailable, result.Status); Assert.Null(result.Execution);
            Assert.Equal(discards, probe.Discards);
            if (entry == "borrowed") { Assert.Same(borrowed, db.Database.CurrentTransaction); Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State); }
            if (entry is "dirty" or "afterFence-dirty") Assert.Equal(EntityState.Added, db.Entry(pending).State);
            if (entry is "audit" or "afterFence-audit") Assert.True(writer.HasPendingDrafts);
            // Only this synthetic caller releases its own work after the preservation assertions.
            if (borrowed is not null) await borrowed.RollbackAsync();
            db.ChangeTracker.Clear(); writer.DiscardPending();
        }
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
    }

    [SqlServerFact]
    public async Task Controlled_seen_merge_1205_1222_aborts_once_then_reads_only_the_original_key_without_mutation_retry()
    {
        var probe = new MergePurposeProbe(); await using var test = new MergeTest(true, services => ConfigureMergePurpose(services, probe)); using var client = test.Client();
        foreach (var number in new[] { 1205, 1222 })
        foreach (var winner in new[] { false, true })
        {
            probe.Actor = null;
            var survivor = await CreatePartyAsync(client, "SENTETIK race survivor"); var merged = await CreatePartyAsync(client, "SENTETIK race duplicate");
            probe.Actor = await MergeActorAsync(test, survivor);
            await using var scope = test.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var service = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>());
            var preview = await service.PreviewPurposeAsync(survivor, new(merged, "1", "1"), _ => Task.CompletedTask); Assert.NotNull(preview.SeenImpactDigest);
            var metadata = MergePurposeMetadata(probe.Actor!, Guid.NewGuid().ToString("N")); var command = new ApproveCounterpartyPartyMerge(merged, "1", "1", "SENTETIK provider race");
            probe.Arm("provider"); probe.SqlNumber = number;
            if (winner) probe.AfterAbort = async () =>
            {
                // A separate real owner command is admitted only after the losing context has positively cleaned up.
                await using var winnerScope = test.Factory.Services.CreateAsyncScope(); winnerScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var winnerOwner = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(winnerScope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>());
                var accepted = await winnerOwner.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest,
                    metadata with { CorrelationId = "SENTETIK-original-winner-" + metadata.IdempotencyKey }, _ => Task.CompletedTask);
                Assert.Equal(204, accepted.Execution!.Response.StatusCode);
            };
            var result = await service.ApprovePurposeAsync(survivor, command, preview.SeenImpactDigest, metadata, probe.Consumer);
            Assert.Equal(1, probe.Injections); Assert.Equal(winner ? 2 : 1, probe.SaveAttempts);
            Assert.Equal(winner ? CounterpartyPartyMergePurposeStatusV1.Executed : CounterpartyPartyMergePurposeStatusV1.Unavailable, result.Status);
            if (winner)
            {
                Assert.True(result.Execution!.IsReplay);
                Assert.Equal("SENTETIK-original-winner-" + metadata.IdempotencyKey, result.Execution.OriginalCorrelationId);
            }
            else Assert.Null(result.Execution);
            Assert.Equal(winner ? 1 : 0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
            Assert.Equal(winner ? 1 : 0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [Operation]='ApproveMerge' AND [EntityId]=CONVERT(nvarchar(36),@Party);", merged));
            var db = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Equal(winner ? 1 : 0, await db.CommandExecutions.AsNoTracking().CountAsync(x => x.OriginalCorrelationId == "SENTETIK-original-winner-" + metadata.IdempotencyKey));
            Assert.False(await db.CommandExecutions.AsNoTracking().AnyAsync(x => x.OriginalCorrelationId == metadata.CorrelationId));
            probe.Arm(null);
        }
        // These are controlled provider failures with real rollback/winner reads, not a real two-session deadlock claim.
    }

    [SqlServerFact]
    public async Task Seen_merge_exact_audit_reader_borrows_caller_handles_and_cancelled_read_preserves_caller_work()
    {
        var probe = new MergePurposeProbe(); await using var test = new MergeTest(true, services => ConfigureMergePurpose(services, probe)); using var client = test.Client();
        var survivor = await CreatePartyAsync(client, "SENTETIK audit owner survivor"); var merged = await CreatePartyAsync(client, "SENTETIK audit owner duplicate");
        probe.Actor = await MergeActorAsync(test, survivor);
        await using var scope = test.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var service = Assert.IsAssignableFrom<ICounterpartyPartyMergePurposeV1>(scope.ServiceProvider.GetRequiredService<ICounterpartyPartyMergeService>());
        var preview = await service.PreviewPurposeAsync(survivor, new(merged, "1", "1"), _ => Task.CompletedTask); Assert.NotNull(preview.SeenImpactDigest);
        var metadata = MergePurposeMetadata(probe.Actor!, Guid.NewGuid().ToString("N"));
        var accepted = await service.ApprovePurposeAsync(survivor, new(merged, "1", "1", "SENTETIK audit owner"), preview.SeenImpactDigest, metadata, _ => Task.CompletedTask);
        Assert.Equal(204, accepted.Execution!.Response.StatusCode);
        var db = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>(); db.ChangeTracker.Clear();
        await using var caller = await db.Database.BeginTransactionAsync();
        var providerTransaction = caller.GetDbTransaction(); var connection = providerTransaction.Connection;
        Assert.NotNull(connection);
        var pending = new Party(Guid.NewGuid(), PartyCode.FromSequence(999998), PartyKind.LegalEntity, "SENTETIK pending caller", null); db.Parties.Add(pending);
        var reader = scope.ServiceProvider.GetRequiredService<ICounterpartyMergePurposeAuditReadV1>();
        var selector = new CounterpartyMergePurposeAuditSelectorV1(metadata.CompanyId, merged, metadata.ActorId, metadata.ActorId,
            metadata.CorrelationId, IdempotencyHash.ComputeKey(metadata.IdempotencyKey));
        Assert.NotNull(await reader.ReadAsync(selector, _ => Task.CompletedTask, providerTransaction));
        AssertCallerOwned();
        using var cancelled = new CancellationTokenSource(); var callbacks = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(selector, _ =>
        { if (++callbacks == 3) cancelled.Cancel(); return Task.CompletedTask; }, providerTransaction, cancelled.Token));
        Assert.Equal(3, callbacks); AssertCallerOwned();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(selector with { CompanyId = TestCompanyIds.CompanyB }, _ => Task.CompletedTask, providerTransaction));
        AssertCallerOwned();
        await caller.RollbackAsync(); await caller.DisposeAsync(); db.ChangeTracker.Clear();
        Assert.False(await db.Parties.AsNoTracking().AnyAsync(x => x.Id == pending.Id));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", merged));
        void AssertCallerOwned()
        {
            Assert.Same(caller, db.Database.CurrentTransaction); Assert.Same(connection, providerTransaction.Connection);
            Assert.Equal(ConnectionState.Open, connection.State); Assert.Equal(EntityState.Added, db.Entry(pending).State);
        }
    }

    private static Task<string> MergeActorAsync(MergeTest test, Guid survivor) => test.ScalarAsync<string>(
        "SELECT TOP(1) [ActorId] FROM [audit].[AuditEvents] WHERE [EntityId]=CONVERT(nvarchar(36),@Party) ORDER BY [OccurredAtUtc];", survivor);
    private static CounterpartyCommandMetadata MergePurposeMetadata(string actor, string key) => new(TestCompanyIds.CompanyA,
        actor, "SENTETIK-merge-" + key, null, "Web", null, null, key);

    private sealed class MergePurposeProbe
    {
        internal string? Actor; internal string? Phase; internal string? FiredPhase;
        internal bool Saved; internal bool AuditWritten; internal bool Committed; internal bool Denied; internal bool Fired; internal bool RangeRead;
        internal int SavedFences; internal int Discards; internal bool ReceiptRead; internal int ReceiptFences;
        internal int SqlNumber; internal int Injections; internal int SaveAttempts; internal Func<Task>? AfterAbort;
        internal TaskCompletionSource RangeHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseRange = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource LinkInsertStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm(string? phase) { Phase = phase; Saved = AuditWritten = Committed = Denied = Fired = RangeRead = ReceiptRead = false; SavedFences = Discards = ReceiptFences = SqlNumber = Injections = SaveAttempts = 0; FiredPhase = null; AfterAbort = null; }
        internal async Task Consumer(CancellationToken token)
        {
            if (Discards > 0 && AfterAbort is { } createWinner) { AfterAbort = null; await createWinner(); }
            if (Phase == "range" && RangeRead && !Fired)
            { Fired = true; RangeHeld.TrySetResult(); await ReleaseRange.Task.WaitAsync(token); return; }
            if (Saved) SavedFences++;
            if (ReceiptRead) ReceiptFences++;
            // Actual original-artifact SELECT -> Audit after-query fence 1 -> Audit all-company fence 2
            // -> Counterparty after-port fence 3 -> owned precommit fence 4. Earlier saves are not called precommit.
            if (!Fired && (Phase == "afterAudit" && AuditWritten || Phase == "afterSave" && Saved
                || Phase == "preCommit" && Saved && ReceiptRead && ReceiptFences == 4 || Phase == "afterCommit" && Committed))
            { Fired = Denied = true; FiredPhase = Phase; }
        }
    }
    private sealed class MergePurposeUser(ICurrentUser inner, MergePurposeProbe probe) : ICurrentUser
    {
        public bool IsAuthenticated => probe.Actor is not null || inner.IsAuthenticated;
        public string? UserId => probe.Actor ?? inner.UserId;
        public IReadOnlySet<Guid> AllowedCompanyIds => probe.Actor is null ? inner.AllowedCompanyIds : new HashSet<Guid> { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB };
        public bool CanAccessCompany(Guid company) => probe.Actor is null ? inner.CanAccessCompany(company) : AllowedCompanyIds.Contains(company);
    }
    private sealed class MergePurposeAuthority(IErpAuthorityRevalidationService inner, MergePurposeProbe probe) : IErpAuthorityRevalidationService
    {
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { var actual = await inner.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken); return probe.Denied ? PermissionScope.Denied : actual; }
    }
    private sealed class MergePurposeAudit(IAuditTransactionWriterV1 inner, MergePurposeProbe probe) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { await inner.WritePendingAsync(transaction, cancellationToken); if (probe.Phase is not null) probe.AuditWritten = true; }
        public void DiscardPending() { probe.Discards++; inner.DiscardPending(); }
    }
    private sealed class MergePurposeSave(MergePurposeProbe probe) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (probe.Phase == "provider" && eventData.Context!.ChangeTracker.Entries<PartyMerge>().Any(x => x.State == EntityState.Added))
            {
                probe.SaveAttempts++;
                if (probe.Injections++ == 0)
                {
                    var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(probe.SqlNumber);
                    if (probe.SqlNumber == 1222) throw new DbUpdateException("SENTETIK wrapped lock timeout", error);
                    throw error;
                }
                probe.Injections--; // Count actual injections, not the separate real winner's successful save.
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (probe.Phase is not null) probe.Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class MergePurposeTransaction(MergePurposeProbe probe) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (probe.Phase is not null) probe.Committed = true; return Task.CompletedTask; }
    }
    private sealed class MergePurposeRead(MergePurposeProbe probe) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (probe.Phase == "range" && command.CommandText.Contains("INSERT INTO [counterparty].[CompanyPartyAccounts]", StringComparison.Ordinal))
                probe.LinkInsertStarted.TrySetResult();
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (probe.Phase == "preCommit" && probe.Saved && command.CommandText.Contains("FROM [audit].[AuditEvents]", StringComparison.Ordinal)) probe.ReceiptRead = true;
            if (probe.Phase == "range" && command.Transaction?.IsolationLevel == IsolationLevel.Serializable
                && command.CommandText.Contains("FROM [counterparty].[CompanyPartyAccounts]", StringComparison.Ordinal)
                && command.CommandText.Contains("[ContactInformationJson]", StringComparison.Ordinal)) probe.RangeRead = true;
            return ValueTask.FromResult(result);
        }
    }
    private static void ConfigureMergePurpose(IServiceCollection services, MergePurposeProbe probe)
    {
        services.AddDbContext<CounterpartyDbContext>((_, options) => options.AddInterceptors(new MergePurposeSave(probe), new MergePurposeTransaction(probe), new MergePurposeRead(probe)));
        services.AddDbContext<AuditDbContext>((_, options) => options.AddInterceptors(new MergePurposeRead(probe)));
        Decorate<ICurrentUser>(inner => new MergePurposeUser(inner, probe));
        Decorate<IErpAuthorityRevalidationService>(inner => new MergePurposeAuthority(inner, probe));
        Decorate<IAuditTransactionWriterV1>(inner => new MergePurposeAudit(inner, probe));
        void Decorate<T>(Func<T, T> wrap) where T : class
        {
            var descriptor = services.Last(x => x.ServiceType == typeof(T)); services.Remove(descriptor);
            services.AddScoped<T>(provider => wrap((T)(descriptor.ImplementationFactory?.Invoke(provider) ?? descriptor.ImplementationInstance
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))));
        }
    }
}
