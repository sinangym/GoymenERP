using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.CariAcceptance006;

public sealed record ContactJourneyAttempt(string IdempotencyKey, UpdateCounterpartyContactInformation Command);

public static partial class DatabaseEvidence
{
    // identity-seed.json maps the Testing header alias group-bootstrap-admin to
    // this internal user ID; actual smoke /identity/me confirms that mapping.
    // Durable commands persist the resolved ID, never the external alias.
    private const string ContactJourneyActorId = "20000000-0000-0000-0000-000000000003";

    // L-03: six edits, one same-key replay and one durable stale-version refusal.
    // Inputs are the browser's actual requests, not values reverse-engineered from SQL.
    // Client-minted UUIDs vary; every other expected value is fixed by the L-03
    // source at frontend #235/44ae49cd6c54984fb7a52c9c6e4ee5fd67284d6e.
    public static void VerifyContactJourney(RunSettings identity, DatabaseSnapshot baseline,
        DatabaseSnapshot current, Guid partyId, Guid companyId,
        IReadOnlyList<ContactJourneyAttempt> primary, IReadOnlyList<ContactJourneyAttempt> secondary)
    {
        identity.Validate("Testing");
        RequireContact(primary is { Count: 6 } && secondary is { Count: 2 }, "exact two-page eight-PUT journey required");
        var first = primary[0];
        var replay = primary[1];
        var clear = primary[2];
        var stale = secondary[0];
        var recovered = secondary[1];
        RequireContact(companyId == Guid.Parse(identity.CompanyAId)
            && partyId != Guid.Empty && baseline.DatabaseName == identity.DatabaseName
            && current.DatabaseName == identity.DatabaseName && baseline.Rcsi && current.Rcsi
            && baseline.PartyStateSha256.Length == 64 && baseline.PartyStateSha256 == current.PartyStateSha256
            && baseline.ContactExecutionRows is { Count: 0 }
            && baseline.Effects.ContactAudits == 0 && baseline.Effects.ContactExecutions == 0,
            "fresh baseline identity or contact history differs");
        var before = baseline.Rows.SingleOrDefault(row => row.PartyId == partyId && row.CompanyId == companyId);
        var after = current.Rows.SingleOrDefault(row => row.PartyId == partyId && row.CompanyId == companyId);
        RequireContact(before is not null && after is not null && before.IsActive
            && before.ContactInformationJson is null, "target is not a fresh active smoke account");
        var version = CounterpartyContactInformationEditor.ParseVersion(before!.Version);
        var next = checked(version + 1).ToString(CultureInfo.InvariantCulture);
        var finalVersion = checked(version + 6).ToString(CultureInfo.InvariantCulture);
        var requests = new[] { first, clear, primary[3], primary[4], primary[5], stale, recovered };
        RequireContact(requests.All(attempt => !string.IsNullOrWhiteSpace(attempt.IdempotencyKey))
            && requests.Select(attempt => IdempotencyHash.ComputeKey(attempt.IdempotencyKey)).Distinct().Count() == 7
            && first.IdempotencyKey == replay.IdempotencyKey
            && Payload(partyId, first) == Payload(partyId, replay)
            && first.Command.ExpectedVersion == before.Version && clear.Command.ExpectedVersion == next
            && stale.Command.ExpectedVersion == before.Version
            && primary[3].Command.ExpectedVersion == checked(version + 2).ToString(CultureInfo.InvariantCulture)
            && primary[4].Command.ExpectedVersion == checked(version + 3).ToString(CultureInfo.InvariantCulture)
            && primary[5].Command.ExpectedVersion == checked(version + 4).ToString(CultureInfo.InvariantCulture)
            && recovered.Command.ExpectedVersion == checked(version + 5).ToString(CultureInfo.InvariantCulture),
            "request keys, replay or version fences differ");

        var initial = CounterpartyContactInformationEditor.Empty();
        var edited = CounterpartyContactInformationEditor.Apply(initial, first.Command.Changes);
        RequireFirstContactState(identity, edited);
        var clearChanges = clear.Command.Changes;
        RequireContact(clearChanges.Phone.ValueKind == JsonValueKind.Null
            && clearChanges.PhoneExtension.ValueKind == JsonValueKind.Null
            && clearChanges.Email.ValueKind == JsonValueKind.Undefined
            && clearChanges.Website.ValueKind == JsonValueKind.Undefined
            && clearChanges.Addresses is { Count: 0 } && clearChanges.Contacts is { Count: 0 },
            "second edit must only clear phone and extension together");
        var staleChanges = stale.Command.Changes;
        RequireContact(staleChanges.Email.ValueKind == JsonValueKind.String
            && staleChanges.Email.GetString() == identity.BrowserPrefix.ToLowerInvariant() + "-profil2@example.test"
            && staleChanges.Phone.ValueKind == JsonValueKind.Undefined
            && staleChanges.PhoneExtension.ValueKind == JsonValueKind.Undefined
            && staleChanges.Website.ValueKind == JsonValueKind.Undefined
            && staleChanges.Addresses is { Count: 0 } && staleChanges.Contacts is { Count: 0 },
            "stale attempt does not match the separate editor journey");
        var state = CounterpartyContactInformationEditor.Apply(edited, clearChanges);
        var addressId = state.Addresses.Single(address => address.Purpose == "delivery").Id;
        foreach (var index in new[] { 3, 4, 5 })
        {
            var expectedChanges = new CounterpartyContactInformationChanges
            {
                Addresses = [new()
                {
                    Id = addressId, Operation = index == 3 ? "deactivate" : index == 4 ? "upsert" : "reactivate",
                    Line1 = index == 4 ? JsonSerializer.SerializeToElement(identity.BrowserPrefix + " Teslim Yolu 1A") : default,
                }],
            };
            RequireContact(JsonSerializer.Serialize(primary[index].Command.Changes) == JsonSerializer.Serialize(expectedChanges),
                "child transitions must be explicit, ordered and preserve all other fields");
            state = CounterpartyContactInformationEditor.Apply(state, primary[index].Command.Changes);
            var address = state.Addresses.Single(value => value.Id == addressId);
            RequireContact(address.IsActive == (index == 5) && address.Version == (index - 1).ToString(CultureInfo.InvariantCulture),
                "inactive upsert must not reactivate the address");
        }
        RequireContact(JsonSerializer.Serialize(recovered.Command.Changes) == JsonSerializer.Serialize(staleChanges),
            "reviewed recovery must retain the requested email under a new key and current version");
        state = CounterpartyContactInformationEditor.Apply(state, recovered.Command.Changes);
        var expectedJson = CounterpartyContactInformationEditor.Serialize(state);
        RequireContact(current.Effects == baseline.Effects with { ContactAudits = 6, ContactExecutions = 7 }
            && current.Rows.Count == baseline.Rows.Count
            && after == before with { Version = finalVersion, ContactInformationJson = expectedJson }
            && baseline.Rows.Where(row => row != before).All(current.Rows.Contains),
            "six edits must preserve Party, other accounts and unrelated effects");

        // A stale-version refusal is durable. Its receipt is not a seventh business edit.
        var receipts = current.ContactExecutionRows;
        RequireContact(receipts is { Count: 7 } && receipts.All(row => row.Id != Guid.Empty)
            && receipts.Select(row => row.Id).Distinct().Count() == 7,
            "seven distinct durable results are required, not eight requests or six successes");
        foreach (var attempt in requests)
        {
            var key = IdempotencyHash.ComputeKey(attempt.IdempotencyKey);
            var matching = receipts!.Where(row => row.IdempotencyKeyHash == key).ToArray();
            var conflict = ReferenceEquals(attempt, stale);
            RequireContact(matching.Length == 1 && matching[0].CompanyId == companyId
                && matching[0].ActorId == ContactJourneyActorId && matching[0].RequestHash == Payload(partyId, attempt)
                && matching[0].Outcome == (conflict ? "VersionConflict" : "Succeeded")
                && matching[0].HttpStatusCode == (conflict ? 409 : 204) && matching[0].SchemaVersion == 1,
                "durable key, payload, company, actor or result differs");
            ValidateContactResponse(matching[0], conflict, version, checked(version + 5));
        }
    }

