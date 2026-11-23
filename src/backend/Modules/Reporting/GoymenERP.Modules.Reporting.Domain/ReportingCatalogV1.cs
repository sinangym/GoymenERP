using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>The kind of one parameter, as the server parses it. Nothing has a default: a missing required value is refused.</summary>
public enum ReportingParameterKindV1
{
    Date = 1,
    Guid = 2,
    Choice = 3,
    WholeNumber = 4,
    Decimal = 5,
}

/// <summary>The kind of one column's cells (all cells are invariant text on the wire; numbers are lossless).</summary>
public enum ReportingColumnKindV1
{
    Text = 1,
    Number = 2,
    Date = 3,
    Instant = 4,
    Identifier = 5,
    Flag = 6,
}

public sealed record ReportingParameterDefinitionV1(
    string Id,
    ReportingParameterKindV1 Kind,
    bool Required,
    IReadOnlyList<string> Choices,
    decimal? Minimum,
    decimal? Maximum);

public sealed record ReportingColumnDefinitionV1(string Id, ReportingColumnKindV1 Kind);

/// <summary>
/// One report of the centre: its stable id, the module that owns its numbers, the owner read permissions an actor must hold
/// company-wide to see it at all, its parameters and its columns. The centre lists only reports whose every required permission
/// the actor holds now (ROL-D13/ROL-D15: no report is visible "just in case").
/// </summary>
public sealed record ReportingReportDefinitionV1(
    string Id,
    string OwnerModule,
    IReadOnlyList<string> RequiredPermissions,
    IReadOnlyList<ReportingParameterDefinitionV1> Parameters,
    IReadOnlyList<ReportingColumnDefinitionV1> Columns);

/// <summary>
/// WP14: the closed report catalogue. Report ids, parameter ids and column ids are wire names (the screen holds the Turkish
/// labels). Measures come from the decisions — OTIF (YOL-D29: on or before the promised day AND the whole quantity, no
/// tolerance), receivable/payable aging (YOL-D30: from the due date, 0-30/31-60/61-90/90+, not-yet-due apart), group company
/// (YOL-D31: the party ↔ company link on the party card). Stock aging (YOL-D39) uses the same fixed buckets from the label root's
/// first entry. A measure no decision defines would stay a required parameter.
/// </summary>
public static class ReportingCatalogV1
{
    public const string StockAging = "stock-aging";
    public const string Otif = "otif";
    public const string Recall = "recall";
    public const string Intercompany = "intercompany";
    public const string PartyAging = "party-aging";

    public const string DirectionReceivable = "receivable";
    public const string DirectionPayable = "payable";
    public const string DirectionForward = "forward";
    public const string DirectionBackward = "backward";

    /// <summary>Technical bounds (not business thresholds).</summary>
    public const int MaximumPageSize = 500;
    public const int DefaultPageSize = 50;
    public const int MaximumExportPageSize = 5000;
    public const int MaximumPeriodDays = 366;

    private static ReportingParameterDefinitionV1 Date(string id) => new(id, ReportingParameterKindV1.Date, true, [], null, null);

    private static ReportingColumnDefinitionV1 Col(string id, ReportingColumnKindV1 kind) => new(id, kind);

