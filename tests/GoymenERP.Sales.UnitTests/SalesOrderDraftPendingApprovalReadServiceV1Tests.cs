using System.Reflection;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesOrderDraftPendingApprovalReadServiceV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid First = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid OwnAllowed = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid OwnRestricted = Guid.Parse("a1000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task List_hydrates_exact_pending_details_and_excludes_only_owner_restricted_items()
    {
        var drafts = Drafts();
        drafts.List = (query, _) =>
        {
            Assert.Equal((50, "owner-cursor", "PendingApproval"),
                (query.PageSize, query.Cursor, query.ApprovalState));
            return Task.FromResult(new SalesOrderDraftListResponse(
                [Summary(First), Summary(OwnAllowed), Summary(OwnRestricted)], "next-owner-cursor"));
        };
        drafts.Detail = (id, _) => Task.FromResult(id switch
        {
            var value when value == First => Detail(value, isOwn: false, restricted: false),
            var value when value == OwnAllowed => Detail(value, isOwn: true, restricted: false),
            _ => Detail(id, isOwn: true, restricted: true),
        });
        var fixture = Fixture(drafts);

        var result = await fixture.Service.ListAsync(Context(), "owner-cursor", default);

        Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Found, result.Outcome);
        Assert.Equal((Company, "next-owner-cursor"), (result.Resource!.CompanyId, result.Resource.NextCursor));
        Assert.Equal([First, OwnAllowed], result.Resource.Items.Select(item => item.DraftId));
        Assert.False(result.Resource.Items[0].IsOwnDraft);
        Assert.True(result.Resource.Items[1].IsOwnDraft);
        Assert.All(result.Resource.Items, item =>
        {
            Assert.Equal((Company, "3", "Rev.0", "submitter"),
                (item.CompanyId, item.Version, item.Revision, item.SubmittedBy));
        });
        Assert.Equal(16, fixture.Authority.Asked.Count);
        Assert.All(fixture.Authority.Asked.Chunk(2), pair => Assert.Equal(
            [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove],
            pair.Select(item => item.Permission)));
    }

    [Fact]
    public async Task Missing_approve_authority_denies_before_the_owner_and_registration_is_scoped()
    {
        var drafts = Drafts();
        var fixture = Fixture(drafts, permission => permission == SalesPermissionManifestV1.DraftsRead);

        var result = await fixture.Service.ListAsync(Context(), null, default);

        Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(0, drafts.ListCalls);
        Assert.Equal(0, drafts.DetailCalls);

        var services = new ServiceCollection();
        services.AddSalesModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=(local);Database=SENTETIK;Trusted_Connection=True;",
        }).Build());
        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ISalesOrderDraftPendingApprovalReadServiceV1));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(SalesOrderDraftPendingApprovalReadServiceV1), registration.ImplementationType);
    }

    [Fact]
    public async Task Malformed_or_drifting_owner_answers_fail_closed()
    {
        var malformed = Drafts();
        malformed.List = (_, _) => Task.FromResult(new SalesOrderDraftListResponse([Summary(First)], null));
        malformed.Detail = (_, _) => Task.FromResult(Detail(First) with
        {
            Draft = Summary(First) with { Version = "4" },
        });
        var malformedResult = await Fixture(malformed).Service.ListAsync(Context(), null, default);
        Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Unavailable, malformedResult.Outcome);

        var drifting = Drafts();
        var driftFixture = Fixture(drifting);
        drifting.List = (_, _) =>
        {
            driftFixture.Company.Set(Guid.NewGuid());
            return Task.FromResult(new SalesOrderDraftListResponse([], null));
        };
        var driftResult = await driftFixture.Service.ListAsync(Context(), null, default);
        Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Denied, driftResult.Outcome);
    }

    [Fact]
    public async Task Whitespace_or_oversized_owner_cursor_fails_closed()
    {
        foreach (var cursor in new[] { " ", new string('x', 8193) })
        {
            var drafts = Drafts();
            drafts.List = (_, _) => Task.FromResult(new SalesOrderDraftListResponse([], cursor));

            var result = await Fixture(drafts).Service.ListAsync(Context(), null, default);

            Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Unavailable, result.Outcome);
            Assert.Null(result.Resource);
        }
    }

    [Fact]
    public async Task Detail_preserves_non_support_creator_and_hides_only_restricted_creator()
    {
        var drafts = Drafts();
        drafts.Detail = (id, _) => Task.FromResult(
            Detail(id, isOwn: true, restricted: id == OwnRestricted));
        var service = Fixture(drafts).Service;

        var allowed = await service.GetAsync(Context(), OwnAllowed, default);
        var restricted = await service.GetAsync(Context(), OwnRestricted, default);

        Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Found, allowed.Outcome);
        Assert.True(allowed.Resource!.Item.IsOwnDraft);
        Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.NotFound, restricted.Outcome);
        Assert.Null(restricted.Resource);
    }

    private static FixtureState Fixture(DraftProxy drafts, Func<string, bool>? granted = null)
    {
        var company = new MutableCompanyContext(Company);
        var user = new User("approver", Company);
        var authority = new Authority(granted ?? (_ => true));
        return new(new SalesOrderDraftPendingApprovalReadServiceV1(
            drafts.Service, company, user, authority), company, authority);
    }

    private static SalesOrderDraftPendingApprovalReadContextV1 Context() => new(Company, "approver");

    private static SalesOrderDraftSummaryView Summary(Guid id) => new(
        id, Company, $"SO-{id.ToString("N")[^4..]}", "Order", "Draft", "3", "Rev.0",
        Guid.Parse("b1000000-0000-0000-0000-000000000001"),
        Guid.Parse("b2000000-0000-0000-0000-000000000001"),
        Guid.Parse("b3000000-0000-0000-0000-000000000001"),
        new DateOnly(2026, 10, 9), DateTimeOffset.Parse("2026-10-09T08:00:00Z"), 1,
        "PendingApproval");

    private static SalesOrderDraftDetailView Detail(
        Guid id,
        bool isOwn = false,
        bool restricted = false) => new(
        Summary(id), null, null, [], null, null, null, null, null, null,
        new SalesDraftApprovalV1("submitter", DateTimeOffset.Parse("2026-10-09T09:00:00Z"), null, null, null),
        new SalesDraftCustomerAcceptanceV1(false, null, null, null, false),
        isOwn, restricted);

    private static DraftProxy Drafts()
    {
        var service = DispatchProxy.Create<ISalesDraftService, DraftProxy>();
        return (DraftProxy)(object)service;
    }

    private sealed record FixtureState(
        SalesOrderDraftPendingApprovalReadServiceV1 Service,
        MutableCompanyContext Company,
        Authority Authority);

    public class DraftProxy : DispatchProxy
    {
        public ISalesDraftService Service => (ISalesDraftService)(object)this;
        public Func<SalesOrderDraftListQuery, CancellationToken, Task<SalesOrderDraftListResponse>> List { get; set; } =
            (_, _) => Task.FromResult(new SalesOrderDraftListResponse([], null));
        public Func<Guid, CancellationToken, Task<SalesOrderDraftDetailView>> Detail { get; set; } =
            (id, _) => Task.FromResult(SalesOrderDraftPendingApprovalReadServiceV1Tests.Detail(id));
        public int ListCalls { get; private set; }
        public int DetailCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ISalesDraftService.GetDraftListAsync))
            {
                ListCalls++;
                return List((SalesOrderDraftListQuery)args![0]!, (CancellationToken)args[1]!);
            }
            if (targetMethod?.Name == nameof(ISalesDraftService.GetDraftDetailAsync))
            {
                DetailCalls++;
                return Detail((Guid)args![0]!, (CancellationToken)args[1]!);
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User(string actorId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority(Func<string, bool> granted) : IErpAuthorityRevalidationService
    {
        public List<(string Permission, Guid CompanyId)> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((permission, companyId));
            return Task.FromResult(granted(permission)
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(),
                    new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }
}
