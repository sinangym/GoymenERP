using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService : IApprovedSalesRevisionInvoiceSourceV1
{
    public async Task<ApprovedSalesRevisionInvoiceSourceAnswerV1> ReadAsync(
        Guid orderId,
        int revision,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        scope = await RequireStillAsync(scope, bindings.PriceReadPermission, cancellationToken);
        if (orderId == Guid.Empty || revision < 0)
        {
            return Missing();
        }

        var requested = lineIds.ToArray();
        if (requested.Length == 0 || requested.Any(lineId => lineId == Guid.Empty)
            || requested.Distinct().Count() != requested.Length)
        {
            throw new ArgumentException("Fatura kaynağı en az bir farklı, boş olmayan satır kimliği ister.", nameof(lineIds));
        }

        SalesOrderDraft? draft;
        try
        {
            var guarded = await GuardedWithScopeAsync(scope, bindings.ReadPermission,
                () => store.FindDraftAsync(orderId, cancellationToken), cancellationToken);
            draft = guarded.Result;
            scope = guarded.Scope;
            scope = await RequireStillAsync(scope, bindings.PriceReadPermission, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            return Missing();
        }

        if (revision == draft.Revision)
        {
            if (draft.ApprovalState != InternalApprovalState.Approved)
            {
                return new(ApprovedSalesRevisionInvoiceSourceOutcomeV1.NotApproved, null);
            }

            var acceptance = Acceptance(draft.CustomerAcceptanceRequired, revision, draft.CustomerAcceptance);
            return acceptance is null
                ? new(ApprovedSalesRevisionInvoiceSourceOutcomeV1.CustomerAcceptanceUnavailable, null)
                : Build(draft, revision, draft.Approval, draft.Money.ProfileId, draft.Money.ProfileVersion, draft.Money.Scale,
                    acceptance, requested,
                    [.. draft.Lines.Select(line => new InvoiceLine(line.Id, line.LineNo, line.Product.ProductId,
                        line.Product.ProductVersion, line.OrderUnit.UnitId, line.QuantityText, line.Commercial))]);
        }

        var archived = draft.ArchivedRevisions.SingleOrDefault(candidate => candidate.Revision == revision);
        if (archived is null)
        {
            return Missing();
        }

        var archivedAcceptance = Acceptance(draft.CustomerAcceptanceRequired, revision, archived.CustomerAcceptance);
        return archivedAcceptance is null
            ? new(ApprovedSalesRevisionInvoiceSourceOutcomeV1.CustomerAcceptanceUnavailable, null)
            : Build(draft, revision, archived.Approval, archived.MoneyProfileId, archived.MoneyProfileVersion,
                archived.MoneyScale, archivedAcceptance, requested,
                [.. archived.Lines.Select(line => new InvoiceLine(line.LineId, line.LineNo, line.ProductId,
                    line.ProductVersion, line.OrderUnitId, line.QuantityText, line.Commercial))]);
    }

    private static ApprovedSalesRevisionInvoiceSourceAnswerV1 Build(
        SalesOrderDraft draft,
        int revision,
        ApprovalFacts approval,
        Guid moneyProfileId,
        string moneyProfileVersion,
        int moneyScale,
        SalesRevisionCustomerAcceptanceV1 acceptance,
        IReadOnlyList<Guid> requested,
        IReadOnlyList<InvoiceLine> lines)
    {
        var byId = lines.ToDictionary(line => line.LineId);
        if (requested.Any(lineId => !byId.ContainsKey(lineId)))
        {
            return Missing();
        }

        var submittedBy = approval.SubmittedBy ?? throw new InvalidOperationException("Onaylı revizyonun göndereni yok.");
        var submittedAt = approval.SubmittedAtUtc ?? throw new InvalidOperationException("Onaylı revizyonun gönderim zamanı yok.");
        var approvedBy = approval.DecidedBy ?? throw new InvalidOperationException("Onaylı revizyonun onaylayanı yok.");
        var approvedAt = approval.DecidedAtUtc ?? throw new InvalidOperationException("Onaylı revizyonun onay zamanı yok.");
        var selected = requested.Select(lineId => Line(byId[lineId])).ToArray();
        return new(ApprovedSalesRevisionInvoiceSourceOutcomeV1.Available,
            new ApprovedSalesRevisionInvoiceSourceV1(
                draft.CompanyId,
                draft.Id,
                draft.DocumentNo,
                revision,
                draft.Header.PartyId,
                draft.Header.CompanyAccountId,
                draft.Header.CurrencyId,
                draft.Header.BusinessDate,
                moneyProfileId,
                moneyProfileVersion,
                moneyScale,
                new SalesRevisionApprovalV1(submittedBy, submittedAt, approvedBy, approvedAt),
                acceptance,
                selected));
    }

    private static SalesRevisionCustomerAcceptanceV1? Acceptance(
        bool required,
        int revision,
        CustomerAcceptanceFacts? facts)
    {
        if (!required)
        {
            return facts is null ? new SalesRevisionCustomerAcceptanceV1(false, null, null, null) : null;
        }

        return facts?.Revision == revision
            ? new SalesRevisionCustomerAcceptanceV1(true, facts.Revision, facts.AcceptedBy, facts.AcceptedAtUtc)
            : null;
    }

    private static ApprovedSalesRevisionInvoiceSourceLineV1 Line(InvoiceLine line) =>
        new(
            line.LineId,
            line.LineNo,
            line.ProductId,
            line.ProductVersion,
            line.OrderUnitId,
            line.Quantity,
            line.Commercial.UnitPrice,
            line.Commercial.PriceUnitId,
            line.Commercial.DiscountPercent,
            line.Commercial.DiscountAmount,
            line.Commercial.SurchargePercent,
            line.Commercial.SurchargeAmount,
            SalesLinePriceCalculator.NetUnitPriceText(line.Commercial),
            line.Commercial.Reference is { } reference
                ? new SalesRevisionPriceReferenceV1(reference.PriceListId, reference.PriceListName, reference.VersionNo,
                    reference.ValidFrom, reference.ValidThrough, reference.CurrencyCode, reference.PriceUnitId,
                    reference.UnitPrice, reference.FloorUnitPrice, reference.VatBasis, reference.LowerQuantity, reference.UpperQuantity)
                : null,
            line.Commercial.FloorException is { } exception
                ? new SalesRevisionPriceFloorExceptionV1(exception.RequestedBy, exception.RequestedAtUtc, exception.PriceListId,
                    exception.PriceListVersionNo, exception.PriceUnitId, exception.UnitPrice, exception.NetUnitPrice,
                    exception.FloorUnitPrice, exception.ApprovedBy, exception.ApprovedAtUtc, exception.Reason)
                : null);

    private static ApprovedSalesRevisionInvoiceSourceAnswerV1 Missing() =>
        new(ApprovedSalesRevisionInvoiceSourceOutcomeV1.NotFound, null);

    private sealed record InvoiceLine(
        Guid LineId,
        int LineNo,
        Guid ProductId,
        string ProductVersion,
        Guid OrderUnitId,
        string Quantity,
        CommercialDraftFields Commercial);
}
