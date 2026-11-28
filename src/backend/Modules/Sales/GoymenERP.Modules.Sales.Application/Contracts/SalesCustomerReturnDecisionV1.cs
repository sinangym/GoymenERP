using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesCustomerReturnDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesCustomerReturnDecisionReadResultV1(SalesCustomerReturnDecisionReadStateV1 State, SalesCustomerReturnDecisionDetailV1? Detail);
public sealed record SalesCustomerReturnDecisionDetailV1(Guid CompanyId, SalesCustomerReturnView CustomerReturn, bool ApprovalSupported,
    bool RejectionSupported, string SourceFactsDigest, string PreviewDigest);
public sealed record SalesCustomerReturnOriginalReadResultV1(SalesCustomerReturnDecisionReadStateV1 State,
    [property: JsonIgnore] SalesCustomerReturnOriginalReceiptV1? Receipt)
{ [JsonIgnore] public long? CurrentVersion { get; init; } }
public interface ISalesCustomerReturnDecisionReadV1
{
    Task<SalesCustomerReturnDecisionReadResultV1> ReadCustomerReturnDecisionAsync(Guid returnId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesCustomerReturnOriginalReadResultV1> ReadCustomerReturnOriginalVersionAsync(Guid returnId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public interface ISalesCustomerReturnDecisionReadStoreV1
{
    Task<SalesCustomerReturn?> ReadCustomerReturnDecisionSourceAsync(Guid returnId, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default);
    Task<SalesCustomerReturnOriginalReadResultV1> ReadCustomerReturnOriginalVersionAsync(Guid returnId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesCustomerReturnCommandResult> AttachCustomerReturnOriginalReceiptAsync(SalesCustomerReturnCommandResult response,
        Guid returnId, SalesDraftStateCommandRequest request, CancellationToken cancellationToken = default);
}
/// <summary>
/// The immutable decision proven by the native append-only Attempt and integrity-checked Audit, with the return's immutable
/// commercial fields. No original full response JSON was stored: native replay returns the CURRENT return separately.
/// </summary>
public sealed record SalesCustomerReturnOriginalReceiptV1(Guid CompanyId, Guid ReturnId, string CommandName, string ActorId,
    string IdempotencyKeyHash, string PayloadHash, long ExpectedVersion, long ResultVersion, string OriginalCorrelationId,
    DateTimeOffset RecordedAtUtc, SalesCustomerReturnView OriginalDecision, Guid AuditEventId, string AuditIntegrityHash,
    string SourceFactsDigest, string OriginalDecisionDigest)
{ public string CurrentResponseDigest { get; init; } = ""; }
public static class SalesCustomerReturnDecisionDigestsV1
{
    // The submitted facts and all commercial/line facts never change; approval/finalization/cancellation are a separate axis.
    public static string SourceFactsDigest(Guid companyId, SalesCustomerReturnView view) => Hash(new { companyId, source = view with
    { State = "", Version = "", ApprovedBy = null, ApprovedAtUtc = null, FinalizedBy = null, FinalizedAtUtc = null,
        RejectedBy = null, RejectedAtUtc = null, RejectionReason = null, CancelledBy = null, CancelledAtUtc = null,
        CancellationReason = null, UpdatedAtUtc = default } });
    public static string PreviewDigest(SalesCustomerReturnDecisionDetailV1 detail) => Hash(detail with { PreviewDigest = "" });
    public static string OriginalDecisionDigest(SalesCustomerReturnOriginalReceiptV1 receipt) => Hash(receipt with { OriginalDecisionDigest = "", CurrentResponseDigest = "" });
    public static bool IsOriginalReceipt(SalesCustomerReturnOriginalReceiptV1? r) => r is { ExpectedVersion: > 0 } && r.ExpectedVersion < long.MaxValue
        && r.CompanyId != Guid.Empty && r.ReturnId != Guid.Empty && !string.IsNullOrWhiteSpace(r.ActorId) && !string.IsNullOrWhiteSpace(r.OriginalCorrelationId)
        && r.ResultVersion == r.ExpectedVersion + 1 && r.OriginalDecision is { } v && v.Id == r.ReturnId && v.Version == r.ResultVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
        && r.CommandName is SalesCustomerReturnCommands.ApproveV1 or SalesCustomerReturnCommands.RejectV1
        && v.State == (r.CommandName == SalesCustomerReturnCommands.ApproveV1 ? "Approved" : "Rejected")
        && (r.CommandName == SalesCustomerReturnCommands.ApproveV1 ? v.ApprovedBy == r.ActorId && v.ApprovedAtUtc == r.RecordedAtUtc : v.RejectedBy == r.ActorId && v.RejectedAtUtc == r.RecordedAtUtc)
        && r.RecordedAtUtc.Offset == TimeSpan.Zero && r.AuditEventId != Guid.Empty && NativeHash(r.AuditIntegrityHash) && NativeHash(r.IdempotencyKeyHash) && NativeHash(r.PayloadHash)
        && v.CreatedBy != r.ActorId && v.SubmittedBy != r.ActorId && !string.IsNullOrWhiteSpace(v.SubmittedBy) && v.SubmittedAtUtc is not null && v.Lines is { Count: > 0 } && v.Lines.All(x => x is not null)
        && r.SourceFactsDigest == SourceFactsDigest(r.CompanyId, v) && r.OriginalDecisionDigest == OriginalDecisionDigest(r);
    private static bool NativeHash(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
    public static string CurrentResponseDigest(SalesCustomerReturnCommandResult response) => Hash(response with { IsReplay = false, OriginalDecisionReceipt = null });
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
