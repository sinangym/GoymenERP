using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Modules.Accounting.Application.Ai;

/// <summary>
/// WP-A3c READ tool <c>accounting.period-journals.v1</c>: how many journals (fiş) are recorded in the current or the previous
/// accounting period — by kind (original, reversal, correction) and posting purpose — and the latest of them, read ONLY
/// through the Accounting owner's reads (<see cref="IAccountingOwnerReadV1.ListPeriodsAsync"/> to find the period,
/// <see cref="IAccountingOwnerReadV1.ListJournalsAsync"/> for its journals; <c>accounting.periods.read</c> and
/// <c>accounting.journals.read</c>). The journals' debit/credit totals and lines are NOT released: this is a count, not a
/// trial balance. At the scan cap the counts are withheld and <c>complete</c> is false.
/// <para>
/// A journal draft waiting for approval (NormalJournalDraft) has no list read yet, so "açık (onay bekleyen) fiş" is not
/// counted here; the answer says so with <c>draftJournalsCounted = false</c> (NEEDS-SINAN in the WP-A3c report).
/// </para>
/// </summary>
public sealed class AccountingPeriodJournalsAiTool(IAccountingOwnerReadV1 owner, ICompanyContext company, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "accounting.period-journals.v1";
    public const string ToolVersion = "1";
    public const string PeriodField = "period";
    public const string LimitField = "limit";
    public const string Current = "Current";
    public const string Previous = "Previous";
    public const int DefaultLimit = 10;
    public const int MaximumLimit = 50;
    public const int PeriodScanLimit = 600;
    public const int JournalScanLimit = 5000;
    public const string TargetKind = "accounting.period-journals";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly AiReadToolArgumentV1[] Arguments =
    [
        AiReadToolArgumentV1.OneOf(PeriodField, Current, Previous),
        AiReadToolArgumentV1.Integer(LimitField, 1, MaximumLimit, DefaultLimit),
    ];

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Accounting",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [AccountingPermissionManifestV1.JournalsRead, AccountingPermissionManifestV1.PeriodsRead],
        fieldAllowlist: [LimitField, PeriodField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "accounting.period-journals.v1/input@1",
            OutputSchema: "accounting.period-journals.v1/output@1",
            FieldPolicy: "accounting.period-journals.v1/fields@1",
            LimitPolicy: "accounting.period-journals.v1/limit-50-scan-5000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+accounting.periods.read+accounting.journals.read@1",
            SourceContract: "IAccountingOwnerReadV1.ListPeriodsAsync@v1+IAccountingOwnerReadV1.ListJournalsAsync@v1",
            RedactionPolicy: "accounting.period-journals.v1/no-amounts-no-lines@1"));

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

        var which = arguments.Choice(PeriodField) ?? Current;
        try
        {
            var (periods, refusal, periodsComplete) =
                await AccountingAiReadSupport.ReadPeriodsAsync(owner, context, PeriodScanLimit, cancellationToken);
            if (refusal is not null || periods is null)
            {
                return refusal ?? new AiReadResultV1(AiToolOutcomeV1.Unavailable);
            }

            // The period is chosen only from a complete list; two periods covering today is not a choice the tool makes.
            var covering = periods.Where(period => period.StartsOn <= today && today <= period.EndsOn).ToArray();
            if (!periodsComplete || covering.Length > 1)
            {
                return Answer(context, now, today, which, null, false, [], arguments.Integer(LimitField));
            }

            PeriodViewV1? chosen = covering.SingleOrDefault();
            if (which == Previous)
            {
                var before = chosen?.StartsOn ?? today;
                chosen = periods
                    .Where(period => period.EndsOn < before)
                    .OrderByDescending(period => period.EndsOn)
                    .ThenBy(period => period.PeriodId)
                    .FirstOrDefault();
            }

            if (chosen is null)
            {
                return Answer(context, now, today, which, null, true, [], arguments.Integer(LimitField));
            }

            var journals = new List<JournalSummaryV1>();
            var complete = true;
            string? cursor = null;
            do
            {
                var page = await owner.ListJournalsAsync(chosen.PeriodId, AccountingAiReadSupport.PageSize, cursor, cancellationToken);
                if (page is not { Status: AccountingApiStatus.Ok, Resource: { } found })
                {
                    return AccountingAiReadSupport.Refusal(page?.Status ?? AccountingApiStatus.Unavailable);
                }

                foreach (var journal in found.Items)
                {
                    // A journal of another period, or of an unknown kind, is not part of this answer: release nothing.
                    if (journal is null || journal.PeriodId != chosen.PeriodId
                        || !Enum.TryParse<JournalKind>(journal.Kind, out var kind) || !Enum.IsDefined(kind))
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    journals.Add(journal);
                }

                cursor = found.NextCursor;
                if (journals.Count > JournalScanLimit)
                {
                    complete = false;
                    break;
                }
            }
            while (cursor is not null);

            if (journals.Select(journal => journal.JournalId).Distinct().Count() != journals.Count
                || !AccountingAiReadSupport.SameCompany(company, context))
            {
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
            }

            return Answer(context, now, today, which, chosen, complete, journals, arguments.Integer(LimitField));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }
    }

    private static AiReadResultV1 Answer(AiServerContextV1 context, DateTimeOffset now, DateOnly today, string which,
        PeriodViewV1? period, bool complete, IReadOnlyList<JournalSummaryV1> journals, int limit)
    {
        int? Count(JournalKind kind) => complete && period is not null
            ? journals.Count(journal => journal.Kind == kind.ToString())
            : null;
        var byPurpose = complete && period is not null
            ? journals.GroupBy(journal => journal.PostingPurpose, StringComparer.Ordinal)
                .Select(group => new AccountingJournalPurposeCountV1(group.Key, group.Count()))
                .OrderBy(group => group.PostingPurpose, StringComparer.Ordinal)
                .ToArray()
            : [];
        var listed = journals
            .OrderByDescending(journal => journal.AccountingDate)
            .ThenByDescending(journal => journal.StoredAtUtc)
            .ThenBy(journal => journal.JournalId)
            .Take(limit)
            .Select(journal => new AccountingJournalItemV1(journal.JournalId, journal.Kind, journal.PostingPurpose,
                journal.SourceId, journal.AccountingDate, journal.LineCount, journal.LinkedJournalId))
            .ToArray();
        var content = new AccountingPeriodJournalsContentV1(
            today,
            which,
            complete,
            period is null ? null : new AccountingPeriodItemV1(period.PeriodId, period.StartsOn, period.EndsOn, period.Status,
                period.IsOwnProposal, period.Version),
            complete ? journals.Count : null,
            Count(JournalKind.Original),
            Count(JournalKind.Reversal),
            Count(JournalKind.Correction),
            byPurpose,
            DraftJournalsCounted: false,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Dönemdeki muhasebe fişleri",
            AiReadToolSupportV1.Digest([$"period:{period?.PeriodId:D}:{period?.Version}",
                .. journals.OrderBy(journal => journal.JournalId).Select(journal => $"{journal.JournalId:D}:{journal.Kind}")]),
            now, AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>
/// <c>Period</c> is null when no period matches (then <c>JournalCount</c> is 0 when <c>Complete</c>). Counts are null when
/// <c>Complete</c> is false. No amount or line is released.
/// </summary>
public sealed record AccountingPeriodJournalsContentV1(
    DateOnly BusinessDate,
    string PeriodSelector,
    bool Complete,
    AccountingPeriodItemV1? Period,
    int? JournalCount,
    int? OriginalCount,
    int? ReversalCount,
    int? CorrectionCount,
    IReadOnlyList<AccountingJournalPurposeCountV1> ByPostingPurpose,
    bool DraftJournalsCounted,
    int ListedCount,
    IReadOnlyList<AccountingJournalItemV1> Items);

public sealed record AccountingJournalPurposeCountV1(string PostingPurpose, int Count);

public sealed record AccountingJournalItemV1(
    Guid JournalId,
    string Kind,
    string PostingPurpose,
    Guid SourceId,
    DateOnly AccountingDate,
    int LineCount,
    Guid? LinkedJournalId);
