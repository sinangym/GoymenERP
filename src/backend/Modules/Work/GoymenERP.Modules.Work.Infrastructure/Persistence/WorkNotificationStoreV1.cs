using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Work.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>D9 (WP2): writes into the single store <c>work.InboxNotifications</c> (kinds 4 and 5). Derived notification/inbox effects only; original stock and money audit histories remain with their owners.</summary>
public sealed class WorkNotificationStoreV1(IServiceScopeFactory scopes, ICompanyContext company,
    IWorkNotificationRecipientReaderV1 recipients, IWorkNotificationRetentionReaderV1 retention, TimeProvider time,
    IWorkPurchaseNotificationRecipientReaderV1 purchaseRecipients)
    : IWorkNotificationDeliveryV1, IWorkNotificationMaintenanceV1
{
    public async Task DeliverAsync(DeliverWorkNotificationV1 command, CancellationToken cancellationToken = default)
    {
        Validate(command);
        var companyId = RequireCompany();
        if (command.CompanyId != companyId) throw new OutboxPermanentFailureException("notification_company_mismatch", "Notification event company does not match its delivery scope.");
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var database = scope.ServiceProvider.GetRequiredService<WorkDbContext>();
        try
        {
            var recorded = await FindInboxAsync(database, command, cancellationToken);
            RequireOriginalCompany(companyId);
            if (recorded is not null) { RequireSame(recorded, command, fingerprint); return; }
            var resolved = new List<(WorkNotificationTargetV1 Target, WorkNotificationRecipientV1 Recipient)>();
            foreach (var target in command.Targets)
            {
                var answer = await recipients.ReadAsync(target.SalesOrderId, target.SalesOrderLineId, cancellationToken);
                RequireOriginalCompany(companyId);
                if (answer is null || answer.Outcome != WorkNotificationRecipientOutcomeV1.Found
                    || answer.CompanyId != companyId || answer.SalesOrderId != target.SalesOrderId
                    || answer.SalesOrderLineId != target.SalesOrderLineId || answer.UserId is not { } userId
                    || userId == Guid.Empty || answer.ObservedSalesOrderVersion is not > 0)
                    throw new InvalidOperationException("Notification recipient owner is unavailable, missing or unassigned; delivery must retry.");
                resolved.Add((target, answer));
            }
            WorkPurchaseNotificationRecipientsV1? purchase = null;
            if (command.PurchaseTarget is { } purchaseTarget)
            {
                purchase = await purchaseRecipients.ReadAsync(companyId, purchaseTarget.PurchaseOrderId,
                    purchaseTarget.PurchaseOrderLineId, cancellationToken);
                RequireOriginalCompany(companyId);
                if (purchase is null || purchase.Outcome != WorkNotificationRecipientOutcomeV1.Found
                    || purchase.CompanyId != companyId || purchase.PurchaseOrderId != purchaseTarget.PurchaseOrderId
                    || purchase.PurchaseOrderLineId != purchaseTarget.PurchaseOrderLineId || purchase.ObservedOrderVersion is not > 0
                    || purchase.UserIds is null || purchase.UserIds.Count == 0 || purchase.UserIds.Any(id => id == Guid.Empty)
                    || purchase.UserIds.Distinct().Count() != purchase.UserIds.Count)
                    throw new InvalidOperationException("Recorded purchase notification recipients are missing or unavailable; delivery must retry.");
            }
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            recorded = await FindInboxAsync(database, command, cancellationToken);
            RequireOriginalCompany(companyId);
            if (recorded is not null) { RequireSame(recorded, command, fingerprint); return; }
            var now = time.GetUtcNow();
            database.NotificationInbox.Add(new WorkNotificationInboxRow() { CompanyId = companyId, SourceName = command.SourceName,
                EventId = command.EventId, EventType = command.EventType, SchemaVersion = command.SchemaVersion,
                PayloadSha256 = fingerprint, RecordedAtUtc = now });
            foreach (var (target, recipient) in resolved)
                database.InboxNotifications.Add(Row(companyId, command, recipient.UserId!.Value, now, row =>
                {
                    row.SalesOrderId = target.SalesOrderId;
                    row.SalesOrderLineId = target.SalesOrderLineId;
                    row.ObservedSalesOrderVersion = recipient.ObservedSalesOrderVersion!.Value;
                }));
            if (purchase is not null)
                foreach (var recipientId in purchase.UserIds)
                    database.InboxNotifications.Add(Row(companyId, command, recipientId, now, row =>
                    {
                        row.PurchaseOrderId = purchase.PurchaseOrderId;
                        row.PurchaseOrderLineId = purchase.PurchaseOrderLineId;
                        row.ObservedPurchaseOrderVersion = purchase.ObservedOrderVersion;
                    }));
            RequireOriginalCompany(companyId);
            await database.SaveChangesAsync(cancellationToken);
            RequireOriginalCompany(companyId);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { database.ChangeTracker.Clear(); }
    }

    public async Task PassivateExpiredAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        var policy = await retention.ReadAsync(cancellationToken);
        RequireOriginalCompany(companyId);
        if (policy is null || policy.CompanyId != companyId || policy.ActiveDays < 0
            || string.IsNullOrWhiteSpace(policy.Version) || policy.Version.Length > 128)
            throw new InvalidOperationException("Notification retention policy is unavailable.");
        var now = time.GetUtcNow();
        // An explicitly entered large duration cannot overflow or become a shorter duration.
        if (policy.ActiveDays > (now - DateTimeOffset.MinValue).TotalDays) return;
        var cutoff = now.AddDays(-policy.ActiveDays);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var database = scope.ServiceProvider.GetRequiredService<WorkDbContext>();
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var expired = await EventRows(database).AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.RecordedAtUtc <= cutoff
                    && !database.NotificationPassivations.Any(passive => passive.NotificationId == row.Id))
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
            RequireOriginalCompany(companyId);
            foreach (var id in expired) database.NotificationPassivations.Add(new WorkNotificationPassivationRow() { CompanyId = companyId,
                NotificationId = id, PassivatedAtUtc = now, ActiveDays = policy.ActiveDays, PolicyVersion = policy.Version });
            await database.SaveChangesAsync(cancellationToken);
            RequireOriginalCompany(companyId);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { database.ChangeTracker.Clear(); }
    }
    /// <summary>
    /// D9 (WP2): the single notification store row of one event-derived notification. Kind 1 → 4 (allocation released),
    /// kind 2 → 5 (purchase supply revived); "özet" class (AI-D29); the source id is the row's own id.
    /// </summary>
    internal static WorkInboxNotificationKindV1 StoredKind(WorkNotificationKindV1 kind) => kind switch
    {
        WorkNotificationKindV1.AllocationReleased => WorkInboxNotificationKindV1.AllocationReleased,
        WorkNotificationKindV1.PurchaseSupplyRevived => WorkInboxNotificationKindV1.PurchaseSupplyRevived,
        _ => throw new OutboxPermanentFailureException("notification_event_invalid", "The notification kind is unknown."),
    };

    private static WorkInboxNotificationRow Row(Guid companyId, DeliverWorkNotificationV1 command, Guid recipient,
        DateTimeOffset now, Action<WorkInboxNotificationRow> target)
    {
        var id = Guid.NewGuid();
        var kind = StoredKind(command.Kind);
        var row = new WorkInboxNotificationRow
        {
            CompanyId = companyId, Id = id, RecipientUserId = recipient, Kind = (int)kind,
            Class = (int)WorkInboxNotificationClassesV1.Of(kind), SourceId = id, RecordedAtUtc = now,
            CorrelationId = command.CorrelationId, CausationId = command.CausationId, SourceName = command.SourceName,
            EventId = command.EventId, SourceEntityId = command.SourceEntityId, SourceVersion = command.SourceVersion,
            ReasonCode = command.ReasonCode, ReasonText = command.ReasonText, OccurredAtUtc = command.OccurredAtUtc,
        };
        target(row);
        return row;
    }

    private static readonly int[] EventKinds =
        [(int)WorkInboxNotificationKindV1.AllocationReleased, (int)WorkInboxNotificationKindV1.PurchaseSupplyRevived];

    internal static IQueryable<WorkInboxNotificationRow> EventRows(WorkDbContext database) =>
        database.InboxNotifications.Where(row => EventKinds.Contains(row.Kind));

    private Guid RequireCompany() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId
        : throw new InvalidOperationException("A resolved notification company is required.");
    private void RequireOriginalCompany(Guid expected)
    {
        if (RequireCompany() != expected) throw new InvalidOperationException("Notification company changed during owner read.");
    }
    private static Task<WorkNotificationInboxRow?> FindInboxAsync(WorkDbContext database, DeliverWorkNotificationV1 command,
        CancellationToken token) => database.NotificationInbox.AsNoTracking()
        .SingleOrDefaultAsync(row => row.SourceName == command.SourceName && row.EventId == command.EventId, token);
    private static void RequireSame(WorkNotificationInboxRow row, DeliverWorkNotificationV1 command, string fingerprint)
    {
        if (row.EventType != command.EventType || row.SchemaVersion != command.SchemaVersion || row.PayloadSha256 != fingerprint)
            throw new OutboxPermanentFailureException("notification_event_payload_mismatch", "A recorded notification event changed.");
    }
    private static void Validate(DeliverWorkNotificationV1 value)
    {
        if (value is null || value.CompanyId == Guid.Empty || value.EventId == Guid.Empty || value.SourceEntityId == Guid.Empty
            || !WorkOutboxRegistrationNamesV1.IsValidName(value.SourceName)
            || value.SchemaVersion != 1
            || (value.Kind == WorkNotificationKindV1.AllocationReleased
                ? value.EventType != "inventory.future-allocation.released.v1" || value.Targets?.Count != 1 || value.PurchaseTarget is not null
                : value.Kind != WorkNotificationKindV1.PurchaseSupplyRevived || value.EventType != "purchasing.order-line.revived.v1"
                    || value.PurchaseTarget is not { PurchaseOrderId: var orderId, PurchaseOrderLineId: var lineId }
                    || orderId == Guid.Empty || lineId == Guid.Empty || value.SourceEntityId != lineId)
            || !long.TryParse(value.SourceVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
            || value.SourceVersion != version.ToString(CultureInfo.InvariantCulture)
            || string.IsNullOrWhiteSpace(value.PayloadJson) || string.IsNullOrWhiteSpace(value.ReasonCode) || value.ReasonCode.Length > 64
            || string.IsNullOrWhiteSpace(value.ReasonText) || value.ReasonText.Length > 4000
            || string.IsNullOrWhiteSpace(value.CorrelationId) || value.CorrelationId.Length > 128 || value.CausationId?.Length > 128
            || value.Targets is null || value.Targets.Any(row => row is null
                || row.SalesOrderId == Guid.Empty || row.SalesOrderLineId == Guid.Empty)
            || value.Targets.Distinct().Count() != value.Targets.Count)
            throw new OutboxPermanentFailureException("notification_event_invalid", "The notification event contract is invalid.");
    }
}

