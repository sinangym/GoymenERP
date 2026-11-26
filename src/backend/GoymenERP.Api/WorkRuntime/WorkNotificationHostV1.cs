using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure;

namespace GoymenERP.Api.WorkRuntime;

public static class WorkNotificationHostV1
{
    public static IServiceCollection AddAccountingCostProposalStaleNotificationHostV1(this IServiceCollection services) =>
        services.AddWorkInboxConsumer<AccountingCostProposalStaleNotificationConsumerV1>("work.accounting-cost-proposal-stale",
            new WorkInboxSubscriptionV1(GoymenERP.Modules.Accounting.Application.AccountingCostProposalStaleEventV1.SourceName,
                GoymenERP.Modules.Accounting.Application.AccountingCostProposalStaleEventV1.EventType, 1));

    /// <summary>Host composition only; caller registers Inventory's real committed-horizon source. No dispatcher activation.</summary>
    public static IServiceCollection AddWorkNotificationHostV1(this IServiceCollection services, string inventorySourceName, string purchasingSourceName)
    {
        if (!WorkOutboxRegistrationNamesV1.IsValidName(inventorySourceName)) throw new ArgumentException("A registered Inventory source is required.");
        if (!WorkOutboxRegistrationNamesV1.IsValidName(purchasingSourceName)) throw new ArgumentException("A registered Purchasing source is required.");
        services.AddWorkNotificationStorageV1();
        services.AddScoped<IWorkNotificationRecipientReaderV1, WorkSalesNotificationRecipientReaderV1>();
        services.AddScoped<IWorkPurchaseNotificationRecipientReaderV1, WorkPurchaseNotificationRecipientReaderV1>();
        services.AddScoped<IWorkNotificationRetentionReaderV1, WorkSettingsNotificationRetentionReaderV1>();
        services.AddScoped<IWorkNotificationActorMembershipV1, WorkNotificationActorMembershipV1>();
        services.AddScoped<InventoryAllocationReleaseNotificationConsumerV1>(provider => new(
            provider.GetRequiredService<IWorkNotificationDeliveryV1>(), inventorySourceName));
        services.AddWorkInboxConsumer<InventoryAllocationReleaseNotificationConsumerV1>(
            "work.allocation-release-notification", new WorkInboxSubscriptionV1(inventorySourceName, InventoryFutureAllocationReleasedOutboxV1.EventType, 1));
        services.AddScoped<PurchaseSupplyRevivedNotificationConsumerV1>(provider => new(
            provider.GetRequiredService<IWorkNotificationDeliveryV1>(), purchasingSourceName));
        return services.AddWorkInboxConsumer<PurchaseSupplyRevivedNotificationConsumerV1>(
            "work.purchase-supply-revived-notification", new WorkInboxSubscriptionV1(purchasingSourceName, PurchasingOrderLineRevivedEventV1.EventType, 1));
    }
}
internal sealed class WorkPurchaseNotificationRecipientReaderV1(IPurchaseNotificationRecipientsV1 owner)
    : IWorkPurchaseNotificationRecipientReaderV1
{
    public async Task<WorkPurchaseNotificationRecipientsV1> ReadAsync(Guid companyId, Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        var result = await owner.ReadAsync(companyId, purchaseOrderId, purchaseOrderLineId, cancellationToken);
        return new(result.Outcome switch { PurchaseNotificationRecipientsOutcomeV1.Found => WorkNotificationRecipientOutcomeV1.Found,
            PurchaseNotificationRecipientsOutcomeV1.NotFound => WorkNotificationRecipientOutcomeV1.NotFound,
            PurchaseNotificationRecipientsOutcomeV1.Unassigned => WorkNotificationRecipientOutcomeV1.Unassigned,
            _ => WorkNotificationRecipientOutcomeV1.Unavailable }, result.CompanyId, result.PurchaseOrderId,
            result.GeneralLineId, result.ObservedOrderVersion, result.UserIds);
    }
}

internal sealed class WorkNotificationActorMembershipV1(IUserCompanyMembershipReadPortV1 owner, ICurrentUser user)
    : IWorkNotificationActorMembershipV1
{
    public async Task<bool> IsActiveAsync(Guid companyId, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(user.UserId, out var current) || current != actorId) return false;
        var actor = user.UserId!;
        var answer = await owner.GetActiveCompaniesAsync(new(companyId, actor, actorId, [companyId]), cancellationToken);
        if (answer.Outcome == UserDirectoryOutcomeV1.Unavailable) throw new InvalidOperationException("Notification membership owner is unavailable.");
        return answer.Outcome == UserDirectoryOutcomeV1.Found && answer.ActiveCompanyIds.Count == 1
            && answer.ActiveCompanyIds[0] == companyId && user.UserId == actor;
    }
}

