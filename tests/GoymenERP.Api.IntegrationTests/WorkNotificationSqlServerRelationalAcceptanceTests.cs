using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// Reuses the real SQL database/migration harness; owner answers are synthetic so this is storage/atomicity evidence,
// not the final Inventory -> Sales -> Work production-composition acceptance.
public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Notification_Down_allows_an_empty_store_and_preserves_delivery_dedup_and_passivation_history()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddPersonalNotifications().DownOperations[0]).Sql;
            await using var database = Db(connectionString, TestCompanyIds.CompanyA);
            await database.Database.ExecuteSqlRawAsync(guard);
            await using var provider = NotificationServices(connectionString);
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            await scope.ServiceProvider.GetRequiredService<IWorkNotificationDeliveryV1>().DeliverAsync(NotificationCommand());
            provider.GetRequiredService<NotificationClock>().Now = provider.GetRequiredService<NotificationClock>().Now.AddDays(90);
            await scope.ServiceProvider.GetRequiredService<IWorkNotificationMaintenanceV1>().PassivateExpiredAsync();
            var refusal = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(51955, refusal.Number);
            Assert.Equal(1, await database.Notifications.CountAsync());
            Assert.Equal(1, await database.NotificationInbox.CountAsync());
            Assert.Equal(1, await database.NotificationPassivations.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Revival_notification_delivers_sales_and_recorded_purchase_recipients_atomically_and_replays_without_retargeting()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = NotificationServices(connectionString);
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var delivery = scope.ServiceProvider.GetRequiredService<IWorkNotificationDeliveryV1>();
            var purchase = new WorkPurchaseNotificationTargetV1(Guid.NewGuid(), Guid.NewGuid());
            var command = NotificationCommand() with { SourceName = "purchasing", EventType = "purchasing.order-line.revived.v1",
                Kind = WorkNotificationKindV1.PurchaseSupplyRevived, SourceEntityId = purchase.PurchaseOrderLineId, PurchaseTarget = purchase };
            var owner = provider.GetRequiredService<NotificationPurchaseRecipients>();
            owner.Outcome = WorkNotificationRecipientOutcomeV1.Unassigned;
            await Assert.ThrowsAsync<InvalidOperationException>(() => delivery.DeliverAsync(command));
            Assert.Equal(0, await CountAsync(connectionString, "Notifications", TestCompanyIds.CompanyA));
            Assert.Equal(0, await CountAsync(connectionString, "NotificationInbox", TestCompanyIds.CompanyA));
            owner.Outcome = WorkNotificationRecipientOutcomeV1.Found;
            await delivery.DeliverAsync(command);
            Assert.Equal(3, await CountAsync(connectionString, "Notifications", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "NotificationInbox", TestCompanyIds.CompanyA));
            await using (var database = Db(connectionString, TestCompanyIds.CompanyA))
            {
                var rows = await database.Notifications.ToArrayAsync();
                var sales = Assert.Single(rows, row => row.SalesOrderId is not null);
                Assert.Null(sales.PurchaseOrderId);
                Assert.Equal(command.Targets[0].SalesOrderLineId, sales.SalesOrderLineId);
                var purchasing = rows.Where(row => row.PurchaseOrderId is not null).ToArray();
                Assert.Equal(owner.Ids.Order(), purchasing.Select(row => row.RecipientUserId).Order());
                Assert.All(purchasing, row => { Assert.Null(row.SalesOrderId); Assert.Equal(purchase.PurchaseOrderLineId, row.PurchaseOrderLineId); });
            }
            owner.Outcome = WorkNotificationRecipientOutcomeV1.Unavailable;
            provider.GetRequiredService<NotificationRecipient>().Outcome = WorkNotificationRecipientOutcomeV1.Unavailable;
            await delivery.DeliverAsync(command); // First immutable recipients remain; later ownership cannot retarget a replay.
            Assert.Equal(3, await CountAsync(connectionString, "Notifications", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "NotificationInbox", TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task Notification_inbox_is_atomic_idempotent_company_recipient_scoped_and_payload_bound_on_real_sql()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = NotificationServices(connectionString);
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var delivery = scope.ServiceProvider.GetRequiredService<IWorkNotificationDeliveryV1>();
            var command = NotificationCommand();
            var recipient = provider.GetRequiredService<NotificationRecipient>();
            recipient.Outcome = WorkNotificationRecipientOutcomeV1.Unassigned;
            await Assert.ThrowsAsync<InvalidOperationException>(() => delivery.DeliverAsync(command));
            Assert.Equal(0, await CountAsync(connectionString, "NotificationInbox", TestCompanyIds.CompanyA));
            Assert.Equal(0, await CountAsync(connectionString, "Notifications", TestCompanyIds.CompanyA));
            recipient.Outcome = WorkNotificationRecipientOutcomeV1.Found;
            await delivery.DeliverAsync(command);
            await delivery.DeliverAsync(command);
            Assert.Equal(1, await CountAsync(connectionString, "NotificationInbox", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "Notifications", TestCompanyIds.CompanyA));
            var poison = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => delivery.DeliverAsync(command with { ReasonText = "SENTETIK değişmiş olay" }));
            Assert.Equal("notification_event_payload_mismatch", poison.Code);
            var read = scope.ServiceProvider.GetRequiredService<IWorkNotificationReadV1>();
            Assert.Single((await read.ListMineAsync(false)).Items);
            var membership = provider.GetRequiredService<NotificationMembership>();
            membership.Calls = 0;
            membership.DenyAtCall = 3; // Initial access and post-policy check pass; final post-SQL answer is refused.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => read.ListMineAsync(false));
            Assert.Equal(3, membership.Calls);
            membership.DenyAtCall = null;
            provider.GetRequiredService<NotificationUser>().Id = Guid.NewGuid();
            Assert.Empty((await read.ListMineAsync(true)).Items);
            provider.GetRequiredService<NotificationUser>().Id = NotificationUser.RecipientId;
            provider.GetRequiredService<NotificationMembership>().Allow = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => read.ListMineAsync(true));
            await using var foreign = Db(connectionString, TestCompanyIds.CompanyB);
            Assert.Empty(await foreign.Notifications.ToArrayAsync());
        });
    }

    [SqlServerFact]
    public async Task Notification_retention_passivates_without_delete_or_reactivation_and_retains_policy_evidence_on_real_sql()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = NotificationServices(connectionString);
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            await scope.ServiceProvider.GetRequiredService<IWorkNotificationDeliveryV1>().DeliverAsync(NotificationCommand());
            var clock = provider.GetRequiredService<NotificationClock>();
            clock.Now = clock.Now.AddDays(89);
            var read = scope.ServiceProvider.GetRequiredService<IWorkNotificationReadV1>();
            Assert.Single((await read.ListMineAsync(false)).Items);
            provider.GetRequiredService<NotificationRetention>().Unavailable = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => read.ListMineAsync(false));
            provider.GetRequiredService<NotificationRetention>().Unavailable = false;
            clock.Now = clock.Now.AddDays(1);
            Assert.Empty((await read.ListMineAsync(false)).Items);
            Assert.NotNull(Assert.Single((await read.ListMineAsync(true)).Items).PassivatedAtUtc);
            provider.GetRequiredService<NotificationRetention>().Days = 365;
            Assert.Empty((await read.ListMineAsync(false)).Items);
            Assert.Equal(1, await CountAsync(connectionString, "Notifications", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "NotificationInbox", TestCompanyIds.CompanyA));
            await using var database = Db(connectionString, TestCompanyIds.CompanyA);
            var passive = Assert.Single(await database.NotificationPassivations.ToArrayAsync());
            Assert.Equal((90, "work.notification-active-days:0"), (passive.ActiveDays, passive.PolicyVersion));
        });
    }

    private static DeliverWorkNotificationV1 NotificationCommand() => new(TestCompanyIds.CompanyA, "inventory.notifications",
        Guid.NewGuid(), "inventory.future-allocation.released.v1", 1, "{\"synthetic\":true}", WorkNotificationKindV1.AllocationReleased,
        Guid.NewGuid(), "2", "acceptance-link-failed", "SENTETIK ayrım bırakıldı", DateTimeOffset.Parse("2026-11-17T00:00:00Z"),
        "SENTETIK-notification", null, [new(Guid.NewGuid(), Guid.NewGuid())]);
    private static ServiceProvider NotificationServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICompanyContext, CompanyContext>();
        services.AddDbContext<WorkDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema)));
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
        return services.BuildServiceProvider();
    }
    private sealed class NotificationClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class NotificationRecipient : IWorkNotificationRecipientReaderV1
    {
        public WorkNotificationRecipientOutcomeV1 Outcome { get; set; } = WorkNotificationRecipientOutcomeV1.Found;
        public Task<WorkNotificationRecipientV1> ReadAsync(Guid order, Guid line, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkNotificationRecipientV1(Outcome, TestCompanyIds.CompanyA, order, line,
                Outcome == WorkNotificationRecipientOutcomeV1.Found ? NotificationUser.RecipientId : null, 1));
    }
    private sealed class NotificationPurchaseRecipients : IWorkPurchaseNotificationRecipientReaderV1
    {
        public WorkNotificationRecipientOutcomeV1 Outcome { get; set; } = WorkNotificationRecipientOutcomeV1.Found;
        public Guid[] Ids { get; } = [Guid.NewGuid(), Guid.NewGuid()];
        public Task<WorkPurchaseNotificationRecipientsV1> ReadAsync(Guid companyId, Guid order, Guid line, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPurchaseNotificationRecipientsV1(Outcome, companyId, order, line, 7,
                Outcome == WorkNotificationRecipientOutcomeV1.Found ? Ids : []));
    }
    private sealed class NotificationRetention : IWorkNotificationRetentionReaderV1
    {
        public int Days { get; set; } = 90;
        public bool Unavailable { get; set; }
        public Task<WorkNotificationRetentionV1> ReadAsync(CancellationToken cancellationToken = default) => Unavailable
            ? throw new InvalidOperationException("SENTETIK policy failure")
            : Task.FromResult(new WorkNotificationRetentionV1(TestCompanyIds.CompanyA, Days, "work.notification-active-days:0"));
    }
    private sealed class NotificationUser : ICurrentUser
    {
        public static readonly Guid RecipientId = Guid.Parse("99999999-0000-4000-8000-000000000001");
        public Guid Id { get; set; } = RecipientId;
        public bool IsAuthenticated => true;
        public string UserId => Id.ToString();
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { TestCompanyIds.CompanyA };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class NotificationMembership : IWorkNotificationActorMembershipV1
    {
        public bool Allow { get; set; } = true;
        public int Calls { get; set; }
        public int? DenyAtCall { get; set; }
        public Task<bool> IsActiveAsync(Guid company, Guid actor, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Allow && Calls != DenyAtCall); }
    }
}
