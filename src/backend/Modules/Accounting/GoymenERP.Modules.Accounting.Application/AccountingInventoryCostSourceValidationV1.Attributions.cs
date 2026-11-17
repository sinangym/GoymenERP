using System.Globalization;
using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public static partial class AccountingInventoryCostSourceValidationV1
{
    private static void RequirePostedComponents(AccountingInventoryCostComponentV1 component, Guid companyId)
    {
        var manufacturing = component.AlreadyPostedComponents;
        var purchasing = component.AlreadyPostedPurchaseComponents;
        if (manufacturing is null && purchasing is null)
        {
            if (component.UnpostedTlAmount is not null) throw Invalid("inventory_cost_attribution_invalid");
            return;
        }
        if (component.SalesPhysicalClaims is not { Count: > 0 and <= 10000 } physical
            || manufacturing is { Count: 0 or > 10000 } || purchasing is { Count: 0 or > 10000 })
            throw Invalid("inventory_cost_attribution_invalid");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        if (manufacturing is not null && (manufacturing.Select(x => (x.SourceEventId, x.OutputId,
                x.Shipment.MovementId, x.Shipment.MovementLineNumber)).Distinct().Count() != manufacturing.Count
            || manufacturing.Any(x => x.CompanyId != companyId || x.SourceEventId == Guid.Empty || x.OutputId == Guid.Empty
                || x.JournalId == Guid.Empty || x.CurrencyId == Guid.Empty || x.CogsAccountId == Guid.Empty
                || x.CounterAccountId == Guid.Empty || x.CogsAccountId == x.CounterAccountId
                || x.Fingerprint != Sha256(JsonSerializer.Serialize(x with { Fingerprint = "" }, json))
                || !physical.Any(p => p.MovementId == x.Shipment.MovementId && p.MovementLineNumber == x.Shipment.MovementLineNumber
                    && p.ShipmentId == x.Shipment.ShipmentId && p.ProductId == x.Shipment.ProductId && p.QuantityKg == x.Shipment.PhysicalQuantityKg))))
            throw Invalid("inventory_cost_attribution_invalid");
        if (purchasing is not null && (purchasing.Select(x => (x.Approval.SourceEventId, x.Approval.InvoiceLineId,
                x.MovementId, x.MovementLineNumber)).Distinct().Count() != purchasing.Count
            || purchasing.Any(x => x.Approval.CompanyId != companyId || x.Approval.SourceEventId == Guid.Empty
                || x.Approval.InvoiceLineId == Guid.Empty || x.Approval.CurrencyId == Guid.Empty || x.Approval.ProposalId == Guid.Empty
                || x.Approval.Status is not ("Posted" or "RoundedNoCost") || x.Approval.Status == "Posted" && x.Approval.JournalId is null
                || x.Approval.Status == "RoundedNoCost" && (x.Approval.JournalId is not null || x.Approval.SignedAmount != "0" || x.SignedAmount != "0")
                || x.Approval.CogsAccountId == Guid.Empty || x.Approval.CounterAccountId == Guid.Empty
                || x.Approval.CogsAccountId == x.Approval.CounterAccountId || x.Approval.PreparedBy == Guid.Empty
                || x.Approval.ApprovedBy == Guid.Empty || x.Approval.PreparedBy == x.Approval.ApprovedBy
                || x.Approval.Fingerprint != Sha256(JsonSerializer.Serialize(x.Approval with { Fingerprint = "" }, json))
                || x.Fingerprint != Sha256(JsonSerializer.Serialize(x with { Fingerprint = "" }, json))
                || x.Candidates is not { Count: > 0 and <= 10000 }
                || x.Candidates.Any(c => c.CompanyId != companyId || c.SourceEventId != x.Approval.SourceEventId
                    || c.InvoiceLineId != x.Approval.InvoiceLineId || c.ProductId != x.ProductId
                    || c.Shipment.ShipmentId != x.ShipmentId || c.Shipment.MovementId != x.MovementId
                    || c.Shipment.MovementLineNumber != x.MovementLineNumber || c.Shipment.PhysicalQuantityKg != x.PhysicalQuantityKg
                    || c.Fingerprint != Sha256(JsonSerializer.Serialize(c with { Fingerprint = "" }, json)))
                || !physical.Any(p => p.MovementId == x.MovementId && p.MovementLineNumber == x.MovementLineNumber
                    && p.ShipmentId == x.ShipmentId && p.ProductId == x.ProductId && p.QuantityKg == x.PhysicalQuantityKg))))
            throw Invalid("inventory_cost_attribution_invalid");
        var sum = AccountingInventoryCostAmountsV1.FromDecimal(0m);
        foreach (var text in (manufacturing?.Select(x => x.SignedAmount) ?? []).Concat(purchasing?.Select(x => x.SignedAmount) ?? []))
        {
            if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount)) throw Invalid("inventory_cost_attribution_invalid");
            sum = AccountingInventoryCostAmountsV1.Add(sum, AccountingInventoryCostAmountsV1.FromDecimal(amount));
        }
        var unpostedPhysical = false;
        foreach (var movement in physical)
        {
            var mfg = manufacturing?.Where(x => x.Shipment.MovementId == movement.MovementId
                && x.Shipment.MovementLineNumber == movement.MovementLineNumber).ToArray() ?? [];
            var purchase = purchasing?.Where(x => x.MovementId == movement.MovementId
                && x.MovementLineNumber == movement.MovementLineNumber).ToArray() ?? [];
            if (mfg.Length == 0 && purchase.Length == 0) unpostedPhysical = true;
            else if (!PostedCostPhysicalCoverageV1.IsComplete(mfg, purchase)) throw Invalid("inventory_cost_attribution_invalid");
        }
        if (component.UnpostedTlAmount is { } remaining)
        {
            if (!unpostedPhysical || !AccountingInventoryCostAmountsV1.TryRead(remaining, out var n, out _) || n < 0)
                throw Invalid("inventory_cost_attribution_invalid");
            sum = AccountingInventoryCostAmountsV1.Add(sum, remaining);
        }
        else if (unpostedPhysical) throw Invalid("inventory_cost_attribution_invalid");
        if (sum != component.SignedTlAmount) throw Invalid("inventory_cost_attribution_invalid");
    }
}
