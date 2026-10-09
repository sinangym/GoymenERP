using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Api.WorkRuntime;

internal static class NativeApprovalKindsV1
{
    public const string FinanceOfficialInvoiceImport = "finance.official-invoice-import.v1";
    public const string SalesOrderDraft = "sales.order-draft.v1";

    public static bool IsKnown(string kind) =>
        string.Equals(kind, FinanceOfficialInvoiceImport, StringComparison.Ordinal)
        || string.Equals(kind, SalesOrderDraft, StringComparison.Ordinal);
}

internal sealed record NativeApprovalReadContextV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId);

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

internal sealed record NativeApprovalReadPageV1(
    Guid CompanyId,
    IReadOnlyList<NativeApprovalReadItemV1> Items,
    string? NextCursor);

[JsonDerivedType(typeof(FinanceInvoiceImportNativeApprovalReadDetailV1))]
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
