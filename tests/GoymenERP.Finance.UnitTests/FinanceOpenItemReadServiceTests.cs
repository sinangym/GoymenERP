using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOpenItemReadServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OpenItem = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Source = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid SourceLine = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Party = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid CompanyAccount = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Currency = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Separate_read_and_amount_authorities_return_stored_metadata_and_money_without_writes()
    {
        await using var run = await Run.CreateAsync(Row());
        var permissions = Allowed(8);
        var service = run.Service(Company, "reader", permissions);

        var list = await service.ListAuthorizedAsync(
            Context(Company, "reader"), new SourceListRequest(null), CancellationToken.None);
        var detail = await service.DetailAuthorizedAsync(
            Context(Company, "reader"), OpenItem, CancellationToken.None);

        var listed = Assert.Single(Assert.IsType<FinanceOpenItemPageV1>(list.Resource).Items);
        var reopened = Assert.IsType<FinanceOpenItemViewV1>(detail.Resource);
        Assert.Equal(listed, reopened);
        Assert.Equal(OpenItem, reopened.Id);
        Assert.Equal(Company, reopened.CompanyId);
        Assert.Equal("official.invoice-installment.v1", reopened.SourceKind);
        Assert.Equal(Source, reopened.SourceId);
        Assert.Equal(SourceLine, reopened.SourceLineId);
        Assert.Equal("invoice:v3", reopened.SourceVersion);
        Assert.Equal(Party, reopened.PartyId);
        Assert.Equal(CompanyAccount, reopened.CompanyAccountId);
        Assert.Equal(Currency, reopened.CurrencyId);
        Assert.Equal("7", reopened.Version);
        Assert.Equal(new DateOnly(2026, 11, 1), reopened.DueOn);
        Assert.Equal(FinanceOpenItemStatusV1.Open, reopened.Status);
        Assert.Equal(CreatedAtUtc, reopened.CreatedAtUtc);
        Assert.Equal(AmountDisclosure.Visible, reopened.InitialAmount.Disclosure);
        Assert.Equal(new MoneyText("125.50", Currency), reopened.InitialAmount.Money);
        Assert.Equal(new MoneyText("75.25", Currency), reopened.OutstandingAmount.Money);
        Assert.Equal(
            new (FinanceAction Action, Guid? SourceId)[]
            {
                (FinanceAction.ReadOpenItems, (Guid?)null),
                (FinanceAction.ReadOpenItemAmount, (Guid?)null),
                (FinanceAction.ReadOpenItemAmount, (Guid?)null),
                (FinanceAction.ReadOpenItems, (Guid?)null),
                (FinanceAction.ReadOpenItems, (Guid?)OpenItem),
                (FinanceAction.ReadOpenItemAmount, (Guid?)OpenItem),
                (FinanceAction.ReadOpenItemAmount, (Guid?)OpenItem),
                (FinanceAction.ReadOpenItems, (Guid?)OpenItem),
            },
            permissions.Asked);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, PermissionDecision.Allowed, AmountDisclosure.Redacted)]
    [InlineData(PermissionDecision.Allowed, PermissionDecision.Denied, AmountDisclosure.Redacted)]
    [InlineData(PermissionDecision.Unavailable, PermissionDecision.Allowed, AmountDisclosure.Unavailable)]
    [InlineData(PermissionDecision.Allowed, PermissionDecision.Unavailable, AmountDisclosure.Unavailable)]
    public async Task Amount_is_released_only_when_both_fresh_measurements_allow_it(
        PermissionDecision amountBefore,
        PermissionDecision amountAfter,
        AmountDisclosure expected)
    {
        await using var run = await Run.CreateAsync(Row());
        var result = await run.Service(Company, "reader", new SequencePermission(
                PermissionDecision.Allowed, amountBefore, amountAfter, PermissionDecision.Allowed))
            .DetailAuthorizedAsync(Context(Company, "reader"), OpenItem, CancellationToken.None);

        var resource = Assert.IsType<FinanceOpenItemViewV1>(result.Resource);
        Assert.Equal(ReadOutcome.Found, result.Outcome);
        Assert.Equal(expected, resource.InitialAmount.Disclosure);
        Assert.Null(resource.InitialAmount.Money);
        Assert.Equal(expected, resource.OutstandingAmount.Disclosure);
        Assert.Null(resource.OutstandingAmount.Money);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Metadata_revocation_after_the_query_refuses_the_whole_response()
    {
        await using var run = await Run.CreateAsync(Row());
        var result = await run.Service(Company, "reader", new SequencePermission(
                PermissionDecision.Allowed,
                PermissionDecision.Allowed,
                PermissionDecision.Allowed,
                PermissionDecision.Denied))
            .DetailAuthorizedAsync(Context(Company, "reader"), OpenItem, CancellationToken.None);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Malformed_stored_source_identity_fails_closed()
    {
        var malformed = Row();
        malformed.SourceVersion = " invoice:v3 ";
        await using var run = await Run.CreateAsync(malformed);

        var result = await run.Service(Company, "reader", Allowed(4))
            .DetailAuthorizedAsync(Context(Company, "reader"), OpenItem, CancellationToken.None);

        Assert.Equal(ReadOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Stable_keyset_page_is_actor_bound_and_does_not_cross_company()
    {
        var rows = Enumerable.Range(0, FinanceOpenItemReadService.PageSize + 1)
            .Select(index => Row(Guid.NewGuid(), CreatedAtUtc.AddMinutes(-index)))
            .ToArray();
        await using var run = await Run.CreateAsync(rows);
        var permissions = Allowed(8);
        var service = run.Service(Company, "reader", permissions);

        var first = await service.ListAuthorizedAsync(
            Context(Company, "reader"), new SourceListRequest(null), CancellationToken.None);
        var firstPage = Assert.IsType<FinanceOpenItemPageV1>(first.Resource);
        Assert.Equal(FinanceOpenItemReadService.PageSize, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(rows.Take(FinanceOpenItemReadService.PageSize).Select(row => row.Id),
            firstPage.Items.Select(item => item.Id));

        var second = await service.ListAuthorizedAsync(
            Context(Company, "reader"), new SourceListRequest(firstPage.NextCursor), CancellationToken.None);
        var secondPage = Assert.IsType<FinanceOpenItemPageV1>(second.Resource);
        Assert.Equal(rows[^1].Id, Assert.Single(secondPage.Items).Id);
        Assert.Null(secondPage.NextCursor);

        var wrongActor = await run.Service(Company, "other-reader", Allowed(1)).ListAuthorizedAsync(
            Context(Company, "other-reader"), new SourceListRequest(firstPage.NextCursor), CancellationToken.None);
        Assert.Equal(ReadOutcome.NotFound, wrongActor.Outcome);
        Assert.Null(wrongActor.Resource);

        var otherCompany = await run.Service(OtherCompany, "reader", Allowed(4)).DetailAuthorizedAsync(
            Context(OtherCompany, "reader"), rows[0].Id, CancellationToken.None);
        Assert.Equal(ReadOutcome.NotFound, otherCompany.Outcome);
        Assert.Null(otherCompany.Resource);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    private static SequencePermission Allowed(int count) =>
        new(Enumerable.Repeat(PermissionDecision.Allowed, count).ToArray());

    private static FinanceContext Context(Guid company, string actor) =>
        new(company, actor, "SENTETIK-correlation", null);

    private static FinanceOpenItemRow Row(Guid? id = null, DateTimeOffset? createdAtUtc = null) => new()
    {
        Id = id ?? OpenItem,
        CompanyId = Company,
        SourceKind = "official.invoice-installment.v1",
        SourceId = Source,
        SourceLineId = id ?? SourceLine,
        SourceVersion = "invoice:v3",
        SourceBatchId = Guid.NewGuid(),
        SourceBatchVersion = "batch:v1",
        SourceBatchChecksum = new string('a', 64),
        PartyId = Party,
        CompanyAccountId = CompanyAccount,
        CurrencyId = Currency,
        OriginalAmountText = "125.50",
        OutstandingAmountText = "75.25",
        DueOn = new DateOnly(2026, 11, 1),
        Status = FinanceOpenItemStatuses.Open,
        Version = 7,
        CreatedBy = "SENTETIK-seed",
        CreatedAtUtc = createdAtUtc ?? CreatedAtUtc,
        UpdatedBy = "SENTETIK-seed",
        UpdatedAtUtc = createdAtUtc ?? CreatedAtUtc,
    };

    private sealed class SequencePermission(params PermissionDecision[] decisions) : IFinancePermissionPort
    {
        private int _index;
        public List<(FinanceAction Action, Guid? SourceId)> Asked { get; } = [];

        public Task<PermissionDecision> CheckAsync(
            FinanceContext context,
            FinanceAction action,
            Guid? sourceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Asked.Add((action, sourceId));
            var decision = decisions[Math.Min(_index, decisions.Length - 1)];
            _index++;
            return Task.FromResult(decision);
        }
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _database = $"open-item-read-{Guid.NewGuid():N}";
        public FinanceDbContext Db { get; private set; } = null!;

        public static async Task<Run> CreateAsync(params FinanceOpenItemRow[] rows)
        {
            var run = new Run();
            var seedCompany = new CompanyContext();
            seedCompany.Set(Company);
            run.Db = run.Context(seedCompany);
            run.Db.OpenItems.AddRange(rows);
            await run.Db.SaveChangesAsync();
            run.Db.ChangeTracker.Clear();
            return run;
        }

        public FinanceOpenItemReadService Service(
            Guid company,
            string actor,
            IFinancePermissionPort permissions)
        {
            var context = new CompanyContext();
            context.Set(company);
            Db = Context(context);
            return new FinanceOpenItemReadService(Db, context, new User(actor, company), permissions);
        }

        private FinanceDbContext Context(ICompanyContext company) => new(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(_database)
                .Options,
            company);

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }
}
