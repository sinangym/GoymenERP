using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>The durable Finance state of an admitted open item.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceOpenItemStatusV1>))]
public enum FinanceOpenItemStatusV1
{
    Open = 1,
    Closed = 2,
}

/// <summary>
/// FI04 read projection over the Finance-owned open-item row. It exposes stored identities independently.
/// <paramref name="Status"/> is derived from the remaining amount by the Finance allocation policy, so it and
/// the money in <paramref name="InitialAmount"/> and <paramref name="OutstandingAmount"/> are returned only when
/// the caller holds the separate open-item amount-read authority. No total, difference or converted amount is derived.
/// </summary>
public sealed record FinanceOpenItemViewV1(
    Guid Id,
    Guid CompanyId,
    string SourceKind,
    Guid SourceId,
    Guid SourceLineId,
    string SourceVersion,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    string Version,
    DateOnly? DueOn,
    FinanceOpenItemStatusV1? Status,
    DateTimeOffset CreatedAtUtc,
    AmountView InitialAmount,
    AmountView OutstandingAmount);

/// <summary>One stable page, newest admitted item first.</summary>
public sealed record FinanceOpenItemPageV1(
    Guid CompanyId,
    IReadOnlyList<FinanceOpenItemViewV1> Items,
    string? NextCursor);

/// <summary>
/// Independent FI04 list/detail read. The read authority reveals stored metadata that is independent of money;
/// the amount authority controls <see cref="FinanceOpenItemViewV1.Status"/> and the two <see cref="AmountView"/>
/// disclosures. Neither operation writes, allocates or posts money.
/// </summary>
public interface IFinanceOpenItemReadServiceV1
{
    Task<ReadResult<FinanceOpenItemPageV1>> ListAuthorizedAsync(
        FinanceContext context,
        SourceListRequest request,
        CancellationToken cancellationToken);

    Task<ReadResult<FinanceOpenItemViewV1>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid openItemId,
        CancellationToken cancellationToken);
}
