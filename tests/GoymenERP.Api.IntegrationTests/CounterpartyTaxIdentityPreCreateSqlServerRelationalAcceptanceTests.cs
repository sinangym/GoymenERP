using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Pre-create AT-009 warning only. Reuses native creation/tax fixtures; never creates or links a card in a probe.</summary>
public sealed partial class CounterpartyTaxIdentitySqlServerRelationalAcceptanceTests
{
    private const string PreCreateDuplicatesPath = "/api/v1/counterparties/tax-identity/duplicates";

    [SqlServerFact]
    public async Task Pre_create_duplicate_probe_is_normalized_company_fenced_bounded_and_does_not_create_a_card()
    {
        await using var test = new TaxTest();
        using var maker = test.Client();
        var emptyFootprint = await PreCreateMutationFootprintAsync(test);
        using (var empty = await maker.PostAsJsonAsync(PreCreateDuplicatesPath, new { taxIdentityNumber = Vkn }))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.Empty((await JsonAsync(empty)).GetProperty("duplicates").EnumerateArray());
        }
        Assert.Equal(emptyFootprint, await PreCreateMutationFootprintAsync(test));

        var companyParties = new HashSet<Guid>();
        for (var index = 0; index < 21; index++)
        {
            var party = await CreateAsync(maker, $"SENTETIK şirket adayı {index}");
            await EnterAsync(maker, party, Vkn);
            companyParties.Add(party);
        }
        using var foreignMaker = test.Client(TestCompanyIds.CompanyB);
        var foreignParty = await CreateAsync(foreignMaker, "SENTETIK yabancı şirket adayı");
        await EnterAsync(foreignMaker, foreignParty, Vkn);
        var before = await PreCreateMutationFootprintAsync(test);
        var auditsBefore = await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents];", Guid.Empty);

        using var response = await maker.PostAsJsonAsync(PreCreateDuplicatesPath, new { taxIdentityNumber = $" {Vkn} " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("private", response.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl.NoStore);
        var result = await JsonAsync(response);
        Assert.Equal(Vkn, result.GetProperty("taxIdentityNumber").GetString());
        Assert.Equal("vkn", result.GetProperty("kind").GetString());
        var candidates = result.GetProperty("duplicates").EnumerateArray().ToArray();
        Assert.Equal(20, candidates.Length);
        Assert.All(candidates, candidate => Assert.Contains(candidate.GetProperty("partyId").GetGuid(), companyParties));
        Assert.DoesNotContain(candidates, candidate => candidate.GetProperty("partyId").GetGuid() == foreignParty);
        Assert.Equal(before, await PreCreateMutationFootprintAsync(test));
        Assert.Equal(auditsBefore, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents];", Guid.Empty));
    }

    [SqlServerFact]
    public async Task Pre_create_duplicate_probe_refuses_invalid_query_extra_fields_and_unacknowledged_clients_without_mutation()
    {
        await using var test = new TaxTest();
        using var maker = test.Client();
        var before = await PreCreateMutationFootprintAsync(test);
        foreach (var number in new[] { "1234567891", "123 4567890", "１２３４５６７８９０", "" })
        {
            using var invalid = await maker.PostAsJsonAsync(PreCreateDuplicatesPath, new { taxIdentityNumber = number });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("tax_identity_invalid", (await JsonAsync(invalid)).GetProperty("code").GetString());
        }
        using (var query = await maker.PostAsJsonAsync($"{PreCreateDuplicatesPath}?candidate=forbidden", new { taxIdentityNumber = Vkn }))
            Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        using (var company = await maker.PostAsJsonAsync(PreCreateDuplicatesPath, new { taxIdentityNumber = Vkn, companyId = TestCompanyIds.CompanyB }))
            Assert.Equal(HttpStatusCode.BadRequest, company.StatusCode);
        using (var actor = await maker.PostAsJsonAsync(PreCreateDuplicatesPath, new { taxIdentityNumber = Vkn, actorId = "forbidden" }))
            Assert.Equal(HttpStatusCode.BadRequest, actor.StatusCode);
        maker.DefaultRequestHeaders.Remove("X-ERP-Counterparty-Contract");
        using (var oldClient = await maker.PostAsJsonAsync(PreCreateDuplicatesPath, new { taxIdentityNumber = Vkn }))
        {
            Assert.Equal(HttpStatusCode.Conflict, oldClient.StatusCode);
            Assert.Equal("client_contract_required", (await JsonAsync(oldClient)).GetProperty("code").GetString());
        }
        Assert.Equal(before, await PreCreateMutationFootprintAsync(test));
    }

    [SqlServerFact]
    public async Task Pre_create_duplicate_probe_requires_the_existing_tax_update_and_read_permissions()
    {
        await using var test = new TaxTest(grant: false);
        using var creatorWithoutTaxUpdate = test.Client();
        using var viewerWithoutRead = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyAViewer);
        var before = await PreCreateMutationFootprintAsync(test);
        foreach (var client in new[] { creatorWithoutTaxUpdate, viewerWithoutRead })
        {
            using var refused = await client.PostAsJsonAsync(PreCreateDuplicatesPath, new { taxIdentityNumber = Vkn });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.DoesNotContain(Vkn, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        Assert.Equal(before, await PreCreateMutationFootprintAsync(test));
    }

    [SqlServerFact]
    public async Task Pre_create_duplicate_probe_suppresses_candidates_when_authority_is_lost_after_the_SQL_lookup()
    {
        var authority = new RevocableAuthority();
        var interleave = new DuplicateReadRevocationInterceptor(authority);
        await using var test = new TaxTest(freshAuthority: authority, interceptors: [interleave]);
        using var maker = test.Client();
        var candidate = await CreateAsync(maker, "SENTETIK gizli aday");
        await EnterAsync(maker, candidate, Vkn);
        var before = await PreCreateMutationFootprintAsync(test);
        interleave.Arm();
        using var refused = await maker.PostAsJsonAsync(PreCreateDuplicatesPath, new { taxIdentityNumber = Vkn });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.True(interleave.Triggered);
        Assert.True(authority.DeniedMeasurements > 0);
        var body = await refused.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Vkn, body, StringComparison.Ordinal);
        Assert.DoesNotContain(candidate.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK gizli aday", body, StringComparison.Ordinal);
        Assert.Equal(before, await PreCreateMutationFootprintAsync(test));
    }

    [SqlServerFact]
    public async Task Pre_create_owner_read_fences_actor_company_membership_and_both_permissions_after_materialization()
    {
        await using var test = new TaxTest();
        using var maker = test.Client();
        var candidate = await CreateAsync(maker, "SENTETIK owner aday");
        await EnterAsync(maker, candidate, Vkn);
        var user = new PreCreateProbeUser();
        var company = new PreCreateProbeCompany();
        var authority = new PreCreateProbeAuthority();
        var reads = new PreCreateDuplicateReadObserver();
        await using var factory = test.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>(); services.AddSingleton<ICurrentUser>(user);
            services.RemoveAll<ICompanyContext>(); services.AddSingleton<ICompanyContext>(company);
            services.RemoveAll<IErpAuthorizationService>(); services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>(); services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.AddDbContext<CounterpartyDbContext>((_, options) => options.AddInterceptors(reads));
        }));
        var before = await PreCreateMutationFootprintAsync(test);
        foreach (var fault in new[] { "read", "update", "read-scope", "update-scope", "actor", "company", "membership", "authentication" })
        {
            user.Reset(); company.Set(TestCompanyIds.CompanyA); authority.Reset();
            authority.BeforeFresh = () =>
            {
                if (fault == "read") authority.DeniedPermission = ErpPermissions.CounterpartyPartiesRead;
                if (fault == "update") authority.DeniedPermission = CounterpartyTaxIdentityPermissionsV1.Update;
                if (fault == "read-scope") authority.NarrowPermission = ErpPermissions.CounterpartyPartiesRead;
                if (fault == "update-scope") authority.NarrowPermission = CounterpartyTaxIdentityPermissionsV1.Update;
                if (fault == "actor") user.UserId = "SENTETIK different reader";
                if (fault == "company") company.Set(TestCompanyIds.CompanyB); // Controlled TEST double, not immutable CompanyContext.Set.
                if (fault == "membership") user.Access = false;
                if (fault == "authentication") user.Authenticated = false;
            };
            var readsBefore = reads.LookupCount;
            await using var scope = factory.Services.CreateAsyncScope();
            var owner = scope.ServiceProvider.GetRequiredService<ICounterpartyTaxIdentityService>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.CheckCreateTaxIdentityDuplicatesAsync(Vkn, default));
            Assert.Equal(readsBefore + 1, reads.LookupCount);
            Assert.Empty(scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>().ChangeTracker.Entries());
            Assert.Equal(before, await PreCreateMutationFootprintAsync(test));
        }
    }

    private static Task<string> PreCreateMutationFootprintAsync(TaxTest test) => test.ScalarAsync<string>("""
        SELECT CONCAT(
            (SELECT COUNT(*) FROM [counterparty].[Parties]), N'/',
            (SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts]), N'/',
            (SELECT COUNT(*) FROM [counterparty].[PartyIdentityRevisions]), N'/',
            (SELECT COUNT(*) FROM [counterparty].[PartyTaxIdentityChangeRequests]), N'/',
            (SELECT COUNT(*) FROM [idempotency].[CommandExecutions]));
        """, Guid.Empty);

    private sealed class PreCreateProbeUser : ICurrentUser
    {
        public string? UserId { get; set; } = "SENTETIK pre-create reader";
        public bool Access = true, Authenticated = true;
        public bool IsAuthenticated => Authenticated;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA, TestCompanyIds.CompanyB]);
        public bool CanAccessCompany(Guid companyId) => Access && AllowedCompanyIds.Contains(companyId);
        public void Reset() { UserId = "SENTETIK pre-create reader"; Access = true; Authenticated = true; }
    }
    private sealed class PreCreateProbeCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = TestCompanyIds.CompanyA;
        public void Set(Guid companyId) => CompanyId = companyId;
    }
    private sealed class PreCreateProbeAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public Action? BeforeFresh;
        public string? DeniedPermission, NarrowPermission;
        private PermissionScope Scope(string permission) => new(permission != DeniedPermission, permission != NarrowPermission,
            new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(permission != DeniedPermission);
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission));
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { BeforeFresh?.Invoke(); return Task.FromResult(Scope(permission)); }
        public void Reset() { BeforeFresh = null; DeniedPermission = null; NarrowPermission = null; }
    }
    private sealed class PreCreateDuplicateReadObserver : DbCommandInterceptor
    {
        public int LookupCount;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[counterparty].[CompanyPartyAccounts]", StringComparison.Ordinal)
                && command.CommandText.Contains("INNER JOIN", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("[TaxIdentityNumber]", StringComparison.Ordinal)) LookupCount++;
            return ValueTask.FromResult(result);
        }
    }
}
