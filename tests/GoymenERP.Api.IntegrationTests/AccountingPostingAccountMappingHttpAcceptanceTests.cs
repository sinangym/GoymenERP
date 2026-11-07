using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-04a HTTP boundary of the posting account mapping catalog over the real host, with the store and command log
/// replaced by in-memory fakes (the real store is proven over SQL Server in
/// <see cref="AccountingPostingAccountMappingSqlServerRelationalAcceptanceTests"/>). The route needs its own narrow
/// permission, a strict JSON body and an idempotency key; 401/403/503 carry no resource; replays carry the headers.
/// </summary>
public sealed class AccountingPostingAccountMappingHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/accounting/posting-account-mappings";
    private static readonly Guid Account = Guid.Parse("d0400000-0000-4000-8000-000000000120");

    [Fact]
    public async Task Registration_grants_nothing_anonymous_is_401_and_an_ungranted_actor_is_403()
    {
        var store = new FakeStore();
        using var ungrantedHost = Host(store, granted: []);
        using var client = Client(ungrantedHost);
        using var prepare = await Send(client, HttpMethod.Post, Path, ValidBody(), "k-403");
        using var read = await client.GetAsync(Path + "/active");
        Assert.Equal(HttpStatusCode.Forbidden, prepare.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        await AssertNoResourceAsync(prepare);

        using var anonymous = ungrantedHost.CreateClient();
        using var unauthenticated = await anonymous.GetAsync(Path + "/active");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var prepareOnly = Host(store, granted: [AccountingPostingAccountMappingPermissionNamesV1.Prepare]);
        using var prepareClient = Client(prepareOnly);
        using var activate = await Send(prepareClient, HttpMethod.Post, Path + "/1/activation",
            """{"expectedVersion":1,"expectedActiveMappingVersion":0}""", "k-activate-403");
        Assert.Equal(HttpStatusCode.Forbidden, activate.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData("""{"expectedLatestMappingVersion":0,"effectiveFrom":"2026-11-01","rows":[],"extra":1}""")]
    [InlineData("""{"expectedLatestMappingVersion":0,"effectiveFrom":"2026-11-01","rows":[{"purpose":"sales-invoice","mappingKey":"receivable","itemType":null,"linePurpose":null,"isExport":null,"accountId":"d0400000-0000-4000-8000-000000000120","accountCode":"120"}]}""")]
    [InlineData("""{"expectedLatestMappingVersion":0,"effectiveFrom":"01.11.2026","rows":[]}""")]
    [InlineData("""not json""")]
    public async Task A_body_with_an_unknown_field_or_a_wrong_type_is_400_before_the_store(string body)
    {
        var store = new FakeStore();
        using var host = Host(store);
        using var client = Client(host);
        using var response = await Send(client, HttpMethod.Post, Path, body, "k-strict");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task A_missing_or_blank_idempotency_key_is_400_and_a_replay_carries_the_replay_headers()
    {
        var store = new FakeStore();
        using var host = Host(store);
        using var client = Client(host);
        using var missing = await Send(client, HttpMethod.Post, Path, ValidBody(), null);
        using var blank = await Send(client, HttpMethod.Post, Path, ValidBody(), "   ");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(0, store.Writes);

        using var first = await Send(client, HttpMethod.Post, Path, ValidBody(), "k-replay");
        using var replay = await Send(client, HttpMethod.Post, Path, ValidBody(), "k-replay");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("/api/v1/accounting/posting-account-mappings/1", first.Headers.Location?.OriginalString);
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.True(replay.Headers.Contains("Idempotency-Original-Correlation-Id"));
        Assert.Equal(1, store.Writes);
        using var body = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("mappingVersion").GetInt64());
        Assert.Equal("120.01", body.RootElement.GetProperty("rows")[0].GetProperty("accountCode").GetString());
    }

    [Fact]
    public async Task An_unknown_store_answer_is_503_without_a_resource_and_is_not_recorded()
    {
        var store = new FakeStore { Fail = true };
        using var host = Host(store);
        using var client = Client(host);
        using var response = await Send(client, HttpMethod.Post, Path, ValidBody(), "k-503");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertNoResourceAsync(response);
        Assert.Empty(store.Log);
    }

    [Fact]
    public async Task Payload_refusals_are_422_with_the_field_and_unknown_versions_are_code_less_404()
    {
        var store = new FakeStore();
        using var host = Host(store);
        using var client = Client(host);
        using var refused = await Send(client, HttpMethod.Post, Path,
            """{"expectedLatestMappingVersion":0,"effectiveFrom":"2026-11-01","rows":[{"purpose":"sales-invoice","mappingKey":"payable","itemType":null,"linePurpose":null,"isExport":null,"accountId":"d0400000-0000-4000-8000-000000000120"}]}""",
            "k-422");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            Assert.Equal(AccountingPostingAccountMappingCodesV1.KeyUnknown, problem.RootElement.GetProperty("code").GetString());
            Assert.Equal("rows[0].mappingKey", problem.RootElement.GetProperty("field").GetString());
        }

        using var unknown = await client.GetAsync(Path + "/9");
        using var notNumeric = await client.GetAsync(Path + "/abc");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notNumeric.StatusCode);
        using var notFound = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.False(notFound.RootElement.TryGetProperty("code", out _));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task The_preparer_activating_their_own_version_is_403_and_the_reads_are_private()
    {
        var store = new FakeStore();
        using var host = Host(store);
        using var client = Client(host);
        using (var prepared = await Send(client, HttpMethod.Post, Path, ValidBody(), "k-prepare"))
        {
            Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        }

        using var self = await Send(client, HttpMethod.Post, Path + "/1/activation",
            """{"expectedVersion":1,"expectedActiveMappingVersion":0}""", "k-self");
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        await AssertNoResourceAsync(self);

        using var read = await client.GetAsync(Path + "/1");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.Private);
        Assert.True(read.Headers.CacheControl?.NoStore);
        using var activeRead = await client.GetAsync(Path + "/active");
        Assert.Equal(HttpStatusCode.NotFound, activeRead.StatusCode);
    }

    private static async Task AssertNoResourceAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("mappingVersion", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rows", text, StringComparison.Ordinal);
    }

    private static string ValidBody() =>
        $$"""{"expectedLatestMappingVersion":0,"effectiveFrom":"2026-11-01","rows":[{"purpose":"sales-invoice","mappingKey":"receivable","itemType":null,"linePurpose":null,"isExport":null,"accountId":"{{Account:D}}","vatRateCode":null}]}""";

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string body, string? key)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private WebApplicationFactory<Program> Host(FakeStore store, string[]? granted = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccountingPostingAccountMappingStoreV1>();
            services.AddSingleton<IAccountingPostingAccountMappingStoreV1>(store);
            services.RemoveAll<IAccountingCommandLog>();
            services.AddSingleton<IAccountingCommandLog>(store);
            var authority = new GrantingAuthority(granted ?? AccountingPostingAccountMappingPermissionNamesV1.All.ToArray());
            services.RemoveAll<IErpAuthorizationService>();
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private sealed class FakeStore : IAccountingPostingAccountMappingStoreV1, IAccountingCommandLog
    {
        private readonly object gate = new();
        private AccountingPostingAccountMappingRecordV1? record;

        public bool Fail { get; init; }
        public int Writes { get; private set; }
        public List<AccountingCommandRecord> Log { get; } = [];

        public Task<AccountingPostingAccountMappingRecordV1?> ReadAsync(long mappingVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(record?.MappingVersion == mappingVersion ? record : null);

        public Task<AccountingPostingAccountMappingRecordV1?> ReadActiveAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(record?.Status == AccountingPostingAccountMappingStatusV1.Active ? record : null);

        public async Task<AccountingPostingAccountMappingRecordV1> PrepareAsync(long expectedLatestMappingVersion,
            DateOnly effectiveFrom, IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> rows, Guid preparedByUserId,
            DateTimeOffset preparedAtUtc, Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new AccountingStoreException("synthetic_unknown_store_answer", "Synthetic.");
            }

            await freshAuthorityFence(cancellationToken);
            Writes++;
            record = new AccountingPostingAccountMappingRecordV1(TestCompanyIds.CompanyA, 1, effectiveFrom,
                AccountingPostingAccountMappingStatusV1.Prepared, 1, "mikro-synthetic", new string('A', 64),
                preparedByUserId, preparedAtUtc, null, null,
                [.. rows.Select((row, index) => new AccountingPostingAccountMappingEntryV1(index + 1, row.Purpose,
                    row.MappingKey, row.ItemType, row.LinePurpose, row.IsExport, row.AccountId, "120.01",
                    row.VatRateCode))]);
            Add(metadata.Completion!(record));
            return record;
        }

        public Task<AccountingPostingAccountMappingRecordV1> ActivateAsync(long mappingVersion, long expectedVersion,
            long expectedActiveMappingVersion, Guid activatedByUserId, DateTimeOffset activatedAtUtc,
            Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            if (record is null || record.MappingVersion != mappingVersion)
            {
                throw new AccountingPostingAccountMappingRefusalException(AccountingPostingAccountMappingCodesV1.NotFound);
            }

            if (record.PreparedByUserId == activatedByUserId)
            {
                throw new AccountingPostingAccountMappingRefusalException(
                    AccountingPostingAccountMappingCodesV1.SelfActivation, "activation");
            }

            throw new NotSupportedException("Activation by another person is proven over SQL Server.");
        }

        public Task<AccountingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult(Log.SingleOrDefault(command => command.CommandName == commandName
                    && command.IdempotencyKeyHash == idempotencyKeyHash));
            }
        }

        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default)
        {
            Add(record);
            return Task.CompletedTask;
        }

        private void Add(AccountingCommandRecord command)
        {
            lock (gate)
            {
                Log.Add(command);
            }
        }
    }

    private sealed class GrantingAuthority(string[] granted) : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Allowed(permission, companyId));

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("b04a-http-only", new HashSet<Guid> { TestCompanyIds.CompanyA },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId),
                    StringComparer.Ordinal)));

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));

        private bool Allowed(string permission, Guid companyId) =>
            companyId == TestCompanyIds.CompanyA && granted.Contains(permission, StringComparer.Ordinal);

        private PermissionScope Scope(string permission, Guid companyId) =>
            new(Allowed(permission, companyId), true, new HashSet<string>(StringComparer.Ordinal) { "B04A-HTTP" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    }
}
