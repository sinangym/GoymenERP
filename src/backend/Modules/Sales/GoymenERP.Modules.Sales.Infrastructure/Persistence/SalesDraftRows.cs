using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// The one mapping from a domain draft to its header, line and delivery-slice rows, as the store writes
/// them (extracted unchanged from <see cref="SalesStore"/>'s create path so that the gated fixture store
/// writes exactly the same rows; the store's Restore rebuilds both through the domain).
/// </summary>
internal static class SalesDraftRows
{
    /// <summary>Adds the draft's rows to the context; the caller saves. <paramref name="createdBy"/> and <paramref name="correlationId"/> are stored trimmed.</summary>
    internal static void Add(SalesDbContext dbContext, SalesOrderDraft draft, string createdBy, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(draft);
        var header = draft.Header;
        var money = draft.Money;
        dbContext.Drafts.Add(new DraftRow
        {
            Id = draft.Id,
            CompanyId = draft.CompanyId,
            DocumentNo = draft.DocumentNo,
            Kind = (int)draft.Kind,
            State = (int)draft.State,
            Revision = draft.Revision,
            Version = draft.Version,
            ApprovalState = (int)draft.ApprovalState,
            SubmittedBy = draft.Approval.SubmittedBy,
            SubmittedAtUtc = draft.Approval.SubmittedAtUtc,
            DecidedBy = draft.Approval.DecidedBy,
            DecidedAtUtc = draft.Approval.DecidedAtUtc,
            RejectionReason = draft.Approval.RejectionReason,
            PartyId = header.PartyId,
            CompanyAccountId = header.CompanyAccountId,
            CurrencyId = header.CurrencyId,
            BusinessDate = header.BusinessDate,
            RequestedDeliveryDate = header.RequestedDeliveryDate,
            Note = header.Note,
            DeliveryAddressId = header.DeliveryAddress?.Id,
            DeliveryAddressVersion = header.DeliveryAddress?.ExpectedVersion,
            ContactId = header.Contact?.Id,
            ContactVersion = header.Contact?.ExpectedVersion,
            ResponsibleId = header.Responsible?.Id,
            ResponsibleVersion = header.Responsible?.ExpectedVersion,
            PaymentTermsId = header.PaymentTerms?.Id,
            PaymentTermsVersion = header.PaymentTerms?.ExpectedVersion,
            SourceStrategyId = header.SourceStrategy?.Id,
            SourceStrategyVersion = header.SourceStrategy?.ExpectedVersion,
            PackageId = header.Package?.Id,
            PackageVersion = header.Package?.ExpectedVersion,
            MoneyOperation = money.Operation,
            MoneyProfileId = money.ProfileId,
            MoneyProfileVersion = money.ProfileVersion,
            MoneyEffectiveOn = money.EffectiveOn,
            MoneyIsActive = money.IsActive,
            MoneyEffectiveFrom = money.EffectiveFrom,
            MoneyEffectiveThrough = money.EffectiveThrough,
            MoneyScale = money.Scale,
            CreatedAtUtc = draft.CreatedAtUtc,
            UpdatedAtUtc = draft.UpdatedAtUtc,
            CreatedBy = createdBy.Trim(),
            CorrelationId = correlationId.Trim(),
        });
        foreach (var line in draft.Lines)
        {
            var product = line.Product;
            var unit = line.OrderUnit;
            dbContext.DraftLines.Add(new DraftLineRow
            {
                CompanyId = draft.CompanyId,
                DraftId = draft.Id,
                LineNo = line.LineNo,
                LineId = line.Id,
                ProductId = product.ProductId,
                ProductVersion = product.ProductVersion,
                ProductCode = product.Code,
                ProductName = product.Name,
                ProductEffectiveOn = product.EffectiveOn,
                StockUnitCompanyId = product.StockUnit.CompanyId,
                StockUnitId = product.StockUnit.UnitId,
                StockUnitProfileId = product.StockUnit.ProfileId,
                StockUnitProfileVersion = product.StockUnit.ProfileVersion,
                SalesUnitCompanyId = product.SalesUnit.CompanyId,
                SalesUnitId = product.SalesUnit.UnitId,
                SalesUnitProfileId = product.SalesUnit.ProfileId,
                SalesUnitProfileVersion = product.SalesUnit.ProfileVersion,
                OrderUnitBindingCompanyId = product.OrderUnit.CompanyId,
                OrderUnitBindingUnitId = product.OrderUnit.UnitId,
                OrderUnitBindingProfileId = product.OrderUnit.ProfileId,
                OrderUnitBindingProfileVersion = product.OrderUnit.ProfileVersion,
                OrderUnitCompanyId = unit.CompanyId,
                OrderUnitId = unit.UnitId,
                OrderUnitCode = unit.UnitCode,
                OrderUnitDimensionCode = unit.DimensionCode,
                OrderUnitProfileId = unit.ProfileId,
                OrderUnitProfileVersion = unit.ProfileVersion,
                OrderUnitIsKilogram = unit.IsKilogram,
                OrderUnitProfileScale = unit.ProfileScale,
                OrderUnitScale = unit.Scale,
                OrderUnitIsActive = unit.IsActive,
                OrderUnitEffectiveFrom = unit.EffectiveFrom,
                OrderUnitEffectiveThrough = unit.EffectiveThrough,
                Quantity = line.Quantity.Value,
                QuantityText = line.QuantityText,
                RequestedDeliveryDate = line.RequestedDeliveryDate,
                Note = line.Note,
                UnitPrice = line.Commercial.UnitPrice,
                PriceUnitId = line.Commercial.PriceUnitId,
                DiscountPercent = line.Commercial.DiscountPercent,
                PricingJson = SalesLinePricingJson.Serialize(line.Commercial),
            });
            var sliceNo = 0;
            foreach (var slice in line.RequestedDeliverySlices)
            {
                dbContext.DraftDeliverySlices.Add(new DraftDeliverySliceRow
                {
                    CompanyId = draft.CompanyId,
                    DraftId = draft.Id,
                    LineNo = line.LineNo,
                    SliceNo = ++sliceNo,
                    SliceId = slice.SliceId,
                    OrderUnitId = unit.UnitId,
                    Quantity = slice.Quantity.Value,
                    QuantityText = slice.QuantityText,
                    RequestedDeliveryDate = slice.RequestedDeliveryDate,
                });
            }
        }
    }
}
