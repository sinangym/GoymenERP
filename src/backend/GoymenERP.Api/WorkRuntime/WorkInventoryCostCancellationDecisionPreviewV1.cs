using System.Globalization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Human facts only. Both native versions remain independent text values; no proof JSON or receipt is serialized.</summary>
public sealed record WorkInventoryCostCancellationPreviewV1(WorkInventoryCostViewV1 OriginalProposal,
    WorkInventoryCostCancellationV1 PreparedCancellation, WorkInventoryCostCancellationV1 Cancellation,
    DateTimeOffset PreparedRecordedAtUtc, WorkInventoryCostSourceCancellationV1 SourceCancellation)
{
    internal static WorkInventoryCostCancellationPreviewV1 FromNative(AccountingInventoryCostCancellationDecisionDetailV1 d) =>
        new(WorkInventoryCostViewV1.FromNative(d.OriginalProposal), WorkInventoryCostCancellationV1.FromNative(d.PreparedCancellation),
            WorkInventoryCostCancellationV1.FromNative(d.Cancellation), d.PreparedRecordedAtUtc,
            WorkInventoryCostSourceCancellationV1.FromNative(d.SourceCancellation));
}

public sealed record WorkInventoryCostSourceCancellationV1(Guid CompanyId, Guid CancellationId, string DocumentKind,
    Guid SourceDocumentId, Guid InvoiceId, string FinalizedVersion, Guid CancelledBy, DateTimeOffset CancelledAtUtc,
    string Reason, DateTimeOffset RecordedAtUtc)
{
    internal static WorkInventoryCostSourceCancellationV1 FromNative(AccountingInventoryCostCancellationSourceV1 source) =>
        new(source.CompanyId, source.CancellationId, source.DocumentKind, source.SourceDocumentId, source.InvoiceId,
            source.FinalizedVersion.ToString(CultureInfo.InvariantCulture), source.CancelledBy, source.CancelledAtUtc,
            source.Reason, source.RecordedAtUtc);
}
