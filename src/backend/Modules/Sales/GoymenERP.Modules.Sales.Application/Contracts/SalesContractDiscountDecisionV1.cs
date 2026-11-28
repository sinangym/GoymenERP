using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesContractDiscountDecisionReadStateV1 { Found, NotFound, Denied, Unavailable }
public sealed record SalesContractDiscountDecisionReadResultV1(SalesContractDiscountDecisionReadStateV1 State, SalesContractDiscountDecisionDetailV1? Detail);
public sealed record SalesContractDiscountOriginalVersionReadResultV1(SalesContractDiscountDecisionReadStateV1 State,
    [property: JsonIgnore] SalesContractDiscountOriginalReceiptV1? Receipt)
{ [JsonIgnore] public long? CurrentVersion { get; init; } }
public interface ISalesContractDiscountDecisionReadV1
{
    Task<SalesContractDiscountDecisionReadResultV1> ReadContractDiscountDecisionAsync(Guid contractId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<SalesContractDiscountOriginalVersionReadResultV1> ReadContractDiscountOriginalVersionAsync(Guid contractId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
/// <summary>The native human projection, under its existing optional price disclosure; source facts never become a read grant.</summary>
public sealed record SalesContractDiscountDecisionDetailV1(Guid CompanyId, SalesContractView Contract,
    bool ApprovalSupported, bool RejectionSupported, string SourceFactsDigest, string PreviewDigest)
{
    [JsonIgnore] public SalesContractDiscountDecisionSourceV1? Source { get; init; }
    [JsonIgnore] public DateOnly? ObservedBusinessDate { get; init; }
}
public sealed record SalesContractDiscountDecisionSourceV1(Guid CompanyId, SalesContract Contract);
public interface ISalesContractDiscountDecisionReadStoreV1
{
    Task<SalesContractDiscountDecisionSourceV1?> ReadContractDiscountDecisionSourceAsync(Guid contractId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesContractDiscountOriginalReceiptV1?> ReadContractDiscountDecisionReceiptAsync(SalesDraftWriteEnvelope envelope,
        string hash, string command, SalesContractCommandResult response, Func<CancellationToken, Task> freshFence, CancellationToken token);
    Task<SalesContractDiscountOriginalVersionReadResultV1> ReadContractDiscountOriginalVersionAsync(Guid contractId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken token);
}
/// <summary>Original immutable decision facts; neither an invented historical HTTP answer nor an historical business-day assertion.</summary>
public sealed record SalesContractDiscountOriginalReceiptV1(Guid CompanyId, Guid ContractId, string CommandName, string ActorId,
    string IdempotencyKeyHash, string PayloadHash, string ExpectedVersion, string ResultVersion, string OriginalCorrelationId,
    DateTimeOffset RecordedAtUtc, Guid AuditEventId, string AuditIntegrityHash,
    SalesContractDiscountDecisionSourceV1 Before, SalesContractDiscountDecisionSourceV1 After, string OriginalDecisionDigest)
{
    [JsonIgnore] public SalesContractDiscountDecisionSourceV1? CurrentSource { get; init; }
    [JsonIgnore] public string CurrentResponseDigest { get; init; } = "";
}
public static class SalesContractDiscountDecisionDigestsV1
{
    // Clone through the native restore contract before any consumer await: a provider cannot mutate the validated domain instance later.
    public static SalesContractDiscountDecisionSourceV1 Freeze(SalesContractDiscountDecisionSourceV1 source)
    {
        ArgumentNullException.ThrowIfNull(source); var s = source.Contract ?? throw new InvalidOperationException("Sözleşme olguları eksik.");
        if (source.CompanyId != s.CompanyId) throw new InvalidOperationException("Sözleşme şirketi uyuşmuyor.");
        var item = SalesContract.Restore(s.Id, s.CompanyId, s.ContractNo, s.PartyId, s.CompanyAccountId, s.CurrencyId,
            s.MoneyProfileId, s.MoneyProfileVersion, s.MoneyScale, s.ValidFrom, s.ValidThrough,
            Array.AsReadOnly(s.Installments.ToArray()), Array.AsReadOnly(s.DiscountRules.ToArray()), s.Clauses, s.DiscountLimitPercent,
            s.State, s.CreatedBy, s.CreatedAtUtc, s.Decision, s.Version, s.UpdatedAtUtc);
        return new(source.CompanyId, item);
    }
    public static string SourceFactsDigest(SalesContractDiscountDecisionSourceV1 source) => Hash(source);
    public static string PreviewDigest(SalesContractDiscountDecisionDetailV1 detail) => Hash(detail with { PreviewDigest = "" });
    public static string OriginalDecisionDigest(SalesContractDiscountOriginalReceiptV1 receipt) => Hash(receipt with { OriginalDecisionDigest = "" });
    public static string CurrentResponseDigest(SalesContractCommandResult response) => Hash(response with { IsReplay = false, OriginalDecisionReceipt = null });
    public static bool TryVersion(string? value, out long version) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version)
        && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;
    public static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left, SalesDraftJson.Options) == JsonSerializer.Serialize(right, SalesDraftJson.Options);
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SalesDraftJson.Options)));
}
