namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>
/// How an owner answered a report source read. Only <see cref="Found"/> carries rows; everything else is fail-closed and never
/// shown as an empty report. <see cref="NotConnected"/> is a source no owner answers yet (the report exists, its numbers do not).
/// </summary>
public enum ReportingSourceOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
    NotConnected = 3,
    NotFound = 4,
}

/// <summary>
/// The report centre's source ports. Each is declared here and answered by the OWNER module through a host adapter that calls
/// the owner's own read port: Reporting never reads another module's tables. A port carries no authority (the centre measures
/// the report's required permissions before and after asking) and creates nothing.
/// </summary>
public interface IReportingStockAgingSourceV1
{
    Task<ReportingStockAgingSourcePageV1> ReadAsync(Guid companyId, int page, int pageSize, Guid? productId, Guid? locationId,
        CancellationToken cancellationToken = default);
}

public sealed record ReportingStockAgingSourceRowV1(
    Guid LabelId,
    string LabelCode,
    Guid ProductId,
    Guid LocationId,
    string? HeatNumber,
    string? CoilNumber,
    string PhysicalKg,
    DateTimeOffset? EntryAtUtc,
    bool EntryInherited);

public sealed record ReportingStockAgingSourcePageV1(
    ReportingSourceOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<ReportingStockAgingSourceRowV1> Items,
    bool HasMore);

/// <summary>OTIF lines whose promised delivery day is in [from, to]; dated shipments in the line's own unit.</summary>
public interface IReportingOtifSourceV1
{
    Task<ReportingOtifSourcePageV1> ReadAsync(Guid companyId, DateOnly from, DateOnly to, int page, int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed record ReportingOtifSourceLineV1(
    Guid OrderId,
    string OrderNumber,
    int LineNumber,
    Guid CustomerId,
    string UnitCode,
    DateOnly PromisedDate,
    decimal OrderedQuantity,
    IReadOnlyList<ReportingOtifShipmentV1> Shipments);

public sealed record ReportingOtifSourcePageV1(
    ReportingSourceOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<ReportingOtifSourceLineV1> Lines,
    bool HasMore);

/// <summary>The label trace (recall / traceability) as the stock owner walks it.</summary>
public interface IReportingRecallSourceV1
{
    Task<ReportingRecallSourceV1> ReadAsync(Guid companyId, Guid labelId, bool forward, CancellationToken cancellationToken = default);
}

public sealed record ReportingRecallSourceRowV1(
    Guid LabelId,
    string LabelCode,
    string Step,
    DateTimeOffset OccurredAtUtc,
    string QuantityKg,
    string? PartyRole,
    string? PartyCode,
    string? PartyName,
    string SourceKind,
    Guid SourceId);

/// <summary><paramref name="Statuses"/> are the owner's own enrichment statuses (production, party, party name) passed through.</summary>
public sealed record ReportingRecallSourceV1(
    ReportingSourceOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<ReportingRecallSourceRowV1> Rows,
    bool WindowFull,
    IReadOnlyList<KeyValuePair<string, string>> Statuses);

/// <summary>
/// ROL-D15 / EK-40 / YOL-D31: the company's intercompany legs — documents with a party whose card carries a party ↔ company
/// link (never a tax-number match). No owner records that link yet, so the host answers
/// <see cref="ReportingSourceOutcomeV1.NotConnected"/> — never a guessed counterparty match.
/// </summary>
public interface IReportingIntercompanySourceV1
{
    Task<ReportingIntercompanySourcePageV1> ReadAsync(Guid companyId, DateOnly from, DateOnly to, int page, int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed record ReportingIntercompanySourceRowV1(
    DateOnly DocumentDate,
    string DocumentKind,
    string DocumentNumber,
    Guid CounterpartyCompanyId,
    string CurrencyCode,
    string NetAmount);

public sealed record ReportingIntercompanySourcePageV1(
    ReportingSourceOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<ReportingIntercompanySourceRowV1> Rows,
    bool HasMore);

/// <summary>YOL-D30: the company's OPEN items of one direction (Finance's sub-ledger), with their due date and outstanding money.</summary>
public interface IReportingPartyAgingSourceV1
{
    /// <summary>The technical read bound (not a business threshold): more open items set <c>WindowFull</c>.</summary>
    const int MaximumItems = 10000;

    Task<ReportingPartyAgingSourceV1> ReadAsync(Guid companyId, bool receivable, CancellationToken cancellationToken = default);
}

public sealed record ReportingPartyAgingItemV1(Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly? DueOn, string OutstandingAmount);

public sealed record ReportingPartyAgingSourceV1(
    ReportingSourceOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<ReportingPartyAgingItemV1> Items,
    bool WindowFull);

/// <summary>YOL-D13 auditor package section "mizan": the Accounting owner's trial balance (its own authority applies too).</summary>
public interface IReportingTrialBalanceSourceV1
{
    Task<ReportingTrialBalanceSourceV1> ReadAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public sealed record ReportingTrialBalanceSourceRowV1(Guid AccountId, Guid CurrencyId, string Debit, string Credit, string BalanceSide,
    string Balance);

public sealed record ReportingTrialBalanceSourceV1(
    ReportingSourceOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<ReportingTrialBalanceSourceRowV1> Rows,
    string? SourceVersion);

/// <summary>Defaults registered by the module: a source the host has not replaced answers "not connected", never "nothing".</summary>
public sealed class ReportingNotConnectedSourcesV1 : IReportingStockAgingSourceV1, IReportingOtifSourceV1, IReportingRecallSourceV1,
    IReportingIntercompanySourceV1, IReportingTrialBalanceSourceV1, IReportingPartyAgingSourceV1
{
    public Task<ReportingPartyAgingSourceV1> ReadAsync(Guid companyId, bool receivable, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportingPartyAgingSourceV1(ReportingSourceOutcomeV1.NotConnected, companyId, [], false));

    public Task<ReportingStockAgingSourcePageV1> ReadAsync(Guid companyId, int page, int pageSize, Guid? productId, Guid? locationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportingStockAgingSourcePageV1(ReportingSourceOutcomeV1.NotConnected, companyId, [], false));

    public Task<ReportingOtifSourcePageV1> ReadAsync(Guid companyId, DateOnly from, DateOnly to, int page, int pageSize,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportingOtifSourcePageV1(ReportingSourceOutcomeV1.NotConnected, companyId, [], false));

    public Task<ReportingRecallSourceV1> ReadAsync(Guid companyId, Guid labelId, bool forward, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportingRecallSourceV1(ReportingSourceOutcomeV1.NotConnected, companyId, [], false, []));

    Task<ReportingIntercompanySourcePageV1> IReportingIntercompanySourceV1.ReadAsync(Guid companyId, DateOnly from, DateOnly to, int page,
        int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new ReportingIntercompanySourcePageV1(ReportingSourceOutcomeV1.NotConnected, companyId, [], false));

    public Task<ReportingTrialBalanceSourceV1> ReadAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportingTrialBalanceSourceV1(ReportingSourceOutcomeV1.NotConnected, companyId, [], null));
}
