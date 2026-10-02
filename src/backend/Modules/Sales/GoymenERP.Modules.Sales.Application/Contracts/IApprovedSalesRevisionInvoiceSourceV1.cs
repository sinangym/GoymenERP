namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// Sales-owned, read-only commercial source for a native invoice preparation. The caller names the
/// exact order revision and line identities already carried by its immutable logistics source. The
/// implementation measures both draft-read and price-read authority; it never falls back from an
/// archived revision to the current order.
/// </summary>
public interface IApprovedSalesRevisionInvoiceSourceV1
{
    Task<ApprovedSalesRevisionInvoiceSourceAnswerV1> ReadAsync(
        Guid orderId,
        int revision,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken cancellationToken = default);
}

public enum ApprovedSalesRevisionInvoiceSourceOutcomeV1
{
    Available = 1,
    NotFound = 2,
    NotApproved = 3,
    CustomerAcceptanceUnavailable = 4,
}

public sealed record ApprovedSalesRevisionInvoiceSourceAnswerV1(
    ApprovedSalesRevisionInvoiceSourceOutcomeV1 Outcome,
    ApprovedSalesRevisionInvoiceSourceV1? Source);

public sealed record ApprovedSalesRevisionInvoiceSourceV1(
    Guid CompanyId,
    Guid OrderId,
    string OrderDocumentNo,
    int Revision,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    DateOnly BusinessDate,
    Guid MoneyProfileId,
    string MoneyProfileVersion,
    int MoneyScale,
    SalesRevisionApprovalV1 Approval,
    SalesRevisionCustomerAcceptanceV1 CustomerAcceptance,
    IReadOnlyList<ApprovedSalesRevisionInvoiceSourceLineV1> Lines);

public sealed record SalesRevisionApprovalV1(
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc);

/// <summary>
/// Required=false is the stored pre-cutover exemption on the immutable draft root; it deliberately
/// carries no invented actor or timestamp. Required=true always names the exact accepted revision.
/// </summary>
public sealed record SalesRevisionCustomerAcceptanceV1(
    bool Required,
    int? AcceptedRevision,
    string? AcceptedBy,
    DateTimeOffset? AcceptedAtUtc);

public sealed record ApprovedSalesRevisionInvoiceSourceLineV1(
    Guid LineId,
    int LineNo,
    Guid ProductId,
    string ProductVersion,
    Guid OrderUnitId,
    string Quantity,
    string? UnitPrice,
    Guid? PriceUnitId,
    string? DiscountPercent,
    string? DiscountAmount,
    string? SurchargePercent,
    string? SurchargeAmount,
    string? NetUnitPrice,
    SalesRevisionPriceReferenceV1? PriceReference,
    SalesRevisionPriceFloorExceptionV1? PriceFloorException);

public sealed record SalesRevisionPriceReferenceV1(
    Guid PriceListId,
    string PriceListName,
    int VersionNo,
    DateOnly ValidFrom,
    DateOnly? ValidThrough,
    string CurrencyCode,
    Guid PriceUnitId,
    string UnitPrice,
    string? FloorUnitPrice,
    string VatBasis,
    string LowerQuantity,
    string? UpperQuantity);

public sealed record SalesRevisionPriceFloorExceptionV1(
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    Guid PriceListId,
    int PriceListVersionNo,
    Guid PriceUnitId,
    string UnitPrice,
    string NetUnitPrice,
    string FloorUnitPrice,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? Reason);
