using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    // Called inside source acceptance's Serializable transaction, after the exact historical cancellation receipt.
    // A terminal receipt acknowledges only an event that never obtained this owner's economic share. It does not
    // release, supersede, reverse or recreate the already accepted invoice's immutable source, claim or journal.
    private async Task<AccountingInventoryCostSourceV1> RecordObsoleteComponents(AccountingInventoryCostSourceV1 source,
        CancellationToken token)
    {
        if (source.HistoricalCancellation is null) return source;
        if (await db.InventoryCostProposals.AsNoTracking().AnyAsync(x => x.EventId == source.EventId, token)) throw ObsoletePending();
        RequireCompany(source.CompanyId);
        if (await db.InvoiceCogsEconomicClaims.AsNoTracking().AnyAsync(x => x.InvoiceId == source.InvoiceId
                && x.FinalizedVersion == source.InvoiceVersion, token)) throw ObsoletePending();
        RequireCompany(source.CompanyId);
        if (await db.InvoiceCogsEconomicClaimSuccessions.AsNoTracking().AnyAsync(x => x.InvoiceId == source.InvoiceId
                && x.FinalizedVersion == source.InvoiceVersion, token)) throw ObsoletePending();
        RequireCompany(source.CompanyId);
        var receipts = new List<AccountingInventoryCostObsoleteComponentV1>();
        foreach (var component in source.Components)
        {
            if (component.SalesPhysicalClaims is not { Count: > 0 and <= 10000 } physicalClaims) continue;
            var owners = new List<AccountingInventoryCostObsoleteOwnerV1>();
            foreach (var physical in physicalClaims)
            {
                var history = await InvoiceCogsClaimHistoryV1.ReadAsync(db, source.CompanyId, physical.MovementId,
                    physical.MovementLineNumber, () => RequireCompany(source.CompanyId), token);
                var existing = history.LastOrDefault()?.Claim;
                RequireCompany(source.CompanyId);
                if (existing is null) continue;
                if (existing.CompanyId != source.CompanyId || existing.InvoiceId == source.InvoiceId
                    || existing.SalesEventId == source.EventId || existing.AdmissionId == source.AdmissionId
                    || existing.ShipmentId != physical.ShipmentId || existing.InvoiceLineId == Guid.Empty
                    || existing.FinalizedVersion < 1 || existing.Origin is not (1 or 2)) throw ObsoletePending();
                var header = await db.InventoryCostSources.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == existing.SalesEventId, token);
                RequireCompany(source.CompanyId);
                if (header is null || header.CompanyId != source.CompanyId
                    || header.Kind != (int)AccountingInventoryCostKindV1.SalesInvoicedShipment
                    || header.InvoiceId != existing.InvoiceId || header.InvoiceVersion != existing.FinalizedVersion
                    || header.AdmissionId != existing.AdmissionId) throw ObsoletePending();
                var key = AccountingInventoryCostTraceV1.SalesComponent(existing.InvoiceLineId);
                var rows = await db.InventoryCostProposals.AsNoTracking().Where(x => x.EventId == header.EventId
                    && x.ComponentKey == key && x.Version == 1).Take(2).ToListAsync(token);
                RequireCompany(source.CompanyId);
                if (rows is not [var row]) throw ObsoletePending();
                AccountingInventoryCostProposalV1 owner;
                try { owner = await ReadStored(row, token); }
                catch (AccountingStoreException) { RequireCompany(source.CompanyId); throw ObsoletePending(); }
                RequireCompany(source.CompanyId);
                var matches = owner.Component.SalesPhysicalClaims?.Where(x => x.MovementId == physical.MovementId
                    && x.MovementLineNumber == physical.MovementLineNumber).ToArray();
                if (matches is not [var bound] || bound != physical || owner.Component.InvoiceLineId != existing.InvoiceLineId
                    || owner.Component.ProductId != component.ProductId) throw ObsoletePending();
                var expectedOrigin = owner.Component.AlreadyPostedComponents?.Any(x => x.Shipment.MovementId == physical.MovementId
                        && x.Shipment.MovementLineNumber == physical.MovementLineNumber) == true
                    || owner.Component.AlreadyPostedPurchaseComponents?.Any(x => x.MovementId == physical.MovementId
                        && x.MovementLineNumber == physical.MovementLineNumber) == true ? 2 : 1;
                var proof = Encode(new { source.CompanyId, EventId = owner.EventId, owner.InvoiceId,
                    owner.InvoiceVersion, owner.AdmissionId, owner.Component.InvoiceLineId,
                    Physical = bound, owner.Component.SourceProofSha256 });
                if (existing.Origin != expectedOrigin || existing.ViewJson != proof
                    || existing.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(proof)) throw ObsoletePending();
                owners.Add(new(bound, header.EventId, header.InvoiceId, existing.InvoiceLineId, header.AdmissionId,
                    header.InvoiceVersion, row.Id, AccountingInventoryCostSourceValidationV1.Sha256(header.SourceJson), existing.SourceHash));
            }
            if (owners.Count == 0) continue;
            // Never apportion an invoice line between competing owners or silently discard its remaining physical pay.
            if (owners.Count != physicalClaims.Count) throw ObsoletePending();
            receipts.Add(new(component.ComponentKey, owners));
        }
        return receipts.Count == 0 ? source : source with { ObsoleteComponents = receipts };
    }

    private static AccountingInvoiceJournalRetryException ObsoletePending() =>
        new("inventory_cost_obsolete_owner_unavailable", "İptal edilmiş kaynağın mevcut ekonomik sahiplik kanıtı doğrulanamadı.");
}
