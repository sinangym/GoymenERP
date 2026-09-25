using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// K-1(a) authority of Product's operational selection read, measured with the host's REAL
/// authorization service against a REAL database: no authorization double anywhere. One service
/// scope stands for one request. The grant is a synthetic test role on the registered name
/// (#277); it is created and removed inside this test's own database, never seeded.
/// <para>
/// The stored-record reader is the one test seam: it answers "no such product", so a caller that
/// passes every gate gets the owner's field error, and it is also where "another request" revokes
/// the grant mid-read. Product rows, the family policy and unit profiles are not what this
/// measures; the service's own tests cover them.
/// </para>
/// </summary>
public sealed class ProductSalesEligibilityAuthoritySqlServerRelationalAcceptanceTests
{
    private const string Permission = ErpPermissions.ProductOperationalSelectionRead;

    [SqlServerFact]
    public async Task Candidate_discovery_denies_before_storage_without_the_narrow_grant()
    {
        var run = await Run.StartAsync();
        try
        {
            await using var request = run.Factory.Services.CreateAsyncScope();
            var reader = new EmptyCandidateReader();
            var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
                run.Candidates(request, reader).SearchAsync(new(null, new(2026, 9, 21)), default));
            Assert.Equal(403, error.StatusCode);
            Assert.Equal(0, reader.Reads);
        }
        finally { await run.DisposeAsync(); }
    }

    [SqlServerFact]
    public async Task Candidate_discovery_discards_window_when_another_request_revokes_authority()
    {
        var run = await Run.StartAsync();
        try
        {
            await run.GrantAsync();
            await using var request = run.Factory.Services.CreateAsyncScope();
            var reader = new EmptyCandidateReader { During = run.RevokeAsync };
            var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
                run.Candidates(request, reader).SearchAsync(new(null, new(2026, 9, 21)), default));
            Assert.Equal((403, "product_access_denied"), (error.StatusCode, error.Code));
            Assert.Equal(1, reader.Reads);
            Assert.Equal(2, run.AuthorityReads.Count);
        }
        finally { await run.DisposeAsync(); }
    }

    [SqlServerFact]
    public async Task A_standing_grant_passes_every_fence_with_one_authority_read_per_fence()
    {
        var run = await Run.StartAsync();
        try
        {
            await run.GrantAsync();
            await using var request = run.Factory.Services.CreateAsyncScope();
            var reader = new MissingProductReader();

            var result = await run.Service(request, reader).EvaluateAsync(Selection(), default);

            // Through the gate and the fence after the read: the owner's own answer comes back.
            Assert.Null(result.Snapshot);
            Assert.Equal(("productId", "invalid_reference"), (result.Errors.Single().Field, result.Errors.Single().Code));
            Assert.Equal(1, reader.Reads);
            // One real authority read before the record read and one at the fence after it.
            Assert.Equal(2, run.AuthorityReads.Count);
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    [SqlServerFact]
    public async Task A_revocation_by_another_request_during_the_read_is_seen_at_the_next_fence()
    {
        // The limit measured in #265 5719822744: with the request's cached view a grant revoked
        // mid-request kept answering yes until the next request. Now it is seen at the next fence.
        var run = await Run.StartAsync();
        try
        {
            await run.GrantAsync();
            await using var request = run.Factory.Services.CreateAsyncScope();
            var reader = new MissingProductReader { During = run.RevokeAsync };

            var error = await Assert.ThrowsAsync<ProductOperationException>(
                () => run.Service(request, reader).EvaluateAsync(Selection(), default));

            Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
            Assert.Equal(1, reader.Reads);
            Assert.Equal(2, run.AuthorityReads.Count);
            // The request's view moved with the measurement: the cached port of the SAME request
            // no longer answers yes, and it pays no further read for that.
            Assert.False(await request.ServiceProvider.GetRequiredService<IErpAuthorizationService>()
                .HasPermissionAsync(Permission, run.Company));
            Assert.Equal(2, run.AuthorityReads.Count);
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    [SqlServerFact]
    public async Task Without_a_grant_the_registered_name_denies_before_any_read()
    {
        // Registered is not granted (#277): the default is closed, and nothing is read.
        var run = await Run.StartAsync();
        try
        {
            await using var request = run.Factory.Services.CreateAsyncScope();
            var reader = new MissingProductReader();

            var error = await Assert.ThrowsAsync<ProductOperationException>(
                () => run.Service(request, reader).EvaluateAsync(Selection(), default));

            Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
            Assert.Equal(0, reader.Reads);
            Assert.Equal(1, run.AuthorityReads.Count);
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    private static ProductSalesSelectionV1 Selection() =>
        new(Guid.Parse("7a1f0c55-2b8e-4d3a-9f61-0c4b5e6d7f80"), "1",
            Guid.Parse("5d2c9b44-1a7e-4c3f-8e50-9b3a4c5d6e7f"), new DateOnly(2026, 9, 18));

    private sealed class Run : IAsyncDisposable
    {
        private Run(SqlServerRelationalApiFactory factory, Guid actor, CountingPermissionScopeReadObserver reads)
        {
            Factory = factory;
            Actor = actor;
            AuthorityReads = reads;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public Guid Actor { get; }
        public Guid Company => TestCompanyIds.CompanyA;
        public CountingPermissionScopeReadObserver AuthorityReads { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var actor = Guid.NewGuid();
            var reads = new CountingPermissionScopeReadObserver();
            var factory = new SqlServerRelationalApiFactory(
                baseConnectionString,
                services =>
                {
                    services.RemoveAll<ICurrentUser>();
                    services.AddSingleton<ICurrentUser>(new FixedCurrentUser(actor, TestCompanyIds.CompanyA));
                    services.RemoveAll<IPermissionScopeReadObserver>();
                    services.AddSingleton<IPermissionScopeReadObserver>(reads);
                });
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return new Run(factory, actor, reads);
        }

        /// <summary>The service as one request would build it, from that request's own scope.</summary>
        public ProductSalesEligibilityService Service(AsyncServiceScope request, IProductSalesRecordReader reader)
        {
            var provider = request.ServiceProvider;
            var company = provider.GetRequiredService<ICompanyContext>();
            company.Set(Company);
            return new ProductSalesEligibilityService(
                company,
                provider.GetRequiredService<ICurrentUser>(),
                provider.GetRequiredService<IErpAuthorityRevalidationService>(),
                reader,
                new UnacceptedProductFamilyRequirementPolicyV1(),
                new ProductUnitProfileAdapter(provider.GetRequiredService<ISettingsPrerequisiteProvider>(), company));
        }

        public ProductSalesCandidateService Candidates(AsyncServiceScope request, IProductSalesCandidateReader reader)
        {
            var eligibility = Service(request, new MissingProductReader());
            var provider = request.ServiceProvider;
            var company = provider.GetRequiredService<ICompanyContext>();
            return new(company, provider.GetRequiredService<ICurrentUser>(),
                provider.GetRequiredService<IErpAuthorityRevalidationService>(), reader, eligibility,
                new ProductUnitProfileAdapter(provider.GetRequiredService<ISettingsPrerequisiteProvider>(), company));
        }

        public async Task GrantAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SELECTION-{roleId:N}", "Synthetic operational selection test role", true);
            role.AddPermission(Permission);
            var assignment = new RoleAssignment(Guid.NewGuid(), Actor, roleId, Company,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, Company);
            dbContext.Users.Add(new UserAccount(Actor, "Synthetic operational selection actor", null));
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            // Not a count of the service's reads: reset after the setup.
            AuthorityReads.Reset();
        }

        public async Task RevokeAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var assignment = await dbContext.RoleAssignments
                .Include(x => x.Scopes)
                .SingleAsync(x => x.UserId == Actor);
            dbContext.RoleAssignments.Remove(assignment);
            await dbContext.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private sealed class EmptyCandidateReader : IProductSalesCandidateReader
    {
        public Func<Task>? During { get; init; }
        public int Reads { get; private set; }
        public async Task<ProductSalesCandidateWindow> ReadAsync(ProductSalesReadScope scope,
            ProductSalesCandidateQueryV1 query, CancellationToken cancellationToken)
        {
            Reads++;
            if (During is not null) await During();
            return new([], false);
        }
    }

    private sealed class MissingProductReader : IProductSalesRecordReader
    {
        public Func<Task>? During { get; init; }
        public int Reads { get; private set; }

        public async Task<ProductStoredRecord?> ReadForSalesSelectionAsync(ProductSalesReadScope scope, Guid productId,
            CancellationToken cancellationToken)
        {
            Reads++;
            if (During is not null) await During();
            return null;
        }
    }

    private sealed class CountingPermissionScopeReadObserver : IPermissionScopeReadObserver
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public void ObserveDatabaseRead(string permission, Guid companyId) => Interlocked.Increment(ref _count);
    }

    private sealed class FixedCurrentUser(Guid userId, params Guid[] companyIds) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companyIds.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
