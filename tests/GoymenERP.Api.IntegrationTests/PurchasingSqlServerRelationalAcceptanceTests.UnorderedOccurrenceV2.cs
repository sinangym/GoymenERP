using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Unordered_v2_explicit_day_raw_optional_reason_and_real_actor_are_one_original_receipt_and_actual_audit()
    {
        await using var run = await Run.StartAsync(configureNumbers: false);
        var request = new RecordPurchaseUnorderedAcceptanceV2(UnorderedSqlRequest(false), new(2026, 10, 9), "  fiilî kabul\nkontrol sonra  ");
        var envelope = UnorderedEnvelope(run, "SENTETIK-unordered-v2-original");
        await using var scope = UnorderedSqlScope(run, CompanyA);
        var first = await RecordUnorderedV2(scope, request, envelope);
        Assert.False(first.IsReplay); Assert.Equal(envelope.ActorId, first.Record.RecordedBy);
        Assert.Equal(request.Occurrence.OccurredAtUtc, first.Record.Source.OccurredAtUtc); Assert.Equal(T0, first.Record.RecordedAtUtc);
        Assert.Equal(new PurchaseUnorderedAcceptanceDeclarationV2(request.BusinessDate, request.Reason), first.Record.Declaration);
        var read = await scope.Store.ReadAsync(CompanyA, request.Occurrence.AcceptanceId, request.Occurrence.AcceptanceLineId);
        Assert.Equal(PurchaseUnorderedAcceptanceSourceOutcomeV1.Found, read.Outcome);
        Assert.Equal(first.Record.Declaration, read.Source!.Declaration);
        var facts = await run.ScalarAsync<string>("SELECT RecordJson FROM purchasing.UnorderedAcceptanceFacts WHERE CompanyId=@company AND AcceptanceId=@id",
            ("@company", CompanyA), ("@id", request.Occurrence.AcceptanceId));
        var attempt = await run.ScalarAsync<string>("SELECT RecordJson FROM purchasing.UnorderedAcceptanceAttempts WHERE CompanyId=@company AND AcceptanceId=@id",
            ("@company", CompanyA), ("@id", request.Occurrence.AcceptanceId));
        var audit = await run.ScalarAsync<string>("SELECT AfterJson FROM audit.AuditEvents WHERE CompanyId=@company AND EventType=N'purchasing.unordered-acceptance.recorded.v2'",
            ("@company", CompanyA));
        Assert.Equal(facts, attempt); Assert.Equal(facts, audit);
        var replay = await scope.Store.RecordOccurrenceAsync(CompanyA, request, envelope with { CorrelationId = "SENTETIK-v2-replay" },
            PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request), _ => throw new InvalidOperationException("No re-preparation on original-key replay."),
            _ => Task.CompletedTask, default);
        Assert.True(replay.IsReplay); Assert.Equal(envelope.CorrelationId, replay.OriginalCorrelationId); Assert.Equal(first.Record.Declaration, replay.Record.Declaration);
        Assert.Equal(facts, JsonSerializer.Serialize(replay.Record, PurchasingJson.Options));
        Assert.Equal((1, 1, 1), await UnorderedV2Counts(run));
    }

    [SqlServerFact]
    public async Task Unordered_v2_day_reason_and_v1_command_are_not_equivalent_payloads_under_the_same_original_key()
    {
        await using var run = await Run.StartAsync(configureNumbers: false);
        var request = new RecordPurchaseUnorderedAcceptanceV2(UnorderedSqlRequest(true), new(2026, 10, 9), null);
        var envelope = UnorderedEnvelope(run, "SENTETIK-unordered-v2-hash");
        await using var scope = UnorderedSqlScope(run, CompanyA);
        await RecordUnorderedV2(scope, request, envelope);
        foreach (var changed in new[] { request with { BusinessDate = request.BusinessDate.AddDays(1) }, request with { Reason = "" }, request with { Reason = " raw " } })
        {
            var refused = await Assert.ThrowsAsync<PurchasingStoreException>(() => RecordUnorderedV2(scope, changed, envelope));
            Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch, refused.Code);
        }
        var old = await Assert.ThrowsAsync<PurchasingStoreException>(() => RecordUnordered(scope, request.Occurrence, envelope));
        Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch, old.Code);
        var replay = await RecordUnorderedV2(scope, request, envelope); Assert.True(replay.IsReplay);
        Assert.Null(replay.Record.Declaration!.Reason); Assert.Equal(request.BusinessDate, replay.Record.Declaration.BusinessDate);
        Assert.Equal((1, 1, 1), await UnorderedV2Counts(run));
    }

    [SqlServerFact]
    public async Task Unordered_v1_record_and_source_stay_readable_without_declaration_backfill_or_rewritten_original_json()
    {
        await using var run = await Run.StartAsync(configureNumbers: false);
        var request = UnorderedSqlRequest(false); var envelope = UnorderedEnvelope(run, "SENTETIK-unordered-v1-compat");
        await using var scope = UnorderedSqlScope(run, CompanyA);
        await RecordUnordered(scope, request, envelope);
        var original = await run.ScalarAsync<string>("SELECT RecordJson FROM purchasing.UnorderedAcceptanceFacts WHERE AcceptanceId=@id", ("@id", request.AcceptanceId));
        Assert.DoesNotContain("declaration", original, StringComparison.OrdinalIgnoreCase);
        var replay = await RecordUnordered(scope, request, envelope); Assert.True(replay.IsReplay); Assert.Null(replay.Record.Declaration);
        var source = await scope.Store.ReadAsync(CompanyA, request.AcceptanceId, request.AcceptanceLineId);
        Assert.Equal(PurchaseUnorderedAcceptanceSourceOutcomeV1.Found, source.Outcome); Assert.NotNull(source.Source);
        Assert.Equal(request.OccurredAtUtc, source.Source.OccurredAtUtc);
        Assert.Null(source.Source.Declaration);
        Assert.Equal(original, JsonSerializer.Serialize(replay.Record, PurchasingJson.Options));
        Assert.Equal(original, await run.ScalarAsync<string>("SELECT RecordJson FROM purchasing.UnorderedAcceptanceFacts WHERE AcceptanceId=@id", ("@id", request.AcceptanceId)));
        Assert.Equal((1, 1, 1), await UnorderedCounts(run));
    }

    [SqlServerFact]
    public async Task Unordered_v2_actual_audit_failure_rolls_back_occurrence_declaration_and_original_attempt_then_same_key_records_once()
    {
        await using var run = await Run.StartAsync(configureNumbers: false);
        var request = new RecordPurchaseUnorderedAcceptanceV2(UnorderedSqlRequest(false), new(2026, 10, 9));
        var envelope = UnorderedEnvelope(run, "SENTETIK-unordered-v2-audit-failure");
        await using (var failed = UnorderedSqlScope(run, CompanyA, failAudit: true))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RecordUnorderedV2(failed, request, envelope));
            Assert.Empty(failed.Database.ChangeTracker.Entries()); Assert.False(failed.Writer.HasPendingDrafts);
        }
        Assert.Equal((0, 0, 0), await UnorderedV2Counts(run));
        await using var scope = UnorderedSqlScope(run, CompanyA);
        Assert.False((await RecordUnorderedV2(scope, request, envelope)).IsReplay);
        Assert.True((await RecordUnorderedV2(scope, request, envelope)).IsReplay);
        Assert.Equal((1, 1, 1), await UnorderedV2Counts(run));
    }

    // Actual SQL owner transaction; preparation is controlled here. The Documents adapter has separate owner-protocol tests.
    private static Task<PurchaseUnorderedAcceptanceWriteV1> RecordUnorderedV2(UnorderedSqlOwner scope, RecordPurchaseUnorderedAcceptanceV2 request,
        PurchasingWriteEnvelope envelope) => scope.Store.RecordOccurrenceAsync(CompanyA, request, envelope,
        PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request), _ =>
        {
            var record = UnorderedSqlRecord(request.Occurrence, envelope.ActorId);
            return Task.FromResult(record with { Source = record.Source with { Declaration = new(request.BusinessDate, request.Reason) } });
        }, _ => Task.CompletedTask, default);
    private static async Task<(int Facts, int Attempts, int Audits)> UnorderedV2Counts(Run run) =>
        (await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.UnorderedAcceptanceFacts"),
         await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.UnorderedAcceptanceAttempts"),
         await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.unordered-acceptance.recorded.v2'"));
}
