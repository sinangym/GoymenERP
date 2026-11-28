using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesRiskExceptionDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesRiskExceptionDecisionReadResultV1(SalesRiskExceptionDecisionReadStateV1 State, SalesRiskExceptionDecisionDetailV1? Detail);
public sealed record SalesRiskExceptionOriginalVersionReadResultV1(SalesRiskExceptionDecisionReadStateV1 State,
    [property: JsonIgnore] SalesRiskExceptionOriginalDecisionReceiptV1? Receipt)
{ [JsonIgnore] public long? CurrentVersion { get; init; } }
public interface ISalesRiskExceptionDecisionReadV1
{
    Task<SalesRiskExceptionDecisionReadResultV1> ReadRiskExceptionDecisionAsync(Guid exceptionId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesRiskExceptionOriginalVersionReadResultV1> ReadRiskExceptionOriginalVersionAsync(Guid exceptionId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record SalesRiskExceptionDecisionDetailV1(Guid CompanyId, Guid ExceptionId, string OrderDocumentNo,
    string OrderVersion, string OrderState, string OrderApprovalState, bool HasApprovedRevision, SalesRiskExceptionView Exception,
    bool ApprovalSupported, bool RejectionSupported, string SourceFactsDigest, string PreviewDigest)
{ [JsonIgnore] public SalesRiskExceptionDecisionSourceV1? Source { get; init; }
  [JsonIgnore] public DateOnly? ObservedBusinessDate { get; init; } }
public sealed record SalesRiskExceptionRecordedStateV1(Guid Id, Guid OrderId, int ExceptionNo, Guid LoadingInstructionId,
    string State, long Version, string Reason, string RequestedBy, DateTimeOffset RequestedAtUtc,
    string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? RejectionReason, DateOnly? ValidOn);
public sealed record SalesRiskExceptionDecisionSourceV1(Guid CompanyId, string OrderDocumentNo, long OrderVersion,
    string OrderState, string OrderApprovalState, bool HasApprovedRevision, SalesRiskExceptionRecordedStateV1 Recorded,
    Guid? DroppedByShipmentId);
public interface ISalesRiskExceptionDecisionReadStoreV1
{
    Task<SalesRiskExceptionDecisionSourceV1?> ReadRiskExceptionDecisionSourceAsync(Guid exceptionId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesRiskExceptionOriginalDecisionReceiptV1?> ReadRiskExceptionDecisionReceiptAsync(SalesDraftWriteEnvelope envelope,
        string hash, string command, SalesRiskExceptionCommandResult response, Func<CancellationToken, Task> freshFence, CancellationToken token);
    Task<SalesRiskExceptionOriginalVersionReadResultV1> ReadRiskExceptionOriginalVersionAsync(Guid exceptionId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken token);
}
// Original recorded decision facts, never an invented original HTTP view or an historical IsValidToday.
public sealed record SalesRiskExceptionOriginalDecisionReceiptV1(Guid CompanyId, Guid ExceptionId, string CommandName,
    string ActorId, string IdempotencyKeyHash, string PayloadHash, string ExpectedVersion, string ResultVersion,
    string OriginalCorrelationId, DateTimeOffset RecordedAtUtc, Guid AuditEventId, string AuditIntegrityHash,
    SalesRiskExceptionRecordedStateV1 Before, SalesRiskExceptionRecordedStateV1 After, string OriginalDecisionDigest)
{
    [JsonIgnore] public SalesRiskExceptionDecisionSourceV1? CurrentSource { get; init; }
    [JsonIgnore] public string CurrentResponseDigest { get; init; } = "";
}
public static class SalesRiskExceptionDecisionDigestsV1
{
    public static SalesShipmentRiskException Restore(Guid company, SalesRiskExceptionRecordedStateV1 s)
    {
        if (!Enum.TryParse<SalesShipmentRiskExceptionState>(s.State, out var state) || state.ToString() != s.State)
            throw new InvalidOperationException("Risk istisnası durumu tanınmıyor.");
        var item = SalesShipmentRiskException.Request(s.Id, company, s.OrderId, s.ExceptionNo, s.LoadingInstructionId, s.Reason, s.RequestedBy, s.RequestedAtUtc);
        var decision = s.DecidedBy is null && s.DecidedAtUtc is null && s.RejectionReason is null ? null
            : new SalesShipmentRiskExceptionDecision(s.DecidedBy ?? throw new InvalidOperationException(), s.DecidedAtUtc ?? throw new InvalidOperationException(), s.RejectionReason);
        item.Restore(state, decision, s.ValidOn, s.Version, s.DecidedAtUtc ?? s.RequestedAtUtc);
        return item;
    }
    public static string SourceFactsDigest(SalesRiskExceptionDecisionSourceV1 source) => Hash(source);
    public static string PreviewDigest(SalesRiskExceptionDecisionDetailV1 detail) => Hash(detail with { PreviewDigest = "" });
    public static string OriginalDecisionDigest(SalesRiskExceptionOriginalDecisionReceiptV1 receipt) => Hash(receipt with { OriginalDecisionDigest = "", CurrentSource = null, CurrentResponseDigest = "" });
    public static string CurrentResponseDigest(SalesRiskExceptionCommandResult response) => Hash(response with { IsReplay = false, OriginalDecisionReceipt = null });
    public static bool MatchesCurrent(SalesRiskExceptionRecordedStateV1 s, SalesRiskExceptionView v) =>
        v.OrderId == s.OrderId && v.ExceptionNo == s.ExceptionNo && v.LoadingInstructionId == s.LoadingInstructionId && v.State == s.State
        && v.Version == s.Version.ToString(CultureInfo.InvariantCulture) && v.Reason == s.Reason && v.RequestedBy == s.RequestedBy && v.RequestedAtUtc == s.RequestedAtUtc
        && v.DecidedBy == s.DecidedBy && v.DecidedAtUtc == s.DecidedAtUtc && v.RejectionReason == s.RejectionReason && v.ValidOn == s.ValidOn;
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
