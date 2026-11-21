using System.Security.Claims;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The company lifecycle is never a foreign-company discovery channel. These tests invoke the real middleware and
/// HttpCurrentUser without starting a host or a database; only the owner reads and denial recorder are substituted.
/// </summary>
public sealed class CompanyLifecycleScopeFenceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData(CompanyLifecycleStateV1.SetupPending, false)]
    [InlineData(CompanyLifecycleStateV1.Active, false)]
    [InlineData(CompanyLifecycleStateV1.Active, true)]
    public async Task A_foreign_company_is_always_403_without_reading_its_pending_active_or_unavailable_lifecycle(
        CompanyLifecycleStateV1 state, bool ownerThrows)
    {
        using var request = new RequestFixture(canAccessCompany: false);
        request.Lifecycle.Read = (_, _) => ownerThrows
            ? Task.FromException<CompanyLifecycleViewV1?>(new InvalidOperationException("Owner unavailable."))
            : Task.FromResult<CompanyLifecycleViewV1?>(View(state));

        await request.InvokeAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, request.Http.Response.StatusCode);
        Assert.Equal(0, request.Lifecycle.Calls);
        Assert.Equal(0, request.Setup.AuthorityCalls);
        Assert.Equal(0, request.NextCalls);
        Assert.False(request.CompanyContext.IsResolved);
        AssertMismatchDenial(request);
        using var problem = request.ReadProblem();
        Assert.Equal("forbidden", problem.RootElement.GetProperty("code").GetString());
        Assert.False(problem.RootElement.TryGetProperty("companyId", out _));
        Assert.False(problem.RootElement.TryGetProperty("state", out _));
    }

    [Fact]
    public async Task A_first_admin_request_without_membership_uses_only_the_fresh_group_operation()
    {
        using var request = new RequestFixture(canAccessCompany: false, firstAdminEndpoint: true);
        request.Setup.IsGranted = _ => true;
        request.Lifecycle.Read = (_, _) => Task.FromResult<CompanyLifecycleViewV1?>(View(CompanyLifecycleStateV1.SetupPending));

        await request.InvokeAsync();

        Assert.Equal(StatusCodes.Status200OK, request.Http.Response.StatusCode);
        Assert.Equal(new[] { "group-authority", "lifecycle", "group-authority", "next" }, request.Calls);
        Assert.Equal(2, request.Setup.AuthorityCalls);
        Assert.Equal(1, request.NextCalls);
        Assert.True(request.NextSawResolvedCompany);
        Assert.Equal(Company, request.CompanyContext.CompanyId);
        Assert.False(request.User.CanAccessCompany(Company));
        Assert.Empty(request.Denials.Entries);
    }

    [Fact]
    public async Task An_active_company_still_allows_only_the_explicit_group_first_admin_operation_for_replay()
    {
        using var request = new RequestFixture(canAccessCompany: false, firstAdminEndpoint: true);
        request.Setup.IsGranted = _ => true;
        await request.InvokeAsync();
        Assert.Equal(StatusCodes.Status200OK, request.Http.Response.StatusCode);
        Assert.Equal(new[] { "group-authority", "lifecycle", "group-authority", "next" }, request.Calls);
        Assert.False(request.User.CanAccessCompany(Company));
        Assert.True(request.NextSawResolvedCompany);
    }

    [Fact]
    public async Task First_admin_metadata_without_group_authority_never_reads_a_foreign_company()
    {
        using var request = new RequestFixture(canAccessCompany: false, firstAdminEndpoint: true);

        await request.InvokeAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, request.Http.Response.StatusCode);
        Assert.Equal(1, request.Setup.AuthorityCalls);
        Assert.Equal(0, request.Lifecycle.Calls);
        Assert.Equal(0, request.NextCalls);
        Assert.False(request.CompanyContext.IsResolved);
        AssertMismatchDenial(request);
    }

    [Fact]
    public async Task Group_authority_lost_during_the_lifecycle_read_cannot_use_the_first_admin_exception()
    {
        using var request = new RequestFixture(canAccessCompany: false, firstAdminEndpoint: true);
        request.Setup.IsGranted = call => call == 1;
        request.Lifecycle.Read = (_, _) => Task.FromResult<CompanyLifecycleViewV1?>(View(CompanyLifecycleStateV1.SetupPending));

        await request.InvokeAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, request.Http.Response.StatusCode);
        Assert.Equal(new[] { "group-authority", "lifecycle", "group-authority" }, request.Calls);
        Assert.Equal(0, request.NextCalls);
        Assert.False(request.CompanyContext.IsResolved);
        using var problem = request.ReadProblem();
        Assert.Equal("forbidden", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_authorized_company_with_an_unavailable_lifecycle_still_answers_503_without_continuing()
    {
        using var request = new RequestFixture(canAccessCompany: true);
        request.Lifecycle.Read = (_, _) => Task.FromException<CompanyLifecycleViewV1?>(
            new InvalidOperationException("Owner unavailable."));

        await request.InvokeAsync();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, request.Http.Response.StatusCode);
        Assert.Equal(1, request.Lifecycle.Calls);
        Assert.Equal(0, request.Setup.AuthorityCalls);
        Assert.Equal(0, request.NextCalls);
        Assert.False(request.CompanyContext.IsResolved);
        Assert.Empty(request.Denials.Entries);
        using var problem = request.ReadProblem();
        Assert.Equal("service_unavailable", problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("detail").GetString()));
        Assert.DoesNotContain("Owner unavailable.", problem.RootElement.GetRawText());
    }

    [Fact]
    public async Task Company_membership_lost_while_awaiting_the_active_lifecycle_is_refused_by_the_final_fence()
    {
        using var request = new RequestFixture(canAccessCompany: true);
        request.Lifecycle.Read = async (_, _) =>
        {
            await Task.Yield();
            var identity = Assert.IsType<ClaimsIdentity>(request.Http.User.Identity);
            identity.RemoveClaim(Assert.Single(identity.FindAll(GoymenClaimTypes.CompanyAccess)));
            return View(CompanyLifecycleStateV1.Active);
        };

        await request.InvokeAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, request.Http.Response.StatusCode);
        Assert.Equal(1, request.Lifecycle.Calls);
        Assert.Equal(0, request.NextCalls);
        Assert.False(request.CompanyContext.IsResolved);
        AssertMismatchDenial(request);
    }

    private static CompanyLifecycleViewV1 View(CompanyLifecycleStateV1 state) => new(Company, state, true, 1);

    private static void AssertMismatchDenial(RequestFixture request)
    {
        var denial = Assert.Single(request.Denials.Entries);
        Assert.Equal(AuthorizationDenialReasonCodesV1.CompanyContextMismatch, denial.ReasonCode);
        Assert.Equal(Company, denial.RequestedCompanyId);
        Assert.Null(denial.PermissionCode);
    }

    private sealed class RequestFixture : IDisposable
    {
        private readonly ServiceProvider services;
        private readonly CompanyContextMiddleware middleware;
        public List<string> Calls { get; } = [];
        public DefaultHttpContext Http { get; }
        public HttpCurrentUser User { get; }
        public CompanyContext CompanyContext { get; } = new();
        public LifecycleReader Lifecycle { get; }
        public CompanySetupService Setup { get; }
        public DenialRecorder Denials { get; } = new();
        public int NextCalls { get; private set; }
        public bool NextSawResolvedCompany { get; private set; }

        public RequestFixture(bool canAccessCompany, bool firstAdminEndpoint = false)
        {
            var registrations = new ServiceCollection();
            registrations.AddLogging();
            registrations.AddErpProblemDetails();
            registrations.AddSingleton<IAuthorizationDenialRecorderV1>(Denials);
            services = registrations.BuildServiceProvider();
            var identity = new ClaimsIdentity([new Claim(GoymenClaimTypes.UserId, Actor.ToString("D"))], "synthetic");
            if (canAccessCompany) identity.AddClaim(new Claim(GoymenClaimTypes.CompanyAccess, Company.ToString("D")));
            Http = new DefaultHttpContext { RequestServices = services, User = new ClaimsPrincipal(identity) };
            Http.Request.Headers[CompanyContextMiddleware.CompanyHeaderName] = Company.ToString("D");
            Http.Response.Body = new MemoryStream();
            var metadata = new List<object> { new RequiresCompanyContextAttribute() };
            if (firstAdminEndpoint) metadata.Add(new FirstCompanyAdminEndpointMetadata());
            Http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "scope-fence"));
            User = new HttpCurrentUser(new HttpContextAccessor { HttpContext = Http });
            Lifecycle = new LifecycleReader(Calls);
            Setup = new CompanySetupService(Calls);
            middleware = new CompanyContextMiddleware(_ =>
            {
                Calls.Add("next");
                NextCalls++;
                NextSawResolvedCompany = CompanyContext.IsResolved && CompanyContext.CompanyId == Company;
                return Task.CompletedTask;
            });
        }

        public Task InvokeAsync() => middleware.InvokeAsync(Http, User, CompanyContext, Lifecycle, Setup);
        public JsonDocument ReadProblem() => JsonDocument.Parse(((MemoryStream)Http.Response.Body).ToArray());
        public void Dispose() { Http.Response.Body.Dispose(); services.Dispose(); }
    }

    private sealed class LifecycleReader(List<string> calls) : ICompanyLifecycleReadPortV1
    {
        public int Calls { get; private set; }
        public Func<Guid, CancellationToken, Task<CompanyLifecycleViewV1?>> Read { get; set; } =
            (_, _) => Task.FromResult<CompanyLifecycleViewV1?>(View(CompanyLifecycleStateV1.Active));
        public Task<CompanyLifecycleViewV1?> ReadAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++; calls.Add("lifecycle");
            return Read(companyId, cancellationToken);
        }
    }

    private sealed class CompanySetupService(List<string> calls) : ICompanySetupGroupAuthorityV1
    {
        public int AuthorityCalls { get; private set; }
        public Func<int, bool> IsGranted { get; set; } = _ => false;
        public Task<CompanySetupGroupAuthoritySnapshotV1> ReadAsync(CompanySetupGroupOperationV1 operation,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(CompanySetupGroupOperationV1.AssignFirstAdmin, operation);
            AuthorityCalls++; calls.Add("group-authority");
            return Task.FromResult(IsGranted(AuthorityCalls)
                ? new CompanySetupGroupAuthoritySnapshotV1(true, operation, Actor, "SENTETIK-stable-authority")
                : CompanySetupGroupAuthoritySnapshotV1.Denied(operation));
        }
    }

    private sealed class DenialRecorder : IAuthorizationDenialRecorderV1
    {
        public List<AuthorizationDenialV1> Entries { get; } = [];
        public bool HasAttempted => Entries.Count != 0;
        public Task<SecurityEventWriteResultV1> RecordAsync(AuthorizationDenialV1 denial,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(CancellationToken.None, cancellationToken);
            Entries.Add(denial);
            return Task.FromResult(SecurityEventWriteResultV1.Written(SecurityEventWriteFailureMode.FailOpenWithAlarm));
        }
    }
}
