using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Shell;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// The published Sales shell provider: three screens plus the approve, commercial-revision, floor-exception, delivery-schedule and cancel controls, the frontend
/// lane's ids and routes (<c>src/sales/capabilities.ts</c> read from the repository, so a drift is red
/// here), the Sales permission names, and <c>ImplementationAvailable: true</c> (S03-c; the registry
/// never publishes a staged declaration, so the browser suite s03 accepts the flip rather than
/// preceding it).
/// </summary>
public sealed class SalesShellCapabilityProviderContractTests
{
    private const string FrontendCapabilities = "src/frontend/goymen-erp-web/src/sales/capabilities.ts";

    [Fact]
    public void The_three_screens_and_the_two_controls_are_declared_with_the_frontends_ids_and_routes()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var provider = new SalesShellCapabilityProvider();
        var declarations = provider.GetDeclarations();

        // DEC-SAL-003 (1.2): the delivery-schedule control joins the approve and commercial-revision controls;
        // DEC-SAL-005 (1.3): cancel; DEC-SAL-002 (1.4): quantity revision; Q9 (1.5): customer acceptance;
        // AT-021 (1.6): reasoned floor-exception approval remains a separate child control; No 30 (1.7): force-close.
        // UI-20 (1.8): the S-01 request and quote screens with their decision, write and conversion controls.
        // UI-60 / UI-61 (1.9): the S-07 dispatch list and the S-08 invoice candidates.
        // SAT-D24 / SAT-D44 merged over 1.9 (1.10): the expired-quote conversion approval control of the quote detail.
        // UI-80 (1.11): the Sales reports screen.
        // API-04 (1.12, merged after 1.11): the header-revision control of the detail.
        Assert.Equal(("SALES", "1.12"), (provider.Owner, provider.ContractVersion));
        Assert.Equal(25, declarations.Count);
        foreach (var declaration in declarations)
        {
            Assert.Contains($"'{declaration.CapabilityId}'", source);
            Assert.Contains($"'{declaration.RoutePath}'", source);
            Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
            Assert.Null(declaration.GlobalProductOperation);
            // S03-c: published to the shell (the grant stays a human gate; an ungranted actor still sees nothing).
            Assert.True(declaration.ImplementationAvailable);
        }

