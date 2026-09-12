using System.Text.Json;
using GoymenERP.CariAcceptance006;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class ContactJourneyEvidenceTests
{
    [Fact]
    public void TwoUpdatesReplayAndDurableConflictRequireThreeReceiptsAndNoNewParty()
    {
        var value = Fixture();
        Verify(value);
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.VerifyBrowserJourney(
            value.Identity, value.Before, value.After, 1));
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.VerifyContactEdit(
            value.Identity, value.Before, value.After, value.PartyId, value.CompanyId,
            CounterpartyContactInformationEditor.Empty()));
    }

    [Theory]
    [InlineData("missingConflict")]
    [InlineData("extraReplayReceipt")]
    [InlineData("wrongCompany")]
    [InlineData("wrongActor")]
    [InlineData("wrongPayload")]
    [InlineData("wrongKey")]
    [InlineData("conflictSuccess")]
    [InlineData("conflictStatus")]
    [InlineData("schema")]
    [InlineData("duplicateReceipt")]
    [InlineData("newParty")]
    [InlineData("newAccount")]
    [InlineData("extraAudit")]
    [InlineData("missingDurableConflictCount")]
    [InlineData("otherCompanyEdit")]
    [InlineData("globalEdit")]
    [InlineData("wrongFinalValue")]
    [InlineData("wrongFinalVersion")]
    [InlineData("oldBaseline")]
    [InlineData("foreignDatabase")]
    [InlineData("rcsi")]
    public void UnrelatedEffectsOrMisboundDurableResultFailClosed(string mutation)
    {
        var value = Fixture();
        var after = value.After;
        var receipts = after.ContactExecutionRows!.ToArray();
        switch (mutation)
        {
            case "missingConflict": after = after with { ContactExecutionRows = receipts[..2] }; break;
            case "extraReplayReceipt": after = after with { ContactExecutionRows = [.. receipts, receipts[0] with { Id = Guid.NewGuid() }] }; break;
            case "wrongCompany": receipts[2] = receipts[2] with { CompanyId = Guid.Parse(value.Identity.CompanyBId) }; break;
            case "wrongActor": receipts[2] = receipts[2] with { ActorId = "another-actor" }; break;
            case "wrongPayload": receipts[2] = receipts[2] with { RequestHash = new string('A', 64) }; break;
            case "wrongKey": receipts[2] = receipts[2] with { IdempotencyKeyHash = new string('B', 64) }; break;
            case "conflictSuccess": receipts[2] = receipts[2] with { Outcome = "Succeeded" }; break;
            case "conflictStatus": receipts[2] = receipts[2] with { HttpStatusCode = 204 }; break;
            case "schema": receipts[2] = receipts[2] with { SchemaVersion = 2 }; break;
            case "duplicateReceipt": receipts[2] = receipts[2] with { Id = receipts[0].Id }; break;
            case "newParty": after = after with { Effects = after.Effects with { Parties = 2 } }; break;
            case "newAccount": after = after with { Effects = after.Effects with { Accounts = 3 } }; break;
            case "extraAudit": after = after with { Effects = after.Effects with { ContactAudits = 3 } }; break;
            case "missingDurableConflictCount": after = after with { Effects = after.Effects with { ContactExecutions = 2 } }; break;
            case "otherCompanyEdit": after = after with { Rows = [after.Rows[0], after.Rows[1] with { Version = "2" }] }; break;
            case "globalEdit": after = after with { PartyStateSha256 = new string('b', 64) }; break;
            case "wrongFinalValue": after = after with { Rows = [after.Rows[0] with { ContactInformationJson = "{}" }, after.Rows[1]] }; break;
            case "wrongFinalVersion": after = after with { Rows = [after.Rows[0] with { Version = "4" }, after.Rows[1]] }; break;
            case "oldBaseline": value = value with { Before = value.Before with { ContactExecutionRows = null } }; break;
            case "foreignDatabase": after = after with { DatabaseName = "another-database" }; break;
            case "rcsi": after = after with { Rcsi = false }; break;
            default: throw new ArgumentException(nameof(mutation));
        }
        if (ReferenceEquals(after.ContactExecutionRows, value.After.ContactExecutionRows))
            after = after with { ContactExecutionRows = receipts };
        Assert.Throws<InvalidOperationException>(() => Verify(value with { After = after }));
    }

    [Theory]
    [InlineData("replayKey")]
    [InlineData("replayBody")]
    [InlineData("clearVersion")]
    [InlineData("staleVersion")]
    [InlineData("sharedKey")]
    [InlineData("targetCompany")]
    [InlineData("firstValue")]
    [InlineData("clearOnlyPhone")]
    [InlineData("staleValue")]
    public void ActualBrowserAttemptsMustMatchThePinnedJourney(string mutation)
    {
        var value = Fixture();
        value = mutation switch
        {
            "replayKey" => value with { Replay = value.Replay with { IdempotencyKey = "new-replay-key" } },
            "replayBody" => value with { Replay = value.Replay with { Command = value.Replay.Command with { ExpectedVersion = "2" } } },
            "clearVersion" => value with { Clear = value.Clear with { Command = value.Clear.Command with { ExpectedVersion = "1" } } },
            "staleVersion" => value with { Stale = value.Stale with { Command = value.Stale.Command with { ExpectedVersion = "3" } } },
            "sharedKey" => value with { Stale = value.Stale with { IdempotencyKey = value.First.IdempotencyKey } },
            "targetCompany" => value with { CompanyId = Guid.Parse(value.Identity.CompanyBId) },
            "firstValue" => ChangeFirst(value),
            "clearOnlyPhone" => value with { Clear = value.Clear with { Command = new("2", new() { Phone = JsonSerializer.SerializeToElement<string?>(null) }) } },
            "staleValue" => value with { Stale = value.Stale with { Command = new("1", new() { Email = Text("different@example.test") }) } },
            _ => throw new ArgumentException(nameof(mutation)),
        };
        Assert.Throws<InvalidOperationException>(() => Verify(value));
    }

    private static Case ChangeFirst(Case value)
    {
        var changed = value.First with
        {
            Command = value.First.Command with { Changes = value.First.Command.Changes with { Phone = Text("wrong") } },
        };
        return value with { First = changed, Replay = changed with { } };
    }

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);
    private static void Verify(Case value) => DatabaseEvidence.VerifyContactJourney(value.Identity,
        value.Before, value.After, value.PartyId, value.CompanyId, value.First, value.Replay, value.Clear, value.Stale);

    private static Case Fixture()
    {
        var identity = new RunSettings();
        var party = Guid.NewGuid();
        var company = Guid.Parse(identity.CompanyAId);
        var a = new AccountRow(party, "CAR-TEST", "Synthetic", company, 0, "1");
        var b = a with { CompanyId = Guid.Parse(identity.CompanyBId) };
        var before = new DatabaseSnapshot(identity.DatabaseName, true, new Effects(1, 2, 1, 1, 1, 1),
            [a, b], new string('a', 64), []);
        var first = new ContactJourneyAttempt("first", new("1", new()
        {
            Phone = Text("+90 212 555 00 01"), PhoneExtension = Text("123"),
            Addresses = [
                new() { Id = Guid.NewGuid(), Operation = "upsert", Purpose = Text("delivery"), CountryCode = Text("TR"),
                    Line1 = Text(identity.BrowserPrefix + " Teslim Yolu 1"), City = Text("Kocaeli") },
                new() { Id = Guid.NewGuid(), Operation = "upsert", Purpose = Text("operational"), CountryCode = Text("DE"),
                    Line1 = Text(identity.BrowserPrefix + " Operational 2") },
            ],
            Contacts = [new() { Id = Guid.NewGuid(), Operation = "upsert", DisplayName = Text(identity.BrowserPrefix + " Operasyon Muhatab\u0131"),
                Purpose = Text("Operasyon"), Channels = [new() { Id = Guid.NewGuid(), Operation = "upsert", Kind = Text("phone"), Value = Text("0532 000 00 01") }] }],
        }));
        var clear = new ContactJourneyAttempt("clear", new("2", new()
        {
            Phone = JsonSerializer.SerializeToElement<string?>(null),
            PhoneExtension = JsonSerializer.SerializeToElement<string?>(null),
        }));
        var stale = new ContactJourneyAttempt("stale", new("1", new()
        {
            Email = Text(identity.BrowserPrefix.ToLowerInvariant() + "-stale@example.test"),
        }));
        var final = CounterpartyContactInformationEditor.Apply(
            CounterpartyContactInformationEditor.Apply(CounterpartyContactInformationEditor.Empty(), first.Command.Changes),
            clear.Command.Changes);
        ContactExecutionRow Receipt(ContactJourneyAttempt attempt, bool conflict = false) => new(
            Guid.NewGuid(), company, IdempotencyHash.ComputeKey(attempt.IdempotencyKey),
            IdempotencyHash.ComputePayload(new { PartyId = party, Command = attempt.Command }),
            identity.Actor, conflict ? "VersionConflict" : "Succeeded", conflict ? 409 : 204, "{}", 1);
        var after = before with
        {
            Effects = before.Effects with { ContactAudits = 2, ContactExecutions = 3 },
            Rows = [a with { Version = "3", ContactInformationJson = CounterpartyContactInformationEditor.Serialize(final) }, b],
            ContactExecutionRows = [Receipt(first), Receipt(clear), Receipt(stale, true)],
        };
        return new(identity, before, after, party, company, first, first with { }, clear, stale);
    }

    private sealed record Case(RunSettings Identity, DatabaseSnapshot Before, DatabaseSnapshot After,
        Guid PartyId, Guid CompanyId, ContactJourneyAttempt First, ContactJourneyAttempt Replay,
        ContactJourneyAttempt Clear, ContactJourneyAttempt Stale);
}
