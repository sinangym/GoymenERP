using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// A bounded accounting-date view of the posted ledger. Currencies are never converted or netted
/// together, and reversals/corrections remain ordinary posted journal effects.
/// </summary>
public sealed record TrialBalanceRequestV1(
    DateOnly FromAccountingDate,
    DateOnly ThroughAccountingDate,
    Guid? CurrencyId = null);

/// <summary>One store-side aggregate. Amounts remain decimal until the API boundary.</summary>
public sealed record TrialBalanceReadRowV1(
    Guid AccountId,
    Guid CurrencyId,
    decimal Debit,
    decimal Credit);

public interface IAccountingTrialBalanceReadStore
{
    Task<IReadOnlyList<TrialBalanceReadRowV1>> ReadAsync(
        DateOnly fromAccountingDate,
        DateOnly throughAccountingDate,
        Guid? currencyId,
        CancellationToken cancellationToken = default);
}

public sealed record TrialBalanceLineV1(
    Guid AccountId,
    Guid CurrencyId,
    string Debit,
    string Credit,
    string BalanceSide,
    string Balance);

public sealed record TrialBalanceCurrencyTotalV1(
    Guid CurrencyId,
    string Debit,
    string Credit,
    bool IsBalanced);

/// <summary>
/// Owner-issued evidence for exactly the aggregate returned in this response. SourceVersion is a
/// canonical content hash, not an accounting date, maximum row value, client clock or ordinal.
/// ServerAsOfUtc records when the Accounting server completed the read; it is not the version.
/// </summary>
public sealed record TrialBalanceReadReceiptV1(
    string SourceId,
    string SourceVersion,
    Guid CompanyId,
    string ActorId,
    DateTimeOffset ServerAsOfUtc,
    string Unit,
    string FactPlan,
    string Completeness,
    IReadOnlyList<Guid> CurrencyIds);

public static class TrialBalanceReadReceiptValuesV1
{
    public const string SourceId = "accounting.trial-balance.v1";
    public const string CurrencyAmount = "CurrencyAmount";
    public const string Fact = "Fact";
    public const string Complete = "Complete";
}

public sealed record TrialBalanceReportV1(
    DateOnly FromAccountingDate,
    DateOnly ThroughAccountingDate,
    Guid? CurrencyId,
    IReadOnlyList<TrialBalanceLineV1> Lines,
    IReadOnlyList<TrialBalanceCurrencyTotalV1> CurrencyTotals,
    TrialBalanceReadReceiptV1 Receipt);

