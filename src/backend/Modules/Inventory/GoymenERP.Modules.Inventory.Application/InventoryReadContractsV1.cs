using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// T07(a): what an authorized reader of the server-resolved company sees of one label. Identity,
/// product, lot and the piece's own facts; the document requirement and its status (Ö3); the print
/// and document history as sequence, time and kind only. Actor identities are audit's, behind
/// audit's own permission, so they are not repeated here. <see cref="Lot"/> is the label's lot record
/// (DEC-PRD-003, Business #66 5734060341): its own heat and coil, which are separate real values and
/// never derived from the label's (null = unknown).
/// </summary>
public sealed record InventoryLabelViewV1(
    Guid LabelId,
    Guid CompanyId,
    string Code,
    Guid ProductId,
    Guid LotId,
    string? HeatNumber,
    string? CoilNumber,
    string DocumentStatus,
    IReadOnlyList<string> RequiredDocuments,
    IReadOnlyList<InventoryLabelDocumentV1> Documents,
    IReadOnlyList<InventoryLabelPrintV1> Prints,
    InventoryLotV1 Lot);

/// <summary>The label's lot: its identity and its own heat and coil, null when unknown (never empty or 0).</summary>
public sealed record InventoryLotV1(Guid Id, string? HeatNumber, string? CoilNumber);

public sealed record InventoryLabelDocumentV1(string Kind, DateTimeOffset AttachedAtUtc);

public sealed record InventoryLabelPrintV1(int Sequence, DateTimeOffset PrintedAtUtc, string? Reason);

/// <summary>
/// T07(a): the stock of one label, position by position. Kilograms are lossless invariant text
/// without trailing zeros (Ö2); totals are sums of the positions shown, for the label's owner(s) as
/// stored. A label with no stock has no positions and every total "0".
/// </summary>
public sealed record InventoryLabelBalancesV1(
    Guid LabelId,
    string PhysicalKg,
    string FreeKg,
    string BlockedKg,
    IReadOnlyList<InventoryPositionV1> Positions);

public sealed record InventoryPositionV1(
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    Guid ProductId,
    string Block,
    string QuantityKg);

/// <summary>Maps the domain's label and stored balances to the V1 read shapes. No rule is decided here.</summary>
public static class InventoryReadModelsV1
{
    public static InventoryLabelViewV1 LabelView(MaterialLabel label, MaterialLot lot)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(lot);
        if (lot.Id != label.LotId || lot.CompanyId != label.CompanyId)
        {
            throw new ArgumentException("Lot bu etiketin lotu değil.", nameof(lot));
        }

        return new InventoryLabelViewV1(
            label.Id,
            label.CompanyId,
            label.Code,
            label.ProductId,
            label.LotId,
            label.HeatNumber,
            label.CoilNumber,
            label.DocumentStatus.ToString(),
            [.. label.RequiredDocuments.Order(StringComparer.Ordinal)],
            [.. label.DocumentAttachments.Select(document => new InventoryLabelDocumentV1(document.Kind, document.AttachedAtUtc))],
            [.. label.Prints.Select(print => new InventoryLabelPrintV1(print.Sequence, print.PrintedAtUtc, print.Reason))],
            new InventoryLotV1(lot.Id, lot.HeatNumber, lot.CoilNumber));
    }

    public static InventoryLabelBalancesV1 Balances(Guid labelId, IReadOnlyList<StockBalance> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);
        if (balances.Any(balance => balance.Position.LabelId != labelId))
        {
            throw new ArgumentException("Bakiyeler istenen etiketin değil.", nameof(balances));
        }

        var shown = balances.Where(balance => balance.Quantity.Value != 0m).ToArray();
        decimal Sum(Func<StockBalance, bool> which) => shown.Where(which).Sum(balance => balance.Quantity.Value);
        return new InventoryLabelBalancesV1(
            labelId,
            Kilograms.Of(Sum(_ => true)).ToString(),
            Kilograms.Of(Sum(balance => balance.Position.Block == BlockState.Free)).ToString(),
            Kilograms.Of(Sum(balance => balance.Position.Block == BlockState.Blocked)).ToString(),
            [.. shown
                .OrderBy(balance => balance.Position.LocationId)
                .ThenBy(balance => balance.Position.Owner.Kind).ThenBy(balance => balance.Position.Owner.Id)
                .ThenBy(balance => balance.Position.Custodian.Kind).ThenBy(balance => balance.Position.Custodian.Id)
                .ThenBy(balance => balance.Position.Block)
                .Select(balance => new InventoryPositionV1(
                    balance.Position.Owner.Kind.ToString(),
                    balance.Position.Owner.Id,
                    balance.Position.Custodian.Kind.ToString(),
                    balance.Position.Custodian.Id,
                    balance.Position.LocationId,
                    balance.Position.ProductId,
                    balance.Position.Block.ToString(),
                    balance.Quantity.ToString()))]);
    }
}
