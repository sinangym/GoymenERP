using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// SAT-D295 with BS-1: the Purchasing-owned answer to <see cref="IPurchaseOrderLineCostReadPortV1"/> over Purchasing's own
/// company-filtered rows. It reads the order header (status, version, money snapshot), the asked line and — for a technical line —
/// the general line it was carved from, and answers the recorded price as stored. Untracked; creates nothing (no attempt, audit or
/// version effect). A Draft order is not an approved commitment and answers
/// <see cref="PurchaseOrderLineCostReasonsV1.OrderNotApproved"/>; a Cancelled order keeps the accepted shares received against it,
/// so its recorded price is still answered. A stored row that cannot be answered (a half money snapshot, a price above the
/// snapshot's price scale, a technical line without its general line, a company mismatch, an unknown kind or status) is
/// <see cref="PurchaseOrderLineCostReasonsV1.StoredLineUnreadable"/>, never a guessed price.
/// </summary>
internal sealed class PurchaseOrderLineCostReadPort(PurchasingDbContext dbContext, ICompanyContext companyContext)
    : IPurchaseOrderLineCostReadPortV1
{
    public async Task<PurchaseOrderLineCostAnswerV1> GetAsync(Guid companyId, Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyContext.CompanyId != companyId)
        {
            // Deny-first: without the caller's resolved company, or for another company, there is no tenant filter to read under.
            throw new InvalidOperationException("Alış sipariş satırı fiyatı yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty)
        {
            return PurchaseOrderLineCostAnswerV1.NotFound(companyId, purchaseOrderId, purchaseOrderLineId);
        }

        var order = await dbContext.Orders.AsNoTracking()
            .Where(row => row.Id == purchaseOrderId)
            .Select(row => new OrderFacts(row.CompanyId, row.Status, row.Version, row.CurrencyId, row.CurrencyCode, row.MoneyProfileId,
                row.MoneyProfileVersion, row.MoneyScale, row.UnitPriceScale))
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            // Unknown, or another company's order hidden by the tenant filter: NotFound, never 403.
            return PurchaseOrderLineCostAnswerV1.NotFound(companyId, purchaseOrderId, purchaseOrderLineId);
        }

        var line = await ReadLineAsync(purchaseOrderId, purchaseOrderLineId, cancellationToken);
        if (line is null)
        {
            return PurchaseOrderLineCostAnswerV1.NotFound(companyId, purchaseOrderId, purchaseOrderLineId);
        }

        if (order.CompanyId != companyId || line.CompanyId != companyId || order.Version < 1
            || !Enum.IsDefined(typeof(PurchaseOrderStatus), order.Status) || !Enum.IsDefined(typeof(PurchaseOrderLineKind), line.Kind))
        {
            return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
        }

        if ((PurchaseOrderStatus)order.Status == PurchaseOrderStatus.Draft)
        {
            // Fail-closed: a Draft's lines are not an approved commitment and nothing is received against them yet.
            return PurchaseOrderLineCostAnswerV1.Unreadable(companyId, purchaseOrderId, purchaseOrderLineId, order.Version,
                PurchaseOrderLineCostReasonsV1.OrderNotApproved);
        }

        var priced = line;
        if ((PurchaseOrderLineKind)line.Kind == PurchaseOrderLineKind.Technical)
        {
            // A2a-1: a technical line carries no price of its own; its quantity is priced on its general line, in the same unit.
            if (line.ParentLineId is not { } parentLineId || parentLineId == Guid.Empty || HasAnyPriceFact(line))
            {
                return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
            }

            var general = await ReadLineAsync(purchaseOrderId, parentLineId, cancellationToken);
            if (general is null || general.CompanyId != companyId || general.Kind != (int)PurchaseOrderLineKind.General
                || general.ParentLineId is not null || general.UnitId != line.UnitId || general.UnitIsKilogram != line.UnitIsKilogram)
            {
                return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
            }

            priced = general;
        }
        else if (line.ParentLineId is not null)
        {
            return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
        }

        if (priced.UnitPrice is not { } unitPrice || priced.PriceUnitId is not { } priceUnitId)
        {
            if (HasAnyPriceFact(priced))
            {
                // A half price (a discount or a surcharge without the unit price and its unit) is never answered.
                return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
            }

            // ALS-D18: "fiyat bekliyor" — the line is recorded without a price yet.
            return PurchaseOrderLineCostAnswerV1.Unreadable(companyId, purchaseOrderId, purchaseOrderLineId, order.Version,
                PurchaseOrderLineCostReasonsV1.PricePending);
        }

        if (order.CurrencyId is not { } currencyId || order.CurrencyCode is not { } currencyCode
            || order.MoneyProfileId is not { } profileId || order.MoneyProfileVersion is not { } profileVersion
            || order.MoneyScale is not { } scale)
        {
            // A price is written only beside the order's full money snapshot (A2a-1).
            return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
        }

        try
        {
            var money = PurchaseOrderMoney.Of(currencyId, currencyCode, profileId, profileVersion, scale, order.UnitPriceScale);
            if (!Money.TryOf(unitPrice, money.CurrencyId, money.PriceScale, out var unitPriceMoney))
            {
                return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
            }

            var price = PurchaseOrderLineCostPriceV1.Of(priced.LineId, line.ProductId, line.UnitId, priceUnitId, priced.UnitIsKilogram,
                unitPriceMoney, money.CurrencyCode, priced.DiscountPercent, priced.DiscountAmount, priced.DeferralPercent,
                priced.DeferralAmount);
            return PurchaseOrderLineCostAnswerV1.Known(companyId, purchaseOrderId, purchaseOrderLineId, order.Version, price);
        }
        catch (ArgumentException)
        {
            return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
        }
        catch (OverflowException)
        {
            return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
        }
        catch (MoneyRuleException)
        {
            return Stored(companyId, purchaseOrderId, purchaseOrderLineId);
        }
    }

    private async Task<LineFacts?> ReadLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken) =>
        await dbContext.OrderLines.AsNoTracking()
            .Where(row => row.OrderId == orderId && row.LineId == lineId)
            .Select(row => new LineFacts(row.CompanyId, row.LineId, row.Kind, row.ParentLineId, row.ProductId, row.UnitId,
                row.UnitIsKilogram, row.UnitPrice, row.PriceUnitId, row.DiscountPercent, row.DiscountAmount, row.DeferralPercent,
                row.DeferralAmount))
            .SingleOrDefaultAsync(cancellationToken);

    private static bool HasAnyPriceFact(LineFacts line) =>
        line.UnitPrice is not null || line.PriceUnitId is not null || line.DiscountPercent is not null || line.DiscountAmount is not null
        || line.DeferralPercent is not null || line.DeferralAmount is not null;

    private static PurchaseOrderLineCostAnswerV1 Stored(Guid companyId, Guid purchaseOrderId, Guid purchaseOrderLineId) =>
        PurchaseOrderLineCostAnswerV1.Unreadable(companyId, purchaseOrderId, purchaseOrderLineId, null,
            PurchaseOrderLineCostReasonsV1.StoredLineUnreadable);

    private sealed record OrderFacts(
        Guid CompanyId,
        int Status,
        long Version,
        Guid? CurrencyId,
        string? CurrencyCode,
        Guid? MoneyProfileId,
        long? MoneyProfileVersion,
        int? MoneyScale,
        int? UnitPriceScale);

    private sealed record LineFacts(
        Guid CompanyId,
        Guid LineId,
        int Kind,
        Guid? ParentLineId,
        Guid ProductId,
        Guid UnitId,
        bool UnitIsKilogram,
        decimal? UnitPrice,
        Guid? PriceUnitId,
        decimal? DiscountPercent,
        decimal? DiscountAmount,
        decimal? DeferralPercent,
        decimal? DeferralAmount);
}
