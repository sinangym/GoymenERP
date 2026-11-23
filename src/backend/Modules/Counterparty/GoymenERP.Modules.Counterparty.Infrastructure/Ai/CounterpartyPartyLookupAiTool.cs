using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Ai;

/// <summary>
/// WP-A3a READ tool <c>counterparty.party-lookup.v1</c> (DALGA_PLANI_v2 A.3): the company's ACTIVE Cari parties whose code,
/// legal or trade name contains the asked text (the C-01 list's own Turkish matching), optionally narrowed to one role, read
/// ONLY through the Cari owner's authorized list read (<see cref="ICounterpartyReadService.GetAuthorizedPageAsync"/>), which
/// measures <c>counterparty.parties.read</c> (company-wide) for the server-side actor, company and session. No SQL here, no
/// other module, no contact, address, tax or bank field.
/// <para>
/// <c>matchCount</c> is the owner's exact filtered count; when the owner cannot give it, it is null and <c>complete</c> is false.
/// A natural person's names are masked (<see cref="CounterpartyAiRelease.PersonMask"/>). A party carrying an account of another
/// company, or none of this one, releases nothing.
/// </para>
/// </summary>
public sealed class CounterpartyPartyLookupAiTool(ICounterpartyReadService parties, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "counterparty.party-lookup.v1";
    public const string ToolVersion = "1";
    public const string QueryField = "query";
    public const string RoleField = "role";
    public const string LimitField = "limit";
    public const int MinimumQueryLength = 2;
    public const int MaximumQueryLength = 80;
    public const int DefaultLimit = 10;
    public const int MaximumLimit = 20;
    public const string TargetKind = "counterparty.parties.lookup";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Counterparty",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [ErpPermissions.CounterpartyPartiesRead],
        fieldAllowlist: [LimitField, QueryField, RoleField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "counterparty.party-lookup.v1/input@1",
            OutputSchema: "counterparty.party-lookup.v1/output@1",
            FieldPolicy: "counterparty.party-lookup.v1/fields@1",
            LimitPolicy: "counterparty.party-lookup.v1/limit-20@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+counterparty.parties.read@1",
            SourceContract: "ICounterpartyReadService.GetAuthorizedPageAsync@v1",
            RedactionPolicy: "counterparty.party-lookup.v1/natural-person-masked-no-contact@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        string? text = null;
        string? roleName = null;
        var limit = DefaultLimit;
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                [QueryField] = value => AiReadToolArgumentsV1.TryText(value, MinimumQueryLength, MaximumQueryLength, out text),
                [RoleField] = value => AiReadToolArgumentsV1.TryName(value, [.. CounterpartyAiRelease.Roles.Select(entry => entry.Name)], out roleName),
                [LimitField] = value => AiReadToolArgumentsV1.TryInt(value, 1, MaximumLimit, out limit),
            }, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        if (text is null)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [QueryField]);
        }

        var role = roleName is null ? PartyRole.None : CounterpartyAiRelease.Roles.Single(entry => entry.Name == roleName).Role;
        var query = new CounterpartyPageQuery(text, null, null, null, null, role, CounterpartyRoleMatch.Any,
            CounterpartyRecordState.Active, role == PartyRole.None ? CounterpartyRecordState.All : CounterpartyRecordState.Active,
            CounterpartySort.Code, CounterpartySortDirection.Ascending, limit, null, IncludeCount: true);
        CounterpartyPageExecution execution;
        try
        {
            execution = await parties.GetAuthorizedPageAsync(new CounterpartyReadRequest(query, context.ActorId, context.SessionId),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        switch (execution?.Result)
        {
            case CounterpartyPageResult.Forbidden:
                return new AiReadResultV1(AiToolOutcomeV1.Denied);
            case CounterpartyPageResult.Succeeded when execution.Page is { Items: not null } page && page.Items.Count <= limit:
                var released = new List<CounterpartyAiPartyV1>(page.Items.Count);
                foreach (var item in page.Items)
                {
                    if (!TryRelease(item, context.CompanyId, role, out var party))
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    released.Add(party);
                }

                if (released.Select(party => party.PartyId).Distinct().Count() != released.Count)
                {
                    return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }

                var countKnown = page.TotalCount is { } total && total >= released.Count && total <= int.MaxValue
                                 && !string.IsNullOrWhiteSpace(page.CountScope);
                var content = new CounterpartyPartyLookupContentV1(
                    countKnown,
                    roleName,
                    countKnown ? (int)page.TotalCount!.Value : null,
                    page.HasMore,
                    CounterpartyAiRelease.Masking,
                    CounterpartyAiRelease.FieldPolicy,
                    CounterpartyAiRelease.EffectiveClass(CounterpartyAiRelease.PartyFields).ToString(),
                    CounterpartyAiRelease.Classes(CounterpartyAiRelease.PartyFields),
                    released.Count,
                    released);
                var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Cari arama",
                    AiReadToolArgumentsV1.SourceVersion(released.Select(party => $"{party.PartyId:D}:{party.Version}:{party.Account.Version}")),
                    timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
                return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
            default:
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }
    }

    private static bool TryRelease(CounterpartyListItem? item, Guid companyId, PartyRole role, out CounterpartyAiPartyV1 party)
    {
        party = null!;
        if (item?.CompanyAccounts is null || item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Code)
            || !CounterpartyAiRelease.IsKnownKind(item.Kind) || !item.IsActive
            || item.CompanyAccounts.Count != 1)
        {
            return false;
        }

        var account = item.CompanyAccounts[0];
        if (account is null || account.CompanyId != companyId || account.Id == Guid.Empty
            || !CounterpartyAiRelease.IsKnownRoles(account.Roles)
            || role != PartyRole.None && (!account.Roles.HasFlag(role) || !account.IsActive))
        {
            return false;
        }

        var masked = item.Kind == Domain.Parties.PartyKind.NaturalPerson;
        party = new CounterpartyAiPartyV1(item.Id, item.Code, item.Kind.ToString(),
            CounterpartyAiRelease.Name(item.Kind, item.LegalName), CounterpartyAiRelease.Name(item.Kind, item.TradeName), masked,
            item.IsActive, item.Version.ToString(CultureInfo.InvariantCulture),
            new CounterpartyAiAccountV1(account.Id, CounterpartyAiRelease.RoleNames(account.Roles), account.IsActive,
                account.Version.ToString(CultureInfo.InvariantCulture)));
        return true;
    }
}

/// <summary>The released fields only (all Internal, AI-D42): no contact, address, tax identity or bank field; natural-person names masked.</summary>
public sealed record CounterpartyPartyLookupContentV1(
    bool Complete,
    string? RoleFilter,
    int? MatchCount,
    bool Truncated,
    string Masking,
    string FieldPolicy,
    string DataClass,
    IReadOnlyDictionary<string, string> FieldClasses,
    int ListedCount,
    IReadOnlyList<CounterpartyAiPartyV1> Items);
