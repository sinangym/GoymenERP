using System.Text.Json.Serialization;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

public sealed record PurchaseOrderServiceSelection(Guid ServiceOrderId, long ExpectedVersion, IReadOnlyList<Guid> LineIds);
public sealed record PurchaseOrderServiceMembership(Guid CompanyId, Guid PurchaseOrderId, Guid SupplierPartyId,
    Guid CompanyAccountId, Guid CurrencyId, Guid ServiceOrderId, long PreparedVersion, IReadOnlyList<Guid> LineIds);

public sealed record PurchaseOrderServiceMembershipBatch(long OrderVersionAfter, DateTimeOffset RecordedAtUtc,
    IReadOnlyList<PurchaseOrderServiceMembership> Members);

public sealed partial class PurchaseOrder
{
    public const string ServiceMembershipUnavailable = "purchasing_mixed_order_participant_unavailable";
    private readonly List<PurchaseOrderServiceMembership> serviceMemberships = [];
    private readonly List<PurchaseOrderServiceMembershipBatch> serviceMembershipBatches = [];

    // Server-only facts. No legacy order DTO, request hash or event is extended by this prerequisite.
    [JsonIgnore] public IReadOnlyList<PurchaseOrderServiceMembership> ServiceMemberships => serviceMemberships.AsReadOnly();
    [JsonIgnore] public IReadOnlyList<PurchaseOrderServiceMembershipBatch> ServiceMembershipBatches => serviceMembershipBatches.AsReadOnly();

