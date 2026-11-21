using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    private async Task ClaimPhysicalCost(AccountingInventoryCostSourceV1 source, AccountingInventoryCostComponentV1 component,
        CancellationToken token)
    {
        if (source.Kind != AccountingInventoryCostKindV1.SalesInvoicedShipment)
        {
            if (component.SalesPhysicalClaims is not null || component.AlreadyPostedComponents is not null || component.AlreadyPostedPurchaseComponents is not null)
                throw Refuse("inventory_cost_physical_claim_invalid");
            return;
        }
        var claims = component.SalesPhysicalClaims;
        if (claims is null)
        {
            if (component.AlreadyPostedComponents is not null || component.AlreadyPostedPurchaseComponents is not null) throw Refuse("inventory_cost_physical_claim_invalid");
            return; // Old known-stamp source shape; it never resolves an unavailable stamp through attribution.
        }
        if (claims.Count is < 1 or > 10000 || claims.Select(x => (x.MovementId, x.MovementLineNumber)).Distinct().Count() != claims.Count)
            throw Refuse("inventory_cost_physical_claim_invalid");
        foreach (var physical in claims)
        {
            if (physical.ShipmentId == Guid.Empty || physical.MovementId == Guid.Empty || physical.MovementLineNumber < 1
                || physical.ProductId != component.ProductId || physical.QuantityKg <= 0)
                throw Refuse("inventory_cost_physical_claim_invalid");
            var history = await InvoiceCogsClaimHistoryV1.ReadAsync(db, source.CompanyId, physical.MovementId,
                physical.MovementLineNumber, () => RequireCompany(source.CompanyId), token);
            var old = history.LastOrDefault();
            RequireCompany(source.CompanyId);
            if (old is not null)
            {
                var succession = component.ClaimSuccession ?? throw Refuse("inventory_cost_economic_claim_conflict");
                var previous = succession.Claims.SingleOrDefault(x => x.Physical == physical);
                if (previous is null || previous.PreviousGeneration != old.Generation || previous.PreviousClaimSha256 != old.Claim.SourceHash
                    || succession.PreviousEventId != old.Claim.SalesEventId || succession.PreviousInvoiceId != old.Claim.InvoiceId
                    || succession.RestoredAmount != component.SignedTlAmount || component.HasPostedComponents)
                    throw Refuse("inventory_cost_economic_claim_conflict");
                var json = Encode(new { source.CompanyId, source.EventId, source.InvoiceId, source.InvoiceVersion, source.AdmissionId,
                    component.InvoiceLineId, Physical = physical, component.SourceProofSha256 });
                var successionJson = Encode(succession);
                db.InvoiceCogsEconomicClaimSuccessions.Add(new() { CompanyId = source.CompanyId, MovementId = physical.MovementId,
                    MovementLineNumber = physical.MovementLineNumber, Generation = checked(old.Generation + 1),
                    ShipmentId = physical.ShipmentId, SalesEventId = source.EventId, InvoiceId = source.InvoiceId,
                    InvoiceLineId = component.InvoiceLineId!.Value, AdmissionId = source.AdmissionId, FinalizedVersion = source.InvoiceVersion,
                    PreviousClaimSha256 = old.Claim.SourceHash, CancellationId = succession.CancellationId,
                    SourceHash = AccountingInventoryCostSourceValidationV1.Sha256(json), ViewJson = json,
                    SuccessionJson = successionJson, SuccessionSha256 = AccountingInventoryCostSourceValidationV1.Sha256(successionJson),
                    RecordedAtUtc = clock.GetUtcNow() });
                continue;
            }
            if (component.ClaimSuccession is not null) throw Refuse("inventory_cost_economic_claim_conflict");
            var expected = component.AlreadyPostedComponents?.Where(x => x.Shipment.MovementId == physical.MovementId
                && x.Shipment.MovementLineNumber == physical.MovementLineNumber).OrderBy(x => x.SourceEventId).ThenBy(x => x.OutputId).ToArray() ?? [];
            var purchased = component.AlreadyPostedPurchaseComponents?.Where(x => x.MovementId == physical.MovementId
                && x.MovementLineNumber == physical.MovementLineNumber).OrderBy(x => x.Approval.SourceEventId).ThenBy(x => x.Approval.InvoiceLineId).ToArray() ?? [];
            if (expected.Length > 0)
            {
                if (postedManufacturing is null) throw Refuse("manufacturing_attribution_owner_unavailable");
                var actual = await postedManufacturing.ReadProvenSharesAsync(source.CompanyId, physical.ShipmentId, physical.MovementId,
                    physical.MovementLineNumber, physical.ProductId, physical.QuantityKg,
                    _ => { RequireCompany(source.CompanyId); return Task.CompletedTask; }, token);
                RequireCompany(source.CompanyId);
                if (actual is null || expected.Length == 0 || Encode(actual.Shares) != Encode(expected))
                    throw Refuse("inventory_cost_attribution_changed");
            }
            if (purchased.Length > 0)
            {
                if (postedPurchasing is null) throw Refuse("purchase_cost_attribution_owner_unavailable");
                var actual = await postedPurchasing.ReadProvenSharesAsync(source.CompanyId, physical.ShipmentId, physical.MovementId,
                    physical.MovementLineNumber, physical.ProductId, physical.QuantityKg,
                    _ => { RequireCompany(source.CompanyId); return Task.CompletedTask; }, token);
                RequireCompany(source.CompanyId);
                if (actual.HasPendingCandidate || Encode(actual.Shares) != Encode(purchased)) throw Refuse("inventory_cost_attribution_changed");
            }
            if ((expected.Length > 0 || purchased.Length > 0) && !PostedCostPhysicalCoverageV1.IsComplete(expected, purchased))
                throw Refuse("inventory_cost_attribution_changed");
            var proof = Encode(new { source.CompanyId, source.EventId, source.InvoiceId, source.InvoiceVersion, source.AdmissionId,
                component.InvoiceLineId, Physical = physical, component.SourceProofSha256 });
            db.InvoiceCogsEconomicClaims.Add(new() { CompanyId = source.CompanyId, MovementId = physical.MovementId,
                MovementLineNumber = physical.MovementLineNumber, ShipmentId = physical.ShipmentId, SalesEventId = source.EventId,
                InvoiceId = source.InvoiceId, InvoiceLineId = component.InvoiceLineId!.Value, AdmissionId = source.AdmissionId,
                FinalizedVersion = source.InvoiceVersion, Origin = expected.Length == 0 && purchased.Length == 0 ? 1 : 2,
                SourceHash = AccountingInventoryCostSourceValidationV1.Sha256(proof), ViewJson = proof, RecordedAtUtc = clock.GetUtcNow() });
        }
    }
}
