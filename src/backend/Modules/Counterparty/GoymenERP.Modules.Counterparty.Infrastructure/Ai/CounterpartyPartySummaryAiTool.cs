using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Ai;

/// <summary>
/// WP-A3a READ tool <c>counterparty.party-summary.v1</c> (DALGA_PLANI_v2 A.3): ONE Cari party's core summary — code, kind,
/// names (masked for a natural person), active state and this company's account with its roles — read ONLY through the Cari
/// owner's authorized detail read (<see cref="ICounterpartyReadService.GetAuthorizedPartyAsync"/>), which measures
/// <c>counterparty.parties.read</c> (company-wide) in the resolved company and answers nothing for a party without an account
/// there. No SQL here, no other module, no contact, address, tax, bank, balance or risk field (the customer balance and risk
/// belong to the finance owner, not to Cari).
/// <para>
/// The owner does not tell "not visible" from "unknown": both are NotFound here. An answer for another party, or with an account
/// of another company, releases nothing.
/// </para>
/// </summary>
public sealed class CounterpartyPartySummaryAiTool(ICounterpartyReadService parties, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "counterparty.party-summary.v1";
    public const string ToolVersion = "1";
    public const string PartyIdField = "partyId";
    public const string TargetKind = "counterparty.party.summary";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Counterparty",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [ErpPermissions.CounterpartyPartiesRead],
        fieldAllowlist: [PartyIdField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "counterparty.party-summary.v1/input@1",
            OutputSchema: "counterparty.party-summary.v1/output@1",
            FieldPolicy: "counterparty.party-summary.v1/fields@1",
            LimitPolicy: "counterparty.party-summary.v1/single@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+counterparty.parties.read@1",
            SourceContract: "ICounterpartyReadService.GetAuthorizedPartyAsync@v1",
            RedactionPolicy: "counterparty.party-summary.v1/natural-person-masked-no-contact@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var partyId = Guid.Empty;
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                [PartyIdField] = value => AiReadToolArgumentsV1.TryId(value, out partyId),
            }, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        if (partyId == Guid.Empty)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [PartyIdField]);
        }

        CounterpartyDetailView? detail;
        try
        {
            detail = await parties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (detail is null)
        {
            return new AiReadResultV1(AiToolOutcomeV1.NotFound);
        }

        // Another party's answer, an unknown kind or role, or an account outside this company: release nothing (fail-closed).
        if (detail.Id != partyId || string.IsNullOrWhiteSpace(detail.Code) || string.IsNullOrWhiteSpace(detail.Version)
            || !CounterpartyAiRelease.IsKnownKind(detail.Kind) || detail.CompanyAccounts is not { Count: 1 }
            || detail.CompanyAccounts[0] is not { } account || account.CompanyId != context.CompanyId || account.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(account.Version) || !CounterpartyAiRelease.IsKnownRoles(account.Roles))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var party = new CounterpartyAiPartyV1(detail.Id, detail.Code, detail.Kind.ToString(),
            CounterpartyAiRelease.Name(detail.Kind, detail.LegalName), CounterpartyAiRelease.Name(detail.Kind, detail.TradeName),
            detail.Kind == Domain.Parties.PartyKind.NaturalPerson, detail.IsActive, detail.Version,
            new CounterpartyAiAccountV1(account.Id, CounterpartyAiRelease.RoleNames(account.Roles), account.IsActive, account.Version));
        var content = new CounterpartyPartySummaryContentV1(CounterpartyAiRelease.Masking, CounterpartyAiRelease.FieldPolicy,
            CounterpartyAiRelease.EffectiveClass(CounterpartyAiRelease.PartyFields).ToString(),
            CounterpartyAiRelease.Classes(CounterpartyAiRelease.PartyFields), party);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, detail.Id, "Cari özeti",
            AiReadToolArgumentsV1.SourceVersion([$"{detail.Id:D}:{detail.Version}:{account.Id:D}:{account.Version}"]),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>All Internal under AI-D42; the authorised persons and their channels are <c>counterparty.party-contacts.v1</c> (Confidential).</summary>
public sealed record CounterpartyPartySummaryContentV1(string Masking, string FieldPolicy, string DataClass,
    IReadOnlyDictionary<string, string> FieldClasses, CounterpartyAiPartyV1 Party);
