using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Sales.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.S04;

/// <summary>
/// The `s04` seed (SİPARİŞ, SALES-A1 user-flow acceptance + SALES-A2a references): ONE Preparing sales order
/// draft with two lines in GDC, written through the gated Sales fixture writer (W1). E1 also materializes one
/// fixed Product-owned sales candidate through Product's gated fixture port; the browser searches that candidate
/// through the real reference API and creates one separate draft through the real Sales command. Every approval
/// transition (submit / approve / reject) happens only through the real API + UI in the browser suite, never here.
/// <para>
/// SALES-A2a (Business 5745076917 item 2): a SECOND draft names REAL Cari references. A real party and its
/// GDC account are opened through the real counterparty endpoint (CARİ's shared seed) as the canonical
/// <c>group-bootstrap-admin</c>, its contact information (one delivery address, one contact person) is
/// written through the REAL contact-information command, and the second draft is written by the gated
/// writer with those ids and the owner's versions. The browser proves those real selects and their reopen;
/// E1 proves a separate real create and reopen with the isolated Product-owned candidate.
/// </para>
/// <para>
/// Decision C: the writer writes no DraftAttempt and no AuditEvent, and the fixture reads are not audited;
/// the SUITE_FIXTURE line's audit delta is exactly the two real Cari commands (party create, contact
/// information update). The draft ids and every synthetic id are fixed, so a re-run is a no-op: the Cari
/// party replays under its step key, the contact information is written only when the fixed address id is
/// not there yet, the drafts are accepted as already present.
/// </para>
/// <para>
/// DEC-SAL-003 (the delivery-date update): a SEPARATE one-line Preparing draft for the disposable planner, the ONLY
/// holder of <c>sales.orders.delivery-schedule.write</c> in this run database (user decision 2026-09-27: no real user or
/// role receives the name). The browser suite submits and approves it through the real API before the planner moves its
/// dates in the form; nothing here submits, approves or reschedules it. The read-backs below fail the seed by name when
/// the planner's control or the schedule history route is not served (central: the ErpPermissions registration, the
/// Sales shell provider 1.2 control and its accepted host authority, and the s04 override's planner role).
/// </para>
/// </summary>
public sealed class S04BrowserSeed : IBrowserSuiteFixture
{
    public const string PreparerActor = "s04-preparer";
    public const string ApproverActor = "s04-approver";
    public const string ReaderActor = "s04-reader";
    public const string SupportCreatorActor = "s04-support-creator";
    public const string OrdinaryCreatorActor = "s04-ordinary-creator";

    /// <summary>DEC-SAL-003: the disposable planner (sales.drafts.read + sales.orders.delivery-schedule.write only).</summary>
    public const string PlannerActor = "s04-planner";

    // Identity's authenticated actor is the canonical user id resolved from the development subject.
    // Fixture CreatedBy must store that same identity, never the external subject/header value.
    private static readonly Guid PreparerIdentityId = Guid.Parse("b2000000-0000-4000-8000-000000000401");
    private static readonly Guid SupportCreatorIdentityId = Guid.Parse("b2000000-0000-4000-8000-000000000405");
    private static readonly Guid OrdinaryCreatorIdentityId = Guid.Parse("b2000000-0000-4000-8000-000000000406");

    /// <summary>The canonical bootstrap administrator, whose seeded SYSTEM_ADMINISTRATOR role opens Cari parties (the fin01 way).</summary>
    public const string CariActor = "group-bootstrap-admin";

    /// <summary>
    /// ASSUMPTION-SALESA2A-1: the seed-only fixture actor that writes the customer's contact information through the real
    /// Cari command (counterparty.parties.read + counterparty.parties.update.basic; no canonical actor holds update.basic).
    /// Never a persona the browser signs in as, and not a seed output.
    /// </summary>
    public const string CariWriterActor = "s04-cari";

    /// <summary>The GDC company of the built-in organization seed (the context company of the web profile).</summary>
    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid DraftId = Guid.Parse("d1000000-0000-4000-8000-000000000401");
    public const string DocumentNo = "SENTETIK browser-suite:s04 SO-0001";

    /// <summary>SALES-A2a: the second draft, on the REAL Cari party with its REAL delivery address and contact.</summary>
    public static readonly Guid DeliveryDraftId = Guid.Parse("d1000000-0000-4000-8000-000000000402");
    public const string DeliveryDocumentNo = "SENTETIK browser-suite:s04 SO-0002";

