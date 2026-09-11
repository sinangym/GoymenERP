using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.Contracts;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests
{
    private const string PartiesUrl = "/api/v1/counterparties";
    private const string CandidatesUrl = PartiesUrl + "/company-account-link-candidates";
    private const string PreviousMigration = "20260828150000_AddCounterpartyWriteSupport";
    private const string OptionalRolesMigration = "20260910120000_AllowCounterpartyAccountNoRole";
    private const PartyRole AllRoles =
        PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service;

    // SQL facts deliberately integrate their input matrices in one disposable database.
    // There are nine discovered executions, with no InlineData expansion.
    [SqlServerFact]
    public async Task ClientContractGate_BlocksUnnegotiatedWritesAndPreservesSameKeyRetry()
    {
        await WithDatabaseAsync(async (factory, connection) =>
        {
            using var source = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var target = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            var key = NewIdentity();
            var before = await ReadSnapshotAsync(connection);
            source.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
            foreach (var roles in new string?[] { null, "0", "15" })
            {
                using var rejected = await PostJsonAsync(source, PartiesUrl,
                    CreateJson("Contract gate create", roles), key);
                await AssertProblemAsync(rejected, HttpStatusCode.Conflict,
                    CounterpartyClientContract.RequiredCode);
                Assert.False(rejected.Headers.Contains(CounterpartyClientContract.HeaderName));
                Assert.False(rejected.Headers.Contains("Idempotency-Replayed"));
                Assert.Equal(before, await ReadSnapshotAsync(connection));
            }

            source.DefaultRequestHeaders.Add(CounterpartyClientContract.HeaderName,
                CounterpartyClientContract.Version);
            using (var handshake = await source.GetAsync(PartiesUrl))
            {
                Assert.Equal(HttpStatusCode.OK, handshake.StatusCode);
                Assert.Equal(CounterpartyClientContract.Version,
                    Assert.Single(handshake.Headers.GetValues(CounterpartyClientContract.HeaderName)));
            }
            var created = await CreateReceiptAsync(source, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("Contract gate create"), key, PartyRole.None);
            var afterCreate = await ReadSnapshotAsync(connection);

            // Simulate a lost successful response followed by a stale-client retry.
            source.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
            using (var rejectedReplay = await PostJsonAsync(source, PartiesUrl, created.Json, key))
            {
                await AssertProblemAsync(rejectedReplay, HttpStatusCode.Conflict,
                    CounterpartyClientContract.RequiredCode);
                Assert.False(rejectedReplay.Headers.Contains("Idempotency-Replayed"));
            }
            Assert.Equal(afterCreate, await ReadSnapshotAsync(connection));
            source.DefaultRequestHeaders.Add(CounterpartyClientContract.HeaderName,
                CounterpartyClientContract.Version);
            await AssertReplayAsync(source, created, CreateJson("Contract gate create", "0"));
            Assert.Equal(afterCreate, await ReadSnapshotAsync(connection));

            target.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
            var linkKey = NewIdentity();
            using (var rejectedLink = await PostJsonAsync(target, LinkUrl(created.PartyId), "{}", linkKey))
            {
                await AssertProblemAsync(rejectedLink, HttpStatusCode.Conflict,
                    CounterpartyClientContract.RequiredCode);
            }
            using (var status = new HttpRequestMessage(HttpMethod.Put,
                       LinkUrl(created.PartyId) + "/active-state"))
            {
                source.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
                status.Content = new StringContent(
                    "{\"isActive\":false,\"expectedVersion\":1,\"reason\":\"Contract gate\"}",
                    Encoding.UTF8, "application/json");
                using var rejectedStatus = await source.SendAsync(status);
                await AssertProblemAsync(rejectedStatus, HttpStatusCode.Conflict,
                    CounterpartyClientContract.RequiredCode);
            }
            Assert.Equal(afterCreate, await ReadSnapshotAsync(connection));

            target.DefaultRequestHeaders.Add(CounterpartyClientContract.HeaderName,
                CounterpartyClientContract.Version);
            var linked = await CreateReceiptAsync(target, TestCompanyIds.CompanyB,
                LinkUrl(created.PartyId), "{}", linkKey, PartyRole.None);
            await AssertReplayAsync(target, linked, "{\"roles\":0}");
            await AssertSingleCreatedEffectAsync(connection, linked, PartyRole.None);
            var afterLink = await ReadSnapshotAsync(connection);

            using var wrongCompany = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyB);
            using (var forbidden = await PostJsonAsync(wrongCompany, PartiesUrl, created.Json, key))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var viewer = factory.CreateClientFor(TestUsers.CompanyAViewer, TestCompanyIds.CompanyA);
            using (var forbiddenReplay = await PostJsonAsync(viewer, PartiesUrl, created.Json, key))
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenReplay.StatusCode);
            using var loggedOut = factory.CreateClient();
            loggedOut.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString());
            using (var unauthenticated = await PostJsonAsync(loggedOut, PartiesUrl, created.Json, key))
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            Assert.Equal(afterLink, await ReadSnapshotAsync(connection));
        });
    }

    [SqlServerFact]
    public async Task ClientContractGate_WithholdsRolelessReadsWithoutLeakingForeignCompanyState()
    {
        await WithDatabaseAsync(async (factory, connection) =>
        {
            using var compatible = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            var legacy = await CreateReceiptAsync(compatible, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("Contract legacy fifteen", "15"), NewIdentity(), AllRoles);
            var roleless = await CreateReceiptAsync(compatible, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("Contract roleless"), NewIdentity(), PartyRole.None);
            using var oldReader = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            oldReader.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
            using var oldTarget = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            oldTarget.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
            var before = await ReadSnapshotAsync(connection);

            foreach (var url in new[] { PartiesUrl, $"{PartiesUrl}/{roleless.PartyId}" })
            {
                using var rejected = await oldReader.GetAsync(url);
                await AssertProblemAsync(rejected, HttpStatusCode.Conflict,
                    CounterpartyClientContract.RequiredCode);
                Assert.DoesNotContain(roleless.PartyId.ToString(),
                    await rejected.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
                Assert.True(rejected.Headers.CacheControl?.NoStore);
            }
            using (var candidates = await oldTarget.GetAsync(CandidatesUrl))
                await AssertProblemAsync(candidates, HttpStatusCode.Conflict,
                    CounterpartyClientContract.RequiredCode);
            using (var legacyDetail = await oldReader.GetAsync($"{PartiesUrl}/{legacy.PartyId}"))
                Assert.Equal(HttpStatusCode.OK, legacyDetail.StatusCode);
            using (var foreignScope = await oldTarget.GetAsync(PartiesUrl))
                Assert.Equal(HttpStatusCode.OK, foreignScope.StatusCode);

            oldReader.DefaultRequestHeaders.Add(CounterpartyClientContract.HeaderName,
                "optional-trade-roles-v2");
            using (var unsupported = await oldReader.GetAsync($"{PartiesUrl}/{legacy.PartyId}"))
                await AssertProblemAsync(unsupported, HttpStatusCode.Conflict,
                    CounterpartyClientContract.UnsupportedCode);

            using var unauthorizedReader = factory.CreateClientFor(
                TestUsers.CompanyAViewer, TestCompanyIds.CompanyA);
            foreach (var declaration in new string?[] { null, "optional-trade-roles-v2" })
            {
                unauthorizedReader.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
                if (declaration is not null)
                    unauthorizedReader.DefaultRequestHeaders.Add(CounterpartyClientContract.HeaderName, declaration);
                using var forbidden = await unauthorizedReader.GetAsync(PartiesUrl);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                Assert.False(forbidden.Headers.Contains(CounterpartyClientContract.HeaderName));
                var body = await forbidden.Content.ReadAsStringAsync();
                Assert.DoesNotContain(roleless.PartyId.ToString(), body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(legacy.PartyId.ToString(), body, StringComparison.OrdinalIgnoreCase);
            }

            using (var invalidFilter = await compatible.GetAsync(PartiesUrl + "?role=0"))
                Assert.Equal(HttpStatusCode.BadRequest, invalidFilter.StatusCode);
            using (var detail = await compatible.GetAsync($"{PartiesUrl}/{roleless.PartyId}"))
            {
                Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
                Assert.Equal(CounterpartyClientContract.Version,
                    Assert.Single(detail.Headers.GetValues(CounterpartyClientContract.HeaderName)));
                using var document = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
                var account = Assert.Single(document.RootElement.GetProperty("companyAccounts").EnumerateArray());
                Assert.Equal(0, account.GetProperty("roles").GetInt32());
                Assert.Equal("1", account.GetProperty("version").GetString());
            }
            Assert.Equal(before, await ReadSnapshotAsync(connection));
        });
    }

    [SqlServerFact]
    public async Task MissingAndZeroRoles_CreateAndLinkShareReplayAndRejectInvalidJson()
    {
        await WithDatabaseAsync(async (factory, connection) =>
        {
            using var source = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var target = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            var createKey = NewIdentity();
            var linkKey = NewIdentity();
            const string legalName = "SQL Optional Roles JSON Fixture";

            // Both numeric and named JSON strings must be rejected, as well as null,
            // negative masks and the first unknown bit. Each rejected key stays reusable.
            string[] invalidRoles =
                ["-1", "null", "\"0\"", "\"Customer\"", "16", "17", "0.0", "0.5", "15.5", "false"];
            var beforeCreate = await ReadSnapshotAsync(connection);
            foreach (var rolesJson in invalidRoles)
            {
                using var rejected = await PostJsonAsync(
                    source, PartiesUrl, CreateJson(legalName, rolesJson), createKey);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.False(rejected.Headers.Contains("Idempotency-Replayed"));
                Assert.Equal(beforeCreate, await ReadSnapshotAsync(connection));
            }

            var created = await CreateReceiptAsync(
                source, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson(legalName), createKey, PartyRole.None);
            await AssertSingleCreatedEffectAsync(connection, created, PartyRole.None);
            var afterCreate = await ReadSnapshotAsync(connection);
            await AssertReplayAsync(source, created, CreateJson(legalName, "0"));
            Assert.Equal(afterCreate, await ReadSnapshotAsync(connection));

            var linkUrl = LinkUrl(created.PartyId);
            foreach (var rolesJson in invalidRoles)
            {
                using var rejected = await PostJsonAsync(
                    target, linkUrl, LinkJson(rolesJson), linkKey);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.False(rejected.Headers.Contains("Idempotency-Replayed"));
                Assert.Equal(afterCreate, await ReadSnapshotAsync(connection));
            }

            var linked = await CreateReceiptAsync(
                target, TestCompanyIds.CompanyB, linkUrl, LinkJson(), linkKey, PartyRole.None);
            Assert.Equal(created.PartyId, linked.PartyId);
            Assert.NotEqual(created.AccountId, linked.AccountId);
            await AssertSingleCreatedEffectAsync(connection, linked, PartyRole.None);
            var afterLink = await ReadSnapshotAsync(connection);
            await AssertReplayAsync(target, linked, LinkJson("0"));
            Assert.Equal(afterLink, await ReadSnapshotAsync(connection));

            // None remains distinct from a real trade role in both fingerprints.
            using var createMismatch = await PostJsonAsync(
                source, PartiesUrl, CreateJson(legalName, RoleJson(PartyRole.Customer)), createKey);
            await AssertProblemAsync(
                createMismatch, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.IdempotencyConflict);
            using var linkMismatch = await PostJsonAsync(
                target, linkUrl, LinkJson(RoleJson(PartyRole.Supplier)), linkKey);
            await AssertProblemAsync(
                linkMismatch, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.IdempotencyConflict);
            Assert.Equal(afterLink, await ReadSnapshotAsync(connection));
            Assert.Equal(1, await CountAsync(connection, "[counterparty].[Parties]"));
            Assert.Equal(2, await CountAsync(connection, "[counterparty].[CompanyPartyAccounts]"));
        });
    }

    [SqlServerFact]
    public async Task PreviousSchemaUpgradeAndSafeDown_PreserveLegacyRolesAndDurableReplays()
    {
        await WithDatabaseAsync(async (factory, connection) =>
        {
            // Startup provisions the isolated database. Downgrade the still-empty
            // counterparty schema to the actual preceding migration before any fixture write.
            await MigrateAsync(factory.DatabaseConnectionString, PreviousMigration);
            Assert.Equal(2, await MigrationCountAsync(connection));
            using var source = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var target = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            var legacy = new List<MutationReceipt>();
            PartyRole[] legacyRoles = [PartyRole.Customer, PartyRole.Customer | PartyRole.Supplier, AllRoles];
            foreach (var roles in legacyRoles)
            {
                var created = await CreateReceiptAsync(
                    source, TestCompanyIds.CompanyA, PartiesUrl,
                    CreateJson($"SQL Previous Schema Roles {(int)roles}", RoleJson(roles)),
                    NewIdentity(), roles);
                var linked = await CreateReceiptAsync(
                    target, TestCompanyIds.CompanyB, LinkUrl(created.PartyId),
                    LinkJson(RoleJson(roles)), NewIdentity(), roles);
                legacy.Add(created);
                legacy.Add(linked);
                await AssertSingleCreatedEffectAsync(connection, created, roles);
                await AssertSingleCreatedEffectAsync(connection, linked, roles);
            }

            Assert.Equal(3, await CountAsync(connection, "[counterparty].[Parties]"));
            Assert.Equal(6, await CountAsync(connection, "[counterparty].[CompanyPartyAccounts]"));
            var beforeUpgrade = await ReadSnapshotAsync(connection);
            await AssertSqlMaskRejectedAsync(connection, legacy[0].AccountId, PartyRole.None);
            Assert.Equal(beforeUpgrade, await ReadSnapshotAsync(connection));

            await MigrateAsync(factory.DatabaseConnectionString, OptionalRolesMigration);
            Assert.Equal(3, await MigrationCountAsync(connection));
            Assert.Equal(beforeUpgrade, await ReadSnapshotAsync(connection));
            await AssertTrustedRolesConstraintAsync(connection);
            await AssertSqlMaskRejectedAsync(connection, legacy[0].AccountId, (PartyRole)(-1));
            await AssertSqlMaskRejectedAsync(connection, legacy[0].AccountId, (PartyRole)16);
            await AssertLegacyReplaysAsync(factory, connection, legacy, beforeUpgrade);

            // A safe down must restore 1..15 without rewriting any identities, timestamps,
            // rowversions, audits, request hashes or stored response bytes.
            await MigrateAsync(factory.DatabaseConnectionString, PreviousMigration);
            Assert.Equal(2, await MigrationCountAsync(connection));
            Assert.Equal(beforeUpgrade, await ReadSnapshotAsync(connection));
            await AssertSqlMaskRejectedAsync(connection, legacy[0].AccountId, PartyRole.None);
            await AssertTrustedRolesConstraintAsync(connection);
            await AssertLegacyReplaysAsync(factory, connection, legacy, beforeUpgrade);

            await MigrateAsync(factory.DatabaseConnectionString, OptionalRolesMigration);
            Assert.Equal(3, await MigrationCountAsync(connection));
            await AssertLegacyReplaysAsync(factory, connection, legacy, beforeUpgrade);
            await CreateReceiptAsync(
                source, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("SQL Reapplied Optional Roles Fixture"), NewIdentity(), PartyRole.None);
        });
    }

    [SqlServerFact]
    public async Task RolelessRows_BlockDownBeforeSchemaChanges_AndAllowForwardReapply()
    {
        await WithDatabaseAsync(async (factory, connection) =>
        {
            using var source = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var target = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            var created = await CreateReceiptAsync(
                source, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("SQL Blocked Down Roleless Fixture"), NewIdentity(), PartyRole.None);
            var linked = await CreateReceiptAsync(
                target, TestCompanyIds.CompanyB, LinkUrl(created.PartyId),
                LinkJson(), NewIdentity(), PartyRole.None);
            var beforeDown = await ReadSnapshotAsync(connection);
            var schemaBeforeDown = await ReadRolesSchemaAsync(connection);
            var historyBeforeDown = await ReadMigrationHistoryAsync(connection);

            var rejected = await Assert.ThrowsAsync<SqlException>(() =>
                MigrateAsync(factory.DatabaseConnectionString, PreviousMigration));
            Assert.Equal(51000, rejected.Number);
            Assert.Equal(schemaBeforeDown, await ReadRolesSchemaAsync(connection));
            Assert.Equal(historyBeforeDown, await ReadMigrationHistoryAsync(connection));
            Assert.Equal(beforeDown, await ReadSnapshotAsync(connection));

            // Also execute the generated Down with no wrapping transaction. A failed
            // ADD CHECK alone would leave a dropped constraint here; preflight must run first.
            string downScript;
            await using (var context = CreateMigrationContext(factory.DatabaseConnectionString))
            {
                downScript = context.GetService<IMigrator>().GenerateScript(
                    OptionalRolesMigration, PreviousMigration, MigrationsSqlGenerationOptions.NoTransactions);
            }

            var scriptRejected = await Assert.ThrowsAsync<SqlException>(() =>
                ExecuteScriptAsync(connection, downScript));
            Assert.Equal(51000, scriptRejected.Number);
            Assert.Equal(schemaBeforeDown, await ReadRolesSchemaAsync(connection));
            Assert.Equal(historyBeforeDown, await ReadMigrationHistoryAsync(connection));
            Assert.Equal(beforeDown, await ReadSnapshotAsync(connection));
            await AssertTrustedRolesConstraintAsync(connection);
            await AssertSqlMaskRejectedAsync(connection, created.AccountId, (PartyRole)(-1));
            await AssertSqlMaskRejectedAsync(connection, linked.AccountId, (PartyRole)16);
            await AssertReplayAsync(source, created, CreateJson("SQL Blocked Down Roleless Fixture", "0"));
            await AssertReplayAsync(target, linked, LinkJson("0"));
            Assert.Equal(beforeDown, await ReadSnapshotAsync(connection));

            // Forward repair re-executes the real migration's Up operations, without
            // inventing a role for existing None accounts or editing the migration journal.
            await using (var context = CreateMigrationContext(factory.DatabaseConnectionString))
            {
                var commands = context.GetService<IMigrationsSqlGenerator>().Generate(
                    new AllowCounterpartyAccountNoRole().UpOperations, context.Model);
                await using var transaction = await context.Database.BeginTransactionAsync();
                foreach (var command in commands)
                {
                    await context.Database.ExecuteSqlRawAsync(command.CommandText);
                }

                await transaction.CommitAsync();
            }

            await MigrateAsync(factory.DatabaseConnectionString, OptionalRolesMigration);
            Assert.Equal(historyBeforeDown, await ReadMigrationHistoryAsync(connection));
            Assert.Equal(beforeDown, await ReadSnapshotAsync(connection));
            await AssertTrustedRolesConstraintAsync(connection);
            await AssertSqlMaskRejectedAsync(connection, created.AccountId, (PartyRole)(-1));
            await AssertSqlMaskRejectedAsync(connection, linked.AccountId, (PartyRole)16);
            await AssertReplayAsync(source, created, created.Json);
            await AssertReplayAsync(target, linked, linked.Json);
            Assert.Equal(beforeDown, await ReadSnapshotAsync(connection));
            await CreateReceiptAsync(
                source, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("SQL Forward Repair Roleless Fixture", "0"), NewIdentity(), PartyRole.None);
        });
    }

    [SqlServerFact]
    public async Task RolelessReads_RespectReadOnlyAuthorizationAndCompanyIsolationWithoutCreatingAccounts()
    {
        await WithDatabaseAsync(async (factory, connection) =>
        {
            await GrantReadOnlyCompanyAccessAsync(factory);
            using var source = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var target = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            using var reader = factory.CreateClientFor(TestUsers.CompanyAViewer, TestCompanyIds.CompanyA);
            var own = await CreateReceiptAsync(
                source, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("SQL Readable Roleless A"), NewIdentity(), PartyRole.None);
            var foreign = await CreateReceiptAsync(
                target, TestCompanyIds.CompanyB, PartiesUrl,
                CreateJson("SQL Hidden Roleless B", "0"), NewIdentity(), PartyRole.None);
            var beforeReads = await ReadSnapshotAsync(connection);

            await AssertPageAsync(reader, [own.PartyId]);
            await AssertDetailAsync(reader, own);
            await AssertPageAsync(target, [foreign.PartyId]);
            using (var filtered = await GetJsonAsync(
                       reader, PartiesUrl + "?includeCount=true&role=Customer"))
            {
                Assert.Empty(filtered.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(0, filtered.RootElement.GetProperty("totalCount").GetInt64());
            }

            using (var candidates = await GetJsonAsync(target, CandidatesUrl))
            {
                var item = Assert.Single(candidates.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(own.PartyId, item.GetProperty("id").GetGuid());
                var account = Assert.Single(item.GetProperty("visibleSourceAccounts").EnumerateArray());
                Assert.Equal(own.AccountId, account.GetProperty("id").GetGuid());
                Assert.Equal(TestCompanyIds.CompanyA, account.GetProperty("companyId").GetGuid());
                Assert.Equal((int)PartyRole.None, account.GetProperty("roles").GetInt32());
            }

            using (var hidden = await reader.GetAsync($"{PartiesUrl}/{foreign.PartyId:D}"))
            {
                await AssertProblemAsync(hidden, HttpStatusCode.NotFound, CounterpartyProblemCodesV1.NotFound);
                Assert.DoesNotContain(foreign.PartyId.ToString("D"), await hidden.Content.ReadAsStringAsync());
            }

            using (var unlinked = await target.GetAsync($"{PartiesUrl}/{own.PartyId:D}"))
            {
                await AssertProblemAsync(unlinked, HttpStatusCode.NotFound, CounterpartyProblemCodesV1.NotFound);
            }

            using (var forbiddenCandidates = await reader.GetAsync(CandidatesUrl))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenCandidates.StatusCode);
            }

            foreach (var rolesJson in new string?[] { null, "0" })
            {
                using var deniedCreate = await PostJsonAsync(
                    reader, PartiesUrl, CreateJson("SQL Denied Roleless Write", rolesJson), NewIdentity());
                Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
                using var deniedLink = await PostJsonAsync(
                    reader, LinkUrl(foreign.PartyId), LinkJson(rolesJson), NewIdentity());
                Assert.Equal(HttpStatusCode.Forbidden, deniedLink.StatusCode);
            }

            using (var wrongCompany = factory.CreateClientFor(TestUsers.CompanyAViewer, TestCompanyIds.CompanyB))
            {
                using var deniedRead = await wrongCompany.GetAsync(PartiesUrl + "?includeCount=true");
                Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
                using var deniedLink = await PostJsonAsync(
                    wrongCompany, LinkUrl(own.PartyId), LinkJson(), NewIdentity());
                Assert.Equal(HttpStatusCode.Forbidden, deniedLink.StatusCode);
            }

            using (var anonymous = factory.CreateClient())
            {
                using var deniedRead = await anonymous.GetAsync(PartiesUrl);
                Assert.Equal(HttpStatusCode.Unauthorized, deniedRead.StatusCode);
                using var deniedCreate = await PostJsonAsync(
                    anonymous, PartiesUrl, CreateJson("SQL Anonymous Roleless Write"), NewIdentity());
                Assert.Equal(HttpStatusCode.Unauthorized, deniedCreate.StatusCode);
            }

            // In particular the candidate/list/detail GETs did not materialize a target account.
            Assert.Equal(beforeReads, await ReadSnapshotAsync(connection));
            Assert.Equal(2, await CountAsync(connection, "[counterparty].[CompanyPartyAccounts]"));
            var linked = await CreateReceiptAsync(
                target, TestCompanyIds.CompanyB, LinkUrl(own.PartyId),
                LinkJson(), NewIdentity(), PartyRole.None);
            var afterLink = await ReadSnapshotAsync(connection);
            await AssertDetailAsync(reader, own);
            await AssertDetailAsync(target, linked);
            await AssertPageAsync(reader, [own.PartyId]);
            await AssertPageAsync(target, [own.PartyId, foreign.PartyId]);
            using (var candidates = await GetJsonAsync(target, CandidatesUrl))
            {
                Assert.Empty(candidates.RootElement.GetProperty("items").EnumerateArray());
            }

            Assert.Equal(afterLink, await ReadSnapshotAsync(connection));
        });
    }

    [SqlServerFact]
    public async Task RolelessDifferentKeyLinkRace_CreatesOneAccountAndOneAudit()
    {
        var barrier = new LinkSaveBarrier();
        await WithDatabaseAsync(async (factory, connection) =>
        {
            using var source = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            var created = await CreateReceiptAsync(
                source, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("SQL Roleless Link Race"), NewIdentity(), PartyRole.None);
            using var targetA = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            using var targetB = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            var keys = new[] { NewIdentity(), NewIdentity() };
            var correlations = new[] { NewIdentity(), NewIdentity() };
            var payloads = new[] { LinkJson(), LinkJson("0") };
            var auditCountBefore = await CountAsync(connection, "[audit].[AuditEvents]");
            barrier.Arm(created.PartyId);

            var responses = await Task.WhenAll(
                PostJsonAsync(targetA, LinkUrl(created.PartyId), payloads[0], keys[0], correlations[0]),
                PostJsonAsync(targetB, LinkUrl(created.PartyId), payloads[1], keys[1], correlations[1]));
            try
            {
                Assert.Equal(2, barrier.Arrivals);
                var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
                var loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
                await AssertProblemAsync(loser, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.AlreadyLinked);
                Assert.All(responses, response => Assert.False(response.Headers.Contains("Idempotency-Replayed")));
                var winnerIndex = Array.IndexOf(responses, winner);
                var receipt = await ReadReceiptAsync(
                    winner, TestCompanyIds.CompanyB, LinkUrl(created.PartyId), payloads[winnerIndex],
                    keys[winnerIndex], correlations[winnerIndex], PartyRole.None);
                await AssertSingleCreatedEffectAsync(connection, receipt, PartyRole.None);
                Assert.Equal(1, await CountAsync(connection, "[counterparty].[Parties]"));
                Assert.Equal(2, await CountAsync(connection, "[counterparty].[CompanyPartyAccounts]"));
                Assert.Equal(auditCountBefore + 1, await CountAsync(connection, "[audit].[AuditEvents]"));
                Assert.Equal(2, await ScalarAsync<int>(connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId] = @CompanyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyB)));

                var afterRace = await ReadSnapshotAsync(connection);
                for (var index = 0; index < responses.Length; index++)
                {
                    using var replay = await PostJsonAsync(
                        targetA, LinkUrl(created.PartyId), payloads[1 - index], keys[index]);
                    Assert.Equal(responses[index].StatusCode, replay.StatusCode);
                    AssertReplayHeaders(replay, correlations[index]);
                    if (index == winnerIndex)
                    {
                        Assert.Equal(receipt.Body, await replay.Content.ReadAsStringAsync());
                    }
                    else
                    {
                        await AssertProblemAsync(replay, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.AlreadyLinked);
                    }

                    var outcome = index == winnerIndex
                        ? CounterpartyCommandResult.Created : CounterpartyCommandResult.AlreadyLinked;
                    Assert.Equal(1, await ScalarAsync<int>(connection,
                        """
                        SELECT COUNT(*) FROM [idempotency].[CommandExecutions]
                        WHERE [CompanyId] = @CompanyId AND [IdempotencyKeyHash] = @KeyHash
                          AND [Outcome] = @Outcome AND [OriginalCorrelationId] = @CorrelationId;
                        """,
                        new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                        new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(keys[index])),
                        new SqlParameter("@Outcome", outcome.ToString()),
                        new SqlParameter("@CorrelationId", correlations[index])));
                }

                Assert.Equal(afterRace, await ReadSnapshotAsync(connection));
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }, services => services.AddDbContext<CounterpartyDbContext>(
            options => options.AddInterceptors(barrier)));
    }

    [SqlServerFact]
    public async Task RolelessAuditAndIdempotencyFailures_RollBackAndPermitSameKeyRetry()
    {
        await WithDatabaseAsync(async (factory, connection) =>
        {
            using var source = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var target = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            foreach (var auditFailure in new[] { true, false })
            {
                var legalName = $"SQL Roleless {(auditFailure ? "Audit" : "Idempotency")} Rollback";
                var createKey = NewIdentity();
                await AssertInjectedFailureAsync(
                    connection, source, PartiesUrl, CreateJson(legalName), createKey, auditFailure);
                var created = await CreateReceiptAsync(
                    source, TestCompanyIds.CompanyA, PartiesUrl,
                    CreateJson(legalName, "0"), createKey, PartyRole.None);
                await AssertSingleCreatedEffectAsync(connection, created, PartyRole.None);
                var afterCreate = await ReadSnapshotAsync(connection);
                await AssertReplayAsync(source, created, CreateJson(legalName));
                Assert.Equal(afterCreate, await ReadSnapshotAsync(connection));

                var linkKey = NewIdentity();
                await AssertInjectedFailureAsync(
                    connection, target, LinkUrl(created.PartyId), LinkJson(), linkKey, auditFailure);
                var linked = await CreateReceiptAsync(
                    target, TestCompanyIds.CompanyB, LinkUrl(created.PartyId),
                    LinkJson("0"), linkKey, PartyRole.None);
                await AssertSingleCreatedEffectAsync(connection, linked, PartyRole.None);
                var afterLink = await ReadSnapshotAsync(connection);
                await AssertReplayAsync(target, linked, LinkJson());
                Assert.Equal(afterLink, await ReadSnapshotAsync(connection));
            }

            Assert.Equal(2, await CountAsync(connection, "[counterparty].[Parties]"));
            Assert.Equal(4, await CountAsync(connection, "[counterparty].[CompanyPartyAccounts]"));
        });
    }

    [SqlServerFact]
    [Trait("AcceptanceId", "CRO-AC-009")]
    public async Task RolelessInactiveAccount_StaleVersionAndPermissionGatesRemainEnforced()
    {
        await WithDatabaseAsync(async (factory, connection) =>
        {
            await GrantReadOnlyCompanyAccessAsync(factory);
            using var writer = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var reader = factory.CreateClientFor(TestUsers.CompanyAViewer, TestCompanyIds.CompanyA);
            using var foreign = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyB);
            var created = await CreateReceiptAsync(writer, TestCompanyIds.CompanyA, PartiesUrl,
                CreateJson("CRO-AC-009 roleless inactive", "0"), NewIdentity(), PartyRole.None);
            var statusUrl = LinkUrl(created.PartyId) + "/active-state";
            async Task<HttpResponseMessage> PutAsync(HttpClient client, bool active, long version, string key, string correlation)
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, statusUrl);
                request.Headers.Add("Idempotency-Key", key);
                request.Headers.Add("X-Correlation-Id", correlation);
                request.Content = new StringContent(JsonSerializer.Serialize(
                    new { isActive = active, expectedVersion = version, reason = "CRO-AC-009" }),
                    Encoding.UTF8, "application/json");
                return await client.SendAsync(request);
            }

            var auditsBefore = await CountAsync(connection, "[audit].[AuditEvents]");
            using (var inactive = await PutAsync(writer, false, 1, NewIdentity(), NewIdentity()))
                Assert.Equal(HttpStatusCode.NoContent, inactive.StatusCode);
            Assert.Equal(auditsBefore + 1, await CountAsync(connection, "[audit].[AuditEvents]"));
            Assert.Equal(1, await ScalarAsync<int>(connection,
                """
                SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts]
                WHERE [Id]=@AccountId AND [CompanyId]=@CompanyId AND [Roles]=0 AND [IsActive]=0 AND [Version]=2;
                """, new SqlParameter("@AccountId", created.AccountId),
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA)));
            var afterInactive = await ReadSnapshotAsync(connection);
            var staleKey = NewIdentity();
            var staleCorrelation = NewIdentity();
            using (var stale = await PutAsync(writer, true, 1, staleKey, staleCorrelation))
            {
                await AssertProblemAsync(stale, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.VersionConflict);
                using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
                Assert.Equal(1, problem.RootElement.GetProperty("conflict").GetProperty("expectedVersion").GetInt64());
                Assert.Equal(2, problem.RootElement.GetProperty("conflict").GetProperty("currentVersion").GetInt64());
                Assert.False(stale.Headers.Contains("Idempotency-Replayed"));
            }
            var afterStale = await ReadSnapshotAsync(connection);
            // The terminal 409 is durable; only its idempotency execution is allowed to change.
            Assert.Equal(afterInactive.Parties, afterStale.Parties);
            Assert.Equal(afterInactive.Accounts, afterStale.Accounts);
            Assert.Equal(afterInactive.Audits, afterStale.Audits);
            using (var replay = await PutAsync(writer, true, 1, staleKey, NewIdentity()))
            {
                await AssertProblemAsync(replay, HttpStatusCode.Conflict, CounterpartyProblemCodesV1.VersionConflict);
                AssertReplayHeaders(replay, staleCorrelation);
            }
            Assert.Equal(afterStale, await ReadSnapshotAsync(connection));

            foreach (var client in new[] { reader, foreign })
            {
                foreach (var version in new long[] { 1, 2 })
                {
                    using var denied = await PutAsync(client, true, version, NewIdentity(), NewIdentity());
                    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                    var body = await denied.Content.ReadAsStringAsync();
                    Assert.DoesNotContain("currentVersion", body, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(created.AccountId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
                    Assert.False(denied.Headers.Contains("Idempotency-Replayed"));
                }
            }
            // A previously recorded key cannot bypass reauthorization.
            using (var deniedReplay = await PutAsync(reader, true, 1, staleKey, NewIdentity()))
                Assert.Equal(HttpStatusCode.Forbidden, deniedReplay.StatusCode);
            using (var detail = await GetJsonAsync(reader, $"{PartiesUrl}/{created.PartyId:D}"))
            {
                var account = Assert.Single(detail.RootElement.GetProperty("companyAccounts").EnumerateArray());
                Assert.Equal(0, account.GetProperty("roles").GetInt32());
                Assert.False(account.GetProperty("isActive").GetBoolean());
                Assert.Equal("2", account.GetProperty("version").GetString());
            }
            Assert.Equal(afterStale, await ReadSnapshotAsync(connection));

            using (var active = await PutAsync(writer, true, 2, NewIdentity(), NewIdentity()))
                Assert.Equal(HttpStatusCode.NoContent, active.StatusCode);
            Assert.Equal(1, await ScalarAsync<int>(connection,
                """
                SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts]
                WHERE [Id]=@AccountId AND [CompanyId]=@CompanyId AND [Roles]=0 AND [IsActive]=1 AND [Version]=3;
                """, new SqlParameter("@AccountId", created.AccountId),
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA)));
            Assert.Equal(auditsBefore + 2, await CountAsync(connection, "[audit].[AuditEvents]"));
        });
    }

    private static async Task WithDatabaseAsync(
        Func<SqlServerRelationalApiFactory, SqlConnection, Task> scenario,
        Action<IServiceCollection>? configureServices = null)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString, configureServices);
        try
        {
            using var healthClient = factory.CreateClient();
            using var health = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await scenario(factory, connection);
        }
        finally
        {
            await factory.DisposeAsync();
            using (var poolKey = new SqlConnection(factory.DatabaseConnectionString))
            {
                SqlConnection.ClearPool(poolKey);
            }

            var builder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" };
            await using var cleanup = new SqlConnection(builder.ConnectionString);
            await cleanup.OpenAsync();
            // Only the factory-generated disposable database name is interpolated.
            await ExecuteAsync(cleanup, $"""
                IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{factory.DatabaseName}];
                END;
                """);
        }
    }

    private static CounterpartyDbContext CreateMigrationContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "counterparty"))
            .Options;
        return new CounterpartyDbContext(options, new CompanyContext());
    }

    private static async Task MigrateAsync(string connectionString, string migration)
    {
        await using var context = CreateMigrationContext(connectionString);
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static async Task ExecuteScriptAsync(SqlConnection connection, string script)
    {
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await ExecuteAsync(connection, batch);
            }
        }
    }

    private static async Task AssertSqlMaskRejectedAsync(
        SqlConnection connection, Guid accountId, PartyRole roles)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection,
            "UPDATE [counterparty].[CompanyPartyAccounts] SET [Roles] = @Roles WHERE [Id] = @Id;",
            new SqlParameter("@Roles", (int)roles), new SqlParameter("@Id", accountId)));
        Assert.Equal(547, exception.Number);
        Assert.Contains("CK_CompanyPartyAccounts_Roles", exception.Message, StringComparison.Ordinal);
    }

    private static async Task AssertTrustedRolesConstraintAsync(SqlConnection connection)
    {
        Assert.Equal(1, await ScalarAsync<int>(connection,
            """
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE [parent_object_id] = OBJECT_ID(N'[counterparty].[CompanyPartyAccounts]')
              AND [name] = N'CK_CompanyPartyAccounts_Roles'
              AND [is_disabled] = 0 AND [is_not_trusted] = 0;
            """));
    }

    private static Task<int> MigrationCountAsync(SqlConnection connection) =>
        CountAsync(connection, "[counterparty].[__EFMigrationsHistory]");

    private static Task<string> ReadMigrationHistoryAsync(SqlConnection connection) =>
        ScalarAsync<string>(connection,
            "SELECT (SELECT * FROM [counterparty].[__EFMigrationsHistory] ORDER BY [MigrationId] FOR JSON PATH, INCLUDE_NULL_VALUES);");

    private static Task<string> ReadRolesSchemaAsync(SqlConnection connection) =>
        ScalarAsync<string>(connection,
            """
            SELECT (SELECT [object_id], [name], [definition], [is_disabled], [is_not_trusted]
            FROM sys.check_constraints
            WHERE [parent_object_id] = OBJECT_ID(N'[counterparty].[CompanyPartyAccounts]')
              AND [name] = N'CK_CompanyPartyAccounts_Roles'
            FOR JSON PATH, INCLUDE_NULL_VALUES);
            """);

    private static async Task AssertLegacyReplaysAsync(
        SqlServerRelationalApiFactory factory, SqlConnection connection,
        IEnumerable<MutationReceipt> receipts, DatabaseSnapshot expected)
    {
        foreach (var receipt in receipts)
        {
            using var client = factory.CreateClientFor(TestUsers.GroupAdmin, receipt.CompanyId);
            await AssertReplayAsync(client, receipt, receipt.Json);
        }

        Assert.Equal(expected, await ReadSnapshotAsync(connection));
    }

    private static async Task GrantReadOnlyCompanyAccessAsync(SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = await context.ExternalIdentities
            .Where(identity => identity.Subject == TestUsers.CompanyAViewer)
            .Select(identity => identity.UserId).SingleAsync();
        var role = new RoleDefinition(Guid.NewGuid(), $"SQL_ROLELESS_READER_{Guid.NewGuid():N}",
            "SQL Optional Roles Reader", isSystem: false);
        role.AddPermission(ErpPermissions.CounterpartyPartiesRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), userId, role.Id, TestCompanyIds.CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
        assignment.AddScope(AccessScopeType.Company, TestCompanyIds.CompanyA);
        context.Roles.Add(role);
        context.RoleAssignments.Add(assignment);
        await context.SaveChangesAsync();
    }

    private static async Task AssertPageAsync(HttpClient client, Guid[] expectedIds)
    {
        using var document = await GetJsonAsync(client, PartiesUrl + "?includeCount=true");
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(expectedIds.Order(), items.Select(item => item.GetProperty("id").GetGuid()).Order());
        Assert.Equal((long)expectedIds.Length, document.RootElement.GetProperty("totalCount").GetInt64());
        Assert.False(document.RootElement.GetProperty("hasMore").GetBoolean());
        foreach (var item in items)
        {
            var account = Assert.Single(item.GetProperty("companyAccounts").EnumerateArray());
            Assert.Equal((int)PartyRole.None, account.GetProperty("roles").GetInt32());
        }
    }

    private static async Task AssertDetailAsync(HttpClient client, MutationReceipt expected)
    {
        using var document = await GetJsonAsync(client, $"{PartiesUrl}/{expected.PartyId:D}");
        Assert.Equal(expected.PartyId, document.RootElement.GetProperty("id").GetGuid());
        var account = Assert.Single(document.RootElement.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(expected.AccountId, account.GetProperty("id").GetGuid());
        Assert.Equal(expected.CompanyId, account.GetProperty("companyId").GetGuid());
        Assert.Equal((int)PartyRole.None, account.GetProperty("roles").GetInt32());
        Assert.Equal("1", account.GetProperty("version").GetString());
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("externalIntegrationCode", body, StringComparison.OrdinalIgnoreCase);
        return JsonDocument.Parse(body);
    }

    private static async Task AssertInjectedFailureAsync(
        SqlConnection connection, HttpClient client, string url, string json, string key, bool auditFailure)
    {
        var snapshot = await ReadSnapshotAsync(connection);
        var trigger = auditFailure
            ? "[audit].[TR_AuditEvents_RejectOptionalRolesProbe]"
            : "[idempotency].[TR_CommandExecutions_RejectOptionalRolesProbe]";
        var table = auditFailure ? "[audit].[AuditEvents]" : "[idempotency].[CommandExecutions]";
        await ExecuteAsync(connection, $"""
            CREATE TRIGGER {trigger} ON {table}
            AFTER INSERT AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51041, 'Optional roles atomicity rejection probe.', 1;
            END;
            """);
        try
        {
            using var response = await PostJsonAsync(client, url, json, key);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.False(response.Headers.Contains("Idempotency-Original-Correlation-Id"));
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP TRIGGER {trigger};");
        }

        Assert.Equal(snapshot, await ReadSnapshotAsync(connection));
    }

    private static string NewIdentity() => $"sql-optional-roles-{Guid.NewGuid():N}";
    private static string LinkUrl(Guid partyId) => $"{PartiesUrl}/{partyId:D}/company-account";
    private static string RoleJson(PartyRole roles) => JsonSerializer.Serialize(roles);

    private static string CreateJson(string legalName, string? rolesJson = null)
    {
        var fields = $"\"kind\":{(int)PartyKind.LegalEntity},\"legalName\":{JsonSerializer.Serialize(legalName)}";
        return "{" + fields + (rolesJson is null ? "" : ",\"roles\":" + rolesJson) + "}";
    }

    private static string LinkJson(string? rolesJson = null) =>
        rolesJson is null ? "{}" : "{\"roles\":" + rolesJson + "}";

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client, string url, string json, string key, string? correlation = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", correlation ?? NewIdentity());
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<MutationReceipt> CreateReceiptAsync(
        HttpClient client, Guid companyId, string url, string json, string key, PartyRole roles)
    {
        var correlation = NewIdentity();
        using var response = await PostJsonAsync(client, url, json, key, correlation);
        return await ReadReceiptAsync(response, companyId, url, json, key, correlation, roles);
    }

    private static async Task<MutationReceipt> ReadReceiptAsync(
        HttpResponseMessage response, Guid companyId, string url, string json,
        string key, string correlation, PartyRole roles)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("externalIntegrationCode", body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        var partyId = document.RootElement.GetProperty("id").GetGuid();
        var account = Assert.Single(document.RootElement.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(companyId, account.GetProperty("companyId").GetGuid());
        Assert.Equal((int)roles, account.GetProperty("roles").GetInt32());
        Assert.Equal("1", account.GetProperty("version").GetString());
        Assert.Equal("1", document.RootElement.GetProperty("version").GetString());
        var location = response.Headers.Location?.ToString();
        Assert.Equal($"{PartiesUrl}/{partyId}", location);
        return new MutationReceipt(companyId, url, json, key, correlation, body, location!,
            partyId, account.GetProperty("id").GetGuid());
    }

    private static async Task AssertReplayAsync(HttpClient client, MutationReceipt receipt, string json)
    {
        using var replay = await PostJsonAsync(client, receipt.Url, json, receipt.Key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        AssertReplayHeaders(replay, receipt.Correlation);
        Assert.Equal(receipt.Body, await replay.Content.ReadAsStringAsync());
        Assert.Equal(receipt.Location, replay.Headers.Location?.ToString());
    }

    private static void AssertReplayHeaders(HttpResponseMessage response, string originalCorrelation)
    {
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(originalCorrelation,
            Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertSingleCreatedEffectAsync(
        SqlConnection connection, MutationReceipt receipt, PartyRole roles)
    {
        Assert.Equal(1, await ScalarAsync<int>(connection,
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id] = @PartyId;",
            new SqlParameter("@PartyId", receipt.PartyId)));
        Assert.Equal(1, await ScalarAsync<int>(connection,
            """
            SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts]
            WHERE [Id] = @AccountId AND [CompanyId] = @CompanyId AND [PartyId] = @PartyId
              AND [Roles] = @Roles AND [IsActive] = 1 AND [Version] = 1;
            """,
            new SqlParameter("@AccountId", receipt.AccountId), new SqlParameter("@CompanyId", receipt.CompanyId),
            new SqlParameter("@PartyId", receipt.PartyId), new SqlParameter("@Roles", (int)roles)));
        var isCreate = receipt.Url == PartiesUrl;
        var permission = isCreate ? ErpPermissions.CounterpartyPartiesCreate : ErpPermissions.CounterpartyAccountsCreate;
        var eventType = isCreate ? "counterparty.party.created.v1" : "counterparty.company-account.created.v1";
        var entityId = isCreate ? receipt.PartyId : receipt.AccountId;
        Assert.Equal(1, await ScalarAsync<int>(connection,
            """
            SELECT COUNT(*) FROM [audit].[AuditEvents]
            WHERE [CompanyId] = @CompanyId AND [IdempotencyKeyHash] = @KeyHash;
            """,
            new SqlParameter("@CompanyId", receipt.CompanyId),
            new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(receipt.Key))));
        var auditJson = await ScalarAsync<string>(connection,
            """
            SELECT [AfterJson] FROM [audit].[AuditEvents]
            WHERE [CompanyId] = @CompanyId AND [CorrelationId] = @CorrelationId
              AND [EntityId] = @EntityId AND [EventType] = @EventType
              AND [PermissionCode] = @Permission AND [Outcome] = N'Succeeded';
            """,
            new SqlParameter("@CompanyId", receipt.CompanyId), new SqlParameter("@CorrelationId", receipt.Correlation),
            new SqlParameter("@EntityId", entityId.ToString("D")), new SqlParameter("@EventType", eventType),
            new SqlParameter("@Permission", permission));
        using (var audit = JsonDocument.Parse(auditJson))
        {
            var account = isCreate ? audit.RootElement.GetProperty("CompanyAccount") : audit.RootElement;
            Assert.Equal(receipt.AccountId, account.GetProperty("Id").GetGuid());
            Assert.Equal(receipt.CompanyId, account.GetProperty("CompanyId").GetGuid());
            Assert.Equal(receipt.PartyId, account.GetProperty("PartyId").GetGuid());
            Assert.Equal((int)roles, account.GetProperty("Roles").GetInt32());
        }

        Assert.Equal(1, await ScalarAsync<int>(connection,
            """
            SELECT COUNT(*) FROM [idempotency].[CommandExecutions]
            WHERE [CompanyId] = @CompanyId AND [IdempotencyKeyHash] = @KeyHash
              AND [CommandName] = @CommandName AND [Outcome] = @Outcome
              AND [OriginalCorrelationId] = @CorrelationId AND [SchemaVersion] = 1
              AND [HttpStatusCode] = @StatusCode;
            """,
            new SqlParameter("@CompanyId", receipt.CompanyId),
            new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(receipt.Key)),
            new SqlParameter("@CommandName", isCreate ? "counterparty.party.create.v1" : "counterparty.company-account.create.v1"),
            new SqlParameter("@Outcome", CounterpartyCommandResult.Created.ToString()),
            new SqlParameter("@CorrelationId", receipt.Correlation),
            new SqlParameter("@StatusCode", (int)HttpStatusCode.Created)));
        var durableJson = await ScalarAsync<string>(connection,
            """
            SELECT [ResponseJson] FROM [idempotency].[CommandExecutions]
            WHERE [CompanyId] = @CompanyId AND [IdempotencyKeyHash] = @KeyHash;
            """,
            new SqlParameter("@CompanyId", receipt.CompanyId),
            new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(receipt.Key)));
        var durable = JsonSerializer.Deserialize<CounterpartyCommandResponse>(durableJson);
        Assert.NotNull(durable);
        Assert.NotNull(durable.Resource);
        Assert.Equal(receipt.PartyId, durable.Resource.Id);
        var durableAccount = Assert.Single(durable.Resource.CompanyAccounts);
        Assert.Equal(receipt.AccountId, durableAccount.Id);
        Assert.Equal(receipt.CompanyId, durableAccount.CompanyId);
        Assert.Equal(roles, durableAccount.Roles);
    }

    private static async Task<DatabaseSnapshot> ReadSnapshotAsync(SqlConnection connection)
    {
        // SELECT * also protects rowversion/timestamps and the complete durable JSON/hash
        // payload. SQL sequence allocations are intentionally excluded: gaps may survive rollback.
        return new DatabaseSnapshot(
            await ScalarAsync<string>(connection, "SELECT COALESCE((SELECT * FROM [counterparty].[Parties] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]');"),
            await ScalarAsync<string>(connection, "SELECT COALESCE((SELECT * FROM [counterparty].[CompanyPartyAccounts] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]');"),
            await ScalarAsync<string>(connection, "SELECT COALESCE((SELECT * FROM [audit].[AuditEvents] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]');"),
            await ScalarAsync<string>(connection, "SELECT COALESCE((SELECT * FROM [idempotency].[CommandExecutions] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]');"));
    }

    private static Task<int> CountAsync(SqlConnection connection, string fixtureTable) =>
        ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM {fixtureTable};");

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record MutationReceipt(Guid CompanyId, string Url, string Json, string Key,
        string Correlation, string Body, string Location, Guid PartyId, Guid AccountId);

    private sealed record DatabaseSnapshot(string Parties, string Accounts, string Audits, string Executions);

    private sealed class LinkSaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Guid _partyId;
        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public void Arm(Guid partyId) => _partyId = partyId;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_partyId != Guid.Empty && eventData.Context is CounterpartyDbContext context
                && context.ChangeTracker.Entries<CompanyPartyAccount>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.PartyId == _partyId
                    && entry.Entity.CompanyId == TestCompanyIds.CompanyB))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                {
                    _bothArrived.TrySetResult();
                }

                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }
    }
}
