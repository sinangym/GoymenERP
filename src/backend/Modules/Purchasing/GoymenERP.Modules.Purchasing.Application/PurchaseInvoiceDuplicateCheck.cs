using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16, YOL-D1 "kural tabanlı mükerrer fatura kontrolü"): read-only incoming invoice duplicate check.
/// The rule is <see cref="PurchaseInvoiceDuplicateRule"/>; the answer is always a warning
/// (<see cref="PurchaseInvoiceDuplicateBehaviorV1.Warn"/>) because a match never blocks (ALS-D125; the record command asks for a reason).
/// Authority is the one of the invoice screens: the invoice permission (import before recording, read after) and order price
/// read (the rule compares amounts), both company-wide, measured before the read and fresh after it. Nothing is written.
/// </summary>
public sealed class PurchaseInvoiceDuplicateCheck(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IPurchasingInvoiceDuplicateCandidateSourceV1 candidates,
    IPurchasingIncomingInvoiceStoreV1 invoices) : IPurchaseInvoiceDuplicateCheckV1
{
    /// <summary>Response bound only (a page, not a policy): more matches set <c>Truncated</c>.</summary>
    public const int MaximumWarnings = 20;

    /// <summary>The incoming invoice screen (<c>PURCHASING_ROUTE invoiceDetail</c>).</summary>
    public const string InvoiceScreenPath = "/satinalma/faturalar";

    public async Task<PurchaseInvoiceDuplicateCheckResponseV1> CheckAsync(PurchaseInvoiceDuplicateCheckRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(PurchaseIncomingInvoicePermissionNamesV1.Import, cancellationToken);
        var probe = Probe(request);
        return await EvaluateAsync(scope, probe, null, cancellationToken);
    }

    public async Task<PurchaseInvoiceDuplicateCheckResponseV1> CheckRecordedAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(PurchaseIncomingInvoicePermissionNamesV1.Read, cancellationToken);
        if (invoiceId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        PurchaseIncomingInvoiceSnapshot? invoice;
        try { invoice = await invoices.FindIncomingInvoiceAsync(invoiceId, cancellationToken); }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or PurchasingStoreException)
        { await FenceAsync(scope, cancellationToken); throw new PurchasingServiceException(503, "service_unavailable"); }
        await FenceAsync(scope, cancellationToken);
        if (invoice is null) throw new PurchasingServiceException(404, "not_found");
        if (invoice.CompanyId != scope.Company || invoice.Id != invoiceId) throw new PurchasingServiceException(503, "service_unavailable");
        var probe = new PurchaseInvoiceDuplicateProbe(invoice.SupplierPartyId, invoice.Source.OfficialNumber, invoice.Source.Ettn,
            invoice.Source.InvoiceDate, invoice.CurrencyId, invoice.Amounts.Payable);
        return await EvaluateAsync(scope, probe, invoiceId, cancellationToken);
    }

    private async Task<PurchaseInvoiceDuplicateCheckResponseV1> EvaluateAsync(Scope scope, PurchaseInvoiceDuplicateProbe probe,
        Guid? excludeInvoiceId, CancellationToken cancellationToken)
    {
        IReadOnlyList<PurchaseInvoiceDuplicateCandidate> found;
        try { found = await candidates.ListDuplicateCandidatesAsync(probe, cancellationToken); }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or PurchasingStoreException)
        { await FenceAsync(scope, cancellationToken); throw new PurchasingServiceException(503, "service_unavailable"); }
        await FenceAsync(scope, cancellationToken);
        var byId = found.GroupBy(candidate => candidate.InvoiceId).ToDictionary(group => group.Key, group => group.First());
        var matches = PurchaseInvoiceDuplicateRule.Evaluate(probe, found, excludeInvoiceId);
        var warnings = matches.Take(MaximumWarnings).Select(match =>
        {
            var candidate = byId[match.InvoiceId];
            return new PurchaseInvoiceDuplicateWarningV1(match.InvoiceId, PurchaseInvoiceDuplicateRule.Code(match.Reason),
                candidate.OfficialNumber, candidate.InvoiceDate,
                InvoiceScreenPath + "/" + match.InvoiceId.ToString("D", CultureInfo.InvariantCulture));
        }).ToArray();
        return new(PurchaseInvoiceDuplicateBehaviorV1.Warn, warnings, matches.Count > MaximumWarnings);
    }

    private static PurchaseInvoiceDuplicateProbe Probe(PurchaseInvoiceDuplicateCheckRequestV1 request)
    {
        var errors = new List<PurchasingFieldError>();
        if (request.SupplierPartyId == Guid.Empty) errors.Add(new("supplierPartyId", "validation_failed"));
        if (string.IsNullOrWhiteSpace(request.OfficialNumber) || request.OfficialNumber.Length > 128
            || request.OfficialNumber != request.OfficialNumber.Trim()) errors.Add(new("officialNumber", "validation_failed"));
        if (request.Ettn == Guid.Empty) errors.Add(new("ettn", "validation_failed"));
        if (request.InvoiceDate == default) errors.Add(new("invoiceDate", "validation_failed"));
        if (request.CurrencyId == Guid.Empty) errors.Add(new("currencyId", "validation_failed"));
        if (!TryPayable(request.Payable, out var payable)) errors.Add(new("payable", "validation_failed"));
        if (errors.Count != 0) throw new PurchasingServiceException(400, "validation_failed", errors);
        return new(request.SupplierPartyId, request.OfficialNumber, request.Ettn ?? Guid.Empty, request.InvoiceDate,
            request.CurrencyId, payable);
    }

    private static bool TryPayable(string? text, out decimal value)
    {
        value = 0;
        return text is not null
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value > 0 && value <= PurchaseIncomingInvoice.MaximumStoredQuantity
            && decimal.Round(value, PurchaseIncomingInvoice.MaximumStoredScale) == value;
    }

    private async Task<Scope> RequireAsync(string permission, CancellationToken token)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw new PurchasingServiceException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new PurchasingServiceException(403, "forbidden");
        if (!ErpPermissions.IsKnown(permission) || !ErpPermissions.IsKnown(PurchasingPermissionManifestV1.OrdersPriceRead))
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.PermissionUnregistered);
        var companyId = company.CompanyId; var actor = user.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, token);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Same(companyId, actor)) throw new PurchasingServiceException(403, "forbidden");
        var price = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersPriceRead, companyId, token);
        if (!price.IsGranted || !price.IsCompanyWide || !Same(companyId, actor)) throw new PurchasingServiceException(403, "forbidden");
        return new(companyId, actor, permission);
    }

    private async Task FenceAsync(Scope scope, CancellationToken token)
    {
        var current = await RequireAsync(scope.Permission, token);
        if (current.Company != scope.Company || current.Actor != scope.Actor) throw new PurchasingServiceException(403, "forbidden");
    }

    private bool Same(Guid companyId, string actor) => company.IsResolved && company.CompanyId == companyId
        && user.IsAuthenticated && user.UserId == actor && user.CanAccessCompany(companyId);

    private sealed record Scope(Guid Company, string Actor, string Permission);
}