    // D2: separate records keep every persona and terminal-state assertion independent. Their creators are
    // real disposable Identity actors; another actor submits, so creator and submitter cannot be confused.
    public static readonly Guid SupportDraftId = Guid.Parse("d1000000-0000-4000-8000-000000000410");
    public static readonly Guid OrdinaryApproveDraftId = Guid.Parse("d1000000-0000-4000-8000-000000000411");
    public static readonly Guid OrdinaryRejectDraftId = Guid.Parse("d1000000-0000-4000-8000-000000000412");
    public static readonly Guid WithdrawalDraftId = Guid.Parse("d1000000-0000-4000-8000-000000000413");

    /// <summary>DEC-SAL-003: the planner's own order (one line, no slice), written Preparing by the preparer.</summary>
    public static readonly Guid ScheduleDraftId = Guid.Parse("d1000000-0000-4000-8000-000000000414");

    // Synthetic owner identities: nothing in Cari, Settings or Product answers them; the draft stores snapshots.
    private static readonly Guid PartyId = Guid.Parse("d2000000-0000-4000-8000-000000000401");
    private static readonly Guid CompanyAccountId = Guid.Parse("d3000000-0000-4000-8000-000000000401");
    private static readonly Guid CurrencyId = Guid.Parse("d4000000-0000-4000-8000-000000000401");
    private static readonly Guid MoneyProfileId = Guid.Parse("d5000000-0000-4000-8000-000000000401");
    private static readonly Guid KgUnitId = Guid.Parse("d6000000-0000-4000-8000-000000000401");
    private static readonly Guid KgProfileId = Guid.Parse("d6100000-0000-4000-8000-000000000401");
    private static readonly Guid PieceUnitId = Guid.Parse("d7000000-0000-4000-8000-000000000401");
    private static readonly Guid PieceProfileId = Guid.Parse("d7100000-0000-4000-8000-000000000401");
    private static readonly Guid CoilProductId = Guid.Parse("d8000000-0000-4000-8000-000000000401");
    private static readonly Guid SheetProductId = Guid.Parse("d8000000-0000-4000-8000-000000000402");
    private static readonly Guid CoilLineId = Guid.Parse("d9000000-0000-4000-8000-000000000401");
    private static readonly Guid SheetLineId = Guid.Parse("d9000000-0000-4000-8000-000000000402");

    // SALES-A2a: the second draft's line (synthetic product, as above) and the REAL Cari reference ids the seed
    // chooses (the contact-information command takes client-chosen ids), fixed so that a re-run is a no-op.
    private static readonly Guid DeliveryProductId = Guid.Parse("d8000000-0000-4000-8000-000000000403");
    private static readonly Guid DeliveryLineId = Guid.Parse("d9000000-0000-4000-8000-000000000403");
    private static readonly Guid SupportProductId = Guid.Parse("d8000000-0000-4000-8000-000000000410");
    private static readonly Guid SupportLineId = Guid.Parse("d9000000-0000-4000-8000-000000000410");
    private static readonly Guid OrdinaryApproveProductId = Guid.Parse("d8000000-0000-4000-8000-000000000411");
    private static readonly Guid OrdinaryApproveLineId = Guid.Parse("d9000000-0000-4000-8000-000000000411");
    private static readonly Guid OrdinaryRejectProductId = Guid.Parse("d8000000-0000-4000-8000-000000000412");
    private static readonly Guid OrdinaryRejectLineId = Guid.Parse("d9000000-0000-4000-8000-000000000412");
    private static readonly Guid WithdrawalProductId = Guid.Parse("d8000000-0000-4000-8000-000000000413");
    private static readonly Guid WithdrawalLineId = Guid.Parse("d9000000-0000-4000-8000-000000000413");
    private static readonly Guid ScheduleProductId = Guid.Parse("d8000000-0000-4000-8000-000000000414");
    private static readonly Guid ScheduleLineId = Guid.Parse("d9000000-0000-4000-8000-000000000414");
    private static readonly Guid DeliveryAddressId = Guid.Parse("da000000-0000-4000-8000-000000000401");
    private static readonly Guid DeliveryContactId = Guid.Parse("db000000-0000-4000-8000-000000000401");
    private static readonly Guid AllocationOpeningId = Guid.Parse("dc000000-0000-4000-8000-000000000401");
    private static readonly Guid AllocationOpeningLineId = Guid.Parse("dd000000-0000-4000-8000-000000000401");
    private static readonly Guid AllocationLabelId = Guid.Parse("de000000-0000-4000-8000-000000000401");
    private static readonly Guid AllocationLotId = Guid.Parse("df000000-0000-4000-8000-000000000401");

