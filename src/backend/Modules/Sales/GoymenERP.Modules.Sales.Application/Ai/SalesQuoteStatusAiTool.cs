using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application.Ai;

/// <summary>
/// WP-A3a READ tool <c>sales.quote-status.v1</c> (DALGA_PLANI_v2 A.3): the company's quotes counted by their owner state
/// (<c>Open</c>, <c>Converted</c>, <c>Lost</c>) and the latest ones listed, read ONLY through the Sales owner's paged quote list
/// (<see cref="ISalesQuoteService.GetQuoteListAsync"/>), which measures <c>sales.quotes.read</c> for the server-side actor and
/// company and filters by tenant. No SQL, no table, no other module, no price (the list carries none).
/// <para>
/// Counts are exact integers over every page read; a state that was not asked is not read and its count is null; when the
/// scan cap is reached every count is withheld (null) and <c>complete</c> is false. The validity date is released as the owner
/// recorded it: whether a quote is "expired" is a business-date policy this tool does not compute.
/// </para>
/// </summary>
public sealed class SalesQuoteStatusAiTool(ISalesQuoteService quotes, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "sales.quote-status.v1";
    public const string ToolVersion = "1";
    public const string StateField = "state";
    public const string LimitField = "limit";
    public const string OpenState = "Open";
    public const string ConvertedState = "Converted";
    public const string LostState = "Lost";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int PageSize = 100;
    public const int ScanLimit = 1000;
    public const string TargetKind = "sales.quotes.status";

    private static readonly string[] States = [OpenState, ConvertedState, LostState];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Sales",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [SalesPermissionManifestV1.QuotesRead],
        fieldAllowlist: [LimitField, StateField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "sales.quote-status.v1/input@1",
            OutputSchema: "sales.quote-status.v1/output@1",
            FieldPolicy: "sales.quote-status.v1/fields@1",
            LimitPolicy: "sales.quote-status.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+sales.quotes.read@1",
            SourceContract: "ISalesQuoteService.GetQuoteListAsync@v1",
            RedactionPolicy: "sales.quote-status.v1/no-price-no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var limit = DefaultLimit;
        string? state = null;
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                [StateField] = value => AiReadToolArgumentsV1.TryName(value, States, out state),
                [LimitField] = value => AiReadToolArgumentsV1.TryInt(value, 1, MaximumLimit, out limit),
            }, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        var wantedStates = state is null ? States : [state];
        var rows = new List<SalesQuoteSummaryView>();
        var complete = true;
        try
        {
            foreach (var wanted in wantedStates)
            {
                string? cursor = null;
                do
                {
                    var page = await quotes.GetQuoteListAsync(new SalesQuoteListQuery(PageSize, cursor, wanted), cancellationToken);
                    if (page?.Items is null || page.Items.Count > PageSize)
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    foreach (var item in page.Items)
                    {
                        // The owner answered for another company or another state: release nothing (fail-closed).
                        if (item is null || item.CompanyId != context.CompanyId || !string.Equals(item.State, wanted, StringComparison.Ordinal)
                            || item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.DocumentNo))
                        {
                            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                        }

                        rows.Add(item);
                    }

                    cursor = page.NextCursor;
                    if (rows.Count > ScanLimit)
                    {
                        complete = false;
                        break;
                    }
                }
                while (cursor is not null);

                if (!complete)
                {
                    break;
                }
            }
        }
        catch (SalesDraftServiceException exception) when (exception.Status is 401 or 403)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (rows.Select(row => row.Id).Distinct().Count() != rows.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        int? CountOf(string name) => complete && wantedStates.Contains(name)
            ? rows.Count(row => row.State == name)
            : null;

        var ordered = rows.OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id).ToArray();
        var listed = ordered.Take(limit).Select(row => new SalesQuoteStatusItemV1(
            row.Id, row.DocumentNo, row.State, row.Revision, row.Version, row.PartyId, row.CompanyAccountId, row.BusinessDate,
            row.ValidUntil, row.LineCount, row.ConvertedDraftId, row.CreatedAtUtc)).ToArray();
        var content = new SalesQuoteStatusContentV1(
            complete,
            state,
            CountOf(OpenState),
            CountOf(ConvertedState),
            CountOf(LostState),
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Satış teklifleri durumu",
            AiReadToolArgumentsV1.SourceVersion(ordered.Select(row => $"{row.Id:D}:{row.Version}:{row.State}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>The released fields only: no price, no loss reason text, no free text. A count is null when not read or not complete.</summary>
public sealed record SalesQuoteStatusContentV1(
    bool Complete,
    string? StateFilter,
    int? OpenCount,
    int? ConvertedCount,
    int? LostCount,
    int ListedCount,
    IReadOnlyList<SalesQuoteStatusItemV1> Items);

public sealed record SalesQuoteStatusItemV1(
    Guid QuoteId,
    string DocumentNo,
    string State,
    string Revision,
    string Version,
    Guid PartyId,
    Guid CompanyAccountId,
    DateOnly BusinessDate,
    DateOnly? ValidUntil,
    int LineCount,
    Guid? ConvertedDraftId,
    DateTimeOffset CreatedAtUtc);
