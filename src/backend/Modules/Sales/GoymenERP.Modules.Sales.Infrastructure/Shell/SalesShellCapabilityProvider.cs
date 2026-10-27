using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.Shell;

/// <summary>
/// Publishes the three Sales draft screens of #260 (S-02 list, S-03 create, S-04 detail) — and, since shell contract 1.8,
/// the six S-01 request / quote pool screens of UI-20 with their controls — to the
/// shell — PUBLISHED (S03-c): every declaration is <c>ImplementationAvailable: true</c> since the
/// draft API (#321) landed; the registry never publishes a staged declaration, so the browser suite
/// <c>s03</c> is the acceptance of this flip, not its precondition (the Product #316 precedent; the
/// Sales documents carry no activation envelope rule). A declaration grants nothing: the screens open
/// only for an actor holding the permission, and the grant is a human gate. Company permissions,
/// the Counterparty way: the list and the detail share
/// <see cref="SalesPermissionManifestV1.DraftsRead"/>, the create screen needs
/// <see cref="SalesPermissionManifestV1.DraftsCreate"/>; selecting a product on it still needs the
/// Product owner's narrow selection read, measured by that owner. The ids and routes are the
/// frontend lane's (<c>src/sales/capabilities.ts</c>); a contract test pins them to that file.
/// The central registry requires registered names, so the host registers this provider only after
/// the central catalogue knows them, together with its own accepted revision (one module = one row
/// in the optional providers and one normal-host authority).
/// </summary>
public sealed class SalesShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "SALES";
    public const string OwnerContractVersion = "1.8";

    public const string ListCapabilityId = "sales.drafts.list";
    public const string ListRouteId = "sales.drafts.list";
    public const string ListRoutePath = "/satis/siparisler";
    public const string ListScreenId = "S-02";
    public const string CreateCapabilityId = "sales.drafts.create";
    public const string CreateRouteId = "sales.drafts.create";
    public const string CreateRoutePath = "/satis/siparisler/yeni";
    public const string CreateScreenId = "S-03";
    public const string DetailCapabilityId = "sales.drafts.detail";
    public const string DetailRouteId = "sales.drafts.detail";
    public const string DetailRoutePath = "/satis/siparisler/:draftId";
    public const string DetailScreenId = "S-04";

    /// <summary>
    /// SALES-A1: the approve / reject control on the S-04 detail (the Finance bank-account approval
    /// pattern): the detail's route and access plus <see cref="SalesPermissionManifestV1.OrdersApprove"/>.
    /// A control, not a route (<c>ParentCapabilityId</c> = the detail). The submit button needs no
    /// control of its own: it follows the create capability the shell already publishes.
    /// </summary>
    public const string ApproveCapabilityId = "sales.orders.approve";

    /// <summary>K3: the separate commercial-revision control on S-04; create and approve never confer it.</summary>
    public const string PriceWriteCapabilityId = "sales.orders.price.write";

    /// <summary>AT-021: reasoned below-floor approval control; price write and order approval never confer it.</summary>
    public const string PriceFloorExceptionApproveCapabilityId = "sales.orders.price-floor-exception.approve";

    /// <summary>DEC-SAL-003: the delivery-schedule update CONTROL of the S-04 detail (read + the schedule-write name).</summary>
    public const string DeliveryScheduleWriteCapabilityId = "sales.orders.delivery-schedule.write";

    /// <summary>
    /// DEC-SAL-005: the cancel CONTROL of the S-04 detail (read + the cancel name) — the impact preview and the cancellation of
    /// an approved order. Create, approve, price and schedule never confer it (shell contract 1.3).
    /// </summary>
    public const string CancelCapabilityId = "sales.orders.cancel";

    /// <summary>
    /// DEC-SAL-002: the quantity-revision CONTROL of the S-04 detail (read + the quantity-write name). Create, approve, price,
    /// schedule and cancel never confer it (shell contract 1.4).
    /// </summary>
    public const string QuantityWriteCapabilityId = "sales.orders.quantity.write";

    /// <summary>Q9: records customer acceptance of the current approved revision; a child control of S-04.</summary>
    public const string CustomerAcceptanceRecordCapabilityId = "sales.orders.customer-acceptance.record";

    /// <summary>
    /// No 30 / SAT-D12: the force-close CONTROL of the S-04 detail (read + the force-close name) — the impact preview and the
    /// force-closure of an approved order. Create, approve, price, schedule, quantity and cancel never confer it (shell contract 1.7).
    /// </summary>
    public const string ForceCloseCapabilityId = "sales.orders.force-close";

    /// <summary>
    /// UI-20 / S-01 "Satış Talebi / Teklif Havuzu" (shell contract 1.8): the sales request screens — list, create, detail —
    /// and the single-approver decision control of the detail (SAT-D22). Each screen is its own grant, the Sales draft way:
    /// the list and the detail need <see cref="SalesPermissionManifestV1.RequestsRead"/>, the create screen
    /// <see cref="SalesPermissionManifestV1.RequestsWrite"/>, the decision the read plus
    /// <see cref="SalesPermissionManifestV1.RequestsApprove"/>. No draft or order name confers any of them.
    /// </summary>
    public const string RequestListCapabilityId = "sales.requests.list";
    public const string RequestListRouteId = "sales.requests.list";
    public const string RequestListRoutePath = "/satis/talepler";
    public const string RequestListScreenId = "S-01";
    public const string RequestCreateCapabilityId = "sales.requests.create";
    public const string RequestCreateRouteId = "sales.requests.create";
    public const string RequestCreateRoutePath = "/satis/talepler/yeni";
    public const string RequestCreateScreenId = "S-01-NEW";
    public const string RequestDetailCapabilityId = "sales.requests.detail";
    public const string RequestDetailRouteId = "sales.requests.detail";
    public const string RequestDetailRoutePath = "/satis/talepler/:requestId";
    public const string RequestDetailScreenId = "S-01-DETAIL";
    public const string RequestApproveCapabilityId = "sales.requests.approve";

    /// <summary>
    /// UI-20 / S-01 (shell contract 1.8): the sales quote screens — list, create, detail — and two controls of the detail:
    /// the revision and loss control (the read plus <see cref="SalesPermissionManifestV1.QuotesWrite"/>; SAT-D26: the quote
    /// write name alone writes its price) and the "Siparişe dönüştür" control (the read, the quote write name AND
    /// <see cref="SalesPermissionManifestV1.DraftsCreate"/>, exactly the two names the conversion command measures; a priced
    /// quote additionally meets the order's own price-write fence on the server, which the control does not stand in for).
    /// </summary>
    public const string QuoteListCapabilityId = "sales.quotes.list";
    public const string QuoteListRouteId = "sales.quotes.list";
    public const string QuoteListRoutePath = "/satis/teklifler";
    public const string QuoteListScreenId = "S-01-QUOTES";
    public const string QuoteCreateCapabilityId = "sales.quotes.create";
    public const string QuoteCreateRouteId = "sales.quotes.create";
    public const string QuoteCreateRoutePath = "/satis/teklifler/yeni";
    public const string QuoteCreateScreenId = "S-01-QUOTE-NEW";
    public const string QuoteDetailCapabilityId = "sales.quotes.detail";
    public const string QuoteDetailRouteId = "sales.quotes.detail";
    public const string QuoteDetailRoutePath = "/satis/teklifler/:quoteId";
    public const string QuoteDetailScreenId = "S-01-QUOTE-DETAIL";
    public const string QuoteWriteCapabilityId = "sales.quotes.write";
    public const string QuoteConvertCapabilityId = "sales.quotes.convert";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var detail = new ShellCapabilityDeclaration(
            DetailCapabilityId,
            DetailRouteId,
            DetailRoutePath,
            DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsRead],
            ImplementationAvailable: true);
        return
        [
        new ShellCapabilityDeclaration(
            ListCapabilityId,
            ListRouteId,
            ListRoutePath,
            ListScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsRead],
            ImplementationAvailable: true),
        new ShellCapabilityDeclaration(
            CreateCapabilityId,
            CreateRouteId,
            CreateRoutePath,
            CreateScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsCreate],
            ImplementationAvailable: true),
        detail,
        // SALES-A1: the decision is a control of the detail: exactly the detail's route, screen, precedence,
        // context and scope (the registry requires a control to keep its parent's), plus the approve name.
        // The command re-authorizes the company on the server on every send.
        detail with
        {
            CapabilityId = ApproveCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove],
        },
        detail with
        {
            CapabilityId = PriceWriteCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceWrite],
        },
        detail with
        {
            CapabilityId = PriceFloorExceptionApproveCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove],
        },
        // DEC-SAL-003: the planner's delivery-schedule update is a control of the detail, like the price revision.
        detail with
        {
            CapabilityId = DeliveryScheduleWriteCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersDeliveryScheduleWrite],
        },
        // DEC-SAL-005: the impact preview and the cancellation are a control of the detail, like the schedule update.
        detail with
        {
            CapabilityId = CancelCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel],
        },
        // DEC-SAL-002: the quantity revision is a control of the detail, like the price revision.
        detail with
        {
            CapabilityId = QuantityWriteCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite],
        },
        detail with
        {
            CapabilityId = CustomerAcceptanceRecordCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord],
        },
        // No 30: the force-close preview and command are a control of the detail, exactly like the cancellation.
        detail with
        {
            CapabilityId = ForceCloseCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersForceClose],
        },
        .. RequestDeclarations(),
        .. QuoteDeclarations(),
        ];
    }

    private static ShellCapabilityDeclaration[] RequestDeclarations()
    {
        var detail = new ShellCapabilityDeclaration(
            RequestDetailCapabilityId,
            RequestDetailRouteId,
            RequestDetailRoutePath,
            RequestDetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.RequestsRead],
            ImplementationAvailable: true);
        return
        [
            new ShellCapabilityDeclaration(
                RequestListCapabilityId,
                RequestListRouteId,
                RequestListRoutePath,
                RequestListScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SalesPermissionManifestV1.RequestsRead],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                RequestCreateCapabilityId,
                RequestCreateRouteId,
                RequestCreateRoutePath,
                RequestCreateScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SalesPermissionManifestV1.RequestsWrite],
                ImplementationAvailable: true),
            detail,
            // SAT-D22: the single-approver decision (approve / reject) is a control of the request detail, the order approve way.
            detail with
            {
                CapabilityId = RequestApproveCapabilityId,
                ParentCapabilityId = RequestDetailCapabilityId,
                RequiredPermissions = [SalesPermissionManifestV1.RequestsRead, SalesPermissionManifestV1.RequestsApprove],
            },
        ];
    }

    private static ShellCapabilityDeclaration[] QuoteDeclarations()
    {
        var detail = new ShellCapabilityDeclaration(
            QuoteDetailCapabilityId,
            QuoteDetailRouteId,
            QuoteDetailRoutePath,
            QuoteDetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.QuotesRead],
            ImplementationAvailable: true);
        return
        [
            new ShellCapabilityDeclaration(
                QuoteListCapabilityId,
                QuoteListRouteId,
                QuoteListRoutePath,
                QuoteListScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SalesPermissionManifestV1.QuotesRead],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                QuoteCreateCapabilityId,
                QuoteCreateRouteId,
                QuoteCreateRoutePath,
                QuoteCreateScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SalesPermissionManifestV1.QuotesWrite],
                ImplementationAvailable: true),
            detail,
            // Revision and loss: the read plus the quote's own write name.
            detail with
            {
                CapabilityId = QuoteWriteCapabilityId,
                ParentCapabilityId = QuoteDetailCapabilityId,
                RequiredPermissions = [SalesPermissionManifestV1.QuotesRead, SalesPermissionManifestV1.QuotesWrite],
            },
            // Conversion to one order: the read plus the two names the command measures — neither confers the other.
            detail with
            {
                CapabilityId = QuoteConvertCapabilityId,
                ParentCapabilityId = QuoteDetailCapabilityId,
                RequiredPermissions =
                [
                    SalesPermissionManifestV1.QuotesRead,
                    SalesPermissionManifestV1.QuotesWrite,
                    SalesPermissionManifestV1.DraftsCreate,
                ],
            },
        ];
    }
}
