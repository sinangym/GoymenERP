using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// DEC-SAL-005, the Logistics side without a database: the module registers its answer to the Sales-declared loading read
/// (so Sales never reaches Logistics tables), and the port refuses to read without a server-resolved company before any
/// connection is opened. What it sums on real rows — every instruction line of the order, instructed or loaded included — is
/// proven on SQL Server (<c>SalesOrderCancellationSqlServerRelationalAcceptanceTests</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsSalesOrderLoadingReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void The_logistics_module_registers_the_sales_loading_read_over_its_own_store_as_a_scoped_service()
    {
        var services = new ServiceCollection().AddLogisticsModule(new Settings("Server=unused.invalid;Database=unused;"));

        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISalesOrderLoadingReadPortV1));
        Assert.Equal((ServiceLifetime.Scoped, typeof(LogisticsSalesOrderLoadingReadPort)),
            (registration.Lifetime, registration.ImplementationType));
    }

    [Fact]
    public async Task Without_a_resolved_company_the_port_refuses_before_any_connection_and_never_reads_unscoped()
    {
        await using var context = Context(resolved: false);
        var port = new LogisticsSalesOrderLoadingReadPort(context, new CompanyContext());

        // The connection string points nowhere: reaching the database would be a SqlException, not this refusal.
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadOrderLoadingAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task An_empty_order_id_answers_nothing_for_the_context_company_without_a_query()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var context = Context(resolved: true);
        var port = new LogisticsSalesOrderLoadingReadPort(context, company);

        var answer = await port.ReadOrderLoadingAsync(Guid.Empty);

        Assert.Equal((CompanyA, Guid.Empty, 0), (answer.CompanyId, answer.OrderId, answer.Lines.Count));
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
