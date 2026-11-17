namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>ALS-D44/D59/D62/D64: consumer-owned offer after Purchasing has committed FinancePending.
/// Finance owns the single payable; this contract neither writes Finance tables nor shares a transaction.</summary>
public sealed record PurchaseInvoiceFinanceInstallmentV1(Guid OpenItemId, Guid InstallmentId, int Sequence,
    DateOnly DueOn, string Amount, string? FunctionalAmount);

public sealed record PurchaseInvoiceFinanceAdmissionRequestV1(Guid CompanyId, string ActorId, string CorrelationId,
    string? CausationId, string IdempotencyKey, Guid AdmissionId, Guid InvoiceId, string FinalizedVersion,
    Guid SupplierPartyId, Guid CompanyAccountId, Guid CurrencyId, string? OfficialRate,
    IReadOnlyList<PurchaseInvoiceFinanceInstallmentV1> Installments);

public enum PurchaseInvoiceFinanceAdmissionOutcomeV1 { Admitted = 1, Refused = 2, KeyConflict = 3, Retry = 4, Unavailable = 5 }
public sealed record PurchaseInvoiceFinanceAdmissionAnswerV1(PurchaseInvoiceFinanceAdmissionOutcomeV1 Outcome, string Reason)
{
    public static PurchaseInvoiceFinanceAdmissionAnswerV1 Unavailable { get; } =
        new(PurchaseInvoiceFinanceAdmissionOutcomeV1.Unavailable, "finance_admission_unavailable");
}

public interface IPurchaseInvoiceFinanceAdmissionPortV1
{
    Task<PurchaseInvoiceFinanceAdmissionAnswerV1> AdmitAsync(PurchaseInvoiceFinanceAdmissionRequestV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>Names only. Grant, role assignment and production activation remain human gates.</summary>
public static class PurchaseIncomingInvoicePermissionNamesV1
{
    public const string Read = "purchasing.invoices.read";
    public const string Import = "purchasing.invoices.import";
    public const string Match = "purchasing.invoices.match";
    public const string ApproveVariance = "purchasing.invoices.approve-variance";
    public const string Finalize = "purchasing.invoices.finalize";
}
