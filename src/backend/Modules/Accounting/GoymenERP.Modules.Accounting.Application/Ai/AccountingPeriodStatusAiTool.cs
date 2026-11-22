using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Modules.Accounting.Application.Ai;

/// <summary>
/// WP-A3c READ tool <c>accounting.period-status.v1</c>: which accounting period covers today (Europe/Istanbul business day,
/// SAT-D10 / P7) and in which state it is — open, proposed (waiting for a second person's approval, Ö4) or closed — plus
/// exact counts by state and the latest periods, read ONLY through the Accounting owner's period list
/// (<see cref="IAccountingOwnerReadV1.ListPeriodsAsync"/>, <c>accounting.periods.read</c>, company-wide, fenced). No preparer
/// or approver identity is released. At the scan cap the counts and the current period are withheld and
/// <c>complete</c> is false.
/// </summary>
public sealed class AccountingPeriodStatusAiTool(IAccountingOwnerReadV1 owner, ICompanyContext company, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "accounting.period-status.v1";
    public const string ToolVersion = "1";
    public const string LimitField = "limit";
    public const int DefaultLimit = 12;
    public const int MaximumLimit = 24;
    public const int ScanLimit = 600;
    public const string NoPeriod = "NoPeriod";
    public const string TargetKind = "accounting.periods.status";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly AiReadToolArgumentV1[] Arguments =
    [
        AiReadToolArgumentV1.Integer(LimitField, 1, MaximumLimit, DefaultLimit),
    ];

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Accounting",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [AccountingPermissionManifestV1.PeriodsRead],
        fieldAllowlist: [LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "accounting.period-status.v1/input@1",
            OutputSchema: "accounting.period-status.v1/output@1",
            FieldPolicy: "accounting.period-status.v1/fields@1",
            LimitPolicy: "accounting.period-status.v1/limit-24-scan-600@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+accounting.periods.read@1",
            SourceContract: "IAccountingOwnerReadV1.ListPeriodsAsync@v1",
            RedactionPolicy: "accounting.period-status.v1/no-person-ids@1"));

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
        if (AiReadToolSupportV1.BusinessDay(now) is not { } today || !AccountingAiReadSupport.SameCompany(company, context))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        List<PeriodViewV1>? periods;
        AiReadResultV1? refusal;
        bool complete;
        try
        {
            (periods, refusal, complete) = await AccountingAiReadSupport.ReadPeriodsAsync(owner, context, ScanLimit, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (refusal is not null || periods is null)
        {
            return refusal ?? new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (!AccountingAiReadSupport.SameCompany(company, context))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var covering = periods.Where(period => period.StartsOn <= today && today <= period.EndsOn).ToArray();
        // Two stored periods covering one day is not an answer the tool may choose between: say it is not complete.
        if (covering.Length > 1)
        {
            complete = false;
        }

        var current = complete && covering.Length == 1 ? Item(covering[0]) : null;
        int? Count(AccountingPeriodStatus status) => complete
            ? periods.Count(period => string.Equals(period.Status, status.ToString(), StringComparison.Ordinal))
            : null;
        var listed = periods
            .OrderByDescending(period => period.StartsOn)
            .ThenBy(period => period.PeriodId)
            .Take(arguments.Integer(LimitField))
            .Select(Item)
            .ToArray();
        var content = new AccountingPeriodStatusContentV1(
            today,
            complete,
            complete ? current?.Status ?? NoPeriod : null,
            current,
            Count(AccountingPeriodStatus.Open),
            Count(AccountingPeriodStatus.Proposed),
            Count(AccountingPeriodStatus.Closed),
            complete ? periods.Count(period => period.Status == nameof(AccountingPeriodStatus.Proposed) && period.IsOwnProposal) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Muhasebe dönemleri",
            AiReadToolSupportV1.Digest(periods.OrderBy(period => period.PeriodId).Select(period => $"{period.PeriodId:D}:{period.Version}")),
            now, AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static AccountingPeriodItemV1 Item(PeriodViewV1 period) =>
        new(period.PeriodId, period.StartsOn, period.EndsOn, period.Status, period.IsOwnProposal, period.Version);
}

/// <summary>
/// <c>CurrentPeriodState</c> is the state of the one period covering <c>BusinessDate</c>, or <c>"NoPeriod"</c>; it, the
/// current period and the counts are null when <c>Complete</c> is false. <c>ProposedByMeCount</c>: proposals the caller
/// prepared and therefore may not approve (Ö4).
/// </summary>
public sealed record AccountingPeriodStatusContentV1(
    DateOnly BusinessDate,
    bool Complete,
    string? CurrentPeriodState,
    AccountingPeriodItemV1? CurrentPeriod,
    int? OpenCount,
    int? ProposedCount,
    int? ClosedCount,
    int? ProposedByMeCount,
    int ListedCount,
    IReadOnlyList<AccountingPeriodItemV1> Items);

public sealed record AccountingPeriodItemV1(
    Guid PeriodId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status,
    bool IsOwnProposal,
    long Version);
