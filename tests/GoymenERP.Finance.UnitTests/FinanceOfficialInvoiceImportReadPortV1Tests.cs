using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOfficialInvoiceImportReadPortV1Tests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Batch = Guid.NewGuid();

    [Fact]
    public async Task Read_returns_the_persisted_finance_open_item_after_two_authority_checks()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        db.OpenItems.Add(Row());
        await db.SaveChangesAsync();
        var authority = new Authority(PermissionDecision.Allowed, PermissionDecision.Allowed);
        var service = new FinanceOfficialInvoiceImportReadPortV1(db, company, new User("actor", Company), authority);

        var result = await service.ReadAsync(Context(), Batch, default);

        Assert.Equal(ReadOutcome.Found, result.Outcome);
        var item = Assert.Single(result.Resource!.OpenItems);
        Assert.Equal(Row().Id, item.Id);
        Assert.Equal("1250.50", item.OutstandingAmount);
        Assert.Equal("1", item.Version);
        Assert.Equal(2, authority.Calls);
    }

    [Fact]
    public async Task Authority_revoked_during_read_refuses_the_result()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        db.OpenItems.Add(Row());
        await db.SaveChangesAsync();
        var service = new FinanceOfficialInvoiceImportReadPortV1(
            db, company, new User("actor", Company),
            new Authority(PermissionDecision.Allowed, PermissionDecision.Denied));

        var result = await service.ReadAsync(Context(), Batch, default);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Wrong_company_refuses_before_authority_or_database_read()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var authority = new Authority(PermissionDecision.Allowed);
        var service = new FinanceOfficialInvoiceImportReadPortV1(
            db, company, new User("actor", Company), authority);

        var result = await service.ReadAsync(
            new FinanceContext(Guid.NewGuid(), "actor", "correlation", null), Batch, default);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Equal(0, authority.Calls);
    }

    [Fact]
    public async Task Missing_batch_is_not_found_only_after_the_second_authority_check()
    {
        var company = CompanyContext();
        await using var db = Database(company);
        var authority = new Authority(PermissionDecision.Allowed, PermissionDecision.Allowed);
        var service = new FinanceOfficialInvoiceImportReadPortV1(db, company, new User("actor", Company), authority);

        var result = await service.ReadAsync(Context(), Batch, default);

        Assert.Equal(ReadOutcome.NotFound, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(2, authority.Calls);
    }

    private static FinanceDbContext Database(ICompanyContext company) =>
        new(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"finance-import-read-{Guid.NewGuid():N}")
            .Options, company);

    private static CompanyContext CompanyContext()
    {
        var context = new CompanyContext();
        context.Set(Company);
        return context;
    }

    private static FinanceContext Context() => new(Company, "actor", "correlation", null);

    private static FinanceOpenItemRow Row() => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        CompanyId = Company,
        SourceKind = FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
        SourceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        SourceLineId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        SourceVersion = "invoice-v1",
        SourceBatchId = Batch,
        SourceBatchVersion = "official-file-v1",
        SourceBatchChecksum = new string('a', 64),
        PartyId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        CompanyAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
        CurrencyId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
        OriginalAmountText = "1250.50",
        OutstandingAmountText = "1250.50",
        DueOn = new DateOnly(2026, 10, 31),
        Status = FinanceOpenItemStatuses.Open,
        Version = 1,
        CreatedBy = "actor",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedBy = "actor",
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority(params PermissionDecision[] decisions) : IFinanceOpenItemSourceAuthority
    {
        private readonly Queue<PermissionDecision> _decisions = new(decisions);
        public int Calls { get; private set; }

        public Task<PermissionDecision> CheckAsync(
            FinanceContext context,
            Guid batchId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_decisions.Count == 0 ? PermissionDecision.Denied : _decisions.Dequeue());
        }
    }
}
