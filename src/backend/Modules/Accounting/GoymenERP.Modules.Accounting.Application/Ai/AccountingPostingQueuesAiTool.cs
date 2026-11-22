using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;

namespace GoymenERP.Modules.Accounting.Application.Ai;

/// <summary>
/// WP-A3c READ tool <c>accounting.posting-queues.v1</c>: the source facts whose journal is not yet created — waiting for an
/// account mapping (a posting exception) or for an amount (a pending valuation, M-01) — counted by queue and posting
/// purpose and listed, read ONLY through the Accounting owner's queue read
/// (<see cref="IAccountingOwnerReadV1.GetPostingQueuesAsync"/>, <c>accounting.posting-queues.read</c>). These are the
/// "kayda dönmeyi bekleyen" items; nothing is guessed or posted. No amount is part of either queue.
/// </summary>
public sealed class AccountingPostingQueuesAiTool(IAccountingOwnerReadV1 owner, ICompanyContext company, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "accounting.posting-queues.v1";
    public const string ToolVersion = "1";
    public const string QueueField = "queue";
    public const string LimitField = "limit";
    public const string MissingMapping = "MissingMapping";
    public const string PendingValuation = "PendingValuation";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const string TargetKind = "accounting.posting-queues";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly AiReadToolArgumentV1[] Arguments =
    [
        AiReadToolArgumentV1.OneOf(QueueField, MissingMapping, PendingValuation),
        AiReadToolArgumentV1.Integer(LimitField, 1, MaximumLimit, DefaultLimit),
    ];

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Accounting",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [AccountingPermissionManifestV1.PostingQueuesRead],
        fieldAllowlist: [LimitField, QueueField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "accounting.posting-queues.v1/input@1",
            OutputSchema: "accounting.posting-queues.v1/output@1",
            FieldPolicy: "accounting.posting-queues.v1/fields@1",
            LimitPolicy: "accounting.posting-queues.v1/limit-50@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+accounting.posting-queues.read@1",
            SourceContract: "IAccountingOwnerReadV1.GetPostingQueuesAsync@v1",
            RedactionPolicy: "accounting.posting-queues.v1/no-message-ids@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, Arguments, out var arguments, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        if (!AccountingAiReadSupport.SameCompany(company, context))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        AccountingApiResult<PostingQueuesV1> result;
        try
        {
            result = await owner.GetPostingQueuesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (result is not { Status: AccountingApiStatus.Ok, Resource: { } queues })
        {
            return AccountingAiReadSupport.Refusal(result?.Status ?? AccountingApiStatus.Unavailable);
        }

        if (!AccountingAiReadSupport.SameCompany(company, context)
            || queues.Exceptions is null || queues.Pending is null
            || queues.Exceptions.Any(entry => entry is null || entry.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(entry.PostingPurpose)
                                              || entry.MissingMappingKeys is null)
            || queues.Pending.Any(entry => entry is null || entry.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(entry.PostingPurpose)
                                           || entry.LinesAwaitingAmount < 0))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var queue = arguments.Choice(QueueField);
        var entries = queues.Exceptions
            .Select(entry => new AccountingPostingQueueItemV1(MissingMapping, entry.SourceId, entry.PostingPurpose,
                entry.PostingRuleVersion, [.. entry.MissingMappingKeys.Order(StringComparer.Ordinal)], null))
            .Concat(queues.Pending.Select(entry => new AccountingPostingQueueItemV1(PendingValuation, entry.SourceId,
                entry.PostingPurpose, entry.PostingRuleVersion, [], entry.LinesAwaitingAmount)))
            .Where(entry => queue is null || entry.Queue == queue)
            .OrderBy(entry => entry.Queue, StringComparer.Ordinal)
            .ThenBy(entry => entry.PostingPurpose, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourceId)
            .ToArray();
        var byPurpose = entries
            .GroupBy(entry => (entry.Queue, entry.PostingPurpose))
            .Select(group => new AccountingPostingQueueGroupV1(group.Key.Queue, group.Key.PostingPurpose, group.Count()))
            .OrderBy(group => group.Queue, StringComparer.Ordinal)
            .ThenBy(group => group.PostingPurpose, StringComparer.Ordinal)
            .ToArray();
        var listed = entries.Take(arguments.Integer(LimitField)).ToArray();
        var content = new AccountingPostingQueuesContentV1(
            queue,
            true,
            queue is null or MissingMapping ? queues.Exceptions.Count : null,
            queue is null or PendingValuation ? queues.Pending.Count : null,
            byPurpose,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Kayıt bekleyen muhasebe kaynakları",
            AiReadToolSupportV1.Digest(entries.Select(entry => $"{entry.Queue}:{entry.SourceId:D}:{entry.PostingPurpose}:{entry.PostingRuleVersion}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>Counts are exact; the count of a queue not asked for is null. The owner's message ids are not released.</summary>
public sealed record AccountingPostingQueuesContentV1(
    string? Queue,
    bool Complete,
    int? MissingMappingCount,
    int? PendingValuationCount,
    IReadOnlyList<AccountingPostingQueueGroupV1> ByPostingPurpose,
    int ListedCount,
    IReadOnlyList<AccountingPostingQueueItemV1> Items);

public sealed record AccountingPostingQueueGroupV1(string Queue, string PostingPurpose, int Count);

public sealed record AccountingPostingQueueItemV1(
    string Queue,
    Guid SourceId,
    string PostingPurpose,
    string PostingRuleVersion,
    IReadOnlyList<string> MissingMappingKeys,
    int? LinesAwaitingAmount);
