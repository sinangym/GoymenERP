using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceAllocationReadServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Allocation = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid OpenItem = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public async Task Read_permission_lists_and_reopens_the_same_allocation_without_writes()
    {
        await using var run = await Run.CreateAsync();
        var permissions = new SequencePermission(PermissionDecision.Allowed, PermissionDecision.Allowed,
            PermissionDecision.Allowed, PermissionDecision.Allowed, PermissionDecision.Allowed);
        var service = run.Service(Company, "reader", permissions);

        var page = await service.ListAuthorizedAsync(Context(Company, "reader"), CancellationToken.None);
        var detail = await service.GetAuthorizedAsync(Context(Company, "reader"), Allocation, CancellationToken.None);

        var listed = Assert.Single(Assert.IsType<FinanceAllocationPage>(page.Resource).Items);
        Assert.Equal(Allocation, listed.Id);
        Assert.Equal("1", listed.Version);
        Assert.Equal("7", Assert.IsType<FinanceAllocationDetail>(detail.Resource).OpenItemVersion);
        Assert.All(permissions.Actions, action => Assert.Equal(FinanceAction.ReadAllocation, action));
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Revocation_after_the_query_refuses_the_read_without_returning_rows()
    {
        await using var run = await Run.CreateAsync();
        var permissions = new SequencePermission(PermissionDecision.Allowed, PermissionDecision.Denied);
        var result = await run.Service(Company, "reader", permissions)
            .ListAuthorizedAsync(Context(Company, "reader"), CancellationToken.None);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Write_authority_does_not_substitute_for_the_separate_read_authority()
    {
        await using var run = await Run.CreateAsync();
        var result = await run.Service(Company, "writer", new ReadDenied())
            .GetAuthorizedAsync(Context(Company, "writer"), Allocation, CancellationToken.None);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Another_company_cannot_observe_the_allocation()
    {
        await using var run = await Run.CreateAsync();
        var result = await run.Service(OtherCompany, "reader", new SequencePermission(
                PermissionDecision.Allowed, PermissionDecision.Allowed))
            .GetAuthorizedAsync(Context(OtherCompany, "reader"), Allocation, CancellationToken.None);

        Assert.Equal(ReadOutcome.NotFound, result.Outcome);
        Assert.Null(result.Resource);
    }

    private static FinanceContext Context(Guid company, string actor) => new(company, actor, "SENTETIK-correlation", null);

    private sealed class SequencePermission(params PermissionDecision[] decisions) : IFinancePermissionPort
    {
        private int _index;
        public List<FinanceAction> Actions { get; } = [];
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken)
        {
            Actions.Add(action);
            var decision = decisions[Math.Min(_index, decisions.Length - 1)];
            _index++;
            return Task.FromResult(decision);
        }
    }

    private sealed class ReadDenied : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(
                action == FinanceAction.WriteAllocation ? PermissionDecision.Allowed : PermissionDecision.Denied);
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
        private readonly string _database = $"allocation-read-{Guid.NewGuid():N}";
        private readonly CompanyContext _seedCompany = new();
        public FinanceDbContext Db { get; private set; } = null!;

        public static async Task<Run> CreateAsync()
        {
            var run = new Run();
            run._seedCompany.Set(Company);
            run.Db = run.Context(run._seedCompany);
            run.Db.OpenItems.Add(new FinanceOpenItemRow
            {
                Id = OpenItem, CompanyId = Company, CurrencyId = Currency, Version = 7,
                SourceKind = "official.invoice-installment.v1", SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(),
                SourceVersion = "1", SourceBatchId = Guid.NewGuid(), SourceBatchVersion = "1", SourceBatchChecksum = "abc",
                PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), OriginalAmountText = "125.50",
                OutstandingAmountText = "0", Status = FinanceOpenItemStatuses.Closed, CreatedBy = "seed",
                UpdatedBy = "seed", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            run.Db.Allocations.Add(new FinanceAllocationRow
            {
                Id = Allocation, CompanyId = Company, SourceId = Guid.NewGuid(), OpenItemId = OpenItem,
                CurrencyId = Currency, AmountText = "125.50", Reason = "SENTETIK", Status = FinanceAllocationStatuses.Active,
                Version = 1, CreatedBy = "maker", CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await run.Db.SaveChangesAsync();
            run.Db.ChangeTracker.Clear();
            return run;
        }

        public FinanceAllocationReadService Service(Guid company, string actor, IFinancePermissionPort permissions)
        {
            var context = new CompanyContext();
            context.Set(company);
            Db = Context(context);
            return new FinanceAllocationReadService(Db, context, new User(actor, company), permissions);
        }

        private FinanceDbContext Context(ICompanyContext company) => new(
            new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(_database).Options, company);

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }
}
