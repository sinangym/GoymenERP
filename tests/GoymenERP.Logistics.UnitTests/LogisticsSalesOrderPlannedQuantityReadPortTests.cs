using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// SAT-D367, the Logistics side without a database: the module registers the open-instruction quantity read, and every
/// refusal (no company, another company, too many ids) and the empty request answer before any connection is opened — the
/// connection string points nowhere, so reaching the database would be a SqlException, not these answers. What the port sums
/// on real rows (issued only, instructed minus shipped, zero lines omitted) is proven on SQL Server
/// (<c>LogisticsInstructionSqlServerRelationalAcceptanceTests</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsSalesOrderPlannedQuantityReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public void The_logistics_module_registers_the_planned_quantity_read_over_its_own_store_as_a_scoped_service()
    {
        var services = new ServiceCollection().AddLogisticsModule(new Settings("Server=unused.invalid;Database=unused;"));

        var registration = Assert.Single(services,
            descriptor => descriptor.ServiceType == typeof(ILogisticsSalesOrderPlannedQuantityReadPortV1));
        Assert.Equal((ServiceLifetime.Scoped, typeof(LogisticsSalesOrderPlannedQuantityReadPort)),
            (registration.Lifetime, registration.ImplementationType));
    }

    [Fact]
    public async Task Without_a_resolved_company_the_port_answers_denied_before_any_connection()
    {
        await using var context = Context(resolved: false);
        var port = new LogisticsSalesOrderPlannedQuantityReadPort(context, new CompanyContext());

        var answer = await port.ReadOpenInstructionQuantitiesAsync(CompanyA, [Guid.NewGuid()]);

        Assert.Equal((LogisticsSalesOrderPlannedQuantityOutcomeV1.Denied,
                LogisticsSalesOrderPlannedQuantityReasonCodesV1.CompanyNotResolved, 0, 0, false),
            (answer.Outcome, answer.ReasonCode, answer.Lines.Count, answer.AnsweredOrderIds.Count, answer.HasMore));
    }

    [Fact]
    public async Task Another_company_than_the_context_company_is_denied_and_never_read()
    {
        await using var context = Context(resolved: true);
        var port = new LogisticsSalesOrderPlannedQuantityReadPort(context, Resolved());

        var answer = await port.ReadOpenInstructionQuantitiesAsync(CompanyB, [Guid.NewGuid()]);

        Assert.Equal((LogisticsSalesOrderPlannedQuantityOutcomeV1.Denied,
                LogisticsSalesOrderPlannedQuantityReasonCodesV1.CompanyMismatch, CompanyB, 0),
            (answer.Outcome, answer.ReasonCode, answer.CompanyId, answer.Lines.Count));
    }

    [Fact]
    public async Task No_order_id_or_only_empty_ids_answer_found_with_nothing_for_the_context_company_without_a_query()
    {
        await using var context = Context(resolved: true);
        var port = new LogisticsSalesOrderPlannedQuantityReadPort(context, Resolved());

        var none = await port.ReadOpenInstructionQuantitiesAsync(CompanyA, []);
        var empty = await port.ReadOpenInstructionQuantitiesAsync(CompanyA, [Guid.Empty, Guid.Empty]);

        foreach (var answer in new[] { none, empty })
        {
            Assert.Equal((LogisticsSalesOrderPlannedQuantityOutcomeV1.Found, (string?)null, CompanyA, 0, 0, false),
                (answer.Outcome, answer.ReasonCode, answer.CompanyId, answer.Lines.Count, answer.AnsweredOrderIds.Count,
                    answer.HasMore));
        }
    }

    [Fact]
    public async Task More_distinct_order_ids_than_the_contract_maximum_or_a_null_list_is_a_caller_error()
    {
        await using var context = Context(resolved: true);
        var port = new LogisticsSalesOrderPlannedQuantityReadPort(context, Resolved());
        var tooMany = Enumerable.Range(0, LogisticsSalesOrderPlannedQuantityContractV1.MaximumOrderIds + 1)
            .Select(_ => Guid.NewGuid()).ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => port.ReadOpenInstructionQuantitiesAsync(CompanyA, tooMany));
        await Assert.ThrowsAsync<ArgumentNullException>(() => port.ReadOpenInstructionQuantitiesAsync(CompanyA, null!));
    }

    [Fact]
    public void The_contract_name_and_limits_are_stable()
    {
        Assert.Equal(("logistics.sales-order-planned-quantity.v1", 200, 2000),
            (LogisticsSalesOrderPlannedQuantityContractV1.ContractName, LogisticsSalesOrderPlannedQuantityContractV1.MaximumOrderIds,
                LogisticsSalesOrderPlannedQuantityContractV1.MaximumLines));
        Assert.Equal((1, 2, 3), ((int)LogisticsSalesOrderPlannedQuantityOutcomeV1.Found,
            (int)LogisticsSalesOrderPlannedQuantityOutcomeV1.Denied, (int)LogisticsSalesOrderPlannedQuantityOutcomeV1.Unavailable));
    }

    private static CompanyContext Resolved()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return company;
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
        var company = resolved ? Resolved() : new CompanyContext();
        return new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
