using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using System.Text.Json;

namespace GoymenERP.Modules.Purchasing.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>purchasing.invoice-variances.v1</c> (gelen fatura farkları): incoming purchase invoices whose match
/// against goods receipts left quantity or price differences, by default those still waiting for the variance decision.
/// Read ONLY through the Purchasing owner's invoice workspace (<see cref="IPurchaseIncomingInvoiceWorkspaceV1.ListAsync"/>,
/// which measures <c>purchasing.invoices.read</c> and <c>purchasing.orders.price.read</c> company-wide around every read);
/// the tool measures the same names for the server company before and after.
/// <para>
/// CONFIDENTIAL: a price difference is money (maliyet/fiyat = hassas). Expected, actual and difference are the owner's
/// exact texts; price differences are totalled PER CURRENCY (never across currencies), quantity differences are only
/// counted (each is in its own line's unit; no kg ↔ piece conversion). The decision's approver and reason are not
/// released. Over the scan cap counts and totals are withheld and <c>complete</c> is false.
/// </para>
/// </summary>
public sealed class PurchasingInvoiceVariancesAiTool(IPurchaseIncomingInvoiceWorkspaceV1 invoices,
    IErpAuthorityRevalidationService authority, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "purchasing.invoice-variances.v1";
    public const string ToolVersion = "1";
    public const string DecisionField = "decision";
    public const string LimitField = "limit";
    public const string PendingDecision = "pending";
    public const string DecidedDecision = "decided";
    public const string AnyDecision = "any";
    public const string QuantityKind = "Quantity";
    public const string PriceKind = "Price";
    public const int DefaultLimit = 10;
    public const int MaximumLimit = 25;
    public const int MaximumPages = 40;
    public const string TargetKind = "purchasing.incoming-invoices.variances";
    private static readonly string[] Fields = [DecisionField, LimitField];
    private static readonly string[] Decisions = [AnyDecision, DecidedDecision, PendingDecision];
    private static readonly string[] Permissions = [PurchaseIncomingInvoicePermissionNamesV1.Read, PurchasingPermissionManifestV1.OrdersPriceRead];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Purchasing",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential,
        requiredPermissions: Permissions,
        fieldAllowlist: Fields,
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "purchasing.invoice-variances.v1/input@1",
            OutputSchema: "purchasing.invoice-variances.v1/output@1",
            FieldPolicy: "purchasing.invoice-variances.v1/fields@1",
            LimitPolicy: "purchasing.invoice-variances.v1/limit-25-pages-40@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+purchasing.invoices.read+purchasing.orders.price.read@1",
            SourceContract: "IPurchaseIncomingInvoiceWorkspaceV1.ListAsync@v1",
            RedactionPolicy: "purchasing.invoice-variances.v1/no-approver-no-reason@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryChoice(DecisionField, Decisions, out var decision)) return Invalid(DecisionField);
        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);
        decision ??= PendingDecision;

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        var read = new List<PurchaseIncomingInvoiceViewV1>();
        var complete = true;
        try
        {
            for (var pageIndex = 0; ; pageIndex++)
            {
                if (pageIndex >= MaximumPages)
                {
                    complete = false;
                    break;
                }

                var page = await invoices.ListAsync(null, pageIndex, cancellationToken);
                if (page.PageIndex != pageIndex)
                {
                    return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }

                foreach (var invoice in page.Items)
                {
                    if (invoice.CompanyId != context.CompanyId)
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    read.Add(invoice);
                }

                if (!page.HasNext)
                {
                    break;
                }
            }
        }
        catch (PurchasingServiceException exception) when (exception.Status is 401 or 403)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } after)
        {
            return new AiReadResultV1(after);
        }

        if (read.Select(invoice => invoice.Id).Distinct().Count() != read.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var selected = new List<PurchasingInvoiceVarianceInvoiceV1>();
        var priceByCurrency = new Dictionary<Guid, decimal>();
        var quantityCount = 0;
        var priceCount = 0;
        try
        {
            foreach (var invoice in read.Where(item => item.Variances.Count > 0))
            {
                var decided = invoice.VarianceDecision is not null;
                if ((decision == PendingDecision && decided) || (decision == DecidedDecision && !decided))
                {
                    continue;
                }

                var units = invoice.Lines.ToDictionary(line => line.Id, line => line.UnitId);
                var variances = new List<PurchasingInvoiceVarianceItemV1>();
                foreach (var variance in invoice.Variances)
                {
                    if (variance.Kind is not (QuantityKind or PriceKind) || !units.TryGetValue(variance.InvoiceLineId, out var unitId)
                        || !AiExactDecimalV1.TryParse(variance.Expected, out var expected)
                        || !AiExactDecimalV1.TryParse(variance.Actual, out var actual))
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    var difference = checked(actual - expected);
                    if (variance.Kind == PriceKind)
                    {
                        priceCount++;
                        priceByCurrency[invoice.CurrencyId] = checked(priceByCurrency.GetValueOrDefault(invoice.CurrencyId) + difference);
                    }
                    else
                    {
                        quantityCount++;
                    }

                    variances.Add(new PurchasingInvoiceVarianceItemV1(variance.InvoiceLineId, variance.Kind,
                        variance.Kind == QuantityKind ? unitId : null, variance.Kind == PriceKind ? invoice.CurrencyId : null,
                        AiExactDecimalV1.Format(expected), AiExactDecimalV1.Format(actual), AiExactDecimalV1.Format(difference)));
                }

                selected.Add(new PurchasingInvoiceVarianceInvoiceV1(invoice.Id, invoice.OfficialNumber, invoice.InvoiceDate,
                    invoice.SupplierPartyId, invoice.Status, invoice.CurrencyId, decided, variances));
            }
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = selected.OrderBy(item => item.InvoiceDate).ThenBy(item => item.InvoiceId).ToArray();
        var listed = ordered.Take(limit).ToArray();
        var content = new PurchasingInvoiceVariancesContentV1(
            decision,
            complete,
            complete ? ordered.Length : null,
            complete ? quantityCount : null,
            complete ? priceCount : null,
            complete
                ? priceByCurrency.OrderBy(pair => pair.Key)
                    .Select(pair => new PurchasingCurrencyTotalV1(pair.Key, AiExactDecimalV1.Format(pair.Value))).ToArray()
                : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Gelen fatura farkları",
            AiSourceVersionV1.Of(read.OrderBy(item => item.Id).Select(item => $"{item.Id:D}:{item.Version}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Confidential);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

/// <summary>The exact sum of (actual − expected) of the price differences in one currency.</summary>
public sealed record PurchasingCurrencyTotalV1(Guid CurrencyId, string PriceDifferenceTotal);

/// <summary>Counts and totals are null when <c>Complete</c> is false.</summary>
public sealed record PurchasingInvoiceVariancesContentV1(
    string Decision,
    bool Complete,
    int? InvoiceCount,
    int? QuantityVarianceCount,
    int? PriceVarianceCount,
    IReadOnlyList<PurchasingCurrencyTotalV1>? PriceDifferenceByCurrency,
    int ListedCount,
    IReadOnlyList<PurchasingInvoiceVarianceInvoiceV1> Items);

public sealed record PurchasingInvoiceVarianceInvoiceV1(
    Guid InvoiceId,
    string OfficialNumber,
    DateOnly InvoiceDate,
    Guid SupplierPartyId,
    string Status,
    Guid CurrencyId,
    bool Decided,
    IReadOnlyList<PurchasingInvoiceVarianceItemV1> Variances);

/// <summary>A quantity difference names its line's unit; a price difference names the invoice currency.</summary>
public sealed record PurchasingInvoiceVarianceItemV1(
    Guid InvoiceLineId,
    string Kind,
    Guid? UnitId,
    Guid? CurrencyId,
    string Expected,
    string Actual,
    string Difference);