    private static string Payload(Guid partyId, ContactJourneyAttempt attempt) =>
        IdempotencyHash.ComputePayload(new { PartyId = partyId, Command = attempt.Command });

    private static void RequireFirstContactState(RunSettings identity, CounterpartyContactInformation information)
    {
        var delivery = information.Addresses.SingleOrDefault(address => address.Purpose == "delivery");
        var operational = information.Addresses.SingleOrDefault(address => address.Purpose == "operational");
        RequireContact(information.Phone == "+90 212 555 00 01" && information.PhoneExtension == "123"
            && information.Email == identity.BrowserPrefix.ToLowerInvariant() + "-profil1@example.test"
            && information.Website is null && information.Addresses.Count == 2
            && delivery is not null && operational is not null
            && delivery.CountryCode == "TR" && delivery.Line1 == identity.BrowserPrefix + " Teslim Yolu 1"
            && delivery.City == "Kocaeli" && operational.CountryCode == "DE"
            && operational.Line1 == identity.BrowserPrefix + " Operational 2" && operational.City is null
            && information.Addresses.All(address => address.Id != Guid.Empty && address.Version == "1"
                && address.IsActive && address.Line2 is null && address.District is null && address.PostalCode is null)
            && information.Contacts.Count == 2, "first edit does not match the pinned L-03 values");
        var contact = information.Contacts.Single(value => value.Purpose == "Operasyon");
        RequireContact(contact.Id != Guid.Empty && contact.Version == "1" && contact.IsActive
            && contact.DisplayName == identity.BrowserPrefix + " Operasyon Muhatab\u0131"
            && contact.JobTitle is null && contact.Purpose == "Operasyon" && contact.Channels.Count == 1,
            "contact does not match the pinned L-03 values");
        var channel = contact.Channels[0];
        RequireContact(channel.Id != Guid.Empty && channel.Version == "1" && channel.IsActive
            && channel.Kind == "phone" && channel.Value == "0532 000 00 01" && channel.Extension is null,
            "channel does not match the pinned L-03 values");
        var finance = information.Contacts.Single(value => value.Purpose == "Muhasebe");
        RequireContact(finance.Id != Guid.Empty && finance.Version == "1" && finance.IsActive
            && finance.DisplayName == identity.BrowserPrefix + " Muhasebe Muhatab\u0131" && finance.JobTitle is null
            && finance.Channels.Count == 1, "finance contact does not match the pinned L-03 values");
        var email = finance.Channels[0];
        RequireContact(email.Id != Guid.Empty && email.Version == "1" && email.IsActive && email.Kind == "email"
            && email.Value == identity.BrowserPrefix.ToLowerInvariant() + "-muhasebe@example.test" && email.Extension is null,
            "finance channel does not match the pinned L-03 values");
    }

