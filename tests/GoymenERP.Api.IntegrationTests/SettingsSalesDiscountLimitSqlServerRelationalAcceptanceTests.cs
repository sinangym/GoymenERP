using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D61…SAT-D70 against a REAL SQL Server and the REAL host composition (routes, authorization, Settings migrations,
/// Identity directory, audit interceptor): limit groups and their limits (fixed and linked + extra points), the person →
/// group bindings written to every company of the actor in one transaction, and the Settings reads the Sales limit check
/// is composed with (the host's <see cref="SalesDiscountLimitGroupHost"/> and <see cref="SettingsSalesPolicyProviderV1"/>).
/// Every grant is a fresh synthetic role; every value is SENTETIK.
/// </summary>
public sealed class SettingsSalesDiscountLimitSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid CompanyC = TestCompanyIds.CompanyC;
    private const string Permission = SettingsPermissionManifestV1.SalesDiscountLimitsManage;
    private const string Groups = SettingsEndpoints.SalesDiscountLimitsPath + "/groups";

    [SqlServerFact]
    public async Task Groups_and_limits_lifecycle_with_linked_limits_replay_conflicts_isolation_and_audit_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (editorSubject, editor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Permission);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, "sales.drafts.read");
        using var client = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);

        // Without the name: 403 on the read and on a command, and nothing is written.
        using (var forbidden = await viewer.GetAsync(Groups))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, Groups, "k-forbidden",
                   new CreateSalesDiscountLimitGroupRequestV1("SATIS-DESTEK", "Satış Destek", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitGroups]"));

        // Reason required (not stored as the key's answer).
        using (var noReason = await client.SendAsync(Json(HttpMethod.Post, Groups, "k-create-1",
                   new CreateSalesDiscountLimitGroupRequestV1("satis-destek", "Satış Destek", "   "))))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, SalesDiscountLimitProblemCodesV1.ReasonRequired);
        }

        var seller = await run.CreateGroupAsync(client, "k-create-1", "satis-destek", "Satış Destek");
        var manager = await run.CreateGroupAsync(client, "k-create-2", "MUDUR", "Müdür");

        // The same key and body replays the stored 201 with the same id; another body under it is 409.
        using (var replay = await client.SendAsync(Json(HttpMethod.Post, Groups, "k-create-1",
                   new CreateSalesDiscountLimitGroupRequestV1("satis-destek", "Satış Destek", "SENTETIK kuruluş"))))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(seller, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        using (var other = await client.SendAsync(Json(HttpMethod.Post, Groups, "k-create-1",
                   new CreateSalesDiscountLimitGroupRequestV1("BASKA", "Başka", "SENTETIK kuruluş"))))
        {
            await AssertProblemAsync(other, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.IdempotencyConflict);
        }

        using (var taken = await client.SendAsync(Json(HttpMethod.Post, Groups, "k-create-3",
                   new CreateSalesDiscountLimitGroupRequestV1("SATIS-DESTEK", "Yine", "SENTETIK"))))
        {
            await AssertProblemAsync(taken, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.GroupCodeTaken);
        }

        // SAT-D61/D62: seller 35 %, manager = seller + 3 points → 38 % in force today.
        await run.SetLimitAsync(client, "k-limit-1", seller, "1", new("Fixed", "35", null, null));
        await run.SetLimitAsync(client, "k-limit-2", manager, "1", new("Linked", null, seller, "3"));
        var list = await run.JsonAsync(client, Groups);
        Assert.Equal(CompanyA, list.GetProperty("companyId").GetGuid());
        var byCode = list.GetProperty("groups").EnumerateArray().ToDictionary(group => group.GetProperty("code").GetString()!);
        Assert.Equal(("Defined", "35", "2"), (byCode["SATIS-DESTEK"].GetProperty("effectiveLimit").GetProperty("state").GetString(),
            byCode["SATIS-DESTEK"].GetProperty("effectiveLimit").GetProperty("percent").GetString(),
            byCode["SATIS-DESTEK"].GetProperty("version").GetString()));
        Assert.Equal(("Defined", "38", "Linked", seller), (
            byCode["MUDUR"].GetProperty("effectiveLimit").GetProperty("state").GetString(),
            byCode["MUDUR"].GetProperty("effectiveLimit").GetProperty("percent").GetString(),
            byCode["MUDUR"].GetProperty("limit").GetProperty("kind").GetString(),
            byCode["MUDUR"].GetProperty("limit").GetProperty("linkedGroupId").GetGuid()));

        // The capped computation (99 + 3 → 100) and the policy port the Sales check reads.
        await run.SetLimitAsync(client, "k-limit-3", seller, "2", new("Fixed", "99", null, null));
        await using (var port = run.PolicyPort(CompanyA))
        {
            var today = run.Today;
            var managerLimit = await port.Value.GetSalesDiscountLimitAsync(manager, today);
            Assert.Equal((100m, true, CompanyA, manager), (managerLimit.MaximumDiscountPercent, managerLimit.IsActive,
                managerLimit.CompanyId, managerLimit.LimitGroupId));
            Assert.Equal(1, managerLimit.Profile.Version);
            Assert.Equal(today, managerLimit.EffectiveFrom);
            Assert.Null(managerLimit.EffectiveThrough);
            var sellerLimit = await port.Value.GetSalesDiscountLimitAsync(seller, today);
            Assert.Equal((99m, 2L), (sellerLimit.MaximumDiscountPercent, sellerLimit.Profile.Version));
            // Before the first record nothing is in force; an unknown group is the catalog's "not found" as before.
            Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetSalesDiscountLimitAsync(seller, today.AddDays(-1)))).Code);
            Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetSalesDiscountLimitAsync(Guid.NewGuid(), today))).Code);
        }

        // A stale version is the key's stored 409; the same limit again is a no-op (no record, no version move).
        using (var stale = await client.SendAsync(Json(HttpMethod.Put, $"{Groups}/{seller:D}/limit", "k-limit-stale",
                   new SetSalesDiscountLimitRequestV1("2", "Fixed", "10", null, null, "SENTETIK"))))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.VersionConflict);
        }

        await run.SetLimitAsync(client, "k-limit-same", seller, "3", new("Fixed", "99", null, null));
        Assert.Equal((2L, 3L), (
            await run.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM [settings].[SalesDiscountLimitVersions] WHERE [LimitGroupId] = @id",
                ("@id", seller)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [settings].[SalesDiscountLimitGroups] WHERE [Id] = @id", ("@id", seller))));

        // EK10: a cycle (seller → manager → seller) and a self link are refused.
        using (var cycle = await client.SendAsync(Json(HttpMethod.Put, $"{Groups}/{seller:D}/limit", "k-cycle",
                   new SetSalesDiscountLimitRequestV1("3", "Linked", null, manager, "1", "SENTETIK"))))
        {
            await AssertProblemAsync(cycle, HttpStatusCode.UnprocessableEntity, SalesDiscountLimitProblemCodesV1.LinkCycle);
        }

        using (var self = await client.SendAsync(Json(HttpMethod.Put, $"{Groups}/{seller:D}/limit", "k-self",
                   new SetSalesDiscountLimitRequestV1("3", "Linked", null, seller, "1", "SENTETIK"))))
        {
            await AssertProblemAsync(self, HttpStatusCode.UnprocessableEntity, SalesDiscountLimitProblemCodesV1.LinkCycle);
        }

        // Company isolation: from company B, A's group is the code-less 404, and linking to it is 422.
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Permission);
        using var clientB = run.Client(editorBSubject, CompanyB);
        using (var foreign = await clientB.SendAsync(Json(HttpMethod.Put, $"{Groups}/{seller:D}/name", "k-foreign",
                   new RenameSalesDiscountLimitGroupRequestV1("3", "Ele geçir", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        var groupB = await run.CreateGroupAsync(clientB, "k-b-1", "SATIS-DESTEK", "Satış Destek");
        using (var foreignLink = await clientB.SendAsync(Json(HttpMethod.Put, $"{Groups}/{groupB:D}/limit", "k-b-link",
                   new SetSalesDiscountLimitRequestV1("1", "Linked", null, seller, "3", "SENTETIK"))))
        {
            await AssertProblemAsync(foreignLink, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.LinkedGroupNotFound);
        }

        Assert.Single((await run.JsonAsync(clientB, Groups)).GetProperty("groups").EnumerateArray());

        // SAT-D117 (Sinan, 2026-10-29): deactivating the seller group closes it to new bindings and links only — its own
        // limit and the manager's limit linked to it keep counting; a passive group refuses further changes.
        await run.CommandAsync(client, HttpMethod.Post, $"{Groups}/{seller:D}/deactivation", "k-deactivate",
            new DeactivateSalesDiscountLimitGroupRequestV1("3", "SENTETIK kapanış"), HttpStatusCode.NoContent);
        byCode = (await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .ToDictionary(group => group.GetProperty("code").GetString()!);
        Assert.Equal((false, "99", "100"), (byCode["SATIS-DESTEK"].GetProperty("isActive").GetBoolean(),
            byCode["SATIS-DESTEK"].GetProperty("effectiveLimit").GetProperty("percent").GetString(),
            byCode["MUDUR"].GetProperty("effectiveLimit").GetProperty("percent").GetString()));
        await using (var port = run.PolicyPort(CompanyA))
        {
            Assert.Equal(100m, (await port.Value.GetSalesDiscountLimitAsync(manager, run.Today)).MaximumDiscountPercent);
        }

        using (var newLink = await client.SendAsync(Json(HttpMethod.Put, $"{Groups}/{manager:D}/limit", "k-link-passive",
                   new SetSalesDiscountLimitRequestV1("2", "Linked", null, seller, "5", "SENTETIK"))))
        {
            await AssertProblemAsync(newLink, HttpStatusCode.UnprocessableEntity, SalesDiscountLimitProblemCodesV1.LinkedGroupInactive);
        }

        using (var passive = await client.SendAsync(Json(HttpMethod.Put, $"{Groups}/{seller:D}/name", "k-passive",
                   new RenameSalesDiscountLimitGroupRequestV1("4", "Eski", "SENTETIK"))))
        {
            await AssertProblemAsync(passive, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.GroupInactive);
        }

        // Audit: one event per real change, the actor, the name, the correlation and the whole reason in the after-image.
        Assert.Equal(6, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @company AND [EntityType] = N'Settings.SalesDiscountLimitGroup'",
            ("@company", CompanyA)));
        Assert.Equal((editor, Permission, "settings.sales-discount-limit.recorded.v1", "RecordLimit", "SENTETIK limit k-limit-2",
                "sales-limit-k-limit-2"),
            (await run.ScalarAsync<string>("SELECT [ActorId] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'"),
                await run.ScalarAsync<string>("SELECT [PermissionCode] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'"),
                await run.ScalarAsync<string>("SELECT [EventType] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'"),
                await run.ScalarAsync<string>("SELECT [Operation] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'"),
                await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.reason') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'"),
                await run.ScalarAsync<string>("SELECT [CorrelationId] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'")));
        Assert.Equal(("Linked", "3", seller.ToString("D")), (
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.kind') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.extraPoints') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.linkedGroupId') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-k-limit-2'")));

        // The database itself refuses an out-of-range or malformed record and a version-row update is fenced by the model.
        var invalid = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO [settings].[SalesDiscountLimitVersions] ([CompanyId],[LimitGroupId],[Version],[ProfileId],[Kind],[Percent],[LinkedGroupId],[ExtraPoints],[EffectiveFrom],[RecordedAtUtc],[RecordedBy],[Reason]) "
            + "VALUES (@company, @group, 9, NEWID(), 'Fixed', 101, NULL, NULL, '2026-10-27', SYSUTCDATETIME(), N'x', N'x')",
            ("@company", CompanyA), ("@group", manager)));
        Assert.Contains("CK_SettingsDiscountLimitVersions_Percent", invalid.Message, StringComparison.Ordinal);
        var shape = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO [settings].[SalesDiscountLimitVersions] ([CompanyId],[LimitGroupId],[Version],[ProfileId],[Kind],[Percent],[LinkedGroupId],[ExtraPoints],[EffectiveFrom],[RecordedAtUtc],[RecordedBy],[Reason]) "
            + "VALUES (@company, @group, 9, NEWID(), 'Linked', NULL, NULL, 3, '2026-10-27', SYSUTCDATETIME(), N'x', N'x')",
            ("@company", CompanyA), ("@group", manager)));
        Assert.Contains("CK_SettingsDiscountLimitVersions_Shape", shape.Message, StringComparison.Ordinal);
        var foreignKey = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO [settings].[SalesDiscountLimitVersions] ([CompanyId],[LimitGroupId],[Version],[ProfileId],[Kind],[Percent],[LinkedGroupId],[ExtraPoints],[EffectiveFrom],[RecordedAtUtc],[RecordedBy],[Reason]) "
            + "VALUES (@company, @group, 9, NEWID(), 'Linked', NULL, @foreign, 3, '2026-10-27', SYSUTCDATETIME(), N'x', N'x')",
            ("@company", CompanyA), ("@group", manager), ("@foreign", groupB)));
        Assert.Equal(547, foreignKey.Number);
    }

    [SqlServerFact]
    public async Task Sat_d84_a_scheduled_record_can_be_corrected_or_withdrawn_before_its_date_with_audit_and_chain_recalculation_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (editorSubject, editor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Permission);
        using var client = run.Client(editorSubject, CompanyA);
        var today = run.Today;
        var start = today.AddDays(10);
        var seller = await run.CreateGroupAsync(client, "w-create-1", "SATICI", "Satıcı");
        var manager = await run.CreateGroupAsync(client, "w-create-2", "MUDUR", "Müdür");
        await run.SetLimitAsync(client, "w-limit-1", seller, "1", new("Fixed", "30", null, null));
        await run.SetLimitAsync(client, "w-limit-2", seller, "2", new("Fixed", "35", null, null, start));
        await run.SetLimitAsync(client, "w-limit-3", manager, "1", new("Linked", null, seller, "3"));
        var withdrawal = $"{Groups}/{seller:D}/limit/withdrawal";
        var correction = $"{Groups}/{seller:D}/limit/correction";

        // Correct the scheduled record (v2 → 36 % on the same date): v2 is withdrawn and v3 written in one save.
        await run.CommandAsync(client, HttpMethod.Post, correction, "w-correct",
            new CorrectSalesDiscountLimitRequestV1("3", "2", "Fixed", "36", null, null, "SENTETIK düzeltme"),
            HttpStatusCode.NoContent);
        await using (var port = run.PolicyPort(CompanyA))
        {
            var corrected = await port.Value.GetSalesDiscountLimitAsync(seller, start);
            Assert.Equal((36m, 3L), (corrected.MaximumDiscountPercent, corrected.Profile.Version));
            Assert.Equal(39m, (await port.Value.GetSalesDiscountLimitAsync(manager, start)).MaximumDiscountPercent);
            Assert.Equal(30m, (await port.Value.GetSalesDiscountLimitAsync(seller, today)).MaximumDiscountPercent);
        }

        var sellerView = (await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("code").GetString() == "SATICI");
        var scheduled = Assert.Single(sellerView.GetProperty("scheduled").EnumerateArray());
        Assert.Equal(("3", "36"), (scheduled.GetProperty("limit").GetProperty("limitVersion").GetString(),
            scheduled.GetProperty("limit").GetProperty("percent").GetString()));

        // The withdrawn record cannot be corrected or withdrawn again.
        using (var again = await client.SendAsync(Json(HttpMethod.Post, withdrawal, "w-again",
                   new WithdrawSalesDiscountLimitRequestV1("4", "2", "SENTETIK"))))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.RecordWithdrawn);
        }

        // Withdraw the corrected record: the earlier record stays in force, and the linked manager limit follows it again.
        await run.CommandAsync(client, HttpMethod.Post, withdrawal, "w-withdraw",
            new WithdrawSalesDiscountLimitRequestV1("4", "3", "SENTETIK geri çekme"), HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, withdrawal, "w-withdraw",
            new WithdrawSalesDiscountLimitRequestV1("4", "3", "SENTETIK geri çekme"), HttpStatusCode.NoContent, replayed: true);
        await using (var port = run.PolicyPort(CompanyA))
        {
            var back = await port.Value.GetSalesDiscountLimitAsync(seller, start);
            Assert.Equal((30m, 1L, (DateOnly?)null), (back.MaximumDiscountPercent, back.Profile.Version, back.EffectiveThrough));
            Assert.Equal(33m, (await port.Value.GetSalesDiscountLimitAsync(manager, start)).MaximumDiscountPercent);
        }

        Assert.Empty((await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("code").GetString() == "SATICI").GetProperty("scheduled").EnumerateArray());

        // A record in force (today's) is immutable: neither withdrawn nor corrected; a new record is entered instead.
        using (var inEffect = await client.SendAsync(Json(HttpMethod.Post, withdrawal, "w-in-effect-1",
                   new WithdrawSalesDiscountLimitRequestV1("5", "1", "SENTETIK"))))
        {
            await AssertProblemAsync(inEffect, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.RecordInEffect);
        }

        using (var inEffect = await client.SendAsync(Json(HttpMethod.Post, correction, "w-in-effect-2",
                   new CorrectSalesDiscountLimitRequestV1("5", "1", "Fixed", "20", null, null, "SENTETIK"))))
        {
            await AssertProblemAsync(inEffect, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.RecordInEffect);
        }

        // Rows: records never change; two insert-only withdrawals (v2 replaced by v3, v3 withdrawn alone).
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesDiscountLimitVersions] WHERE [LimitGroupId] = @id", ("@id", seller)));
        Assert.Equal("2>3;3>-", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(CONCAT([LimitVersion], '>', COALESCE(CONVERT(nvarchar(20), [ReplacedByVersion]), '-')), ';') "
            + "WITHIN GROUP (ORDER BY [LimitVersion]) FROM [settings].[SalesDiscountLimitWithdrawals] WHERE [LimitGroupId] = @id",
            ("@id", seller)));

        // Audit: one event each, the actor, the operation, the whole reason and what was replaced.
        Assert.Equal(("settings.sales-discount-limit.corrected.v1", "CorrectLimit", editor, "SENTETIK düzeltme", "2", "3"), (
            await run.ScalarAsync<string>("SELECT [EventType] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-correct'"),
            await run.ScalarAsync<string>("SELECT [Operation] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-correct'"),
            await run.ScalarAsync<string>("SELECT [ActorId] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-correct'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.reason') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-correct'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.replacesLimitVersions[0]') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-correct'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.limitVersion') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-correct'")));
        Assert.Equal(("settings.sales-discount-limit.withdrawn.v1", "WithdrawLimit", "SENTETIK geri çekme", "3", 1), (
            await run.ScalarAsync<string>("SELECT [EventType] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-withdraw'"),
            await run.ScalarAsync<string>("SELECT [Operation] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-withdraw'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.reason') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-withdraw'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.withdrawnLimitVersion') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-withdraw'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-withdraw'")));

        // A withdrawal that would extend an earlier link into a cycle is refused: X follows Y today and is fixed from day
        // +10; Y follows X from day +20. Withdrawing X's day +10 record would close X → Y → X from day +20.
        var x = await run.CreateGroupAsync(client, "w-create-x", "X", "X");
        var y = await run.CreateGroupAsync(client, "w-create-y", "Y", "Y");
        await run.SetLimitAsync(client, "w-y-1", y, "1", new("Fixed", "20", null, null));
        await run.SetLimitAsync(client, "w-x-1", x, "1", new("Linked", null, y, "1"));
        await run.SetLimitAsync(client, "w-x-2", x, "2", new("Fixed", "25", null, null, start));
        await run.SetLimitAsync(client, "w-y-2", y, "2", new("Linked", null, x, "1", today.AddDays(20)));
        using (var cycle = await client.SendAsync(Json(HttpMethod.Post, $"{Groups}/{x:D}/limit/withdrawal", "w-cycle",
                   new WithdrawSalesDiscountLimitRequestV1("3", "2", "SENTETIK"))))
        {
            await AssertProblemAsync(cycle, HttpStatusCode.UnprocessableEntity, SalesDiscountLimitProblemCodesV1.LinkCycle);
        }

        // Review A P2-1 (SAT-D84): with TWO scheduled records, either can be corrected keeping its own date and either can be
        // withdrawn. T: 30 % today, 35 % on day +10, 40 % on day +20.
        var t = await run.CreateGroupAsync(client, "w-create-t", "T", "T");
        var later = today.AddDays(20);
        await run.SetLimitAsync(client, "w-t-1", t, "1", new("Fixed", "30", null, null));
        await run.SetLimitAsync(client, "w-t-2", t, "2", new("Fixed", "35", null, null, start));
        await run.SetLimitAsync(client, "w-t-3", t, "3", new("Fixed", "40", null, null, later));
        var tCorrection = $"{Groups}/{t:D}/limit/correction";
        var tWithdrawal = $"{Groups}/{t:D}/limit/withdrawal";
        await run.CommandAsync(client, HttpMethod.Post, tCorrection, "w-t-correct-earlier",
            new CorrectSalesDiscountLimitRequestV1("4", "2", "Fixed", "36", null, null, "SENTETIK öndeki düzeltme"),
            HttpStatusCode.NoContent);
        await using (var port = run.PolicyPort(CompanyA))
        {
            var first = await port.Value.GetSalesDiscountLimitAsync(t, start);
            Assert.Equal((36m, 4L, start, (DateOnly?)later.AddDays(-1)), (first.MaximumDiscountPercent, first.Profile.Version,
                first.EffectiveFrom, first.EffectiveThrough));
            Assert.Equal(40m, (await port.Value.GetSalesDiscountLimitAsync(t, later)).MaximumDiscountPercent);
        }

        await run.CommandAsync(client, HttpMethod.Post, tCorrection, "w-t-correct-later",
            new CorrectSalesDiscountLimitRequestV1("5", "3", "Fixed", "42", null, null, "SENTETIK sondaki düzeltme"),
            HttpStatusCode.NoContent);
        var tScheduled = (await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("code").GetString() == "T").GetProperty("scheduled").EnumerateArray()
            .Select(entry => (entry.GetProperty("limit").GetProperty("effectiveFrom").GetString(),
                entry.GetProperty("limit").GetProperty("percent").GetString()))
            .ToArray();
        Assert.Equal([(start.ToString("yyyy-MM-dd"), "36"), (later.ToString("yyyy-MM-dd"), "42")], tScheduled);

        await run.CommandAsync(client, HttpMethod.Post, tWithdrawal, "w-t-withdraw-earlier",
            new WithdrawSalesDiscountLimitRequestV1("6", "4", "SENTETIK öndeki geri"), HttpStatusCode.NoContent);
        await using (var port = run.PolicyPort(CompanyA))
        {
            var kept = await port.Value.GetSalesDiscountLimitAsync(t, start);
            Assert.Equal((30m, 1L, (DateOnly?)later.AddDays(-1)), (kept.MaximumDiscountPercent, kept.Profile.Version,
                kept.EffectiveThrough));
            Assert.Equal(42m, (await port.Value.GetSalesDiscountLimitAsync(t, later)).MaximumDiscountPercent);
        }

        await run.CommandAsync(client, HttpMethod.Post, tWithdrawal, "w-t-withdraw-later",
            new WithdrawSalesDiscountLimitRequestV1("7", "5", "SENTETIK sondaki geri"), HttpStatusCode.NoContent);
        await using (var port = run.PolicyPort(CompanyA))
        {
            var none = await port.Value.GetSalesDiscountLimitAsync(t, later.AddDays(100));
            Assert.Equal((30m, 1L, (DateOnly?)null), (none.MaximumDiscountPercent, none.Profile.Version, none.EffectiveThrough));
        }

        Assert.Equal("2>4;3>5;4>-;5>-", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(CONCAT([LimitVersion], '>', COALESCE(CONVERT(nvarchar(20), [ReplacedByVersion]), '-')), ';') "
            + "WITHIN GROUP (ORDER BY [LimitVersion]) FROM [settings].[SalesDiscountLimitWithdrawals] WHERE [LimitGroupId] = @id",
            ("@id", t)));

        // Review P2-1: schedule → same-date replacement → withdraw the replacement: the earlier limit stays in force and the
        // replaced record never comes back. Today 5 %; 50 % is scheduled for day +10 by mistake; a new 35 % record on the
        // same date REPLACES it (withdrawn in the same save, audited as a correction); withdrawing the 35 % leaves 5 %.
        var z = await run.CreateGroupAsync(client, "w-create-z", "Z", "Z");
        await run.SetLimitAsync(client, "w-z-1", z, "1", new("Fixed", "5", null, null));
        await run.SetLimitAsync(client, "w-z-2", z, "2", new("Fixed", "50", null, null, start));
        await run.SetLimitAsync(client, "w-z-3", z, "3", new("Fixed", "35", null, null, start));
        Assert.Equal("2>3", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(CONCAT([LimitVersion], '>', COALESCE(CONVERT(nvarchar(20), [ReplacedByVersion]), '-')), ';') "
            + "FROM [settings].[SalesDiscountLimitWithdrawals] WHERE [LimitGroupId] = @id", ("@id", z)));
        Assert.Equal(("settings.sales-discount-limit.corrected.v1", "2"), (
            await run.ScalarAsync<string>("SELECT [EventType] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-z-3'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.replacesLimitVersions[0]') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-w-z-3'")));
        var zView = (await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("code").GetString() == "Z");
        Assert.Equal("35", Assert.Single(zView.GetProperty("scheduled").EnumerateArray()).GetProperty("limit").GetProperty("percent").GetString());

        await run.CommandAsync(client, HttpMethod.Post, $"{Groups}/{z:D}/limit/withdrawal", "w-z-withdraw",
            new WithdrawSalesDiscountLimitRequestV1("4", "3", "SENTETIK planlı değişiklik olmasın"), HttpStatusCode.NoContent);
        await using (var port = run.PolicyPort(CompanyA))
        {
            var kept = await port.Value.GetSalesDiscountLimitAsync(z, start);
            Assert.Equal((5m, 1L, (DateOnly?)null), (kept.MaximumDiscountPercent, kept.Profile.Version, kept.EffectiveThrough));
            Assert.Equal(5m, (await port.Value.GetSalesDiscountLimitAsync(z, start.AddDays(400))).MaximumDiscountPercent);
        }

        Assert.Empty((await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("code").GetString() == "Z").GetProperty("scheduled").EnumerateArray());
    }

    [SqlServerFact]
    public async Task Sat_d78_a_future_dated_limit_waits_for_its_date_and_the_linked_chain_resolves_per_date_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (editorSubject, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Permission);
        using var client = run.Client(editorSubject, CompanyA);
        var today = run.Today;
        var start = today.AddDays(30);
        var seller = await run.CreateGroupAsync(client, "f-create-1", "SATICI", "Satıcı");
        var manager = await run.CreateGroupAsync(client, "f-create-2", "MUDUR", "Müdür");
        var senior = await run.CreateGroupAsync(client, "f-create-3", "KIDEMLI", "Kıdemli");

        // Seller 30 % from today (date omitted), 35 % scheduled from day +30; manager = seller + 3 from today.
        await run.SetLimitAsync(client, "f-limit-1", seller, "1", new("Fixed", "30", null, null));
        await run.SetLimitAsync(client, "f-limit-2", seller, "2", new("Fixed", "35", null, null, start));
        await run.SetLimitAsync(client, "f-limit-3", manager, "1", new("Linked", null, seller, "3"));

        // The list shows the record in force today and the scheduled one with its limit on its first day.
        var byCode = (await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .ToDictionary(group => group.GetProperty("code").GetString()!);
        var sellerView = byCode["SATICI"];
        Assert.Equal(("30", "30", today.ToString("yyyy-MM-dd")), (sellerView.GetProperty("limit").GetProperty("percent").GetString(),
            sellerView.GetProperty("effectiveLimit").GetProperty("percent").GetString(),
            sellerView.GetProperty("limit").GetProperty("effectiveFrom").GetString()));
        var scheduled = Assert.Single(sellerView.GetProperty("scheduled").EnumerateArray());
        Assert.Equal(("35", start.ToString("yyyy-MM-dd"), "2", "35"), (
            scheduled.GetProperty("limit").GetProperty("percent").GetString(),
            scheduled.GetProperty("limit").GetProperty("effectiveFrom").GetString(),
            scheduled.GetProperty("limit").GetProperty("limitVersion").GetString(),
            scheduled.GetProperty("effectiveLimit").GetProperty("percent").GetString()));
        Assert.Empty(byCode["MUDUR"].GetProperty("scheduled").EnumerateArray());
        Assert.Equal("33", byCode["MUDUR"].GetProperty("effectiveLimit").GetProperty("percent").GetString());

        // The policy port the Sales check reads: not yet in force the day before, in force by itself on its date, and the
        // linked manager limit follows the seller's record of each date.
        await using (var port = run.PolicyPort(CompanyA))
        {
            var before = await port.Value.GetSalesDiscountLimitAsync(seller, start.AddDays(-1));
            var on = await port.Value.GetSalesDiscountLimitAsync(seller, start);
            Assert.Equal((30m, 1L, (DateOnly?)start.AddDays(-1)), (before.MaximumDiscountPercent, before.Profile.Version,
                before.EffectiveThrough));
            Assert.Equal((35m, 2L, start, (DateOnly?)null), (on.MaximumDiscountPercent, on.Profile.Version, on.EffectiveFrom,
                on.EffectiveThrough));
            Assert.Equal(33m, (await port.Value.GetSalesDiscountLimitAsync(manager, start.AddDays(-1))).MaximumDiscountPercent);
            var managerOn = await port.Value.GetSalesDiscountLimitAsync(manager, start);
            Assert.Equal((38m, start), (managerOn.MaximumDiscountPercent, managerOn.EffectiveFrom));
        }

        // A past date is refused; nothing is written.
        using (var past = await client.SendAsync(Json(HttpMethod.Put, $"{Groups}/{seller:D}/limit", "f-past",
                   new SetSalesDiscountLimitRequestV1("3", "Fixed", "20", null, null, "SENTETIK", today.AddDays(-1)))))
        {
            await AssertProblemAsync(past, HttpStatusCode.UnprocessableEntity, SalesDiscountLimitProblemCodesV1.EffectiveFromPast);
        }

        Assert.Equal((2L, 3L), (
            await run.ScalarAsync<long>("SELECT COUNT_BIG(*) FROM [settings].[SalesDiscountLimitVersions] WHERE [LimitGroupId] = @id",
                ("@id", seller)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [settings].[SalesDiscountLimitGroups] WHERE [Id] = @id", ("@id", seller))));

        // Review A P2-1: a record may start BEFORE an already scheduled one (the timeline is ordered by date, then version):
        // 32 % from day +20 holds until day +29, and the scheduled 35 % still takes effect on day +30.
        var earlier = start.AddDays(-10);
        await run.SetLimitAsync(client, "f-limit-earlier", seller, "3", new("Fixed", "32", null, null, earlier));
        await using (var port = run.PolicyPort(CompanyA))
        {
            Assert.Equal(30m, (await port.Value.GetSalesDiscountLimitAsync(seller, earlier.AddDays(-1))).MaximumDiscountPercent);
            var inserted = await port.Value.GetSalesDiscountLimitAsync(seller, earlier);
            Assert.Equal((32m, 3L, (DateOnly?)start.AddDays(-1)), (inserted.MaximumDiscountPercent, inserted.Profile.Version,
                inserted.EffectiveThrough));
            Assert.Equal((35m, 2L), ((await port.Value.GetSalesDiscountLimitAsync(seller, start)).MaximumDiscountPercent,
                (await port.Value.GetSalesDiscountLimitAsync(seller, start)).Profile.Version));
        }

        // A record on a scheduled record's date REPLACES it (review B P2-1: the scheduled record is withdrawn in the same save
        // and the change is audited as a correction); the audit after-image carries the date.
        await run.SetLimitAsync(client, "f-limit-4", seller, "4", new("Fixed", "36", null, null, start));
        await using (var port = run.PolicyPort(CompanyA))
        {
            var corrected = await port.Value.GetSalesDiscountLimitAsync(seller, start);
            Assert.Equal((36m, 4L), (corrected.MaximumDiscountPercent, corrected.Profile.Version));
            Assert.Equal(39m, (await port.Value.GetSalesDiscountLimitAsync(manager, start)).MaximumDiscountPercent);
        }

        sellerView = (await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("code").GetString() == "SATICI");
        Assert.Equal(["32", "36"], sellerView.GetProperty("scheduled").EnumerateArray()
            .Select(entry => entry.GetProperty("limit").GetProperty("percent").GetString()!).ToArray());
        Assert.Equal((start.ToString("yyyy-MM-dd"), "settings.sales-discount-limit.corrected.v1", "2"), (
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.effectiveFrom') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-f-limit-4'"),
            await run.ScalarAsync<string>("SELECT [EventType] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-f-limit-4'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.replacesLimitVersions[0]') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-f-limit-4'")));

        // A cycle that only exists after a scheduled change is refused: the senior group is fixed now and scheduled to follow
        // the seller from day +60, so the seller cannot follow the senior group from day +30 on.
        await run.SetLimitAsync(client, "f-limit-5", senior, "1", new("Fixed", "40", null, null));
        await run.SetLimitAsync(client, "f-limit-6", senior, "2", new("Linked", null, seller, "1", today.AddDays(60)));
        using (var cycle = await client.SendAsync(Json(HttpMethod.Put, $"{Groups}/{seller:D}/limit", "f-cycle",
                   new SetSalesDiscountLimitRequestV1("5", "Linked", null, senior, "1", "SENTETIK", start))))
        {
            await AssertProblemAsync(cycle, HttpStatusCode.UnprocessableEntity, SalesDiscountLimitProblemCodesV1.LinkCycle);
        }
    }

    [SqlServerFact]
    public async Task A_binding_is_written_to_every_company_of_the_actor_all_or_nothing_and_feeds_the_sales_port_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (editorSubject, editor) = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, Permission);
        await run.GrantAsync(TestUsers.NoAssignment, CompanyB, Permission);
        await run.GrantAsync(TestUsers.NoAssignment, CompanyC, Permission);
        using var clientA = run.Client(editorSubject, CompanyA);
        using var clientB = run.Client(editorSubject, CompanyB);
        using var clientC = run.Client(editorSubject, CompanyC);
        var person = await run.PersonAsync("SENTETIK Satıcı", CompanyA, CompanyB);
        var sellerA = await run.CreateGroupAsync(clientA, "a-1", "SATIS-DESTEK", "Satış Destek");
        var managerA = await run.CreateGroupAsync(clientA, "a-2", "MUDUR", "Müdür");
        await run.CreateGroupAsync(clientB, "b-1", "SATIS-DESTEK", "Satış Destek");
        await run.CreateGroupAsync(clientB, "b-2", "MUDUR", "Müdür");
        await run.SetLimitAsync(clientA, "a-limit-1", sellerA, "1", new("Fixed", "35", null, null));
        await run.SetLimitAsync(clientA, "a-limit-2", managerA, "1", new("Linked", null, sellerA, "3"));
        var path = $"{SettingsEndpoints.SalesDiscountLimitsPath}/bindings/{person:D}";

        // The editor's view covers every company they can act in, with version "0" before any binding; SAT-D116 (Sinan,
        // 2026-10-29): a company where the person is not a member (C) is marked skipped and nothing of it is read.
        var initial = await run.JsonAsync(clientA, path);
        var initialCompanies = initial.GetProperty("companies").EnumerateArray()
            .ToDictionary(company => company.GetProperty("companyId").GetGuid());
        Assert.Equal([CompanyA, CompanyB, CompanyC], initialCompanies.Keys.Order().ToArray());
        Assert.All(initialCompanies.Values, company => Assert.Equal("0", company.GetProperty("version").GetString()));
        Assert.Equal((true, true, false), (initialCompanies[CompanyA].GetProperty("isMember").GetBoolean(),
            initialCompanies[CompanyB].GetProperty("isMember").GetBoolean(), initialCompanies[CompanyC].GetProperty("isMember").GetBoolean()));
        var allZero = Companies(("0", CompanyA), ("0", CompanyB), ("0", CompanyC));

        // Naming a company where the person is not a member is a stale list: 422, nothing written.
        using (var notMember = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-1",
                   new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK", "MUDUR"], allZero, "SENTETIK atama"))))
        {
            await AssertProblemAsync(notMember, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.BindingCompaniesMismatch);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships]"));

        // SAT-D116: written to the member companies A and B in one request; C is skipped and stays untouched.
        await run.CommandAsync(clientA, HttpMethod.Put, path, "bind-1b",
            new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK"], Companies(("0", CompanyA), ("0", CompanyB)),
                "SENTETIK atama"),
            HttpStatusCode.NoContent);
        Assert.Equal((2, 0), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships] WHERE [UserId] = @user",
                ("@user", person)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships] WHERE [UserId] = @user AND [CompanyId] = @c",
                ("@user", person), ("@c", CompanyC))));

        // A later membership of C does NOT extend the binding by itself (fail-closed): C is now offered at version "0" and
        // reads no group for the person until the binding is written again.
        await run.AddMembershipAsync(person, CompanyC);
        var joined = (await run.JsonAsync(clientA, path)).GetProperty("companies").EnumerateArray()
            .Single(company => company.GetProperty("companyId").GetGuid() == CompanyC);
        Assert.Equal((true, "0", 0), (joined.GetProperty("isMember").GetBoolean(), joined.GetProperty("version").GetString(),
            joined.GetProperty("groups").GetArrayLength()));
        await using (var sales = run.SalesPorts(person, CompanyC))
        {
            Assert.Empty(await sales.Groups.GetCurrentActorLimitGroupsAsync());
        }

        // C has no such group: the failing company refuses ALL companies (no partial write).
        var afterFirst = Companies(("1", CompanyA), ("1", CompanyB), ("0", CompanyC));
        using (var missing = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-2",
                   new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK", "MUDUR"], afterFirst, "SENTETIK atama"))))
        {
            var problem = await AssertProblemAsync(missing, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.BindingGroupNotFound);
            Assert.Equal((CompanyC, "MUDUR"), (problem.GetProperty("companyId").GetGuid(),
                problem.GetProperty("groupCode").GetString()));
        }

        Assert.Equal((2, 2, 2), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMembershipGroups]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.SalesDiscountLimitBinding'")));

        // Only some of the member companies: the editor must name every company where the person works.
        using (var partial = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-3",
                   new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK"], Companies(("1", CompanyA), ("1", CompanyB)),
                       "SENTETIK atama"))))
        {
            await AssertProblemAsync(partial, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.BindingCompaniesMismatch);
        }

        // Every member company has both groups: ONE request writes all three, each with its own audit event in its company.
        await run.CreateGroupAsync(clientC, "c-1", "SATIS-DESTEK", "Satış Destek");
        await run.CreateGroupAsync(clientC, "c-2", "MUDUR", "Müdür");
        await run.CommandAsync(clientA, HttpMethod.Put, path, "bind-4",
            new SetSalesDiscountLimitBindingRequestV1(["mudur", "SATIS-DESTEK"], afterFirst, "SENTETIK atama"),
            HttpStatusCode.NoContent);
        Assert.Equal((3, 8), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships] WHERE [UserId] = @user",
                ("@user", person)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMembershipGroups] WHERE [UserId] = @user",
                ("@user", person))));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT [CompanyId]) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.SalesDiscountLimitBinding' AND [EntityId] = @user "
            + "AND [ActorId] = @actor AND [CorrelationId] = N'sales-limit-bind-4' AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK atama'",
            ("@user", person.ToString("D")), ("@actor", editor)));

        // Replay: the stored 204 with no second effect; a stale version per company is 409 naming the company.
        await run.CommandAsync(clientA, HttpMethod.Put, path, "bind-4",
            new SetSalesDiscountLimitBindingRequestV1(["mudur", "SATIS-DESTEK"], afterFirst, "SENTETIK atama"),
            HttpStatusCode.NoContent, replayed: true);
        using (var stale = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-5",
                   new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK"], Companies(("2", CompanyA), ("1", CompanyB), ("1", CompanyC)),
                       "SENTETIK daraltma"))))
        {
            var problem = await AssertProblemAsync(stale, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.VersionConflict);
            Assert.Equal(CompanyB, problem.GetProperty("companyId").GetGuid());
        }

        // SAT-D63/D66 → API-12: the person's ACTIVE groups in the request's company, and SAT-D62's linked limit for them.
        await using (var sales = run.SalesPorts(person, CompanyA))
        {
            var groups = await sales.Groups.GetCurrentActorLimitGroupsAsync();
            Assert.Equal(new[] { sellerA, managerA }.Order(), groups.Order());
            var limits = new List<decimal>();
            foreach (var group in groups)
            {
                limits.Add((await sales.Policies.GetSalesDiscountLimitAsync(group, run.Today)).MaximumDiscountPercent);
            }

            // SAT-D34: the Sales check takes the highest of them.
            Assert.Equal(38m, limits.Max());
        }

        // SAT-D70: the editor may bind themselves (no self-block). SAT-D117 (Sinan, 2026-10-29): a passive group keeps
        // counting for the people already bound to it — the Sales port still reads it in A.
        var self = Guid.Parse(editor);
        await run.CommandAsync(clientA, HttpMethod.Put, $"{SettingsEndpoints.SalesDiscountLimitsPath}/bindings/{self:D}", "bind-self",
            new SetSalesDiscountLimitBindingRequestV1(["MUDUR"], allZero, "SENTETIK kendi bağı"), HttpStatusCode.NoContent);
        await run.CommandAsync(clientA, HttpMethod.Post, $"{Groups}/{managerA:D}/deactivation", "a-deactivate",
            new DeactivateSalesDiscountLimitGroupRequestV1("2", "SENTETIK"), HttpStatusCode.NoContent);
        await using (var sales = run.SalesPorts(person, CompanyA))
        {
            var groups = await sales.Groups.GetCurrentActorLimitGroupsAsync();
            Assert.Equal(new[] { sellerA, managerA }.Order(), groups.Order());
            Assert.Equal(38m, (await sales.Policies.GetSalesDiscountLimitAsync(managerA, run.Today)).MaximumDiscountPercent);
        }

        // SAT-D117: a NEW binding to the passive group is refused for every company (here: a person not yet in MUDUR) ...
        var newcomer = await run.PersonAsync("SENTETIK Yeni", CompanyA);
        using (var passive = await clientA.SendAsync(Json(HttpMethod.Put, $"{SettingsEndpoints.SalesDiscountLimitsPath}/bindings/{newcomer:D}",
                   "bind-6", new SetSalesDiscountLimitBindingRequestV1(["MUDUR"], Companies(("0", CompanyA)), "SENTETIK"))))
        {
            var problem = await AssertProblemAsync(passive, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.BindingGroupInactive);
            Assert.Equal(CompanyA, problem.GetProperty("companyId").GetGuid());
        }

        // ... while a person already in it keeps it when their binding is saved again.
        await run.CommandAsync(clientA, HttpMethod.Put, path, "bind-6b",
            new SetSalesDiscountLimitBindingRequestV1(["MUDUR", "SATIS-DESTEK"], Companies(("2", CompanyA), ("2", CompanyB), ("1", CompanyC)),
                "SENTETIK yeniden kayıt"),
            HttpStatusCode.NoContent);

        // SAT-D118: the passive group is reactivated with a reason (audited); a second reactivation is 409 group_active.
        await run.CommandAsync(clientA, HttpMethod.Post, $"{Groups}/{managerA:D}/activation", "a-reactivate",
            new ReactivateSalesDiscountLimitGroupRequestV1("3", "SENTETIK yeniden açıldı"), HttpStatusCode.NoContent);
        using (var again = await clientA.SendAsync(Json(HttpMethod.Post, $"{Groups}/{managerA:D}/activation", "a-reactivate-2",
                   new ReactivateSalesDiscountLimitGroupRequestV1("4", "SENTETIK"))))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, SalesDiscountLimitProblemCodesV1.GroupActive);
        }

        Assert.Equal(("settings.sales-discount-limit-group.reactivated.v1", "Reactivate", "SENTETIK yeniden açıldı"), (
            await run.ScalarAsync<string>("SELECT [EventType] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-a-reactivate'"),
            await run.ScalarAsync<string>("SELECT [Operation] FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-a-reactivate'"),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([AfterJson], '$.reason') FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'sales-limit-a-reactivate'")));
        await run.CommandAsync(clientA, HttpMethod.Put, $"{SettingsEndpoints.SalesDiscountLimitsPath}/bindings/{newcomer:D}", "bind-7",
            new SetSalesDiscountLimitBindingRequestV1(["MUDUR"], Companies(("0", CompanyA)), "SENTETIK"), HttpStatusCode.NoContent);

        // SAT-D116: without the name in ONE company where the person works (B): 403 and nothing written anywhere.
        var (narrowSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, Permission);
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyB, "sales.drafts.read");
        using var narrow = run.Client(narrowSubject, CompanyA);
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMembershipGroups]");
        using (var forbidden = await narrow.SendAsync(Json(HttpMethod.Put, path, "bind-narrow",
                   new SetSalesDiscountLimitBindingRequestV1([], Companies(("3", CompanyA), ("3", CompanyB)), "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMembershipGroups]"));

        // A person who works in none of the narrow editor's companies (only C) cannot be bound by them: 422, nothing written.
        var outsider = await run.PersonAsync("SENTETIK Dış", CompanyC);
        using (var none = await narrow.SendAsync(Json(HttpMethod.Put, $"{SettingsEndpoints.SalesDiscountLimitsPath}/bindings/{outsider:D}",
                   "bind-outsider", new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK"], Companies(("0", CompanyA)), "SENTETIK"))))
        {
            await AssertProblemAsync(none, HttpStatusCode.UnprocessableEntity, SalesDiscountLimitProblemCodesV1.BindingPersonNotMember);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships] WHERE [UserId] = @user", ("@user", outsider)));

        // The people picker lists the company's active members (id + name only).
        var people = await run.JsonAsync(clientA, $"{SettingsEndpoints.SalesDiscountLimitsPath}/people?q=SENTETIK%20Sat");
        Assert.Contains(people.GetProperty("items").EnumerateArray(), item => item.GetProperty("userId").GetGuid() == person);

        // The migration's rollback is refused while groups and bindings exist (forward fix, 51095); nothing is dropped.
        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = Array.IndexOf(applied, "20261027221309_AddSalesDiscountLimitGroups");
        Assert.True(migration > 0);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[migration - 1]));
        Assert.Equal(51095, rollback.Number);
        // EF reverts the later migrations first, each in its own transaction: a later one whose store is empty (MOD-03
        // AddTaxMaster here) rolls back freely, and the refused migration and everything before it stay applied.
        Assert.Equal(applied.Take(migration + 1), await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(6, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitGroups]"));
    }

    private static IReadOnlyList<SalesDiscountLimitBindingCompanyVersionV1> Companies(params (string Version, Guid Company)[] values) =>
        values.Select(value => new SalesDiscountLimitBindingCompanyVersionV1(value.Company, value.Version)).ToArray();

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"sales-limit-{key}");
        return request;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status == response.StatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }

    private sealed record LimitInput(string Kind, string? Percent, Guid? LinkedGroupId, string? ExtraPoints,
        DateOnly? EffectiveFrom = null);

    private sealed class PortScope<T>(SettingsDbContext database, T value) : IAsyncDisposable
    {
        public T Value { get; } = value;

        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed class SalesPortScope(SettingsDbContext database, ISalesDiscountLimitGroupSourceV1 groups,
        ISettingsSalesPolicyProviderV1 policies) : IAsyncDisposable
    {
        public ISalesDiscountLimitGroupSourceV1 Groups { get; } = groups;

        public ISettingsSalesPolicyProviderV1 Policies { get; } = policies;

        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed class FixedActor(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
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

        public DateOnly Today { get; } = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task<Guid> CreateGroupAsync(HttpClient client, string key, string code, string name)
        {
            using var response = await client.SendAsync(Json(HttpMethod.Post, Groups, key,
                new CreateSalesDiscountLimitGroupRequestV1(code, name, "SENTETIK kuruluş")));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {body}");
            Assert.Equal("1", body.GetProperty("version").GetString());
            return body.GetProperty("id").GetGuid();
        }

        public Task SetLimitAsync(HttpClient client, string key, Guid group, string version, LimitInput limit) =>
            CommandAsync(client, HttpMethod.Put, $"{Groups}/{group:D}/limit", key,
                new SetSalesDiscountLimitRequestV1(version, limit.Kind, limit.Percent, limit.LinkedGroupId, limit.ExtraPoints,
                    $"SENTETIK limit {key}", limit.EffectiveFrom),
                HttpStatusCode.NoContent);

        public async Task CommandAsync(HttpClient client, HttpMethod method, string path, string key, object body,
            HttpStatusCode expected, bool replayed = false)
        {
            using var response = await client.SendAsync(Json(method, path, key, body));
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {text}");
            Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
            return body;
        }

        /// <summary>The host's Settings policy port over the real database, for a member of the company.</summary>
        public PortScope<SettingsSalesPolicyProviderV1> PolicyPort(Guid company)
        {
            var (database, companyContext) = Settings(company);
            return new PortScope<SettingsSalesPolicyProviderV1>(database,
                new SettingsSalesPolicyProviderV1(database, companyContext, new FixedActor("SENTETIK-sales-actor", company)));
        }

        /// <summary>What the Sales limit check is composed with, for <paramref name="person"/> acting in the company.</summary>
        public SalesPortScope SalesPorts(Guid person, Guid company)
        {
            var (database, companyContext) = Settings(company);
            var actor = new FixedActor(person.ToString("D"), company);
            return new SalesPortScope(database,
                new SalesDiscountLimitGroupHost(new SettingsSalesDiscountLimitGroupMembershipReaderV1(database, companyContext, actor)),
                new SettingsSalesPolicyProviderV1(database, companyContext, actor));
        }

        public SettingsDbContext SettingsContext(Guid company) => Settings(company).Database;

        private (SettingsDbContext Database, CompanyContext Company) Settings(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
                .Options, companyContext);
            return (database, companyContext);
        }

        /// <summary>A fresh synthetic role of the given names for the subject in one company (company-wide).</summary>
        public async Task<(string Subject, string ActorId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK limit düzenleyici", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            AddRole(dbContext, actor, company, permissions);
            await dbContext.SaveChangesAsync();
            return (developmentUser, actor.ToString("D"));
        }

        /// <summary>A person (no login) who is an active member of the given companies.</summary>
        public async Task<Guid> PersonAsync(string displayName, params Guid[] companies)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var person = Guid.NewGuid();
            dbContext.Users.Add(new UserAccount(person, displayName, null));
            foreach (var company in companies)
            {
                AddRole(dbContext, person, company, "sales.drafts.read");
            }

            await dbContext.SaveChangesAsync();
            return person;
        }

        public async Task AddMembershipAsync(Guid person, Guid company)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            AddRole(dbContext, person, company, "sales.drafts.read");
            await dbContext.SaveChangesAsync();
        }

        private static void AddRole(IdentityDbContext dbContext, Guid user, Guid company, params string[] permissions)
        {
            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SAT-D65-{roleId:N}", "SENTETIK limit rolü", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), user, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
                approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
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
    }
}
