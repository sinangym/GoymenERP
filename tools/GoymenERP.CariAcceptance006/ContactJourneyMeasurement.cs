using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.CariAcceptance006;

// Adapter for the existing browser measurement, not a second run or manifest family.
public static class ContactJourneyMeasurement
{
    public static void Verify(RunSettings identity, DatabaseSnapshot baseline, DatabaseSnapshot current, JsonElement measurement)
    {
        // Exact #235/44ae49c L-03 measurement shape. These are declarations,
        // not SQL-derived expectations; the durable join below proves the effects.
        var expected = measurement.GetProperty("expected");
        var outcomes = expected.GetProperty("durableOutcomes");
        var versions = measurement.GetProperty("versions");
        Require(measurement.GetProperty("runId").GetString() == identity.RunId
            && measurement.GetProperty("instanceId").GetString() == identity.InstanceId
            && measurement.GetProperty("slug").GetString() == "l03-contact-edit-journey"
            && measurement.GetProperty("companyA").GetString() == identity.CompanyAId
            && measurement.GetProperty("companyB").GetString() == identity.CompanyBId
            && expected.GetProperty("puts").GetInt32() == 8
            && expected.GetProperty("successfulLogicalUpdates").GetInt32() == 6
            && expected.GetProperty("auditEvents").GetInt32() == 6
            && expected.GetProperty("durableUniqueKeys").GetInt32() == 7
            && outcomes.GetProperty("succeeded204").GetInt32() == 6
            && outcomes.GetProperty("versionConflict409").GetInt32() == 1
            && expected.GetProperty("newParties").GetInt32() == 0
            && expected.GetProperty("newAccounts").GetInt32() == 0
            && expected.GetProperty("companyBWrites").GetInt32() == 0, "measurement identity or declared journey differs");
        var partyId = measurement.GetProperty("partyId").GetGuid();
        var companyId = Guid.Parse(identity.CompanyAId);
        var original = baseline.Rows.Single(row => row.PartyId == partyId && row.CompanyId == companyId);
        Require(original.Version == "1" && versions.GetProperty("start").GetString() == original.Version
            && versions.GetProperty("afterU1").GetString() == "2"
            && versions.GetProperty("afterU2").GetString() == "3"
            && versions.GetProperty("afterU3").GetString() == "4"
            && versions.GetProperty("afterU4").GetString() == "5"
            && versions.GetProperty("afterU5").GetString() == "6"
            && versions.GetProperty("afterU6").GetString() == "7", "measurement version progression differs from the published journey");
        var primary = ReadAttempts(identity, measurement.GetProperty("contactPuts"), primary: true);
        var secondary = ReadAttempts(identity, measurement.GetProperty("staleContactPuts"), primary: false);
        DatabaseEvidence.VerifyContactJourney(identity, baseline, current, partyId, companyId, primary, secondary);
    }

    public static ContactJourneyAttempt[] ReadAttempts(RunSettings identity, JsonElement items, bool primary)
    {
        Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() == (primary ? 6 : 2), "wrong per-page invocation inventory");
        var result = new List<ContactJourneyAttempt>();
        long previous = -1;
        foreach (var item in items.EnumerateArray())
        {
            var index = result.Count;
            var seq = item.GetProperty("seq").GetInt64();
            Require(seq > previous && item.GetProperty("companyId").GetString() == identity.CompanyAId
                && item.GetProperty("contractDeclaration").GetString() == SourceBinding.ClientContractVersion,
                "request order, company or declaration differs");
            previous = seq; // Each page owns its sequence space.
            var key = item.GetProperty("idempotencyKey").GetString();
            Require(!string.IsNullOrWhiteSpace(key), "actual request key missing");
            // The first response's delivery is intentionally withheld. SQL proves its
            // durable success; the separate browser verdict proves delivery behavior.
            if (!(primary && index == 0))
            {
                var conflict = !primary && index == 0;
                Require(item.GetProperty("status").GetInt32() == (conflict ? 409 : 204), "unexpected actual HTTP result");
                if (!conflict)
                    Require(item.GetProperty("contractAck").GetString() == SourceBinding.ClientContractVersion,
                        "successful response acknowledgment missing");
            }
            var replay = item.GetProperty("replayed");
            Require(primary && index == 1 ? replay.GetString() == "true" : replay.ValueKind == JsonValueKind.Null,
                "unexpected replay metadata");
            var command = item.GetProperty("requestBody").Deserialize<UpdateCounterpartyContactInformation>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Require(command is not null, "actual typed request missing");
            result.Add(new(key!, command!));
        }
        return result.ToArray();
    }

    public static void ValidatePublication(RunSettings identity, SourceSnapshot source, JsonElement environment,
        JsonElement ready, string environmentHash, string baselineHash, string contractHash)
    {
        identity.Validate("Testing");
        foreach (var document in new[] { environment, ready })
            Require(document.GetProperty("runId").GetString() == identity.RunId
                && document.GetProperty("instanceId").GetString() == identity.InstanceId
                && document.GetProperty("readyForBrowser").GetBoolean(), "publication belongs to another run or is not ready");
        Require(ready.GetProperty("environmentSha256").GetString() == environmentHash
            && environment.GetProperty("baselineSha256").GetString() == baselineHash
            && environment.GetProperty("contractSha256").GetString() == contractHash,
            "publication, external contract or SQL baseline bytes changed");
        var publishedSource = environment.GetProperty("sources").Deserialize<SourceSnapshot>()
            ?? throw new InvalidOperationException("C006 L-03 published source missing.");
        SourceBinding.Validate(source, publishedSource);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("C006 L-03 " + message + ".");
    }
}
