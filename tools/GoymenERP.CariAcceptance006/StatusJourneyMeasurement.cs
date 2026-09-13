using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.CariAcceptance006;

// Adapts #250/b9f094c's existing browser projection to the owned SQL verifier.
public static class StatusJourneyMeasurement
{
    public static void Verify(RunSettings identity, StatusJourneySnapshot baseline,
        StatusJourneySnapshot current, JsonElement measurement)
    {
        Require(measurement.GetProperty("runId").GetString() == identity.RunId
            && measurement.GetProperty("instanceId").GetString() == identity.InstanceId
            && measurement.GetProperty("slug").GetString() == "l04-account-status-journey"
            && measurement.GetProperty("companyA").GetString() == identity.CompanyAId
            && measurement.GetProperty("companyB").GetString() == identity.CompanyBId
            && measurement.GetProperty("readOnlyActor").GetString() == AcceptanceProfile.ReadOnlyActor,
            "measurement identity differs");
        var expected = measurement.GetProperty("expected");
        foreach (var (name, count) in new (string, int)[]
        {
            ("puts", 4), ("successfulLogicalUpdates", 2), ("auditEvents", 2), ("noChangeAudits", 0),
            ("durableUniqueKeys", 3), ("replays", 1), ("reDeactivations", 0), ("newParties", 0),
            ("newAccounts", 0), ("companyBWrites", 0), ("readOnlyWrites", 0),
        }) Require(expected.GetProperty(name).GetInt32() == count, "declared " + name + " differs");
        var outcomes = expected.GetProperty("durableOutcomes");
        Require(outcomes.GetProperty("succeeded204").GetInt32() == 2
            && outcomes.GetProperty("versionConflict409").GetInt32() == 1, "durable inventory differs");
        var versions = measurement.GetProperty("versions");
        foreach (var (name, value) in new[] { ("start", "7"), ("afterK1", "8"), ("afterK2", "9"), ("afterK1Replay", "9") })
            Require(versions.GetProperty(name).GetString() == value, "declared version differs");
        var counts = measurement.GetProperty("finalCounts");
        foreach (var (name, value) in new[] { ("main", 2), ("stale", 2), ("total", 4), ("mainInCompanyB", 0), ("readOnly", 0) })
            Require(counts.GetProperty(name).GetInt32() == value, "observed final invocation inventory differs");
        foreach (var name in new[] { "main", "stale", "readOnly" })
        {
            var barrier = measurement.GetProperty("barriers").GetProperty(name);
            Require(barrier.GetProperty("accepted").GetBoolean()
                && barrier.GetProperty("outstanding").GetArrayLength() == 0, "all contexts must be quiet");
        }
        var primary = ReadAttempts(identity, measurement.GetProperty("accountStatusPuts"), true);
        var secondary = ReadAttempts(identity, measurement.GetProperty("staleAccountStatusPuts"), false);
        var party = measurement.GetProperty("partyId").GetGuid();
        var withheld = measurement.GetProperty("withheld");
        var original = OneWithheld(withheld.GetProperty("main"), identity, "PUT", 204);
        Require(original.GetProperty("idempotencyKey").GetString() == primary[0].IdempotencyKey
            && original.GetProperty("path").GetString() == $"/api/v1/counterparties/{party}/company-account/active-state",
            "lost original response belongs to another command");
        var lostRead = OneWithheld(withheld.GetProperty("staleRead"), identity, "GET", 200);
        Require(lostRead.GetProperty("partyId").GetGuid() == party
            && lostRead.GetProperty("path").GetString() == $"/api/v1/counterparties/{party}"
            && lostRead.GetProperty("idempotencyKey").ValueKind == JsonValueKind.Null, "lost GET belongs to another read");
        ActiveV9(lostRead.GetProperty("account"));
        var order = measurement.GetProperty("k1ReplayOrdering");
        var pre = order.GetProperty("preRead");
        var put = order.GetProperty("put");
        var post = order.GetProperty("postRead");
        Require(pre.GetProperty("seq").GetInt64() != post.GetProperty("seq").GetInt64()
            && put.GetProperty("status").GetInt32() == 204
            && pre.GetProperty("transferTick").GetDouble() < put.GetProperty("requestTick").GetDouble()
            && put.GetProperty("transferTick").GetDouble() < post.GetProperty("requestTick").GetDouble(),
            "fresh reads must bracket the replay in network order");
        ActiveV9(pre.GetProperty("account"));
        ActiveV9(post.GetProperty("account"));
        DatabaseEvidence.VerifyStatusJourney(identity, baseline, current, party, primary, secondary);
    }

    public static StatusJourneyAttempt[] ReadAttempts(RunSettings identity, JsonElement items, bool primary)
    {
        Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() == 2, "two requests per page required");
        var result = new List<StatusJourneyAttempt>();
        long previous = -1;
        foreach (var item in items.EnumerateArray())
        {
            var firstLost = primary && result.Count == 0;
            var conflict = !primary && result.Count == 0;
            var seq = item.GetProperty("seq").GetInt64();
            Require(seq > previous && item.GetProperty("companyId").GetString() == identity.CompanyAId
                && item.GetProperty("contractDeclaration").GetString() == SourceBinding.ClientContractVersion,
                "actual request company/declaration/order differs");
            previous = seq;
            Require(firstLost ? item.GetProperty("status").ValueKind == JsonValueKind.Null
                : item.GetProperty("status").GetInt32() == (conflict ? 409 : 204), "actual HTTP status differs");
            if (!firstLost && !conflict)
                Require(item.GetProperty("contractAck").GetString() == SourceBinding.ClientContractVersion, "successful ack missing");
            Require(primary && result.Count == 1 ? item.GetProperty("replayed").GetString() == "true"
                : item.GetProperty("replayed").ValueKind == JsonValueKind.Null, "replay header differs");
            if (conflict) Require(item.GetProperty("problemCode").GetString() == "version_conflict", "stale problem code differs");
            var key = item.GetProperty("idempotencyKey").GetString();
            Require(!string.IsNullOrWhiteSpace(key), "actual idempotency key missing");
            var body = item.GetProperty("requestBody");
            Require(body.ValueKind == JsonValueKind.Object && body.EnumerateObject().Count() == 3,
                "actual body must contain exactly state, fence and reason");
            var command = body.Deserialize<SetCompanyPartyAccountActiveState>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Require(command is not null && !string.IsNullOrWhiteSpace(command.Reason), "actual typed command missing");
            result.Add(new(key!, command!));
        }
        return result.ToArray();
    }

    private static JsonElement OneWithheld(JsonElement items, RunSettings identity, string method, int status)
    {
        Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() == 1, "exactly one withheld delivery required");
        var value = items[0];
        Require(value.GetProperty("marker").GetString() == "upstream_success_then_delivery_abort"
            && value.GetProperty("method").GetString() == method && value.GetProperty("upstreamStatus").GetInt32() == status
            && value.GetProperty("companyId").GetString() == identity.CompanyAId
            && value.GetProperty("contractAck").GetString() == SourceBinding.ClientContractVersion
            && !value.GetProperty("deliveredToBrowser").GetBoolean(), "original upstream success not demonstrated");
        return value;
    }

    private static void ActiveV9(JsonElement account) => Require(account.GetProperty("version").GetString() == "9"
        && account.GetProperty("isActive").GetBoolean(), "fresh account must remain active v9");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("C006 L-04 " + message + ".");
    }
}
