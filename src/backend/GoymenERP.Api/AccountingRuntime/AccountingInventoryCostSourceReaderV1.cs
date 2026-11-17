using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.AccountingRuntime;

/// <summary>
/// Adapts committed owner evidence, without pricing, averaging or proportional allocation. Sales claims one whole
/// dispatch line. Commercial invoice kg can differ from physical stock kg; only the original Inventory TL stamp is money.
/// An unavailable original stamp stays unavailable even if a later manufacturing generation has posted a consumed-cost
/// proposal. It cannot be turned into a second initial cost without a separately proved economic lineage.
/// </summary>
public sealed class AccountingInventoryCostSourceReaderV1(ICompanyContext company,
    ILogisticsInvoicedShipmentProofReadPortV1 logistics, IInventoryShipmentCostReadPortV1 inventory,
    IPostedManufacturingShipmentCostReaderV1? postedManufacturing = null,
    ISalesFinalizedShipmentClaimReadPortV1? salesClaims = null,
    IPostedPurchaseShipmentCostReaderV1? postedPurchasing = null)
    : IAccountingInventoryCostSourceReaderV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope,
        CancellationToken cancellationToken = default)
    {
        var captured = Company();
        AccountingInventoryCostSourceValidationV1.RequireEnvelope(envelope, captured);
        if (envelope.SourceName == AccountingInventoryCostInboxV1.InventorySourceName)
            return AccountingPurchaseInvoiceCostSourceV1.Read(envelope, captured);
        AccountingSalesInvoiceFinalizedEventV1? source;
        try { source = JsonSerializer.Deserialize<AccountingSalesInvoiceFinalizedEventV1>(envelope.PayloadJson, Json); }
        catch (JsonException) { throw Invalid("sales_cost_source_invalid"); }
        if (source is null || source.CompanyId != captured || source.InvoiceId == Guid.Empty || source.Version < 1
            || source.AdmissionId == Guid.Empty || source.InvoiceDate is null || source.InvoiceDate == default(DateOnly)
            || !Guid.TryParse(source.FinalizedBy, out var actor) || actor == Guid.Empty
            || source.Lines is not { Count: > 0 and <= 10000 }) throw Invalid("sales_cost_source_invalid");
        var components = new List<AccountingInventoryCostComponentV1>();
        var lineIds = new HashSet<Guid>();
        var numbers = new HashSet<int>();
        var claimed = new HashSet<(Guid, int)>();
        var movementLines = new HashSet<(Guid, int)>();
        foreach (var line in source.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || line.LineNo < 1 || !lineIds.Add(line.LineId)
                || !numbers.Add(line.LineNo)) throw Invalid("sales_cost_line_invalid");
            if (line.ItemType == "Service")
            {
                if (line.ShipmentSource is not null) throw Invalid("sales_cost_service_shipment_invalid");
                continue;
            }
            var claim = line.ShipmentSource;
            if (claim is null) throw Pending("sales_cost_shipment_source_missing");
            if (claim.ShipmentId == Guid.Empty || claim.DispatchNoteId == Guid.Empty || claim.DispatchNoteLineNo < 1
                || claim.SalesOrderId == Guid.Empty || claim.SalesOrderLineId == Guid.Empty || claim.PriceBasisRevision < 0
                || claim.ProductId == Guid.Empty || claim.UnitId == Guid.Empty || !Quantity(claim.InvoicedQuantity, out var commercialQuantity)
                || !claimed.Add((claim.DispatchNoteId, claim.DispatchNoteLineNo))) throw Invalid("sales_cost_shipment_source_invalid");
            var proofRead = await logistics.GetAsync(captured, claim.DispatchNoteId, claim.DispatchNoteLineNo, claim.ShipmentId, cancellationToken);
            SameCompany(captured);
            var proof = Proof(proofRead, claim, captured, commercialQuantity);
            var stock = await inventory.GetByShipmentAsync(captured, claim.ShipmentId, cancellationToken);
            SameCompany(captured);
            if (stock is null || stock.Outcome != InventoryShipmentCostReadOutcomeV1.Available || stock.CompanyId != captured
                || stock.ShipmentId != claim.ShipmentId || stock.MovementId == Guid.Empty || string.IsNullOrWhiteSpace(stock.SourceVersion)
                || stock.Lines is not { Count: > 0 and <= 10000 }) throw Pending("sales_cost_inventory_proof_unavailable");
            // The owner currently proves one physical movement line. Reject extra unrelated lines rather than drop them.
            var total = AccountingInventoryCostAmountsV1.FromDecimal(0m);
            var unposted = AccountingInventoryCostAmountsV1.FromDecimal(0m);
            var physicalClaims = new List<AccountingSalesPhysicalCostClaimV1>();
            var alreadyPosted = new List<PostedManufacturingCostShareV1>();
            var alreadyPurchased = new List<PostedPurchaseCostShareV1>();
            var hasOriginalCost = false;
            foreach (var actual in stock.Lines)
            {
                if (actual is null || actual.MovementLineNumber < 1 || !movementLines.Add((stock.MovementId, actual.MovementLineNumber))
                    || actual.LoadingInstructionId != proof.InstructionId || actual.LoadingInstructionLineId != proof.InstructionLineId
                    || actual.ProductId != claim.ProductId || string.IsNullOrWhiteSpace(actual.ProductVersion)
                    || actual.AllocationId == Guid.Empty || actual.LabelId == Guid.Empty || string.IsNullOrWhiteSpace(actual.LabelCode)
                    || actual.LocationId == Guid.Empty || actual.QuantityKg <= 0m || actual.OwnerKind != StockPartyKind.Company
                    || actual.OwnerId != captured || actual.CustodianKind != StockPartyKind.Company || actual.CustodianId != captured
                    || actual.Block != BlockState.Free) throw Pending("sales_cost_inventory_binding_unavailable");
                physicalClaims.Add(new(claim.ShipmentId, stock.MovementId, actual.MovementLineNumber, actual.ProductId, actual.QuantityKg));
                if (actual.Tl is { Status: InventoryMovementCostStampStatusV1.Unavailable })
                {
                    var posted = postedManufacturing is null ? null : await postedManufacturing.ReadProvenSharesAsync(captured, claim.ShipmentId, stock.MovementId,
                        actual.MovementLineNumber, actual.ProductId, actual.QuantityKg,
                        _ => { SameCompany(captured); return Task.CompletedTask; }, cancellationToken);
                    SameCompany(captured);
                    if (posted is not null && (posted.CompanyId != captured || posted.ShipmentId != claim.ShipmentId
                        || posted.MovementId != stock.MovementId || posted.MovementLineNumber != actual.MovementLineNumber
                        || posted.ProductId != actual.ProductId || posted.QuantityKg != actual.QuantityKg))
                        throw Pending("sales_cost_original_tl_unavailable");
                    var purchase = postedPurchasing is null ? null : await postedPurchasing.ReadProvenSharesAsync(captured,
                        claim.ShipmentId, stock.MovementId, actual.MovementLineNumber, actual.ProductId, actual.QuantityKg,
                        _ => { SameCompany(captured); return Task.CompletedTask; }, cancellationToken);
                    SameCompany(captured);
                    if (purchase is not null && (purchase.CompanyId != captured || purchase.ShipmentId != claim.ShipmentId
                        || purchase.MovementId != stock.MovementId || purchase.MovementLineNumber != actual.MovementLineNumber
                        || purchase.ProductId != actual.ProductId || purchase.QuantityKg != actual.QuantityKg || purchase.HasPendingCandidate)
                        || !PostedCostPhysicalCoverageV1.IsComplete(posted?.Shares ?? [], purchase?.Shares ?? []))
                        throw Pending("sales_cost_original_tl_unavailable");
                    alreadyPosted.AddRange(posted?.Shares ?? []);
                    alreadyPurchased.AddRange(purchase?.Shares ?? []);
                    foreach (var share in posted?.Shares ?? [])
                    {
                        if (!decimal.TryParse(share.SignedAmount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out var signed)) throw Pending("sales_cost_posted_share_unavailable");
                        total = AccountingInventoryCostAmountsV1.Add(total, AccountingInventoryCostAmountsV1.FromDecimal(signed));
                    }
                    foreach (var share in purchase?.Shares ?? [])
                    {
                        if (!decimal.TryParse(share.SignedAmount, NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out var signed)) throw Pending("sales_cost_posted_share_unavailable");
                        total = AccountingInventoryCostAmountsV1.Add(total, AccountingInventoryCostAmountsV1.FromDecimal(signed));
                    }
                    continue;
                }
                if (actual.Tl is not { Status: InventoryMovementCostStampStatusV1.Known, LedgerVersion: >= 1 } stamp
                    || stamp.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira || string.IsNullOrWhiteSpace(stamp.StampVersion)
                    || stamp.Reason is not null) throw Pending("sales_cost_original_tl_unavailable");
                var exactUnit = stamp.UnitCostRatio is { } ratio
                    ? new AccountingInventoryCostRatioV1(ratio.Numerator, ratio.Denominator)
                    : stamp.UnitCostPerKg is { } scalar ? AccountingInventoryCostAmountsV1.FromDecimal(scalar) : null;
                if (!AccountingInventoryCostAmountsV1.TryRead(exactUnit, out var numerator, out _) || numerator.Sign < 0)
                    throw Pending("sales_cost_original_tl_unavailable");
                var exactAmount = AccountingInventoryCostAmountsV1.Multiply(exactUnit!, AccountingInventoryCostAmountsV1.FromDecimal(actual.QuantityKg));
                if (stamp.Amount is { } amount && AccountingInventoryCostAmountsV1.FromDecimal(amount) != exactAmount)
                    throw Pending("sales_cost_inventory_proof_unavailable");
                total = AccountingInventoryCostAmountsV1.Add(total, exactAmount);
                unposted = AccountingInventoryCostAmountsV1.Add(unposted, exactAmount);
                hasOriginalCost = true;
            }
            if (alreadyPosted.Count != 0 || alreadyPurchased.Count != 0)
            {
                if (salesClaims is null) throw Pending("sales_cost_owner_claim_unavailable");
                var owned = await salesClaims.ReadAsync(captured, [claim.ShipmentId],
                    _ => { SameCompany(captured); return Task.CompletedTask; }, cancellationToken);
                SameCompany(captured);
                var item = owned.Items.SingleOrDefault();
                var actualClaim = item?.Claim;
                if (owned.CompanyId != captured || owned.Outcome != SalesShipmentClaimReadOutcomeV1.Available
                    || item?.ShipmentId != claim.ShipmentId || item.Outcome != SalesShipmentClaimReadOutcomeV1.Available
                    || actualClaim is null || actualClaim.CompanyId != captured || actualClaim.InvoiceId != source.InvoiceId
                    || actualClaim.InvoiceLineId != line.LineId || actualClaim.InvoiceLineNumber != line.LineNo
                    || actualClaim.AdmissionId != source.AdmissionId || actualClaim.FinalizedVersion != source.Version
                    || actualClaim.FinalizedEventId != envelope.EventId
                    || actualClaim.FinalizedPayloadSha256 != AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson)
                    || actualClaim.Lifecycle.State != "Finalized" || actualClaim.Lifecycle.ReleasedAtUtc is not null
                    || actualClaim.Lifecycle.CancelledAtUtc is not null || actualClaim.Lifecycle.CancelledBy is not null
                    || JsonSerializer.Serialize(actualClaim.ShipmentSource, Json) != JsonSerializer.Serialize(claim, Json))
                    throw Pending("sales_cost_owner_claim_unavailable");
            }
            var reread = await logistics.GetAsync(captured, claim.DispatchNoteId, claim.DispatchNoteLineNo, claim.ShipmentId, cancellationToken);
            SameCompany(captured);
            if (Proof(reread, claim, captured, commercialQuantity) != proof) throw Pending("sales_cost_logistics_proof_changed");
            var body = JsonSerializer.Serialize(new { source.CompanyId, source.InvoiceId, source.Version, source.AdmissionId,
                line.LineId, line.LineNo, Claim = claim, Logistics = proof, Inventory = stock,
                AlreadyPosted = alreadyPosted.Count == 0 ? null : alreadyPosted,
                AlreadyPurchased = alreadyPurchased.Count == 0 ? null : alreadyPurchased }, Json);
            components.Add(new(AccountingInventoryCostTraceV1.SalesComponent(line.LineId), claim.ProductId, line.LineId,
                line.LineNo, null, total, body, AccountingInventoryCostSourceValidationV1.Sha256(body), physicalClaims,
                alreadyPosted.Count == 0 ? null : alreadyPosted,
                UnpostedTlAmount: hasOriginalCost && (alreadyPosted.Count != 0 || alreadyPurchased.Count != 0) ? unposted : null,
                AlreadyPostedPurchaseComponents: alreadyPurchased.Count == 0 ? null : alreadyPurchased));
        }
        var result = new AccountingInventoryCostSourceV1(captured, envelope.EventId, AccountingInventoryCostKindV1.SalesInvoicedShipment,
            source.InvoiceId, source.Version, source.AdmissionId, source.InvoiceDate.Value, source.OfficialInvoiceNo, actor,
            AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson), components);
        SameCompany(captured);
        AccountingInventoryCostSourceValidationV1.Require(envelope, result, captured);
        return result;
    }

    private static LogisticsInvoicedShipmentProofV1 Proof(LogisticsInvoicedShipmentProofReadV1? read,
        AccountingSalesInvoiceShipmentSourceV1 claim, Guid companyId, decimal commercialQuantity)
    {
        if (read is not { Outcome: LogisticsInvoicedShipmentProofOutcomeV1.Found, Proof: { } proof }
            || proof.CompanyId != companyId || proof.DispatchNoteId != claim.DispatchNoteId || proof.DispatchNoteLineNo != claim.DispatchNoteLineNo
            || proof.ShipmentId != claim.ShipmentId || proof.InstructionId == Guid.Empty || proof.InstructionLineId == Guid.Empty
            || proof.SalesOrderId != claim.SalesOrderId || proof.SalesOrderLineId != claim.SalesOrderLineId
            || proof.SourceOrderRevision < 1 || proof.ProductId != claim.ProductId || proof.UnitId != claim.UnitId
            || proof.DispatchQuantity != commercialQuantity || string.IsNullOrWhiteSpace(proof.SourceVersion))
            throw Pending("sales_cost_logistics_proof_unavailable");
        // SourceOrderRevision is the physical source revision; PriceBasisRevision is commercial and may differ.
        return proof;
    }
    private static bool Quantity(string? text, out decimal value)
    {
        value = default;
        return !string.IsNullOrWhiteSpace(text) && text == text.Trim() && decimal.TryParse(text,
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value > 0;
    }
    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId : throw Pending("company_context_mismatch");
    private void SameCompany(Guid id) { if (Company() != id) throw Pending("company_context_mismatch"); }
    private static AccountingInvoiceJournalRetryException Pending(string code) => new(code, "SMM için gerçek sevk maliyeti kanıtı bekleniyor.");
    private static OutboxPermanentFailureException Invalid(string code) => AccountingInventoryCostSourceValidationV1.Invalid(code);
}
