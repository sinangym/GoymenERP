using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.CariAcceptance006;

public sealed record ContactJourneyAttempt(string IdempotencyKey, UpdateCounterpartyContactInformation Command);

public static partial class DatabaseEvidence
{
    // L-03 has a different effect from L-01's create/link and from a single edit.
    // Inputs are the browser's actual requests, not values reverse-engineered from SQL.
    // Client-minted UUIDs vary; every other expected value is fixed by the L-03 source.
    public static void VerifyContactJourney(RunSettings identity, DatabaseSnapshot baseline,
        DatabaseSnapshot current, Guid partyId, Guid companyId, ContactJourneyAttempt first,
        ContactJourneyAttempt replay, ContactJourneyAttempt clear, ContactJourneyAttempt stale)
    {
        identity.Validate("Testing");
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
        var finalVersion = checked(version + 2).ToString(CultureInfo.InvariantCulture);
        var requests = new[] { first, clear, stale };
        RequireContact(requests.All(attempt => !string.IsNullOrWhiteSpace(attempt.IdempotencyKey))
            && requests.Select(attempt => IdempotencyHash.ComputeKey(attempt.IdempotencyKey)).Distinct().Count() == 3
            && first.IdempotencyKey == replay.IdempotencyKey
            && Payload(partyId, first) == Payload(partyId, replay)
            && first.Command.ExpectedVersion == before.Version && clear.Command.ExpectedVersion == next
            && stale.Command.ExpectedVersion == before.Version, "request keys, replay or version fences differ");

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
            && staleChanges.Email.GetString() == identity.BrowserPrefix.ToLowerInvariant() + "-stale@example.test"
            && staleChanges.Phone.ValueKind == JsonValueKind.Undefined
            && staleChanges.PhoneExtension.ValueKind == JsonValueKind.Undefined
            && staleChanges.Website.ValueKind == JsonValueKind.Undefined
            && staleChanges.Addresses is { Count: 0 } && staleChanges.Contacts is { Count: 0 },
            "stale attempt does not match the separate editor journey");
        var expectedJson = CounterpartyContactInformationEditor.Serialize(
            CounterpartyContactInformationEditor.Apply(edited, clearChanges));
        RequireContact(current.Effects == baseline.Effects with { ContactAudits = 2, ContactExecutions = 3 }
            && current.Rows.Count == baseline.Rows.Count
            && after == before with { Version = finalVersion, ContactInformationJson = expectedJson }
            && baseline.Rows.Where(row => row != before).All(current.Rows.Contains),
            "two edits must preserve Party, other accounts and unrelated effects");

        // A stale-version refusal is durable. Its receipt is not a third business edit.
        var receipts = current.ContactExecutionRows;
        RequireContact(receipts is { Count: 3 } && receipts.All(row => row.Id != Guid.Empty)
            && receipts.Select(row => row.Id).Distinct().Count() == 3,
            "three distinct durable results are required, not four requests or two successes");
        foreach (var attempt in requests)
        {
            var key = IdempotencyHash.ComputeKey(attempt.IdempotencyKey);
            var matching = receipts!.Where(row => row.IdempotencyKeyHash == key).ToArray();
            var conflict = ReferenceEquals(attempt, stale);
            RequireContact(matching.Length == 1 && matching[0].CompanyId == companyId
                && matching[0].ActorId == identity.Actor && matching[0].RequestHash == Payload(partyId, attempt)
                && matching[0].Outcome == (conflict ? "VersionConflict" : "Succeeded")
                && matching[0].HttpStatusCode == (conflict ? 409 : 204) && matching[0].SchemaVersion == 1,
                "durable key, payload, company, actor or result differs");
        }
    }

    private static string Payload(Guid partyId, ContactJourneyAttempt attempt) =>
        IdempotencyHash.ComputePayload(new { PartyId = partyId, Command = attempt.Command });

    private static void RequireFirstContactState(RunSettings identity, CounterpartyContactInformation information)
    {
        var delivery = information.Addresses.SingleOrDefault(address => address.Purpose == "delivery");
        var operational = information.Addresses.SingleOrDefault(address => address.Purpose == "operational");
        RequireContact(information.Phone == "+90 212 555 00 01" && information.PhoneExtension == "123"
            && information.Email is null && information.Website is null && information.Addresses.Count == 2
            && delivery is not null && operational is not null
            && delivery.CountryCode == "TR" && delivery.Line1 == identity.BrowserPrefix + " Teslim Yolu 1"
            && delivery.City == "Kocaeli" && operational.CountryCode == "DE"
            && operational.Line1 == identity.BrowserPrefix + " Operational 2" && operational.City is null
            && information.Addresses.All(address => address.Id != Guid.Empty && address.Version == "1"
                && address.IsActive && address.Line2 is null && address.District is null && address.PostalCode is null)
            && information.Contacts.Count == 1, "first edit does not match the pinned L-03 values");
        var contact = information.Contacts[0];
        RequireContact(contact.Id != Guid.Empty && contact.Version == "1" && contact.IsActive
            && contact.DisplayName == identity.BrowserPrefix + " Operasyon Muhatab\u0131"
            && contact.JobTitle is null && contact.Purpose == "Operasyon" && contact.Channels.Count == 1,
            "contact does not match the pinned L-03 values");
        var channel = contact.Channels[0];
        RequireContact(channel.Id != Guid.Empty && channel.Version == "1" && channel.IsActive
            && channel.Kind == "phone" && channel.Value == "0532 000 00 01" && channel.Extension is null,
            "channel does not match the pinned L-03 values");
    }

    private static void RequireContact(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("C006 L-03 " + reason + ".");
    }
}
