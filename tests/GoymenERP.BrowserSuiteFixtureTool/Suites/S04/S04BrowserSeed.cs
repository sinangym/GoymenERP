using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool.Shared;
using GoymenERP.Modules.Sales.Infrastructure.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.BrowserSuiteFixtureTool.Suites.S04;

/// <summary>
/// The `s04` seed (SİPARİŞ, SALES-A1 user-flow acceptance + SALES-A2a references): ONE Preparing sales order
/// draft with two lines in GDC, written through the gated Sales fixture writer (W1) — the positive create path
/// is still blocked (Product activation, GDC data), so the draft's owner snapshots are synthetic and no
/// Product, GDC or Cari data is read or written for it. Every approval transition (submit / approve / reject)
/// happens only through the real API + UI in the browser suite, never here.
/// <para>
/// SALES-A2a (Business 5745076917 item 2): a SECOND draft names REAL Cari references. A real party and its
/// GDC account are opened through the real counterparty endpoint (CARİ's shared seed) as the canonical
/// <c>group-bootstrap-admin</c>, its contact information (one delivery address, one contact person) is
/// written through the REAL contact-information command, and the second draft is written by the gated
/// writer with those ids and the owner's versions — exactly what a real create would store once the E1
/// product picker exists. The browser proves the selects (real options) and the reopen; the create WITH
/// references is proven on real SQL through the host, not in the browser, until E1.
/// </para>
/// <para>
/// Decision C: the writer writes no DraftAttempt and no AuditEvent, and the fixture reads are not audited;
/// the SUITE_FIXTURE line's audit delta is exactly the two real Cari commands (party create, contact
/// information update). The draft ids and every synthetic id are fixed, so a re-run is a no-op: the Cari
/// party replays under its step key, the contact information is written only when the fixed address id is
/// not there yet, the drafts are accepted as already present.
/// </para>
/// </summary>
public sealed class S04BrowserSeed : IBrowserSuiteFixture
{
    public const string PreparerActor = "s04-preparer";
    public const string ApproverActor = "s04-approver";
    public const string ReaderActor = "s04-reader";

    /// <summary>The canonical bootstrap administrator, whose seeded SYSTEM_ADMINISTRATOR role opens Cari parties (the fin01 way).</summary>
    public const string CariActor = "group-bootstrap-admin";

    /// <summary>The GDC company of the built-in organization seed (the context company of the web profile).</summary>
    public static readonly Guid CompanyGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid DraftId = Guid.Parse("d1000000-0000-4000-8000-000000000401");
    public const string DocumentNo = "SENTETIK browser-suite:s04 SO-0001";

    /// <summary>SALES-A2a: the second draft, on the REAL Cari party with its REAL delivery address and contact.</summary>
    public static readonly Guid DeliveryDraftId = Guid.Parse("d1000000-0000-4000-8000-000000000402");
    public const string DeliveryDocumentNo = "SENTETIK browser-suite:s04 SO-0002";

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
    private static readonly Guid DeliveryAddressId = Guid.Parse("da000000-0000-4000-8000-000000000401");
    private static readonly Guid DeliveryContactId = Guid.Parse("db000000-0000-4000-8000-000000000401");

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
    private const string ApproveControl = "sales.orders.approve";
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
                        new SalesDraftFixtureLine(CoilLineId, CoilProductId, "S04-COIL", context.Label("rulo 1,25 × 1240"),
                            KgUnitId, KgProfileId, "kg", "weight", IsKilogram: true, Scale: 3, "1.250", 1.250m),
                        new SalesDraftFixtureLine(SheetLineId, SheetProductId, "S04-SHEET", context.Label("levha 2 × 1000 × 2000"),
                            PieceUnitId, PieceProfileId, "adet", "count", IsKilogram: false, Scale: 0, "3", 3m),
                    ],
                    context.Label("seed"),
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
                    context.Label("seed"),
                    context.IdempotencyKey("delivery-draft"),
                    At),
                cancellationToken);
            if (delivery.DraftId != DeliveryDraftId)
            {
                throw new InvalidOperationException("The Sales fixture writer answered another id for the delivery draft.");
            }
        }

        // Read back through the real host, as each actor: the fixture is only done when the product actually
        // answers with it — the preparer's detail, the reader's list, and the approve control published to the
        // approver only. A grant or a mount that does not resolve fails the run here, not in the browser.
        var detail = await context.GetJsonAsync(PreparerActor, CompanyGdc, $"{DraftsPath}/{DraftId:D}", cancellationToken);
        var draft = detail.GetProperty("draft");
        if (draft.GetProperty("id").GetGuid() != DraftId
            || draft.GetProperty("approvalState").GetString() != "Preparing"
            || draft.GetProperty("lineCount").GetInt32() != 2
            || detail.GetProperty("lines").GetArrayLength() != 2)
        {
            throw new InvalidOperationException("The seeded draft is not a Preparing draft with two lines on the real detail route.");
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
            ["companyId"] = CompanyGdc.ToString("D"),
            ["draftId"] = DraftId.ToString("D"),
            ["realPartyId"] = customer.PartyId.ToString("D"),
            ["realCompanyAccountId"] = customer.AccountId.ToString("D"),
            ["deliveryAddressId"] = DeliveryAddressId.ToString("D"),
            ["deliveryContactId"] = DeliveryContactId.ToString("D"),
            ["deliveryDraftId"] = DeliveryDraftId.ToString("D"),
            ["deliveryAddressText"] = addressText,
            ["deliveryContactName"] = contactName,
            ["currencyCode"] = TurkishLiraCode,
        };
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
        var current = await context.GetJsonAsync(CariActor, CompanyGdc, path, cancellationToken);
        if (!current.GetProperty("information").GetProperty("addresses").EnumerateArray()
                .Any(address => address.GetProperty("id").GetGuid() == DeliveryAddressId))
        {
            var expectedVersion = current.GetProperty("version").GetString()
                ?? throw new InvalidOperationException("The contact information version is missing.");
            using var client = context.Api(CariActor, CompanyGdc);
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
            using var response = await client.SendAsync(request, cancellationToken);
            current = await context.GetJsonAsync(CariActor, CompanyGdc, path, cancellationToken);
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
