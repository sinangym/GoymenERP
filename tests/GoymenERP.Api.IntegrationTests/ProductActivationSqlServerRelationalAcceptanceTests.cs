using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-PRD-010 approval (Draft -> Active) through the REAL host on a REAL SQL Server: the global Product
/// Activate authority, the shared command boundary (replay, race, late revocation), the canonical audit and
/// the Product row. Grants are synthetic, isolated Product master assignments in this test's own database,
/// never a Sinan or production grant; the code group is an isolated test row.
/// </summary>
public sealed class ProductActivationSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupId = Guid.Parse("82000000-0000-0000-0000-000000000011");

    private static readonly ProductMasterOperation[] DraftOperations =
        [ProductMasterOperation.Read, ProductMasterOperation.DraftCreate, ProductMasterOperation.DraftBasicEdit];

    [SqlServerFact]
    public Task An_approver_activates_a_draft_once_and_the_same_key_replays_it() =>
        WithDatabaseAsync(withActivate: true, async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var product = await CreateDraftAsync(client, "activation-create-1");

            using var activated = await ActivateAsync(client, product.Id, "activation-1", "1");
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
            Assert.False(activated.Headers.Contains("Idempotency-Replayed"));
            var result = (await activated.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.Equal(("Active", "2", false), (result.Card.Product.Lifecycle, result.Card.Product.Version, result.IsReplay));
            var correlation = Assert.Single(activated.Headers.GetValues("X-Correlation-Id"));

            // A lost answer retried with the same key is the stored one, with the first correlation.
            using var replay = await ActivateAsync(client, product.Id, "activation-1", "1");
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(correlation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.True((await replay.Content.ReadFromJsonAsync<ProductMutationResponse>())!.IsReplay);

            // The same key with another request is a conflict that discloses nothing.
            using var other = await ActivateAsync(client, product.Id, "activation-1", "2");
            Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
            Assert.Equal(ProductApiV1.IdempotencyConflict, await CodeAsync(other));

            // The card is Active for every later read.
            using var read = await client.GetAsync($"/api/v1/products/{product.Id:D}");
            Assert.Equal("Active", (await read.Content.ReadFromJsonAsync<ProductCardView>())!.Product.Lifecycle);
            Assert.Equal((1, 2L), (await ScalarAsync<int>(factory, "SELECT [Lifecycle] FROM [product].[Products] WHERE [Id] = @id", product.Id),
                await ScalarAsync<long>(factory, "SELECT [Version] FROM [product].[Products] WHERE [Id] = @id", product.Id)));
            Assert.Equal(1, await ScalarAsync<int>(factory,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.card.activated.v1'",
                product.Id));
        });

    [SqlServerFact]
    public Task Without_the_Activate_authority_or_after_its_late_revocation_nothing_changes() =>
        WithDatabaseAsync(withActivate: false, async (factory, observer) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var product = await CreateDraftAsync(client, "activation-create-2");

            // Draft grants alone never approve a card.
            using var denied = await ActivateAsync(client, product.Id, "activation-denied", "1");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(0, await ScalarAsync<int>(factory, "SELECT [Lifecycle] FROM [product].[Products] WHERE [Id] = @id", product.Id));

            // Granted, then revoked while the approval is in flight: 403 and nothing is written.
            await GrantAsync(factory, ProductMasterOperation.Activate);
            observer.Callback = async (stage, _, cancellationToken) =>
            {
                if (stage != ProductCommandAttemptStage.BeforeCommit) return;
                await using var scope = factory.Services.CreateAsyncScope();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var grant = await identity.Set<ProductMasterAssignment>().SingleAsync(row => row.UserId == ActorId
                    && row.Operation == ProductMasterOperation.Activate && row.IsActive, cancellationToken);
                grant.Revoke(Guid.NewGuid(), "Synthetic isolated SQL late revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync(cancellationToken);
            };
            using var revoked = await ActivateAsync(client, product.Id, "activation-revoked", "1");
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
            observer.Callback = null;
            Assert.Equal((0, 1L), (await ScalarAsync<int>(factory, "SELECT [Lifecycle] FROM [product].[Products] WHERE [Id] = @id", product.Id),
                await ScalarAsync<long>(factory, "SELECT [Version] FROM [product].[Products] WHERE [Id] = @id", product.Id)));
            Assert.Equal(0, await ScalarAsync<int>(factory,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.card.activated.v1'",
                product.Id));
        });

    [SqlServerFact]
    public Task A_stale_version_or_a_card_that_is_not_a_draft_is_refused_and_an_active_card_is_not_edited() =>
        WithDatabaseAsync(withActivate: true, async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var product = await CreateDraftAsync(client, "activation-create-3");

            using var stale = await ActivateAsync(client, product.Id, "activation-stale", "7");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(ProductApiV1.VersionConflict, await CodeAsync(stale));

            using var unknown = await ActivateAsync(client, Guid.NewGuid(), "activation-unknown", "1");
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

            using var activated = await ActivateAsync(client, product.Id, "activation-3", "1");
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

            // Only Draft becomes Active: an Active card is not approved again, nor edited as a draft.
            using var again = await ActivateAsync(client, product.Id, "activation-again", "2");
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal(ProductApiV1.LifecycleConflict, await CodeAsync(again));
            var edit = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/products/{product.Id:D}/draft")
            {
                Content = JsonContent.Create(new { expectedVersion = "2", changes = new { name = "SENTETIK edited" }, reason = "Isolated SQL edit" }),
            };
            edit.Headers.Add("Idempotency-Key", "activation-edit-after");
            using var edited = await client.SendAsync(edit);
            Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
            Assert.Equal(ProductApiV1.LifecycleConflict, await CodeAsync(edited));
            Assert.Equal(2L, await ScalarAsync<long>(factory, "SELECT [Version] FROM [product].[Products] WHERE [Id] = @id", product.Id));
        });

    [SqlServerFact]
    public Task Two_approvals_of_one_key_racing_leave_one_transition_and_the_loser_replays_it() =>
        WithDatabaseAsync(withActivate: true, async (factory, observer) =>
        {
            using var first = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var second = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var product = await CreateDraftAsync(first, "activation-create-4");
            var staged = new TwoAttemptStageBarrier();
            observer.Callback = staged.ObserveAsync;

            var responses = await Task.WhenAll(
                ActivateAsync(first, product.Id, "activation-race", "1"),
                ActivateAsync(second, product.Id, "activation-race", "1"));
            observer.Callback = null;

            Assert.Equal(2, staged.ArrivalCount);
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var results = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<ProductMutationResponse>()));
            Assert.NotEqual(results[0]!.IsReplay, results[1]!.IsReplay);
            Assert.All(results, result => Assert.Equal(("Active", "2"), (result!.Card.Product.Lifecycle, result.Card.Product.Version)));
            foreach (var response in responses) response.Dispose();
            Assert.Equal(1, await ScalarAsync<int>(factory,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.card.activated.v1'",
                product.Id));
            Assert.Equal(2L, await ScalarAsync<long>(factory, "SELECT [Version] FROM [product].[Products] WHERE [Id] = @id", product.Id));
        });

    [SqlServerFact]
    public Task An_activated_card_is_listed_as_active_and_a_draft_stays_a_draft() =>
        WithDatabaseAsync(withActivate: true, async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var approved = await CreateDraftAsync(client, "activation-create-5a");
            var draft = await CreateDraftAsync(client, "activation-create-5b");
            using var activated = await ActivateAsync(client, approved.Id, "activation-5", "1");
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

            using var active = await client.GetAsync("/api/v1/products?lifecycle=Active");
            var activeItems = (await active.Content.ReadFromJsonAsync<ProductPage>())!.Items;
            Assert.Equal([approved.Id], activeItems.Select(item => item.Id));
            using var drafts = await client.GetAsync("/api/v1/products?lifecycle=Draft");
            Assert.Equal([draft.Id], (await drafts.Content.ReadFromJsonAsync<ProductPage>())!.Items.Select(item => item.Id));
        });

    private static async Task<ProductDraftView> CreateDraftAsync(HttpClient client, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDraftRequest($"SENTETIK {key}", GroupId)),
        };
        request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product;
    }

    private static Task<HttpResponseMessage> ActivateAsync(HttpClient client, Guid id, string key, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{id:D}/activation")
        {
            Content = JsonContent.Create(new ActivateProductRequest(version)),
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<T> ScalarAsync<T>(SqlServerRelationalApiFactory factory, string sql, Guid id)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task GrantAsync(SqlServerRelationalApiFactory factory, params ProductMasterOperation[] operations)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        foreach (var operation in operations)
            identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                "Synthetic isolated SQL fixture; not a Sinan or production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null));
        await identity.SaveChangesAsync();
    }

    private static async Task WithDatabaseAsync(bool withActivate, Func<SqlServerRelationalApiFactory, TestObserver, Task> test)
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
            await GrantAsync(factory, withActivate ? [.. DraftOperations, ProductMasterOperation.Activate] : DraftOperations);
            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var seed = sql.CreateCommand();
                seed.CommandText = "INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix]) VALUES (@id, N'SENTETIK activation group', 'PACT');";
                seed.Parameters.AddWithValue("@id", GroupId);
                Assert.Equal(1, await seed.ExecuteNonQueryAsync());
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

    private sealed class TestObserver : IProductCommandAttemptObserver
    {
        public Func<ProductCommandAttemptStage, GroupSharedMasterCommandContext, CancellationToken, Task>? Callback { get; set; }

        public Task ObserveAsync(ProductCommandAttemptStage stage, GroupSharedMasterCommandContext authority,
            CancellationToken cancellationToken) => Callback?.Invoke(stage, authority, cancellationToken) ?? Task.CompletedTask;
    }

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
