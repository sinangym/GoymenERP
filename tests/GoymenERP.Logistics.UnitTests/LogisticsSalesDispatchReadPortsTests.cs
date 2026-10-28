using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// MOD-06 / API-30 / API-31, the Logistics side without a database: the module registers its answers to the two Sales-declared
/// dispatch reads (so Sales never reaches Logistics tables), and without a server-resolved company both answer Denied before any
/// connection is opened (never an unscoped read, never an empty "found"). What they read on real rows — approved and unreleased
/// lines only, company-filtered, paged, AT-055 single link — is proven on SQL Server
/// (<c>SalesDispatchSqlServerRelationalAcceptanceTests</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsSalesDispatchReadPortsTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void The_logistics_module_registers_both_sales_dispatch_reads_over_one_scoped_owner_implementation()
    {
        var services = new ServiceCollection().AddLogisticsModule(new Settings("Server=unused.invalid;Database=unused;"));

        Assert.Equal(ServiceLifetime.Scoped,
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISalesDispatchNoteReadPortV1)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped,
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISalesInvoiceableShipmentReadPortV1)).Lifetime);
        var owner = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(LogisticsSalesDispatchReadPorts));
        Assert.Equal((ServiceLifetime.Scoped, typeof(LogisticsSalesDispatchReadPorts)), (owner.Lifetime, owner.ImplementationType));
    }

    [Fact]
    public async Task Without_a_resolved_company_both_reads_answer_denied_before_any_connection()
    {
        await using var context = Context(resolved: false);
        var ports = new LogisticsSalesDispatchReadPorts(context, new CompanyContext());

        // The connection string points nowhere: reaching the database would be a SqlException, not this answer.
        var notes = await ports.ListSalesDispatchNotesAsync(new SalesDispatchNotePageQueryV1(10, null, null));
        var shares = await ports.ReadInvoiceableShipmentsAsync(new SalesInvoiceableShipmentQueryV1(null, 10));

        Assert.Equal((SalesDispatchReadOutcomeV1.Denied, Guid.Empty, 0, null),
            (notes.Outcome, notes.CompanyId, notes.Notes.Count, notes.NextCursor));
        Assert.Equal((SalesDispatchReadOutcomeV1.Denied, Guid.Empty, 0, false, SalesInvoicedStateSourceV1.NoSource),
            (shares.Outcome, shares.CompanyId, shares.Shares.Count, shares.HasMore, shares.InvoicedState));
    }

    [Fact]
    public async Task A_broken_or_foreign_cursor_is_an_invalid_cursor_answer_before_any_connection()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var context = Context(resolved: true);
        var ports = new LogisticsSalesDispatchReadPorts(context, company);
        var foreign = LogisticsStore.ListCursor.Encode(Guid.NewGuid(), DateTimeOffset.UnixEpoch, Guid.NewGuid());

        foreach (var cursor in new[] { "%%%", foreign })
        {
            var answer = await ports.ListSalesDispatchNotesAsync(new SalesDispatchNotePageQueryV1(10, cursor, null));
            Assert.Equal((SalesDispatchReadOutcomeV1.InvalidCursor, CompanyA, 0), (answer.Outcome, answer.CompanyId, answer.Notes.Count));
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ports.ListSalesDispatchNotesAsync(new SalesDispatchNotePageQueryV1(0, null, null)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ports.ReadInvoiceableShipmentsAsync(new SalesInvoiceableShipmentQueryV1(Guid.Empty, 10)));
    }

    /// <summary>The two host settings the module reads (no configuration package in this test project).</summary>
    private sealed class Settings(string connectionString) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING" ? connectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    private static LogisticsDbContext Context(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        return new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
