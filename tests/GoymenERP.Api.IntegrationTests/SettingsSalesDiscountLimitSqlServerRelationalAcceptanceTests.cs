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

        // Deactivating the seller group: the manager's linked limit becomes "none" (fail-closed → approval), and a passive
        // group refuses further changes.
        await run.CommandAsync(client, HttpMethod.Post, $"{Groups}/{seller:D}/deactivation", "k-deactivate",
            new DeactivateSalesDiscountLimitGroupRequestV1("3", "SENTETIK kapanış"), HttpStatusCode.NoContent);
        byCode = (await run.JsonAsync(client, Groups)).GetProperty("groups").EnumerateArray()
            .ToDictionary(group => group.GetProperty("code").GetString()!);
        Assert.Equal((false, "None", "None"), (byCode["SATIS-DESTEK"].GetProperty("isActive").GetBoolean(),
            byCode["SATIS-DESTEK"].GetProperty("effectiveLimit").GetProperty("state").GetString(),
            byCode["MUDUR"].GetProperty("effectiveLimit").GetProperty("state").GetString()));
        await using (var port = run.PolicyPort(CompanyA))
        {
            Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetSalesDiscountLimitAsync(manager, run.Today))).Code);
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

        // The editor's view covers every company they can act in, with version "0" before any binding.
        var initial = await run.JsonAsync(clientA, path);
        Assert.Equal([CompanyA, CompanyB, CompanyC], initial.GetProperty("companies").EnumerateArray()
            .Select(company => company.GetProperty("companyId").GetGuid()).Order().ToArray());
        Assert.All(initial.GetProperty("companies").EnumerateArray(), company =>
            Assert.Equal("0", company.GetProperty("version").GetString()));
        var allZero = Companies(("0", CompanyA), ("0", CompanyB), ("0", CompanyC));

        // The person is not a member of C: refused for the whole request (fail-closed), nothing written.
        using (var notMember = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-1",
                   new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK", "MUDUR"], allZero, "SENTETIK atama"))))
        {
            var problem = await AssertProblemAsync(notMember, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.BindingPersonNotMember);
            Assert.Equal(CompanyC, problem.GetProperty("companyId").GetGuid());
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships]"));

        // A member of C now, but C has no such group: the failing company refuses ALL companies (no partial write).
        await run.AddMembershipAsync(person, CompanyC);
        using (var missing = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-2",
                   new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK", "MUDUR"], allZero, "SENTETIK atama"))))
        {
            var problem = await AssertProblemAsync(missing, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.BindingGroupNotFound);
            Assert.Equal((CompanyC, "MUDUR"), (problem.GetProperty("companyId").GetGuid(),
                problem.GetProperty("groupCode").GetString()));
        }

        Assert.Equal((0, 0, 0), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMembershipGroups]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.SalesDiscountLimitBinding'")));

        // Only some of the companies: the editor must name every company they can act in.
        using (var partial = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-3",
                   new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK"], Companies(("0", CompanyA), ("0", CompanyB)),
                       "SENTETIK atama"))))
        {
            await AssertProblemAsync(partial, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.BindingCompaniesMismatch);
        }

        // Every company has both groups: ONE request writes all three, each with its own audit event in its own company.
        await run.CreateGroupAsync(clientC, "c-1", "SATIS-DESTEK", "Satış Destek");
        await run.CreateGroupAsync(clientC, "c-2", "MUDUR", "Müdür");
        await run.CommandAsync(clientA, HttpMethod.Put, path, "bind-4",
            new SetSalesDiscountLimitBindingRequestV1(["mudur", "SATIS-DESTEK"], allZero, "SENTETIK atama"),
            HttpStatusCode.NoContent);
        Assert.Equal((3, 6), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMemberships] WHERE [UserId] = @user AND [Version] = 1",
                ("@user", person)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMembershipGroups] WHERE [UserId] = @user",
                ("@user", person))));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT [CompanyId]) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.SalesDiscountLimitBinding' AND [EntityId] = @user "
            + "AND [ActorId] = @actor AND [CorrelationId] = N'sales-limit-bind-4' AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK atama'",
            ("@user", person.ToString("D")), ("@actor", editor)));

        // Replay: the stored 204 with no second effect; a stale version per company is 409 naming the company.
        await run.CommandAsync(clientA, HttpMethod.Put, path, "bind-4",
            new SetSalesDiscountLimitBindingRequestV1(["mudur", "SATIS-DESTEK"], allZero, "SENTETIK atama"),
            HttpStatusCode.NoContent, replayed: true);
        using (var stale = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-5",
                   new SetSalesDiscountLimitBindingRequestV1(["SATIS-DESTEK"], Companies(("1", CompanyA), ("0", CompanyB), ("1", CompanyC)),
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

        // SAT-D70: the editor may bind themselves (no self-block); a passive group drops out of the Sales port at once.
        var self = Guid.Parse(editor);
        await run.CommandAsync(clientA, HttpMethod.Put, $"{SettingsEndpoints.SalesDiscountLimitsPath}/bindings/{self:D}", "bind-self",
            new SetSalesDiscountLimitBindingRequestV1(["MUDUR"], allZero, "SENTETIK kendi bağı"), HttpStatusCode.NoContent);
        await run.CommandAsync(clientA, HttpMethod.Post, $"{Groups}/{managerA:D}/deactivation", "a-deactivate",
            new DeactivateSalesDiscountLimitGroupRequestV1("2", "SENTETIK"), HttpStatusCode.NoContent);
        await using (var sales = run.SalesPorts(person, CompanyA))
        {
            Assert.Equal([sellerA], await sales.Groups.GetCurrentActorLimitGroupsAsync());
        }

        await using (var sales = run.SalesPorts(person, CompanyB))
        {
            Assert.Equal(2, (await sales.Groups.GetCurrentActorLimitGroupsAsync()).Count);
        }

        // Binding to a passive group is refused for every company.
        using (var passive = await clientA.SendAsync(Json(HttpMethod.Put, path, "bind-6",
                   new SetSalesDiscountLimitBindingRequestV1(["MUDUR"], Companies(("1", CompanyA), ("1", CompanyB), ("1", CompanyC)),
                       "SENTETIK"))))
        {
            var problem = await AssertProblemAsync(passive, HttpStatusCode.UnprocessableEntity,
                SalesDiscountLimitProblemCodesV1.BindingGroupInactive);
            Assert.Equal(CompanyA, problem.GetProperty("companyId").GetGuid());
        }

        // Without the name in ONE of the companies the editor can act in: 403 and nothing written anywhere.
        var (narrowSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, Permission);
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyB, "sales.drafts.read");
        using var narrow = run.Client(narrowSubject, CompanyA);
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMembershipGroups]");
        using (var forbidden = await narrow.SendAsync(Json(HttpMethod.Put, path, "bind-narrow",
                   new SetSalesDiscountLimitBindingRequestV1([], Companies(("1", CompanyA), ("1", CompanyB)), "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[SalesDiscountLimitMembershipGroups]"));

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
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
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

    private sealed record LimitInput(string Kind, string? Percent, Guid? LinkedGroupId, string? ExtraPoints);

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
                    $"SENTETIK limit {key}"),
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
