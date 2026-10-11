using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Api.WorkRuntime;

internal static class NativeApprovalKindsV1
{
    public const string FinanceOfficialInvoiceImport = "finance.official-invoice-import.v1";
    public const string InventoryStockCount = "inventory.stock-count.v1";
    public const string InventoryStockScrap = "inventory.stock-scrap.v1";
    public const string SalesOrderDraft = "sales.order-draft.v1";

    public static bool IsKnown(string kind) =>
        string.Equals(kind, FinanceOfficialInvoiceImport, StringComparison.Ordinal)
        || string.Equals(kind, InventoryStockCount, StringComparison.Ordinal)
        || string.Equals(kind, InventoryStockScrap, StringComparison.Ordinal)
        || string.Equals(kind, SalesOrderDraft, StringComparison.Ordinal);
}

internal sealed record NativeApprovalReadContextV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    bool IncludeInventoryLabelCode = false);

internal static class NativeApprovalReadContractV1
{
    public const string Header = "X-ERP-Work-Approval-Contract";
    public const string InventoryLabelDisplayVersion = "2.0";
}

internal enum NativeApprovalReadOutcomeV1
{
    Found,
    NotFound,
    Denied,
    Unavailable,
}

internal sealed record NativeApprovalReadResultV1<T>(
    NativeApprovalReadOutcomeV1 Outcome,
    T? Resource) where T : class;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FinanceInvoiceImportNativeApprovalReadItemV1),
    NativeApprovalKindsV1.FinanceOfficialInvoiceImport)]
[JsonDerivedType(typeof(InventoryStockCountNativeApprovalReadItemV1),
    NativeApprovalKindsV1.InventoryStockCount)]
[JsonDerivedType(typeof(InventoryStockScrapNativeApprovalReadItemV1),
    NativeApprovalKindsV1.InventoryStockScrap)]
[JsonDerivedType(typeof(SalesOrderDraftNativeApprovalReadItemV1),
    NativeApprovalKindsV1.SalesOrderDraft)]
internal abstract record NativeApprovalReadItemV1(
    [property: JsonPropertyOrder(1)] Guid ApprovalId,
    [property: JsonPropertyOrder(2)] Guid CompanyId,
    [property: JsonPropertyOrder(3)] string Status,
    [property: JsonPropertyOrder(4)] string Version,
    [property: JsonPropertyOrder(9)] string SubmittedBy,
    [property: JsonPropertyOrder(10)] DateTimeOffset SubmittedAtUtc)
{
    [JsonIgnore]
    public abstract string Kind { get; }
}

internal sealed record SalesOrderDraftNativeApprovalReadItemV1(
    Guid ApprovalId,
    Guid CompanyId,
    string Status,
    string Version,
    [property: JsonPropertyOrder(5)] string Revision,
    [property: JsonPropertyOrder(6)] string DocumentNo,
    [property: JsonPropertyOrder(7)] bool IsOwnDraft,
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc)
    : NativeApprovalReadItemV1(
        ApprovalId,
        CompanyId,
        Status,
        Version,
        SubmittedBy,
        SubmittedAtUtc)
{
    [JsonIgnore]
    public override string Kind => NativeApprovalKindsV1.SalesOrderDraft;
}

internal sealed record FinanceInvoiceImportNativeApprovalReadItemV1(
    Guid ApprovalId,
    Guid CompanyId,
    string Status,
    string Version,
    [property: JsonPropertyOrder(5)] string BatchVersion,
    [property: JsonPropertyOrder(6)] string Checksum,
    [property: JsonPropertyOrder(7)] string TemplateVersion,
    [property: JsonPropertyOrder(8)] int ItemCount,
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc)
    : NativeApprovalReadItemV1(
        ApprovalId,
        CompanyId,
        Status,
        Version,
        SubmittedBy,
        SubmittedAtUtc)
{
    [JsonIgnore]
    public override string Kind => NativeApprovalKindsV1.FinanceOfficialInvoiceImport;
}

