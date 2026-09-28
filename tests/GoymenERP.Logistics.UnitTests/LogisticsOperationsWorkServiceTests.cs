using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// AT-025 ("Satış ve satın alma belgeleri operasyon listelerinde karışmaz") without a database or a host: the two sections
/// of the operations work list are SEPARATE — each asks only its own owner, measures only its own name, names its own
/// section and carries its own wire type; a foreign-company or malformed owner answer fails closed; neither type has a price
/// member (Sinan §4, #66 5738113623). Registered platform names stand in for the two operations names until the central
/// catalogue knows them (the LogisticsInstructionServiceTests way). What the owners' ports answer on real rows — company
/// fence, cancelled / draft / fully received lines left out — is proven on SQL Server
/// (<c>LogisticsOperationsWorkSqlServerRelationalAcceptanceTests</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsOperationsWorkServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Customer = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid CustomerAccount = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Supplier = Guid.Parse("35353535-3535-4353-8353-353535353535");
    private static readonly Guid SupplierAccount = Guid.Parse("45454545-4545-4454-8454-454545454545");
    private static readonly Guid SalesOrder = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid SalesLine = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid SecondSalesLine = Guid.Parse("67676767-6767-4676-8676-676767676767");
    private static readonly Guid PurchaseOrder = Guid.Parse("56565656-5656-4565-8565-565656565656");
    private static readonly Guid PurchaseLine = Guid.Parse("68686868-6868-4686-8686-686868686868");
    private static readonly Guid KgUnit = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private const string Actor = "synthetic-actor";
    private const string SalesPermission = ErpPermissions.CounterpartyPartiesRead;
    private const string PurchasePermission = ErpPermissions.CounterpartyPartiesCreate;

    [Fact]
    public async Task The_sales_section_lists_the_approved_sales_lines_with_logistics_own_totals_and_asks_nothing_of_purchasing()
    {
        var world = new World();

        var page = await world.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery(25, null));

        Assert.Equal(LogisticsOperationsWorkSectionsV1.Sales, page.Section);
        Assert.Equal("SENTETIK-sales-next", page.NextCursor);
        Assert.Equal(2, page.Items.Count);
        var first = page.Items[0];
        Assert.Equal((SalesOrder, "SENTETIK SO-0001", SalesLine, 1, Customer, "Sentetik Müşteri", "GDC-001", "Sentetik rulo", KgUnit, "kg"),
            (first.OrderId, first.DocumentNo, first.LineId, first.LineNo, first.CustomerPartyId, first.CustomerName, first.ProductCode,
                first.ProductName, first.UnitId, first.UnitCode));
        // The ordered text is the Sales owner's, verbatim; Logistics's own totals are canonical exact texts (Ö2, never rounded).
        Assert.Equal(("12.500", "10", "8.5", "4", "2.25"), (first.Ordered, first.Instructed, first.Loaded, first.Shipped, first.Delivered));
        Assert.Equal(new DateOnly(2026, 9, 25), first.RequestedDeliveryDate);
        // A line Logistics holds nothing for reads zero, never absent.
        var second = page.Items[1];
        Assert.Equal((SecondSalesLine, "3", "0", "0", "0", "0"), (second.LineId, second.Ordered, second.Instructed, second.Loaded, second.Shipped, second.Delivered));
        Assert.Equal(25, world.SalesLines.LastPageSize);
        Assert.Equal([SalesLine, SecondSalesLine], world.Progress.LastIds);
        Assert.Equal((0, 1), (world.PurchaseLines.Calls, world.Accounts.Calls));
        Assert.All(world.Authority.Permissions, permission => Assert.Equal(SalesPermission, permission));
        Assert.True(world.Authority.Permissions.Count >= 4, $"measured {world.Authority.Permissions.Count} times");
    }

    [Fact]
    public async Task The_purchase_section_lists_the_lines_awaiting_receipt_and_asks_nothing_of_sales()
    {
        var world = new World();

        var page = await world.Service().GetPurchaseWorkAsync(new LogisticsOperationsWorkQuery(10, "SENTETIK-purchase-cursor"));

        Assert.Equal(LogisticsOperationsWorkSectionsV1.Purchase, page.Section);
        Assert.Null(page.NextCursor);
        var line = Assert.Single(page.Items);
        Assert.Equal((PurchaseOrder, new DateOnly(2026, 9, 19), PurchaseLine, 1, "General", Supplier, "Sentetik Tedarikçi", Product, KgUnit, "100", "40", "60"),
            (line.OrderId, line.OrderEffectiveOn, line.LineId, line.LineNo, line.Kind, line.SupplierPartyId, line.SupplierName, line.ProductId,
                line.UnitId, line.Committed, line.Accepted, line.Remaining));
        Assert.Equal((10, "SENTETIK-purchase-cursor"), (world.PurchaseLines.LastPageSize, world.PurchaseLines.LastCursor));
        Assert.Equal((0, 0), (world.SalesLines.Calls, world.Progress.Calls));
        Assert.All(world.Authority.Permissions, permission => Assert.Equal(PurchasePermission, permission));
    }

    [Fact]
    public async Task Each_section_needs_its_own_grant_and_neither_grant_opens_the_other()
    {
        var salesOnly = new World { Authority = { Denied = PurchasePermission } };
        Assert.Equal(LogisticsOperationsWorkSectionsV1.Sales, (await salesOnly.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery())).Section);
        var refusedPurchase = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            salesOnly.Service().GetPurchaseWorkAsync(new LogisticsOperationsWorkQuery()));
        Assert.Equal((403, "forbidden"), (refusedPurchase.Status, refusedPurchase.Code));
        Assert.Equal(0, salesOnly.PurchaseLines.Calls);

        var purchaseOnly = new World { Authority = { Denied = SalesPermission } };
        Assert.Equal(LogisticsOperationsWorkSectionsV1.Purchase,
            (await purchaseOnly.Service().GetPurchaseWorkAsync(new LogisticsOperationsWorkQuery())).Section);
        var refusedSales = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            purchaseOnly.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery()));
        Assert.Equal((403, "forbidden"), (refusedSales.Status, refusedSales.Code));
        Assert.Equal((0, 0), (purchaseOnly.SalesLines.Calls, purchaseOnly.Progress.Calls));
    }

    [Fact]
    public async Task A_grant_lost_while_an_owner_answers_releases_nothing()
    {
        var world = new World();
        world.SalesLines.OnRead = () => world.Authority.Denied = SalesPermission;

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery()));

        Assert.Equal(403, refusal.Status);
        Assert.Equal((0, 0), (world.Progress.Calls, world.Accounts.Calls));
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("duplicate-line")]
    [InlineData("over-cap")]
    public async Task A_sales_owner_answer_of_another_company_or_malformed_fails_closed(string fault)
    {
        var world = new World { SalesLines = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery(fault == "over-cap" ? 1 : 50, null)));

        Assert.Equal((503, LogisticsProblemCodesV1.ReferenceProviderInvalid), (refusal.Status, refusal.Code));
        Assert.Equal(0, world.Accounts.Calls);
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("nothing-remaining")]
    [InlineData("negative-accepted")]
    public async Task A_purchase_owner_answer_of_another_company_or_without_anything_to_receive_fails_closed(string fault)
    {
        var world = new World { PurchaseLines = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetPurchaseWorkAsync(new LogisticsOperationsWorkQuery()));

        Assert.Equal((503, LogisticsProblemCodesV1.ReferenceProviderInvalid), (refusal.Status, refusal.Code));
    }

    [Theory]
    [InlineData("other-unit")]
    [InlineData("foreign-line")]
    [InlineData("delivered-above-shipped")]
    [InlineData("throws")]
    public async Task Logistics_totals_that_cannot_be_shown_against_the_order_line_fail_closed(string fault)
    {
        var world = new World { Progress = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery()));

        Assert.Equal(503, refusal.Status);
        Assert.Equal(fault == "throws" ? "service_unavailable" : LogisticsProblemCodesV1.ReferenceProviderInvalid, refusal.Code);
    }

    [Fact]
    public async Task A_cursor_the_owner_refuses_is_400_and_an_owner_outage_is_its_own_503()
    {
        var badCursor = new World { SalesLines = { Fault = "bad-cursor" }, PurchaseLines = { Fault = "bad-cursor" } };
        Assert.Equal((400, LogisticsProblemCodesV1.InvalidCursor), Code(await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            badCursor.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery(50, "foreign")))));
        Assert.Equal((400, LogisticsProblemCodesV1.InvalidCursor), Code(await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            badCursor.Service().GetPurchaseWorkAsync(new LogisticsOperationsWorkQuery(50, "foreign")))));

        var outage = new World { SalesLines = { Fault = "throws" }, PurchaseLines = { Fault = "throws" } };
        Assert.Equal((503, LogisticsProblemCodesV1.SalesProviderUnavailable), Code(await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            outage.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery()))));
        Assert.Equal((503, LogisticsProblemCodesV1.PurchasingProviderUnavailable), Code(await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            outage.Service().GetPurchaseWorkAsync(new LogisticsOperationsWorkQuery()))));
    }

    [Theory]
    [InlineData(101, null)]
    [InlineData(-1, null)]
    [InlineData(50, "  ")]
    public async Task A_page_size_or_cursor_out_of_shape_is_refused_before_any_owner_is_asked(int pageSize, string? cursor)
    {
        var world = new World();

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery(pageSize, cursor)));

        Assert.Equal((400, "validation_failed"), Code(refusal));
        Assert.Equal(0, world.SalesLines.Calls);
    }

    [Fact]
    public async Task A_counterparty_the_cari_owner_does_not_answer_keeps_its_rows_under_a_labelled_reference_and_a_denial_is_403()
    {
        var unknown = new World { Accounts = { Fault = "not-found" } };
        var purchase = await unknown.Service().GetPurchaseWorkAsync(new LogisticsOperationsWorkQuery());
        Assert.Equal($"ref. …{Supplier.ToString("D")[^8..]}", Assert.Single(purchase.Items).SupplierName);

        var denied = new World { Accounts = { Fault = "denied" } };
        Assert.Equal(403, (await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            denied.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery()))).Status);
    }

    [Fact]
    public async Task An_unregistered_operations_name_is_a_setup_problem_never_a_grant()
    {
        var world = new World { Bindings = new LogisticsOperationsWorkBindings("logistics.synthetic-unregistered.read", null) };

        Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), Code(await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().GetSalesWorkAsync(new LogisticsOperationsWorkQuery()))));
        Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), Code(await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().GetPurchaseWorkAsync(new LogisticsOperationsWorkQuery()))));
        Assert.Empty(world.Authority.Permissions);
    }

    [Fact]
    public void Neither_section_type_carries_a_price_and_the_two_types_share_no_order_member()
    {
        string[] sales = [.. typeof(LogisticsSalesWorkItemView).GetProperties().Select(property => property.Name)];
        string[] purchase = [.. typeof(LogisticsPurchaseWorkItemView).GetProperties().Select(property => property.Name)];
        Assert.Equal(
            ["OrderId", "DocumentNo", "LineId", "LineNo", "CustomerPartyId", "CustomerName", "ProductId", "ProductCode", "ProductName", "UnitId",
                "UnitCode", "Ordered", "Instructed", "Loaded", "Shipped", "Delivered", "RequestedDeliveryDate"],
            sales);
        Assert.Equal(
            ["OrderId", "OrderEffectiveOn", "LineId", "LineNo", "Kind", "SupplierPartyId", "SupplierName", "ProductId", "UnitId", "Committed",
                "Accepted", "Remaining"],
            purchase);
        string[] barred = ["price", "amount", "discount", "currency", "payment", "money", "tax", "cost", "margin"];
        Assert.DoesNotContain(sales.Concat(purchase), name => barred.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        // A sales row names a customer, a purchase row a supplier: no member lets one be read as the other.
        Assert.DoesNotContain("SupplierPartyId", sales);
        Assert.DoesNotContain("CustomerPartyId", purchase);
    }

    [Fact]
    public void The_default_bindings_are_the_two_manifest_names_and_the_module_registers_the_service_and_its_own_reader()
    {
        Assert.Equal(new LogisticsOperationsWorkBindings(LogisticsPermissionManifestV1.OperationsSalesWorkRead,
            LogisticsPermissionManifestV1.OperationsPurchaseWorkRead), new LogisticsOperationsWorkBindings());
        Assert.NotEqual(LogisticsPermissionManifestV1.OperationsSalesWorkRead, LogisticsPermissionManifestV1.OperationsPurchaseWorkRead);
        Assert.Contains(LogisticsPermissionManifestV1.OperationsSalesWorkRead, LogisticsPermissionManifestV1.Names);
        Assert.Contains(LogisticsPermissionManifestV1.OperationsPurchaseWorkRead, LogisticsPermissionManifestV1.Names);

        var services = new ServiceCollection().AddLogisticsModule(new Settings("Server=unused.invalid;Database=unused;"));
        Assert.Equal((ServiceLifetime.Scoped, typeof(LogisticsOperationsWorkService)),
            Registration(services, typeof(ILogisticsOperationsWorkService)));
        Assert.Equal((ServiceLifetime.Scoped, typeof(LogisticsOrderLineProgressReader)),
            Registration(services, typeof(ILogisticsOrderLineProgressReader)));
    }

    [Fact]
    public void The_shell_declares_one_screen_per_section_each_behind_its_own_name()
    {
        var declarations = new LogisticsShellCapabilityProvider().GetDeclarations();
        var sales = Assert.Single(declarations, item => item.CapabilityId == LogisticsShellCapabilityProvider.SalesWorkCapabilityId);
        var purchase = Assert.Single(declarations, item => item.CapabilityId == LogisticsShellCapabilityProvider.PurchaseWorkCapabilityId);
        Assert.Equal(("/lojistik/operasyon-isleri/satis", "L-01", LogisticsPermissionManifestV1.OperationsSalesWorkRead),
            (sales.RoutePath, sales.ScreenId, Assert.Single(sales.RequiredPermissions)));
        Assert.Equal(("/lojistik/operasyon-isleri/satinalma", "L-09", LogisticsPermissionManifestV1.OperationsPurchaseWorkRead),
            (purchase.RoutePath, purchase.ScreenId, Assert.Single(purchase.RequiredPermissions)));
        Assert.Null(sales.ParentCapabilityId);
        Assert.Null(purchase.ParentCapabilityId);
    }

    [Fact]
    public async Task Without_a_resolved_company_the_progress_reader_refuses_before_any_connection()
    {
        await using var context = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, new CompanyContext());
        var reader = new LogisticsOrderLineProgressReader(context, new CompanyContext());

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync([SalesLine]));
    }

    [Theory]
    [InlineData("6.000", "6")]
    [InlineData("0", "0")]
    [InlineData("0.000", "0")]
    [InlineData("2.250", "2.25")]
    [InlineData("1000", "1000")]
    public void A_total_is_shown_in_its_canonical_exact_text(string stored, string shown) =>
        Assert.Equal(shown, LogisticsOperationsWorkService.Text(decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture)));

    private static (int Status, string Code) Code(LogisticsInstructionServiceException exception) => (exception.Status, exception.Code);

    private static (ServiceLifetime, Type?) Registration(IServiceCollection services, Type serviceType)
    {
        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
        return (registration.Lifetime, registration.ImplementationType);
    }

    // ---- fakes ----------------------------------------------------------------------------------

    private sealed class World
    {
        public FixedUser User { get; } = new();
        public RecordingAuthority Authority { get; init; } = new();
        public FakeSalesLines SalesLines { get; init; } = new();
        public FakePurchaseLines PurchaseLines { get; init; } = new();
        public FakeProgress Progress { get; init; } = new();
        public FakeAccounts Accounts { get; init; } = new();
        public LogisticsOperationsWorkBindings Bindings { get; set; } = new(SalesPermission, PurchasePermission);

        public LogisticsOperationsWorkService Service()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new LogisticsOperationsWorkService(company, User, Authority, SalesLines, PurchaseLines, Progress, Accounts, Bindings);
        }
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class RecordingAuthority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }
        public List<string> Permissions { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Permissions.Add(permission);
            return Task.FromResult(new PermissionScope(permission != Denied, true, new HashSet<string>(["ROLE-A"], StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class FakeSalesLines : ISalesOrderLineReferencePortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; private set; }
        public int LastPageSize { get; private set; }
        public Action? OnRead { get; set; }

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPageSize = pageSize;
            OnRead?.Invoke();
            var company = Fault == "other-company" ? Guid.NewGuid() : Company;
            var first = new SalesApprovedLineReferenceV1(company, SalesOrder, "SENTETIK SO-0001", SalesLine, 1, Customer, CustomerAccount, Product, "7",
                "GDC-001", "Sentetik rulo", KgUnit, "kg", "12.500", new DateOnly(2026, 9, 25), 0);
            var second = first with { LineId = Fault == "duplicate-line" ? SalesLine : SecondSalesLine, LineNo = 2, QuantityText = "3", RequestedDeliveryDate = null };
            return Fault switch
            {
                "throws" => Task.FromException<SalesApprovedLinePageV1>(new InvalidOperationException("synthetic outage")),
                "bad-cursor" => Task.FromException<SalesApprovedLinePageV1>(new ArgumentException("synthetic cursor")),
                _ => Task.FromResult(new SalesApprovedLinePageV1([first, second], "SENTETIK-sales-next")),
            };
        }

        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The operations work list never asks for one line.");
    }

    private sealed class FakePurchaseLines : IPurchaseOpenLineReadPortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; private set; }
        public int LastPageSize { get; private set; }
        public string? LastCursor { get; private set; }

        public Task<PurchaseOpenLinePageV1> ListOpenApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPageSize = pageSize;
            LastCursor = cursor;
            return Fault switch
            {
                "throws" => Task.FromException<PurchaseOpenLinePageV1>(new InvalidOperationException("synthetic outage")),
                "bad-cursor" => Task.FromException<PurchaseOpenLinePageV1>(new ArgumentException("synthetic cursor")),
                _ => Task.FromResult(new PurchaseOpenLinePageV1(
                    [
                        new PurchaseOpenLineReferenceV1(Fault == "other-company" ? Guid.NewGuid() : Company, PurchaseOrder, new DateOnly(2026, 9, 19),
                            PurchaseLine, 1, "General", Supplier, SupplierAccount, Product, "7", KgUnit, "100",
                            Fault == "nothing-remaining" ? "100" : Fault == "negative-accepted" ? "-40" : "40",
                            Fault == "nothing-remaining" ? "0" : "60"),
                    ],
                    null)),
            };
        }
    }

    private sealed class FakeProgress : ILogisticsOrderLineProgressReader
    {
        public string Fault { get; set; } = "";
        public int Calls { get; private set; }
        public Guid[] LastIds { get; private set; } = [];

        public Task<IReadOnlyList<LogisticsOrderLineProgress>> ReadAsync(IReadOnlyCollection<Guid> orderLineIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastIds = [.. orderLineIds];
            IReadOnlyList<LogisticsOrderLineProgress> answer = Fault switch
            {
                "other-unit" => [new LogisticsOrderLineProgress(SalesLine, Guid.NewGuid(), 10m, 0m, 0m, 0m)],
                "foreign-line" => [new LogisticsOrderLineProgress(Guid.NewGuid(), KgUnit, 10m, 0m, 0m, 0m)],
                "delivered-above-shipped" => [new LogisticsOrderLineProgress(SalesLine, KgUnit, 10m, 8m, 4m, 5m)],
                _ => [new LogisticsOrderLineProgress(SalesLine, KgUnit, 10.000m, 8.500m, 4.000m, 2.250m)],
            };
            return Fault == "throws"
                ? Task.FromException<IReadOnlyList<LogisticsOrderLineProgress>>(new InvalidOperationException("synthetic outage"))
                : Task.FromResult(answer);
        }
    }

    private sealed class FakeAccounts : ILogisticsPartyAccountPortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; private set; }

        public Task<LogisticsPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Fault switch
            {
                "not-found" => new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.NotFound, null),
                "denied" => new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Denied, null),
                _ => new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Found, new LogisticsPartyAccountV1(partyId, companyAccountId,
                    Company, true, true, "3", "2", partyId == Supplier ? "Sentetik Tedarikçi Anonim Şirketi" : "Sentetik Müşteri Anonim Şirketi",
                    partyId == Supplier ? "Sentetik Tedarikçi" : "Sentetik Müşteri")),
            });
        }
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

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => new EmptySection(key);
    }

    private sealed class EmptySection(string key) : IConfigurationSection
    {
        public string? this[string key1]
        {
            get => null;
            set => throw new NotSupportedException();
        }

        public string Key => key;
        public string Path => key;
        public string? Value { get; set; }
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key1) => new EmptySection(key1);
    }
}
