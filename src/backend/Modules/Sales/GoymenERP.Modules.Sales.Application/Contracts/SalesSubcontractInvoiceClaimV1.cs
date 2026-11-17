namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>SAT-D378 price-free owner evidence. Unavailable never means that the weighing has no invoice.</summary>
public enum SalesSubcontractInvoiceClaimStatusV1 { Unavailable = 0, Available = 1 }

public sealed record SalesSubcontractInvoiceClaimV1(
    Guid CompanyId,
    Guid LoadingInstructionId,
    Guid WeighingId,
    SalesSubcontractInvoiceClaimStatusV1 Status,
    bool? HasActiveInvoice,
    IReadOnlyList<Guid>? ActiveInvoiceIds,
    string? SourceVersion);