internal sealed record InventoryStockCountNativeApprovalReadItemV1(
    Guid ApprovalId,
    Guid CompanyId,
    string Status,
    string Version,
    [property: JsonPropertyOrder(5)] Guid LabelId,
    [property: JsonPropertyOrder(6)] Guid ProductId,
    [property: JsonPropertyOrder(7)] Guid LocationId,
    [property: JsonPropertyOrder(8)] string DifferenceKg,
    [property: JsonPropertyOrder(11)] string LabelVersion,
    [property: JsonPropertyOrder(12)] string OwnerKind,
    [property: JsonPropertyOrder(13)] Guid OwnerId,
    [property: JsonPropertyOrder(14)] string CustodianKind,
    [property: JsonPropertyOrder(15)] Guid CustodianId,
    [property: JsonPropertyOrder(16)] string Block,
    [property: JsonPropertyOrder(17)] string BookKg,
    [property: JsonPropertyOrder(18)] string CountedKg,
    [property: JsonPropertyOrder(19)] string Reason,
    [property: JsonPropertyOrder(20),
     JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LabelCode,
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc)
    : NativeApprovalReadItemV1(
        ApprovalId,
        CompanyId,
        Status,
        Version,
        SubmittedBy,
        SubmittedAtUtc)
{
    [JsonIgnore]
    public override string Kind => NativeApprovalKindsV1.InventoryStockCount;
}

internal sealed record InventoryStockScrapNativeApprovalReadItemV1(
    Guid ApprovalId,
    Guid CompanyId,
    string Status,
    string Version,
    [property: JsonPropertyOrder(5)] Guid LabelId,
    [property: JsonPropertyOrder(6)] Guid ProductId,
    [property: JsonPropertyOrder(7)] Guid LocationId,
    [property: JsonPropertyOrder(8)] string ScrapKg,
    [property: JsonPropertyOrder(11)] string LabelVersion,
    [property: JsonPropertyOrder(12)] string OwnerKind,
    [property: JsonPropertyOrder(13)] Guid OwnerId,
    [property: JsonPropertyOrder(14)] string CustodianKind,
    [property: JsonPropertyOrder(15)] Guid CustodianId,
    [property: JsonPropertyOrder(16)] string Block,
    [property: JsonPropertyOrder(17)] string BookKg,
    [property: JsonPropertyOrder(18)] string RemainingKg,
    [property: JsonPropertyOrder(19)] string Reason,
    [property: JsonPropertyOrder(20),
     JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LabelCode,
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc)
    : NativeApprovalReadItemV1(
        ApprovalId,
        CompanyId,
        Status,
        Version,
        SubmittedBy,
        SubmittedAtUtc)
{
    [JsonIgnore]
    public override string Kind => NativeApprovalKindsV1.InventoryStockScrap;
}

internal sealed record NativeApprovalReadPageV1(
    Guid CompanyId,
    IReadOnlyList<NativeApprovalReadItemV1> Items,
    string? NextCursor);

[JsonDerivedType(typeof(FinanceInvoiceImportNativeApprovalReadDetailV1))]
[JsonDerivedType(typeof(InventoryStockCountNativeApprovalReadDetailV1))]
[JsonDerivedType(typeof(InventoryStockScrapNativeApprovalReadDetailV1))]
[JsonDerivedType(typeof(SalesOrderDraftNativeApprovalReadDetailV1))]
internal abstract record NativeApprovalReadDetailV1(
    [property: JsonPropertyOrder(0)] NativeApprovalReadItemV1 Item);

internal sealed record FinanceInvoiceImportNativeApprovalReadDetailV1(
    NativeApprovalReadItemV1 Item,
    [property: JsonPropertyOrder(1)]
    GoymenERP.Modules.Finance.Contracts.FinanceOfficialInvoiceImportPreviewV1 Preview)
    : NativeApprovalReadDetailV1(Item);

internal sealed record SalesOrderDraftNativeApprovalReadDetailV1(
    NativeApprovalReadItemV1 Item)
    : NativeApprovalReadDetailV1(Item);

internal sealed record InventoryStockCountNativeApprovalReadDetailV1(
    NativeApprovalReadItemV1 Item)
    : NativeApprovalReadDetailV1(Item);

internal sealed record InventoryStockScrapNativeApprovalReadDetailV1(
    NativeApprovalReadItemV1 Item)
    : NativeApprovalReadDetailV1(Item);

internal interface INativeApprovalReadAdapterV1
{
    string Kind { get; }

    Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
        NativeApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(
        NativeApprovalReadContextV1 context,
        Guid approvalId,
        CancellationToken cancellationToken);
}

internal static class NativeApprovalJson
{
    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };
}
