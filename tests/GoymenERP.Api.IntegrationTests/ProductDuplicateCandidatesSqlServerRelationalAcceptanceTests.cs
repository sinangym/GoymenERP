using System.Collections.Concurrent;
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
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Spec PRODUCT-U01-U02-JIT-001 §7/§10.2/§15 through the REAL host on a REAL SQL Server: the read-only exact-name
/// duplicate preview (a warning that never blocks) and the code hard block (the database refuses a second holder).
/// Grants are synthetic, isolated Product master assignments in this test's own database, never a Sinan or
/// production grant; the code group is an isolated test row.
/// </summary>
public sealed class ProductDuplicateCandidatesSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupId = Guid.Parse("82000000-0000-0000-0000-000000000021");

    private static readonly ProductMasterOperation[] AllOperations =
    [
        ProductMasterOperation.Read, ProductMasterOperation.DraftCreate, ProductMasterOperation.DraftBasicEdit,
        ProductMasterOperation.Activate,
    ];

    [SqlServerFact]
    public Task Exact_name_candidates_follow_the_Turkish_name_collation_across_lifecycles_and_never_list_the_card_itself() =>
        WithDatabaseAsync(AllOperations, async (factory, observer) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var upper = await CreateDraftAsync(client, "SENTETİK SİYAH SAC", "dup-upper");
            var lower = await CreateDraftAsync(client, "sentetik siyah sac", "dup-lower");
            // Not the same name under the rule: a longer name (no substring rule), a dotless I (Turkish I/ı is not
            // İ/i), an accent (the collation is accent-sensitive).
            var longer = await CreateDraftAsync(client, "SENTETİK SİYAH SAC 2", "dup-longer");
            var dotless = await CreateDraftAsync(client, "SENTETIK SIYAH SAC", "dup-dotless");
            var accent = await CreateDraftAsync(client, "SENTETİK SİYAH SAÇ", "dup-accent");
            observer.ResetFailureDiagnostic();
            using (var activated = await ActivateAsync(client, upper.Id, "dup-activate-upper", upper.Version))
                Assert.True(activated.StatusCode == HttpStatusCode.OK,
                    $"Expected activation HTTP 200; received {(int)activated.StatusCode}. {observer.SafeFailureDiagnostic}");
            var writesBefore = await WriteCountsAsync(factory);

            // Surrounding blanks are the write path's trim; an Active card is still a candidate; nothing is written.
            var all = await CandidatesAsync(client, "  Sentetik Siyah Sac ", null);
            Assert.Equal((ProductDuplicateRules.ExactName, "Sentetik Siyah Sac", false), (all.Rule, all.Name, all.HasMore));
            Assert.Equal(new[] { upper.Id, lower.Id }.OrderBy(id => Code(id, upper, lower)),
                all.ExactName.Select(item => item.Id));
            Assert.Equal(["Active", "Draft"], all.ExactName.OrderBy(item => item.Code, StringComparer.Ordinal)
                .Select(item => item.Lifecycle).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(all.ExactName, item => item.Id == longer.Id || item.Id == dotless.Id || item.Id == accent.Id);

            // The card being edited or approved never lists itself.
            var excluded = await CandidatesAsync(client, lower.Name, lower.Id);
            Assert.Equal([upper.Id], excluded.ExactName.Select(item => item.Id));
            var dotlessOnly = await CandidatesAsync(client, "sentetık sıyah sac", null);
            Assert.Equal([dotless.Id], dotlessOnly.ExactName.Select(item => item.Id));

            Assert.Equal(writesBefore, await WriteCountsAsync(factory));
        });

    [SqlServerFact]
    public Task The_preview_is_bounded_and_says_there_are_more() =>
        WithDatabaseAsync(AllOperations, async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            for (var index = 0; index <= ProductDuplicateRules.MaximumCandidates; index++)
                await CreateDraftAsync(client, "SENTETIK kalabalik ad", $"dup-many-{index}");

            var page = await CandidatesAsync(client, "SENTETIK kalabalik ad", null);
            Assert.True(page.HasMore);
            Assert.Equal(ProductDuplicateRules.MaximumCandidates, page.ExactName.Count);
            Assert.Equal(page.ExactName.Select(item => item.Code).Order(StringComparer.Ordinal),
                page.ExactName.Select(item => item.Code));
        });

    [SqlServerFact]
    public Task Without_the_global_Read_the_preview_is_refused_before_the_name_is_compared_or_judged() =>
        WithDatabaseAsync([ProductMasterOperation.DraftCreate], async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            await SeedProductRowAsync(factory, "SENTETIK gizli ad");

            using var denied = await client.GetAsync($"{ProductDuplicateRules.Path}?name=SENTETIK%20gizli%20ad");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var body = await denied.Content.ReadAsStringAsync();
            Assert.DoesNotContain("gizli", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PDUP", body, StringComparison.Ordinal);

            // Even a malformed name is not judged for an actor without the Read: no 400 oracle.
            using var malformed = await client.GetAsync($"{ProductDuplicateRules.Path}?name=%20");
            Assert.Equal(HttpStatusCode.Forbidden, malformed.StatusCode);
        });

    [SqlServerFact]
    public Task Two_same_named_drafts_approved_concurrently_both_become_active_and_each_still_lists_the_other() =>
        WithDatabaseAsync(AllOperations, async (factory, observer) =>
        {
            using var first = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var second = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            // Turkish case pairs (i/İ, ı/I): the two names are one name under the Turkish name collation.
            var left = await CreateDraftAsync(first, "sentetik yarış adı", "dup-race-left");
            var right = await CreateDraftAsync(first, "SENTETİK YARIŞ ADI", "dup-race-right");
            Assert.NotEqual(left.Code, right.Code);

            // Both approvals are held until both have staged their card, so they overlap in the database.
            var staged = new TwoAttemptStageBarrier();
            observer.Callback = staged.ObserveAsync;
            var responses = await Task.WhenAll(
                ActivateAsync(first, left.Id, "dup-race-activate-left", left.Version),
                ActivateAsync(second, right.Id, "dup-race-activate-right", right.Version));
            observer.Callback = null;

            // §10.2: a name match never blocks an approval, and a race of two such approvals loses nothing.
            Assert.Equal(2, staged.ArrivalCount);
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            foreach (var response in responses) response.Dispose();
            Assert.Equal(2, await ScalarAsync<int>(factory,
                "SELECT COUNT(*) FROM [product].[Products] WHERE [Lifecycle] = 1 AND [Id] IN (@id, @other)", left.Id, right.Id));
            Assert.Equal(2, await ScalarAsync<int>(factory,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.card.activated.v1'", left.Id));

            Assert.Equal([right.Id], (await CandidatesAsync(first, left.Name, left.Id)).ExactName.Select(item => item.Id));
            Assert.Equal([left.Id], (await CandidatesAsync(first, right.Name, right.Id)).ExactName.Select(item => item.Id));
        });

    [SqlServerFact]
    public Task Two_cards_can_never_hold_one_code_the_database_refuses_the_second_holder() =>
        WithDatabaseAsync(AllOperations, async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var holder = await CreateDraftAsync(client, "SENTETIK kod sahibi", "dup-code-holder");
            var other = await CreateDraftAsync(client, "SENTETIK kod isteyen", "dup-code-other");

            // §7: the code/alias exact collision is the hard block, enforced by the store itself (the same unique
            // keys that settle the server's own minting race), not by a check that could be raced.
            var onCard = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory,
                "UPDATE [product].[Products] SET [Code] = @code WHERE [Id] = @id", ("@code", holder.Code), ("@id", other.Id)));
            Assert.Contains(onCard.Number, new[] { 2601, 2627 });
            var onRegistration = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory,
                "UPDATE [product].[CodeRegistrations] SET [Code] = @code WHERE [ProductId] = @id", ("@code", holder.Code), ("@id", other.Id)));
            Assert.Contains(onRegistration.Number, new[] { 2601, 2627 });

            Assert.Equal(other.Code, await ScalarAsync<string>(factory, "SELECT [Code] FROM [product].[Products] WHERE [Id] = @id", other.Id));
            Assert.Equal(1, await ScalarAsync<int>(factory, "SELECT COUNT(*) FROM [product].[Products] WHERE [Code] = (SELECT [Code] FROM [product].[Products] WHERE [Id] = @id)", holder.Id));
        });

    private static string Code(Guid id, params ProductDraftView[] cards) => cards.Single(card => card.Id == id).Code;

    private static async Task<ProductDuplicateCandidates> CandidatesAsync(HttpClient client, string name, Guid? exclude)
    {
        var query = $"{ProductDuplicateRules.Path}?name={Uri.EscapeDataString(name)}"
            + (exclude is null ? string.Empty : $"&excludeProductId={exclude:D}");
        using var response = await client.GetAsync(query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        return (await response.Content.ReadFromJsonAsync<ProductDuplicateCandidates>())!;
    }

    private static async Task<ProductDraftView> CreateDraftAsync(HttpClient client, string name, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDraftRequest(name, GroupId)),
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

    /// <summary>Product rows, Product audit events and stored command terminals: a read must move none of them.</summary>
    private static async Task<(int Products, int Audit, int Terminals)> WriteCountsAsync(SqlServerRelationalApiFactory factory) =>
        (await ScalarAsync<int>(factory, "SELECT COUNT(*) FROM [product].[Products]", Guid.Empty),
            await ScalarAsync<int>(factory, "SELECT COUNT(*) FROM [audit].[AuditEvents]", Guid.Empty),
            await ScalarAsync<int>(factory, "SELECT COUNT(*) FROM [idempotency].[GroupSharedCommandTerminals]", Guid.Empty));

    private static async Task<T> ScalarAsync<T>(SqlServerRelationalApiFactory factory, string sql, Guid id, Guid? other = null)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@other", other ?? Guid.Empty);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqlServerRelationalApiFactory factory, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A card created under a Read granted for the moment and revoked again, so the actor is left without it.</summary>
    private static async Task SeedProductRowAsync(SqlServerRelationalApiFactory factory, string name)
    {
        await GrantAsync(factory, ProductMasterOperation.Read);
        using (var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA))
            await CreateDraftAsync(client, name, "dup-seed-hidden");
        await RevokeAsync(factory, ProductMasterOperation.Read);
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

    private static async Task RevokeAsync(SqlServerRelationalApiFactory factory, ProductMasterOperation operation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        foreach (var grant in await identity.Set<ProductMasterAssignment>().Where(row => row.UserId == ActorId
            && row.Operation == operation && row.IsActive).ToListAsync())
            grant.Revoke(Guid.NewGuid(), "Synthetic isolated SQL fixture revocation", DateTimeOffset.UtcNow);
        await identity.SaveChangesAsync();
    }

    private static async Task WithDatabaseAsync(ProductMasterOperation[] operations,
        Func<SqlServerRelationalApiFactory, TestObserver, Task> test)
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
            // Observe only this isolated factory; canonical redacted handling still runs.
            services.Insert(0, ServiceDescriptor.Singleton<IExceptionHandler>(observer));
        }, databaseName: databaseName);
        try
        {
            using var startup = factory.CreateClient();
            await GrantAsync(factory, operations);
            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var seed = sql.CreateCommand();
                seed.CommandText = "INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix]) VALUES (@id, N'SENTETIK duplicate group', 'PDUP');";
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

    private sealed class TestObserver : IProductCommandAttemptObserver, IExceptionHandler
    {
        private readonly ConcurrentQueue<string> exceptionKinds = new();
        private int lastStage = -1;

        public Func<ProductCommandAttemptStage, GroupSharedMasterCommandContext, CancellationToken, Task>? Callback { get; set; }

        public string SafeFailureDiagnostic =>
            $"stage={StageLabel()}; exceptionKinds={string.Join("|", exceptionKinds)}";

        private string StageLabel() => Volatile.Read(ref lastStage) switch
        {
            (int)ProductCommandAttemptStage.ProductStaged => "product-staged",
            (int)ProductCommandAttemptStage.ProductFlushed => "product-flushed",
            (int)ProductCommandAttemptStage.AuditFlushed => "audit-flushed",
            (int)ProductCommandAttemptStage.TerminalFlushed => "terminal-flushed",
            (int)ProductCommandAttemptStage.BeforeCommit => "before-commit",
            _ => "not-observed",
        };

        public void ResetFailureDiagnostic()
        {
            exceptionKinds.Clear();
            Volatile.Write(ref lastStage, -1);
        }

        public Task ObserveAsync(ProductCommandAttemptStage stage, GroupSharedMasterCommandContext authority,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref lastStage, (int)stage);
            return Callback?.Invoke(stage, authority, cancellationToken) ?? Task.CompletedTask;
        }

        public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
            CancellationToken cancellationToken)
        {
            // Never emit messages, SQL text, stack traces, bodies or connection strings.
            Exception? current = exception;
            for (var depth = 0; current is not null && depth < 8; depth++, current = current.InnerException)
                exceptionKinds.Enqueue(current switch
                {
                    SqlException sql => $"sql(Number={sql.Number},State={sql.State},Class={sql.Class})",
                    DbUpdateConcurrencyException => "ef-concurrency",
                    DbUpdateException => "ef-update",
                    OperationCanceledException => "cancelled",
                    TimeoutException => "timeout",
                    InvalidOperationException => "invalid-operation",
                    UnauthorizedAccessException => "unauthorized",
                    _ => "unknown",
                });
            return ValueTask.FromResult(false);
        }
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