    /// <summary>The caller supplies the actual stored parent account and selected native child records.
    /// Persistence, authority and same-transaction membership uniqueness remain the owner's responsibility.</summary>
    public void BindServiceOrders(long expectedVersion, Guid companyAccountId,
        IReadOnlyList<PurchaseServiceOrderState> services, IReadOnlyList<PurchaseOrderServiceSelection> selections,
        DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Draft, PurchasingRuleCodes.OrderNotDraft, "Üyelik yalnız taslak siparişte bağlanabilir.");
        var additions = PrepareServiceMemberships(companyAccountId, services, selections);
        // Validate the whole set first; an invalid later child must not leave a partial binding.
        _ = checked(Version + 1);
        serviceMemberships.AddRange(additions);
        Touch(now);
        serviceMembershipBatches.Add(new(Version, UpdatedAtUtc, additions.AsReadOnly()));
    }

    private List<PurchaseOrderServiceMembership> PrepareServiceMemberships(Guid companyAccountId,
        IReadOnlyList<PurchaseServiceOrderState> services, IReadOnlyList<PurchaseOrderServiceSelection> selections)
    {
        Identity.Require(companyAccountId, nameof(companyAccountId));
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(selections);
        if (Money is null || services.Count == 0 || services.Count != selections.Count
            || services.Any(x => x is null) || selections.Any(x => x is null)
            || services.Select(x => x.OrderId).Distinct().Count() != services.Count
            || selections.Select(x => x.ServiceOrderId).Distinct().Count() != selections.Count)
            throw new ArgumentException("Açık ve tekil hizmet üyeliği gereklidir.");
        var additions = new List<PurchaseOrderServiceMembership>();
        foreach (var selection in selections)
        {
            var service = services.SingleOrDefault(x => x.OrderId == selection.ServiceOrderId)
                ?? throw new ArgumentException("Seçilen hizmet kaynağı bulunamadı.");
            PurchaseServiceOrder.Validate(service);
            if (service.CompanyId != CompanyId || service.SupplierPartyId != SupplierPartyId
                || service.CompanyAccountId != companyAccountId || service.Money.CurrencyId != Money.CurrencyId
                || service.Status != PurchaseServiceOrderStatus.Draft || selection.ExpectedVersion != service.Version
                || selection.LineIds is not { Count: > 0 } || selection.LineIds.Any(x => x == Guid.Empty)
                || selection.LineIds.Distinct().Count() != selection.LineIds.Count
                || !selection.LineIds.ToHashSet().SetEquals(service.Lines.Select(x => x.LineId))
                || serviceMemberships.Any(x => x.ServiceOrderId == service.OrderId))
                throw new ArgumentException("Hizmet üyeliğinin şirket, hesap, sürüm veya satır kaynağı uyuşmuyor.");
            additions.Add(new(CompanyId, Id, SupplierPartyId, companyAccountId, Money.CurrencyId, service.OrderId,
                service.Version, Array.AsReadOnly(selection.LineIds.ToArray())));
        }
        return additions;
    }

    /// <summary>Stored Draft timeline only. Restores the exact recorded slot, never calls Touch, Approve or a command.</summary>
    internal void RestoreServiceMembershipBatch(PurchaseOrderServiceMembershipBatch batch, Guid companyAccountId,
        IReadOnlyList<PurchaseServiceOrderState> preparedServices)
    {
        ArgumentNullException.ThrowIfNull(batch);
        RequireStatus(PurchaseOrderStatus.Draft, PurchasingRuleCodes.OrderNotDraft, "Membership restore requires the recorded Draft timeline.");
        if (batch.OrderVersionAfter != checked(Version + 1) || batch.RecordedAtUtc.Offset != TimeSpan.Zero
            || batch.Members is not { Count: > 0 } || batch.Members.Any(x => x is null))
            throw new InvalidOperationException("Stored membership batch version or shape is invalid.");
        var selections = batch.Members.Select(x => new PurchaseOrderServiceSelection(x.ServiceOrderId, x.PreparedVersion, x.LineIds)).ToArray();
        var restored = PrepareServiceMemberships(companyAccountId, preparedServices, selections);
        for (var i = 0; i < restored.Count; i++)
            if ((restored[i] with { LineIds = batch.Members[i].LineIds }) != batch.Members[i])
                throw new InvalidOperationException("Stored membership differs from the actual parent/prepared child facts.");
        serviceMemberships.AddRange(restored);
        serviceMembershipBatches.Add(new(batch.OrderVersionAfter, batch.RecordedAtUtc, restored.AsReadOnly()));
        Version = batch.OrderVersionAfter;
        UpdatedAtUtc = batch.RecordedAtUtc;
    }

    /// <summary>Pure native transitions, NOT an authorized or durable command. A future owned participant must first
    /// revalidate native supplier/reference/tax facts and persist both results with one command/audit transaction.</summary>
    public IReadOnlyList<PurchaseServiceOrderState> ApproveWithServiceOrders(long expectedVersion, Guid companyAccountId,
        IReadOnlyList<PurchaseServiceOrderState> services, string approvedBy, DateTimeOffset now, Guid functionalCurrencyId,
        IReadOnlyDictionary<Guid, PurchaseOrderLineTheoreticalKilograms> theoreticalKilograms)
    {
        RequireVersion(expectedVersion);
        ArgumentNullException.ThrowIfNull(services);
        if (serviceMemberships.Count == 0 || services.Count != serviceMemberships.Count
            || services.Any(x => x is null) || services.Select(x => x.OrderId).Distinct().Count() != services.Count)
            throw new ArgumentException("Tam hizmet üyeliği bulunamadı.");
        var approved = new List<PurchaseServiceOrderState>();
        foreach (var membership in serviceMemberships)
        {
            var service = services.SingleOrDefault(x => x.OrderId == membership.ServiceOrderId)
                ?? throw new ArgumentException("Üye hizmet bulunamadı.");
            if (membership.CompanyId != CompanyId || membership.PurchaseOrderId != Id || membership.SupplierPartyId != SupplierPartyId
                || membership.CompanyAccountId != companyAccountId || Money?.CurrencyId != membership.CurrencyId
                || service.CompanyId != CompanyId || service.SupplierPartyId != SupplierPartyId
                || service.CompanyAccountId != companyAccountId || service.Money.CurrencyId != membership.CurrencyId
                || !membership.LineIds.ToHashSet().SetEquals(service.Lines.Select(x => x.LineId)))
                throw new ArgumentException("Üyelik güncel native sahiplerle uyuşmuyor.");
            // Reuse the child's exact expected version, state, maker and tax guards; no second approval policy.
            approved.Add(PurchaseServiceOrder.Approve(service, membership.PreparedVersion, approvedBy, now));
        }
        // Child states are immutable. Parent native rules run last, before either result can be persisted.
        ApproveCore(expectedVersion, approvedBy, now, functionalCurrencyId, theoreticalKilograms);
        return approved.AsReadOnly();
    }

    private void RequireNoServiceApprovalParticipant()
    {
        if (serviceMemberships.Count != 0)
            throw new PurchasingRuleException(ServiceMembershipUnavailable,
                "Karma siparişin aynı işlemdeki native katılımcısı henüz bağlı değil.");
    }
}
