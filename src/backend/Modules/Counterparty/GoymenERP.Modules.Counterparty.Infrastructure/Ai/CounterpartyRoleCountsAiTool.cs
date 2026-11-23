using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Ai;

/// <summary>
/// WP-A3a READ tool <c>counterparty.role-counts.v1</c> (DALGA_PLANI_v2 A.3): how many ACTIVE Cari parties hold an ACTIVE account
/// of this company, in total and per role (<c>Customer</c>, <c>Supplier</c>, <c>Tolling</c>, <c>Service</c>; a party with two
/// roles is counted under each). Every figure is the owner's own exact filtered count from its authorized list read
/// (<see cref="ICounterpartyReadService.GetAuthorizedPageAsync"/> with the count requested), which measures
/// <c>counterparty.parties.read</c> (company-wide). No SQL here, no name or other party field is released.
/// <para>
/// A count the owner does not give is null and <c>complete</c> is false; nothing is estimated or summed here (the per-role
/// counts overlap and are never added into the total).
/// </para>
/// </summary>
public sealed class CounterpartyRoleCountsAiTool(ICounterpartyReadService parties, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "counterparty.role-counts.v1";
    public const string ToolVersion = "1";
    public const string TargetKind = "counterparty.parties.role-counts";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Counterparty",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [ErpPermissions.CounterpartyPartiesRead],
        fieldAllowlist: [],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "counterparty.role-counts.v1/input@1",
            OutputSchema: "counterparty.role-counts.v1/output@1",
            FieldPolicy: "counterparty.role-counts.v1/fields@1",
            LimitPolicy: "counterparty.role-counts.v1/five-owner-counts@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+counterparty.parties.read@1",
            SourceContract: "ICounterpartyReadService.GetAuthorizedPageAsync@v1",
            RedactionPolicy: "counterparty.role-counts.v1/counts-only@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal),
                out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        var counts = new Dictionary<PartyRole, int?>();
        try
        {
            foreach (var role in new[] { PartyRole.None }.Concat(CounterpartyAiRelease.Roles.Select(entry => entry.Role)))
            {
                var query = new CounterpartyPageQuery(null, null, null, null, null, role, CounterpartyRoleMatch.Any,
                    CounterpartyRecordState.Active, CounterpartyRecordState.Active, CounterpartySort.Code,
                    CounterpartySortDirection.Ascending, PageSize: 1, Cursor: null, IncludeCount: true);
                var execution = await parties.GetAuthorizedPageAsync(new CounterpartyReadRequest(query, context.ActorId, context.SessionId),
                    cancellationToken);
                switch (execution?.Result)
                {
                    case CounterpartyPageResult.Forbidden:
                        return new AiReadResultV1(AiToolOutcomeV1.Denied);
                    case CounterpartyPageResult.Succeeded when execution.Page is { Items: not null } page && page.Items.Count <= 1:
                        // Even one sample row must be this company's: another company's account releases nothing.
                        if (page.Items.Any(item => item?.CompanyAccounts is null
                                                   || item.CompanyAccounts.Any(account => account is null || account.CompanyId != context.CompanyId)))
                        {
                            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                        }

                        counts[role] = page.TotalCount is { } total && total >= page.Items.Count && total <= int.MaxValue
                                       && !string.IsNullOrWhiteSpace(page.CountScope)
                            ? (int)total
                            : null;
                        break;
                    default:
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var content = new CounterpartyRoleCountsContentV1(
            counts.Values.All(value => value is not null),
            CounterpartyAiRelease.FieldPolicy,
            CounterpartyAiRelease.EffectiveClass(["count"]).ToString(),
            counts[PartyRole.None],
            counts[PartyRole.Customer],
            counts[PartyRole.Supplier],
            counts[PartyRole.Tolling],
            counts[PartyRole.Service]);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Cari rol sayıları",
            AiReadToolArgumentsV1.SourceVersion(counts.OrderBy(entry => (int)entry.Key)
                .Select(entry => $"{(int)entry.Key}:{entry.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>Counts only. The role counts overlap (a party with two roles is in both) and do not add up to the total.</summary>
public sealed record CounterpartyRoleCountsContentV1(
    bool Complete,
    string FieldPolicy,
    string DataClass,
    int? ActivePartyCount,
    int? CustomerCount,
    int? SupplierCount,
    int? TollingCount,
    int? ServiceCount);
