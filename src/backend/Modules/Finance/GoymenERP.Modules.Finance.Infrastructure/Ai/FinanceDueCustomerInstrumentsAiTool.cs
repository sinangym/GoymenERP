using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure.Ai;

/// <summary>
/// WP-A3c READ tool <c>finance.due-customer-instruments.v1</c>: the customer cheques and notes still held — in the
/// portfolio or at the bank for collection (DEC-FIN-016) — whose due date has passed, is today or falls within the asked
/// number of days, read ONLY through the Finance owner's portfolio list
/// (<see cref="ICustomerInstrumentService.ListAuthorizedAsync"/>), which measures <c>finance.customer-instrument.read</c>
/// and discloses amounts only with <c>finance.customer-instrument.amount.read</c>. Collected, bounced, protested and
/// returned instruments are not "held". The serial number, drawer, bank, branch and description are never released.
/// Counts are exact; at the scan cap they are withheld and <c>complete</c> is false. Amounts carry an explicit ISO
/// currency code or are withheld; no total is derived.
/// </summary>
public sealed class FinanceDueCustomerInstrumentsAiTool(
    ICustomerInstrumentService instruments,
    TimeProvider timeProvider,
    ISettingsMoneyCandidateReader? currencies = null) : IAiOwnerReadToolV1
{
    public const string ToolId = "finance.due-customer-instruments.v1";
    public const string ToolVersion = "1";
    public const string KindField = "kind";
    public const string WithinDaysField = "withinDays";
    public const string LimitField = "limit";
    public const int DefaultWithinDays = 7;
    public const int MaximumWithinDays = 90;
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int ScanLimit = 2000;
    public const string TargetKind = "finance.customer-instruments.due";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly AiReadToolArgumentV1[] Arguments =
    [
        AiReadToolArgumentV1.OneOf(KindField, nameof(CustomerInstrumentKind.Cheque), nameof(CustomerInstrumentKind.PromissoryNote)),
        AiReadToolArgumentV1.Integer(WithinDaysField, 0, MaximumWithinDays, DefaultWithinDays),
        AiReadToolArgumentV1.Integer(LimitField, 1, MaximumLimit, DefaultLimit),
    ];

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Finance",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential,
        requiredPermissions: [FinancePermissionNames.CustomerInstrumentRead, FinancePermissionNames.CustomerInstrumentAmountRead],
        fieldAllowlist: [KindField, LimitField, WithinDaysField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "finance.due-customer-instruments.v1/input@1",
            OutputSchema: "finance.due-customer-instruments.v1/output@1",
            FieldPolicy: "finance.due-customer-instruments.v1/fields@1",
            LimitPolicy: "finance.due-customer-instruments.v1/limit-50-days-90-scan-2000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+finance.customer-instrument.read+finance.customer-instrument.amount.read@1",
            SourceContract: "ICustomerInstrumentService.ListAuthorizedAsync@v1",
            RedactionPolicy: "finance.due-customer-instruments.v1/no-serial-drawer-bank-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadToolArgumentReaderV1.TryRead(input.ArgumentsJson, Arguments, out var arguments, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        if (AiReadToolSupportV1.BusinessDay(now) is not { } today)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        CustomerInstrumentKind? kind = arguments.Choice(KindField) is { } text ? Enum.Parse<CustomerInstrumentKind>(text) : null;
        var horizon = today.AddDays(arguments.Integer(WithinDaysField));
        var owner = FinanceAiReadSupport.Context(context);
        var rows = new List<CustomerInstrumentView>();
        var complete = true;
        string? cursor = null;
        try
        {
            do
            {
                var page = await instruments.ListAuthorizedAsync(owner, cursor, cancellationToken);
                if (page is not { Outcome: ReadOutcome.Found, Resource: { } found })
                {
                    return FinanceAiReadSupport.Refusal(page?.Outcome ?? ReadOutcome.Unavailable);
                }

                if (found.CompanyId != context.CompanyId)
                {
                    return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }

                foreach (var item in found.Items)
                {
                    if (item.CompanyId != context.CompanyId || !Enum.IsDefined(item.Status) || !Enum.IsDefined(item.Kind))
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    rows.Add(item);
                }

                cursor = found.NextCursor;
                if (rows.Count > ScanLimit)
                {
                    complete = false;
                    break;
                }
            }
            while (cursor is not null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (rows.Select(row => row.Id).Distinct().Count() != rows.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var due = rows
            .Where(row => row.Status is CustomerInstrumentStatus.InPortfolio or CustomerInstrumentStatus.AtBankForCollection)
            .Where(row => kind is null || row.Kind == kind)
            .Where(row => row.DueOn <= horizon)
            .OrderBy(row => row.DueOn)
            .ThenBy(row => row.Id)
            .ToArray();
        // The amount grant is required by the descriptor; an owner answer that hides the money of a listed item says the
        // grant is gone (Redacted) or the amount cannot be told (Unavailable): release nothing rather than half an answer.
        if (due.Any(row => row.Amount.Disclosure == AmountDisclosure.Redacted))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }

        if (due.Any(row => row.Amount is not { Disclosure: AmountDisclosure.Visible, Money: { } money }
                           || money.CurrencyId != row.CurrencyId
                           || !decimal.TryParse(money.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var codes = await FinanceAiReadSupport.CurrencyCodesAsync(currencies, cancellationToken);
        var listed = due.Take(arguments.Integer(LimitField)).Select(row =>
        {
            var known = codes.TryGetValue(row.CurrencyId, out var code);
            return new FinanceDueCustomerInstrumentV1(
                row.Id,
                row.Kind.ToString(),
                row.Status.ToString(),
                row.PartyId,
                row.DueOn,
                State(row.DueOn, today),
                row.DueOn < today ? today.DayNumber - row.DueOn.DayNumber : 0,
                known ? row.Amount.Money!.Amount : null,
                known ? code : null,
                row.Version);
        }).ToArray();
        var content = new FinanceDueCustomerInstrumentsContentV1(
            today,
            horizon,
            arguments.Choice(KindField),
            complete,
            due.All(row => codes.ContainsKey(row.CurrencyId)),
            complete ? due.Count(row => row.DueOn < today) : null,
            complete ? due.Count(row => row.DueOn == today) : null,
            complete ? due.Count(row => row.DueOn > today) : null,
            complete ? due.Count(row => row.Status == CustomerInstrumentStatus.InPortfolio) : null,
            complete ? due.Count(row => row.Status == CustomerInstrumentStatus.AtBankForCollection) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Vadesi gelen müşteri çek ve senetleri",
            AiReadToolSupportV1.Digest(rows.OrderBy(row => row.Id).Select(row => $"{row.Id:D}:{row.Version}")), now,
            AiDataClassV1.Confidential);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static string State(DateOnly dueOn, DateOnly today) =>
        dueOn < today ? FinanceDueOpenItemsAiTool.Overdue : dueOn == today ? FinanceDueOpenItemsAiTool.DueToday : FinanceDueOpenItemsAiTool.Upcoming;
}

/// <summary>Released fields only; counts are null when <c>Complete</c> is false.</summary>
public sealed record FinanceDueCustomerInstrumentsContentV1(
    DateOnly BusinessDate,
    DateOnly DueUntil,
    string? Kind,
    bool Complete,
    bool CurrencyCodesResolved,
    int? OverdueCount,
    int? DueTodayCount,
    int? UpcomingCount,
    int? InPortfolioCount,
    int? AtBankForCollectionCount,
    int ListedCount,
    IReadOnlyList<FinanceDueCustomerInstrumentV1> Items);

public sealed record FinanceDueCustomerInstrumentV1(
    Guid InstrumentId,
    string Kind,
    string Status,
    Guid PartyId,
    DateOnly DueOn,
    string DueState,
    int DaysOverdue,
    string? Amount,
    string? CurrencyCode,
    string Version);
