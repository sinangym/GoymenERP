using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using LogisticsQuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D30 / SAT-D168 / SAT-D213 / SAT-D216 against a REAL SQL Server and the REAL host composition (Sales store, Identity grants,
/// the real approved Settings GDC tolerance 1 / 2 / 3 %, the audit pipeline). The REAL Logistics owner answers shipped totals
/// without naming the shipper, so through it the approval is the key's recorded fail-closed 409
/// (<c>sales_over_delivery_shipper_unverified</c>) and nothing is approved. Where a test needs the shipper named, the host's
/// delivery port is replaced by a SYNTHETIC owner (the only substitution; every other owner is real): an approval-band
/// over-delivery is then approved in ONE save (approval row + header version + attempt + audit with SourceChannel Web), the
/// status reads "Tamamlandı + onaylı fazla teslim", the same key replays with no second effect, another body under the key is
/// 409, a larger total awaits a new approval, the shipper cannot approve, another company gets 404 and a reader without the name
/// 403; the migration refuses to roll back over a recorded approval. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderOverDeliveryApprovalSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    // A business day on which GDC's (company A) approved SAT-D5 tolerance (1 / 2 / 3 %) is in force (SAT-D14).
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a01");
    private static readonly Guid MoneyProfile = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a02");
    private static readonly Guid KgProfile = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a03");
    private static readonly Guid Product = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a04");
    private const string Shipper = "SENTETIK-shipper";

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit OverDeliveryApproved =
        new("sales.order-draft.over-delivery-approved.v1", "ApproveOverDelivery", "SENTETIK fazla teslim onaylandı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");

    [SqlServerFact]
    public async Task An_approval_band_over_delivery_is_approved_through_the_real_route_in_one_save_and_replays_on_real_sql()
    {
        var owner = new SyntheticDeliveries();
        await using var run = await Run.StartAsync(owner);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        // 102.5 of 100 kg: 2.5 % over — GDC's approval band (2 % ≤ x < 3 %); the synthetic owner names who shipped it.
        owner.Set(CompanyA, draft.Id, new SalesOrderLineDeliveryV1(line.Id, KgUnit, 102.5m, 0m, [Shipper]));
        var (subject, userId) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersOverDeliveryApprove);
        using var client = run.Client(subject, CompanyA);
        var statusPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status";
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/over-delivery-approval";

        var awaiting = await run.JsonAsync(client, statusPath);
        Assert.Equal(("AwaitingApproval", "Approval", false), LineOf(awaiting));

        var before = await run.EffectsAsync(draft.Id);
        using (var approved = await client.SendAsync(Approve(path, "SENTETIK-over-1", "4", line.Id, "102.5")))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            Assert.False(approved.Headers.Contains("Idempotency-Replayed"));
            var summary = (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft");
            Assert.Equal(("5", "Approved", "Draft"), (summary.GetProperty("version").GetString(),
                summary.GetProperty("approvalState").GetString(), summary.GetProperty("state").GetString()));
        }

        // ONE save as SQL Server holds it: the approval row, the header version, the attempt and the audit event (Web).
        Assert.Equal(before with { SalesAttempts = before.SalesAttempts + 1, AuditEvents = before.AuditEvents + 1, Approvals = 1, DraftVersion = 5 },
            await run.EffectsAsync(draft.Id));
        Assert.Equal(("100", "102.5", userId.ToString("D"), 5L, 1, 0), (
            await run.ScalarAsync<string>("SELECT [Ordered] FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [ApprovedShipped] FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [ApprovedBy] FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalNo] FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [BasisRevision] FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.over-delivery-approved.v1' AND [EntityId] = @id "
            + "AND [SourceChannel] = N'Web' AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5 AND [PermissionCode] = N'sales.orders.over-delivery.approve'",
            ("@id", draft.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.approve-over-delivery.v1' AND [Outcome] = 1 AND [DraftId] = @id",
            ("@id", draft.Id)));
        // The line keeps its ordered quantity; nothing of the approval axis moved.
        Assert.Equal(("100", 3), (
            await run.ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));

        // SAT-D30: "Tamamlandı + onaylı fazla teslim" — the stored approval fed to the calculator through the store's restore.
        Assert.Equal(("Completed", "Approval", true), LineOf(await run.JsonAsync(client, statusPath)));

        // The same key and content: replayed, no second row, attempt or event.
        var afterApproval = await run.EffectsAsync(draft.Id);
        using (var replayed = await client.SendAsync(Approve(path, "SENTETIK-over-1", "4", line.Id, "102.5")))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        // The same key with another body: one 409, nothing written.
        using (var mismatch = await client.SendAsync(Approve(path, "SENTETIK-over-1", "4", line.Id, "102.6")))
        {
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.Equal("same_key_payload_mismatch", (await mismatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(afterApproval, await run.EffectsAsync(draft.Id));

        // SAT-D213: a later shipment makes 102.8 — the approval no longer covers it; the line awaits a new approval.
        owner.Set(CompanyA, draft.Id, new SalesOrderLineDeliveryV1(line.Id, KgUnit, 102.8m, 0m, [Shipper]));
        Assert.Equal(("AwaitingApproval", "Approval", false), LineOf(await run.JsonAsync(client, statusPath)));
    }

    [SqlServerFact]
    public async Task The_real_logistics_owner_does_not_name_the_shipper_so_the_approval_is_refused_fail_closed_on_real_sql()
    {
        await using var run = await Run.StartAsync(owner: null);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        // Logistics really instructs, loads and ships 102.5 kg of the 100 kg line (through its own store).
        await run.ShippedInstructionAsync(CompanyA, draft, "k", 102.5m);
        var (subject, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersOverDeliveryApprove);
        using var client = run.Client(subject, CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/over-delivery-approval";
        Assert.Equal(("AwaitingApproval", "Approval", false), LineOf(await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status")));

        using (var refused = await client.SendAsync(Approve(path, "SENTETIK-real-1", "4", draft.Lines[0].Id, "102.5")))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("sales_over_delivery_shipper_unverified", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var effects = await run.EffectsAsync(draft.Id);
        Assert.Equal((0, 4L), (effects.Approvals, effects.DraftVersion));
        // The refusal is the key's recorded answer: the same key replays it.
        using (var replayed = await client.SendAsync(Approve(path, "SENTETIK-real-1", "4", draft.Lines[0].Id, "102.5")))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(effects, await run.EffectsAsync(draft.Id));
    }

    [SqlServerFact]
    public async Task Another_company_gets_404_a_reader_without_the_name_gets_403_and_the_shipper_cannot_approve_on_real_sql()
    {
        var owner = new SyntheticDeliveries();
        await using var run = await Run.StartAsync(owner);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/over-delivery-approval";

        var (subjectOfB, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersOverDeliveryApprove);
        using (var clientOfB = run.Client(subjectOfB, CompanyB))
        using (var foreign = await clientOfB.SendAsync(Approve(path, "SENTETIK-b-1", "4", line.Id, "102.5")))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        // Company A: a reader holding the draft read (and the force-close name) only — the approval is 403.
        var (reader, userId) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersForceClose);
        using (var readOnly = run.Client(reader, CompanyA))
        using (var forbidden = await readOnly.SendAsync(Approve(path, "SENTETIK-a-read", "4", line.Id, "102.5")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // SAT-D216: the approver is the person the owner names as the shipper — refused, recorded, nothing approved.
        owner.Set(CompanyA, draft.Id, new SalesOrderLineDeliveryV1(line.Id, KgUnit, 102.5m, 0m, [userId.ToString("D")]));
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.OrdersOverDeliveryApprove);
        using var client = run.Client(reader, CompanyA);
        using (var self = await client.SendAsync(Approve(path, "SENTETIK-self", "4", line.Id, "102.5")))
        {
            Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
            Assert.Equal("sales_over_delivery_self_approval", (await self.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var effects = await run.EffectsAsync(draft.Id);
        Assert.Equal((0, 4L), (effects.Approvals, effects.DraftVersion));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.approve-over-delivery.v1' AND [Outcome] = 1"));
    }

    [SqlServerFact]
    public async Task The_over_delivery_approval_migrations_Down_passes_with_no_approval_and_refuses_while_an_approval_exists()
    {
        await using var run = await Run.StartAsync(owner: null);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var guard = Assert.IsType<SqlOperation>(new AddDraftOverDeliveryApprovals().DownOperations[0]).Sql;
        // No approval: the guard passes (an empty table rolls back freely).
        await run.ScalarAsync<int>(guard + " SELECT 1");

        // One approval through the real Sales store: the domain's input as the command builds it.
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.ApproveOverDeliveryAsync(run.SalesEnvelope("k-over", "SENTETIK-approver-2"), Hash("k-over"),
                SalesDraftCommands.ApproveOverDeliveryV1, draft.Id,
                (d, input, now) => d.ApproveOverDelivery(4, "SENTETIK-approver-2", input, now), OverDeliveryApproved,
                (_, _) => Task.FromResult(new OverDeliveryApprovalInput(line.Id, 0, "100", 102.5m, [Shipper])));
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"));
        Assert.Equal(51213, error.Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("_AddDraftOverDeliveryApprovals", applied[^1], StringComparison.Ordinal);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[^2]));
        Assert.Equal(51213, rollback.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id)));

        // The rebuilt order carries its approval (the store's restore through the domain, counted by the version floor).
        await using (var reader = run.SalesStore(CompanyA))
        {
            var reread = await reader.Store.FindDraftAsync(draft.Id);
            var approval = Assert.Single(reread!.OverDeliveryApprovals);
            Assert.Equal((5L, "102.5", "SENTETIK-approver-2"), (reread.Version, approval.ApprovedShipped, approval.ApprovedBy));
        }

        // The database refuses a second approval under the same number, one under another company and one that breaks the check.
        const string insert = "INSERT INTO [sales].[DraftOverDeliveryApprovals] ([CompanyId],[DraftId],[LineId],[ApprovalNo],[LineNo],[BasisRevision],"
            + "[Ordered],[ApprovedShipped],[ApprovedBy],[ApprovedAtUtc],[DraftVersion],[CorrelationId]) "
            + "VALUES (@company, @id, @line, @no, 1, 0, N'100', N'103', N'x', SYSDATETIMEOFFSET(), @version, N'x'); SELECT 1";
        Assert.Equal(2627, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyA), ("@id", draft.Id), ("@line", line.Id), ("@no", 1), ("@version", 6L)))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyB), ("@id", draft.Id), ("@line", line.Id), ("@no", 2), ("@version", 6L)))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyA), ("@id", draft.Id), ("@line", line.Id), ("@no", 2), ("@version", 4L)))).Number);
        Assert.Equal(2601, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyA), ("@id", draft.Id), ("@line", line.Id), ("@no", 2), ("@version", 5L)))).Number);
    }

    [SqlServerFact]
    public async Task Two_concurrent_approvals_of_the_same_line_leave_one_record_and_the_loser_is_a_concurrent_change_on_real_sql()
    {
        await using var run = await Run.StartAsync(owner: null);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var meet = Meeting();
        // Both writers read the order at version 4 and meet before either saves: exactly one save can win.
        async Task<SalesDraftStateResult?> ApproveAsync(string key, string actor)
        {
            await using var scope = run.SalesStore(CompanyA);
            return await scope.Store.ApproveOverDeliveryAsync(run.SalesEnvelope(key, actor), Hash(key), SalesDraftCommands.ApproveOverDeliveryV1,
                draft.Id, (d, input, now) => d.ApproveOverDelivery(4, actor, input, now), OverDeliveryApproved,
                async (_, _) =>
                {
                    await meet();
                    return new OverDeliveryApprovalInput(line.Id, 0, "100", 102.5m, [Shipper]);
                });
        }

        var outcomes = await Task.WhenAll(Outcome(ApproveAsync("k-a", "SENTETIK-approver-a")), Outcome(ApproveAsync("k-b", "SENTETIK-approver-b")));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Equal(SalesDraftStoreCodes.ConcurrentChange,
            Assert.IsType<SalesDraftStoreException>(Assert.Single(outcomes, outcome => outcome is not null)).Code);
        var effects = await run.EffectsAsync(draft.Id);
        Assert.Equal((1, 5L), (effects.Approvals, effects.DraftVersion));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.approve-over-delivery.v1' AND [Outcome] = 1"));
    }

    [SqlServerFact]
    public async Task An_approval_racing_a_quantity_revision_leaves_exactly_one_of_them_on_real_sql()
    {
        await using var run = await Run.StartAsync(owner: null);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var meet = Meeting();
        async Task<SalesDraftStateResult?> ApproveAsync()
        {
            await using var scope = run.SalesStore(CompanyA);
            return await scope.Store.ApproveOverDeliveryAsync(run.SalesEnvelope("k-over", "SENTETIK-approver-2"), Hash("k-over"),
                SalesDraftCommands.ApproveOverDeliveryV1, draft.Id, (d, input, now) => d.ApproveOverDelivery(4, "SENTETIK-approver-2", input, now),
                OverDeliveryApproved, async (_, _) =>
                {
                    await meet();
                    return new OverDeliveryApprovalInput(line.Id, 0, "100", 102.5m, [Shipper]);
                });
        }

        async Task<SalesDraftStateResult?> ReviseAsync()
        {
            await using var scope = run.SalesStore(CompanyA);
            return await scope.Store.ReviseDraftQuantityAsync(run.SalesEnvelope("k-qty", "SENTETIK-reviser"), Hash("k-qty"),
                SalesDraftCommands.ReviseQuantityV1, draft.Id,
                (d, now) => d.ReviseQuantities(4, [new LineQuantityInput(line.Id, "101", 101m)], "SENTETIK-reviser", "SENTETIK neden", now),
                QuantityRevised, (_, _) => meet());
        }

        var outcomes = await Task.WhenAll(Outcome(ApproveAsync()), Outcome(ReviseAsync()));

        // One of them committed at version 5; the other lost the row-version race and wrote nothing.
        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Equal(SalesDraftStoreCodes.ConcurrentChange,
            Assert.IsType<SalesDraftStoreException>(Assert.Single(outcomes, outcome => outcome is not null)).Code);
        var effects = await run.EffectsAsync(draft.Id);
        var revisions = await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id));
        Assert.Equal(5L, effects.DraftVersion);
        Assert.True((effects.Approvals, revisions) is (1, 0) or (0, 1), $"approvals={effects.Approvals}, revision={revisions}");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static (string? State, string? Band, bool Approved) LineOf(JsonElement status)
    {
        var line = status.GetProperty("realization").GetProperty("lines")[0];
        return (line.GetProperty("state").GetString(), line.GetProperty("band").GetString(), line.GetProperty("approvedOverDelivery").GetBoolean());
    }

    private static HttpRequestMessage Approve(string path, string key, string expectedVersion, Guid lineId, string shipped)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new SalesDraftOverDeliveryApprovalRequest(expectedVersion, lineId, shipped),
                SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static readonly SalesDraftStateAudit QuantityRevised =
        new("sales.order-draft.quantity-revised.v1", "ReviseQuantities", "SENTETIK miktar revize edildi");

    /// <summary>A two-party meeting point: each call waits until the other has arrived (at most 30 s), so both writers read first.</summary>
    private static Func<Task> Meeting()
    {
        var arrived = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return async () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                both.SetResult();
            }

            await both.Task.WaitAsync(TimeSpan.FromSeconds(30));
        };
    }

    /// <summary>The exception a write ended with, or null when it committed.</summary>
    private static async Task<Exception?> Outcome(Task<SalesDraftStateResult?> write)
    {
        try
        {
            await write;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity) from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>(sales attempts, audit events, over-delivery approvals, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int Approvals, long DraftVersion);

    /// <summary>
    /// A SYNTHETIC delivery owner that NAMES the shipper (the real Logistics owner does not: SAT-D209). It answers only under the
    /// server-resolved company, creates nothing, and an order it holds nothing for is an empty list (the port's rules).
    /// </summary>
    private sealed class SyntheticDeliveries
    {
        private readonly ConcurrentDictionary<(Guid Company, Guid Order), SalesOrderLineDeliveryV1[]> lines = new();

        public void Set(Guid company, Guid order, params SalesOrderLineDeliveryV1[] entries) => lines[(company, order)] = entries;

        public SalesOrderDeliveryReadAnswerV1 Answer(Guid company, Guid order) =>
            new(company, order, lines.TryGetValue((company, order), out var entries) ? entries : []);
    }

    private sealed class SyntheticDeliveryPort(SyntheticDeliveries owner, ICompanyContext company) : ISalesOrderDeliveryReadPortV1
    {
        public Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(owner.Answer(company.CompanyId, orderId));
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class LogisticsScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public LogisticsStore Store => store;
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
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-sat-d30-{Guid.NewGuid():N}";

        /// <summary>The real host; with <paramref name="owner"/>, its delivery port is the synthetic owner that names the shipper.</summary>
        public static async Task<Run> StartAsync(SyntheticDeliveries? owner)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = owner is null
                ? new SqlServerRelationalApiFactory(baseConnectionString)
                : new SqlServerRelationalApiFactory(baseConnectionString, services =>
                {
                    services.RemoveAll<ISalesOrderDeliveryReadPortV1>();
                    services.AddScoped<ISalesOrderDeliveryReadPortV1>(provider =>
                        new SyntheticDeliveryPort(owner, provider.GetRequiredService<ICompanyContext>()));
                });
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored draft of the company on the business day, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        /// <summary>An instruction for the draft's one line, issued, loaded and shipped <paramref name="shipped"/> kg through the real Logistics store.</summary>
        public async Task ShippedInstructionAsync(Guid company, SalesOrderDraft draft, string keyPrefix, decimal shipped)
        {
            var orderLine = Assert.Single(draft.Lines);
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), draft.Header.BusinessDate, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(shipped), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loaded = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(shipped),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loaded.Version, Guid.NewGuid(), lineId, Kg(shipped), now),
                Shipped);
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draftId)),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key) =>
            new(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public LogisticsScope LogisticsStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new LogisticsScope(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company (company-wide scope).</summary>
        public async Task<(string Subject, Guid UserId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales over-delivery user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SAT-D30-{roleId:N}", "Synthetic Sales over-delivery role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return (developmentUser, actor);
        }

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
    }
}