    // The GDC TRY money profile Settings actually answers for sales.order-draft.v1 on the business date
    // (ApprovedMoneyProfileCatalog.GdcTurkishLiraV1 / SettingsCurrencyIdentitiesV1.TurkishLira, Sinan Ö2 2026-09-18):
    // the second draft names the REAL currency so the currencies read shows its code on reopen.
    private static readonly Guid TurkishLiraCurrencyId = Guid.Parse("9e60dcf0-f4bd-5bca-b464-f9a2db98e31a");
    private static readonly Guid TurkishLiraProfileId = Guid.Parse("7f836492-a54f-512f-a8f4-5f556b3f0f9d");
    private static readonly DateOnly TurkishLiraEffectiveFrom = new(2026, 9, 18);
    private const string TurkishLiraCode = "TRY";

    private static readonly DateOnly BusinessDate = new(2026, 9, 18);
    private static readonly DateOnly EffectiveFrom = new(2026, 9, 1);
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private const string DraftsPath = "/api/v1/sales/drafts";
    private const string ContactInformationReferencePath = "/api/v1/sales/references/contact-information";
    private const string CurrenciesReferencePath = "/api/v1/sales/references/currencies";
    private const string ProductsReferencePath = "/api/v1/sales/references/products";
    private const string OpeningCountsPath = "/api/v1/inventory/opening-counts";
    private const string OrganizationLocationsPath = "/api/v1/organization/locations?page=1&pageSize=100";
    private const string PhysicalAllocationControl = "inventory.physical-allocation.write";
    private const string AllocationLabelCode = "SYNS04-ALLOC-LABEL-1";
    private const string ApproveControl = "sales.orders.approve";
    private const string DeliveryScheduleControl = "sales.orders.delivery-schedule.write";
    private const int CustomerRole = 1;
    private const string DeliveryPurpose = "delivery";
    private const string AddressLine1 = "Sanayi Cad. 1";
    private const string AddressCity = "Kocaeli";
    private const string AddressCountry = "TR";
    private const string ContactName = "Ayşe Yükleme";

    public string Suite => "s04";

