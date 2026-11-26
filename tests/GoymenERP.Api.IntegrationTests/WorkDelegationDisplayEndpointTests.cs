using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real HTTP/company middleware; synthetic owner and Identity port. Not SQL acceptance.</summary>
public sealed class WorkDelegationDisplayEndpointTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/work/delegations";
    private static Guid Id(int value) => Guid.Parse($"d4100000-0000-4000-8000-{value:000000000000}");
    private static WorkDelegationViewV1 Row(int value) => new(Id(value), TestCompanyIds.CompanyA, "sales.order.v1", "sales.orders.approve",
        Id(value + 1000), Id(value + 2000), DateTimeOffset.Parse("2026-11-27T08:00:00Z"), DateTimeOffset.Parse("2026-11-28T08:00:00Z"),
        "1", Id(value + 3000), DateTimeOffset.Parse("2026-11-27T07:00:00Z"), "SENTETIK", null, null, null);

    [Fact]
    public async Task Authorized_page_resolves_only_its_actor_ids_in_bounded_batches_and_keeps_pagination()
    {
        var owner = new Owner(Enumerable.Range(1, 200).Select(Row).ToArray());
        var directory = new Directory(); using var host = Host(owner, directory); using var client = Client(host);
        using var response = await client.GetAsync(Path + "?all=true&page=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WorkDelegationDisplayTableV1>();
        Assert.NotNull(result); Assert.Equal(3, result.Page); Assert.True(result.HasMore); Assert.Equal(200, result.Items.Count);
        Assert.Equal(400, result.People.Count); Assert.Equal(4, directory.Requests.Count);
        Assert.All(directory.Requests, request =>
        {
            Assert.Equal(TestCompanyIds.CompanyA, request.CompanyId);
            Assert.False(string.IsNullOrWhiteSpace(request.ActorId));
            Assert.InRange(request.UserIds.Count, 1, UserDirectoryLimitsV1.MaximumResolveCount);
        });
        Assert.DoesNotContain(result.People, person => person.UserId == Id(9999)); // An overbroad directory reply cannot disclose another id.
        Assert.Equal(2, owner.Calls); // Owner authority is remeasured after resolving names.
        Assert.True(response.Headers.CacheControl!.Private); Assert.True(response.Headers.CacheControl.NoStore);
    }

    [Theory]
    [InlineData(UserDirectoryOutcomeV1.Denied)]
    [InlineData(UserDirectoryOutcomeV1.Unavailable)]
    public async Task Unavailable_or_denied_names_preserve_authorized_records_without_fabricating_names(UserDirectoryOutcomeV1 outcome)
    {
        var owner = new Owner([Row(1)]); var directory = new Directory { Outcome = outcome };
        using var host = Host(owner, directory); using var client = Client(host);
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WorkDelegationDisplayTableV1>();
        Assert.NotNull(result); Assert.Single(result.Items); Assert.Empty(result.People);
    }

    [Fact]
    public async Task Owner_authority_loss_after_directory_read_returns_no_name_or_record()
    {
        var owner = new Owner([Row(1)]) { DenySecondRead = true }; var directory = new Directory();
        using var host = Host(owner, directory); using var client = Client(host);
        using var response = await client.GetAsync(Path + "?all=true&page=0");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("SENTETIK person", await response.Content.ReadAsStringAsync());
        Assert.Single(directory.Requests);
    }

    [Fact]
    public async Task A_freshly_changed_owner_page_never_carries_the_previous_pages_names()
    {
        var owner = new Owner([Row(1)]) { LaterRows = [Row(2)] }; var directory = new Directory();
        using var host = Host(owner, directory); using var client = Client(host);
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WorkDelegationDisplayTableV1>();
        Assert.NotNull(result); Assert.Equal(Row(2).Id, Assert.Single(result.Items).Id); Assert.Empty(result.People);
    }

    [Fact]
    public async Task Foreign_company_is_stopped_before_owner_and_directory()
    {
        var owner = new Owner([Row(1)]); var directory = new Directory();
        using var host = Host(owner, directory); using var client = Client(host);
        client.DefaultRequestHeaders.Remove("X-Company-Id"); client.DefaultRequestHeaders.Add("X-Company-Id", Id(8888).ToString("D"));
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(0, owner.Calls); Assert.Empty(directory.Requests);
    }

    private WebApplicationFactory<Program> Host(Owner owner, Directory directory) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IWorkDelegationServiceV1>(); services.AddSingleton<IWorkDelegationServiceV1>(owner);
        services.RemoveAll<IUserDirectoryReadPortV1>(); services.AddSingleton<IUserDirectoryReadPortV1>(directory);
    }));
    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D")); return client;
    }
    private sealed class Owner(IReadOnlyList<WorkDelegationViewV1> rows) : IWorkDelegationServiceV1
    {
        public int Calls { get; private set; }
        public bool DenySecondRead { get; init; }
        public IReadOnlyList<WorkDelegationViewV1>? LaterRows { get; init; }
        public Task<WorkDelegationTableV1> ListAsync(bool all = false, int page = 0, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (DenySecondRead && Calls == 2) throw new WorkDelegationException(403, "forbidden");
            return Task.FromResult(new WorkDelegationTableV1(TestCompanyIds.CompanyA, Calls > 1 ? LaterRows ?? rows : rows, page, true));
        }
        public Task<WorkDelegationCommandResultV1> CreateAsync(CreateWorkDelegationV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WorkDelegationCommandResultV1> RevokeAsync(Guid id, RevokeWorkDelegationV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
    private sealed class Directory : IUserDirectoryReadPortV1
    {
        public List<UserDirectoryResolveRequestV1> Requests { get; } = [];
        public UserDirectoryOutcomeV1 Outcome { get; init; } = UserDirectoryOutcomeV1.Found;
        public Task<UserDirectoryResolveAnswerV1> ResolveAsync(UserDirectoryResolveRequestV1 request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new UserDirectoryResolveAnswerV1(Outcome, Outcome == UserDirectoryOutcomeV1.Found
                ? request.UserIds.Select(id => new UserDirectoryEntryV1(id, $"SENTETIK person {id}", false))
                    .Append(new(Id(9999), "SENTETIK foreign", true)).ToArray() : []));
        }
        public Task<UserDirectorySearchAnswerV1> SearchActiveAsync(UserDirectorySearchRequestV1 request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