public sealed class WorkNotificationReadV1(WorkDbContext database, ICompanyContext company, ICurrentUser user,
    IWorkNotificationActorMembershipV1 membership, IWorkNotificationMaintenanceV1 maintenance) : IWorkNotificationReadV1
{
    public async Task<WorkNotificationPageV1> ListMineAsync(bool includePassive, CancellationToken cancellationToken = default, Guid? beforeId = null)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        if (!Guid.TryParse(actor, out var userId) || userId == Guid.Empty || companyId == Guid.Empty) throw new UnauthorizedAccessException();
        await RequireStillAsync(companyId, actor!, cancellationToken);
        await maintenance.PassivateExpiredAsync(cancellationToken);
        await RequireStillAsync(companyId, actor!, cancellationToken);
        var own = WorkNotificationStoreV1.EventRows(database).AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.RecipientUserId == userId);
        if (beforeId is { } cursor)
        {
            var before = await own.Where(item => item.Id == cursor).Select(item => new { item.Id, item.RecordedAtUtc }).SingleOrDefaultAsync(cancellationToken);
            await RequireStillAsync(companyId, actor!, cancellationToken);
            if (before is null) throw new ArgumentException("The notification cursor is not available in this inbox.");
            own = own.Where(item => item.RecordedAtUtc < before.RecordedAtUtc || (item.RecordedAtUtc == before.RecordedAtUtc && item.Id.CompareTo(cursor) > 0));
        }
        var rows = await (from item in own
            join passive in database.NotificationPassivations.AsNoTracking() on new { item.CompanyId, NotificationId = item.Id }
                equals new { passive.CompanyId, passive.NotificationId } into passives
            from passive in passives.DefaultIfEmpty()
            where item.CompanyId == companyId && item.RecipientUserId == userId && (includePassive || passive == null)
            orderby item.RecordedAtUtc descending, item.Id
            select new WorkNotificationItemV1(item.Id, item.CompanyId,
                item.Kind == (int)WorkInboxNotificationKindV1.AllocationReleased ? "allocation-released" : "purchase-supply-revived",
                item.SalesOrderId, item.SalesOrderLineId, item.SourceEntityId!.Value, item.SourceVersion!, item.ReasonCode!,
                item.ReasonText!, item.OccurredAtUtc!.Value, item.RecordedAtUtc, passive == null ? null : passive.PassivatedAtUtc,
                item.PurchaseOrderId, item.PurchaseOrderLineId)).Take(101).ToArrayAsync(cancellationToken);
        await RequireStillAsync(companyId, actor!, cancellationToken);
        return new WorkNotificationPageV1(companyId, rows.Take(100).ToArray(), rows.Length > 100, rows.Length > 100 ? rows[99].Id : null);
    }
    private async Task RequireStillAsync(Guid companyId, string actor, CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId != actor || !company.IsResolved || company.CompanyId != companyId
            || !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException();
        var active = await membership.IsActiveAsync(companyId, Guid.Parse(actor), token);
        if (!user.IsAuthenticated || user.UserId != actor || !company.IsResolved || company.CompanyId != companyId
            || !user.CanAccessCompany(companyId) || !active) throw new UnauthorizedAccessException();
    }
}