    public async Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken)
    {
        // Product's technical registry is a prerequisite and must finish in its own scope. The next
        // scope owns fresh Product persistence services and materializes the one fixed Active+Sellable candidate.
        await using (var registryScope = context.Services.CreateAsyncScope())
        {
            await registryScope.ServiceProvider.GetRequiredService<C01E2eProductTechnicalCardFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        ProductStockDisplayQualifiedFixtureOutput productCandidate;
        await using (var productScope = context.Services.CreateAsyncScope())
        {
            productCandidate = await productScope.ServiceProvider
                .GetRequiredService<C01E2eProductStockDisplayQualifiedFixtureWriter>()
                .EnsureAsync(cancellationToken);
        }

        var locations = await context.GetJsonAsync(CariActor, CompanyGdc, OrganizationLocationsPath, cancellationToken);
        var allocationLocationId = locations.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("isActive").GetBoolean() && !item.GetProperty("isExternal").GetBoolean())
            .OrderBy(item => item.GetProperty("code").GetString(), StringComparer.Ordinal)
            .Select(item => item.GetProperty("id").GetGuid())
            .FirstOrDefault();
        if (allocationLocationId == Guid.Empty)
        {
            throw new InvalidOperationException("The s04 allocation prerequisite has no active non-external GDC location.");
        }
        var allocationLabelVersion = await EnsureAllocationLabelAsync(
            context, productCandidate, allocationLocationId, cancellationToken);

        // SALES-A2a: the REAL customer, opened and described through the real Cari API (audited commands).
        var customer = await CariBrowserSeed.OpenPartyWithAccountAsync(
            context, CariActor, CompanyGdc, "cari-party", "Teslimat müşterisi", CustomerRole, cancellationToken);
        var (addressVersion, contactVersion) = await EnsureContactInformationAsync(context, customer, cancellationToken);

        await using (var scope = context.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<C01E2eSalesDraftFixtureWriter>();
            var outcome = await writer.EnsureAsync(
                new SalesDraftFixtureRequest(
                    DraftId,
                    CompanyGdc,
                    DocumentNo,
                    context.Label(string.Empty).TrimEnd(),
                    BusinessDate,
                    new SalesDraftFixtureOwners(PartyId, CompanyAccountId, CurrencyId, MoneyProfileId, EffectiveFrom),
                    [
                        new SalesDraftFixtureLine(CoilLineId, productCandidate.ProductId, "S04-ALLOC", context.Label("tahsis rulosu"),
                            productCandidate.UnitId, productCandidate.ProfileId, "kg", "weight", IsKilogram: true, Scale: 3, "1.250", 1.250m,
                            ProductVersion: productCandidate.ProductVersion),
                        new SalesDraftFixtureLine(SheetLineId, SheetProductId, "S04-SHEET", context.Label("levha 2 × 1000 × 2000"),
                            PieceUnitId, PieceProfileId, "adet", "count", IsKilogram: false, Scale: 0, "3", 3m),
                    ],
                    PreparerIdentityId.ToString("D"),
                    context.IdempotencyKey("draft"),
                    At),
                cancellationToken);
            if (outcome.DraftId != DraftId)
            {
                throw new InvalidOperationException("The Sales fixture writer answered another draft id.");
            }

            // SALES-A2a: the second draft carries the REAL party/account, the REAL TRY currency and the REAL address and
            // contact ids with the owner's versions; only its product line is synthetic (E1).
            var delivery = await writer.EnsureAsync(
                new SalesDraftFixtureRequest(
                    DeliveryDraftId,
                    CompanyGdc,
                    DeliveryDocumentNo,
                    context.Label(string.Empty).TrimEnd(),
                    BusinessDate,
                    new SalesDraftFixtureOwners(customer.PartyId, customer.AccountId, TurkishLiraCurrencyId, TurkishLiraProfileId,
                        TurkishLiraEffectiveFrom,
                        DeliveryAddress: new SalesDraftFixtureOwnerReference(DeliveryAddressId, addressVersion),
                        Contact: new SalesDraftFixtureOwnerReference(DeliveryContactId, contactVersion)),
                    [
                        new SalesDraftFixtureLine(DeliveryLineId, DeliveryProductId, "S04-COIL-2", context.Label("rulo 1,50 × 1000"),
                            KgUnitId, KgProfileId, "kg", "weight", IsKilogram: true, Scale: 3, "2.500", 2.500m),
                    ],
                    PreparerIdentityId.ToString("D"),
                    context.IdempotencyKey("delivery-draft"),
                    At),
                cancellationToken);
            if (delivery.DraftId != DeliveryDraftId)
            {
                throw new InvalidOperationException("The Sales fixture writer answered another id for the delivery draft.");
            }

            await EnsureD2DraftAsync(writer, context, SupportDraftId, "SO-D2-SUPPORT", SupportCreatorIdentityId.ToString("D"),
                SupportProductId, SupportLineId, "support-creator-draft", cancellationToken);
            await EnsureD2DraftAsync(writer, context, OrdinaryApproveDraftId, "SO-D2-ORD-APPROVE", OrdinaryCreatorIdentityId.ToString("D"),
                OrdinaryApproveProductId, OrdinaryApproveLineId, "ordinary-approve-draft", cancellationToken);
            await EnsureD2DraftAsync(writer, context, OrdinaryRejectDraftId, "SO-D2-ORD-REJECT", OrdinaryCreatorIdentityId.ToString("D"),
                OrdinaryRejectProductId, OrdinaryRejectLineId, "ordinary-reject-draft", cancellationToken);
            await EnsureD2DraftAsync(writer, context, WithdrawalDraftId, "SO-D2-WITHDRAW", OrdinaryCreatorIdentityId.ToString("D"),
                WithdrawalProductId, WithdrawalLineId, "withdrawal-draft", cancellationToken);
            // DEC-SAL-003: the same one-line shape (the D2 helper), created by the preparer who later submits it.
            await EnsureD2DraftAsync(writer, context, ScheduleDraftId, "SO-DS-SCHEDULE", PreparerIdentityId.ToString("D"),
                ScheduleProductId, ScheduleLineId, "schedule-draft", cancellationToken);
        }

        // Read back through the real host, as each actor: the fixture is only done when the product actually
        // answers with it — the preparer's detail, the reader's list, and the approve control published to the
        // approver only. A grant or a mount that does not resolve fails the run here, not in the browser.
        var detail = await context.GetJsonAsync(PreparerActor, CompanyGdc, $"{DraftsPath}/{DraftId:D}", cancellationToken);
        var draft = detail.GetProperty("draft");
        var allocationLine = detail.GetProperty("lines").EnumerateArray().SingleOrDefault(line =>
            line.GetProperty("lineId").GetGuid() == CoilLineId);
        if (draft.GetProperty("id").GetGuid() != DraftId
            || draft.GetProperty("approvalState").GetString() != "Preparing"
            || draft.GetProperty("lineCount").GetInt32() != 2
            || detail.GetProperty("lines").GetArrayLength() != 2
            || allocationLine.ValueKind != JsonValueKind.Object
            || allocationLine.GetProperty("productVersion").GetString() != productCandidate.ProductVersion)
        {
            throw new InvalidOperationException(
                "The seeded draft is not a Preparing two-line draft with the exact qualified Product version on the real detail route.");
        }

        var list = await context.GetJsonAsync(ReaderActor, CompanyGdc, $"{DraftsPath}?approvalState=Preparing", cancellationToken);
        if (!list.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == DraftId))
        {
            throw new InvalidOperationException("The reader does not see the seeded draft in the real list.");
        }

        var approver = await context.GetJsonAsync(ApproverActor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken);
        if (!Capabilities(approver).Contains(ApproveControl, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The approve control is not published to the approver.");
        }

        var reader = await context.GetJsonAsync(ReaderActor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken);
        if (Capabilities(reader).Contains(ApproveControl, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The approve control must not be published to the reader.");
        }
        var preparer = await context.GetJsonAsync(PreparerActor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken);
        if (!Capabilities(preparer).Contains(PhysicalAllocationControl, StringComparer.Ordinal)
            || Capabilities(reader).Contains(PhysicalAllocationControl, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The physical-allocation control must be published only to the authorized s04 preparer.");
        }

        // D2 persona preflight through the real API: both actors own their fixture record and hold the same
        // ordinary command capabilities, but only the actor whose fresh Identity roles include SALES_SUPPORT
        // carries the server-computed own-decision restriction. The browser never infers either relationship.
        var support = await context.GetJsonAsync(SupportCreatorActor, CompanyGdc, $"{DraftsPath}/{SupportDraftId:D}", cancellationToken);
        var ordinary = await context.GetJsonAsync(OrdinaryCreatorActor, CompanyGdc, $"{DraftsPath}/{OrdinaryApproveDraftId:D}", cancellationToken);
        if (!support.GetProperty("isOwnDraft").GetBoolean() || !support.GetProperty("isOwnDecisionRestricted").GetBoolean()
            || !ordinary.GetProperty("isOwnDraft").GetBoolean() || ordinary.GetProperty("isOwnDecisionRestricted").GetBoolean())
        {
            throw new InvalidOperationException("The real Sales detail did not preserve the D2 SalesSupport creator distinction.");
        }
        foreach (var actor in new[] { SupportCreatorActor, OrdinaryCreatorActor })
        {
            var capabilities = await context.GetJsonAsync(actor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken);
            if (!Capabilities(capabilities).Contains(ApproveControl, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"The approve control is not published to D2 actor {actor}.");
            }
        }

        // DEC-SAL-003 preflight through the real API: the planner alone gets the delivery-schedule control (a control of
        // the detail, published beside the list and detail its draft read opens) and nothing else of Sales; it reads its
        // order as Preparing with one line, and the schedule history route answers schedule 0 only.
        var planner = Capabilities(await context.GetJsonAsync(PlannerActor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken));
        if (!planner.Contains(DeliveryScheduleControl, StringComparer.Ordinal)
            || !planner.Contains("sales.drafts.detail", StringComparer.Ordinal)
            || planner.Contains(ApproveControl, StringComparer.Ordinal)
            || planner.Contains("sales.drafts.create", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "DEC-SAL-003: the delivery-schedule control is not published to the s04 planner alone (central: ErpPermissions, "
                + "the Sales shell provider 1.2 control with its host authority, and the s04 override's planner role).");
        }

        foreach (var actor in new[] { PreparerActor, ApproverActor, ReaderActor })
        {
            var capabilities = await context.GetJsonAsync(actor, CompanyGdc, "/api/v1/shell/capabilities", cancellationToken);
            if (Capabilities(capabilities).Contains(DeliveryScheduleControl, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"DEC-SAL-003: the delivery-schedule control must not be published to {actor}.");
            }
        }

        var scheduleDetail = await context.GetJsonAsync(PlannerActor, CompanyGdc, $"{DraftsPath}/{ScheduleDraftId:D}", cancellationToken);
        var scheduleDraft = scheduleDetail.GetProperty("draft");
        if (scheduleDraft.GetProperty("id").GetGuid() != ScheduleDraftId
            || scheduleDraft.GetProperty("approvalState").GetString() != "Preparing"
            || scheduleDetail.GetProperty("lines").GetArrayLength() != 1)
        {
            throw new InvalidOperationException("DEC-SAL-003: the planner's order is not a Preparing one-line draft on the real detail route.");
        }

        var schedules = await context.GetJsonAsync(
            PlannerActor, CompanyGdc, $"{DraftsPath}/{ScheduleDraftId:D}/delivery-schedules", cancellationToken);
        if (schedules.GetProperty("items").GetArrayLength() != 1
            || schedules.GetProperty("items")[0].GetProperty("scheduleNo").GetInt32() != 0)
        {
            throw new InvalidOperationException("DEC-SAL-003: the planner's order must start with schedule 0 only on the real history route.");
        }

        // SALES-A2a read-backs, as the preparer (sales.drafts.create): the two reference LIST reads answer the REAL
        // address, contact and currency, and the second draft's detail carries the two references.
        // A withheld read (503: Program.cs's handler-factory rows / the ISalesPartyContactInformationPortV1 registration not
        // adopted by CARİ yet) fails the seed here, by name — a seed never passes on a withheld read.
        var references = await ReferenceAsync(context,
            $"{ContactInformationReferencePath}?partyId={customer.PartyId:D}&companyAccountId={customer.AccountId:D}",
            "the contact-information reference read (central: Program.cs CreateSalesReferenceReadService passes ISalesPartyContactInformationPortV1; "
            + "the port is registered to the Cari adapter)", cancellationToken);
        var address = references.GetProperty("addresses").EnumerateArray().SingleOrDefault(item =>
            item.GetProperty("id").GetGuid() == DeliveryAddressId && item.GetProperty("version").GetString() == addressVersion);
        var contact = references.GetProperty("contacts").EnumerateArray().SingleOrDefault(item =>
            item.GetProperty("id").GetGuid() == DeliveryContactId && item.GetProperty("version").GetString() == contactVersion);
        if (references.GetProperty("companyId").GetGuid() != CompanyGdc
            || address.ValueKind != JsonValueKind.Object || contact.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The Sales contact-information reference does not list the seeded address and contact at their versions.");
        }

        var addressText = address.GetProperty("text").GetString() ?? string.Empty;
        var contactName = contact.GetProperty("displayName").GetString() ?? string.Empty;
        if (!addressText.Contains(AddressLine1, StringComparison.Ordinal) || !contactName.Contains(ContactName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Sales contact-information reference does not present the seeded address text and contact name.");
        }

        var currencies = await ReferenceAsync(context,
            $"{CurrenciesReferencePath}?effectiveOn={BusinessDate:yyyy-MM-dd}",
            "the currencies reference read (central: Program.cs CreateSalesReferenceReadService passes ISettingsMoneyCandidateReader)",
            cancellationToken);
        if (currencies.GetProperty("companyId").GetGuid() != CompanyGdc
            || !currencies.GetProperty("currencies").EnumerateArray().Any(currency =>
                currency.GetProperty("currencyId").GetGuid() == TurkishLiraCurrencyId
                && currency.GetProperty("code").GetString() == TurkishLiraCode))
        {
            throw new InvalidOperationException("The Sales currencies reference does not list TRY for GDC on the business date.");
        }

        var products = await ReferenceAsync(context,
            $"{ProductsReferencePath}?effectiveOn={BusinessDate:yyyy-MM-dd}&q={Uri.EscapeDataString(productCandidate.Code)}",
            "the Product E1 candidate read (central endpoint plus Product operational-selection owner)",
            cancellationToken);
        var product = products.GetProperty("items").EnumerateArray().SingleOrDefault(item =>
            item.GetProperty("productId").GetGuid() == productCandidate.ProductId);
        var unit = product.ValueKind == JsonValueKind.Object
            ? product.GetProperty("orderUnits").EnumerateArray().SingleOrDefault(item =>
                item.GetProperty("unitId").GetGuid() == productCandidate.UnitId)
            : default;
        if (product.ValueKind != JsonValueKind.Object
            || product.GetProperty("productVersion").GetString() != productCandidate.ProductVersion
            || product.GetProperty("code").GetString() != productCandidate.Code
            || product.GetProperty("name").GetString() != productCandidate.Name
            || unit.ValueKind != JsonValueKind.Object
            || unit.GetProperty("profileId").GetGuid() != productCandidate.ProfileId
            || unit.GetProperty("profileVersion").GetString() != productCandidate.ProfileVersion)
        {
            throw new InvalidOperationException("The Product E1 route did not return the exact persisted s04 candidate tuple.");
        }

        var deliveryDetail = await context.GetJsonAsync(PreparerActor, CompanyGdc, $"{DraftsPath}/{DeliveryDraftId:D}", cancellationToken);
        var deliveryDraft = deliveryDetail.GetProperty("draft");
        if (deliveryDraft.GetProperty("id").GetGuid() != DeliveryDraftId
            || deliveryDraft.GetProperty("partyId").GetGuid() != customer.PartyId
            || deliveryDraft.GetProperty("companyAccountId").GetGuid() != customer.AccountId
            || deliveryDraft.GetProperty("currencyId").GetGuid() != TurkishLiraCurrencyId
            || deliveryDetail.GetProperty("deliveryAddress").GetProperty("id").GetGuid() != DeliveryAddressId
            || deliveryDetail.GetProperty("deliveryAddress").GetProperty("expectedVersion").GetString() != addressVersion
            || deliveryDetail.GetProperty("contact").GetProperty("id").GetGuid() != DeliveryContactId
            || deliveryDetail.GetProperty("contact").GetProperty("expectedVersion").GetString() != contactVersion)
        {
            throw new InvalidOperationException("The delivery draft's real detail does not carry the seeded party, currency, address and contact.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["preparerActor"] = PreparerActor,
            ["approverActor"] = ApproverActor,
            ["readerActor"] = ReaderActor,
            ["supportCreatorActor"] = SupportCreatorActor,
            ["ordinaryCreatorActor"] = OrdinaryCreatorActor,
            ["plannerActor"] = PlannerActor,
            ["companyId"] = CompanyGdc.ToString("D"),
            ["draftId"] = DraftId.ToString("D"),
            ["supportDraftId"] = SupportDraftId.ToString("D"),
            ["ordinaryApproveDraftId"] = OrdinaryApproveDraftId.ToString("D"),
            ["ordinaryRejectDraftId"] = OrdinaryRejectDraftId.ToString("D"),
            ["withdrawalDraftId"] = WithdrawalDraftId.ToString("D"),
            ["scheduleDraftId"] = ScheduleDraftId.ToString("D"),
            ["realPartyId"] = customer.PartyId.ToString("D"),
            ["realCompanyAccountId"] = customer.AccountId.ToString("D"),
            ["deliveryAddressId"] = DeliveryAddressId.ToString("D"),
            ["deliveryContactId"] = DeliveryContactId.ToString("D"),
            ["deliveryDraftId"] = DeliveryDraftId.ToString("D"),
            ["productId"] = productCandidate.ProductId.ToString("D"),
            ["productVersion"] = productCandidate.ProductVersion,
            ["productUnitId"] = productCandidate.UnitId.ToString("D"),
            ["productUnitProfileId"] = productCandidate.ProfileId.ToString("D"),
            ["productUnitProfileVersion"] = productCandidate.ProfileVersion,
            ["productCode"] = productCandidate.Code,
            ["productName"] = productCandidate.Name,
            ["allocationLineId"] = CoilLineId.ToString("D"),
            ["allocationLabelId"] = AllocationLabelId.ToString("D"),
            ["allocationLabelVersion"] = allocationLabelVersion,
        };
    }

    private static async Task<string> EnsureAllocationLabelAsync(
        BrowserSuiteFixtureContext context,
        ProductStockDisplayQualifiedFixtureOutput product,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        using var client = context.Api(PreparerActor, CompanyGdc);
        using var request = new HttpRequestMessage(HttpMethod.Post, OpeningCountsPath)
        {
            Content = JsonContent.Create(new
            {
                countId = AllocationOpeningId,
                locationId,
                countedOn = BusinessDate,
                sheetReference = context.Label("allocation opening"),
                lines = new[]
                {
                    new
                    {
                        lineId = AllocationOpeningLineId,
                        labelId = AllocationLabelId,
                        labelCode = AllocationLabelCode,
                        lotId = AllocationLotId,
                        heatNumber = (string?)null,
                        coilNumber = (string?)null,
                        productId = product.ProductId,
                        productVersion = product.ProductVersion,
                        quantityKg = "10",
                    },
                },
            }),
        };
        request.Headers.Add(BrowserSuiteFixtureContext.IdempotencyHeader, context.IdempotencyKey("allocation-opening"));
        using var response = await client.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode is not 200 and not 201)
        {
            throw new InvalidOperationException($"The real opening command refused the s04 allocation label: {(int)response.StatusCode}.");
        }
        var count = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var line = count.GetProperty("lines").EnumerateArray().Single();
        if (count.GetProperty("countId").GetGuid() != AllocationOpeningId
            || line.GetProperty("labelId").GetGuid() != AllocationLabelId
            || line.GetProperty("productId").GetGuid() != product.ProductId
            || line.GetProperty("quantityKg").GetString() != "10")
        {
            throw new InvalidOperationException("The real opening command returned another allocation prerequisite.");
        }

        var label = await context.GetJsonAsync(
            PreparerActor, CompanyGdc, $"/api/v1/inventory/labels/{AllocationLabelId:D}/detail", cancellationToken);
        if (label.GetProperty("identity").GetProperty("productId").GetGuid() != product.ProductId
            || label.GetProperty("balances").GetProperty("physicalKg").GetString() != "10")
        {
            throw new InvalidOperationException("The s04 allocation label does not reopen from Inventory server truth.");
        }
        return label.GetProperty("labelVersion").GetString()
            ?? throw new InvalidOperationException("The s04 allocation label version is missing.");
    }

    private static async Task EnsureD2DraftAsync(
        C01E2eSalesDraftFixtureWriter writer,
        BrowserSuiteFixtureContext context,
        Guid draftId,
        string documentSuffix,
        string creator,
        Guid productId,
        Guid lineId,
        string step,
        CancellationToken cancellationToken)
    {
        var outcome = await writer.EnsureAsync(
            new SalesDraftFixtureRequest(
                draftId,
                CompanyGdc,
                context.Label(documentSuffix),
                context.Label(string.Empty).TrimEnd(),
                BusinessDate,
                new SalesDraftFixtureOwners(PartyId, CompanyAccountId, CurrencyId, MoneyProfileId, EffectiveFrom),
                [
                    new SalesDraftFixtureLine(lineId, productId, $"S04-{documentSuffix}", context.Label(documentSuffix),
                        PieceUnitId, PieceProfileId, "adet", "count", IsKilogram: false, Scale: 0, "1", 1m),
                ],
                creator,
                context.IdempotencyKey(step),
                At),
            cancellationToken);
        if (outcome.DraftId != draftId)
        {
            throw new InvalidOperationException($"The Sales fixture writer answered another id for {step}.");
        }
    }

    /// <summary>A Sales reference read as the preparer; a non-success answer names the read and its central row.</summary>
    private static async Task<JsonElement> ReferenceAsync(
        BrowserSuiteFixtureContext context,
        string path,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.GetJsonAsync(PreparerActor, CompanyGdc, path, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"SALES-A2a: {description} did not answer the preparer: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The REAL customer's contact information through the real Cari command (PUT, expected version, step key): one
    /// delivery address and one contact person, both under the seed's fixed ids. Written only when the fixed address
    /// is not there yet (a re-run reads the owner's current versions and writes nothing). Returns the owner's
    /// versions of the address and the contact — what the second draft stores as its expected versions.
    /// </summary>
    private static async Task<(string AddressVersion, string ContactVersion)> EnsureContactInformationAsync(
        BrowserSuiteFixtureContext context,
        CariBrowserAccount customer,
        CancellationToken cancellationToken)
    {
        var path = $"{CariBrowserSeed.CounterpartiesPath}/{customer.PartyId:D}/contact-information";
        var current = await context.GetJsonAsync(CariWriterActor, CompanyGdc, path, cancellationToken);
        if (!current.GetProperty("information").GetProperty("addresses").EnumerateArray()
                .Any(address => address.GetProperty("id").GetGuid() == DeliveryAddressId))
        {
            var expectedVersion = current.GetProperty("version").GetString()
                ?? throw new InvalidOperationException("The contact information version is missing.");
            using var client = context.Api(CariWriterActor, CompanyGdc);
            using var request = new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = JsonContent.Create(new
                {
                    expectedVersion,
                    changes = new
                    {
                        addresses = new[]
                        {
                            new
                            {
                                id = DeliveryAddressId,
                                operation = "upsert",
                                purpose = DeliveryPurpose,
                                countryCode = AddressCountry,
                                line1 = context.Label(AddressLine1),
                                city = AddressCity,
                            },
                        },
                        contacts = new[]
                        {
                            new
                            {
                                id = DeliveryContactId,
                                operation = "upsert",
                                displayName = context.Label(ContactName),
                                purpose = DeliveryPurpose,
                            },
                        },
                    },
                }),
            };
            request.Headers.Add(BrowserSuiteFixtureContext.IdempotencyHeader, context.IdempotencyKey("contact-information"));
            // The Cari command routes require the negotiated client contract (409 client_contract_required without it — the
            // second s04 run, 35480444505); the same header the shared seed sends on the party create.
            request.Headers.Add(CariBrowserSeed.ClientContractHeader, CariBrowserSeed.ClientContractVersion);
            using var response = await client.SendAsync(request, cancellationToken);
            current = await context.GetJsonAsync(CariWriterActor, CompanyGdc, path, cancellationToken);
        }

        var information = current.GetProperty("information");
        var stored = information.GetProperty("addresses").EnumerateArray()
            .SingleOrDefault(address => address.GetProperty("id").GetGuid() == DeliveryAddressId);
        var person = information.GetProperty("contacts").EnumerateArray()
            .SingleOrDefault(contact => contact.GetProperty("id").GetGuid() == DeliveryContactId);
        if (stored.ValueKind != JsonValueKind.Object || person.ValueKind != JsonValueKind.Object
            || !stored.GetProperty("isActive").GetBoolean() || !person.GetProperty("isActive").GetBoolean()
            || current.GetProperty("accountId").GetGuid() != customer.AccountId
            || current.GetProperty("companyId").GetGuid() != CompanyGdc)
        {
            throw new InvalidOperationException("The real Cari contact information does not hold the seeded active address and contact for the GDC account.");
        }

        return (
            stored.GetProperty("version").GetString() ?? throw new InvalidOperationException("The address version is missing."),
            person.GetProperty("version").GetString() ?? throw new InvalidOperationException("The contact version is missing."));
    }

    private static string[] Capabilities(JsonElement snapshot) =>
        [.. snapshot.GetProperty("capabilities").EnumerateArray().Select(capability => capability.GetString()!)];
}
