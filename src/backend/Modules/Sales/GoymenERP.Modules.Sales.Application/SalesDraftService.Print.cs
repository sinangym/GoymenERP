using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>MOD-07: the price facts of the sales order PDF (see <see cref="ISalesOrderPrintReadServiceV1"/>).</summary>
public sealed partial class SalesDraftService : ISalesOrderPrintReadServiceV1
{
    public async Task<SalesOrderPrintPricingV1> GetOrderPrintPricingAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var draft = await LoadDraftAsync(scope, permission, draftId, cancellationToken);
        var version = draft.Version.ToString(CultureInfo.InvariantCulture);
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            return Withheld(version);
        }

        // The currency code is a price fact; the Settings read is fenced under the draft read like every owner read,
        // and the price read is asked again AFTER it, so a revocation in between releases nothing.
        var currencyCode = await CurrencyCodeAsync(scope, permission, draft, cancellationToken);
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            return Withheld(version);
        }

        return new SalesOrderPrintPricingV1(version, SalesPriceDisclosure.Visible, currencyCode,
            [.. draft.Lines.OrderBy(line => line.LineNo).Select(line => Pricing(line.Id, line.LineNo, line.Commercial))]);
    }

    private static SalesOrderPrintPricingV1 Withheld(string version) => new(version, SalesPriceDisclosure.Withheld, null, []);
}
