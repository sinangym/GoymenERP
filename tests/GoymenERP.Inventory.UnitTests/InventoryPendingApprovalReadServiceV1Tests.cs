using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryPendingApprovalReadServiceV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid CountId = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid ScrapId = Guid.Parse("72000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Eligible_count_and_scrap_are_returned_with_exact_owner_values()
    {
        var world = new World();
        var count = await world.Counts.ListAsync(world.Context(), null, default);
        var scrap = await world.Scraps.GetAsync(world.Context(), ScrapId, default);

        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Found, count.Outcome);
        Assert.Equal((CountId, "ETK-D05-COUNT"),
            (Assert.Single(count.Resource!.Items).CountId, count.Resource.Items[0].LabelCode));
        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Found, scrap.Outcome);
        Assert.Equal((ScrapId, "recorder", "6"),
            (scrap.Resource!.Item.ScrapId, scrap.Resource.Item.SubmittedBy, scrap.Resource.Item.ScrapKg));
        Assert.Equal(12, world.Authority.Measured.Count);
    }

    [Fact]
    public async Task Missing_approve_permission_is_denied_before_the_owner_read()
    {
        var world = new World();
        world.Authority.Granted.Remove(InventoryPermissionManifestV1.StockCountApprove);

        var result = await world.Counts.ListAsync(world.Context(), null, default);

        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(0, world.Reader.CountListCalls);
    }

    [Fact]
    public async Task Permission_revoked_during_owner_read_is_denied_after_the_await()
    {
        var world = new World();
        world.Reader.OnCountList = () =>
            world.Authority.Granted.Remove(InventoryPermissionManifestV1.StockCountApprove);

        var result = await world.Counts.ListAsync(world.Context(), null, default);

        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(1, world.Reader.CountListCalls);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task Company_or_actor_drift_during_owner_read_is_denied(string drift)
    {
        var world = new World();
        world.Reader.OnScrapList = () =>
        {
            if (drift == "company") world.Company.Set(Guid.NewGuid());
            if (drift == "actor") world.User.UserId = "other-approver";
        };

        var result = await world.Scraps.ListAsync(world.Context(), null, default);

        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Own_or_malformed_owner_rows_fail_closed()
    {
        var world = new World();
        world.Reader.Counts = [Count() with { SubmittedBy = world.User.UserId! }];

        var result = await world.Counts.ListAsync(world.Context(), null, default);

        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Missing_label_code_fails_the_whole_pending_page_closed()
    {
        var world = new World();
        world.Reader.Counts = [Count() with { LabelCode = "" }];

        var result = await world.Counts.ListAsync(world.Context(), null, default);

        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Missing_or_failed_owner_detail_does_not_become_a_positive_result()
    {
        var missing = new World();
        missing.Reader.Scrap = null;
        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.NotFound,
            (await missing.Scraps.GetAsync(missing.Context(), ScrapId, default)).Outcome);

        var failed = new World();
        failed.Reader.Throw = true;
        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.Unavailable,
            (await failed.Counts.GetAsync(failed.Context(), CountId, default)).Outcome);
    }

    [Fact]
    public async Task Keyset_cursor_does_not_skip_or_repeat_when_the_pending_queue_changes_between_pages()
    {
        var world = new World();
        var start = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        world.Reader.Counts = Enumerable.Range(1, 51)
            .Select(index => Count() with
            {
                CountId = Guid.Parse($"71000000-0000-0000-0000-{index:D12}"),
                SubmittedAtUtc = start.AddMinutes(-index),
            })
            .ToArray();

        var first = await world.Counts.ListAsync(world.Context(), null, default);
        Assert.Equal(50, first.Resource!.Items.Count);
        Assert.NotNull(first.Resource.NextCursor);
        var expectedRemaining = world.Reader.Counts.Single(item =>
            item.CountId == Guid.Parse("71000000-0000-0000-0000-000000000051"));

        world.Reader.Counts =
        [
            Count() with
            {
                CountId = Guid.Parse("71000000-0000-0000-0000-999999999999"),
                SubmittedAtUtc = start.AddMinutes(1),
            },
            .. world.Reader.Counts.Skip(10),
        ];
        var second = await world.Counts.ListAsync(world.Context(), first.Resource.NextCursor, default);

        Assert.Equal(expectedRemaining.CountId, Assert.Single(second.Resource!.Items).CountId);
        Assert.DoesNotContain(second.Resource.Items,
            item => first.Resource.Items.Any(firstItem => firstItem.CountId == item.CountId));
    }

    [Fact]
    public async Task Keyset_cursor_is_bound_to_company_kind_and_canonical_payload()
    {
        var world = new World();
        world.Reader.Counts = Enumerable.Range(1, 51)
            .Select(index => Count() with
            {
                CountId = Guid.Parse($"71000000-0000-0000-0000-{index:D12}"),
                SubmittedAtUtc = DateTimeOffset.Parse("2026-10-10T12:00:00Z").AddMinutes(-index),
            })
            .ToArray();
        var first = await world.Counts.ListAsync(world.Context(), null, default);
        var cursor = first.Resource!.NextCursor!;

        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.NotFound,
            (await world.Scraps.ListAsync(world.Context(), cursor, default)).Outcome);
        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.NotFound,
            (await world.Counts.ListAsync(world.Context(), "not-base64", default)).Outcome);

        world.Company.Set(CompanyB);
        world.User.Allowed.Add(CompanyB);
        Assert.Equal(InventoryPendingApprovalReadOutcomeV1.NotFound,
            (await world.Counts.ListAsync(world.Context(), cursor, default)).Outcome);
    }

    private sealed class World
    {
        public MutableCompany Company { get; } = new();
        public MutableUser User { get; } = new();
        public MutableAuthority Authority { get; } = new();
        public Reader Reader { get; } = new();
        public IInventoryStockCountPendingApprovalReadServiceV1 Counts { get; }
        public IInventoryStockScrapPendingApprovalReadServiceV1 Scraps { get; }

        public World()
        {
            var service = new InventoryPendingApprovalReadServiceV1(Reader, Company, User, Authority);
            Counts = service;
            Scraps = service;
        }

        public InventoryPendingApprovalReadContextV1 Context() => new(Company.CompanyId, User.UserId!);
    }

    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class MutableUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = "approver";
        public HashSet<Guid> Allowed { get; } = [Company];
        public IReadOnlySet<Guid> AllowedCompanyIds => Allowed;
        public bool CanAccessCompany(Guid companyId) => Allowed.Contains(companyId);
    }

    private sealed class MutableAuthority : IErpAuthorityRevalidationService
    {
        public HashSet<string> Granted { get; } =
        [
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
            InventoryPermissionManifestV1.StockCountApprove,
            InventoryPermissionManifestV1.StockScrapApprove,
        ];
        public List<string> Measured { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Measured.Add(permission);
            return Task.FromResult(new PermissionScope(
                Granted.Contains(permission), true, new HashSet<string>(),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class Reader : IInventoryPendingApprovalReaderV1
    {
        public IReadOnlyList<InventoryStockCountPendingApprovalItemV1> Counts { get; set; } =
            [InventoryPendingApprovalReadServiceV1Tests.Count()];
        public InventoryStockCountPendingApprovalItemV1? Count { get; set; } = InventoryPendingApprovalReadServiceV1Tests.Count();
        public IReadOnlyList<InventoryStockScrapPendingApprovalItemV1> Scraps { get; set; } =
            [InventoryPendingApprovalReadServiceV1Tests.Scrap()];
        public InventoryStockScrapPendingApprovalItemV1? Scrap { get; set; } = InventoryPendingApprovalReadServiceV1Tests.Scrap();
        public Action? OnCountList { get; set; }
        public Action? OnScrapList { get; set; }
        public bool Throw { get; set; }
        public int CountListCalls { get; private set; }

        public Task<IReadOnlyList<InventoryStockCountPendingApprovalItemV1>> ReadStockCountsAsync(
            string excludingActorId,
            DateTimeOffset? afterSubmittedAtUtc,
            Guid? afterCountId,
            int limit,
            CancellationToken cancellationToken)
        {
            CountListCalls++;
            OnCountList?.Invoke();
            var rows = Counts
                .Where(item => afterSubmittedAtUtc is null
                    || item.SubmittedAtUtc < afterSubmittedAtUtc.Value
                    || item.SubmittedAtUtc == afterSubmittedAtUtc.Value
                    && item.CountId.CompareTo(afterCountId!.Value) > 0)
                .OrderByDescending(item => item.SubmittedAtUtc)
                .ThenBy(item => item.CountId)
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<InventoryStockCountPendingApprovalItemV1>>(rows);
        }

        public Task<InventoryStockCountPendingApprovalItemV1?> ReadStockCountAsync(
            Guid countId, string excludingActorId, CancellationToken cancellationToken) =>
            Throw
                ? Task.FromException<InventoryStockCountPendingApprovalItemV1?>(new InvalidOperationException("failed"))
                : Task.FromResult(Count);

        public Task<IReadOnlyList<InventoryStockScrapPendingApprovalItemV1>> ReadStockScrapsAsync(
            string excludingActorId,
            DateTimeOffset? afterSubmittedAtUtc,
            Guid? afterScrapId,
            int limit,
            CancellationToken cancellationToken)
        {
            OnScrapList?.Invoke();
            var rows = Scraps
                .Where(item => afterSubmittedAtUtc is null
                    || item.SubmittedAtUtc < afterSubmittedAtUtc.Value
                    || item.SubmittedAtUtc == afterSubmittedAtUtc.Value
                    && item.ScrapId.CompareTo(afterScrapId!.Value) > 0)
                .OrderByDescending(item => item.SubmittedAtUtc)
                .ThenBy(item => item.ScrapId)
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<InventoryStockScrapPendingApprovalItemV1>>(rows);
        }

        public Task<InventoryStockScrapPendingApprovalItemV1?> ReadStockScrapAsync(
            Guid scrapId, string excludingActorId, CancellationToken cancellationToken) => Task.FromResult(Scrap);
    }

    private static InventoryStockCountPendingApprovalItemV1 Count() => new(
        CountId, Company, Guid.NewGuid(), "ETK-D05-COUNT", "1", "Company", Company, "Company", Company,
        Guid.NewGuid(), Guid.NewGuid(), "Free", "100", "97", "-3", "Sayım farkı",
        "counter", DateTimeOffset.Parse("2026-10-10T07:00:00Z"));

    private static InventoryStockScrapPendingApprovalItemV1 Scrap() => new(
        ScrapId, Company, Guid.NewGuid(), "ETK-D05-SCRAP", "1", "Company", Company, "Company", Company,
        Guid.NewGuid(), Guid.NewGuid(), "Free", "100", "6", "94", "Hurda",
        "recorder", DateTimeOffset.Parse("2026-10-10T08:00:00Z"));
}
