using System.Globalization;
using System.Numerics;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// SAT-D306/D392: an official, finalized invoice line's net TL amount divided by the original measured stock kilograms.
/// A whole line must bill exactly one whole acceptance: no tax, price, exchange rate or partial-share allocation is invented.
/// SAT-D393 keeps missing, unfinished and non-unique invoice-cost evidence on the existing manual/approval path. Corrupt
/// evidence and a proven cost that cannot fit the return contract fail with InvoiceCostInvalid instead. This read writes nothing.
/// The consumer retains its command permission fence; this owner additionally binds every await to the original actor/company.
/// </summary>
public sealed class PurchaseSupplierReturnInvoiceCostReadPort(PurchasingDbContext database, ICompanyContext company,
    ICurrentUser user, IPurchaseGoodsReceiptReadsV1 physicalReceipts) : IPurchaseSupplierReturnInvoiceCostReadPortV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private const int UnitCostScale = 4;
    private const decimal UnitCostUpperBound = 100_000_000_000_000m;

    public async Task<PurchaseSupplierReturnInvoiceCostV1> ReadAsync(Guid companyId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = RequireScope(companyId);
        try
        {
            // GetReceiptAsync currently composes independently scoped physical reads. The normal return service asks for
            // cost BEFORE its transaction. Never pretend that an already borrowed transaction uses that separate connection.
            if (database.Database.CurrentTransaction is not null) throw Invalid();
            if (goodsAcceptanceId == Guid.Empty) return Missing();
            var share = await database.Acceptances.AsNoTracking().Where(row => row.CompanyId == companyId
                    && row.AcceptanceId == goodsAcceptanceId)
                .Select(row => new { row.OrderId, row.LineId, row.Quantity }).SingleOrDefaultAsync(cancellationToken);
            CheckScope(companyId, actor);
            if (share is null) return Missing(); // Foreign and unknown acceptances are indistinguishable.
            var acceptance = await new PurchaseAcceptanceSource(database, company).ReadAsync(share.OrderId,
                goodsAcceptanceId, cancellationToken);
            CheckScope(companyId, actor);
            if (acceptance is null || share.LineId == Guid.Empty || share.Quantity <= 0m) throw Invalid();
            var order = await database.Orders.AsNoTracking().Where(row => row.CompanyId == companyId && row.Id == share.OrderId)
                .Select(row => new { row.CurrencyId }).SingleOrDefaultAsync(cancellationToken);
            CheckScope(companyId, actor);
            var purchaseLine = await database.OrderLines.AsNoTracking().Where(row => row.CompanyId == companyId
                    && row.OrderId == share.OrderId && row.LineId == share.LineId)
                .Select(row => new { row.ProductId, row.ProductVersion, row.UnitId }).SingleOrDefaultAsync(cancellationToken);
            CheckScope(companyId, actor);
            if (order is null || purchaseLine is null) throw Invalid();

            // The binding, official identity and latest immutable revision are captured in one SQL statement.
            var sources = await (from binding in database.InvoiceReceiptBindings.AsNoTracking()
                                 join identity in database.IncomingInvoices.AsNoTracking()
                                     on new { binding.CompanyId, binding.InvoiceId } equals new { identity.CompanyId, identity.InvoiceId }
                                 join orderBinding in database.InvoiceOrderBindings.AsNoTracking()
                                     on new { binding.CompanyId, binding.InvoiceId, binding.InvoiceLineId, binding.OrderId, binding.OrderLineId }
                                     equals new { orderBinding.CompanyId, orderBinding.InvoiceId, orderBinding.InvoiceLineId, orderBinding.OrderId, orderBinding.OrderLineId }
                                 join revision in database.IncomingInvoiceRevisions.AsNoTracking()
                                     on new { binding.CompanyId, binding.InvoiceId } equals new { revision.CompanyId, revision.InvoiceId }
                                 where binding.CompanyId == companyId && binding.AcceptanceId == goodsAcceptanceId
                                     && revision.Version == database.IncomingInvoiceRevisions
                                         .Where(item => item.CompanyId == companyId && item.InvoiceId == binding.InvoiceId).Max(item => item.Version)
                                 select new { Binding = binding, Identity = identity, OrderBinding = orderBinding, Revision = revision }).ToArrayAsync(cancellationToken);
            CheckScope(companyId, actor);
            // An orphan binding is corruption, not permission to enter a manual replacement price. A concurrent newly
            // appended binding may conservatively refuse this read; a retry captures its complete immutable source.
            var bindingCount = await database.InvoiceReceiptBindings.AsNoTracking().CountAsync(row => row.CompanyId == companyId
                && row.AcceptanceId == goodsAcceptanceId, cancellationToken);
            CheckScope(companyId, actor);
            if (bindingCount != sources.Length) throw Invalid();
            if (sources.Length == 0) return Missing();
            decimal covered = 0m;
            PurchaseInvoiceLine? exactLine = null;
            PurchaseIncomingInvoice? exactInvoice = null;
            var hasPending = false;
            var wholeLine = true;
            foreach (var source in sources)
            {
                var invoice = (JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(source.Revision.SnapshotJson, Json)
                    ?? throw Invalid()).Restore();
                var binding = source.Binding;
                var identity = source.Identity;
                var match = invoice.Matches.SingleOrDefault(item => item.AcceptanceId == goodsAcceptanceId);
                var line = invoice.Lines.SingleOrDefault(item => item.Id == binding.InvoiceLineId);
                if (invoice.CompanyId != companyId || invoice.Id != binding.InvoiceId || invoice.Version != source.Revision.Version
                    || (int)invoice.Status != source.Revision.Status || binding.CompanyId != companyId || identity.CompanyId != companyId
                    || identity.SupplierPartyId != invoice.SupplierPartyId || identity.Ettn != invoice.Source.Ettn
                    || identity.OfficialNumber != invoice.Source.OfficialNumber || identity.DocumentId != invoice.Source.DocumentId
                    || identity.DocumentVersion != invoice.Source.DocumentVersion || identity.DocumentSha256 != invoice.Source.Sha256
                    || binding.OrderId != share.OrderId || binding.OrderLineId != share.LineId || binding.Quantity <= 0m
                    || match is null || line is null || match.CompanyId != companyId || match.InvoiceLineId != binding.InvoiceLineId
                    || match.Quantity != binding.Quantity || match.OrderId != share.OrderId || match.OrderLineId != share.LineId
                    || match.OrderVersion != source.OrderBinding.OrderVersion
                    || invoice.SupplierPartyId != source.OrderBinding.SupplierPartyId
                    || match.UnitId != purchaseLine.UnitId || line.UnitId != purchaseLine.UnitId
                    || line.ProductId != purchaseLine.ProductId || line.ServiceTypeId is not null || line.ExpenseTypeId is not null
                    || invoice.CurrencyId != order.CurrencyId) throw Invalid();
                covered = checked(covered + binding.Quantity);
                hasPending |= invoice.Status < PurchaseIncomingInvoiceStatus.FinancePending;
                wholeLine &= line.Quantity == binding.Quantity && invoice.Matches.Count(item => item.InvoiceLineId == line.Id) == 1;
                exactLine = line;
                exactInvoice = invoice;
            }
            if (covered > share.Quantity) throw Invalid();
            // No policy chooses one of several bills, allocates an invoice-line net to a share, or treats a pending bill as final.
            if (hasPending || covered != share.Quantity || sources.Length != 1 || !wholeLine) return Missing();
            if (exactInvoice!.FunctionalCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira) return Missing();
            var netTl = exactInvoice.CurrencyId == exactInvoice.FunctionalCurrencyId
                ? exactLine!.Amounts.Net : exactLine!.FunctionalAmounts?.Net ?? throw Invalid();
            var physical = await physicalReceipts.GetReceiptAsync(share.OrderId, goodsAcceptanceId, cancellationToken);
            CheckScope(companyId, actor);
            if (physical is null || physical.CompanyId != companyId || physical.AcceptanceId != goodsAcceptanceId
                || physical.PurchaseOrderId != share.OrderId || physical.PurchaseOrderLineId != share.LineId
                || physical.PurchaseOrderVersion != acceptance.PurchaseOrderVersionAfter
                || physical.AcceptedQuantity != acceptance.AcceptedQuantity || physical.ProductId != purchaseLine.ProductId
                || physical.ProductVersion != purchaseLine.ProductVersion || physical.UnitId != purchaseLine.UnitId
                || physical.MovementId == Guid.Empty || physical.LabelId == Guid.Empty || physical.LotId == Guid.Empty
                || !decimal.TryParse(physical.MeasuredQuantityKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kg)
                || kg <= 0m || PurchaseAcceptanceParticipantContractV1.FormatQuantity(kg) != physical.MeasuredQuantityKg) throw Invalid();
            return new PurchaseSupplierReturnInvoiceCostV1(ExactUnitCost(netTl, kg));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            CheckScope(companyId, actor); // Authority loss never leaks source or physical evidence.
            throw Invalid();
        }
    }

    private string RequireScope(Guid companyId)
    {
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(user.UserId) || !user.CanAccessCompany(companyId))
            throw new PurchasingServiceException(403, "forbidden");
        return user.UserId;
    }

    private void CheckScope(Guid companyId, string actor)
    {
        if (RequireScope(companyId) != actor) throw new PurchasingServiceException(403, "forbidden");
    }

    // Exact admission, not a new rounding rule. Decimal division alone can round a nonterminating ratio before validation.
    private static decimal ExactUnitCost(decimal netTl, decimal kg)
    {
        if (netTl <= 0m || kg <= 0m) throw Invalid();
        var (net, netScale) = Decompose(netTl);
        var (mass, massScale) = Decompose(kg);
        var numerator = net * BigInteger.Pow(10, massScale + UnitCostScale);
        var denominator = mass * BigInteger.Pow(10, netScale);
        var scaled = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder != 0 || scaled <= 0 || scaled >= new BigInteger(UnitCostUpperBound) * 10_000) throw Invalid();
        return (decimal)scaled / 10_000m;
    }

    private static (BigInteger Mantissa, int Scale) Decompose(decimal value)
    {
        var bits = decimal.GetBits(value);
        return ((new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | (uint)bits[0],
            (bits[3] >> 16) & 0xff);
    }

    private static PurchaseSupplierReturnInvoiceCostV1 Missing() => PurchaseSupplierReturnInvoiceCostV1.Unavailable;
    private static PurchasingServiceException Invalid() => new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.InvoiceCostInvalid);
}
