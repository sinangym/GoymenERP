using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    // One disposable actual SQL database, real native Accounting closure/source and real dispatcher/consumer/Work stores.
    // Reuses StaleCost's explicit SENTETIK cost source/account-plan/money-profile/authority inputs; no production dispatcher
    // setting is enabled. Test-only manual RunOnce uses the existing dispatcher harness's explicit scheduling values.
    [SqlServerFact]
    public async Task Actual_stale_cost_closure_flows_through_owner_outbox_and_dispatcher_to_one_preparer_notification_on_replay()
    {
        await using var run = await Run.Start();
        var period = await run.Period(); var profiles = new MutableCostProfiles();
        var reader = new RoundedCostReader(run.Source.Facts, new("1", "3"), purchase: false);
        await using var owner = run.Open(); var store = Costs(owner, reader, profiles);
        await store.AcceptAsync(reader.Envelope);
        var source = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        var prepared = await store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var originalRows = await owner.Db.InventoryCostProposals.AsNoTracking().OrderBy(x => x.Version).Select(x => x.ViewJson).ToArrayAsync();
        var auditBefore = await run.Count("audit.AuditEvents");
        profiles.Version = 2; // The existing native stale comparison, not a forged closure/envelope.
        var correlation = new CorrelationContext();
        correlation.Set("SENTETIK-YOLD40-owner-to-work", "SENTETIK-cost-source", SourceChannels.Web, null, null);
        var service = new AccountingInventoryCostServiceV1(store, owner.Posting, new StaleAuthority(), owner.CompanyContext,
            new StaleUser(Checker), correlation);
        var closed = await service.ApproveAsync(source.ProposalId, source.Kind, prepared.Version, "SENTETIK-YOLD40-stale-approval");
        Assert.Equal(AccountingApiStatus.Conflict, closed.Status);
        Assert.Equal(AccountingCostProposalStaleEventV1.ReasonCode, closed.Code);
        var proposal = Assert.IsType<AccountingInventoryCostProposalV1>(closed.Resource);
        var closure = Assert.IsType<AccountingCostProposalStaleClosureV1>(proposal.StaleClosure);
        Assert.Equal(("Stale", prepared.Version + 1, Maker), (proposal.Status, proposal.Version, closure.PreparedBy));
        Assert.Equal(prepared.Preparation, proposal.Preparation);
        Assert.Equal(1, await run.Count("accounting.Commands"));
        Assert.Equal(auditBefore + 1, await run.Count("audit.AuditEvents"));
        Assert.Equal(1, await run.Scalar($"SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId='{Company:D}' "
            + $"AND EntityId=N'{proposal.ProposalId:D}' AND EventType=N'accounting.inventory-cost.stale-closed.v1' "
            + $"AND EntityVersionBefore={prepared.Version} AND EntityVersionAfter={proposal.Version}"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(originalRows, await owner.Db.InventoryCostProposals.AsNoTracking().Where(x => x.Version <= prepared.Version)
            .OrderBy(x => x.Version).Select(x => x.ViewJson).ToArrayAsync());
        var eventSource = new AccountingCostProposalStaleOutboxSourceV1(owner.Db, owner.CompanyContext);
        var committed = Assert.Single(await eventSource.ReadAfterAsync(null, 10, default));
        Assert.Equal(closure.EventId, committed.EventId);
        Assert.Equal(closure.CorrelationId, committed.CorrelationId);
        using (var payload = JsonDocument.Parse(committed.PayloadJson))
        {
            Assert.Equal(new[] { "closedAtUtc", "companyId", "costKind", "preparedBy", "proposalId", "proposalVersion", "reasonCode", "reasonText" },
                payload.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal(Maker, payload.RootElement.GetProperty("preparedBy").GetGuid());
            Assert.Equal(proposal.ProposalId, payload.RootElement.GetProperty("proposalId").GetGuid());
        }
        Assert.DoesNotContain(prepared.Preparation!.Reason, committed.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("amount", committed.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", committed.PayloadJson, StringComparison.OrdinalIgnoreCase);
        var replay = await service.ApproveAsync(source.ProposalId, source.Kind, prepared.Version, "SENTETIK-YOLD40-stale-approval");
        Assert.Equal(AccountingApiStatus.Conflict, replay.Status); Assert.True(replay.IsReplay);
        Assert.Equal(proposal.StaleClosure, replay.Resource!.StaleClosure);
        await WorkOutboxDispatchSqlServerRelationalAcceptanceTests.DispatchActualStaleOwnerEventAsync(
            run.Factory.DatabaseConnectionString, committed, Maker);
        Assert.Equal(1, await run.Count("accounting.Commands"));
        Assert.Equal(auditBefore + 1, await run.Count("audit.AuditEvents"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(originalRows.Length + 1, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(committed, Assert.Single(await eventSource.ReadAfterAsync(null, 10, default)));
    }
}

public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    internal static async Task DispatchActualStaleOwnerEventAsync(string connectionString, WorkOutboxEventV1 committed, Guid preparer)
    {
        // The same actual SQL dispatcher construction as SqlHarness; replace its synthetic Src/Consumer with production
        // AddWorkOutboxEventSource + AddAccountingCostProposalStaleNotificationHostV1 registrations. No synthetic envelope.
        var services = new ServiceCollection();
        services.AddScoped<ICompanyContext, CompanyContext>();
        services.AddDbContext<WorkDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema)));
        services.AddDbContext<AccountingDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)));
        services.AddDbContext<OrganizationDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organization")));
        services.AddScoped<OrganizationBootstrapWriteScope>();
        services.AddScoped<OrganizationCompanyLifecycleReader>();
        services.AddScoped<ICompanyLifecycleReadPortV1>(provider => provider.GetRequiredService<OrganizationCompanyLifecycleReader>());
        services.AddScoped<IOutboxCompanyDirectoryV1>(provider => provider.GetRequiredService<OrganizationCompanyLifecycleReader>());
        // Existing NotificationServices fixture inputs. This event has no Sales/Purchasing target and no price payload.
        services.AddSingleton<NotificationClock>();
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<NotificationClock>());
        services.AddSingleton<NotificationRecipient>();
        services.AddSingleton<IWorkNotificationRecipientReaderV1>(provider => provider.GetRequiredService<NotificationRecipient>());
        services.AddSingleton<NotificationPurchaseRecipients>();
        services.AddSingleton<IWorkPurchaseNotificationRecipientReaderV1>(provider => provider.GetRequiredService<NotificationPurchaseRecipients>());
        services.AddSingleton<NotificationRetention>();
        services.AddSingleton<IWorkNotificationRetentionReaderV1>(provider => provider.GetRequiredService<NotificationRetention>());
        services.AddSingleton<NotificationUser>();
        services.AddSingleton<ICurrentUser>(provider => provider.GetRequiredService<NotificationUser>());
        services.AddSingleton<NotificationMembership>();
        services.AddSingleton<IWorkNotificationActorMembershipV1>(provider => provider.GetRequiredService<NotificationMembership>());
        services.AddWorkNotificationStorageV1();
        services.AddWorkOutboxEventSource<AccountingCostProposalStaleOutboxSourceV1>(AccountingCostProposalStaleEventV1.SourceName,
            new WorkOutboxEventTypeV1(AccountingCostProposalStaleEventV1.EventType, 1));
        services.AddAccountingCostProposalStaleNotificationHostV1();
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<NotificationClock>().Now = committed.OccurredAtUtc.AddSeconds(1);
        provider.GetRequiredService<NotificationRecipient>().Outcome = WorkNotificationRecipientOutcomeV1.Unavailable;
        provider.GetRequiredService<NotificationPurchaseRecipients>().Outcome = WorkNotificationRecipientOutcomeV1.Unavailable;
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(committed.CompanyId);
            await scope.ServiceProvider.GetRequiredService<WorkDbContext>().Database.MigrateAsync();
            var lifecycle = await scope.ServiceProvider.GetRequiredService<ICompanyLifecycleReadPortV1>().ReadAsync(committed.CompanyId);
            Assert.Equal(CompanyLifecycleStateV1.Active, Assert.IsType<CompanyLifecycleViewV1>(lifecycle).State);
        }
        var dispatcher = new WorkOutboxDispatcher(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WorkOutboxDispatcherOptions
            {
                // Manual test invocation only; no hosted service or configuration/production activation.
                Enabled = false, BatchSize = 10, MaxAttempts = 3, InitialBackoff = TimeSpan.FromSeconds(1),
                MaxBackoff = TimeSpan.FromSeconds(4), PollInterval = TimeSpan.FromSeconds(1),
            }), provider.GetRequiredService<TimeProvider>(), NullLogger<WorkOutboxDispatcher>.Instance);
        var delivered = await dispatcher.RunOnceAsync(default);
        Assert.Equal((1, 0, 0, 0), (delivered.Delivered, delivered.Retrying, delivered.Poisoned, delivered.Unrouted));
        var replay = await dispatcher.RunOnceAsync(default);
        Assert.Equal((0, 0, 0), (replay.Delivered, replay.Retrying, replay.Poisoned));
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(committed.CompanyId);
            // Read the original persisted event through the production registration adapter, then redeliver it to the
            // actual registered consumer: the notification store's own inbox must deduplicate too, independently of cursor.
            var envelope = Assert.Single(await Assert.Single(scope.ServiceProvider.GetServices<IOutboxSourceV1>()).ReadAfterAsync(null, 10, default));
            Assert.Equal((committed.EventId, committed.PayloadJson), (envelope.MessageId, envelope.PayloadJson));
            await Assert.Single(scope.ServiceProvider.GetServices<IOutboxConsumerV1>()).HandleAsync(envelope, default);
        }
        await using var database = Db(connectionString, committed.CompanyId);
        var receipt = Assert.Single(await database.NotificationInbox.AsNoTracking().ToArrayAsync());
        Assert.Equal((committed.EventId, AccountingCostProposalStaleEventV1.SourceName, committed.EventType), (receipt.EventId, receipt.SourceName, receipt.EventType));
        var notification = Assert.Single(await database.InboxNotifications.AsNoTracking().ToArrayAsync());
        Assert.Equal((preparer, (int)WorkInboxNotificationKindV1.AccountingCostProposalStale, (int)WorkInboxNotificationClassV1.Digest),
            (notification.RecipientUserId, notification.Kind, notification.Class));
        var original = Assert.IsType<AccountingCostProposalStalePayloadV1>(JsonSerializer.Deserialize<AccountingCostProposalStalePayloadV1>(
            committed.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(committed.EventId, notification.EventId); Assert.Equal(original.ProposalId, notification.SourceEntityId);
        Assert.Equal(original.ProposalVersion.ToString(CultureInfo.InvariantCulture), notification.SourceVersion);
        Assert.Equal((int)original.CostKind, notification.CostKind);
        Assert.Equal(committed.CorrelationId, notification.CorrelationId); Assert.Equal(committed.CausationId, notification.CausationId);
        Assert.Equal(committed.OccurredAtUtc, notification.OccurredAtUtc);
        Assert.Equal("Bayatladı", notification.ReasonText); Assert.Null(notification.SalesOrderId); Assert.Null(notification.PurchaseOrderId);
        var delivery = Assert.Single(await database.OutboxDeliveries.AsNoTracking().ToArrayAsync());
        Assert.Equal((committed.EventId, WorkOutboxDeliveryStatus.Delivered, 1),
            (delivery.MessageId, (WorkOutboxDeliveryStatus)delivery.Status, delivery.AttemptCount));
        var cursor = Assert.Single(await database.OutboxSourceCursors.AsNoTracking().ToArrayAsync());
        Assert.Equal((AccountingCostProposalStaleEventV1.SourceName, committed.Position), (cursor.SourceName, cursor.Position));
        await using var foreign = Db(connectionString, TestCompanyIds.CompanyB);
        Assert.Empty(await foreign.NotificationInbox.AsNoTracking().ToArrayAsync());
        Assert.Empty(await foreign.InboxNotifications.AsNoTracking().ToArrayAsync());
    }
}