    public static IReadOnlyList<ReportingReportDefinitionV1> Reports { get; } =
    [
        new(StockAging, "inventory", [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead],
        [
            Date("asOf"),
            new("productId", ReportingParameterKindV1.Guid, false, [], null, null),
            new("locationId", ReportingParameterKindV1.Guid, false, [], null, null),
        ],
        [
            Col("labelCode", ReportingColumnKindV1.Text), Col("labelId", ReportingColumnKindV1.Identifier),
            Col("productId", ReportingColumnKindV1.Identifier), Col("locationId", ReportingColumnKindV1.Identifier),
            Col("heatNumber", ReportingColumnKindV1.Text), Col("coilNumber", ReportingColumnKindV1.Text),
            Col("physicalKg", ReportingColumnKindV1.Number), Col("entryDate", ReportingColumnKindV1.Date),
            Col("entryInherited", ReportingColumnKindV1.Flag), Col("ageDays", ReportingColumnKindV1.Number),
            Col("bucket", ReportingColumnKindV1.Text),
        ]),
        new(Otif, "sales", [ErpPermissions.SalesReportsRead],
        [
            Date("from"), Date("to"),
        ],
        [
            Col("orderNumber", ReportingColumnKindV1.Text), Col("orderId", ReportingColumnKindV1.Identifier),
            Col("lineNumber", ReportingColumnKindV1.Number), Col("customerId", ReportingColumnKindV1.Identifier),
            Col("unitCode", ReportingColumnKindV1.Text), Col("promisedDate", ReportingColumnKindV1.Date),
            Col("orderedQuantity", ReportingColumnKindV1.Number), Col("shippedByPromisedDate", ReportingColumnKindV1.Number),
            Col("shippedQuantity", ReportingColumnKindV1.Number), Col("inFullDate", ReportingColumnKindV1.Date),
            Col("otif", ReportingColumnKindV1.Flag),
        ]),
        new(Recall, "inventory", [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead],
        [
            new("labelId", ReportingParameterKindV1.Guid, true, [], null, null),
            new("direction", ReportingParameterKindV1.Choice, true, [DirectionForward, DirectionBackward], null, null),
        ],
        [
            Col("labelCode", ReportingColumnKindV1.Text), Col("labelId", ReportingColumnKindV1.Identifier),
            Col("step", ReportingColumnKindV1.Text), Col("occurredAt", ReportingColumnKindV1.Instant),
            Col("quantityKg", ReportingColumnKindV1.Number), Col("partyRole", ReportingColumnKindV1.Text),
            Col("partyCode", ReportingColumnKindV1.Text), Col("partyName", ReportingColumnKindV1.Text),
            Col("sourceKind", ReportingColumnKindV1.Text), Col("sourceId", ReportingColumnKindV1.Identifier),
        ]),
        new(Intercompany, "finance", [ReportingPermissionManifestV1.IntercompanyRead, ErpPermissions.FinanceOpenItemsRead,
            ErpPermissions.FinanceOpenItemsAmountRead],
        [
            Date("from"), Date("to"),
        ],
        [
            Col("documentDate", ReportingColumnKindV1.Date), Col("documentKind", ReportingColumnKindV1.Text),
            Col("sourceId", ReportingColumnKindV1.Identifier), Col("partyId", ReportingColumnKindV1.Identifier),
            Col("counterpartyCompanyId", ReportingColumnKindV1.Identifier), Col("direction", ReportingColumnKindV1.Text),
            Col("currencyId", ReportingColumnKindV1.Identifier), Col("initialAmount", ReportingColumnKindV1.Number),
            Col("outstandingAmount", ReportingColumnKindV1.Number), Col("status", ReportingColumnKindV1.Text),
        ]),
        new(PartyAging, "finance", [ErpPermissions.FinanceOpenItemsRead, ErpPermissions.FinanceOpenItemsAmountRead],
        [
            Date("asOf"),
            new("direction", ReportingParameterKindV1.Choice, true, [DirectionReceivable, DirectionPayable], null, null),
        ],
        [
            Col("partyId", ReportingColumnKindV1.Identifier), Col("companyAccountId", ReportingColumnKindV1.Identifier),
            Col("currencyId", ReportingColumnKindV1.Identifier), Col("notDue", ReportingColumnKindV1.Number),
            Col("overdue0To30", ReportingColumnKindV1.Number), Col("overdue31To60", ReportingColumnKindV1.Number),
            Col("overdue61To90", ReportingColumnKindV1.Number), Col("overdue90Plus", ReportingColumnKindV1.Number),
            Col("dueUnknown", ReportingColumnKindV1.Number), Col("total", ReportingColumnKindV1.Number),
            Col("itemCount", ReportingColumnKindV1.Number),
        ]),
    ];

    public static ReportingReportDefinitionV1? Find(string? reportId) =>
        reportId is null ? null : Reports.FirstOrDefault(report => string.Equals(report.Id, reportId, StringComparison.Ordinal));
}
