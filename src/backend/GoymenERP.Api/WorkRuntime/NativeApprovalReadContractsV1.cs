using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.WorkRuntime;

internal static class NativeApprovalKindsV1
{
    public const string FinanceOfficialInvoiceImport = "finance.official-invoice-import.v1";
}

internal sealed record NativeApprovalReadItemV1(
    string Kind,
    Guid ApprovalId,
    Guid CompanyId,
    FinanceOfficialInvoiceImportApprovalStatusV1 Status,
    string Version,
    string BatchVersion,
    string Checksum,
    string TemplateVersion,
    int ItemCount,
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc);

internal sealed record NativeApprovalReadPageV1(
    Guid CompanyId,
    IReadOnlyList<NativeApprovalReadItemV1> Items,
    string? NextCursor);

internal sealed record NativeApprovalReadDetailV1(
    NativeApprovalReadItemV1 Item,
    FinanceOfficialInvoiceImportPreviewV1 Preview);

internal interface INativeApprovalReadAdapterV1
{
    Task<ReadResult<NativeApprovalReadPageV1>> ListAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<ReadResult<NativeApprovalReadDetailV1>> GetAsync(
        FinanceContext context,
        string kind,
        Guid approvalId,
        CancellationToken cancellationToken);
}
