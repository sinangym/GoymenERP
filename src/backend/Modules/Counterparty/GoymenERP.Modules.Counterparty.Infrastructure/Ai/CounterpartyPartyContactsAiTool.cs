using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Ai;

/// <summary>
/// WP-A3a READ tool <c>counterparty.party-contacts.v1</c>: ONE Cari party's authorised persons (name, job title, purpose) and
/// their phone / e-mail channels in this company, with the account's general phone and e-mail — read ONLY through the Cari
/// owner's contact read (<see cref="ICounterpartyContactInformationService.GetContactInformationAsync"/>), which measures
/// <c>counterparty.parties.read</c> (company-wide) and reads the context company's account only.
/// <para>
/// AI-D42: the authorised person's name, phone and e-mail are Confidential (in-house model only), so the tool is catalogued
/// Confidential and the runtime does not run it while <c>Ai:MaxDataClass</c> is below that. Each field carries its class in
/// the output; the answer's effective class is the highest class of the fields actually returned (an account with no person,
/// phone or e-mail answers Internal). Addresses, the website and TCKN are not released. Passive persons and channels are left
/// out.
/// </para>
/// </summary>
public sealed class CounterpartyPartyContactsAiTool(ICounterpartyContactInformationService contacts, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "counterparty.party-contacts.v1";
    public const string ToolVersion = "1";
    public const string PartyIdField = "partyId";
    public const string LimitField = "limit";
    public const int DefaultLimit = 10;
    public const int MaximumLimit = 20;
    public const string TargetKind = "counterparty.party.contacts";

    private static readonly string[] ChannelKinds = ["phone", "email"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Counterparty",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential,
        requiredPermissions: [ErpPermissions.CounterpartyPartiesRead],
        fieldAllowlist: [LimitField, PartyIdField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "counterparty.party-contacts.v1/input@1",
            OutputSchema: "counterparty.party-contacts.v1/output@1",
            FieldPolicy: "counterparty.party-contacts.v1/fields-AI-D42@1",
            LimitPolicy: "counterparty.party-contacts.v1/limit-20@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+counterparty.parties.read@1",
            SourceContract: "ICounterpartyContactInformationService.GetContactInformationAsync@v1",
            RedactionPolicy: "counterparty.party-contacts.v1/no-address-no-website-no-tckn@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var partyId = Guid.Empty;
        var limit = DefaultLimit;
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                [PartyIdField] = value => AiReadToolArgumentsV1.TryId(value, out partyId),
                [LimitField] = value => AiReadToolArgumentsV1.TryInt(value, 1, MaximumLimit, out limit),
            }, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        if (partyId == Guid.Empty)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [PartyIdField]);
        }

        CounterpartyContactInformationView? view;
        try
        {
            view = await contacts.GetContactInformationAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (view is null)
        {
            return new AiReadResultV1(AiToolOutcomeV1.NotFound);
        }

        var information = view.Information;
        // Another party's or another company's answer, or a channel kind outside the owner vocabulary: release nothing.
        if (view.PartyId != partyId || view.CompanyId != context.CompanyId || view.AccountId == Guid.Empty
            || string.IsNullOrWhiteSpace(view.Version) || information?.Contacts is null
            || information.Contacts.Any(contact => contact?.Channels is null
                                                   || contact.Channels.Any(channel => channel is null || !ChannelKinds.Contains(channel.Kind))))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var active = information.Contacts.Where(contact => contact.IsActive).OrderBy(contact => contact.Id).ToArray();
        var listed = active.Take(limit).Select(contact => new CounterpartyAiContactV1(
            contact.Id, contact.DisplayName, contact.JobTitle, contact.Purpose,
            [.. contact.Channels.Where(channel => channel.IsActive).OrderBy(channel => channel.Id)
                .Select(channel => new CounterpartyAiChannelV1(channel.Kind, channel.Value, channel.Extension))])).ToArray();

        var released = new List<string>();
        if (information.Phone is not null || listed.Any(contact => contact.Channels.Any(channel => channel.Kind == "phone")))
        {
            released.Add("phone");
        }

        if (information.Email is not null || listed.Any(contact => contact.Channels.Any(channel => channel.Kind == "email")))
        {
            released.Add("email");
        }

        if (listed.Length != 0)
        {
            released.Add("contactName");
        }

        if (listed.Any(contact => contact.JobTitle is not null))
        {
            released.Add("contactJobTitle");
        }

        var effective = CounterpartyAiRelease.EffectiveClass(released);
        var content = new CounterpartyPartyContactsContentV1(
            view.PartyId,
            view.AccountId,
            CounterpartyAiRelease.FieldPolicy,
            effective.ToString(),
            CounterpartyAiRelease.Classes(released),
            information.Phone,
            information.Phone is null ? null : information.PhoneExtension,
            information.Email,
            active.Length,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, view.PartyId, "Cari yetkili kişileri",
            AiReadToolArgumentsV1.SourceVersion([$"{view.PartyId:D}:{view.AccountId:D}:{view.Version}",
                .. active.Select(contact => $"{contact.Id:D}:{contact.Version}")]),
            timeProvider.GetUtcNow().ToUniversalTime(), effective);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>
/// The released fields with their AI-D42 classes: <c>DataClass</c> is the highest class among <c>FieldClasses</c>.
/// <c>ActiveContactCount</c> is the exact number of active persons; <c>ListedCount</c> those listed under the limit.
/// </summary>
public sealed record CounterpartyPartyContactsContentV1(
    Guid PartyId,
    Guid AccountId,
    string FieldPolicy,
    string DataClass,
    IReadOnlyDictionary<string, string> FieldClasses,
    string? Phone,
    string? PhoneExtension,
    string? Email,
    int ActiveContactCount,
    int ListedCount,
    IReadOnlyList<CounterpartyAiContactV1> Contacts);

public sealed record CounterpartyAiContactV1(Guid ContactId, string DisplayName, string? JobTitle, string Purpose,
    IReadOnlyList<CounterpartyAiChannelV1> Channels);

public sealed record CounterpartyAiChannelV1(string Kind, string Value, string? Extension);
