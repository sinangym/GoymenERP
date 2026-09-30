using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// LOG-4a on real SQL Server (RCSI, the disposable database of the run): the instruction with its lines
/// is stored, issued and loaded in ONE save each (header, lines, share, attempt, audit); the §13 cap, a
/// blocked line and a stale version write nothing and are recorded / replayed as terminal refusals (§7);
/// a same-key barrier race stores one instruction, one attempt and one audit and the loser answers the
/// winner (§8); company B sees nothing and its same key is its own attempt; the routes answer only with
/// fresh grants and only for the context company. Nothing physical is written (D3). SENTETIK owners:
/// the customer and the order line are identities here — the owners' answers are the service's (unit
/// tests) and the browser suite's (LOG-4b) evidence.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    // The approved GDC kg identity (Ö1/Ö2): hosted shipment verifies the line unit against the real Settings
    // catalog before Sales/Inventory/authority, so a synthetic unit id would answer 422 unsupported_unit first.
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");

    [SqlServerFact]
    public async Task An_instruction_with_lines_is_stored_issued_loaded_within_the_cap_replayed_and_rebuilt_in_one_save_each()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        StoredLoadingInstruction stored;
        Guid lineId;
        await using (var a = run.Store(CompanyA))
        {
            stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners, lines: 2))).Instruction;
            lineId = stored.Instruction.Lines[0].Id;
            var replay = await a.Store.StoreInstructionAsync(run.Envelope("k-create", correlation: "SENTETIK-second"), Hash("create-1"), Day,
                () => throw new InvalidOperationException("a replay never builds"));
            Assert.Equal((LogisticsWriteOutcome.Replayed, stored.Instruction.Id, run.Correlation), (replay.Outcome, replay.Instruction.Instruction.Id, replay.OriginalCorrelationId));
            var other = await Assert.ThrowsAsync<LogisticsStoreException>(() => a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-9"), Day,
                () => throw new InvalidOperationException("never built")));
            Assert.Equal(LogisticsStoreCodes.SameKeyPayloadMismatch, other.Code);
        }

        Assert.Equal((1, 2, 0, 1, 1, 3L, 1), await run.CountsAsync(stored.Instruction.Id));

        await using (var a = run.Store(CompanyA))
        {
            var issued = await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-3"), LogisticsCommands.IssueV1, stored.Instruction.Id,
                (instruction, now) => instruction.Issue(3, now), Issued);
            Assert.Equal((LoadingInstructionStatus.Issued, 4L), (issued!.Instruction.Instruction.Status, issued.Instruction.Instruction.Version));
            var loaded = await a.Store.MutateInstructionAsync(run.Envelope("k-load-1"), Hash("load-4"), LogisticsCommands.RecordLoadV1, stored.Instruction.Id,
                (instruction, now) => instruction.RecordLoad(4, Guid.NewGuid(), lineId, Kg(4m), MeasurementKind.Measured, now), Loaded);
            Assert.Equal((5L, 4m, 2m), (loaded!.Instruction.Instruction.Version, loaded.Instruction.Instruction.Lines[0].LoadedQuantity.Value,
                loaded.Instruction.Instruction.Lines[0].RemainingToLoad.Value));
            // §13: the cap refuses the whole share; nothing below the domain ran, nothing was written.
            var over = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-load-2"), Hash("load-5"),
                LogisticsCommands.RecordLoadV1, stored.Instruction.Id, (instruction, now) => instruction.RecordLoad(5, Guid.NewGuid(), lineId, Kg(2.001m), MeasurementKind.Measured, now), Loaded));
            Assert.Equal(LogisticsRuleCodes.LoadExceedsInstructed, over.Code);
        }

        Assert.Equal((1, 2, 1, 3, 3, 5L, 3), await run.CountsAsync(stored.Instruction.Id));
        Assert.Equal(4m, await run.ScalarAsync<decimal>("SELECT Loaded FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", lineId)));

        await using (var a = run.Store(CompanyA))
        {
            // Rebuilt through the domain in recording order: lines, issue, share; the same version, the same totals.
            var rebuilt = await a.Store.FindInstructionAsync(stored.Instruction.Id);
            Assert.NotNull(rebuilt);
            Assert.Equal((LoadingInstructionStatus.Issued, 5L, 2, 1, 4m, Day), (rebuilt.Instruction.Status, rebuilt.Instruction.Version, rebuilt.Instruction.Lines.Count,
                rebuilt.Instruction.Shares.Count, rebuilt.Instruction.Lines[0].LoadedQuantity.Value, rebuilt.EffectiveOn));
            Assert.Equal((owners.Order, 0), (rebuilt.Instruction.Lines[0].Source.OrderId, rebuilt.Instruction.Lines[0].Source.Revision));
            var page = await a.Store.ListInstructionsAsync(1, null, LoadingInstructionStatus.Issued);
            Assert.Equal(stored.Instruction.Id, Assert.Single(page.Items).Instruction.Id);
            Assert.Null(page.NextCursor);
            var replayed = await a.Store.MutateInstructionAsync(run.Envelope("k-load-1", correlation: "SENTETIK-retry"), Hash("load-4"), LogisticsCommands.RecordLoadV1,
                stored.Instruction.Id, (_, _) => throw new InvalidOperationException("a replay never mutates"), Loaded);
            Assert.Equal((LogisticsWriteOutcome.Replayed, run.Correlation, 5L), (replayed!.Outcome, replayed.OriginalCorrelationId, replayed.Instruction.Instruction.Version));
        }
    }

    [SqlServerFact]
    public async Task A_blocked_line_refuses_a_load_as_a_recorded_terminal_refusal_and_a_stale_version_replays()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        Guid lineId;
        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners))).Instruction.Instruction;
            id = stored.Id;
            lineId = stored.Lines[0].Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-2"), LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(2, now), Issued);
        }

        // D2: the block's owner (Inventory/Quality) is not on main; the flag is set as its owner would set it, and the rebuild carries it.
        await run.ExecuteAsync("UPDATE logistics.LoadingInstructionLines SET IsBlocked = 1 WHERE LineId = @id", ("@id", lineId));
        await run.ExecuteAsync("UPDATE logistics.LoadingInstructions SET Version = Version + 1 WHERE Id = @id", ("@id", id));

        await using (var a = run.Store(CompanyA))
        {
            var envelope = run.Envelope("k-load-blocked");
            var blocked = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(envelope, Hash("load-4"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(4, Guid.NewGuid(), lineId, Kg(1m), MeasurementKind.Measured, now), Loaded));
            Assert.Equal(LogisticsRuleCodes.LineBlocked, blocked.Code);
            // §7: the service records the refusal under the key; the same key answers it again without touching the domain.
            await a.Store.RecordRefusalAsync(envelope, Hash("load-4"), LogisticsCommands.RecordLoadV1,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.LineBlocked, [new("lineId", LogisticsRuleCodes.LineBlocked)]));
            var replay = await a.Store.FindReplayAsync(run.Envelope("k-load-blocked", correlation: "SENTETIK-retry"), Hash("load-4"), LogisticsCommands.RecordLoadV1);
            Assert.Equal((409, LogisticsProblemCodesV1.LineBlocked, run.Correlation), (replay!.Refusal!.Status, replay.Refusal.Code, replay.OriginalCorrelationId));
            Assert.Null(replay.Instruction);
            var stale = await Assert.ThrowsAsync<LogisticsVersionConflictException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-load-stale"), Hash("load-3"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), lineId, Kg(1m), MeasurementKind.Measured, now), Loaded));
            Assert.Equal((3L, 4L), (stale.ExpectedVersion, stale.CurrentVersion));
        }

        Assert.Equal((1, 1, 0, 3, 2, 4L, 2), await run.CountsAsync(id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE Outcome = 2 AND ISJSON(RefusalJson) = 1"));
    }

    [SqlServerFact]
    public async Task A_same_key_race_stores_one_instruction_one_attempt_and_one_audit_and_company_B_sees_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var barrier = new Barrier(2);
        async Task<object> WriteAsync()
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return (await writer.Store.StoreInstructionAsync(run.Envelope("k-race"), Hash("race"), Day, () => Instruction(owners))).Outcome;
            }
            catch (LogisticsStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(WriteAsync), Task.Run(WriteAsync));
        Assert.Single(results, result => result is LogisticsWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == LogisticsStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'logistics.loading-instruction.created.v1'"));

        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            var retry = await a.Store.FindReplayAsync(run.Envelope("k-race", correlation: "SENTETIK-loser"), Hash("race"), LogisticsCommands.CreateV1);
            Assert.Equal((LogisticsWriteOutcome.Recorded.ToString(), run.Correlation), ("Recorded", retry!.OriginalCorrelationId));
            id = retry.Instruction!.Instruction.Id;
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindInstructionAsync(id));
            Assert.Empty((await b.Store.ListInstructionsAsync(10, null)).Items);
            Assert.Null(await b.Store.FindReplayAsync(run.Envelope("k-race"), Hash("race"), LogisticsCommands.CreateV1));
            Assert.Null(await b.Store.MutateInstructionAsync(run.Envelope("k-b-issue"), Hash("issue-b"), LogisticsCommands.IssueV1, id,
                (instruction, now) => instruction.Issue(2, now), Issued));
        }

        Assert.Equal((1, 1, 0, 1, 1, 2L, 1), await run.CountsAsync(id));

        // §8 for a MUTATION (FİNANS #350 P3-1): two issues under one key race at the save; one is Recorded, the loser loses at the
        // store and its retry under the same key reads the winner's stored transition — one attempt, one issued audit, version 3.
        var issueBarrier = new Barrier(2);
        async Task<object> IssueAsync()
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(issueBarrier));
            try
            {
                var result = await writer.Store.MutateInstructionAsync(run.Envelope("k-race-issue"), Hash("race-issue"), LogisticsCommands.IssueV1, id,
                    (instruction, now) => instruction.Issue(2, now), Issued);
                return result!.Outcome;
            }
            catch (LogisticsStoreException exception)
            {
                return exception.Code;
            }
            catch (LogisticsVersionConflictException)
            {
                return "version_conflict";
            }
        }

        var issues = await Task.WhenAll(Task.Run(IssueAsync), Task.Run(IssueAsync));
        Assert.Single(issues, result => result is LogisticsWriteOutcome.Recorded);
        Assert.Single(issues, result => result is string code && code == LogisticsStoreCodes.ConcurrentChange);
        await using (var a = run.Store(CompanyA))
        {
            var retry = await a.Store.FindReplayAsync(run.Envelope("k-race-issue", correlation: "SENTETIK-loser"), Hash("race-issue"), LogisticsCommands.IssueV1);
            Assert.Equal((LoadingInstructionStatus.Issued, 3L, run.Correlation),
                (retry!.Instruction!.Instruction.Status, retry.Instruction.Instruction.Version, retry.OriginalCorrelationId));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'logistics.loading-instruction.issued.v1'"));
        Assert.Equal((1, 1, 0, 2, 2, 3L, 2), await run.CountsAsync(id));
    }

    [SqlServerFact]
    public async Task A_resolved_sales_line_limit_caps_all_instructions_atomically_and_replay_never_recomputes_the_snapshot()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var first = InstructionForOrderLine(owners, orderLineId, 3m);
        var firstLine = Assert.Single(first.Lines);
        var firstLimit = AggregateLimit(firstLine.Id, owners, orderLineId, 5m, 7, 3);
        Guid firstId;

        await using (var a = run.Store(CompanyA))
        {
            var stored = await a.Store.StoreInstructionAsync(run.Envelope("k-aggregate-first"), Hash("aggregate-first"), Day,
                () => first, aggregateLimits: [firstLimit]);
            firstId = stored.Instruction.Instruction.Id;

            var replay = await a.Store.StoreInstructionAsync(run.Envelope("k-aggregate-first", correlation: "SENTETIK-replay"),
                Hash("aggregate-first"), Day, () => throw new InvalidOperationException("a replay never rebuilds"),
                aggregateLimits: [AggregateLimit(firstLine.Id, owners, orderLineId, 1m, 99, 99)]);
            Assert.Equal((LogisticsWriteOutcome.Replayed, firstId, run.Correlation),
                (replay.Outcome, replay.Instruction.Instruction.Id, replay.OriginalCorrelationId));
        }

        var second = InstructionForOrderLine(owners, orderLineId, 3m);
        var secondLine = Assert.Single(second.Lines);
        await using (var a = run.Store(CompanyA))
        {
            var refusal = await Assert.ThrowsAsync<LogisticsStoreException>(() =>
                a.Store.StoreInstructionAsync(run.Envelope("k-aggregate-second"), Hash("aggregate-second"), Day,
                    () => second, aggregateLimits: [AggregateLimit(secondLine.Id, owners, orderLineId, 5m, 7, 3)]));
            Assert.Equal(LogisticsStoreCodes.AggregateLimitExceeded, refusal.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions"));
        Assert.Equal(3m, await run.ScalarAsync<decimal>(
            "SELECT SUM(Instructed) FROM logistics.LoadingInstructionLines WHERE CompanyId = @company AND SourceOrderId = @order AND SourceOrderLineId = @line",
            ("@company", CompanyA), ("@order", owners.Order), ("@line", orderLineId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.created.v1'",
            ("@id", firstId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Concurrent_instructions_for_the_same_sales_line_never_exceed_the_resolved_limit()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();

        async Task<object> WriteAsync(string suffix)
        {
            var instruction = InstructionForOrderLine(owners, orderLineId, 3m);
            var line = Assert.Single(instruction.Lines);
            await using var writer = run.Store(CompanyA);
            try
            {
                return (await writer.Store.StoreInstructionAsync(run.Envelope($"k-aggregate-race-{suffix}"),
                    Hash($"aggregate-race-{suffix}"), Day, () => instruction,
                    aggregateLimits: [AggregateLimit(line.Id, owners, orderLineId, 5m, 7, 3)])).Outcome;
            }
            catch (LogisticsStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => WriteAsync("a")), Task.Run(() => WriteAsync("b")));

        Assert.Single(results, result => result is LogisticsWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code
            && code is LogisticsStoreCodes.AggregateLimitExceeded or LogisticsStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions"));
        Assert.Equal(3m, await run.ScalarAsync<decimal>(
            "SELECT SUM(Instructed) FROM logistics.LoadingInstructionLines WHERE CompanyId = @company AND SourceOrderId = @order AND SourceOrderLineId = @line",
            ("@company", CompanyA), ("@order", owners.Order), ("@line", orderLineId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'logistics.loading-instruction.created.v1'"));
    }

    [SqlServerFact]
    public async Task The_routes_answer_only_with_fresh_grants_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            id = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners))).Instruction.Instruction.Id;
        }

        // No grant: the fence refuses before the store (code-less 403); the instruction stays as it is.
        using (var ungranted = run.Factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
        {
            ungranted.DefaultRequestHeaders.Remove("Idempotency-Key");
            ungranted.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.SendAsync(Command($"{id:D}/issue", "SENTETIK-ungranted", "{\"expectedVersion\":\"2\"}"))).StatusCode);
        }

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.InstructionsCreate);
        using (var granted = run.Factory.CreateClientFor(reader, CompanyA))
        {
            granted.DefaultRequestHeaders.Remove("Idempotency-Key");
            granted.DefaultRequestHeaders.Remove("X-Correlation-Id");
            using var detail = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Contains("\"status\":\"Draft\"", await detail.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var list = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}?status=Draft");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Contains(id.ToString("D"), await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            // The create reaches the REAL Cari owner first: a synthetic customer is its NotFound → 409 account_state, nothing stored.
            using var create = await granted.SendAsync(Command(string.Empty, "SENTETIK-create", Create(owners)));
            Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
            Assert.Contains("\"code\":\"account_state\"", await create.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            // The issue is a real command: 200 after the transition; the same key again is 200 + the replay headers.
            using var issued = await granted.SendAsync(Command($"{id:D}/issue", "SENTETIK-issue", "{\"expectedVersion\":\"2\"}"));
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            Assert.Contains("\"status\":\"Issued\"", await issued.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var replay = await granted.SendAsync(Command($"{id:D}/issue", "SENTETIK-issue", "{\"expectedVersion\":\"2\"}"));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            using var stale = await granted.SendAsync(Command($"{id:D}/issue", "SENTETIK-issue-again", "{\"expectedVersion\":\"2\"}"));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.InstructionsCreate);
        using (var clientOfB = run.Factory.CreateClientFor(readerOfB, CompanyB))
        {
            clientOfB.DefaultRequestHeaders.Remove("Idempotency-Key");
            clientOfB.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.SendAsync(Command($"{id:D}/issue", "SENTETIK-b", "{\"expectedVersion\":\"3\"}"))).StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions"));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task Inventory_terminal_conflict_after_write_authority_loss_rolls_back_both_owners_and_records_no_refusal()
    {
        await using var run = await Run.StartAsync();
        var fixture = await SeedHostedShipmentAsync(run, "SYN-LOG5-TERMINAL-AUTH");
        var grant = await GrantRealizationAsync(run, $"SYN-LOG5-terminal-{Guid.NewGuid():N}");
        var revoker = new InventoryWriteRevoker(run.Factory, grant.RoleId);
        var reads = new HostedAllocationReads(fixture, revoker.RevokeAsync);
        await using var host = HostedShipmentHost(run, fixture, reads, grant.Subject);
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var request = CreateHostedShipmentRequest(fixture, expectedAllocationVersion: "2", "terminal-authority");
        var before = await HostedEffectsAsync(run, fixture, request);

        using var response = await client.SendAsync(request.Message);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(revoker.Revoked);
        Assert.Equal(before, await HostedEffectsAsync(run, fixture, request));
    }

    [SqlServerFact]
    public async Task Inventory_write_authority_revoked_after_staging_rolls_back_hosted_shipment_and_inventory_effects()
    {
        await using var run = await Run.StartAsync();
        var fixture = await SeedHostedShipmentAsync(run, "SYN-LOG5-STAGED-AUTH");
        var grant = await GrantRealizationAsync(run, $"SYN-LOG5-staged-{Guid.NewGuid():N}");
        var revoker = new InventoryWriteRevoker(run.Factory, grant.RoleId);
        var reads = new HostedAllocationReads(fixture);
        await using var host = HostedShipmentHost(run, fixture, reads, grant.Subject,
            new RevokeInventoryWriteAfterStage(revoker));
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var request = CreateHostedShipmentRequest(fixture, expectedAllocationVersion: "1", "staged-authority");
        var before = await HostedEffectsAsync(run, fixture, request);

        using var response = await client.SendAsync(request.Message);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(revoker.Revoked);
        Assert.Equal(before, await HostedEffectsAsync(run, fixture, request));
    }

    [SqlServerFact]
    public async Task Terminal_inventory_conflict_records_one_refusal_replays_and_leaves_no_physical_effect()
    {
        await using var run = await Run.StartAsync();
        var fixture = await SeedHostedShipmentAsync(run, "SYN-LOG5-TERMINAL-REPLAY");
        var grant = await GrantRealizationAsync(run, $"SYN-LOG5-terminal-replay-{Guid.NewGuid():N}");
        await using var host = HostedShipmentHost(
            run, fixture, new HostedAllocationReads(fixture), grant.Subject);
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var first = CreateHostedShipmentRequest(fixture, "2", "terminal-replay");
        var before = await HostedEffectsAsync(run, fixture, first);

        using var firstResponse = await client.SendAsync(first.Message);
        Assert.Equal(HttpStatusCode.Conflict, firstResponse.StatusCode);
        Assert.Contains(InventoryDefiniteShipmentCodesV1.AllocationVersionConflict,
            await firstResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var recorded = await HostedEffectsAsync(run, fixture, first);
        Assert.Equal(before, recorded with { ShipmentAttemptCount = before.ShipmentAttemptCount });
        Assert.Equal(before.ShipmentAttemptCount + 1, recorded.ShipmentAttemptCount);

        var replay = CreateHostedShipmentRequest(fixture, "2", "terminal-replay",
            first.ShipmentId, first.MovementId, correlationId: $"SYN-LOG5-replay-{Guid.NewGuid():N}");
        using var replayResponse = await client.SendAsync(replay.Message);
        Assert.Equal(HttpStatusCode.Conflict, replayResponse.StatusCode);
        Assert.Equal("true", Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(first.CorrelationId,
            Assert.Single(replayResponse.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Contains(InventoryDefiniteShipmentCodesV1.AllocationVersionConflict,
            await replayResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var changed = CreateHostedShipmentRequest(fixture, "2", "terminal-replay",
            first.ShipmentId, first.MovementId, quantity: "1",
            correlationId: $"SYN-LOG5-changed-{Guid.NewGuid():N}");
        using var changedResponse = await client.SendAsync(changed.Message);
        Assert.Equal(HttpStatusCode.Conflict, changedResponse.StatusCode);
        Assert.Contains(LogisticsProblemCodesV1.SameKeyPayloadMismatch,
            await changedResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(recorded, await HostedEffectsAsync(run, fixture, first));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static async Task<HostedShipmentFixture> SeedHostedShipmentAsync(Run run, string labelCode)
    {
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var locationId = await run.ScalarAsync<Guid>(
            "SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId = @company ORDER BY Id",
            ("@company", CompanyA));
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), CompanyA, owners.Party, owners.Account, T0);
        instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(owners.Order, orderLineId, 1),
            productId, KgUnit, allocationId, Kg(6m), T0);
        StoredLoadingInstruction stored;
        await using (var logistics = run.Store(CompanyA))
        {
            stored = (await logistics.Store.StoreInstructionAsync(run.Envelope($"seed-{labelCode}"), Hash(labelCode), Day,
                () => instruction)).Instruction;
            stored = (await logistics.Store.MutateInstructionAsync(run.Envelope($"issue-{labelCode}"), Hash($"issue-{labelCode}"),
                LogisticsCommands.IssueV1, instruction.Id, (value, now) => value.Issue(stored.Instruction.Version, now), Issued))!.Instruction;
            var lineId = Assert.Single(stored.Instruction.Lines).Id;
            stored = (await logistics.Store.MutateInstructionAsync(run.Envelope($"load-{labelCode}"), Hash($"load-{labelCode}"),
                LogisticsCommands.RecordLoadV1, instruction.Id,
                (value, now) => value.RecordLoad(stored.Instruction.Version, Guid.NewGuid(), lineId, Kg(5m), MeasurementKind.Measured, now),
                Loaded))!.Instruction;
        }

        var company = new CompanyContext();
        company.Set(CompanyA);
        await using (var inventory = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema)).Options, company))
        {
            inventory.Lots.Add(new LotRow
            {
                Id = lotId, CompanyId = CompanyA, ProductId = productId, Version = 1,
                CreatedAtUtc = T0, CreatedBy = run.Actor,
            });
            inventory.Labels.Add(new LabelRow
            {
                Id = labelId, CompanyId = CompanyA, Code = labelCode, ProductId = productId, LotId = lotId,
                RequiredDocumentsJson = "[]", Version = 1, CreatedAtUtc = T0, UpdatedAtUtc = T0,
            });
            inventory.Balances.Add(new BalanceRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, OwnerKind = (int)StockPartyKind.Company, OwnerId = CompanyA,
                CustodianKind = (int)StockPartyKind.Company, CustodianId = CompanyA, LocationId = locationId,
                LabelId = labelId, ProductId = productId, Block = (int)BlockState.Free, QuantityKg = 10m,
            });
            inventory.Allocations.Add(new AllocationRow
            {
                Id = allocationId, CompanyId = CompanyA, OwnerKind = (int)StockPartyKind.Company, OwnerId = CompanyA,
                CustodianKind = (int)StockPartyKind.Company, CustodianId = CompanyA, LocationId = locationId,
                LabelId = labelId, Block = (int)BlockState.Free,
                DemandKind = InventoryAllocationDemandKindsV1.SalesOrderLine, DemandId = owners.Order,
                DemandLineId = orderLineId, DemandProductId = productId, DemandProductVersion = "7",
                Reason = "Hosted Logistics authority rollback acceptance.", AllocatedKg = 5m, RemainingKg = 5m,
                Status = (int)StockAllocationStatus.Active, Version = 1, CreatedAtUtc = T0, UpdatedAtUtc = T0,
            });
            await inventory.SaveChangesAsync();
        }

        var line = Assert.Single(stored.Instruction.Lines);
        return new(stored.Instruction.Id, line.Id, stored.Instruction.Version, owners, orderLineId, productId,
            allocationId, labelId, locationId);
    }

    private static async Task<RealizationGrant> GrantRealizationAsync(Run run, string subject)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var actorId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Users.Add(new UserAccount(actorId, "Synthetic Logistics realization actor", null));
        db.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actorId,
            BootstrapIdentityOptions.Issuer, subject));
        var role = new RoleDefinition(roleId, $"LOG5-AUTH-{roleId:N}", "Synthetic Logistics realization role", true);
        role.AddPermission(ErpPermissions.LogisticsShipmentsRecord);
        role.AddPermission(ErpPermissions.InventoryPhysicalAllocationWrite);
        var assignment = new RoleAssignment(Guid.NewGuid(), actorId, roleId, CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, CompanyA);
        db.Roles.Add(role);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return new(subject, roleId);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> HostedShipmentHost(
        Run run,
        HostedShipmentFixture fixture,
        HostedAllocationReads reads,
        string bootstrapSubject,
        ILogisticsRealizationStageObserver? observer = null) =>
        run.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BootstrapIdentity:Users:7:UserId", bootstrapSubject);
            builder.UseSetting("BootstrapIdentity:Users:7:DisplayName", "Synthetic Logistics realization actor");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISalesOrderLineReferencePortV1>();
                services.AddSingleton<ISalesOrderLineReferencePortV1>(new HostedSalesLine(fixture));
                services.RemoveAll<IInventoryPhysicalAllocationReadsV1>();
                services.AddSingleton<IInventoryPhysicalAllocationReadsV1>(reads);
                if (observer is not null)
                {
                    services.AddSingleton(observer);
                }
            });
        });

    private static HttpClient HostedClientFor(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        string subject,
        Guid companyId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", subject);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString("D"));
        return client;
    }

    private static HostedShipmentRequest CreateHostedShipmentRequest(
        HostedShipmentFixture fixture,
        string expectedAllocationVersion,
        string suffix,
        Guid? shipmentId = null,
        Guid? movementId = null,
        string quantity = "2",
        string? correlationId = null)
    {
        var shipment = shipmentId ?? Guid.NewGuid();
        var movement = movementId ?? Guid.NewGuid();
        var correlation = correlationId ?? $"SYN-LOG5-{suffix}-{Guid.NewGuid():N}";
        var message = new HttpRequestMessage(HttpMethod.Post,
            $"{LogisticsEndpoints.InstructionsPath}/{fixture.InstructionId:D}/lines/{fixture.LineId:D}/shipments")
        {
            Content = JsonContent.Create(new RecordShipmentRequest(
                fixture.InstructionVersion.ToString(), shipment, fixture.AllocationId, expectedAllocationVersion,
                "1", movement, quantity, T0.AddMinutes(10))),
        };
        message.Headers.Add("Idempotency-Key", $"SYN-LOG5-{suffix}");
        message.Headers.Add("X-Correlation-Id", correlation);
        return new(message, shipment, movement, correlation);
    }

    private static async Task<HostedShipmentEffects> HostedEffectsAsync(
        Run run,
        HostedShipmentFixture fixture,
        HostedShipmentRequest request) => new(
        await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id",
            ("@id", fixture.InstructionId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId = @id",
            ("@id", fixture.InstructionId)),
        // Every shipment attempt in this run's disposable database: a recorded one names the instruction, a recorded
        // terminal refusal never does (CK_LoadingAttempts_Outcome, ADR-011 §7: Outcome = 2 => InstructionId IS NULL).
        // The seed stores only create/issue/load attempts, so this counts exactly the realization's shipment attempts.
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE CommandName = @command",
            ("@command", LogisticsCommands.RecordShipmentV1)),
        await run.ScalarAsync<decimal>("SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id",
            ("@id", fixture.AllocationId)),
        await run.ScalarAsync<long>("SELECT Version FROM inventory.Allocations WHERE Id = @id",
            ("@id", fixture.AllocationId)),
        await run.ScalarAsync<decimal>("SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id",
            ("@id", fixture.LabelId)),
        await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id",
            ("@id", fixture.LabelId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id",
            ("@id", request.MovementId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id",
            ("@id", request.ShipmentId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE CorrelationId = @correlation",
            ("@correlation", request.CorrelationId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", request.CorrelationId)));

    private sealed class InventoryWriteRevoker(SqlServerRelationalApiFactory factory, Guid roleId)
    {
        private int revoked;
        public bool Revoked => Volatile.Read(ref revoked) != 0;

        public async Task RevokeAsync()
        {
            if (Interlocked.Exchange(ref revoked, 1) != 0) return;
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var permission = await db.RolePermissions.SingleAsync(value => value.RoleId == roleId
                && value.PermissionCode == ErpPermissions.InventoryPhysicalAllocationWrite);
            db.RolePermissions.Remove(permission);
            await db.SaveChangesAsync();
        }
    }

    private sealed class RevokeInventoryWriteAfterStage(InventoryWriteRevoker revoker)
        : ILogisticsRealizationStageObserver
    {
        public Task ObserveAsync(LogisticsRealizationStage stage, CancellationToken cancellationToken)
        {
            Assert.Equal(LogisticsRealizationStage.InventoryStaged, stage);
            return revoker.RevokeAsync();
        }
    }

    private sealed class HostedAllocationReads(HostedShipmentFixture fixture, Func<Task>? afterRead = null)
        : IInventoryPhysicalAllocationReadsV1
    {
        private int read;

        public Task<InventoryLabelPhysicalAllocationsV1> ReadLabelAsync(
            Guid labelId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<InventoryPhysicalAllocationReadsV1> SearchAsync(
            InventoryPhysicalAllocationReadQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal((fixture.Owners.Order, fixture.OrderLineId), (query.SalesOrderId, query.SalesOrderLineId));
            if (afterRead is not null && Interlocked.Exchange(ref read, 1) == 0)
            {
                await afterRead();
            }

            var allocation = new InventoryPhysicalAllocationViewV1(
                fixture.AllocationId, CompanyA, "1", "Active", fixture.LabelId, fixture.ProductId,
                InventoryAllocationDemandKindsV1.SalesOrderLine, fixture.Owners.Order, fixture.OrderLineId,
                "7", "5", "5", "Hosted Logistics authority rollback acceptance.", null, T0, T0);
            return new([new InventoryPhysicalAllocationReadItemV1(allocation, "1")], false);
        }
    }

    private sealed class HostedSalesLine(HostedShipmentFixture fixture) : ISalesOrderLineReferencePortV1
    {
        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(
            Guid orderId,
            Guid lineId,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.Approved,
                    new SalesOrderLineReferenceV1(CompanyA, fixture.Owners.Order, fixture.OrderLineId, 1,
                        fixture.Owners.Party, fixture.Owners.Account, fixture.ProductId, "7", KgUnit, "6", 1, 4), null));

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(
            int pageSize,
            string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record RealizationGrant(string Subject, Guid RoleId);
    private sealed record HostedShipmentFixture(
        Guid InstructionId,
        Guid LineId,
        long InstructionVersion,
        Owners Owners,
        Guid OrderLineId,
        Guid ProductId,
        Guid AllocationId,
        Guid LabelId,
        Guid LocationId);
    private sealed record HostedShipmentRequest(
        HttpRequestMessage Message,
        Guid ShipmentId,
        Guid MovementId,
        string CorrelationId);
    private sealed record HostedShipmentEffects(
        long InstructionVersion,
        int ShareCount,
        int ShipmentAttemptCount,
        decimal AllocationRemainingKg,
        long AllocationVersion,
        decimal BalanceQuantityKg,
        long LabelVersion,
        int MovementCount,
        int CommandCount,
        int OutboxCount,
        int AuditCount);

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A Draft instruction of the company towards SYNTHETIC owner identities, its lines 6 kg each (the §13 example's shape).</summary>
    private static LoadingInstruction Instruction(Owners owners, int lines = 1)
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), owners.Company, owners.Party, owners.Account, T0);
        for (var i = 0; i < lines; i++)
        {
            instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(owners.Order, Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(6m), T0);
        }

        return instruction;
    }

    private static LoadingInstruction InstructionForOrderLine(Owners owners, Guid orderLineId, decimal quantity)
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), owners.Company, owners.Party, owners.Account, T0);
        instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(owners.Order, orderLineId, 0),
            Guid.NewGuid(), KgUnit, null, Kg(quantity), T0);
        return instruction;
    }

    private static LogisticsOrderLineAggregateLimit AggregateLimit(Guid instructionLineId, Owners owners, Guid orderLineId,
        decimal maximum, long salesOrderVersion, long sourceVersion) =>
        new(instructionLineId, owners.Company, owners.Order, orderLineId, KgUnit, salesOrderVersion, maximum,
            maximum.ToString(System.Globalization.CultureInfo.InvariantCulture), "Sales", orderLineId.ToString("D"),
            sourceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), Day);

    private static string Create(Owners owners) =>
        $$"""{"customerPartyId":"{{owners.Party:D}}","companyAccountId":"{{owners.Account:D}}","effectiveOn":"2026-09-19","lines":[{"lineId":"{{Guid.NewGuid():D}}","sourceOrderId":"{{owners.Order:D}}","sourceOrderLineId":"{{Guid.NewGuid():D}}","unitId":"{{KgUnit:D}}","quantity":"6"}]}""";

    private static HttpRequestMessage Command(string segment, string idempotencyKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, segment.Length == 0 ? LogisticsEndpoints.InstructionsPath : $"{LogisticsEndpoints.InstructionsPath}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return request;
    }

    private sealed record Owners(Guid Company, Guid Party, Guid Account, Guid Order)
    {
        public static Owners For(Guid company) => new(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StoreScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public ILogisticsInstructionStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = Guid.NewGuid().ToString("D");
        public string Correlation { get; } = $"log4-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // The host applies the module's migration after Organization's; applied again here idempotently.
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new LogisticsDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public LogisticsWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], correlation ?? Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public StoreScope Store(Guid company, IInterceptor? extra = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var builder = new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (extra is not null)
            {
                builder.AddInterceptors(extra);
            }

            var dbContext = new LogisticsDbContext(builder.Options, companyContext);
            return new StoreScope(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Logistics user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"LOG-4A-{roleId:N}", "Synthetic Logistics test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
        }

        /// <summary>(instructions, lines, shares, attempts, audit events of the instruction, version, recorded attempts) as the real SQL holds them.</summary>
        public async Task<(int Instructions, int Lines, int Shares, int Attempts, int AuditEvents, long Version, int Recorded)> CountsAsync(Guid id) =>
            (await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE InstructionId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts"),
                await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", id.ToString("D"))),
                await ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE Outcome = 1 AND InstructionId = @id", ("@id", id)));

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static DbContextOptions<LogisticsDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .Options;
    }
}