internal sealed class WorkSalesNotificationRecipientReaderV1(ISalesLineResponsibleReadPortV1 owner) : IWorkNotificationRecipientReaderV1
{
    public async Task<WorkNotificationRecipientV1> ReadAsync(Guid salesOrderId, Guid salesOrderLineId, CancellationToken cancellationToken = default)
    {
        var result = await owner.ReadAsync(salesOrderId, salesOrderLineId, cancellationToken);
        return new(result.Outcome switch { SalesLineResponsibleOutcomeV1.Found => WorkNotificationRecipientOutcomeV1.Found,
            SalesLineResponsibleOutcomeV1.NotFound => WorkNotificationRecipientOutcomeV1.NotFound,
            SalesLineResponsibleOutcomeV1.Unassigned => WorkNotificationRecipientOutcomeV1.Unassigned,
            _ => WorkNotificationRecipientOutcomeV1.Unavailable }, result.CompanyId, result.OrderId,
            result.LineId, result.ResponsibleUserId, result.ObservedOrderVersion);
    }
}
internal sealed class WorkSettingsNotificationRetentionReaderV1(ISettingsNotificationRetentionAutomationV1 owner)
    : IWorkNotificationRetentionReaderV1
{
    public async Task<WorkNotificationRetentionV1> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await owner.ReadAsync(cancellationToken);
        return new(result.CompanyId, result.ActiveDays, result.VersionText);
    }
}

internal sealed class InventoryAllocationReleaseNotificationConsumerV1(IWorkNotificationDeliveryV1 delivery, string expectedSource)
    : IWorkInboxConsumerV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly HashSet<string> Properties = ["companyId", "allocationId", "allocationVersion", "salesOrderId",
        "salesOrderLineId", "purchaseOrderId", "purchaseOrderLineId", "unitId", "releasedQuantity", "remainingQuantity",
        "status", "reasonCode", "reasonText", "origin", "correlationId", "causationId"];

    public async Task HandleAsync(WorkInboxEventV1 message, CancellationToken cancellationToken)
    {
        InventoryFutureAllocationReleasedV1 value;
        try
        {
            using var document = JsonDocument.Parse(message.PayloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != Properties.Count || !names.ToHashSet(StringComparer.Ordinal).SetEquals(Properties)) throw new JsonException();
            value = JsonSerializer.Deserialize<InventoryFutureAllocationReleasedV1>(message.PayloadJson, Json) ?? throw new JsonException();
            if (message.SourceName != expectedSource || message.EventType != InventoryFutureAllocationReleasedOutboxV1.EventType
                || message.Version != 1 || message.EventId == Guid.Empty || message.CompanyId == Guid.Empty
                || value.CompanyId != message.CompanyId || value.CorrelationId != message.CorrelationId || value.CausationId != message.CausationId
                || value.AllocationId == Guid.Empty || value.SalesOrderId == Guid.Empty || value.SalesOrderLineId == Guid.Empty
                || value.PurchaseOrderId == Guid.Empty || value.PurchaseOrderLineId == Guid.Empty || value.UnitId == Guid.Empty
                || !long.TryParse(value.AllocationVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
                || value.AllocationVersion != version.ToString(CultureInfo.InvariantCulture)
                || !InventoryFutureAllocationReleaseReasonsV1.IsWellFormed(value.ReasonCode)
                || !InventoryFutureAllocationOriginsV1.IsKnown(value.Origin)
                || value.Status is not ("Active" or "Consumed" or "Released")
                || !IsQuantity(value.ReleasedQuantity, positive: true) || !IsQuantity(value.RemainingQuantity, positive: false)
                || string.IsNullOrWhiteSpace(value.ReasonText) || value.ReasonText.Length > 4000) throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw new OutboxPermanentFailureException("notification_release_contract_invalid", "The release event contract is invalid."); }

        // KLN-D94/D110 covers receipt-link failure. Manual/cancellation/reduction releases acquire no new notification policy here.
        if (value.ReasonCode != InventoryFutureAllocationReleaseReasonsV1.AcceptanceLinkFailed) return;
        await delivery.DeliverAsync(new(message.CompanyId, message.SourceName, message.EventId, message.EventType,
            message.Version, message.PayloadJson, WorkNotificationKindV1.AllocationReleased, value.AllocationId,
            value.AllocationVersion, value.ReasonCode, value.ReasonText, message.OccurredAtUtc,
            message.CorrelationId, message.CausationId, [new(value.SalesOrderId, value.SalesOrderLineId)]), cancellationToken);
    }
    private static bool IsQuantity(string? text, bool positive)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 32 || text.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value < 0 || (positive && value == 0)) return false;
        try { return GoymenERP.Modules.Inventory.Domain.FutureSupply.FutureSupplyQuantity.Of(value).ToString() == text; }
        catch (GoymenERP.Modules.Inventory.Domain.FutureSupply.FutureSupplyRuleException) { return false; }
    }
}
