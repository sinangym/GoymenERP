using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Application;

public abstract record PurchaseInvoiceAcceptanceFactsV1;
public sealed record PurchaseInvoiceOrderedAcceptanceFactsV1(PurchaseAcceptanceSourceV1 Source)
    : PurchaseInvoiceAcceptanceFactsV1;
public sealed record PurchaseInvoiceUnorderedAcceptanceFactsV1(PurchaseUnorderedAcceptanceSourceV1 Source)
    : PurchaseInvoiceAcceptanceFactsV1;
public enum PurchaseInvoiceAcceptanceAdmissionOutcomeV1 { Found = 1, NotFound = 2, Unavailable = 3 }
public sealed record PurchaseInvoiceAcceptanceAdmissionResultV1(PurchaseInvoiceAcceptanceAdmissionOutcomeV1 Outcome,
    PurchaseInvoiceAcceptanceFactsV1? Facts);

/// <summary>Read-only source admission for the later ALS-D130 owner bridge. The caller supplies its native
/// company/actor/current Match permission fence; this type grants no permission and has no mutation/store/Finance port.
/// Found binds operational facts only: it is NOT a commercial price benchmark, match or finalisation approval.</summary>
public sealed class PurchaseInvoiceAcceptanceAdmissionV1(IPurchaseAcceptanceSourceV1? ordered = null,
    IPurchaseUnorderedAcceptanceSourceV1? unordered = null)
{
    public async Task<PurchaseInvoiceAcceptanceAdmissionResultV1> ReadAsync(PurchaseIncomingInvoice invoice,
        PurchaseInvoiceAcceptanceSelectionV1 selection, Func<CancellationToken, Task> requireStill,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(requireStill);
        await FenceAsync(requireStill, cancellationToken);
        if (!PurchaseInvoiceAcceptanceSelectionContractV1.IsValid(selection))
            throw new PurchasingServiceException(400, "validation_failed");
        if (invoice.Status != PurchaseIncomingInvoiceStatus.Recorded)
            throw new PurchasingServiceException(409, PurchaseIncomingInvoice.StateConflict);
        var invoiceVersion = invoice.Version;
        var line = invoice.Lines.SingleOrDefault(row => row.Id == selection.InvoiceLineId);
        if (line is null) throw new PurchasingServiceException(400, "validation_failed");
        _ = PurchaseInvoiceAcceptanceSelectionContractV1.TryQuantity(selection.Quantity, false, out var quantity);
        if (quantity > line.Quantity) throw new PurchasingServiceException(400, "validation_failed");

        PurchaseInvoiceAcceptanceAdmissionResultV1 result;
        try
        {
            result = selection switch
            {
                PurchaseInvoiceOrderedAcceptanceSelectionV1 selected => await OrderedAsync(invoice, selected, quantity, cancellationToken),
                PurchaseInvoiceUnorderedAcceptanceSelectionV1 selected => await UnorderedAsync(invoice, line, selected, quantity, cancellationToken),
                _ => Unavailable(),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            await FenceAsync(requireStill, cancellationToken);
            if (exception is PurchasingServiceException) throw;
            return Unavailable();
        }
        await FenceAsync(requireStill, cancellationToken);
        if (invoice.Version != invoiceVersion || invoice.Status != PurchaseIncomingInvoiceStatus.Recorded)
            throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
        return result;
    }

    private async Task<PurchaseInvoiceAcceptanceAdmissionResultV1> OrderedAsync(PurchaseIncomingInvoice invoice,
        PurchaseInvoiceOrderedAcceptanceSelectionV1 selected, decimal quantity, CancellationToken token)
    {
        if (ordered is null) return Unavailable();
        var source = await ordered.ReadAsync(selected.PurchaseOrderId, selected.AcceptanceId, token);
        if (source is null) return NotFound();
        if (source.CompanyId != invoice.CompanyId || source.AcceptanceId != selected.AcceptanceId
            || source.PurchaseOrderId != selected.PurchaseOrderId || source.PurchaseOrderLineId == Guid.Empty
            || !PurchaseInvoiceAcceptanceSelectionContractV1.TryVersion(source.PurchaseOrderVersionAfter, out _)
            || !PurchaseInvoiceAcceptanceSelectionContractV1.TryQuantity(source.AcceptedQuantity, false, out var accepted)
            || !PurchaseInvoiceAcceptanceSelectionContractV1.TryQuantity(source.RemainingQuantity, true, out _)
            || source.OccurredAtUtc.Offset != TimeSpan.Zero || quantity > accepted) return Unavailable();
        return new(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Found, new PurchaseInvoiceOrderedAcceptanceFactsV1(source));
    }

    private async Task<PurchaseInvoiceAcceptanceAdmissionResultV1> UnorderedAsync(PurchaseIncomingInvoice invoice,
        PurchaseInvoiceLine line, PurchaseInvoiceUnorderedAcceptanceSelectionV1 selected, decimal quantity, CancellationToken token)
    {
        if (unordered is null) return Unavailable();
        var answer = await unordered.ReadAsync(invoice.CompanyId, selected.AcceptanceId, selected.AcceptanceLineId, token);
        if (answer is null) return Unavailable();
        if (answer.Outcome == PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound && answer.Source is null) return NotFound();
        if (answer.Outcome == PurchaseUnorderedAcceptanceSourceOutcomeV1.Unavailable && answer.Source is null) return Unavailable();
        if (answer.Outcome != PurchaseUnorderedAcceptanceSourceOutcomeV1.Found || answer.Source is not { } ownerSource
            || ownerSource.ControlEvidence is null) return Unavailable();
        // Freeze before validation: the final consumer fence may await while the owner still holds its list.
        var source = ownerSource with { ControlEvidence = Array.AsReadOnly(ownerSource.ControlEvidence.ToArray()) };
        if (source.CompanyId != invoice.CompanyId || source.AcceptanceId != selected.AcceptanceId
            || source.AcceptanceLineId != selected.AcceptanceLineId || source.Version < 1
            || source.SupplierPartyId != invoice.SupplierPartyId || source.CompanyAccountId != invoice.CompanyAccountId
            || source.UnitId != line.UnitId || !SubjectMatches(source.Subject, line)
            || !PurchaseInvoiceAcceptanceSelectionContractV1.TryQuantity(source.AcceptedQuantity, false, out var accepted)
            || !PurchaseInvoiceAcceptanceSelectionContractV1.TryQuantity(source.UninvoicedQuantity, true, out var available)
            || available > accepted || quantity > available || source.OccurredAtUtc.Offset != TimeSpan.Zero
            || !ValidEvidence(source.Evidence)
            || source.ControlEvidence.Any(evidence => !ValidEvidence(evidence))
            || source.ControlEvidence.Select(evidence => (evidence.DocumentId, evidence.DocumentVersion, evidence.SourceLineId))
                .Distinct().Count() != source.ControlEvidence.Count) return Unavailable();
        _ = PurchaseInvoiceAcceptanceSelectionContractV1.TryVersion(selected.ExpectedAcceptanceVersion, out var expected);
        if (source.Version != expected) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
        return new(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Found, new PurchaseInvoiceUnorderedAcceptanceFactsV1(source));
    }

    private static bool SubjectMatches(PurchaseUnorderedAcceptanceSubjectV1? subject, PurchaseInvoiceLine line) => subject switch
    {
        PurchaseUnorderedGoodsSubjectV1 goods => goods.ProductId != Guid.Empty && goods.ProductId == line.ProductId
            && line.ServiceTypeId is null && ValidVersionToken(goods.ProductVersion),
        PurchaseUnorderedServiceSubjectV1 service => line.ProductId == Guid.Empty && service.ServiceTypeId != Guid.Empty
            && service.ServiceTypeId == line.ServiceTypeId && ValidVersionToken(service.ServiceTypeVersion),
        _ => false,
    };

    private static bool ValidVersionToken(string? value) => !string.IsNullOrWhiteSpace(value)
        && value == value.Trim() && value.Length <= PurchaseAcceptanceParticipantContractV1.ProductVersionMaximumLength;

    private static bool ValidEvidence(PurchaseAcceptanceDocumentEvidenceV1? evidence) => evidence is not null
        && evidence.DocumentId != Guid.Empty && evidence.DocumentVersion > 0 && evidence.Sha256 is { Length: 64 }
        && evidence.Sha256.All(char.IsAsciiHexDigit) && !string.IsNullOrWhiteSpace(evidence.SourceLineId)
        && evidence.SourceLineId == evidence.SourceLineId.Trim();

    private static async Task FenceAsync(Func<CancellationToken, Task> requireStill, CancellationToken token)
    { token.ThrowIfCancellationRequested(); await requireStill(token); token.ThrowIfCancellationRequested(); }
    private static PurchaseInvoiceAcceptanceAdmissionResultV1 Unavailable() => new(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Unavailable, null);
    private static PurchaseInvoiceAcceptanceAdmissionResultV1 NotFound() => new(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.NotFound, null);
}