    private static void ValidateContactResponse(ContactExecutionRow receipt, bool conflict, long expected, long current)
    {
        RequireContact(!string.IsNullOrWhiteSpace(receipt.ResponseJson), "durable response body missing");
        try
        {
            using var document = JsonDocument.Parse(receipt.ResponseJson);
            var root = document.RootElement;
            string[] properties = ["Result", "StatusCode", "Title", "Detail", "Resource", "Conflict"];
            RequireContact(root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == properties.Length
                && root.EnumerateObject().Select(value => value.Name).ToHashSet().SetEquals(properties),
                "durable response shape differs from the runtime replay contract");
            var response = JsonSerializer.Deserialize<CounterpartyCommandResponse>(receipt.ResponseJson);
            RequireContact(response is not null
                && response.Result == (conflict ? CounterpartyCommandResult.VersionConflict : CounterpartyCommandResult.Succeeded)
                && response.StatusCode == receipt.HttpStatusCode && response.Resource is null,
                "durable body result or status contradicts metadata");
            if (conflict)
            {
                var versionConflict = root.GetProperty("Conflict");
                RequireContact(versionConflict.ValueKind == JsonValueKind.Object && versionConflict.EnumerateObject().Count() == 2
                    && versionConflict.EnumerateObject().Select(value => value.Name).ToHashSet()
                        .SetEquals(["ExpectedVersion", "CurrentVersion"])
                    && response!.Conflict == new CounterpartyVersionConflict(expected, current)
                    && !string.IsNullOrWhiteSpace(response.Title) && !string.IsNullOrWhiteSpace(response.Detail),
                    "durable conflict body does not preserve the actual version fence");
            }
            else
                RequireContact(response!.Conflict is null && response.Title is null && response.Detail is null,
                    "durable success must not contain a problem or conflict");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("C006 L-03 durable response is not replayable JSON.");
        }
    }

    private static void RequireContact(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("C006 L-03 " + reason + ".");
    }
}
