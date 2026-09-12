using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public interface ICounterpartyContactInformationService
{
    Task<CounterpartyContactInformationView?> GetContactInformationAsync(
        Guid partyId, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> UpdateContactInformationAsync(
        Guid partyId, UpdateCounterpartyContactInformation command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCounterpartyContactInformation(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] CounterpartyContactInformationChanges Changes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CounterpartyContactInformationChanges
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Phone { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement PhoneExtension { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Email { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Website { get; init; }

    public IReadOnlyList<CounterpartyAddressChange> Addresses { get; init; } = [];

    public IReadOnlyList<CounterpartyContactChange> Contacts { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CounterpartyAddressChange
{
    [JsonRequired]
    public Guid Id { get; init; }

    [JsonRequired]
    public string Operation { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Purpose { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement CountryCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Line1 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Line2 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement City { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement District { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement PostalCode { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CounterpartyContactChange
{
    [JsonRequired]
    public Guid Id { get; init; }

    [JsonRequired]
    public string Operation { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement DisplayName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement JobTitle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Purpose { get; init; }

    public IReadOnlyList<CounterpartyContactChannelChange> Channels { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CounterpartyContactChannelChange
{
    [JsonRequired]
    public Guid Id { get; init; }

    [JsonRequired]
    public string Operation { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Kind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Value { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Extension { get; init; }
}

public sealed record CounterpartyContactInformationView(
    Guid PartyId, Guid AccountId, Guid CompanyId, string Version, bool IsActive,
    CounterpartyContactInformation Information);

public sealed record CounterpartyContactInformation(
    string? Phone, string? PhoneExtension, string? Email, string? Website,
    IReadOnlyList<CounterpartyOperationalAddress> Addresses,
    IReadOnlyList<CounterpartyOperationalContact> Contacts);

public sealed record CounterpartyOperationalAddress(
    Guid Id, string Version, bool IsActive, string Purpose, string CountryCode,
    string Line1, string? Line2, string? City, string? District, string? PostalCode);

public sealed record CounterpartyOperationalContact(
    Guid Id, string Version, bool IsActive, string DisplayName, string? JobTitle,
    string Purpose, IReadOnlyList<CounterpartyContactChannel> Channels);

public sealed record CounterpartyContactChannel(
    Guid Id, string Version, bool IsActive, string Kind, string Value, string? Extension);
