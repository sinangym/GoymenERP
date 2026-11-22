using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure.Ai;

/// <summary>
/// WP-A3c READ tool <c>finance.treasury-accounts.v1</c>: the company's bank and cash (kasa) accounts — counted by kind,
/// state and ISO currency code, and listed — read ONLY through the Finance owners' own authorized reads
/// (<see cref="IFinanceBankAccountReader.ListAuthorizedAsync"/>, <see cref="IFinanceCashAccountMaster.ListAsync"/>), which
/// measure <c>finance.bank-account.read</c> and <c>finance.cash-account.read</c>. No IBAN, rejection reason or actor.
/// <para>
/// The BALANCE of an account is not released: FNS-D11 / FNS-D22 name a current treasury balance, but no balance source
/// exists yet (the registered <see cref="IFinanceTreasuryBalanceGuard"/> answers Unavailable), so every account says
/// <c>balanceState = "NoSource"</c> and no number is shown — never a zero or an estimate. When a balance source lands the
/// tool gets a new version. A "kasa/banka bakiyesi özeti" therefore answers "bakiye kaynağı yok" (NEEDS-SINAN in the
/// WP-A3c report).
/// </para>
/// </summary>
public sealed class FinanceTreasuryAccountsAiTool(
    IFinanceBankAccountReader bankAccounts,
    IFinanceCashAccountMaster cashAccounts,
    TimeProvider timeProvider,
    ISettingsMoneyCandidateReader? currencies = null) : IAiOwnerReadToolV1
{
    public const string ToolId = "finance.treasury-accounts.v1";
    public const string ToolVersion = "1";
    public const string KindField = "kind";
    public const string LimitField = "limit";
    public const string Bank = "Bank";
    public const string Cash = "Cash";
    public const string BalanceNoSource = "NoSource";
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
        AiDataClassV1.Internal,
        requiredPermissions: [ErpPermissions.FinanceBankAccountRead, ErpPermissions.FinanceCashAccountRead],
        fieldAllowlist: [KindField, LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "finance.treasury-accounts.v1/input@1",
            OutputSchema: "finance.treasury-accounts.v1/output@1",
            FieldPolicy: "finance.treasury-accounts.v1/fields@1",
            LimitPolicy: "finance.treasury-accounts.v1/limit-50-scan-500@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+finance.bank-account.read+finance.cash-account.read@1",
            SourceContract: "IFinanceBankAccountReader.ListAuthorizedAsync@v1+IFinanceCashAccountMaster.ListAsync@v1",
            RedactionPolicy: "finance.treasury-accounts.v1/no-iban-no-balance-no-reason@1"));

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
        var accounts = new List<(string Kind, Guid Id, Guid CurrencyId, string DisplayName, string State, bool IsActive, string Version)>();
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

                accounts.AddRange(bankPage.Items.Select(item => (Bank, item.Id, item.CurrencyId, item.DisplayName,
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

                accounts.AddRange(cashPage.Items.Select(item => (Cash, item.Account.Id, item.Account.CurrencyId,
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
        var listed = ordered.Take(arguments.Integer(LimitField)).Select(account => new FinanceTreasuryAccountV1(
            account.Kind,
            account.Id,
            account.DisplayName,
            codes.TryGetValue(account.CurrencyId, out var code) ? code : null,
            account.State,
            account.IsActive,
            BalanceNoSource,
            account.Version)).ToArray();
        var content = new FinanceTreasuryAccountsContentV1(
            kind,
            true,
            ordered.All(account => codes.ContainsKey(account.CurrencyId)),
            BalanceNoSource,
            ordered.Count(account => account.Kind == Bank),
            ordered.Count(account => account.Kind == Cash),
            groups,
            listed.Length,
            listed);
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Kasa ve banka hesapları",
            AiReadToolSupportV1.Digest(ordered.Select(account => $"{account.Kind}:{account.Id:D}:{account.Version}")), now,
            AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>
/// Released fields only. <c>BalanceState</c> is <c>"NoSource"</c> for the whole answer and every account: no balance is
/// known to the owner yet (FNS-D11), so none is shown.
/// </summary>
public sealed record FinanceTreasuryAccountsContentV1(
    string? Kind,
    bool Complete,
    bool CurrencyCodesResolved,
    string BalanceState,
    int BankAccountCount,
    int CashAccountCount,
    IReadOnlyList<FinanceTreasuryAccountGroupV1> Groups,
    int ListedCount,
    IReadOnlyList<FinanceTreasuryAccountV1> Items);

public sealed record FinanceTreasuryAccountGroupV1(string Kind, string? CurrencyCode, bool IsActive, int Count);

public sealed record FinanceTreasuryAccountV1(
    string Kind,
    Guid AccountId,
    string DisplayName,
    string? CurrencyCode,
    string State,
    bool IsActive,
    string BalanceState,
    string Version);
