using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;

namespace GoymenERP.CariAcceptance006;

public sealed record StatusJourneyAttempt(string IdempotencyKey, SetCompanyPartyAccountActiveState Command);
public sealed record StatusAuditRow(Guid Id, Guid CompanyId, string EntityId, string ActorId,
    string PermissionCode, string IdempotencyKeyHash, string Operation, long VersionBefore,
    long VersionAfter, string Reason, string BeforeJson, string AfterJson);
public sealed record StatusJourneySnapshot(DatabaseSnapshot Database,
    IReadOnlyList<ContactExecutionRow> Executions, IReadOnlyList<StatusAuditRow> Audits);

public static partial class DatabaseEvidence
{
    // Reuses the owned C006 connection and snapshot. No caller-supplied SQL is accepted.
    public static async Task<StatusJourneySnapshot> ReadStatusJourneyAsync(RunSettings identity)
    {
        var database = await ReadAsync(identity);
        await using var connection = new SqlConnection(identity.NewOwnedDatabaseConnection());
        await connection.OpenAsync();
        await using var executions = connection.CreateCommand();
        executions.CommandText = """
            SELECT Id, CompanyId, IdempotencyKeyHash, RequestHash, ActorId, Outcome,
                   HttpStatusCode, ResponseJson, SchemaVersion
            FROM idempotency.CommandExecutions
            WHERE CommandName=N'counterparty.company-account.set-active-state.v1'
            ORDER BY Id;
            """;
        await using var reader = await executions.ExecuteReaderAsync();
        var receipts = new List<ContactExecutionRow>();
        while (await reader.ReadAsync())
            receipts.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6),
                reader.GetString(7), reader.GetInt32(8)));
        await reader.DisposeAsync();
        await using var audits = connection.CreateCommand();
        audits.CommandText = """
            SELECT Id, CompanyId, EntityId, ActorId, PermissionCode, IdempotencyKeyHash,
                   Operation, EntityVersionBefore, EntityVersionAfter, Reason, BeforeJson, AfterJson
            FROM audit.AuditEvents
            WHERE EventType=N'counterparty.company-account.active-state.changed.v1'
            ORDER BY Id;
            """;
        await using var auditReader = await audits.ExecuteReaderAsync();
        var events = new List<StatusAuditRow>();
        while (await auditReader.ReadAsync())
            events.Add(new(auditReader.GetGuid(0), auditReader.GetGuid(1), auditReader.GetString(2),
                auditReader.GetString(3), auditReader.GetString(4), auditReader.GetString(5),
                auditReader.GetString(6), auditReader.GetInt64(7), auditReader.GetInt64(8),
                auditReader.GetString(9), auditReader.GetString(10), auditReader.GetString(11)));
        return new(database, receipts, events);
    }

    public static string StatusPayload(Guid partyId, StatusJourneyAttempt attempt) =>
        IdempotencyHash.ComputePayload(new
        {
            PartyId = partyId, attempt.Command.IsActive, attempt.Command.ExpectedVersion,
            Reason = attempt.Command.Reason.Trim(),
        });

    // Expected requests come from the pinned browser journey, never from the SQL result.
    public static void VerifyStatusJourney(RunSettings identity, StatusJourneySnapshot baseline,
        StatusJourneySnapshot current, Guid partyId,
        IReadOnlyList<StatusJourneyAttempt> primary, IReadOnlyList<StatusJourneyAttempt> secondary)
    {
        identity.Validate("Testing");
        RequireStatus(primary.Count == 2 && secondary.Count == 2, "exact four-PUT inventory required");
        var first = primary[0];
        var replay = primary[1];
        var stale = secondary[0];
        var activate = secondary[1];
        var a = Guid.Parse(identity.CompanyAId);
        var b = Guid.Parse(identity.CompanyBId);
        var before = baseline.Database;
        var after = current.Database;
        RequireStatus(before.DatabaseName == identity.DatabaseName && after.DatabaseName == identity.DatabaseName
            && before.Rcsi && after.Rcsi && before.PartyStateSha256.Length == 64
            && before.PartyStateSha256 == after.PartyStateSha256
            && baseline.Executions.Count == 0 && baseline.Audits.Count == 0,
            "fresh owned baseline or Party identity differs");
        var initial = before.Rows.SingleOrDefault(row => row.PartyId == partyId && row.CompanyId == a);
        var other = before.Rows.SingleOrDefault(row => row.PartyId == partyId && row.CompanyId == b);
        RequireStatus(initial is { Roles: 0, IsActive: true, Version: "7" }
            && initial.AccountId != Guid.Empty
            && !string.IsNullOrWhiteSpace(initial.ContactInformationJson)
            && other is { Roles: 0, IsActive: true, Version: "1" }, "A v7 / B v1 roleless fixture required");
        RequireStatus(first.Command == new SetCompanyPartyAccountActiveState(false, 7,
                identity.BrowserPrefix + " \u2014 bu \u015firket hesab\u0131ndaki \u00e7al\u0131\u015fmaya ara verildi.")
            && stale.Command == new SetCompanyPartyAccountActiveState(false, 7,
                identity.BrowserPrefix + " \u2014 \u00e7al\u0131\u015fma yeniden ba\u015flad\u0131.")
            && activate.Command == stale.Command with { IsActive = true, ExpectedVersion = 8 }
            && first.Command == replay.Command && first.IdempotencyKey == replay.IdempotencyKey,
            "actual intent, reason, fence or historical replay differs");
        var attempts = new[] { first, stale, activate };
        RequireStatus(attempts.All(x => !string.IsNullOrWhiteSpace(x.IdempotencyKey))
            && attempts.Select(x => IdempotencyHash.ComputeKey(x.IdempotencyKey)).Distinct().Count() == 3,
            "three independent durable keys required");
        RequireStatus(after.Effects == before.Effects && after.Rows.Count == before.Rows.Count
            && after.Rows.Contains(initial! with { Version = "9" })
            && before.Rows.Where(x => x != initial).All(after.Rows.Contains)
            && JsonSerializer.Serialize(before.ContactExecutionRows) == JsonSerializer.Serialize(after.ContactExecutionRows),
            "final active v9 must preserve roles, contacts, Party, B and all unrelated effects");
        RequireStatus(current.Executions.Count == 3 && current.Audits.Count == 2
            && current.Executions.All(x => x.Id != Guid.Empty)
            && current.Executions.Select(x => x.Id).Distinct().Count() == 3
            && current.Audits.All(x => x.Id != Guid.Empty)
            && current.Audits.Select(x => x.Id).Distinct().Count() == 2,
            "two audit events and three unique receipts required; replay/NoChange adds nothing");
        foreach (var attempt in attempts)
        {
            var key = IdempotencyHash.ComputeKey(attempt.IdempotencyKey);
            var matching = current.Executions.Where(x => x.IdempotencyKeyHash == key).ToArray();
            var conflict = ReferenceEquals(attempt, stale);
            RequireStatus(matching.Length == 1 && matching[0].CompanyId == a
                && matching[0].ActorId == ContactJourneyActorId
                && matching[0].RequestHash == StatusPayload(partyId, attempt)
                && matching[0].Outcome == (conflict ? "VersionConflict" : "Succeeded")
                && matching[0].HttpStatusCode == (conflict ? 409 : 204) && matching[0].SchemaVersion == 1,
                "durable key/company/actor/payload/outcome differs");
            ValidateContactResponse(matching[0], conflict, 7, 8);
            var events = current.Audits.Where(x => x.IdempotencyKeyHash == key).ToArray();
            RequireStatus(events.Length == (conflict ? 0 : 1), "stale or replay produced an audit effect");
            if (conflict) continue;
            var audit = events[0];
            var version = attempt.Command.ExpectedVersion;
            RequireStatus(audit.CompanyId == a && audit.ActorId == ContactJourneyActorId
                && audit.PermissionCode == ErpPermissions.CounterpartyAccountsStatusUpdate
                && audit.Operation == (attempt.Command.IsActive ? "Activate" : "Deactivate")
                && audit.VersionBefore == version && audit.VersionAfter == version + 1
                && audit.Reason == attempt.Command.Reason, "audit authority/reason/transition differs");
            using var oldState = JsonDocument.Parse(audit.BeforeJson);
            using var newState = JsonDocument.Parse(audit.AfterJson);
            ValidateStatusAuditState(oldState.RootElement, audit, partyId, initial!.AccountId, !attempt.Command.IsActive, version);
            ValidateStatusAuditState(newState.RootElement, audit, partyId, initial!.AccountId, attempt.Command.IsActive, version + 1);
            RequireStatus(oldState.RootElement.GetProperty("ExternalIntegrationCode").GetRawText()
                == newState.RootElement.GetProperty("ExternalIntegrationCode").GetRawText(), "audit changed integration identity");
        }
        RequireStatus(current.Audits.Select(x => x.EntityId).Distinct().Count() == 1,
            "both transitions must belong to the same account");
    }

    private static void ValidateStatusAuditState(JsonElement state, StatusAuditRow audit,
        Guid party, Guid account, bool active, long version)
    {
        RequireStatus(state.ValueKind == JsonValueKind.Object && state.EnumerateObject().Count() == 7
            && Guid.TryParse(audit.EntityId, out var id) && id != Guid.Empty
            && id == account
            && state.GetProperty("Id").GetGuid() == id
            && state.GetProperty("CompanyId").GetGuid() == audit.CompanyId
            && state.GetProperty("PartyId").GetGuid() == party && state.GetProperty("Roles").GetInt32() == 0
            && state.GetProperty("IsActive").GetBoolean() == active
            && state.GetProperty("Version").GetInt64() == version, "audit snapshot differs");
    }

    private static void RequireStatus(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("C006 L-04 " + reason + ".");
    }
}
