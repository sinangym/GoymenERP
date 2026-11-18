using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KIM-D35/D48: real SQL projection of the migration's pending placeholder clear and the later approved identity.
/// Domain transitions below arrange isolated database states; they are not HTTP, grant or Audit acceptance evidence.
/// </summary>
public sealed class OrganizationCompanyTaxIdStatusSqlServerRelationalAcceptanceTests
{
    private const string ApprovedVkn = "9000000033";

    [SqlServerFact]
    public async Task A_pending_placeholder_clear_is_not_entered_and_a_rolled_back_first_approval_never_leaks_its_identity()
    {
        await WithDatabaseAsync(async factory =>
        {
            var companyId = await PlaceholderAsync(factory);
            Assert.Equal(new(companyId, OrganizationCompanyTaxIdStatusV1.NotEntered, null), await ReadAsync(factory, companyId));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
                var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                var company = await db.Companies.SingleAsync(row => row.Id == companyId);
                var correction = await db.CompanyTaxIdCorrections.SingleAsync(row => row.Status == CompanyTaxIdCorrectionStatus.Pending);
                Assert.Null(correction.CorrectedVkn);
                Assert.True(correction.IsCurrentFor(company));
                await using var transaction = await db.Database.BeginTransactionAsync();
                correction.Apply(company, "SENTETIK-clear-approver", DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
                var reader = scope.ServiceProvider.GetRequiredService<IOrganizationCompanyTaxIdStatusReaderV1>();
                Assert.Equal(new(companyId, OrganizationCompanyTaxIdStatusV1.NotEntered, null), await reader.ReadAsync(companyId));
                ApproveFirst(db, company);
                await db.SaveChangesAsync();
                Assert.Equal(new(companyId, OrganizationCompanyTaxIdStatusV1.Entered, ApprovedVkn), await reader.ReadAsync(companyId));
                Assert.True(company.IsPlaceholder);
                Assert.False(company.InvoiceCapable);
                await transaction.RollbackAsync();
            }
            Assert.Equal(new(companyId, OrganizationCompanyTaxIdStatusV1.NotEntered, null), await ReadAsync(factory, companyId));
            Assert.True((await ReadAsync(factory, TestCompanyIds.CompanyA))!.IsTaxIdEntered);
        });
    }

    [SqlServerFact]
    public async Task A_stale_clear_and_a_pending_real_replacement_never_hide_the_current_approved_vkn()
    {
        await WithDatabaseAsync(async factory =>
        {
            var companyId = await PlaceholderAsync(factory);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
                var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                var company = await db.Companies.SingleAsync(row => row.Id == companyId);
                var correction = await db.CompanyTaxIdCorrections.SingleAsync(row => row.Status == CompanyTaxIdCorrectionStatus.Pending);
                ApproveFirst(db, company);
                await db.SaveChangesAsync();
                Assert.True(correction.IsPending);
                Assert.False(correction.IsCurrentFor(company));
            }
            Assert.Equal(new(companyId, OrganizationCompanyTaxIdStatusV1.Entered, ApprovedVkn), await ReadAsync(factory, companyId));

            var before = await ReadAsync(factory, TestCompanyIds.CompanyA);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                var company = await db.Companies.SingleAsync(row => row.Id == TestCompanyIds.CompanyA);
                db.CompanyTaxIdCorrections.Add(new(Guid.NewGuid(), company.Id, "SENTETIK-replacement-source",
                    company.Vkn!, "9000000040", company.Version, "SENTETIK real replacement", DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }
            Assert.Equal(before, await ReadAsync(factory, TestCompanyIds.CompanyA));
            await using var foreign = factory.Services.CreateAsyncScope();
            foreign.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() => foreign.ServiceProvider
                .GetRequiredService<IOrganizationCompanyTaxIdStatusReaderV1>().ReadAsync(companyId));
        });
    }

    [SqlServerFact]
    public async Task Source_failure_or_company_change_after_the_single_sql_read_never_returns_an_entered_identity()
    {
        await WithDatabaseAsync(async factory =>
        {
            var companyId = await PlaceholderAsync(factory);
            var context = new ReboundTestContext(companyId);
            var observer = new ReadObserver(context, fail: true);
            await using (var db = Database(factory, context, observer))
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationCompanyTaxIdStatusReader(db, context).ReadAsync(companyId));
                Assert.Equal("SENTETIK tax-id source unavailable", error.Message);
            }
            observer = new ReadObserver(context, fail: false);
            await using (var db = Database(factory, context, observer))
                await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() => new OrganizationCompanyTaxIdStatusReader(db, context).ReadAsync(companyId));
            Assert.Equal(1, observer.SourceReads);
        });
    }

    private static OrganizationDbContext Database(SqlServerRelationalApiFactory factory, ICompanyContext context, ReadObserver observer) => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(factory.DatabaseConnectionString).AddInterceptors(observer).Options,
        context, new OrganizationBootstrapWriteScope());

    // Production CompanyContext itself rejects a rebind. This explicit interface double isolates the reader's
    // own post-await defence without weakening that production implementation or mutating another request.
    private sealed class ReboundTestContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class ReadObserver(ReboundTestContext context, bool fail) : DbCommandInterceptor
    {
        public int SourceReads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("CompanyTaxIdCorrections", StringComparison.Ordinal))
            {
                SourceReads++;
                if (fail) throw new InvalidOperationException("SENTETIK tax-id source unavailable");
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("CompanyTaxIdCorrections", StringComparison.Ordinal)) context.Set(TestCompanyIds.CompanyA);
            return ValueTask.FromResult(result);
        }
    }

    private static void ApproveFirst(OrganizationDbContext db, Company company)
    {
        var request = new CompanyTaxIdChangeRequest(Guid.NewGuid(), company.Id, company.Vkn, ApprovedVkn, company.Version,
            "SENTETIK first approved identity", "SENTETIK-maker", DateTimeOffset.UtcNow);
        request.Approve(company, "SENTETIK-checker", null, DateTimeOffset.UtcNow);
        db.CompanyTaxIdChangeRequests.Add(request);
    }

    private static async Task<Guid> PlaceholderAsync(SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().Companies.AsNoTracking()
            .Where(company => company.Code == "GIN").Select(company => company.Id).SingleAsync();
    }

    private static async Task<OrganizationCompanyTaxIdStateV1?> ReadAsync(SqlServerRelationalApiFactory factory, Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        return await scope.ServiceProvider.GetRequiredService<IOrganizationCompanyTaxIdStatusReaderV1>().ReadAsync(companyId);
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_TaxIdStatus_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection, databaseName: databaseName);
        // Fresh startup now seeds GIN without a VKN. Arrange the genuine pre-staging legacy state
        // before the host runs its migrations, so these facts exercise the migration's pending clear.
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer(factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organization")).Options;
        await using (var legacy = new OrganizationDbContext(options, new CompanyContext(), new OrganizationBootstrapWriteScope()))
        {
            await legacy.GetService<IMigrator>().MigrateAsync("20261115200655_ClearPlaceholderCompanyVkn");
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO organization.Companies
                    (Id, Code, LegalName, Vkn, IsPlaceholder, InvoiceCapable, IsActive, Version, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({TestCompanyIds.CompanyC}, N'GIN', N'SENTETIK legacy placeholder', N'1111111111',
                    1, 0, 1, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
                """);
        }
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        Assert.Equal(databaseName, cleanupDb.Database.GetDbConnection().Database);
        try { await test(factory); }
        finally { await cleanupDb.Database.EnsureDeletedAsync(); }
    }
}
