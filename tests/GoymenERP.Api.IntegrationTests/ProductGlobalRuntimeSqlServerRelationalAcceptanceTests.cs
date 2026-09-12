using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Claims;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// Reuses the repository's real SQL host/lifecycle. These grants name only the
// existing synthetic development actor, never Sinan or a normal deployment.
public sealed class ProductGlobalRuntimeSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid GroupId = Guid.Parse("82000000-0000-0000-0000-000000000001");

    [SqlServerFact]
    public Task ProductGlobalRuntime_ReadCreateEditReplayAndCompanyIsolation_AreDurable() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var companyOnly = factory.CreateClientFor("company-a-user", CompanyA);
            var empty = await SnapshotAsync(factory.DatabaseConnectionString);
            foreach (var path in new[] { "/api/v1/products", "/api/v1/products/code-groups",
                $"/api/v1/products/reference-options?groupId={GroupId:D}" })
            {
                using var read = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                using var denied = await companyOnly.GetAsync(path);
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            Assert.Equal(empty, await SnapshotAsync(factory.DatabaseConnectionString));

            const string createKey = "product-sql-create-once";
            using var created = await CreateAsync(client, createKey, "SQL global product");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var first = (await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.False(first.IsReplay);
            Assert.Equal("1", first.Card.Product.Version);
            Assert.Equal("Draft", first.Card.Product.Lifecycle);
            var afterCreate = await SnapshotAsync(factory.DatabaseConnectionString);
            using var replay = await CreateAsync(client, createKey, "SQL global product");
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            var repeated = (await replay.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.True(repeated.IsReplay);
            Assert.Equal(first.Card.Product.Id, repeated.Card.Product.Id);
            Assert.Equal(afterCreate, await SnapshotAsync(factory.DatabaseConnectionString));

            using var otherOrigin = factory.CreateClientFor("group-bootstrap-admin", CompanyB);
            using var originConflict = await CreateAsync(otherOrigin, createKey, "SQL global product");
            Assert.Equal(HttpStatusCode.Conflict, originConflict.StatusCode);
            using var detail = await otherOrigin.GetAsync($"/api/v1/products/{first.Card.Product.Id:D}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal(afterCreate, await SnapshotAsync(factory.DatabaseConnectionString));

            using var edit = await EditAsync(client, first.Card.Product.Id, "product-sql-edit-once", "1", "SQL edited product");
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            var edited = (await edit.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.Equal("2", edited.Card.Product.Version);
            var afterEdit = await SnapshotAsync(factory.DatabaseConnectionString);
            using var stale = await EditAsync(client, first.Card.Product.Id, "product-sql-edit-stale", "1", "Stale must not persist");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var staleBody = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
            Assert.Equal("version_conflict", staleBody.RootElement.GetProperty("code").GetString());
            Assert.Equal("1", staleBody.RootElement.GetProperty("expectedVersion").GetString());
            Assert.Equal("2", staleBody.RootElement.GetProperty("currentVersion").GetString());
            using var deniedWrite = await EditAsync(companyOnly, first.Card.Product.Id, "product-sql-denied", "2", "Denied");
            Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
            Assert.Equal(afterEdit, await SnapshotAsync(factory.DatabaseConnectionString));

            await using var sql = new SqlConnection(factory.DatabaseConnectionString);
            await sql.OpenAsync();
            await using var audit = sql.CreateCommand();
            audit.CommandText = """
                SELECT COUNT(*) FROM [audit].[AuditEvents]
                WHERE [SubjectKind] = N'GroupSharedProduct' AND [CompanyId] IS NULL
                  AND [OriginCompanyId] = @origin AND [SchemaVersion] = 2
                  AND [EntityType] = N'Product.Product' AND [EntityId] = @id
                  AND [AuthorityRevision] IS NOT NULL;
                """;
            audit.Parameters.AddWithValue("@origin", CompanyA);
            audit.Parameters.AddWithValue("@id", first.Card.Product.Id.ToString("D"));
            Assert.Equal(2, Convert.ToInt32(await audit.ExecuteScalarAsync()));
            using var companyAudit = await client.GetAsync($"/api/v1/audit/events?entityId={first.Card.Product.Id:D}");
            Assert.Equal(HttpStatusCode.OK, companyAudit.StatusCode);
            Assert.DoesNotContain(first.Card.Product.Id.ToString("D"), await companyAudit.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            await AssertOwnerHistoryAsync(factory, first.Card.Product.Id);
            await AssertDatabaseConstraintsAsync(factory.DatabaseConnectionString);
            await using var migrationScope = factory.Services.CreateAsyncScope();
            await AssertRollbackRefusedAsync(migrationScope.ServiceProvider.GetRequiredService<AuditDbContext>(), 51041);
            await AssertRollbackRefusedAsync(migrationScope.ServiceProvider.GetRequiredService<IdentityDbContext>(), 51042);
            await AssertRollbackRefusedAsync(migrationScope.ServiceProvider.GetRequiredService<GroupSharedCommandDbContext>(), 51043);
            await AssertRollbackRefusedAsync(migrationScope.ServiceProvider.GetRequiredService<ProductDbContext>(), 51044);
            Assert.Equal(afterEdit, await SnapshotAsync(factory.DatabaseConnectionString));
        });

    [SqlServerFact]
    public Task ProductGlobalRuntime_EachFlushFailureAndLateRevocation_RollBackAllOwners() =>
        WithDatabaseAsync(async (factory, observer) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var before = await SnapshotAsync(factory.DatabaseConnectionString);
            foreach (var failureStage in Enum.GetValues<ProductCommandAttemptStage>())
            {
                observer.Callback = (stage, _, _) => stage == failureStage
                    ? Task.FromException(new InjectedProductFailureException()) : Task.CompletedTask;
                using var failed = await CreateAsync(client, $"product-fail-{failureStage}", "Must roll back");
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));
            }
            observer.Callback = async (stage, _, cancellationToken) =>
            {
                if (stage != ProductCommandAttemptStage.BeforeCommit) return;
                await using var scope = factory.Services.CreateAsyncScope();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var grant = await identity.Set<ProductMasterAssignment>()
                    .SingleAsync(row => row.UserId == ActorId && row.Operation == ProductMasterOperation.DraftCreate, cancellationToken);
                Assert.Equal(8, grant.RowVersion.Length);
                grant.Revoke(Guid.NewGuid(), "Synthetic isolated SQL late revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync(cancellationToken);
            };
            using var revoked = await CreateAsync(client, "product-late-revocation", "Revoked before commit");
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));
            observer.Callback = null;
            using var retry = await CreateAsync(client, "product-late-revocation", "Revoked before commit");
            Assert.Equal(HttpStatusCode.Forbidden, retry.StatusCode);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));
        });

    [SqlServerFact]
    public Task ProductGlobalRuntime_ConcurrentDuplicateAndSharedPrefixCommands_HaveSingleEffects() =>
        WithDatabaseAsync(async (factory, observer) =>
        {
            using var firstClient = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var secondClient = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var staged = new TwoAttemptStageBarrier();
            observer.Callback = staged.ObserveAsync;
            var duplicates = await Task.WhenAll(
                CreateAsync(firstClient, "product-concurrent-same-key", "Concurrent duplicate"),
                CreateAsync(secondClient, "product-concurrent-same-key", "Concurrent duplicate"));
            foreach (var response in duplicates) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var first = (await duplicates[0].Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            var second = (await duplicates[1].Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.Equal(first.Card.Product.Id, second.Card.Product.Id);
            Assert.NotEqual(first.IsReplay, second.IsReplay);
            Assert.True(staged.ArrivalCount >= 2);
            foreach (var response in duplicates) response.Dispose();

            staged = new TwoAttemptStageBarrier();
            observer.Callback = staged.ObserveAsync;
            var distinct = await Task.WhenAll(
                CreateAsync(firstClient, "product-concurrent-first", "Concurrent first"),
                CreateAsync(secondClient, "product-concurrent-second", "Concurrent second", Guid.Parse("82000000-0000-0000-0000-000000000002")));
            foreach (var response in distinct) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var left = (await distinct[0].Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            var right = (await distinct[1].Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.NotEqual(left.Card.Product.Code, right.Card.Product.Code);
            Assert.NotEqual(left.Card.Product.Id, right.Card.Product.Id);
            Assert.True(staged.ArrivalCount >= 3); // Two original reads plus the real counter-conflict retry.
            foreach (var response in distinct) response.Dispose();
            await using var sql = new SqlConnection(factory.DatabaseConnectionString);
            await sql.OpenAsync();
            await using var counts = sql.CreateCommand();
            counts.CommandText = """
                SELECT (SELECT COUNT(*) FROM [product].[Products]),
                    (SELECT COUNT(*) FROM [product].[CodeRegistrations]),
                    (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [SubjectKind] = N'GroupSharedProduct'),
                    (SELECT COUNT(*) FROM [idempotency].[GroupSharedCommandTerminals]),
                    (SELECT [LastSequence] FROM [product].[CodeCounters] WHERE [Prefix] = 'PSQL');
                """;
            await using var reader = await counts.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            for (var index = 0; index < 4; index++) Assert.Equal(3, reader.GetInt32(index));
            Assert.Equal(3, reader.GetInt64(4));
            await reader.DisposeAsync();

            // Both identical edits load v1 before either writes. The late loser
            // must recover the winner's terminal, not manufacture a 409.
            staged = new TwoAttemptStageBarrier();
            observer.Callback = staged.ObserveAsync;
            var edits = await Task.WhenAll(
                EditAsync(firstClient, first.Card.Product.Id, "product-same-edit-key", "1", "Same logical edit"),
                EditAsync(secondClient, first.Card.Product.Id, "product-same-edit-key", "1", "Same logical edit"));
            Assert.Equal(2, staged.ArrivalCount);
            foreach (var response in edits) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var editResults = await Task.WhenAll(edits.Select(response => response.Content.ReadFromJsonAsync<ProductMutationResponse>()));
            Assert.NotEqual(editResults[0]!.IsReplay, editResults[1]!.IsReplay);
            Assert.All(editResults, result => Assert.Equal("2", result!.Card.Product.Version));
            Assert.Equal(JsonSerializer.Serialize(editResults[0]!.Card), JsonSerializer.Serialize(editResults[1]!.Card));
            var replayIndex = editResults[0]!.IsReplay ? 0 : 1;
            Assert.Equal("true", Assert.Single(edits[replayIndex].Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(Assert.Single(edits[1 - replayIndex].Headers.GetValues("X-Correlation-Id")),
                Assert.Single(edits[replayIndex].Headers.GetValues("Idempotency-Original-Correlation-Id")));
            foreach (var response in edits) response.Dispose();
            counts.CommandText = """
                SELECT (SELECT COUNT(*) FROM [product].[Products]),
                    (SELECT COUNT(*) FROM [product].[CodeRegistrations]),
                    (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [SubjectKind] = N'GroupSharedProduct'),
                    (SELECT COUNT(*) FROM [idempotency].[GroupSharedCommandTerminals]);
                """;
            await using (var effects = await counts.ExecuteReaderAsync())
            {
                Assert.True(await effects.ReadAsync());
                Assert.Equal(3, effects.GetInt32(0));
                Assert.Equal(3, effects.GetInt32(1));
                Assert.Equal(4, effects.GetInt32(2));
                Assert.Equal(4, effects.GetInt32(3));
            }
            var afterConcurrentEdit = await SnapshotAsync(factory.DatabaseConnectionString);
            using var completeSnapshot = JsonDocument.Parse(afterConcurrentEdit);
            Assert.True(afterConcurrentEdit.Length > 8192);
            using var retryEdit = await EditAsync(firstClient, first.Card.Product.Id, "product-same-edit-key", "1", "Same logical edit");
            Assert.Equal(HttpStatusCode.OK, retryEdit.StatusCode);
            Assert.True((await retryEdit.Content.ReadFromJsonAsync<ProductMutationResponse>())!.IsReplay);
            Assert.Equal(afterConcurrentEdit, await SnapshotAsync(factory.DatabaseConnectionString));

            // Exercise the SAME reader with a tail-only difference in a real SQL
            // JSON result longer than a transport fragment. Scalar-prefix reads
            // would either fail JSON parsing or incorrectly compare equal.
            counts.CommandText = "SELECT REPLICATE(CAST(N'x' AS nvarchar(max)), 8192) AS padding, @tail AS tail FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;";
            counts.Parameters.AddWithValue("@tail", "before");
            var beforeTail = await ReadJsonFragmentsAsync(counts);
            counts.Parameters["@tail"].Value = "after";
            var afterTail = await ReadJsonFragmentsAsync(counts);
            Assert.Equal(beforeTail[..4096], afterTail[..4096]);
            Assert.NotEqual(beforeTail, afterTail);
            using var beforeDocument = JsonDocument.Parse(beforeTail);
            using var afterDocument = JsonDocument.Parse(afterTail);
            Assert.Equal("before", beforeDocument.RootElement.GetProperty("tail").GetString());
            Assert.Equal("after", afterDocument.RootElement.GetProperty("tail").GetString());
        });

    [SqlServerFact]
    public Task ProductGlobalRuntime_TwoEditsStagedAtVersionOne_ProduceOneLateEfConflict() =>
        WithDatabaseAsync(async (factory, observer) =>
        {
            using var first = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var second = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var create = await CreateAsync(first, "product-late-ef-create", "Before edit race");
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var product = (await create.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product;
            var staged = new TwoAttemptStageBarrier();
            observer.Callback = staged.ObserveAsync;
            var responses = await Task.WhenAll(
                EditAsync(first, product.Id, "product-late-ef-first", "1", "First staged edit"),
                EditAsync(second, product.Id, "product-late-ef-second", "1", "Second staged edit"));
            Assert.Equal(2, staged.ArrivalCount); // Both loaded/staged v1 before either EF UPDATE.
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("2", (await winner.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Version);
            using var problem = JsonDocument.Parse(await loser.Content.ReadAsStringAsync());
            Assert.Equal("version_conflict", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal("1", problem.RootElement.GetProperty("expectedVersion").GetString());
            Assert.Equal("2", problem.RootElement.GetProperty("currentVersion").GetString());
            foreach (var response in responses) response.Dispose();
            await using var sql = new SqlConnection(factory.DatabaseConnectionString);
            await sql.OpenAsync();
            await using var counts = sql.CreateCommand();
            counts.CommandText = """
                SELECT (SELECT COUNT(*) FROM [product].[Products]),
                    (SELECT COUNT(*) FROM [product].[CodeRegistrations]),
                    (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [SubjectKind] = N'GroupSharedProduct'),
                    (SELECT COUNT(*) FROM [idempotency].[GroupSharedCommandTerminals]);
                """;
            await using var reader = await counts.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(2, reader.GetInt32(2));
            Assert.Equal(2, reader.GetInt32(3));
            await reader.DisposeAsync();

            // Produce a real late EF failure, then revoke the losing actor before
            // the host can disclose the observed version metadata.
            var arrivals = 0;
            observer.Callback = async (stage, _, cancellationToken) =>
            {
                if (stage != ProductCommandAttemptStage.ProductStaged || Interlocked.Increment(ref arrivals) != 1) return;
                using var intervening = await EditAsync(second, product.Id, "product-late-ef-before-revoke", "2", "Committed before revoke");
                Assert.Equal(HttpStatusCode.OK, intervening.StatusCode);
                await using var scope = factory.Services.CreateAsyncScope();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var assignment = await identity.Set<ProductMasterAssignment>()
                    .SingleAsync(row => row.UserId == ActorId && row.Operation == ProductMasterOperation.DraftBasicEdit, cancellationToken);
                assignment.Revoke(Guid.NewGuid(), "Synthetic isolated SQL conflict disclosure revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync(cancellationToken);
            };
            using var masked = await EditAsync(first, product.Id, "product-late-ef-after-revoke", "2", "Must neither persist nor disclose conflict");
            Assert.Equal(HttpStatusCode.Forbidden, masked.StatusCode);
            using var maskedBody = JsonDocument.Parse(await masked.Content.ReadAsStringAsync());
            Assert.False(maskedBody.RootElement.TryGetProperty("expectedVersion", out _));
            Assert.False(maskedBody.RootElement.TryGetProperty("currentVersion", out _));
            Assert.Equal(2, arrivals);
            counts.CommandText = "SELECT [Version] FROM [product].[Products];";
            Assert.Equal(3L, await counts.ExecuteScalarAsync());
        });

    private static async Task AssertOwnerHistoryAsync(SqlServerRelationalApiFactory factory, Guid productId)
    {
        // This is a real owner-reader SQL test with synthetic local claim context,
        // not an additional HTTP endpoint or browser/OIDC acceptance claim.
        await using var scope = factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        try
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(GoymenClaimTypes.UserId, ActorId.ToString("D")),
                    new Claim(GoymenClaimTypes.CompanyAccess, CompanyA.ToString("D")),
                ], "IsolatedSqlFixture")),
            };
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var history = scope.ServiceProvider.GetRequiredService<GroupSharedProductAuditReader>();
            Assert.Equal(2, (await history.ReadAsync(productId)).Count);
            Assert.Empty(await history.ReadAsync(Guid.NewGuid()));
            accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(GoymenClaimTypes.UserId, "20000000-0000-0000-0000-000000000001"),
                new Claim(GoymenClaimTypes.CompanyAccess, CompanyA.ToString("D")),
            ], "IsolatedSqlFixture"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => history.ReadAsync(productId));
        }
        finally { accessor.HttpContext = previous; }
    }

    private static async Task AssertDatabaseConstraintsAsync(string connectionString)
    {
        await using var sql = new SqlConnection(connectionString);
        await sql.OpenAsync();
        await using (var appendOnly = sql.CreateCommand())
        {
            appendOnly.CommandText = "UPDATE [audit].[AuditEvents] SET [SchemaVersion] = 1 WHERE [SubjectKind] = N'GroupSharedProduct';";
            Assert.Equal(51000, (await Assert.ThrowsAsync<SqlException>(() => appendOnly.ExecuteNonQueryAsync())).Number);
        }
        // The existing append-only trigger correctly dominates UPDATE. Exercise
        // the new subject CHECK with an INSERT, without disabling that trigger.
        var auditColumns = new List<string>();
        await using (var columns = sql.CreateCommand())
        {
            columns.CommandText = "SELECT [name] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[audit].[AuditEvents]') AND [is_computed] = 0 AND [is_identity] = 0 ORDER BY [column_id];";
            await using var names = await columns.ExecuteReaderAsync();
            while (await names.ReadAsync()) auditColumns.Add(names.GetString(0));
        }
        Assert.Contains("Id", auditColumns);
        Assert.Contains("SchemaVersion", auditColumns);
        static string QuoteColumn(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
        var invalidSubjectInsert = "INSERT INTO [audit].[AuditEvents] (" + string.Join(",", auditColumns.Select(QuoteColumn))
            + ") SELECT TOP (1) " + string.Join(",", auditColumns.Select(name => name == "Id" ? "NEWID()"
                : name == "SchemaVersion" ? "1" : QuoteColumn(name)))
            + " FROM [audit].[AuditEvents] WHERE [SubjectKind] = N'GroupSharedProduct';";
        foreach (var (statement, constraint) in new[]
        {
            (invalidSubjectInsert, "CK_AuditEvents_Subject"),
            ("UPDATE [identity].[ProductMasterAssignments] SET [Operation] = 99;", "CK_ProductMasterAssignments_Operation"),
            ("UPDATE [identity].[ProductMasterAssignments] SET [IsActive] = 0;", "CK_ProductMasterAssignments_Revocation"),
            ("UPDATE [product].[Products] SET [Version] = 0;", "CK_Products_LifecycleVersion"),
            ("UPDATE [product].[Products] SET [PurchasableValue] = 1, [PurchasableProvenance] = 0;", "CK_Products_Purchasable"),
            ("UPDATE [idempotency].[GroupSharedCommandTerminals] SET [SchemaVersion] = 0;", "CK_GroupSharedTerminals_Response"),
        })
        {
            await using var command = sql.CreateCommand();
            command.CommandText = statement;
            var failure = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(547, failure.Number);
            Assert.Contains(constraint, failure.Message, StringComparison.Ordinal);
        }
    }

    private static async Task AssertRollbackRefusedAsync(DbContext database, int errorNumber)
    {
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(applied);
        var target = applied.Length == 1 ? Migration.InitialDatabase : applied[^2];
        var error = await Assert.ThrowsAsync<SqlException>(() => database.GetService<IMigrator>().MigrateAsync(target));
        Assert.Equal(errorNumber, error.Number);
        Assert.Equal(applied, await database.Database.GetAppliedMigrationsAsync());
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, TestObserver, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
        var observer = new TestObserver();
        var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(connection,
                new ProductPilotIssuerBinding(ActorId, ActorExternalId), Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
            services.RemoveAll<IProductCommandAttemptObserver>();
            services.AddSingleton<IProductCommandAttemptObserver>(observer);
        }, databaseName: databaseName);
        try
        {
            using var startup = factory.CreateClient();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.True(identity.Database.IsSqlServer());
                foreach (var operation in Enum.GetValues<ProductMasterOperation>())
                    identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                        SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                        "Synthetic isolated SQL fixture; not a Sinan or production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null));
                await identity.SaveChangesAsync();
            }
            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var seed = sql.CreateCommand();
                seed.CommandText = "INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix]) VALUES (@id, N'Isolated SQL product group', 'PSQL'), (@secondId, N'Same-prefix isolated group', 'PSQL');";
                seed.Parameters.AddWithValue("@id", GroupId);
                seed.Parameters.AddWithValue("@secondId", Guid.Parse("82000000-0000-0000-0000-000000000002"));
                Assert.Equal(2, await seed.ExecuteNonQueryAsync());
            }
            await test(factory, observer);
        }
        finally
        {
            await factory.DisposeAsync();
            if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string key, string name, Guid? groupId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        { Content = JsonContent.Create(new CreateProductDraftRequest(name, groupId ?? GroupId)) };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, Guid id, string key, string version, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/products/{id:D}/draft")
        { Content = JsonContent.Create(new { expectedVersion = version, changes = new { name }, reason = "Isolated SQL edit" }) };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task<string> SnapshotAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT * FROM [product].[Products] ORDER BY [Id] FOR JSON PATH) AS products,
                (SELECT * FROM [product].[CodeGroups] ORDER BY [Id] FOR JSON PATH) AS groups,
                (SELECT * FROM [product].[CodeCounters] ORDER BY [Prefix] FOR JSON PATH) AS counters,
                (SELECT * FROM [product].[CodeRegistrations] ORDER BY [Code] FOR JSON PATH) AS registrations,
                (SELECT * FROM [audit].[AuditEvents] ORDER BY [Id] FOR JSON PATH) AS audit,
                (SELECT * FROM [idempotency].[GroupSharedCommandTerminals] ORDER BY [Subject], [CommandName], [IdempotencyKeyHash] FOR JSON PATH) AS terminals
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            """;
        return await ReadJsonFragmentsAsync(command);
    }

    private static async Task<string> ReadJsonFragmentsAsync(SqlCommand command)
    {
        await using var fragments = await command.ExecuteReaderAsync();
        Assert.Equal(1, fragments.FieldCount);
        var json = new System.Text.StringBuilder();
        while (await fragments.ReadAsync()) json.Append(fragments.GetString(0));
        return json.ToString();
    }

    private sealed class TestObserver : IProductCommandAttemptObserver
    {
        public Func<ProductCommandAttemptStage, GroupSharedMasterCommandContext, CancellationToken, Task>? Callback { get; set; }
        public Task ObserveAsync(ProductCommandAttemptStage stage, GroupSharedMasterCommandContext authority,
            CancellationToken cancellationToken) => Callback?.Invoke(stage, authority, cancellationToken) ?? Task.CompletedTask;
    }

    private sealed class InjectedProductFailureException : Exception;

    private sealed class TwoAttemptStageBarrier
    {
        private readonly TaskCompletionSource bothStaged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int ArrivalCount => Volatile.Read(ref arrivals);
        public async Task ObserveAsync(ProductCommandAttemptStage stage, GroupSharedMasterCommandContext authority,
            CancellationToken cancellationToken)
        {
            if (stage != ProductCommandAttemptStage.ProductStaged) return;
            if (Interlocked.Increment(ref arrivals) == 2) bothStaged.TrySetResult();
            await bothStaged.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
    }
}
