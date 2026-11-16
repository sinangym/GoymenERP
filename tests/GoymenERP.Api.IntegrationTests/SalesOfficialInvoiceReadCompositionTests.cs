using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-S1 (F-005 D2, API-35 / API-36): the composition that lifts the 503. The Sales module declares
/// <see cref="ISalesOfficialInvoiceReadPortV1"/> but never registers it (the match reads it optionally and answers 503 while it
/// is absent); the Finance owner answers it from its approved official sales invoice records (FIN-S6). These facts ask WHICH
/// port the two module registrations leave in the container and that the resolved port is Finance's read, not a stand-in:
/// exactly one registration, owned by Finance, and the Sales module adds no competing default that could shadow it.
/// SENTETIK: no SQL Server, no host, no HTTP — an in-memory Finance store and the registration semantics only.
/// </summary>
public sealed class SalesOfficialInvoiceReadCompositionTests
{
    private const string OfficialNo = "SNT2026SATS1000001";
    private const string SyntheticConnection = "Server=(local);Database=SENTETIK;Trusted_Connection=True;";
    private static readonly DateTimeOffset ApprovedAt = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_sales_module_alone_registers_no_official_invoice_read_port_so_the_match_stays_fail_closed()
    {
        var services = new ServiceCollection();
        services.AddSalesModule(Configuration());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ISalesOfficialInvoiceReadPortV1));
    }

    [Fact]
    public void Finance_and_sales_composed_together_leave_exactly_one_scoped_finance_owned_read_port()
    {
        var services = new ServiceCollection();
        services.AddFinanceServices();
        services.AddSalesModule(Configuration());

        var registration = Assert.Single(services,
            descriptor => descriptor.ServiceType == typeof(ISalesOfficialInvoiceReadPortV1));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.NotNull(registration.ImplementationFactory);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IFinanceOfficialSalesInvoiceReadPortV1));
    }

    [Fact]
    public async Task The_composed_port_answers_from_the_finance_records_found_only_for_an_approved_record()
    {
        var company = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var currency = Guid.NewGuid();
        var ettn = Guid.NewGuid().ToString("D");
        var pendingEttn = Guid.NewGuid().ToString("D");
        var database = $"sales-official-read-composition-{Guid.NewGuid():N}";
        await SeedAsync(database, company,
            Row(company, OfficialNo, ettn, customer, currency, FinanceOfficialSalesInvoiceStatusV1.Approved),
            Row(company, OfficialNo + "P", pendingEttn, customer, currency, FinanceOfficialSalesInvoiceStatusV1.PendingApproval));

        using var provider = Provider(database, company);
        using var scope = provider.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<ISalesOfficialInvoiceReadPortV1>();
        Assert.IsType<FinanceSalesOfficialInvoiceReadPortV1>(port);

        var found = await port.LookupAsync(company, OfficialNo, ettn);
        Assert.Equal(SalesOfficialInvoiceOutcomeV1.Found, found.Outcome);
        Assert.Equal(new SalesOfficialInvoiceFacts(OfficialNo, ettn, customer, currency, 1250.50m, 1250.50m, 208.42m, 0m),
            found.Invoice);

        // A record still waiting for its second-person approval is not yet the official fact: the match answers 409, not 503.
        var pending = await port.LookupAsync(company, OfficialNo + "P", pendingEttn);
        Assert.Equal((SalesOfficialInvoiceOutcomeV1.NotFound, (SalesOfficialInvoiceFacts?)null), (pending.Outcome, pending.Invoice));

        // The key is the number AND the ETTN together: the approved number with another ETTN names no record.
        var otherEttn = await port.LookupAsync(company, OfficialNo, pendingEttn);
        Assert.Equal(SalesOfficialInvoiceOutcomeV1.NotFound, otherEttn.Outcome);

        // Another company than the server-resolved one is never answered, not even as "not found".
        var foreign = await port.LookupAsync(Guid.NewGuid(), OfficialNo, ettn);
        Assert.Equal((SalesOfficialInvoiceOutcomeV1.Unavailable, (SalesOfficialInvoiceFacts?)null), (foreign.Outcome, foreign.Invoice));
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["DB_CONNECTION_STRING"] = SyntheticConnection })
        .Build();

    private static ServiceProvider Provider(string database, Guid company)
    {
        var services = new ServiceCollection();
        var context = new CompanyContext();
        context.Set(company);
        services.AddSingleton<ICompanyContext>(context);
        services.AddScoped(_ => new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(database).Options, context));
        services.AddFinanceServices();
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(string database, Guid company, params FinanceOfficialSalesInvoiceRow[] rows)
    {
        var context = new CompanyContext();
        context.Set(company);
        await using var db = new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(database).Options, context);
        db.OfficialSalesInvoices.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static FinanceOfficialSalesInvoiceRow Row(Guid company, string officialNo, string ettn, Guid customer, Guid currency,
        FinanceOfficialSalesInvoiceStatusV1 status)
    {
        var approved = status == FinanceOfficialSalesInvoiceStatusV1.Approved;
        return new FinanceOfficialSalesInvoiceRow
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            OfficialNo = officialNo,
            Ettn = ettn,
            CustomerPartyId = customer,
            CurrencyId = currency,
            PayableAmountText = "1250.50",
            PayableFunctionalAmountText = "1250.50",
            TaxBaseFunctionalAmountText = "1042.08",
            VatFunctionalAmountText = "208.42",
            WithholdingFunctionalAmountText = "0",
            PdfDocumentId = Guid.NewGuid(),
            PdfDocumentVersionNo = 1,
            PdfSha256 = new string('e', 64),
            Status = (int)status,
            Version = approved ? 2 : 1,
            RecordedBy = "SENTETIK-maker",
            RecordedAtUtc = ApprovedAt.AddHours(-1),
            ApprovedBy = approved ? "SENTETIK-checker" : null,
            ApprovedAtUtc = approved ? ApprovedAt : null,
            CorrelationId = "SENTETIK-correlation",
        };
    }
}
