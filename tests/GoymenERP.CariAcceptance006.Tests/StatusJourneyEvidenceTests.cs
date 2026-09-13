using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.CariAcceptance006;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class StatusJourneyEvidenceTests
{
    [Fact]
    public void HistoricalDeactivateReplayPreservesActiveV9AndExactlyTwoAuditsThreeTerminals()
    {
        var value = Fixture();
        Verify(value);
    }

    [Theory]
    [InlineData("deactivatedAgain")]
    [InlineData("extraVersion")]
    [InlineData("otherCompany")]
    [InlineData("contactChanged")]
    [InlineData("rolesChanged")]
    [InlineData("partyChanged")]
    [InlineData("newAccount")]
    [InlineData("newParty")]
    [InlineData("noChange")]
    [InlineData("extraAudit")]
    [InlineData("missingConflict")]
    [InlineData("extraReplayTerminal")]
    [InlineData("readerActor")]
    [InlineData("wrongCompany")]
    [InlineData("wrongPayload")]
    [InlineData("wrongResponseFence")]
    [InlineData("wrongAuditReason")]
    [InlineData("wrongAuditFence")]
    [InlineData("staleAudit")]
    [InlineData("wrongAuditAccount")]
    [InlineData("consistentlyWrongAuditAccount")]
    [InlineData("oldBaseline")]
    [InlineData("foreignDatabase")]
    [InlineData("rcsi")]
    public void IncorrectDurableEvidenceFailsClosed(string mutation)
    {
        var value = Fixture();
        var after = value.After;
        var database = after.Database;
        switch (mutation)
        {
            case "deactivatedAgain": database = database with { Rows = [database.Rows[0] with { IsActive = false }, database.Rows[1]] }; break;
            case "extraVersion": database = database with { Rows = [database.Rows[0] with { Version = "10" }, database.Rows[1]] }; break;
            case "otherCompany": database = database with { Rows = [database.Rows[0], database.Rows[1] with { Version = "2" }] }; break;
            case "contactChanged": database = database with { Rows = [database.Rows[0] with { ContactInformationJson = "{}" }, database.Rows[1]] }; break;
            case "rolesChanged": database = database with { Rows = [database.Rows[0] with { Roles = 1 }, database.Rows[1]] }; break;
            case "partyChanged": database = database with { PartyStateSha256 = new string('b', 64) }; break;
            case "newAccount": database = database with { Effects = database.Effects with { Accounts = 3 } }; break;
            case "newParty": database = database with { Effects = database.Effects with { Parties = 2 } }; break;
            case "noChange": after = after with { Audits = [after.Audits[0] with { Operation = "NoChange" }, after.Audits[1]] }; break;
            case "extraAudit": after = after with { Audits = [.. after.Audits, after.Audits[0] with { Id = Guid.NewGuid() }] }; break;
            case "missingConflict": after = after with { Executions = [after.Executions[0], after.Executions[2]] }; break;
            case "extraReplayTerminal": after = after with { Executions = [.. after.Executions, after.Executions[0] with { Id = Guid.NewGuid() }] }; break;
            case "readerActor": after = ChangeReceipt(after, after.Executions[0] with { ActorId = AcceptanceProfile.ReadOnlyActor }); break;
            case "wrongCompany": after = ChangeReceipt(after, after.Executions[0] with { CompanyId = Guid.Parse(value.Identity.CompanyBId) }); break;
            case "wrongPayload": after = ChangeReceipt(after, after.Executions[0] with { RequestHash = new string('A', 64) }); break;
            case "wrongResponseFence": after = after with { Executions = [after.Executions[0], after.Executions[1] with
                { ResponseJson = JsonSerializer.Serialize(new CounterpartyCommandResponse(CounterpartyCommandResult.VersionConflict, 409, "Conflict", "Read", null, new(7, 9))) }, after.Executions[2]] }; break;
            case "wrongAuditReason": after = after with { Audits = [after.Audits[0] with { Reason = "another reason" }, after.Audits[1]] }; break;
            case "wrongAuditFence": after = after with { Audits = [after.Audits[0] with { VersionAfter = 9 }, after.Audits[1]] }; break;
            case "staleAudit": after = after with { Audits = [after.Audits[0] with { IdempotencyKeyHash = after.Executions[1].IdempotencyKeyHash }, after.Audits[1]] }; break;
            case "wrongAuditAccount": after = after with { Audits = [after.Audits[0] with { EntityId = Guid.NewGuid().ToString("D") }, after.Audits[1]] }; break;
            case "consistentlyWrongAuditAccount":
                var wrongId = Guid.NewGuid();
                after = after with { Audits = after.Audits.Select(audit =>
                {
                    var oldState = JsonNode.Parse(audit.BeforeJson)!;
                    var newState = JsonNode.Parse(audit.AfterJson)!;
                    oldState["Id"] = wrongId;
                    newState["Id"] = wrongId;
                    return audit with { EntityId = wrongId.ToString("D"), BeforeJson = oldState.ToJsonString(), AfterJson = newState.ToJsonString() };
                }).ToArray() };
                break;
            case "oldBaseline": value = value with { Before = value.Before with { Executions = [after.Executions[0]] } }; break;
            case "foreignDatabase": database = database with { DatabaseName = "other" }; break;
            case "rcsi": database = database with { Rcsi = false }; break;
            default: throw new ArgumentException(nameof(mutation));
        }
        Assert.Throws<InvalidOperationException>(() => Verify(value with { After = after with { Database = database } }));
    }

    [Theory]
    [InlineData("newReplayKey")]
    [InlineData("changedReplayIntent")]
    [InlineData("sameStaleKey")]
    [InlineData("staleFence8")]
    [InlineData("activateFence7")]
    [InlineData("wrongReason")]
    public void ActualRequestsMustMatchThePinnedJourney(string mutation)
    {
        var value = Fixture();
        var primary = value.Primary.ToArray();
        var secondary = value.Secondary.ToArray();
        switch (mutation)
        {
            case "newReplayKey": primary[1] = primary[1] with { IdempotencyKey = "new" }; break;
            case "changedReplayIntent": primary[1] = primary[1] with { Command = primary[1].Command with { IsActive = true } }; break;
            case "sameStaleKey": secondary[0] = secondary[0] with { IdempotencyKey = primary[0].IdempotencyKey }; break;
            case "staleFence8": secondary[0] = secondary[0] with { Command = secondary[0].Command with { ExpectedVersion = 8 } }; break;
            case "activateFence7": secondary[1] = secondary[1] with { Command = secondary[1].Command with { ExpectedVersion = 7 } }; break;
            case "wrongReason": secondary[1] = secondary[1] with { Command = secondary[1].Command with { Reason = "different" } }; break;
        }
        Assert.Throws<InvalidOperationException>(() => Verify(value with { Primary = primary, Secondary = secondary }));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("status")]
    [InlineData("company")]
    [InlineData("ack")]
    [InlineData("key")]
    [InlineData("replay")]
    [InlineData("count")]
    [InlineData("seq")]
    public void ProjectionAcceptsStringFenceAndRequiresActualReplayHeaders(string mutation)
    {
        var value = Fixture();
        var rows = new JsonArray();
        for (var i = 0; i < 2; i++) rows.Add(new JsonObject
        {
            ["seq"] = i, ["companyId"] = value.Identity.CompanyAId,
            ["contractDeclaration"] = SourceBinding.ClientContractVersion,
            ["idempotencyKey"] = "k1", ["status"] = i == 0 ? null : JsonValue.Create(204),
            ["replayed"] = i == 0 ? null : "true", ["contractAck"] = i == 0 ? null : SourceBinding.ClientContractVersion,
            ["requestBody"] = new JsonObject { ["isActive"] = false, ["expectedVersion"] = "7", ["reason"] = value.Primary[0].Command.Reason },
        });
        switch (mutation)
        {
            case "status": rows[1]!["status"] = 200; break;
            case "company": rows[1]!["companyId"] = value.Identity.CompanyBId; break;
            case "ack": rows[1]!["contractAck"] = null; break;
            case "key": rows[1]!["idempotencyKey"] = " "; break;
            case "replay": rows[1]!["replayed"] = null; break;
            case "count": rows.RemoveAt(1); break;
            case "seq": rows[1]!["seq"] = 0; break;
        }
        StatusJourneyAttempt[] Read() => StatusJourneyMeasurement.ReadAttempts(value.Identity, JsonSerializer.SerializeToElement(rows), true);
        if (mutation == "valid") Assert.Equal(value.Primary, Read());
        else Assert.Throws<InvalidOperationException>(() => Read());
    }

    private static StatusJourneySnapshot ChangeReceipt(StatusJourneySnapshot value, ContactExecutionRow receipt) =>
        value with { Executions = [receipt, .. value.Executions.Skip(1)] };

    [Theory]
    [InlineData("valid")]
    [InlineData("run")]
    [InlineData("count")]
    [InlineData("expected")]
    [InlineData("barrier")]
    [InlineData("outstanding")]
    [InlineData("lostKey")]
    [InlineData("upstream")]
    [InlineData("readParty")]
    [InlineData("sameRead")]
    [InlineData("readOrder")]
    [InlineData("readState")]
    public void CompleteBrowserProjectionMustBindBothLostDeliveriesAndFinalBarriers(string mutation)
    {
        var value = Fixture();
        JsonArray Attempts(StatusJourneyAttempt[] requests, bool primary)
        {
            var result = new JsonArray();
            for (var i = 0; i < requests.Length; i++) result.Add(new JsonObject
            {
                ["seq"] = i, ["companyId"] = value.Identity.CompanyAId,
                ["contractDeclaration"] = SourceBinding.ClientContractVersion,
                ["idempotencyKey"] = requests[i].IdempotencyKey,
                ["status"] = primary && i == 0 ? null : JsonValue.Create(i == 0 ? 409 : 204),
                ["replayed"] = primary && i == 1 ? "true" : null,
                ["contractAck"] = i == 0 ? null : SourceBinding.ClientContractVersion,
                ["problemCode"] = !primary && i == 0 ? "version_conflict" : null,
                ["requestBody"] = JsonSerializer.SerializeToNode(requests[i].Command, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            });
            return result;
        }
        JsonObject Account() => new() { ["version"] = "9", ["isActive"] = true };
        JsonObject Withheld(bool read) => new()
        {
            ["marker"] = "upstream_success_then_delivery_abort", ["method"] = read ? "GET" : "PUT",
            ["upstreamStatus"] = read ? 200 : 204, ["companyId"] = value.Identity.CompanyAId,
            ["contractAck"] = SourceBinding.ClientContractVersion, ["deliveredToBrowser"] = false,
            ["idempotencyKey"] = read ? null : "k1", ["partyId"] = value.PartyId,
            ["path"] = $"/api/v1/counterparties/{value.PartyId}" + (read ? "" : "/company-account/active-state"),
            ["account"] = Account(),
        };
        var barriers = new JsonObject();
        foreach (var name in new[] { "main", "stale", "readOnly" })
            barriers[name] = new JsonObject { ["accepted"] = true, ["outstanding"] = new JsonArray() };
        var measurement = new JsonObject
        {
            ["runId"] = value.Identity.RunId, ["instanceId"] = value.Identity.InstanceId,
            ["slug"] = "l04-account-status-journey", ["companyA"] = value.Identity.CompanyAId,
            ["companyB"] = value.Identity.CompanyBId, ["readOnlyActor"] = AcceptanceProfile.ReadOnlyActor,
            ["partyId"] = value.PartyId,
            ["expected"] = JsonSerializer.SerializeToNode(new { puts = 4, successfulLogicalUpdates = 2, auditEvents = 2,
                noChangeAudits = 0, durableUniqueKeys = 3, replays = 1, reDeactivations = 0, newParties = 0,
                newAccounts = 0, companyBWrites = 0, readOnlyWrites = 0,
                durableOutcomes = new { succeeded204 = 2, versionConflict409 = 1 } }),
            ["versions"] = JsonSerializer.SerializeToNode(new { start = "7", afterK1 = "8", afterK2 = "9", afterK1Replay = "9" }),
            ["finalCounts"] = JsonSerializer.SerializeToNode(new { main = 2, stale = 2, total = 4, mainInCompanyB = 0, readOnly = 0 }),
            ["barriers"] = barriers,
            ["accountStatusPuts"] = Attempts(value.Primary, true), ["staleAccountStatusPuts"] = Attempts(value.Secondary, false),
            ["withheld"] = new JsonObject { ["main"] = new JsonArray(Withheld(false)), ["staleRead"] = new JsonArray(Withheld(true)) },
            ["k1ReplayOrdering"] = new JsonObject
            {
                ["preRead"] = new JsonObject { ["seq"] = 10, ["transferTick"] = 1, ["account"] = Account() },
                ["put"] = new JsonObject { ["status"] = 204, ["requestTick"] = 2, ["transferTick"] = 3 },
                ["postRead"] = new JsonObject { ["seq"] = 12, ["requestTick"] = 4, ["account"] = Account() },
            },
        };
        switch (mutation)
        {
            case "run": measurement["runId"] = "old-run"; break;
            case "count": measurement["finalCounts"]!["total"] = 5; break;
            case "expected": measurement["expected"]!["noChangeAudits"] = 1; break;
            case "barrier": barriers["stale"]!["accepted"] = false; break;
            case "outstanding": barriers["readOnly"]!["outstanding"]!.AsArray().Add(1); break;
            case "lostKey": measurement["withheld"]!["main"]![0]!["idempotencyKey"] = "another"; break;
            case "upstream": measurement["withheld"]!["main"]![0]!["upstreamStatus"] = 409; break;
            case "readParty": measurement["withheld"]!["staleRead"]![0]!["partyId"] = Guid.NewGuid(); break;
            case "sameRead": measurement["k1ReplayOrdering"]!["postRead"]!["seq"] = 10; break;
            case "readOrder": measurement["k1ReplayOrdering"]!["postRead"]!["requestTick"] = 2; break;
            case "readState": measurement["k1ReplayOrdering"]!["postRead"]!["account"]!["isActive"] = false; break;
        }
        void Check() => StatusJourneyMeasurement.Verify(value.Identity, value.Before, value.After, JsonSerializer.SerializeToElement(measurement));
        if (mutation == "valid") Check();
        else Assert.Throws<InvalidOperationException>(Check);
    }

    private static void Verify(Case value) => DatabaseEvidence.VerifyStatusJourney(value.Identity,
        value.Before, value.After, value.PartyId, value.Primary, value.Secondary);

    private static Case Fixture()
    {
        var identity = new RunSettings();
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();
        var a = Guid.Parse(identity.CompanyAId);
        var b = Guid.Parse(identity.CompanyBId);
        var row = new AccountRow(party, "C1", identity.SmokePrefix, a, 0, "7", "{\"Phone\":\"00123\"}", AccountId: account);
        var database = new DatabaseSnapshot(identity.DatabaseName, true, new(1, 2, 1, 1, 1, 1, 6, 6),
            [row, new(party, "C1", identity.SmokePrefix, b, 0, "1")], new string('a', 64), []);
        var first = new StatusJourneyAttempt("k1", new(false, 7,
            identity.BrowserPrefix + " \u2014 bu \u015firket hesab\u0131ndaki \u00e7al\u0131\u015fmaya ara verildi."));
        var stale = new StatusJourneyAttempt("ks", new(false, 7,
            identity.BrowserPrefix + " \u2014 \u00e7al\u0131\u015fma yeniden ba\u015flad\u0131."));
        var activate = new StatusJourneyAttempt("k2", stale.Command with { IsActive = true, ExpectedVersion = 8 });
        const string actor = "20000000-0000-0000-0000-000000000003";
        ContactExecutionRow Receipt(StatusJourneyAttempt attempt, bool conflict = false) => new(Guid.NewGuid(), a,
            IdempotencyHash.ComputeKey(attempt.IdempotencyKey), DatabaseEvidence.StatusPayload(party, attempt), actor,
            conflict ? "VersionConflict" : "Succeeded", conflict ? 409 : 204,
            JsonSerializer.Serialize(new CounterpartyCommandResponse(conflict ? CounterpartyCommandResult.VersionConflict : CounterpartyCommandResult.Succeeded,
                conflict ? 409 : 204, conflict ? "Conflict" : null, conflict ? "Read current" : null, null,
                conflict ? new CounterpartyVersionConflict(7, 8) : null)), 1);
        string State(bool active, long version) => JsonSerializer.Serialize(new
        { Id = account, CompanyId = a, PartyId = party, Roles = 0, ExternalIntegrationCode = (string?)null, IsActive = active, Version = version });
        StatusAuditRow Audit(StatusJourneyAttempt attempt) => new(Guid.NewGuid(), a, account.ToString("D"), actor,
            ErpPermissions.CounterpartyAccountsStatusUpdate, IdempotencyHash.ComputeKey(attempt.IdempotencyKey),
            attempt.Command.IsActive ? "Activate" : "Deactivate", attempt.Command.ExpectedVersion, attempt.Command.ExpectedVersion + 1,
            attempt.Command.Reason, State(!attempt.Command.IsActive, attempt.Command.ExpectedVersion), State(attempt.Command.IsActive, attempt.Command.ExpectedVersion + 1));
        var before = new StatusJourneySnapshot(database, [], []);
        var after = new StatusJourneySnapshot(database with { Rows = [row with { Version = "9" }, database.Rows[1]] },
            [Receipt(first), Receipt(stale, true), Receipt(activate)], [Audit(first), Audit(activate)]);
        return new(identity, party, before, after, [first, first with { }], [stale, activate]);
    }

    private sealed record Case(RunSettings Identity, Guid PartyId, StatusJourneySnapshot Before,
        StatusJourneySnapshot After, StatusJourneyAttempt[] Primary, StatusJourneyAttempt[] Secondary);
}
