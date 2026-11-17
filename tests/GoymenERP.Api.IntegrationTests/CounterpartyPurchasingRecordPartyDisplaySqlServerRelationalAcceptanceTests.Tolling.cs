using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class CounterpartyPurchasingRecordPartyDisplaySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Purchasing_v2_reads_fresh_recorded_tolling_roles_through_the_real_owner_and_hides_foreign_accounts()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await SeedAsync(factory.DatabaseConnectionString);
            var company = new TestCompanyContext(TestCompanyIds.CompanyA);
            var user = new TestCurrentUser("synthetic-purchasing-v2-reader", new HashSet<Guid> { company.CompanyId });
            await using var db = CreateDbContext(factory.DatabaseConnectionString, company);
            var owner = new CounterpartyReadService(db, user, company, new DetailAuthorization(),
                new TestOrganizationService(company.CompanyId), new UnusedCursor(),
                new NullCounterpartyReadSqlObserver(), TimeProvider.System);
            var port = (IPurchasingPartyAccountPortV2)new CounterpartyPurchasingPartyAccountPortV1(company, user, owner);
            var accountId = await db.CompanyPartyAccounts.Where(x => x.CompanyId == company.CompanyId && x.PartyId == ActivePartyA)
                .Select(x => x.Id).SingleAsync();
            foreach (var roles in new[] { PartyRole.Tolling, PartyRole.Tolling | PartyRole.Supplier, PartyRole.None })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [counterparty].[CompanyPartyAccounts] SET [Roles] = {(int)roles}, [Version] = [Version] + 1 WHERE [CompanyId] = {company.CompanyId} AND [Id] = {accountId}");
                var answer = await port.GetAuthorizedAsync(ActivePartyA, accountId);
                Assert.Equal(PurchasingPartyAccountOutcomeV1.Found, answer.Outcome);
                var actual = Assert.IsType<PurchasingPartyAccountV2>(answer.Account);
                Assert.Equal(roles.HasFlag(PartyRole.Tolling), actual.AccountHasTollingRole);
                Assert.Equal(roles.HasFlag(PartyRole.Supplier), actual.AccountHasSupplierRole);
                Assert.Equal(PurchasingPaymentTermsScopeV2.OrderSpecific, actual.PaymentTermsScope);
                Assert.Equal(company.CompanyId, actual.CompanyId);
            }
            await using var foreignDb = CreateDbContext(factory.DatabaseConnectionString, new TestCompanyContext(TestCompanyIds.CompanyB));
            var foreignAccount = await foreignDb.CompanyPartyAccounts.Where(x => x.PartyId == ForeignParty).Select(x => x.Id).SingleAsync();
            Assert.Equal(new PurchasingPartyAccountAnswerV2(PurchasingPartyAccountOutcomeV1.NotFound, null),
                await port.GetAuthorizedAsync(ForeignParty, foreignAccount));
            Assert.Equal(new PurchasingPartyAccountAnswerV2(PurchasingPartyAccountOutcomeV1.NotFound, null),
                await port.GetAuthorizedAsync(ActivePartyA, foreignAccount));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnection, factory.DatabaseName);
        }
    }

    // These doubles provide only the test actor's authorization context; the account, role flags,
    // owner query and Purchasing adapter above are real. No grant is persisted by this fact.
    private sealed class DetailAuthorization : IErpAuthorizationService
    {
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == ErpPermissions.CounterpartyPartiesRead && companyId == TestCompanyIds.CompanyA
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedCursor : ICounterpartyCursorCodec
    {
        public bool IsAvailable => false;
        public string Protect(CounterpartyCursorState state) => throw new NotSupportedException();
        public bool TryUnprotect(string cursor, out CounterpartyCursorState state) => throw new NotSupportedException();
    }
}
