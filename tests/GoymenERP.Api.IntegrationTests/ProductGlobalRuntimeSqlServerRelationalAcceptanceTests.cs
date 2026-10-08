using System.Net;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using GoymenERP.CariAcceptance006;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Claims;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
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
    private const string ProductPreFamilyRegistryMigration = "20260912175128_InitialProductDrafts";
    private const string ProductFamilyRegistryMigration = "20260919100000_AddProductFamilyRegistry";
    private const string ProductTechnicalDefinitionMigration = "20261004130000_ReleaseInitialProductTechnicalDefinition";
    private const string ProductCodeGroupMigration = "20261004213000_ReleaseInitialProductCodeGroup";
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid GroupId = Guid.Parse("82000000-0000-0000-0000-000000000001");

    [SqlServerFact]
    public Task ProductImport_PreviewApplyReplayAndDeniedActor_AreAtomic() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            var workbook = ImportWorkbook(("LEGACY-IMPORT-1", "Imported Alpha"),
                ("LEGACY-IMPORT-2", "Imported Beta"));
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var deniedClient = factory.CreateClientFor("company-a-user", CompanyA);
            var before = await SnapshotAsync(factory.DatabaseConnectionString);

            using var deniedPreview = await PreviewImportAsync(deniedClient, workbook);
            Assert.Equal(HttpStatusCode.Forbidden, deniedPreview.StatusCode);
            using var previewResponse = await PreviewImportAsync(client, workbook);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = (await previewResponse.Content.ReadFromJsonAsync<ProductImportPreviewV1>())!;
            Assert.True(preview.Ready);
            Assert.Equal(2, preview.Rows.Count);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));

            const string key = "product-import-sql-once";
            using var applied = await ApplyImportAsync(client, workbook, preview, key);
            Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
            var first = (await applied.Content.ReadFromJsonAsync<ProductImportResultV1>())!;
            Assert.False(first.IsReplay);
            Assert.Equal(2, first.Products.Count);
            var afterApply = await SnapshotAsync(factory.DatabaseConnectionString);

            using var replay = await ApplyImportAsync(client, workbook, preview, key);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            var repeated = (await replay.Content.ReadFromJsonAsync<ProductImportResultV1>())!;
            Assert.True(repeated.IsReplay);
            Assert.Equal(first.Products.Select(value => value.ProductId), repeated.Products.Select(value => value.ProductId));
            Assert.Equal(afterApply, await SnapshotAsync(factory.DatabaseConnectionString));

            await using var sql = new SqlConnection(factory.DatabaseConnectionString);
            await sql.OpenAsync();
            await using var counts = sql.CreateCommand();
            counts.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM [product].[Products] WHERE [Id] IN (@first, @second)),
                  (SELECT COUNT(*) FROM [audit].[AuditEvents]
                   WHERE [SubjectKind] = N'GroupSharedProduct' AND [EntityId] IN (@firstText, @secondText)),
                  (SELECT COUNT(*) FROM [idempotency].[GroupSharedCommandTerminals]
                   WHERE [Subject] = N'Product'
                     AND [CommandName] = N'product.draft.create.v1'
                     AND [IdempotencyKeyHash] = @keyHash);
                """;
            counts.Parameters.AddWithValue("@first", first.Products[0].ProductId);
            counts.Parameters.AddWithValue("@second", first.Products[1].ProductId);
            counts.Parameters.AddWithValue("@firstText", first.Products[0].ProductId.ToString("D"));
            counts.Parameters.AddWithValue("@secondText", first.Products[1].ProductId.ToString("D"));
            counts.Parameters.AddWithValue("@keyHash", IdempotencyHash.ComputeKey(key));
            await using var reader = await counts.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal(2, reader.GetInt32(1));
            Assert.Equal(1, reader.GetInt32(2));
        });

    [SqlServerFact]
    public Task ProductImport_AuditFailureAndLateRevocation_RollBackProductAuditAndTerminalTogether() =>
        WithDatabaseAsync(async (factory, observer) =>
        {
            var workbook = ImportWorkbook(("LEGACY-ROLLBACK-1", "Rollback Alpha"),
                ("LEGACY-ROLLBACK-2", "Rollback Beta"));
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var previewResponse = await PreviewImportAsync(client, workbook);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = (await previewResponse.Content.ReadFromJsonAsync<ProductImportPreviewV1>())!;
            var before = await SnapshotAsync(factory.DatabaseConnectionString);

            observer.Callback = (stage, _, _) => stage == ProductCommandAttemptStage.AuditFlushed
                ? Task.FromException(new InjectedProductFailureException()) : Task.CompletedTask;
            using var failed = await ApplyImportAsync(client, workbook, preview, "product-import-audit-failure");
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));

            observer.Callback = async (stage, _, cancellationToken) =>
            {
                if (stage != ProductCommandAttemptStage.BeforeCommit) return;
                await using var scope = factory.Services.CreateAsyncScope();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var grant = await identity.Set<ProductMasterAssignment>().SingleAsync(row => row.UserId == ActorId
                    && row.Operation == ProductMasterOperation.DraftCreate, cancellationToken);
                grant.Revoke(Guid.NewGuid(), "Synthetic isolated SQL import revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync(cancellationToken);
            };
            using var revoked = await ApplyImportAsync(client, workbook, preview, "product-import-late-revocation");
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));
        });

    [SqlServerFact]
    public Task ProductImport_SameKeyWinnerBeforeLoserCatalog_ReplaysOrHidesAfterRevocation() =>
        WithDatabaseAsync(async (factory, observer) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var workbook = ImportWorkbook(("LEGACY-RACE-1", "Race Alpha"), ("LEGACY-RACE-2", "Race Beta"));
            using var previewResponse = await PreviewImportAsync(client, workbook);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = (await previewResponse.Content.ReadFromJsonAsync<ProductImportPreviewV1>())!;

            var loserAtCatalog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseLoser = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            observer.Callback = async (stage, _, cancellationToken) =>
            {
                if (stage != ProductCommandAttemptStage.ProductStaged) return;
                loserAtCatalog.TrySetResult();
                await releaseLoser.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            };
            var loserTask = ApplyImportAsync(client, workbook, preview, "product-import-race-replay");
            await loserAtCatalog.Task.WaitAsync(TimeSpan.FromSeconds(20));
            observer.Callback = null;
            using var winner = await ApplyImportAsync(client, workbook, preview, "product-import-race-replay");
            Assert.Equal(HttpStatusCode.Created, winner.StatusCode);
            var winnerBody = (await winner.Content.ReadFromJsonAsync<ProductImportResultV1>())!;
            releaseLoser.TrySetResult();
            using var loser = await loserTask;
            Assert.Equal(HttpStatusCode.Created, loser.StatusCode);
            Assert.Equal("true", Assert.Single(loser.Headers.GetValues("Idempotency-Replayed")));
            var loserBody = (await loser.Content.ReadFromJsonAsync<ProductImportResultV1>())!;
            Assert.True(loserBody.IsReplay);
            Assert.Equal(winnerBody.Products.Select(value => value.ProductId),
                loserBody.Products.Select(value => value.ProductId));

            var revokedWorkbook = ImportWorkbook(("LEGACY-RACE-3", "Revoked Race Alpha"),
                ("LEGACY-RACE-4", "Revoked Race Beta"));
            using var revokedPreviewResponse = await PreviewImportAsync(client, revokedWorkbook);
            Assert.Equal(HttpStatusCode.OK, revokedPreviewResponse.StatusCode);
            var revokedPreview = (await revokedPreviewResponse.Content.ReadFromJsonAsync<ProductImportPreviewV1>())!;
            var revokedLoserAtCatalog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRevokedLoser = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            observer.Callback = async (stage, _, cancellationToken) =>
            {
                if (stage != ProductCommandAttemptStage.ProductStaged) return;
                revokedLoserAtCatalog.TrySetResult();
                await releaseRevokedLoser.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            };
            var revokedLoserTask = ApplyImportAsync(client, revokedWorkbook, revokedPreview,
                "product-import-race-revoked");
            await revokedLoserAtCatalog.Task.WaitAsync(TimeSpan.FromSeconds(20));
            observer.Callback = null;
            using var revokedWinner = await ApplyImportAsync(client, revokedWorkbook, revokedPreview,
                "product-import-race-revoked");
            Assert.Equal(HttpStatusCode.Created, revokedWinner.StatusCode);
            var afterWinner = await SnapshotAsync(factory.DatabaseConnectionString);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var grant = await identity.Set<ProductMasterAssignment>().SingleAsync(row => row.UserId == ActorId
                    && row.Operation == ProductMasterOperation.DraftCreate);
                grant.Revoke(Guid.NewGuid(), "Synthetic import recovery revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync();
            }
            releaseRevokedLoser.TrySetResult();
            using var revokedLoser = await revokedLoserTask;
            Assert.Equal(HttpStatusCode.Forbidden, revokedLoser.StatusCode);
            Assert.False(revokedLoser.Headers.Contains("Idempotency-Replayed"));
            Assert.Equal(afterWinner, await SnapshotAsync(factory.DatabaseConnectionString));
        });

    [SqlServerFact]
    public Task ProductSalesCandidateWindow_FiltersAndBoundsRealSqlWithoutWrites() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var ids = new List<Guid>();
            foreach (var name in new[] { "Candidate Alpha", "Candidate Beta", "Candidate Draft", "Other" })
            {
                using var created = await CreateAsync(client, Guid.NewGuid().ToString("N"), name);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                ids.Add((await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Id);
            }
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            // Isolated relational query fixture, NOT an activation or real released-family acceptance.
            await db.Products.Where(row => row.Id == ids[0] || row.Id == ids[1] || row.Id == ids[3])
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Lifecycle, 1)
                    .SetProperty(row => row.SellableValue, true).SetProperty(row => row.SellableProvenance, 1));
            var before = await SnapshotAsync(factory.DatabaseConnectionString);
            var reader = new SqlProductSalesCandidateReader(new ProductReadStore(db));
            var authority = new ProductSalesReadScope(CompanyA, ActorId.ToString("D"));
            var first = await reader.ReadAsync(authority, new("Candidate", new(2026, 9, 21), 1), default);
            Assert.Equal(ids[0], Assert.Single(first.Items).Id);
            Assert.True(first.HasMore);
            var all = await reader.ReadAsync(authority, new("Candidate", new(2026, 9, 21), 10), default);
            Assert.Equal(new[] { ids[0], ids[1] }, all.Items.Select(item => item.Id));
            Assert.False(all.HasMore);
            var code = await reader.ReadAsync(authority, new("psql-2", new(2026, 9, 21)), default);
            Assert.Equal(ids[1], Assert.Single(code.Items).Id);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));
        });

    [SqlServerFact]
    public Task ProductStockCandidateWindow_FiltersActiveCardsWithoutInventingARole() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var ids = new List<Guid>();
            foreach (var name in new[] { "Stock Alpha", "Stock Beta", "Stock Draft", "Other Stock" })
            {
                using var created = await CreateAsync(client, Guid.NewGuid().ToString("N"), name);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                ids.Add((await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Id);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            // Only lifecycle is changed: a stock operation has no commercial-role precondition.
            await db.Products.Where(row => row.Id == ids[0] || row.Id == ids[1] || row.Id == ids[3])
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Lifecycle, 1));
            var before = await SnapshotAsync(factory.DatabaseConnectionString);
            var reader = new SqlProductStockCandidateReader(new ProductReadStore(db));
            var authority = new ProductStockReadScope(CompanyA, ActorId.ToString("D"));

            var first = await reader.ReadAsync(authority, new("Stock", new(2026, 9, 23), 1), default);
            Assert.Equal(ids[0], Assert.Single(first.Items).Id);
            Assert.True(first.HasMore);

            var all = await reader.ReadAsync(authority, new("Stock", new(2026, 9, 23), 10), default);
            Assert.Equal(new[] { ids[0], ids[1], ids[3] }, all.Items.Select(item => item.Id));
            Assert.False(all.HasMore);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));
        });

    [SqlServerFact]
    public Task ProductGlobalRuntime_SharedSnapshotEmitsEmptyAndPopulatedJsonArrays() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            await using var sql = new SqlConnection(factory.DatabaseConnectionString);
            await sql.OpenAsync();
            var empty = await ProductSharedStateSnapshot.ReadAsync(sql, CancellationToken.None);
            Assert.Equal(JsonValueKind.Array, empty.Audit.ValueKind);
            Assert.Equal(JsonValueKind.Array, empty.Terminals.ValueKind);
            Assert.Equal(0, empty.Audit.GetArrayLength());
            Assert.Equal(0, empty.Terminals.GetArrayLength());
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var create = await CreateAsync(client, "product-shared-snapshot-create", "Actual empty-to-created shared snapshot");
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var product = (await create.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product;
            var created = await ProductSharedStateSnapshot.ReadAsync(sql, CancellationToken.None);
            Assert.Equal(1, created.Audit.GetArrayLength());
            Assert.Equal(1, created.Terminals.GetArrayLength());
            using var edit = await EditAsync(client, product.Id, "product-shared-snapshot-edit", "1", "Actual edited shared snapshot");
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            var edited = await ProductSharedStateSnapshot.ReadAsync(sql, CancellationToken.None);
            Assert.Equal(2, edited.Audit.GetArrayLength());
            Assert.Equal(2, edited.Terminals.GetArrayLength());
            var unchanged = await ProductSharedStateSnapshot.ReadAsync(sql, CancellationToken.None);
            Assert.True(JsonElement.DeepEquals(edited.Audit, unchanged.Audit));
            Assert.True(JsonElement.DeepEquals(edited.Terminals, unchanged.Terminals));
        });

    [SqlServerFact]
    public Task ProductHistoryReader_OrdersSameTimestampByVersionBeforeApplyingCutoff() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            var productId = Guid.NewGuid();
            var occurredAt = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
            await using var sql = new SqlConnection(factory.DatabaseConnectionString);
            await sql.OpenAsync();

            // SQL Server's uniqueidentifier ordering is deliberately measured instead of
            // inferred. Giving the largest Id to v1 makes the former time/Id ordering return
            // v1 first, while the Product contract must return v3 and v2 before applying Take(2).
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var sqlIdOrder = await OrderIdsDescendingAsync(sql, ids);
            var rows = new[]
            {
                HistoryData(productId, sqlIdOrder[0], occurredAt, ProductHistoryApiV1.DraftCreate, null, 1),
                HistoryData(productId, sqlIdOrder[1], occurredAt, ProductHistoryApiV1.DraftBasicEdit, 1, 2),
                HistoryData(productId, sqlIdOrder[2], occurredAt, ProductHistoryApiV1.DraftBasicEdit, 2, 3),
            };
            foreach (var row in rows) await InsertGroupSharedAuditAsync(sql, row);

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
                var history = await scope.ServiceProvider.GetRequiredService<GroupSharedProductAuditReader>()
                    .ReadAsync(productId, 2);
                Assert.Equal(new long[] { 3, 2 }, history.Select(item => item.Envelope.EntityVersionAfter));
                Assert.Equal(new[] { sqlIdOrder[2], sqlIdOrder[1] }, history.Select(item => item.Envelope.Id));
            }
            finally { accessor.HttpContext = previous; }
        });

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
            var product = migrationScope.ServiceProvider.GetRequiredService<ProductDbContext>();
            var productMigrations = (await product.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(ProductCodeGroupMigration, productMigrations[^1]);

            // EF Core 10 commits each migration in its own transaction. First remove the unreferenced
            // code-group release, then the technical-definition release, before exercising the schema
            // guard as a single rollback step. Reapply both releases afterwards so this acceptance case
            // leaves the Product runtime at its entry schema.
            await product.GetService<IMigrator>().MigrateAsync(ProductTechnicalDefinitionMigration);
            Assert.Equal(productMigrations[..^1], await product.Database.GetAppliedMigrationsAsync());
            await product.GetService<IMigrator>().MigrateAsync(ProductFamilyRegistryMigration);
            Assert.Equal(productMigrations[..^2], await product.Database.GetAppliedMigrationsAsync());
            await AssertRollbackRefusedAsync(product, 51044, ProductPreFamilyRegistryMigration);
            await product.GetService<IMigrator>().MigrateAsync(ProductCodeGroupMigration);
            Assert.Equal(productMigrations, await product.Database.GetAppliedMigrationsAsync());
            Assert.Equal(afterEdit, await SnapshotAsync(factory.DatabaseConnectionString));
        });

    [SqlServerFact]
    public Task Stock_display_qualified_fixture_replays_and_reads_released_technical_sources() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            // The shared Product SQL harness adds two unrelated code groups for command scenarios.
            // This fixture owns an isolated Product store, so remove only those known unreferenced rows.
            await using (var sql = new SqlConnection(factory.DatabaseConnectionString))
            {
                await sql.OpenAsync();
                await using var removeHarnessGroups = sql.CreateCommand();
                removeHarnessGroups.CommandText =
                    "DELETE FROM [product].[CodeGroups] WHERE [Id] IN (@first, @second);";
                removeHarnessGroups.Parameters.AddWithValue("@first", GroupId);
                removeHarnessGroups.Parameters.AddWithValue("@second",
                    Guid.Parse("82000000-0000-0000-0000-000000000002"));
                Assert.Equal(2, await removeHarnessGroups.ExecuteNonQueryAsync());
            }

            await using (var genuineOnlyScope = factory.Services.CreateAsyncScope())
            {
                var genuineOnly = genuineOnlyScope.ServiceProvider.GetRequiredService<ProductDbContext>();
                var before = (
                    Families: await genuineOnly.Families.CountAsync(),
                    Templates: await genuineOnly.Templates.CountAsync(),
                    Links: await genuineOnly.TemplateFamilies.CountAsync(),
                    Fields: await genuineOnly.TemplateFields.CountAsync(),
                    Entries: await genuineOnly.DictionaryEntries.CountAsync());
                Assert.Equal((4, 1, 4, 5, 5), before);
                var store = new ProductStockDisplayQualifiedFixtureStore(
                    genuineOnly,
                    new ProductUnitProfileAdapter(
                        new StockDisplayFixtureSettings(),
                        new StockDisplayFixtureCompany()));
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureAsync(default));
                Assert.Equal(before, (
                    Families: await genuineOnly.Families.CountAsync(),
                    Templates: await genuineOnly.Templates.CountAsync(),
                    Links: await genuineOnly.TemplateFamilies.CountAsync(),
                    Fields: await genuineOnly.TemplateFields.CountAsync(),
                    Entries: await genuineOnly.DictionaryEntries.CountAsync()));
                Assert.Empty(await genuineOnly.Products.ToListAsync());
                Assert.Empty(await genuineOnly.CodeRegistrations.ToListAsync());
                Assert.Empty(await genuineOnly.CodeCounters.ToListAsync());
            }

            await using (var registryScope = factory.Services.CreateAsyncScope())
            {
                await new ProductTechnicalCardFixtureStore(
                        registryScope.ServiceProvider.GetRequiredService<ProductDbContext>())
                    .EnsureAsync(default);
            }

            ProductStockDisplayQualifiedFixtureOutput? first = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var fixtureScope = factory.Services.CreateAsyncScope();
                var db = fixtureScope.ServiceProvider.GetRequiredService<ProductDbContext>();
                var store = new ProductStockDisplayQualifiedFixtureStore(
                    db,
                    new ProductUnitProfileAdapter(
                        new StockDisplayFixtureSettings(),
                        new StockDisplayFixtureCompany()));
                var current = await store.EnsureAsync(default);
                first ??= current;
                Assert.Equal(first, current);
            }

            await using var readScope = factory.Services.CreateAsyncScope();
            var product = readScope.ServiceProvider.GetRequiredService<ProductDbContext>();
            Assert.True(product.Database.IsSqlServer());
            var authority = new StockDisplayFixtureAuthority();
            var service = new ProductStockDisplayReferenceService(
                new StockDisplayFixtureCompany(),
                new StockDisplayFixtureUser(),
                authority,
                new ProductReadStore(product),
                new SqlProductFamilyDefinitionSource(product));
            var batch = await service.ReadAsync(new([
                new(first!.ProductId, first.ProductVersion),
            ]));

            var item = Assert.Single(batch.Items);
            Assert.Empty(item.Errors);
            Assert.Equal((first.ProductId, first.ProductVersion, first.Code, first.Name),
                (item.Identity!.ProductId, item.Identity.ProductVersion, item.Identity.Code, item.Identity.Name));
            var technical = Assert.IsType<ProductStockNominalTechnicalV1>(item.Technical);
            Assert.Equal((ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
                    ProductInitialTechnicalDefinitionV1.SourceRef),
                (technical.Family.FamilyId, technical.Family.SourceRevision));
            Assert.Equal((ProductInitialTechnicalDefinitionV1.TemplateId, "1",
                    ProductInitialTechnicalDefinitionV1.SourceRef),
                (technical.Template.TemplateId, technical.Template.TemplateVersion,
                    technical.Template.SourceRevision));
            Assert.Equal((1.000m, 1000.000m), (technical.ThicknessMm, technical.WidthMm));
            Assert.All(authority.Permissions,
                permission => Assert.Equal(ProductPermissionManifestV1.OperationalSelectionRead, permission));

            var stored = (await product.Products.AsNoTracking().SingleAsync()).ToRecord();
            Assert.Equal(ProductStockDisplayQualifiedFixture.Record(), stored);
            Assert.Equal(2, await product.CodeGroups.CountAsync());
            Assert.Single(await product.CodeRegistrations.ToListAsync());
            Assert.Single(await product.CodeCounters.ToListAsync());
        });

    [SqlServerFact]
    public Task Genuine_sac_card_is_created_replayed_reopened_activated_and_read_as_one_technical_snapshot() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            const string createKey = "product-genuine-sac-card-once";
            const string activateKey = "product-genuine-sac-activate-once";
            var effectiveOn = new DateOnly(2026, 10, 5);
            var references = new ProductReferences(
                ProductInitialTechnicalDefinitionV1.TemplateId, "1",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["thickness-mm"] = JsonSerializer.SerializeToElement(3m),
                    ["width-mm"] = JsonSerializer.SerializeToElement(248m),
                    ["steel-grade"] = JsonSerializer.SerializeToElement("S235JR"),
                    ["coating"] = JsonSerializer.SerializeToElement("uncoated"),
                },
                new ProductUnitReferences(Stock: SettingsUnitIdentitiesV1.Kilogram,
                    Sales: SettingsUnitIdentitiesV1.Kilogram));
            var draft = new CreateProductDraftRequest("S235JR 3 mm × 248 mm kaplamasız sac",
                ProductInitialCodeGroupV1.SacGroupId,
                FamilyId: ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
                References: references);

            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            async Task<HttpResponseMessage> CreateAsync()
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
                { Content = JsonContent.Create(draft) };
                request.Headers.Add(ProductApiV1.IdempotencyHeader, createKey);
                return await client.SendAsync(request);
            }

            using var created = await CreateAsync();
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var first = (await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.False(first.IsReplay);
            Assert.Equal(("SAC-1", "Draft", "1", ProductInitialCodeGroupV1.SacGroupId,
                ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId),
                (first.Card.Product.Code, first.Card.Product.Lifecycle, first.Card.Product.Version,
                    first.Card.Product.GroupId, first.Card.Product.FamilyId));
            var afterCreate = await SnapshotAsync(factory.DatabaseConnectionString);

            using var replay = await CreateAsync();
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            var repeated = (await replay.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.True(repeated.IsReplay);
            Assert.Equal((first.Card.Product.Id, first.Card.Product.Lifecycle, first.Card.Product.Version),
                (repeated.Card.Product.Id, repeated.Card.Product.Lifecycle, repeated.Card.Product.Version));
            Assert.Equal(afterCreate, await SnapshotAsync(factory.DatabaseConnectionString));

            using (var reopenedDraft = await client.GetAsync($"/api/v1/products/{first.Card.Product.Id:D}"))
            {
                Assert.Equal(HttpStatusCode.OK, reopenedDraft.StatusCode);
                var card = (await reopenedDraft.Content.ReadFromJsonAsync<ProductCardView>())!;
                Assert.Equal((first.Card.Product.Id, "1", ProductInitialTechnicalDefinitionV1.TemplateId, "1"),
                    (card.Product.Id, card.Product.Version, card.References.TemplateId, card.References.TemplateVersion));
                Assert.Equal(SettingsUnitIdentitiesV1.Kilogram, card.References.Units!.Stock);
                Assert.Equal(SettingsUnitIdentitiesV1.Kilogram, card.References.Units.Sales);
                Assert.Equal(3m, card.References.Attributes!["thickness-mm"].GetDecimal());
                Assert.Equal(248m, card.References.Attributes["width-mm"].GetDecimal());
                Assert.Equal("S235JR", card.References.Attributes["steel-grade"].GetString());
                Assert.Equal("uncoated", card.References.Attributes["coating"].GetString());
                Assert.False(card.References.Attributes.ContainsKey("producer-grade"));
            }

            using var activationRequest = new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/products/{first.Card.Product.Id:D}/activation")
            { Content = JsonContent.Create(new ActivateProductRequest("1")) };
            activationRequest.Headers.Add(ProductApiV1.IdempotencyHeader, activateKey);
            using var activated = await client.SendAsync(activationRequest);
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
            var active = (await activated.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.Equal((first.Card.Product.Id, "Active", "2"),
                (active.Card.Product.Id, active.Card.Product.Lifecycle, active.Card.Product.Version));

            using var freshClient = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using (var reopenedActive = await freshClient.GetAsync($"/api/v1/products/{first.Card.Product.Id:D}"))
            {
                Assert.Equal(HttpStatusCode.OK, reopenedActive.StatusCode);
                var card = (await reopenedActive.Content.ReadFromJsonAsync<ProductCardView>())!;
                Assert.Equal((first.Card.Product.Id, "SAC-1", "Active", "2"),
                    (card.Product.Id, card.Product.Code, card.Product.Lifecycle, card.Product.Version));
                Assert.Equal(references.TemplateId, card.References.TemplateId);
                Assert.Equal(references.Units!.Stock, card.References.Units!.Stock);
                Assert.Equal(references.Units.Sales, card.References.Units.Sales);
                Assert.False(card.References.Attributes!.ContainsKey("producer-grade"));
            }

            // The operational read is a separate, existing permission. Grant it only to this
            // disposable SQL actor; global Product-master approval alone does not imply it.
            await using (var setup = factory.Services.CreateAsyncScope())
            {
                var identity = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();
                if (!await identity.Users.AnyAsync(user => user.Id == ActorId))
                    identity.Users.Add(new UserAccount(ActorId, "Disposable Product snapshot actor", null));
                var roleId = Guid.NewGuid();
                var role = new RoleDefinition(roleId, $"PRODUCT-SNAPSHOT-{roleId:N}",
                    "Disposable Product and Inventory SQL journey actor", true);
                role.AddPermission(ProductPermissionManifestV1.OperationalSelectionRead);
                role.AddPermission(InventoryPermissionManifestV1.OpeningStockRecord);
                role.AddPermission(ErpPermissions.InventoryLabelsRead);
                role.AddPermission(ErpPermissions.InventoryBalancesRead);
                role.AddPermission(ErpPermissions.ManufacturingPlansWrite);
                role.AddPermission(ErpPermissions.ManufacturingPlansRead);
                var assignment = new RoleAssignment(Guid.NewGuid(), ActorId, roleId, CompanyA,
                    DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
                assignment.AddScope(AccessScopeType.Company, CompanyA);
                identity.Roles.Add(role);
                identity.RoleAssignments.Add(assignment);
                await identity.SaveChangesAsync();
            }

            using var locationsResponse = await freshClient.GetAsync(InventoryReferenceEndpoints.LocationPath);
            Assert.Equal(HttpStatusCode.OK, locationsResponse.StatusCode);
            var locations = (await locationsResponse.Content.ReadFromJsonAsync<OrganizationLocationCandidatesV1>())!;
            Assert.NotEmpty(locations.Items);
            var location = locations.Items[0];
            Assert.NotEqual(Guid.Empty, location.LocationId);
            var labelId = Guid.NewGuid();
            var opening = new OpeningCountRequestV1(Guid.NewGuid(), location.LocationId, effectiveOn,
                "Disposable genuine SAC opening count",
                [new OpeningCountLineRequestV1(Guid.NewGuid(), labelId, "SAC-T02B-LABEL-1", Guid.NewGuid(),
                    null, null, first.Card.Product.Id, active.Card.Product.Version, "1000")]);
            const string openingKey = "product-genuine-sac-opening-once";
            async Task<HttpResponseMessage> RecordOpeningAsync(HttpClient actor, OpeningCountRequestV1 request,
                string key)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/opening-counts")
                { Content = JsonContent.Create(request) };
                message.Headers.Add("Idempotency-Key", key);
                return await actor.SendAsync(message);
            }

            using var opened = await RecordOpeningAsync(freshClient, opening, openingKey);
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var recorded = (await opened.Content.ReadFromJsonAsync<OpeningCountViewV1>())!;
            Assert.Equal((opening.CountId, CompanyA, location.LocationId),
                (recorded.CountId, recorded.CompanyId, recorded.LocationId));
            Assert.Equal((labelId, first.Card.Product.Id, "1000"),
                (Assert.Single(recorded.Lines).LabelId, recorded.Lines[0].ProductId,
                    recorded.Lines[0].QuantityKg));

            using var reopenedCount = await freshClient.GetAsync($"/api/v1/inventory/opening-counts/{opening.CountId:D}");
            Assert.Equal(HttpStatusCode.OK, reopenedCount.StatusCode);
            var reopened = (await reopenedCount.Content.ReadFromJsonAsync<OpeningCountViewV1>())!;
            Assert.Equal((recorded.CountId, recorded.CompanyId, recorded.LocationId, recorded.CountedOn,
                recorded.SheetReference), (reopened.CountId, reopened.CompanyId, reopened.LocationId,
                    reopened.CountedOn, reopened.SheetReference));
            Assert.Equal(Assert.Single(recorded.Lines), Assert.Single(reopened.Lines));
            using var reopenedLabel = await freshClient.GetAsync($"/api/v1/inventory/labels/{labelId:D}");
            Assert.Equal(HttpStatusCode.OK, reopenedLabel.StatusCode);
            var label = (await reopenedLabel.Content.ReadFromJsonAsync<InventoryLabelViewV1>())!;
            Assert.Equal((labelId, CompanyA, first.Card.Product.Id),
                (label.LabelId, label.CompanyId, label.ProductId));
            using var balanceResponse = await freshClient.GetAsync($"/api/v1/inventory/labels/{labelId:D}/balances");
            Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);
            var balance = (await balanceResponse.Content.ReadFromJsonAsync<InventoryLabelBalancesV1>())!;
            Assert.Equal(("1000", "1000", "0"), (balance.PhysicalKg, balance.FreeKg, balance.BlockedKg));
            Assert.Equal((location.LocationId, first.Card.Product.Id),
                (Assert.Single(balance.Positions).LocationId, balance.Positions[0].ProductId));

            async Task<(Guid ProductId, string ProductVersion, long LabelVersion, int MovementLines,
                int OpeningCounts)> StoredInventoryAsync()
            {
                await using var sql = new SqlConnection(factory.DatabaseConnectionString);
                await sql.OpenAsync();
                await using var command = sql.CreateCommand();
                command.CommandText = """
                    SELECT [ProductId], [ProductVersion], [Version],
                        (SELECT COUNT(*) FROM [inventory].[MovementLines] WHERE [CompanyId] = @company AND [LabelId] = @label),
                        (SELECT COUNT(*) FROM [inventory].[OpeningCounts] WHERE [CompanyId] = @company AND [Id] = @count)
                    FROM [inventory].[Labels] WHERE [CompanyId] = @company AND [Id] = @label;
                    """;
                command.Parameters.AddWithValue("@company", CompanyA);
                command.Parameters.AddWithValue("@label", labelId);
                command.Parameters.AddWithValue("@count", opening.CountId);
                await using var row = await command.ExecuteReaderAsync();
                Assert.True(await row.ReadAsync());
                var stored = (row.GetGuid(0), row.GetString(1), row.GetInt64(2), row.GetInt32(3), row.GetInt32(4));
                Assert.False(await row.ReadAsync());
                return stored;
            }

            var stocked = await StoredInventoryAsync();
            Assert.Equal((first.Card.Product.Id, active.Card.Product.Version, 1, 1),
                (stocked.ProductId, stocked.ProductVersion, stocked.MovementLines, stocked.OpeningCounts));
            var afterFirstOpening = await StoredEffectCountsAsync();
            using var openingReplay = await RecordOpeningAsync(freshClient, opening, openingKey);
            Assert.Equal(HttpStatusCode.OK, openingReplay.StatusCode);
            Assert.Equal("true", Assert.Single(openingReplay.Headers.GetValues("Idempotency-Replayed")));
            var replayedCount = (await openingReplay.Content.ReadFromJsonAsync<OpeningCountViewV1>())!;
            Assert.Equal((recorded.CountId, recorded.CompanyId, recorded.LocationId),
                (replayedCount.CountId, replayedCount.CompanyId, replayedCount.LocationId));
            Assert.Equal(Assert.Single(recorded.Lines), Assert.Single(replayedCount.Lines));
            Assert.Equal(stocked, await StoredInventoryAsync());
            Assert.Equal(afterFirstOpening, await StoredEffectCountsAsync());

            async Task<(int Lots, int Labels, int Counts, int Movements, int MovementLines,
                int Balances, int Commands, int Audit)> StoredEffectCountsAsync()
            {
                await using var sql = new SqlConnection(factory.DatabaseConnectionString);
                await sql.OpenAsync();
                await using var command = sql.CreateCommand();
                command.CommandText = """
                    SELECT (SELECT COUNT(*) FROM [inventory].[Lots]),
                        (SELECT COUNT(*) FROM [inventory].[Labels]),
                        (SELECT COUNT(*) FROM [inventory].[OpeningCounts]),
                        (SELECT COUNT(*) FROM [inventory].[Movements]),
                        (SELECT COUNT(*) FROM [inventory].[MovementLines]),
                        (SELECT COUNT(*) FROM [inventory].[Balances]),
                        (SELECT COUNT(*) FROM [inventory].[Commands]),
                        (SELECT COUNT(*) FROM [audit].[AuditEvents]);
                    """;
                await using var row = await command.ExecuteReaderAsync();
                Assert.True(await row.ReadAsync());
                var counts = (row.GetInt32(0), row.GetInt32(1), row.GetInt32(2), row.GetInt32(3),
                    row.GetInt32(4), row.GetInt32(5), row.GetInt32(6), row.GetInt32(7));
                Assert.False(await row.ReadAsync());
                return counts;
            }

            var afterOpeningAudit = await SnapshotAsync(factory.DatabaseConnectionString);

            var staleLabelId = Guid.NewGuid();
            var stale = opening with
            {
                CountId = Guid.NewGuid(),
                Lines = [opening.Lines[0] with
                {
                    LineId = Guid.NewGuid(), LabelId = staleLabelId, LotId = Guid.NewGuid(), ProductVersion = "1",
                }],
            };
            using var staleResponse = await RecordOpeningAsync(freshClient, stale, "product-genuine-sac-stale-opening");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, staleResponse.StatusCode);
            Assert.Contains(ProductApiV1.VersionConflict, await staleResponse.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            using var deniedClient = factory.CreateClientFor(TestUsers.NoAssignment, CompanyA);
            using var deniedResponse = await RecordOpeningAsync(deniedClient, stale,
                "product-genuine-sac-denied-opening");
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
            using var foreignClient = factory.CreateClientFor(TestUsers.CompanyB, CompanyB);
            using var foreignResponse = await RecordOpeningAsync(foreignClient, stale,
                "product-genuine-sac-foreign-opening");
            Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
            Assert.Equal(stocked, await StoredInventoryAsync());
            Assert.Equal(afterFirstOpening, await StoredEffectCountsAsync());
            Assert.Equal(afterOpeningAudit, await SnapshotAsync(factory.DatabaseConnectionString));
            using var missingLabel = await freshClient.GetAsync($"/api/v1/inventory/labels/{staleLabelId:D}");
            Assert.Equal(HttpStatusCode.NotFound, missingLabel.StatusCode);

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
                var result = await scope.ServiceProvider.GetRequiredService<IProductManufacturingTechnicalSnapshotV1>()
                    .ReadAsync(new(stocked.ProductId, stocked.ProductVersion, effectiveOn), default);
                Assert.Empty(result.Errors);
                var snapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV1>(result.Snapshot);
                Assert.Equal((first.Card.Product.Id, "2", 3m, 248m, effectiveOn),
                    (snapshot.ProductId, snapshot.ProductVersion, snapshot.ThicknessMm,
                        snapshot.WidthMm, snapshot.EffectiveOn));
                Assert.Equal((ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
                    ProductInitialTechnicalDefinitionV1.SourceRef),
                    (snapshot.Family.FamilyId, snapshot.Family.SourceRevision));
                Assert.Equal((ProductInitialTechnicalDefinitionV1.TemplateId, "1",
                    ProductInitialTechnicalDefinitionV1.SourceRef),
                    (snapshot.Template.TemplateId, snapshot.Template.TemplateVersion,
                        snapshot.Template.SourceRevision));
                Assert.Equal((CompanyA, SettingsUnitIdentitiesV1.Kilogram, "kg",
                    ApprovedUnitProfileCatalog.GdcKilogramV1.ProfileId, "1"),
                    (snapshot.UnitKg.CompanyId, snapshot.UnitKg.UnitId, snapshot.UnitKg.UnitCode,
                        snapshot.UnitKg.ProfileId, snapshot.UnitKg.ProfileVersion));
            }
            finally { accessor.HttpContext = previous; }
            Assert.Equal(afterOpeningAudit, await SnapshotAsync(factory.DatabaseConnectionString));

            Guid workCenterId;
            await using (var sql = new SqlConnection(factory.DatabaseConnectionString))
            {
                await sql.OpenAsync();
                await using var workCenter = sql.CreateCommand();
                workCenter.CommandText = """
                    SELECT TOP (1) [Id]
                    FROM [organization].[WorkCenters]
                    WHERE [CompanyId] = @company AND [Kind] = N'Slitting'
                    ORDER BY [Id];
                    """;
                workCenter.Parameters.AddWithValue("@company", CompanyA);
                workCenterId = Assert.IsType<Guid>(await workCenter.ExecuteScalarAsync());
            }

            var planId = Guid.NewGuid();
            var planRequest = new SavePlanRequestV1(planId, workCenterId,
                [new PlannedInputRequestV1(labelId, "1000",
                    stocked.LabelVersion.ToString(CultureInfo.InvariantCulture))], null);
            const string planKey = "product-genuine-sac-manufacturing-plan-once";
            async Task<HttpResponseMessage> SavePlanAsync()
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/manufacturing/plans")
                { Content = JsonContent.Create(planRequest) };
                request.Headers.Add("Idempotency-Key", planKey);
                return await freshClient.SendAsync(request);
            }

            using var createdPlanResponse = await SavePlanAsync();
            Assert.Equal(HttpStatusCode.Created, createdPlanResponse.StatusCode);
            var createdPlan = (await createdPlanResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
            var plannedInput = Assert.Single(createdPlan.Inputs);
            var captured = Assert.IsType<CapturedPlanTechnicalSnapshotViewV1>(plannedInput.TechnicalSnapshot);
            Assert.Equal((planId, CompanyA, workCenterId, labelId, "1000"),
                (createdPlan.PlanId, createdPlan.CompanyId, createdPlan.WorkCenterId,
                    plannedInput.ParentRollId, plannedInput.PlannedKg));
            Assert.Equal((stocked.ProductId, stocked.ProductVersion, "3", "248"),
                (captured.ProductId, captured.ProductVersion, captured.ThicknessMm, captured.WidthMm));
            Assert.Equal((ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
                ProductInitialTechnicalDefinitionV1.SourceRef),
                (captured.Family.FamilyId, captured.Family.SourceRevision));
            Assert.Equal((ProductInitialTechnicalDefinitionV1.TemplateId, "1",
                ProductInitialTechnicalDefinitionV1.SourceRef),
                (captured.Template.TemplateId, captured.Template.TemplateVersion,
                    captured.Template.SourceRevision));
            Assert.Equal((CompanyA, SettingsUnitIdentitiesV1.Kilogram, "kg",
                ApprovedUnitProfileCatalog.GdcKilogramV1.ProfileId, "1"),
                (captured.UnitKg.CompanyId, captured.UnitKg.UnitId, captured.UnitKg.UnitCode,
                    captured.UnitKg.ProfileId, captured.UnitKg.ProfileVersion));

            using var replayedPlanResponse = await SavePlanAsync();
            Assert.Equal(HttpStatusCode.Created, replayedPlanResponse.StatusCode);
            Assert.Equal("true", Assert.Single(replayedPlanResponse.Headers.GetValues("Idempotency-Replayed")));
            var replayedPlan = (await replayedPlanResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
            Assert.Equal(JsonSerializer.Serialize(createdPlan), JsonSerializer.Serialize(replayedPlan));

            using var reopenedPlanResponse = await freshClient.GetAsync($"/api/v1/manufacturing/plans/{planId:D}");
            Assert.Equal(HttpStatusCode.OK, reopenedPlanResponse.StatusCode);
            var reopenedPlan = (await reopenedPlanResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
            Assert.Equal(JsonSerializer.Serialize(createdPlan), JsonSerializer.Serialize(reopenedPlan));

            using var initialHistoryResponse = await freshClient.GetAsync(
                $"/api/v1/manufacturing/plans/{planId:D}/revisions");
            Assert.Equal(HttpStatusCode.OK, initialHistoryResponse.StatusCode);
            var initialHistory = (await initialHistoryResponse.Content.ReadFromJsonAsync<PlanRevisionListViewV1>())!;
            Assert.Equal(planId, initialHistory.PlanId);
            var originalRevision = Assert.Single(initialHistory.Revisions);
            var originalRevisionInput = Assert.Single(originalRevision.Inputs);
            var originalRevisionSnapshot = Assert.IsType<CapturedPlanTechnicalSnapshotViewV1>(
                originalRevisionInput.TechnicalSnapshot);
            Assert.Equal((0, 1L, workCenterId, labelId, "1000",
                    stocked.LabelVersion.ToString(CultureInfo.InvariantCulture)),
                (originalRevision.Revision, originalRevision.PlanVersion, originalRevision.WorkCenterId,
                    originalRevisionInput.ParentRollId, originalRevisionInput.PlannedKg,
                    originalRevisionInput.ExpectedLabelVersion));
            Assert.Equal(captured, originalRevisionSnapshot);
            var originalRevisionJson = JsonSerializer.Serialize(originalRevision);

            var revisionRequest = new RevisePlanRequestV1(1, workCenterId,
                [new PlannedInputRequestV1(labelId, "900",
                    stocked.LabelVersion.ToString(CultureInfo.InvariantCulture))], null,
                "Genuine teknik snapshot revizyonu");
            using var revisionMessage = new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/manufacturing/plans/{planId:D}/revision")
            { Content = JsonContent.Create(revisionRequest) };
            revisionMessage.Headers.Add("Idempotency-Key", "product-genuine-sac-manufacturing-plan-revision-once");
            using var revisedPlanResponse = await freshClient.SendAsync(revisionMessage);
            Assert.Equal(HttpStatusCode.OK, revisedPlanResponse.StatusCode);
            var revisedPlan = (await revisedPlanResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
            var revisedInput = Assert.Single(revisedPlan.Inputs);
            var revisedSnapshot = Assert.IsType<CapturedPlanTechnicalSnapshotViewV1>(revisedInput.TechnicalSnapshot);
            Assert.Equal((planId, "Draft", 2L, workCenterId, labelId, "900"),
                (revisedPlan.PlanId, revisedPlan.Status, revisedPlan.Version, revisedPlan.WorkCenterId,
                    revisedInput.ParentRollId, revisedInput.PlannedKg));
            Assert.Equal(captured, revisedSnapshot);

            using var revisedHistoryResponse = await freshClient.GetAsync(
                $"/api/v1/manufacturing/plans/{planId:D}/revisions");
            Assert.Equal(HttpStatusCode.OK, revisedHistoryResponse.StatusCode);
            var revisedHistory = (await revisedHistoryResponse.Content.ReadFromJsonAsync<PlanRevisionListViewV1>())!;
            Assert.Equal(planId, revisedHistory.PlanId);
            Assert.Equal([0, 1], revisedHistory.Revisions.Select(revision => revision.Revision));
            Assert.Equal([1L, 2L], revisedHistory.Revisions.Select(revision => revision.PlanVersion));
            Assert.Equal(originalRevisionJson, JsonSerializer.Serialize(revisedHistory.Revisions[0]));
            var recordedRevisionInput = Assert.Single(revisedHistory.Revisions[1].Inputs);
            Assert.Equal((labelId, "900", stocked.LabelVersion.ToString(CultureInfo.InvariantCulture)),
                (recordedRevisionInput.ParentRollId, recordedRevisionInput.PlannedKg,
                    recordedRevisionInput.ExpectedLabelVersion));
            Assert.Equal(revisedSnapshot, Assert.IsType<CapturedPlanTechnicalSnapshotViewV1>(
                recordedRevisionInput.TechnicalSnapshot));

            await using (var sql = new SqlConnection(factory.DatabaseConnectionString))
            {
                await sql.OpenAsync();
                await using var storedPlan = sql.CreateCommand();
                storedPlan.CommandText = """
                    SELECT [ParentRollId], [ExpectedLabelVersion], [TechnicalProductId], [TechnicalProductVersion],
                        [TechnicalFamilyId], [TechnicalFamilySourceRevision],
                        [TechnicalTemplateId], [TechnicalTemplateVersion], [TechnicalTemplateSourceRevision],
                        [TechnicalKilogramUnitCompanyId], [TechnicalKilogramUnitId],
                        [TechnicalKilogramProfileId], [TechnicalKilogramProfileVersion],
                        [TechnicalThicknessMm], [TechnicalWidthMm],
                        (SELECT COUNT(*) FROM [manufacturing].[Plans]
                            WHERE [CompanyId] = @company AND [Id] = @plan),
                        (SELECT COUNT(*) FROM [manufacturing].[Commands]
                            WHERE [CompanyId] = @company
                              AND [CommandName] = N'manufacturing.plan.save.v1')
                    FROM [manufacturing].[PlanInputs]
                    WHERE [CompanyId] = @company AND [PlanId] = @plan;
                    """;
                storedPlan.Parameters.AddWithValue("@company", CompanyA);
                storedPlan.Parameters.AddWithValue("@plan", planId);
                await using var row = await storedPlan.ExecuteReaderAsync();
                Assert.True(await row.ReadAsync());
                Assert.Equal((labelId, stocked.LabelVersion, stocked.ProductId, stocked.ProductVersion),
                    (row.GetGuid(0), row.GetInt64(1), row.GetGuid(2), row.GetString(3)));
                Assert.Equal((captured.Family.FamilyId, captured.Family.SourceRevision,
                    captured.Template.TemplateId, captured.Template.TemplateVersion,
                    captured.Template.SourceRevision),
                    (row.GetGuid(4), row.GetString(5), row.GetGuid(6), row.GetString(7), row.GetString(8)));
                Assert.Equal((captured.UnitKg.CompanyId, captured.UnitKg.UnitId, captured.UnitKg.ProfileId,
                    captured.UnitKg.ProfileVersion),
                    (row.GetGuid(9), row.GetGuid(10), row.GetGuid(11), row.GetString(12)));
                Assert.Equal((3m, 248m), (row.GetDecimal(13), row.GetDecimal(14)));
                Assert.Equal((1, 1), (row.GetInt32(15), row.GetInt32(16)));
                Assert.False(await row.ReadAsync());
            }
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

    private static async Task<Guid[]> OrderIdsDescendingAsync(SqlConnection connection, Guid[] ids)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Id] FROM (VALUES (@first), (@second), (@third)) AS [Ids]([Id]) ORDER BY [Id] DESC;";
        command.Parameters.AddWithValue("@first", ids[0]);
        command.Parameters.AddWithValue("@second", ids[1]);
        command.Parameters.AddWithValue("@third", ids[2]);
        var ordered = new List<Guid>(ids.Length);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ordered.Add(reader.GetGuid(0));
        return ordered.ToArray();
    }

    private static GroupSharedAuditEventData HistoryData(Guid productId, Guid eventId,
        DateTimeOffset occurredAt, string operation, long? versionBefore, long versionAfter)
    {
        var create = operation == ProductHistoryApiV1.DraftCreate;
        return GroupSharedAuditEventData.Create(new(eventId,
            new(productId, CompanyA, "same-time-cutoff-fixture"), occurredAt,
            create ? "product.draft.created.v1" : "product.draft.basic-edited.v1",
            "Product.Product", productId.ToString("D"), operation, versionBefore, versionAfter,
            "User", ActorId.ToString("D"), create ? "product.draft.create" : "product.draft.edit",
            "[\"SYSTEM_ADMINISTRATOR\"]", "{}", "{}", $"Synthetic v{versionAfter}", "None", null,
            "System", null, null, $"same-time-v{versionAfter}", null, new string('A', 64), "Succeeded", 2));
    }

    private static async Task InsertGroupSharedAuditAsync(SqlConnection connection, GroupSharedAuditEventData data)
    {
        var envelope = data.Envelope;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [audit].[AuditEvents]
                ([Id], [CompanyId], [SubjectKind], [OriginCompanyId], [AuthorityRevision], [OccurredAtUtc],
                 [EventType], [EntityType], [EntityId], [Operation], [EntityVersionBefore], [EntityVersionAfter],
                 [ActorType], [ActorId], [PermissionCode], [RoleCodesJson], [BeforeJson], [AfterJson], [Reason],
                 [ApprovalContextJson], [AiInvolvement], [AiContextJson], [SourceChannel], [ClientIpAddress],
                 [UserAgent], [CorrelationId], [CausationId], [IdempotencyKeyHash], [Outcome], [SchemaVersion], [IntegrityHash])
            VALUES
                (@id, NULL, N'GroupSharedProduct', @origin, @revision, @occurredAt,
                 @eventType, @entityType, @entityId, @operation, @versionBefore, @versionAfter,
                 @actorType, @actorId, @permission, @roles, @before, @after, @reason,
                 NULL, @ai, NULL, @channel, NULL, NULL, @correlation, NULL, @idempotency, @outcome, @schema, @integrity);
            """;
        command.Parameters.AddWithValue("@id", envelope.Id);
        command.Parameters.AddWithValue("@origin", envelope.Subject.OriginCompanyId);
        command.Parameters.AddWithValue("@revision", envelope.Subject.AuthorityRevision);
        command.Parameters.AddWithValue("@occurredAt", envelope.OccurredAtUtc);
        command.Parameters.AddWithValue("@eventType", envelope.EventType);
        command.Parameters.AddWithValue("@entityType", envelope.EntityType);
        command.Parameters.AddWithValue("@entityId", envelope.EntityId);
        command.Parameters.AddWithValue("@operation", envelope.Operation);
        command.Parameters.AddWithValue("@versionBefore", (object?)envelope.EntityVersionBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("@versionAfter", envelope.EntityVersionAfter);
        command.Parameters.AddWithValue("@actorType", envelope.ActorType);
        command.Parameters.AddWithValue("@actorId", envelope.ActorId);
        command.Parameters.AddWithValue("@permission", envelope.PermissionCode);
        command.Parameters.AddWithValue("@roles", envelope.RoleCodesJson);
        command.Parameters.AddWithValue("@before", envelope.BeforeJson);
        command.Parameters.AddWithValue("@after", envelope.AfterJson);
        command.Parameters.AddWithValue("@reason", envelope.Reason);
        command.Parameters.AddWithValue("@ai", envelope.AiInvolvement);
        command.Parameters.AddWithValue("@channel", envelope.SourceChannel);
        command.Parameters.AddWithValue("@correlation", envelope.CorrelationId);
        command.Parameters.AddWithValue("@idempotency", envelope.IdempotencyKeyHash);
        command.Parameters.AddWithValue("@outcome", envelope.Outcome);
        command.Parameters.AddWithValue("@schema", envelope.SchemaVersion);
        command.Parameters.AddWithValue("@integrity", data.IntegrityHash);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
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

    private static async Task AssertRollbackRefusedAsync(DbContext database, int errorNumber,
        string? targetMigration = null)
    {
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(applied);
        var target = targetMigration ?? (applied.Length == 1 ? Migration.InitialDatabase : applied[^2]);
        var error = await Assert.ThrowsAsync<SqlException>(() => database.GetService<IMigrator>().MigrateAsync(target));
        Assert.Equal(errorNumber, error.Number);
        Assert.Equal(applied, await database.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public Task ProductGlobalRuntime_ShellCapabilitiesFollowExplicitGlobalAssignments() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            using var administrator = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var companyOnly = factory.CreateClientFor("company-a-user", CompanyA);
            var before = await SnapshotAsync(factory.DatabaseConnectionString);
            var first = (await administrator.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;
            Assert.Equal(new[] { "product.items.create", "product.items.detail", "product.items.edit", "product.items.list" }, first.Capabilities);
            var denied = (await companyOnly.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;
            Assert.Empty(denied.Capabilities);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var grant = await identity.Set<ProductMasterAssignment>().SingleAsync(row => row.UserId == ActorId && row.Operation == ProductMasterOperation.Read);
                grant.Revoke(Guid.NewGuid(), "Isolated Product Shell read revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync();
            }
            var withoutRead = (await administrator.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;
            Assert.Equal(new[] { "product.items.create", "product.items.edit" }, withoutRead.Capabilities);
            Assert.NotEqual(first.Generation, withoutRead.Generation);
            using var rejectedRead = await administrator.GetAsync("/api/v1/products");
            Assert.Equal(HttpStatusCode.Forbidden, rejectedRead.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                foreach (var grant in await identity.Set<ProductMasterAssignment>().Where(row => row.UserId == ActorId && row.IsActive).ToArrayAsync())
                    grant.Revoke(Guid.NewGuid(), "Isolated Product Shell command revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync();
            }
            var withoutGlobal = (await administrator.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;
            Assert.Empty(withoutGlobal.Capabilities); // The existing company administrator role remains assigned.
            Assert.NotEqual(withoutRead.Generation, withoutGlobal.Generation);
            Assert.Equal(before, await SnapshotAsync(factory.DatabaseConnectionString));
        }, services =>
        {
            var provider = new ProductShellFixtureProvider();
            var revision = ShellCapabilityRegistry.ComputeCandidateRevision([provider]);
            Assert.Equal("sha256:h3eow9s5L3gYs0heRdgllKTw5iTNcXXLokOTL9QCVMY", revision);
            services.RemoveAll<IShellCapabilityProvider>();
            services.AddSingleton<IShellCapabilityProvider>(provider);
            services.RemoveAll<ShellCapabilityAuthorityCatalog>();
            services.AddSingleton(new ShellCapabilityAuthorityCatalog([revision]));
        });

    private sealed class ProductShellFixtureProvider : IShellCapabilityProvider
    {
        public string Owner => "PRODUCT";
        public string ContractVersion => "PRODUCT-TEST-CAPABILITY-V1";
        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
        [
            Declaration("create", "/urun/yeni", GroupSharedMasterOperation.DraftCreate, false),
            Declaration("detail", "/urun/:productId", GroupSharedMasterOperation.Read, true),
            Declaration("edit", "/urun/:productId/duzenle", GroupSharedMasterOperation.DraftBasicEdit, true),
            Declaration("list", "/urun", GroupSharedMasterOperation.Read, false),
        ];
        private static ShellCapabilityDeclaration Declaration(string action, string path, GroupSharedMasterOperation operation, bool parameterized) =>
            new("product.items." + action, "product.items." + action, path, "PRODUCT-TEST-" + action.ToUpperInvariant(),
                parameterized ? ShellCapabilityRoutePrecedence.ParameterizedAfterStatic : ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.AnyGrantedScope, [], true, operation);
    }

    private sealed class StockDisplayFixtureCompany : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = ApprovedUnitProfileCatalog.GdcCompanyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class StockDisplayFixtureUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "stock-display-fixture-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } =
            new HashSet<Guid> { ApprovedUnitProfileCatalog.GdcCompanyId };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class StockDisplayFixtureAuthority : IErpAuthorityRevalidationService
    {
        public List<string> Permissions { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Permissions.Add(permission);
            return Task.FromResult(companyId == ApprovedUnitProfileCatalog.GdcCompanyId
                ? new PermissionScope(true, true,
                    new HashSet<string>(StringComparer.Ordinal) { "synthetic-stock-display-fixture" },
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class StockDisplayFixtureSettings : ISettingsPrerequisiteProvider
    {
        public Task<UnitProfileV1> GetUnitProfileAsync(
            Guid unitId,
            DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            var approved = ApprovedUnitProfileCatalog.GdcKilogramV1;
            Assert.Equal(approved.UnitId, unitId);
            Assert.Equal(ProductStockDisplayQualifiedFixture.EffectiveOn, effectiveOn);
            return Task.FromResult(new UnitProfileV1(
                approved.CompanyId,
                approved.UnitId,
                approved.UnitCode,
                approved.DimensionCode,
                approved.IsKilogram,
                new(approved.ProfileId, approved.Version),
                approved.Scale,
                approved.IsActive,
                approved.EffectiveFrom,
                approved.EffectiveThrough));
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(
            Guid currencyId,
            string operation,
            DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(
            string operation,
            DateOnly occurredOn,
            DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, TestObserver, Task> test,
        Action<IServiceCollection>? configureServices = null)
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
            configureServices?.Invoke(services);
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

    private static async Task<HttpResponseMessage> PreviewImportAsync(HttpClient client, byte[] workbook)
    {
        using var content = ImportForm(workbook);
        return await client.PostAsync(ProductImportApiV1.PreviewPath, content);
    }

    private static async Task<HttpResponseMessage> ApplyImportAsync(HttpClient client, byte[] workbook,
        ProductImportPreviewV1 preview, string key)
    {
        using var content = ImportForm(workbook);
        content.Add(new StringContent(preview.FileSha256), ProductImportApiV1.FileSha256Field);
        content.Add(new StringContent(preview.PreviewToken), ProductImportApiV1.PreviewTokenField);
        using var request = new HttpRequestMessage(HttpMethod.Post, ProductImportApiV1.ApplyPath) { Content = content };
        request.Headers.Add(ProductApiV1.IdempotencyHeader, key);
        return await client.SendAsync(request);
    }

    private static MultipartFormDataContent ImportForm(byte[] workbook)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(workbook);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(file, ProductImportApiV1.FileField, "product-import.xlsx");
        content.Add(new StringContent("Eski Kod"), ProductImportApiV1.LegacyCodeColumnField);
        content.Add(new StringContent("Ürün Adı"), ProductImportApiV1.NameColumnField);
        content.Add(new StringContent("Grup Kimliği"), ProductImportApiV1.GroupIdColumnField);
        return content;
    }

    private static byte[] ImportWorkbook(params (string LegacyCode, string Name)[] rows)
    {
        var sheet = new StringBuilder("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>Eski Kod</t></is></c><c r="B1" t="inlineStr"><is><t>Ürün Adı</t></is></c><c r="C1" t="inlineStr"><is><t>Grup Kimliği</t></is></c></row>
            """);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = index + 2;
            sheet.Append($"<row r=\"{row}\"><c r=\"A{row}\" t=\"inlineStr\"><is><t>{rows[index].LegacyCode}</t></is></c>")
                .Append($"<c r=\"B{row}\" t=\"inlineStr\"><is><t>{rows[index].Name}</t></is></c>")
                .Append($"<c r=\"C{row}\" t=\"inlineStr\"><is><t>{GroupId:D}</t></is></c></row>");
        }
        sheet.Append("</sheetData></worksheet>");

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteWorkbookEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/import.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            WriteWorkbookEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteWorkbookEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Ürünler" sheetId="1" r:id="rIdImport"/></sheets></workbook>
                """);
            WriteWorkbookEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImport" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/import.xml"/>
                </Relationships>
                """);
            WriteWorkbookEntry(archive, "xl/worksheets/import.xml", sheet.ToString());
        }
        return output.ToArray();
    }

    private static void WriteWorkbookEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
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
