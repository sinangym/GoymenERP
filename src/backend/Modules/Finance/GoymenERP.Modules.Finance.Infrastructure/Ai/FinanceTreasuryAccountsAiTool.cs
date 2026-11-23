using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure.Ai;

/// <summary>
/// WP-A3c READ tool <c>finance.treasury-accounts.v1</c> (version 2, AI-D43): the company's bank and cash (kasa) accounts —
/// counted by kind, state and ISO currency code — and, for the listed accounts, each balance from the Finance ledger with,
/// for a bank account, the comparison with its latest bank statement. Read ONLY through Finance owner reads:
/// <see cref="IFinanceBankAccountReader.ListAuthorizedAsync"/> (<c>finance.bank-account.read</c>),
/// <see cref="IFinanceCashAccountMaster.ListAsync"/> (<c>finance.cash-account.read</c>),
/// <see cref="IFinanceTreasuryBalanceReadV1.ReadAuthorizedAsync"/> (<c>finance.treasury-balance.read</c>) and
/// <see cref="IFinanceBankStatementImportServiceV1"/> (<c>finance.bank-statements.read</c>, measured by the owner).
/// <para>
/// AI-D43: "bakiye finans defterindeki hareketlerden gelir; son banka ekstresiyle fark varsa ayrıca uyarı gösterilir." The
/// balance is the ledger's, never the statement's. The statement check compares the ledger balance THROUGH the statement's
/// last transaction date with the balance on that statement's last line; a difference is a warning field
/// (<c>statementBalance</c> + <c>difference</c> = ledger − statement), not a correction. A statement the caller may not
/// read, a statement without a balance, or one in another currency is said as such and does not refuse the answer.
/// Balances are per account in its own currency with an explicit ISO code; nothing is summed or converted across accounts
/// or currencies (the cross-currency total stays undecided). No IBAN, rejection reason, statement line or actor.
/// </para>
/// </summary>
public sealed class FinanceTreasuryAccountsAiTool(
    IFinanceBankAccountReader bankAccounts,
    IFinanceCashAccountMaster cashAccounts,
    IFinanceTreasuryBalanceReadV1 balances,
    TimeProvider timeProvider,
    ISettingsMoneyCandidateReader? currencies = null,
    IFinanceBankStatementImportServiceV1? statements = null) : IAiOwnerReadToolV1
{
    public const string ToolId = "finance.treasury-accounts.v1";
    public const string ToolVersion = "2";
    public const string KindField = "kind";
    public const string LimitField = "limit";
    public const string Bank = "Bank";
    public const string Cash = "Cash";
    public const string BalanceKnown = "Known";
    public const string BalanceUnavailable = "Unavailable";
    public const string StatementMatched = "Matched";
    public const string StatementDifference = "Difference";
    public const string StatementNone = "NoStatement";
    public const string StatementNoBalance = "NoBalanceOnStatement";
    public const string StatementNotAuthorized = "NotAuthorized";
    public const string StatementCurrencyMismatch = "CurrencyMismatch";
    public const string StatementUnavailable = "Unavailable";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int ScanLimit = 500;
    public const string TargetKind = "finance.treasury-accounts";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly AiReadToolArgumentV1[] Arguments =
    [
        AiReadToolArgumentV1.OneOf(KindField, Bank, Cash),
        AiReadToolArgumentV1.Integer(LimitField, 1, MaximumLimit, DefaultLimit),
    ];

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Finance",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential,
        requiredPermissions:
        [
            ErpPermissions.FinanceBankAccountRead, ErpPermissions.FinanceCashAccountRead, ErpPermissions.FinanceTreasuryBalanceRead,
        ],
        fieldAllowlist: [KindField, LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "finance.treasury-accounts.v1/input@1",
            OutputSchema: "finance.treasury-accounts.v1/output@2",
            FieldPolicy: "finance.treasury-accounts.v1/fields@2",
            LimitPolicy: "finance.treasury-accounts.v1/limit-50-scan-500@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+finance.bank-account.read+finance.cash-account.read+finance.treasury-balance.read@2",
            SourceContract: "IFinanceBankAccountReader.ListAuthorizedAsync@v1+IFinanceCashAccountMaster.ListAsync@v1"
                + "+IFinanceTreasuryBalanceReadV1.ReadAuthorizedAsync@v1+IFinanceBankStatementImportServiceV1@v1",
            RedactionPolicy: "finance.treasury-accounts.v1/ledger-balance-statement-warning-no-iban@2"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadToolArgumentReaderV1.TryRead(input.ArgumentsJson, Arguments, out var arguments, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        var kind = arguments.Choice(KindField);
        var owner = FinanceAiReadSupport.Context(context);
        var accounts = new List<Account>();
        try
        {
            if (kind is null or Bank)
            {
                var banks = await bankAccounts.ListAuthorizedAsync(owner, cancellationToken);
                if (banks is not { Outcome: ReadOutcome.Found, Resource: { } bankPage })
                {
                    return FinanceAiReadSupport.Refusal(banks?.Outcome ?? ReadOutcome.Unavailable);
                }

                if (bankPage.CompanyId != context.CompanyId || bankPage.Items.Any(item => item.CompanyId != context.CompanyId))
                {
                    return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }

                accounts.AddRange(bankPage.Items.Select(item => new Account(Bank, item.Id, item.CurrencyId, item.DisplayName,
                    item.IsActive ? "Active" : "Inactive", item.IsActive, item.Version)));
            }

            if (kind is null or Cash)
            {
                var cash = await cashAccounts.ListAsync(owner, cancellationToken);
                if (cash is not { Outcome: ReadOutcome.Found, Resource: { } cashPage })
                {
                    return FinanceAiReadSupport.Refusal(cash?.Outcome ?? ReadOutcome.Unavailable);
                }

                if (cashPage.CompanyId != context.CompanyId
                    || cashPage.Items.Any(item => item?.Account is null || item.Account.CompanyId != context.CompanyId
                                                  || !Enum.IsDefined(item.Account.Status)))
                {
                    return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }

                accounts.AddRange(cashPage.Items.Select(item => new Account(Cash, item.Account.Id, item.Account.CurrencyId,
                    item.Account.DisplayName, item.Account.Status.ToString(), item.Account.IsActive, item.Account.Version)));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (accounts.Count > ScanLimit || accounts.Select(account => (account.Kind, account.Id)).Distinct().Count() != accounts.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var codes = await FinanceAiReadSupport.CurrencyCodesAsync(currencies, cancellationToken);
        var ordered = accounts
            .OrderBy(account => account.Kind, StringComparer.Ordinal)
            .ThenByDescending(account => account.IsActive)
            .ThenBy(account => account.DisplayName, StringComparer.Ordinal)
            .ThenBy(account => account.Id)
            .ToArray();
        var groups = ordered
            .GroupBy(account => (account.Kind, Code: codes.TryGetValue(account.CurrencyId, out var code) ? code : null, account.IsActive))
            .Select(group => new FinanceTreasuryAccountGroupV1(group.Key.Kind, group.Key.Code, group.Key.IsActive, group.Count()))
            .OrderBy(group => group.Kind, StringComparer.Ordinal)
            .ThenBy(group => group.CurrencyCode ?? string.Empty, StringComparer.Ordinal)
            .ThenByDescending(group => group.IsActive)
            .ToArray();

        var listed = new List<FinanceTreasuryAccountV1>();
        var versionLines = new List<string>();
        foreach (var account in ordered.Take(arguments.Integer(LimitField)))
        {
            var known = codes.TryGetValue(account.CurrencyId, out var code);
            var balance = await ReadBalanceAsync(owner, context, account, null, cancellationToken);
            if (balance.Refusal is { } refusal)
            {
                return refusal;
            }

            FinanceTreasuryStatementCheckV1? statement = account.Kind == Bank
                ? await CheckStatementAsync(owner, context, account, known ? code : null, cancellationToken)
                : null;
            if (statement is { State: "Refused" })
            {
                return new AiReadResultV1(AiToolOutcomeV1.Denied);
            }

            // An amount is shown only with its ISO code; without one the balance is withheld, never shown bare.
            var shown = known && balance.Text is not null;
            listed.Add(new FinanceTreasuryAccountV1(
                account.Kind,
                account.Id,
                account.DisplayName,
                known ? code : null,
                account.State,
                account.IsActive,
                shown ? BalanceKnown : BalanceUnavailable,
                shown ? balance.Text : null,
                known ? statement : statement is null ? null : statement with
                {
                    StatementBalance = null, Difference = null, LedgerBalanceAtStatementDate = null,
                },
                account.Version));
            versionLines.Add($"{account.Kind}:{account.Id:D}:{account.Version}:{balance.Text}:{statement?.ImportId:D}:{statement?.ImportVersion}");
        }

        var content = new FinanceTreasuryAccountsContentV1(
            kind,
            true,
            ordered.All(account => codes.ContainsKey(account.CurrencyId)),
            listed.All(account => account.BalanceState == BalanceKnown),
            listed.Count(account => account.Statement?.State == StatementDifference),
            ordered.Count(account => account.Kind == Bank),
            ordered.Count(account => account.Kind == Cash),
            groups,
            listed.Count,
            listed);
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Kasa ve banka hesapları ve defter bakiyeleri",
            AiReadToolSupportV1.Digest([.. ordered.Select(account => $"{account.Kind}:{account.Id:D}:{account.Version}"), .. versionLines]),
            now, AiDataClassV1.Confidential);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private async Task<(string? Text, AiReadResultV1? Refusal)> ReadBalanceAsync(FinanceContext owner, AiServerContextV1 context,
        Account account, DateOnly? throughDate, CancellationToken cancellationToken)
    {
        ReadResult<FinanceTreasuryBalanceViewV1>? result;
        try
        {
            result = await balances.ReadAuthorizedAsync(owner,
                account.Kind == Bank ? FinanceTreasuryAccountKindV1.Bank : FinanceTreasuryAccountKindV1.Cash, account.Id, throughDate,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, null);
        }

        // The balance grant is required by the descriptor: losing it refuses the whole answer. A ledger the owner cannot
        // tell (Unavailable / NotFound) leaves that account's balance "Unavailable" — never a zero.
        if (result?.Outcome == ReadOutcome.Denied)
        {
            return (null, new AiReadResultV1(AiToolOutcomeV1.Denied));
        }

        if (result is not { Outcome: ReadOutcome.Found, Resource: { } view })
        {
            return (null, null);
        }

        if (view.CompanyId != context.CompanyId || view.AccountId != account.Id || view.CurrencyId != account.CurrencyId
            || view.ThroughDate != throughDate || !TryAmount(view.Balance, out _))
        {
            return (null, new AiReadResultV1(AiToolOutcomeV1.Unavailable));
        }

        return (view.Balance, null);
    }

    private async Task<FinanceTreasuryStatementCheckV1> CheckStatementAsync(FinanceContext owner, AiServerContextV1 context,
        Account account, string? currencyCode, CancellationToken cancellationToken)
    {
        if (statements is null)
        {
            return Statement(StatementUnavailable);
        }

        try
        {
            var list = await statements.ListAuthorizedAsync(owner, account.Id, cancellationToken);
            if (list.Outcome == ReadOutcome.Denied)
            {
                return Statement(StatementNotAuthorized);
            }

            if (list is not { Outcome: ReadOutcome.Found, List: { } imports } || imports.CompanyId != context.CompanyId
                || imports.Imports.Any(import => import.CompanyId != context.CompanyId || import.BankAccountId != account.Id))
            {
                return Statement(StatementUnavailable);
            }

            var latest = imports.Imports
                .Where(import => import.State == FinanceBankStatementImportStateV1.Parsed && import.LastTransactionOn is not null)
                .OrderByDescending(import => import.LastTransactionOn)
                .ThenByDescending(import => import.ParsedAtUtc)
                .ThenBy(import => import.Id)
                .FirstOrDefault();
            if (latest is null)
            {
                return Statement(StatementNone);
            }

            var detail = await statements.GetAuthorizedAsync(owner, latest.Id, cancellationToken);
            if (detail.Outcome == ReadOutcome.Denied)
            {
                return Statement(StatementNotAuthorized);
            }

            if (detail is not { Outcome: ReadOutcome.Found, Detail: { } found } || found.Import.Id != latest.Id
                || found.Import.CompanyId != context.CompanyId || found.Import.BankAccountId != account.Id)
            {
                return Statement(StatementUnavailable);
            }

            var statementDate = latest.LastTransactionOn!.Value;
            var last = found.Lines
                .OrderByDescending(line => line.TransactionOn)
                .ThenByDescending(line => line.LineNumber)
                .FirstOrDefault();
            if (last is null || last.TransactionOn != statementDate || last.Balance is null)
            {
                return Statement(StatementNoBalance, latest, statementDate);
            }

            if (last.CurrencyText is { Length: > 0 } text && (currencyCode is null
                                                             || !string.Equals(text.Trim(), currencyCode, StringComparison.OrdinalIgnoreCase)))
            {
                return Statement(StatementCurrencyMismatch, latest, statementDate);
            }

            if (!TryAmount(last.Balance, out var statementBalance))
            {
                return Statement(StatementUnavailable, latest, statementDate);
            }

            var ledger = await ReadBalanceAsync(owner, context, account, statementDate, cancellationToken);
            if (ledger.Refusal is not null)
            {
                return Statement("Refused");
            }

            if (ledger.Text is null || !TryAmount(ledger.Text, out var ledgerBalance))
            {
                return Statement(StatementUnavailable, latest, statementDate);
            }

            var difference = ledgerBalance - statementBalance;
            return new FinanceTreasuryStatementCheckV1(
                difference == 0m ? StatementMatched : StatementDifference,
                latest.Id,
                latest.Version,
                statementDate,
                last.Balance,
                ledger.Text,
                difference.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Statement(StatementUnavailable);
        }
    }

    private static FinanceTreasuryStatementCheckV1 Statement(string state, FinanceBankStatementImportViewV1? import = null,
        DateOnly? statementDate = null) =>
        new(state, import?.Id, import?.Version, statementDate, null, null, null);

    private static bool TryAmount(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private sealed record Account(string Kind, Guid Id, Guid CurrencyId, string DisplayName, string State, bool IsActive, string Version);
}

/// <summary>
/// Released fields only. <c>BalancesKnown</c> is true when every listed account has a ledger balance with a currency code;
/// <c>StatementDifferenceCount</c> counts listed bank accounts whose ledger differs from their latest statement.
/// No total across accounts or currencies.
/// </summary>
public sealed record FinanceTreasuryAccountsContentV1(
    string? Kind,
    bool Complete,
    bool CurrencyCodesResolved,
    bool BalancesKnown,
    int StatementDifferenceCount,
    int BankAccountCount,
    int CashAccountCount,
    IReadOnlyList<FinanceTreasuryAccountGroupV1> Groups,
    int ListedCount,
    IReadOnlyList<FinanceTreasuryAccountV1> Items);

public sealed record FinanceTreasuryAccountGroupV1(string Kind, string? CurrencyCode, bool IsActive, int Count);

/// <summary><c>Balance</c> (ledger, AI-D43) and <c>CurrencyCode</c> are both present or both withheld.</summary>
public sealed record FinanceTreasuryAccountV1(
    string Kind,
    Guid AccountId,
    string DisplayName,
    string? CurrencyCode,
    string State,
    bool IsActive,
    string BalanceState,
    string? Balance,
    FinanceTreasuryStatementCheckV1? Statement,
    string Version);

/// <summary>
/// The latest bank statement comparison of a bank account (AI-D43 warning). <c>Difference</c> = ledger balance through
/// <c>StatementDate</c> − the balance on the statement's last line; present only for <c>Matched</c>/<c>Difference</c>.
/// </summary>
public sealed record FinanceTreasuryStatementCheckV1(
    string State,
    Guid? ImportId,
    string? ImportVersion,
    DateOnly? StatementDate,
    string? StatementBalance,
    string? LedgerBalanceAtStatementDate,
    string? Difference);
