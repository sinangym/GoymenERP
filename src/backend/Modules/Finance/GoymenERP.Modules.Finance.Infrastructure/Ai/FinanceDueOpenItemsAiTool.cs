using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure.Ai;

/// <summary>
/// WP-A3c READ tool <c>finance.due-open-items.v1</c> (DALGA_PLANI_v2 A.3): the company's open receivables and payables
/// that are overdue, due today or due within the asked number of days — counted and listed — read ONLY through the
/// Finance owner's FI04 read (<see cref="IFinanceOpenItemReadServiceV1.ListAuthorizedAsync"/>), which measures
/// <c>finance.open-items.read</c> and <c>finance.open-items.amount.read</c> for the server-side actor and company before
/// and after its database read. No SQL, no table, no other module except the Settings currency identities.
/// <para>
/// An item is open only when the owner says <see cref="FinanceOpenItemStatusV1.Open"/>; that status is disclosed with the
/// amount authority, so an answer without visible amounts is refused, never guessed. The "today" of a due date is the
/// Europe/Istanbul business day (SAT-D10, P7). Counts are exact integers over every page read; when the scan cap is reached
/// the counts are withheld (null) and <c>complete</c> is false. Amounts are the owner's stored outstanding text with an
/// explicit ISO currency code; an amount whose currency code the Settings owner does not answer is withheld and
/// <c>currencyCodesResolved</c> is false. No total or converted amount is derived (FI04; a total is NEEDS-SINAN).
/// </para>
/// </summary>
public sealed class FinanceDueOpenItemsAiTool(
    IFinanceOpenItemReadServiceV1 openItems,
    TimeProvider timeProvider,
    ISettingsMoneyCandidateReader? currencies = null) : IAiOwnerReadToolV1
{
    public const string ToolId = "finance.due-open-items.v1";
    public const string ToolVersion = "1";
    public const string DirectionField = "direction";
    public const string WithinDaysField = "withinDays";
    public const string LimitField = "limit";
    public const string Receivable = "Receivable";
    public const string Payable = "Payable";
    public const int DefaultWithinDays = 7;
    public const int MaximumWithinDays = 90;
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int ScanLimit = 2000;
    public const string TargetKind = "finance.open-items.due";
    public const string Overdue = "Overdue";
    public const string DueToday = "DueToday";
    public const string Upcoming = "Upcoming";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly AiReadToolArgumentV1[] Arguments =
    [
        AiReadToolArgumentV1.OneOf(DirectionField, Receivable, Payable),
        AiReadToolArgumentV1.Integer(WithinDaysField, 0, MaximumWithinDays, DefaultWithinDays),
        AiReadToolArgumentV1.Integer(LimitField, 1, MaximumLimit, DefaultLimit),
    ];

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Finance",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential,
        requiredPermissions: [FinancePermissionNames.OpenItemRead, FinancePermissionNames.OpenItemAmountRead],
        fieldAllowlist: [DirectionField, LimitField, WithinDaysField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "finance.due-open-items.v1/input@1",
            OutputSchema: "finance.due-open-items.v1/output@1",
            FieldPolicy: "finance.due-open-items.v1/fields@1",
            LimitPolicy: "finance.due-open-items.v1/limit-50-days-90-scan-2000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+finance.open-items.read+finance.open-items.amount.read@1",
            SourceContract: "IFinanceOpenItemReadServiceV1.ListAuthorizedAsync@v1",
            RedactionPolicy: "finance.due-open-items.v1/amount-with-currency-code-no-total@1"));

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

        var direction = arguments.Choice(DirectionField) switch
        {
            Receivable => FinanceOpenItemDirectionV1.Receivable,
            Payable => FinanceOpenItemDirectionV1.Payable,
            _ => (FinanceOpenItemDirectionV1?)null,
        };
        var horizon = today.AddDays(arguments.Integer(WithinDaysField));
        var owner = FinanceAiReadSupport.Context(context);
        var rows = new List<FinanceOpenItemViewV1>();
        var complete = true;
        string? cursor = null;
        try
        {
            do
            {
                var page = await openItems.ListAuthorizedAsync(owner, new SourceListRequest(cursor), cancellationToken);
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
                    // Another company's row: release nothing. Money hidden: the open state is not known, refuse.
                    if (item.CompanyId != context.CompanyId)
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    if (item.OutstandingAmount.Disclosure == AmountDisclosure.Redacted)
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Denied);
                    }

                    if (item.OutstandingAmount is not { Disclosure: AmountDisclosure.Visible, Money: { } money }
                        || item.Status is null || money.CurrencyId != item.CurrencyId
                        || !decimal.TryParse(money.Amount, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture, out _))
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

        var open = rows
            .Where(row => row.Status == FinanceOpenItemStatusV1.Open)
            .Where(row => direction is null || row.Direction == direction)
            .ToArray();
        var due = open
            .Where(row => row.DueOn is { } dueOn && dueOn <= horizon)
            .OrderBy(row => row.DueOn)
            .ThenBy(row => row.Id)
            .ToArray();
        var codes = await FinanceAiReadSupport.CurrencyCodesAsync(currencies, cancellationToken);
        var currencyCodesResolved = due.All(row => codes.ContainsKey(row.CurrencyId));

        int? Count(FinanceOpenItemDirectionV1 wanted, string state) =>
            complete ? due.Count(row => row.Direction == wanted && State(row.DueOn!.Value, today) == state) : null;

        var listed = due.Take(arguments.Integer(LimitField)).Select(row =>
        {
            var dueOn = row.DueOn!.Value;
            var known = codes.TryGetValue(row.CurrencyId, out var code);
            return new FinanceDueOpenItemV1(
                row.Id,
                row.Direction.ToString(),
                row.SourceKind,
                row.PartyId,
                dueOn,
                State(dueOn, today),
                dueOn < today ? today.DayNumber - dueOn.DayNumber : 0,
                known ? row.OutstandingAmount.Money!.Amount : null,
                known ? code : null,
                row.Version);
        }).ToArray();
        var content = new FinanceDueOpenItemsContentV1(
            today,
            horizon,
            arguments.Choice(DirectionField),
            complete,
            currencyCodesResolved,
            Count(FinanceOpenItemDirectionV1.Receivable, Overdue),
            Count(FinanceOpenItemDirectionV1.Receivable, DueToday),
            Count(FinanceOpenItemDirectionV1.Receivable, Upcoming),
            Count(FinanceOpenItemDirectionV1.Payable, Overdue),
            Count(FinanceOpenItemDirectionV1.Payable, DueToday),
            Count(FinanceOpenItemDirectionV1.Payable, Upcoming),
            complete ? open.Count(row => row.DueOn is null) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Vadesi gelen açık kalemler",
            AiReadToolSupportV1.Digest(rows.OrderBy(row => row.Id).Select(row => $"{row.Id:D}:{row.Version}")), now,
            AiDataClassV1.Confidential);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static string State(DateOnly dueOn, DateOnly today) =>
        dueOn < today ? Overdue : dueOn == today ? DueToday : Upcoming;
}

/// <summary>
/// Released fields only (no description, bank reference or actor). Counts are null when <c>Complete</c> is false;
/// <c>OpenWithoutDueDateCount</c> says how many open items carry no due date and are therefore never "due".
/// </summary>
public sealed record FinanceDueOpenItemsContentV1(
    DateOnly BusinessDate,
    DateOnly DueUntil,
    string? Direction,
    bool Complete,
    bool CurrencyCodesResolved,
    int? ReceivableOverdueCount,
    int? ReceivableDueTodayCount,
    int? ReceivableUpcomingCount,
    int? PayableOverdueCount,
    int? PayableDueTodayCount,
    int? PayableUpcomingCount,
    int? OpenWithoutDueDateCount,
    int ListedCount,
    IReadOnlyList<FinanceDueOpenItemV1> Items);

/// <summary>One due item. <c>OutstandingAmount</c> and <c>CurrencyCode</c> are both present or both withheld.</summary>
public sealed record FinanceDueOpenItemV1(
    Guid OpenItemId,
    string Direction,
    string SourceKind,
    Guid PartyId,
    DateOnly DueOn,
    string DueState,
    int DaysOverdue,
    string? OutstandingAmount,
    string? CurrencyCode,
    string Version);