/// <summary>
/// Mizan read service. It reuses the existing journal-read authority because the report discloses
/// only an aggregate of those same posted journals. A revoked grant after the awaited SQL read
/// discards the result before release.
/// </summary>
public sealed class AccountingTrialBalanceServiceV1(
    IAccountingTrialBalanceReadStore store,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser)
{
    public async Task<AccountingApiResult<TrialBalanceReportV1>> ReadAsync(
        TrialBalanceRequestV1? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Invalid("request");
        }

        if (request.FromAccountingDate == default || request.ThroughAccountingDate == default
            || request.ThroughAccountingDate < request.FromAccountingDate)
        {
            return Invalid("accountingDate");
        }

        if (request.CurrencyId == Guid.Empty)
        {
            return Invalid("currencyId");
        }

        if (!TryContext(out var companyId, out var actorId)
            || !await FenceAsync(companyId, actorId, cancellationToken))
        {
            return Denied();
        }

        IReadOnlyList<TrialBalanceReadRowV1> rows;
        try
        {
            rows = await store.ReadAsync(request.FromAccountingDate, request.ThroughAccountingDate,
                request.CurrencyId, cancellationToken);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.CompanyContextMismatch)
        {
            return Denied();
        }

        if (!await FenceAsync(companyId, actorId, cancellationToken))
        {
            return Denied();
        }

        if (rows.Any(row => row.AccountId == Guid.Empty || row.CurrencyId == Guid.Empty
                            || row.Debit < 0m || row.Credit < 0m))
        {
            throw new InvalidOperationException("The stored trial-balance aggregate is invalid.");
        }

        var ordered = rows
            .OrderBy(row => row.CurrencyId)
            .ThenBy(row => row.AccountId)
            .ToArray();
        var lines = ordered.Select(Line).ToArray();
        var totals = ordered
            .GroupBy(row => row.CurrencyId)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var debit = group.Sum(row => row.Debit);
                var credit = group.Sum(row => row.Credit);
                return new TrialBalanceCurrencyTotalV1(group.Key, Text(debit), Text(credit), debit == credit);
            })
            .ToArray();

        return new AccountingApiResult<TrialBalanceReportV1>(
            AccountingApiStatus.Ok,
            new TrialBalanceReportV1(request.FromAccountingDate, request.ThroughAccountingDate,
                request.CurrencyId, lines, totals, Receipt(companyId, actorId, request, ordered)),
            false,
            null);
    }

    private static TrialBalanceReadReceiptV1 Receipt(
        Guid companyId,
        string actorId,
        TrialBalanceRequestV1 request,
        IReadOnlyList<TrialBalanceReadRowV1> orderedRows)
    {
        var canonical = new StringBuilder()
            .Append(TrialBalanceReadReceiptValuesV1.SourceId).Append('\n')
            .Append(companyId.ToString("D")).Append('\n')
            .Append(request.FromAccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n')
            .Append(request.ThroughAccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n')
            .Append(request.CurrencyId?.ToString("D") ?? "*").Append('\n')
            .Append(orderedRows.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');

        foreach (var row in orderedRows)
        {
            canonical.Append(row.AccountId.ToString("D")).Append('|')
                .Append(row.CurrencyId.ToString("D")).Append('|')
                .Append(Text(row.Debit)).Append('|')
                .Append(Text(row.Credit)).Append('\n');
        }

        var sourceVersion = "sha256:"
                            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        var currencyIds = orderedRows.Select(row => row.CurrencyId).Distinct().Order().ToArray();
        return new TrialBalanceReadReceiptV1(
            TrialBalanceReadReceiptValuesV1.SourceId,
            sourceVersion,
            companyId,
            actorId,
            DateTimeOffset.UtcNow,
            TrialBalanceReadReceiptValuesV1.CurrencyAmount,
            TrialBalanceReadReceiptValuesV1.Fact,
            TrialBalanceReadReceiptValuesV1.Complete,
            currencyIds);
    }

    private async Task<bool> FenceAsync(Guid companyId, string actorId, CancellationToken cancellationToken)
    {
        var scope = await authority.RevalidatePermissionScopeAsync(
            AccountingPermissionManifestV1.JournalsRead, companyId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return scope is { IsGranted: true, IsCompanyWide: true } && Still(companyId, actorId);
    }

    private bool TryContext(out Guid companyId, out string actorId)
    {
        companyId = companyContext.CompanyId;
        actorId = currentUser.UserId ?? string.Empty;
        return companyContext.IsResolved
               && companyId != Guid.Empty
               && currentUser.IsAuthenticated
               && actorId.Length > 0
               && currentUser.CanAccessCompany(companyId);
    }

    private bool Still(Guid companyId, string actorId) =>
        companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(companyId);

    private static TrialBalanceLineV1 Line(TrialBalanceReadRowV1 row)
    {
        var net = row.Debit - row.Credit;
        return new TrialBalanceLineV1(
            row.AccountId,
            row.CurrencyId,
            Text(row.Debit),
            Text(row.Credit),
            net > 0m ? "Debit" : net < 0m ? "Credit" : "Balanced",
            Text(decimal.Abs(net)));
    }

    private static string Text(decimal value) => AccountingApiService.Text(value);

    private static AccountingApiResult<TrialBalanceReportV1> Invalid(string field) =>
        new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, field);

    private static AccountingApiResult<TrialBalanceReportV1> Denied() =>
        new(AccountingApiStatus.Denied, null, false, null);
}