        var list = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.ListCapabilityId);
        Assert.Equal(("/satis/siparisler", ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead),
            (list.RoutePath, list.RoutePrecedence, list.ContextRequirement));
        Assert.Equal([SalesPermissionManifestV1.DraftsRead], list.RequiredPermissions);
        Assert.Null(list.ParentCapabilityId);

        var create = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.CreateCapabilityId);
        Assert.Equal(("/satis/siparisler/yeni", ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany),
            (create.RoutePath, create.RoutePrecedence, create.ContextRequirement));
        Assert.Equal([SalesPermissionManifestV1.DraftsCreate], create.RequiredPermissions);
        Assert.Null(create.ParentCapabilityId);

        var detail = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.DetailCapabilityId);
        Assert.Equal(("/satis/siparisler/:draftId", ShellCapabilityRoutePrecedence.ParameterizedAfterStatic), (detail.RoutePath, detail.RoutePrecedence));
        Assert.Equal([SalesPermissionManifestV1.DraftsRead], detail.RequiredPermissions);
        Assert.Null(detail.ParentCapabilityId);

        // SALES-A1: the decision is a control of the detail (the Finance bank-account approval pattern):
        // exactly the detail's route, screen, precedence, context and scope (the registry's control rule),
        // the read plus the approve name — nothing of its own.
        var approve = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.ApproveCapabilityId);
        Assert.Equal("sales.orders.approve", approve.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (approve.RouteId, approve.RoutePath, approve.ScreenId, approve.RoutePrecedence, approve.ContextRequirement, approve.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, approve.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove], approve.RequiredPermissions);

        var price = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.PriceWriteCapabilityId);
        Assert.Equal("sales.orders.price.write", price.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (price.RouteId, price.RoutePath, price.ScreenId, price.RoutePrecedence, price.ContextRequirement, price.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, price.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceWrite], price.RequiredPermissions);

        var floorApproval = Assert.Single(declarations,
            item => item.CapabilityId == SalesShellCapabilityProvider.PriceFloorExceptionApproveCapabilityId);
        Assert.Equal("sales.orders.price-floor-exception.approve", floorApproval.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (floorApproval.RouteId, floorApproval.RoutePath, floorApproval.ScreenId, floorApproval.RoutePrecedence,
                floorApproval.ContextRequirement, floorApproval.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, floorApproval.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove],
            floorApproval.RequiredPermissions);

        // DEC-SAL-003: the planner's delivery-date update is a control of the detail too — the read plus its own name.
        var schedule = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.DeliveryScheduleWriteCapabilityId);
        Assert.Equal("sales.orders.delivery-schedule.write", schedule.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (schedule.RouteId, schedule.RoutePath, schedule.ScreenId, schedule.RoutePrecedence, schedule.ContextRequirement, schedule.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, schedule.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersDeliveryScheduleWrite], schedule.RequiredPermissions);

        // DEC-SAL-005: the impact preview and the cancellation are a control of the detail — the read plus the cancel name.
        var cancel = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.CancelCapabilityId);
        Assert.Equal("sales.orders.cancel", cancel.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (cancel.RouteId, cancel.RoutePath, cancel.ScreenId, cancel.RoutePrecedence, cancel.ContextRequirement, cancel.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, cancel.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel], cancel.RequiredPermissions);

        // DEC-SAL-002: the quantity revision is a control of the detail — the read plus its own name.
        var quantity = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.QuantityWriteCapabilityId);
        Assert.Equal("sales.orders.quantity.write", quantity.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (quantity.RouteId, quantity.RoutePath, quantity.ScreenId, quantity.RoutePrecedence, quantity.ContextRequirement, quantity.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, quantity.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite], quantity.RequiredPermissions);

        // API-04: the header revision is a control of the detail — the read plus its own name.
        var header = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.HeaderWriteCapabilityId);
        Assert.Equal("sales.orders.header.write", header.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (header.RouteId, header.RoutePath, header.ScreenId, header.RoutePrecedence, header.ContextRequirement, header.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, header.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersHeaderWrite], header.RequiredPermissions);

        // No 30 / SAT-D12: the force-close preview and command are a control of the detail — the read plus its own name, never
        // the cancel name.
        var forceClose = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.ForceCloseCapabilityId);
        Assert.Equal("sales.orders.force-close", forceClose.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (forceClose.RouteId, forceClose.RoutePath, forceClose.ScreenId, forceClose.RoutePrecedence, forceClose.ContextRequirement, forceClose.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, forceClose.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersForceClose], forceClose.RequiredPermissions);

        var customerAcceptance = Assert.Single(declarations,
            item => item.CapabilityId == SalesShellCapabilityProvider.CustomerAcceptanceRecordCapabilityId);
        Assert.Equal("sales.orders.customer-acceptance.record", customerAcceptance.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (customerAcceptance.RouteId, customerAcceptance.RoutePath, customerAcceptance.ScreenId, customerAcceptance.RoutePrecedence,
                customerAcceptance.ContextRequirement, customerAcceptance.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, customerAcceptance.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord],
            customerAcceptance.RequiredPermissions);
    }

    [Fact]
    public void The_S01_pool_screens_and_their_controls_are_declared_with_the_frontends_ids_routes_and_the_real_names()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var declarations = new SalesShellCapabilityProvider().GetDeclarations();

        // Routed screens: each its own grant, list/detail on the read name, create on the write name (UI-20, plan §3.2 S-01).
        var routes = new (string Id, string Path, string Screen, ShellCapabilityRoutePrecedence Precedence,
            ShellCapabilityContextRequirement Context, string[] Permissions)[]
        {
            ("sales.requests.list", "/satis/talepler", "S-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead, [SalesPermissionManifestV1.RequestsRead]),
            ("sales.requests.create", "/satis/talepler/yeni", "S-01-NEW", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, [SalesPermissionManifestV1.RequestsWrite]),
            ("sales.requests.detail", "/satis/talepler/:requestId", "S-01-DETAIL", ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead, [SalesPermissionManifestV1.RequestsRead]),
            ("sales.quotes.list", "/satis/teklifler", "S-01-QUOTES", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead, [SalesPermissionManifestV1.QuotesRead]),
            ("sales.quotes.create", "/satis/teklifler/yeni", "S-01-QUOTE-NEW", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, [SalesPermissionManifestV1.QuotesWrite]),
            ("sales.quotes.detail", "/satis/teklifler/:quoteId", "S-01-QUOTE-DETAIL", ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead, [SalesPermissionManifestV1.QuotesRead]),
        };
        foreach (var expected in routes)
        {
            var declaration = Assert.Single(declarations, item => item.CapabilityId == expected.Id);
            Assert.Equal((expected.Id, expected.Path, expected.Screen, expected.Precedence, expected.Context),
                (declaration.RouteId, declaration.RoutePath, declaration.ScreenId, declaration.RoutePrecedence, declaration.ContextRequirement));
            Assert.Equal(expected.Permissions, declaration.RequiredPermissions);
            Assert.Null(declaration.ParentCapabilityId);
            Assert.Contains($"'{expected.Id}'", source);
            Assert.Contains($"'{expected.Path}'", source);
        }

        // Controls: exactly the parent's route, screen, precedence, context and scope, the parent's read plus their own names.
        var controls = new (string Id, string Parent, string[] Permissions)[]
        {
            // SAT-D22: the single-approver decision; the write name never confers it.
            ("sales.requests.approve", "sales.requests.detail",
                [SalesPermissionManifestV1.RequestsRead, SalesPermissionManifestV1.RequestsApprove]),
            // Revision and loss (SAT-D26: the quote write name alone writes the quote's price).
            ("sales.quotes.write", "sales.quotes.detail",
                [SalesPermissionManifestV1.QuotesRead, SalesPermissionManifestV1.QuotesWrite]),
            // Conversion: the two names the command measures — the quote write name and the order create name.
            ("sales.quotes.convert", "sales.quotes.detail",
                [SalesPermissionManifestV1.QuotesRead, SalesPermissionManifestV1.QuotesWrite, SalesPermissionManifestV1.DraftsCreate]),
            // SAT-D24 / SAT-D44: the expired-quote approval — the read plus its own name; the quote write name does not confer it.
            ("sales.quotes.expired-conversion.approve", "sales.quotes.detail",
                [SalesPermissionManifestV1.QuotesRead, SalesPermissionManifestV1.QuotesExpiredConversionApprove]),
        };
        foreach (var expected in controls)
        {
            var control = Assert.Single(declarations, item => item.CapabilityId == expected.Id);
            var parent = Assert.Single(declarations, item => item.CapabilityId == expected.Parent);
            Assert.Equal(expected.Parent, control.ParentCapabilityId);
            Assert.Equal((parent.RouteId, parent.RoutePath, parent.ScreenId, parent.RoutePrecedence, parent.ContextRequirement, parent.ScopeRequirement),
                (control.RouteId, control.RoutePath, control.ScreenId, control.RoutePrecedence, control.ContextRequirement, control.ScopeRequirement));
            Assert.Equal(expected.Permissions, control.RequiredPermissions);
            Assert.True(control.ImplementationAvailable);
            Assert.Contains($"'{expected.Id}'", source);
        }

        // No pool declaration asks for an order name it does not use, and no draft/order declaration gained a pool name.
        var poolIds = routes.Select(item => item.Id).Concat(controls.Select(item => item.Id)).ToHashSet(StringComparer.Ordinal);
        var poolNames = new[]
        {
            SalesPermissionManifestV1.RequestsRead, SalesPermissionManifestV1.RequestsWrite, SalesPermissionManifestV1.RequestsApprove,
            SalesPermissionManifestV1.QuotesRead, SalesPermissionManifestV1.QuotesWrite,
            SalesPermissionManifestV1.QuotesExpiredConversionApprove,
        };
        foreach (var declaration in declarations.Where(item => !poolIds.Contains(item.CapabilityId)))
        {
            Assert.DoesNotContain(declaration.RequiredPermissions, permission => poolNames.Contains(permission, StringComparer.Ordinal));
        }

        Assert.All(declarations.Where(item => poolIds.Contains(item.CapabilityId)), declaration =>
            Assert.All(declaration.RequiredPermissions, permission =>
                Assert.True(poolNames.Contains(permission, StringComparer.Ordinal)
                    || (declaration.CapabilityId == "sales.quotes.convert" && permission == SalesPermissionManifestV1.DraftsCreate))));
    }

    [Fact]
    public void The_S07_dispatch_list_and_the_S08_invoice_candidates_are_routed_lists_on_their_own_names()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var declarations = new SalesShellCapabilityProvider().GetDeclarations();

        // API-30 reads under the order read name (plan §3.3); API-31 under its own sales.invoices.read. Neither has a control.
        var expected = new (string Id, string Path, string Screen, string[] Permissions)[]
        {
            ("sales.dispatches.list", "/satis/sevkiyatlar", "S-07", [SalesPermissionManifestV1.DraftsRead]),
            ("sales.invoice-candidates.list", "/satis/faturaya-hazir", "S-08", [SalesPermissionManifestV1.InvoicesRead]),
        };
        foreach (var item in expected)
        {
            var declaration = Assert.Single(declarations, candidate => candidate.CapabilityId == item.Id);
            Assert.Equal((item.Id, item.Path, item.Screen, ShellCapabilityRoutePrecedence.Static,
                    ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead, ShellCapabilityScopeRequirement.CompanyWide),
                (declaration.RouteId, declaration.RoutePath, declaration.ScreenId, declaration.RoutePrecedence,
                    declaration.ContextRequirement, declaration.ScopeRequirement));
            Assert.Equal(item.Permissions, declaration.RequiredPermissions);
            Assert.Null(declaration.ParentCapabilityId);
            Assert.True(declaration.ImplementationAvailable);
            Assert.DoesNotContain(declarations, candidate => candidate.ParentCapabilityId == item.Id);
            Assert.Contains($"'{item.Id}'", source);
            Assert.Contains($"'{item.Path}'", source);
        }

        // The invoice read name is S-08's alone: no other declaration asks for it.
        Assert.Single(declarations, candidate => candidate.RequiredPermissions.Contains(SalesPermissionManifestV1.InvoicesRead));
    }

    [Fact]
    public void The_UI80_reports_screen_is_a_routed_screen_on_its_own_read_name_without_a_control()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var declarations = new SalesShellCapabilityProvider().GetDeclarations();

        // API-41 / API-42 read under their own sales.reports.read; the amounts are the server's price-read decision, not a control.
        var reports = Assert.Single(declarations, candidate => candidate.CapabilityId == "sales.reports.view");
        Assert.Equal(("sales.reports.view", "/satis/raporlar", "R-02-SALES", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead, ShellCapabilityScopeRequirement.CompanyWide),
            (reports.RouteId, reports.RoutePath, reports.ScreenId, reports.RoutePrecedence, reports.ContextRequirement,
                reports.ScopeRequirement));
        Assert.Equal([SalesPermissionManifestV1.ReportsRead], reports.RequiredPermissions);
        Assert.Null(reports.ParentCapabilityId);
        Assert.True(reports.ImplementationAvailable);
        Assert.DoesNotContain(declarations, candidate => candidate.ParentCapabilityId == reports.CapabilityId);
        Assert.Contains("'sales.reports.view'", source);
        Assert.Contains("'/satis/raporlar'", source);
        // The report read name is UI-80's alone: no other declaration asks for it.
        Assert.Single(declarations, candidate => candidate.RequiredPermissions.Contains(SalesPermissionManifestV1.ReportsRead));
    }

    [Fact]
    public void The_permission_names_are_the_manifests_and_spelled_as_the_card_decided()
    {
        Assert.Equal("sales.drafts.create", SalesPermissionManifestV1.DraftsCreate);
        Assert.Equal("sales.drafts.read", SalesPermissionManifestV1.DraftsRead);
        // SALES-A1 (Business 5737989917): the decision's own name, company-wide; its class and grant are the central writer's and a human's.
        Assert.Equal("sales.orders.approve", SalesPermissionManifestV1.OrdersApprove);
        // K3 (Sinan 5748258299): the SEPARATE price-write name; the create name does not confer it, and it confers no approval.
        Assert.Equal("sales.orders.price.write", SalesPermissionManifestV1.OrdersPriceWrite);
        // K3 P2 (Sinan 5748258299; D5 5749046401): the SEPARATE, NARROW price-read name — the draft read does not confer it,
        // and it confers nothing adjacent (no cost, margin, bank or Finance read).
        Assert.Equal("sales.orders.price.read", SalesPermissionManifestV1.OrdersPriceRead);
        Assert.Equal("sales.orders.price-floor-exception.approve", SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove);
        // DEC-SAL-003: the SEPARATE planning/shipping name; the create, approve and price names do not confer it.
        Assert.Equal("sales.orders.delivery-schedule.write", SalesPermissionManifestV1.OrdersDeliveryScheduleWrite);
        // DEC-SAL-005: the SEPARATE cancel name; no other Sales name confers it and it confers none of them.
        Assert.Equal("sales.orders.cancel", SalesPermissionManifestV1.OrdersCancel);
        // DEC-SAL-002: the SEPARATE quantity-revision name; no other Sales name confers it and it confers no approval.
        Assert.Equal("sales.orders.quantity.write", SalesPermissionManifestV1.OrdersQuantityWrite);
        // API-04: the SEPARATE header-revision name (the plan's literal); no other Sales name confers it and it confers no approval.
        Assert.Equal("sales.orders.header.write", SalesPermissionManifestV1.OrdersHeaderWrite);
        Assert.Equal("sales.orders.customer-acceptance.record", SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord);
        // No 30: the SEPARATE force-close name, the decision's own literal, distinct from the cancel name.
        Assert.Equal("sales.orders.force-close", SalesPermissionManifestV1.OrdersForceClose);
        Assert.NotEqual(SalesPermissionManifestV1.OrdersCancel, SalesPermissionManifestV1.OrdersForceClose);
        // API-20 / API-21 (plan §3.2, S-01): the sales request's own three names; no draft or order name confers them.
        Assert.Equal("sales.requests.write", SalesPermissionManifestV1.RequestsWrite);
        Assert.Equal("sales.requests.read", SalesPermissionManifestV1.RequestsRead);
        Assert.Equal("sales.requests.approve", SalesPermissionManifestV1.RequestsApprove);
        // API-22…24: the sales quote's own two names; the conversion additionally needs the draft create name.
        Assert.Equal("sales.quotes.write", SalesPermissionManifestV1.QuotesWrite);
        Assert.Equal("sales.quotes.read", SalesPermissionManifestV1.QuotesRead);
        // SAT-D2 / API-12: the SEPARATE discount-exception approval name; price write, order approval and the floor approval do not confer it.
        Assert.Equal("sales.orders.discount-exception.approve", SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
        // SAT-D24 / SAT-D44: the SEPARATE expired-quote conversion approval name; the quote write and order names do not confer it.
        Assert.Equal("sales.quotes.expired-conversion.approve", SalesPermissionManifestV1.QuotesExpiredConversionApprove);
        // API-31 (plan §3.3, S-08): the invoice candidates' own read name; no order name confers it.
        Assert.Equal("sales.invoices.read", SalesPermissionManifestV1.InvoicesRead);
        // API-41 / API-42 (plan §3.4, UI-80): the reports' own read name; no order, quote, invoice or price name confers it.
        Assert.Equal("sales.reports.read", SalesPermissionManifestV1.ReportsRead);
        Assert.Equal((18, 21), (SalesPermissionManifestV1.Version, SalesPermissionManifestV1.Names.Count));
        var exactNames = new HashSet<string>(
            [
                SalesPermissionManifestV1.DraftsCreate,
                SalesPermissionManifestV1.DraftsRead,
                SalesPermissionManifestV1.OrdersApprove,
                SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord,
                SalesPermissionManifestV1.OrdersPriceWrite,
                SalesPermissionManifestV1.OrdersPriceRead,
                SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove,
                SalesPermissionManifestV1.OrdersDiscountExceptionApprove,
                SalesPermissionManifestV1.OrdersDeliveryScheduleWrite,
                SalesPermissionManifestV1.OrdersCancel,
                SalesPermissionManifestV1.OrdersForceClose,
                SalesPermissionManifestV1.OrdersQuantityWrite,
                SalesPermissionManifestV1.OrdersHeaderWrite,
                SalesPermissionManifestV1.RequestsWrite,
                SalesPermissionManifestV1.RequestsRead,
                SalesPermissionManifestV1.RequestsApprove,
                SalesPermissionManifestV1.QuotesWrite,
                SalesPermissionManifestV1.QuotesRead,
                SalesPermissionManifestV1.QuotesExpiredConversionApprove,
                SalesPermissionManifestV1.InvoicesRead,
                SalesPermissionManifestV1.ReportsRead,
            ],
            StringComparer.Ordinal);
        Assert.True(exactNames.SetEquals(SalesPermissionManifestV1.Names));
        Assert.Equal(new SalesDraftServiceBindings(),
            new SalesDraftServiceBindings(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove,
                SalesPermissionManifestV1.OrdersPriceWrite, SalesPermissionManifestV1.OrdersPriceRead, SalesPermissionManifestV1.OrdersDeliveryScheduleWrite,
                SalesPermissionManifestV1.OrdersCancel, SalesPermissionManifestV1.OrdersQuantityWrite,
                SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord, SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove,
                SalesPermissionManifestV1.OrdersForceClose,
                SalesPermissionManifestV1.RequestsWrite, SalesPermissionManifestV1.RequestsRead, SalesPermissionManifestV1.RequestsApprove,
                SalesPermissionManifestV1.QuotesWrite, SalesPermissionManifestV1.QuotesRead));
        Assert.Equal(new SalesDispatchReadBindings(),
            new SalesDispatchReadBindings(SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.InvoicesRead));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
