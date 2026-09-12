using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.CariAcceptance006;

// Adapter for the existing browser measurement, not a second run or manifest family.
public static class ContactJourneyMeasurement
{
    public static void Verify(RunSettings identity, DatabaseSnapshot baseline, DatabaseSnapshot current, JsonElement measurement)
    {
        Require(measurement.GetProperty("runId").GetString() == identity.RunId
            && measurement.GetProperty("instanceId").GetString() == identity.InstanceId
            && measurement.GetProperty("slug").GetString() == "l03-contact-edit-journey"
            && measurement.GetProperty("companyA").GetString() == identity.CompanyAId
            && measurement.GetProperty("companyB").GetString() == identity.CompanyBId
            && measurement.GetProperty("logicalUpdates").GetInt32() == 6
            && measurement.GetProperty("expectedNewParties").GetInt32() == 0, "measurement identity or declared journey differs");
        var partyId = measurement.GetProperty("partyId").GetGuid();
        var companyId = Guid.Parse(identity.CompanyAId);
        var original = baseline.Rows.Single(row => row.PartyId == partyId && row.CompanyId == companyId);
        Require(measurement.GetProperty("seededVersion").GetString() == original.Version, "measurement did not start from the published account version");
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
