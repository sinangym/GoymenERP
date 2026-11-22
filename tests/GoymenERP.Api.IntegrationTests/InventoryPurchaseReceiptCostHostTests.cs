using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.UnitConversion;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK owner adapters only: these facts exercise the live cost bridge, not SQL, invoice admission or activation.</summary>
public sealed class InventoryPurchaseReceiptCostHostTests
{
    private static readonly Guid CompanyId = Guid.Parse("8c210000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompanyId = Guid.Parse("8c210000-0000-4000-8000-000000000002");
    private static readonly Guid OrderId = Guid.Parse("8c210000-0000-4000-8000-000000000003");
    private static readonly Guid LineId = Guid.Parse("8c210000-0000-4000-8000-000000000004");
    private static readonly Guid AcceptanceId = Guid.Parse("8c210000-0000-4000-8000-000000000005");
    private static readonly Guid ProductId = Guid.Parse("8c210000-0000-4000-8000-000000000006");
    private static readonly Guid OtherProductId = Guid.Parse("8c210000-0000-4000-8000-000000000007");
    private static readonly Guid PriceUnitId = Guid.Parse("8c210000-0000-4000-8000-000000000008");
    private static readonly Guid ProfileId = Guid.Parse("8c210000-0000-4000-8000-000000000009");
    private static readonly Guid RateRecordId = Guid.Parse("8c210000-0000-4000-8000-000000000010");
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Usd = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD");
    private static readonly Guid Eur = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("EUR");
    private static readonly DateTimeOffset ReceivedAt = new(2026, 11, 17, 22, 30, 0, TimeSpan.Zero);
    private static readonly InventoryPurchaseReceiptCostSelectionV1 Selection =
        new(CompanyId, AcceptanceId, OrderId, LineId, ProductId, "7", ReceivedAt);

    [Theory]
    [InlineData("receipt")]
    [InlineData("price")]
    [InlineData("invoice")]
    public async Task A_foreign_purchase_proof_is_refused_without_copying_its_payload_into_company_evidence(string section)
    {
        var world = new World();
        var foreign = PurchaseProof(6m, Lira, companyId: OtherCompanyId);
        world.Owner.Answer = section switch
        {
            "receipt" => foreign,
            "price" => world.Owner.Answer! with { Price = foreign.Price },
            "invoice" => world.Owner.Answer! with { InvoiceRate = foreign.InvoiceRate },
            _ => throw new InvalidOperationException("SENTETIK unknown proof section."),
        };

        var result = await world.Host().ReadAsync(Selection);

        AssertUnavailable(result, "purchase_receipt_cost_proof_unreadable");
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("proof").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("conversion").ValueKind);
        Assert.DoesNotContain(OtherCompanyId.ToString("D"), result.EvidenceJson, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(world.Product.Requests);
        Assert.Empty(world.Rates.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_foreign_product_conversion_is_refused_without_copying_its_payload_into_company_evidence(bool foreignCompany)
    {
        var world = new World();
        world.Product.Answer = world.Product.Answer with
        {
            Source = world.Product.Answer.Source! with
            {
                CompanyId = foreignCompany ? OtherCompanyId : CompanyId,
                ProductId = foreignCompany ? ProductId : OtherProductId,
                FormulaVersion = "SENTETIK-foreign-formula",
            },
        };

        var result = await world.Host().ReadAsync(Selection);

        AssertUnavailable(result, "purchase_receipt_unit_conversion_unreadable");
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        Assert.Equal(JsonValueKind.Object, evidence.RootElement.GetProperty("proof").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("conversion").ValueKind);
        Assert.DoesNotContain("SENTETIK-foreign-formula", result.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain((foreignCompany ? OtherCompanyId : OtherProductId).ToString("D"),
            result.EvidenceJson, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(world.Rates.Requests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_nonrepresentable_purchase_unit_cost_preserves_the_exact_rational_amount(int price)
    {
        var world = new World(price, kgPerPriceUnit: 3m, usdSellingRate: 1m);

        var result = await world.Host().ReadAsync(Selection);

        var ratio = new InventoryExactCostRatioV1(price.ToString(CultureInfo.InvariantCulture), "3");
        Assert.Equal(Lira, result.OriginalCurrencyId);
        Assert.Null(result.OriginalUnitCostPerKg);
        Assert.Null(result.TlUnitCostPerKg);
        Assert.Null(result.UsdUnitCostPerKg);
        Assert.Equal(ratio, result.OriginalUnitCostRatio);
        Assert.Equal(ratio, result.TlUnitCostRatio);
        Assert.Equal(ratio, result.UsdUnitCostRatio);
        Assert.Null(result.TlUnavailableReason);
        Assert.Null(result.UsdUnavailableReason);
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, result.Currency.Status);
        Assert.Single(world.Product.Requests);
        Assert.Single(world.Rates.Requests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_nonrepresentable_USD_division_preserves_the_exact_ratio_and_the_TL_scalar(int lira)
    {
        var world = new World(lira, kgPerPriceUnit: 1m, usdSellingRate: 3m);

        var result = await world.Host().ReadAsync(Selection);

        Assert.Equal((decimal)lira, result.OriginalUnitCostPerKg);
        Assert.Equal((decimal)lira, result.TlUnitCostPerKg);
        Assert.Null(result.TlUnavailableReason);
        Assert.Null(result.UsdUnitCostPerKg);
        Assert.Equal(new InventoryExactCostRatioV1(lira.ToString(CultureInfo.InvariantCulture), "3"),
            result.UsdUnitCostRatio);
        Assert.Null(result.UsdUnavailableReason);
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, result.Currency.Status);
        Assert.Single(world.Rates.Requests);
    }

    [Fact]
    public async Task Exact_price_unit_and_USD_rate_results_keep_the_received_business_day_and_immutable_owner_evidence()
    {
        var world = new World(price: 6m, kgPerPriceUnit: 2m, usdSellingRate: 4m);

        var result = await world.Host().ReadAsync(Selection);

        Assert.Equal(((Guid?)Lira, (decimal?)3m, (decimal?)3m, (decimal?)0.75m),
            (result.OriginalCurrencyId, result.OriginalUnitCostPerKg, result.TlUnitCostPerKg, result.UsdUnitCostPerKg));
        Assert.Null(result.TlUnavailableReason);
        Assert.Null(result.UsdUnavailableReason);
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, result.Currency.Status);
        Assert.Equal(result.SourceVersion, result.Currency.SourceVersion);
        var conversion = Assert.Single(world.Product.Requests);
        Assert.Equal((ProductId, 1m, PriceUnitId, (Guid?)null, new DateOnly(2026, 11, 18)),
            (conversion.ProductId, conversion.Quantity, conversion.FromUnitId, conversion.ToUnitId, conversion.EffectiveOn));
        var rate = Assert.Single(world.Rates.Requests);
        Assert.Equal((conversion.EffectiveOn, Usd, Lira), (rate.Day, rate.Source, rate.Target));
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        Assert.Equal(RateRecordId, evidence.RootElement.GetProperty("usdRate").GetProperty("recordId").GetGuid());
        Assert.Equal("4", evidence.RootElement.GetProperty("usdRate").GetProperty("recordVersion").GetString());
        Assert.Equal("8", evidence.RootElement.GetProperty("conversion").GetProperty("source").GetProperty("productVersion").GetString());
    }

    [Fact]
    public async Task A_recorded_invoice_converts_the_original_USD_cost_to_TL_without_reading_a_current_display_rate()
    {
        var world = new World(currency: Usd, invoiceRecorded: true);

        var result = await world.Host().ReadAsync(Selection);

        Assert.Equal(((decimal?)3m, (decimal?)105m, (decimal?)3m),
            (result.OriginalUnitCostPerKg, result.TlUnitCostPerKg, result.UsdUnitCostPerKg));
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.Converted, result.Currency.Status);
        Assert.Equal(35m, result.Currency.ExchangeRate);
        Assert.Null(result.TlUnavailableReason);
        Assert.Null(result.UsdUnavailableReason);
        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task A_missing_foreign_invoice_never_uses_the_receipt_day_as_a_provisional_TL_cost()
    {
        var world = new World(currency: Usd);

        var result = await world.Host().ReadAsync(Selection);

        Assert.Equal(3m, result.OriginalUnitCostPerKg);
        Assert.Equal(3m, result.UsdUnitCostPerKg);
        Assert.Null(result.TlUnitCostPerKg);
        Assert.Equal(InventoryAverageCostFxRateUnavailableCodesV1.InvoiceNotRecorded, result.TlUnavailableReason);
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.RateMissing, result.Currency.Status);
        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task A_third_currency_uses_the_official_invoice_TL_rate_then_the_receipt_day_USD_selling_rate()
    {
        var world = new World(price: 100m, kgPerPriceUnit: 1m, usdSellingRate: 50m, currency: Eur, invoiceRecorded: true);
        world.Owner.Answer = world.Owner.Answer! with
        {
            InvoiceRate = PurchaseInvoiceCostRateAnswerV1.Rated(CompanyId, AcceptanceId, OrderId, LineId, Eur, "EUR", ExchangeRate.Of(Eur, Lira, 40m)),
        };
        var result = await world.Host().ReadAsync(Selection);
        Assert.Equal((100m, 4000m, 80m), (result.OriginalUnitCostPerKg, result.TlUnitCostPerKg, result.UsdUnitCostPerKg));
        Assert.Equal(40m, result.Currency.ExchangeRate);
        Assert.Null(result.UsdUnavailableReason);
        Assert.Equal((new DateOnly(2026, 11, 18), Usd, Lira), Assert.Single(world.Rates.Requests));
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        Assert.Equal(RateRecordId, evidence.RootElement.GetProperty("usdRate").GetProperty("recordId").GetGuid());
        Assert.Contains("40", evidence.RootElement.GetProperty("proof").GetProperty("invoiceRate").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_third_currency_preserves_a_nonterminating_cross_rate_as_an_exact_ratio()
    {
        var world = new World(price: 1m, kgPerPriceUnit: 3m, usdSellingRate: 3m, currency: Eur, invoiceRecorded: true);
        var result = await world.Host().ReadAsync(Selection);
        Assert.Null(result.UsdUnitCostPerKg);
        Assert.Equal(new InventoryExactCostRatioV1("35", "9"), result.UsdUnitCostRatio);
        Assert.Null(result.UsdUnavailableReason);
    }

    [Fact]
    public async Task A_missing_third_currency_invoice_does_not_substitute_a_current_TL_or_USD_rate()
    {
        var world = new World(currency: Eur);
        var result = await world.Host().ReadAsync(Selection);
        Assert.Null(result.TlUnitCostPerKg);
        Assert.Null(result.UsdUnitCostPerKg);
        Assert.Null(result.UsdUnitCostRatio);
        Assert.Equal("purchase_receipt_usd_invoice_cross_rate_not_recorded", result.UsdUnavailableReason);
        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task A_missing_USD_display_rate_does_not_remove_the_official_third_currency_TL_cost()
    {
        var world = new World(currency: Eur, invoiceRecorded: true);
        world.Rates.Failure = new InvalidOperationException("SENTETIK unavailable USD source");
        var result = await world.Host().ReadAsync(Selection);
        Assert.Equal(105m, result.TlUnitCostPerKg);
        Assert.Null(result.TlUnavailableReason);
        Assert.Null(result.UsdUnitCostPerKg);
        Assert.Equal("purchase_receipt_usd_entry_rate_unreadable", result.UsdUnavailableReason);
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.Converted, result.Currency.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_optional_USD_provider_failure_or_its_own_timeout_preserves_the_readable_TL_value(bool timeout)
    {
        var world = new World();
        world.Rates.Failure = timeout ? new OperationCanceledException("SENTETIK provider timeout.")
            : new InvalidOperationException("SENTETIK provider unavailable.");

        var result = await world.Host().ReadAsync(Selection);

        Assert.Equal(3m, result.TlUnitCostPerKg);
        Assert.Null(result.TlUnavailableReason);
        Assert.Null(result.UsdUnitCostPerKg);
        Assert.Equal("purchase_receipt_usd_entry_rate_unreadable", result.UsdUnavailableReason);
    }

    [Theory]
    [InlineData("receipt", "actor")]
    [InlineData("receipt", "company")]
    [InlineData("receipt", "membership")]
    [InlineData("product", "actor")]
    [InlineData("product", "company")]
    [InlineData("product", "membership")]
    [InlineData("rate", "actor")]
    [InlineData("rate", "company")]
    [InlineData("rate", "membership")]
    public async Task Authority_drift_during_any_awaited_owner_read_is_denied_before_a_cost_is_returned(string phase, string drift)
    {
        var world = new World();
        world.BeforeReturn(phase, () =>
        {
            switch (drift)
            {
                case "actor": world.User.UserId = "SENTETIK-other-actor"; break;
                case "company": world.Company.Set(OtherCompanyId); break;
                case "membership": world.User.HasMembership = false; break;
                default: throw new InvalidOperationException("SENTETIK unknown drift.");
            }
        });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.Host().ReadAsync(Selection));

        Assert.Equal(phase == "receipt" ? 0 : 1, world.Product.Requests.Count);
        Assert.Equal(phase == "rate" ? 1 : 0, world.Rates.Requests.Count);
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("product")]
    [InlineData("rate")]
    public async Task Caller_cancellation_during_any_owner_read_propagates_instead_of_becoming_an_unavailable_cost(string phase)
    {
        var world = new World();
        using var cancellation = new CancellationTokenSource();
        world.BeforeReturn(phase, cancellation.Cancel);

        var refused = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            world.Host().ReadAsync(Selection, cancellation.Token));

        Assert.Equal(cancellation.Token, refused.CancellationToken);
        Assert.All(world.Owner.Tokens.Concat(world.Product.Tokens).Concat(world.Rates.Tokens),
            token => Assert.Equal(cancellation.Token, token));
    }

    // ---- AK-3 (ALS-D119 / ALS-D120): golden ADET_KG_FORMUL_GOLDEN_V1 §2, row ids in the names ------------------------------

    private static decimal Rounded(InventoryExactCostRatioV1? ratio) => ratio!.Value().Round(AverageCostPrecision.InternalScale);

    [Fact]
    public async Task A01_the_production_composition_costs_a_piece_receipt_from_its_weighed_kilograms()
    {
        // R1: 100 pieces x 250,00 TL = 25 000,00 TL over 1 250,400 kg weighed -> 19,9936 (the card's theoretical kg is not asked).
        var world = new World(price: 250m, kgPerPriceUnit: 12.345m).Weighed("100", "1250.4");

        var result = await world.ProductionHost().ReadAsync(Selection);

        Assert.True(PurchaseReceiptWeighedCost.PurchaseCostGoldenAccepted);
        Assert.Equal(0, ExactCostRatio.Of(25000m).Divide(1250.4m).CompareTo(result.TlUnitCostRatio!.Value()));
        Assert.Equal(19.9936m, Rounded(result.TlUnitCostRatio));
        Assert.Equal(19.9936m, Rounded(result.OriginalUnitCostRatio));
        Assert.Null(result.TlUnavailableReason);
        Assert.Empty(world.Product.Requests);
    }

    [Theory]
    [InlineData("1234.5", "20.2511")] // A03: weighed = theoretical, the only case equal to the old rule
    [InlineData("1250.4", "19.9936")] // A04: +1,288 % outside the 1 % tolerance, the cost still uses the weighed kg (TA4)
    [InlineData("1246.845", "20.0506")] // A05: exactly at the 1 % bound
    public async Task A03_A04_A05_the_weighed_kilograms_decide_the_cost_whatever_the_tolerance_outcome(string weighed, string expected)
    {
        var world = new World(price: 250m, kgPerPriceUnit: 12.345m).Weighed("100", weighed);

        var result = await world.Host(weighedKilogramCost: true).ReadAsync(Selection);

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Rounded(result.TlUnitCostRatio));
        Assert.Empty(world.Product.Requests);
    }

    [Fact]
    public async Task A02_a_half_at_the_fourth_decimal_is_kept_exact_and_rounds_once_away_from_zero()
    {
        // 246,89 TL over 200,000 kg = 1.23445 exactly; the adapter keeps the ratio, the average rounds it once to 1,2345.
        var world = new World(price: 246.89m).Weighed("1", "200");

        var result = await world.Host(weighedKilogramCost: true).ReadAsync(Selection);

        Assert.Equal(1.23445m, result.TlUnitCostPerKg);
        Assert.Equal(1.2345m, Rounded(result.TlUnitCostRatio));
    }

    [Fact]
    public async Task A07_a_foreign_piece_receipt_converts_the_exact_weighed_ratio_at_the_invoice_rate()
    {
        // 100 x 7,50 USD over 1 250,400 kg; invoice rate 34,5678 -> 25 925,85 TL / 1 250,400 kg = 20,7340 TL/kg.
        var world = new World(price: 7.5m, currency: Usd, invoiceRecorded: true).Weighed("100", "1250.4");
        world.Owner.Answer = world.Owner.Answer! with
        {
            InvoiceRate = PurchaseInvoiceCostRateAnswerV1.Rated(CompanyId, AcceptanceId, OrderId, LineId, Usd, "USD",
                ExchangeRate.Of(Usd, Lira, 34.5678m)),
        };

        var result = await world.Host(weighedKilogramCost: true).ReadAsync(Selection);

        Assert.Equal(20.7340m, Rounded(result.TlUnitCostRatio));
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.Converted, result.Currency.Status);
        Assert.Empty(world.Product.Requests);
    }

    [Fact]
    public async Task A08_a_foreign_piece_receipt_without_its_invoice_has_no_TL_cost_and_no_provisional_rate()
    {
        var world = new World(price: 7.5m, currency: Usd).Weighed("100", "1250.4");

        var result = await world.Host(weighedKilogramCost: true).ReadAsync(Selection);

        Assert.Null(result.TlUnitCostPerKg);
        Assert.Null(result.TlUnitCostRatio);
        Assert.Equal(InventoryAverageCostFxRateUnavailableCodesV1.InvoiceNotRecorded, result.TlUnavailableReason);
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.RateMissing, result.Currency.Status);
        Assert.Empty(world.Rates.Requests);
    }

    [Theory]
    [InlineData(null)] // A11: the receipt was not weighed
    public async Task A11_a_piece_receipt_without_weighed_kilograms_has_no_cost_and_never_falls_back_to_the_theoretical_kg(string? weighed)
    {
        var world = new World(price: 250m, kgPerPriceUnit: 12.345m).Weighed("100", weighed);

        var result = await world.Host(weighedKilogramCost: true).ReadAsync(Selection);

        AssertUnavailable(result, "purchase_receipt_weighed_kg_unavailable");
        Assert.Empty(world.Product.Requests);
    }

    [Fact]
    public async Task A11_a_fractional_piece_count_has_no_weighed_cost()
    {
        var world = new World(price: 250m).Weighed("2.5", "1250.4");

        AssertUnavailable(await world.Host(weighedKilogramCost: true).ReadAsync(Selection), "purchase_receipt_weighed_kg_unavailable");
    }

    [Fact]
    public async Task A12_a_zero_amount_receipt_is_unreadable_and_never_enters_the_average()
    {
        var world = new World(price: 0m).Weighed("100", "1250.4");

        AssertUnavailable(await world.Host(weighedKilogramCost: true).ReadAsync(Selection), "purchase_receipt_cost_zero_unreadable");
    }

    [Fact]
    public async Task The_closed_switch_keeps_the_theoretical_kilogram_denominator_even_when_the_receipt_was_weighed()
    {
        // SAT-D298 (closed state, pinned by parameter): 250,00 TL per piece over the card's 12,345 kg -> 20,2511, weighed kg ignored.
        var world = new World(price: 250m, kgPerPriceUnit: 12.345m).Weighed("100", "1250.4");

        var result = await world.Host(weighedKilogramCost: false).ReadAsync(Selection);

        Assert.Equal(20.2511m, Rounded(result.TlUnitCostRatio));
        Assert.Single(world.Product.Requests);
    }

    private static void AssertUnavailable(InventoryPurchaseReceiptCostV1 result, string reason)
    {
        Assert.Equal((CompanyId, AcceptanceId, OrderId, LineId),
            (result.CompanyId, result.AcceptanceId, result.PurchaseOrderId, result.PurchaseOrderLineId));
        Assert.Null(result.OriginalUnitCostPerKg);
        Assert.Null(result.TlUnitCostPerKg);
        Assert.Null(result.UsdUnitCostPerKg);
        Assert.Equal(reason, result.TlUnavailableReason);
        Assert.Equal(reason, result.UsdUnavailableReason);
        Assert.Equal(InventoryIssueCostCurrencyStatusV1.Unreadable, result.Currency.Status);
    }

    private static PurchaseReceiptCostProofV1 PurchaseProof(decimal price, Guid currency, bool invoiceRecorded = false,
        Guid? companyId = null)
    {
        var scopedCompany = companyId ?? CompanyId;
        var code = currency == Lira ? "TRY" : currency == Eur ? "EUR" : "USD";
        var recorded = PurchaseOrderLineCostPriceV1.Of(LineId, ProductId, PriceUnitId, PriceUnitId,
            priceUnitIsKilogram: false, Money.Of(price, currency, 4), code, null, null, null, null);
        var invoice = invoiceRecorded
            ? PurchaseInvoiceCostRateAnswerV1.Rated(scopedCompany, AcceptanceId, OrderId, LineId, currency, code,
                ExchangeRate.Of(currency, Lira, 35m))
            : PurchaseInvoiceCostRateAnswerV1.Unavailable(scopedCompany, AcceptanceId, OrderId, LineId, currency, code,
                PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded);
        return new(scopedCompany, OrderId, LineId, AcceptanceId, ProductId, "7", "10", "9", ReceivedAt,
            PurchaseOrderLineCostAnswerV1.Known(scopedCompany, OrderId, LineId, 9, recorded), invoice);
    }

    private sealed class World
    {
        public World(decimal price = 6m, decimal kgPerPriceUnit = 2m, decimal usdSellingRate = 4m,
            Guid? currency = null, bool invoiceRecorded = false)
        {
            Owner.Answer = PurchaseProof(price, currency ?? Lira, invoiceRecorded);
            Product.Answer = new(ProductPurchaseUnitConversionOutcomeV1.Available, kgPerPriceUnit, kgPerPriceUnit, 0m,
                new(CompanyId, ProductId, "8", PriceUnitId, UnitConversionClassV1.FormulaBound,
                    [new(PriceUnitId, ProductPhysicalUnitKindV1.Metre, kgPerPriceUnit, "SENTETIK-unit-v1", ProfileId, 1)],
                    "SENTETIK-formula-v1", 1, null, null, ProfileId, 1, null));
            Rates.SellingRate = usdSellingRate;
        }

        public MutableCompany Company { get; } = new();
        public MutableUser User { get; } = new();
        public ReceiptOwner Owner { get; } = new();
        public ProductOwner Product { get; } = new();
        public RateOwner Rates { get; } = new();
        /// <summary>
        /// The facts above use a non-kilogram price unit without weighed kilograms: they pin SAT-D298's theoretical-kg path, which
        /// is the CLOSED state of ALS-D119's switch. The weighed-kg facts below pass <c>true</c> explicitly.
        /// </summary>
        public PurchasingOwnerInventoryReceiptCostSource Host(bool weighedKilogramCost = false) =>
            new(Owner, Product, Rates, Company, User, weighedKilogramCost);

        /// <summary>The production composition: no parameter, so the adapter reads ALS-D119's switch.</summary>
        public PurchasingOwnerInventoryReceiptCostSource ProductionHost() => new(Owner, Product, Rates, Company, User);

        /// <summary>Golden R1 shape: a piece line's receipt with its accepted pieces and weighed kilograms.</summary>
        public World Weighed(string acceptedPieces, string? measuredKg)
        {
            Owner.Answer = Owner.Answer! with { AcceptedQuantity = acceptedPieces, MeasuredQuantityKg = measuredKg };
            return this;
        }
        public void BeforeReturn(string phase, Action action)
        {
            switch (phase)
            {
                case "receipt": Owner.BeforeReturn = action; break;
                case "product": Product.BeforeReturn = action; break;
                case "rate": Rates.BeforeReturn = action; break;
                default: throw new InvalidOperationException("SENTETIK unknown owner phase.");
            }
        }
    }

    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = InventoryPurchaseReceiptCostHostTests.CompanyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class MutableUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = "SENTETIK-cost-actor";
        public bool HasMembership { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyId };
        public bool CanAccessCompany(Guid companyId) => HasMembership && AllowedCompanyIds.Contains(companyId);
    }

    private sealed class ReceiptOwner : IPurchaseReceiptCostProofV1
    {
        public PurchaseReceiptCostProofV1? Answer { get; set; }
        public Action? BeforeReturn { get; set; }
        public List<CancellationToken> Tokens { get; } = [];
        public async Task<PurchaseReceiptCostProofV1?> ReadAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
            Guid acceptanceId, CancellationToken cancellationToken = default)
        {
            Assert.Equal((OrderId, LineId, AcceptanceId), (purchaseOrderId, purchaseOrderLineId, acceptanceId));
            Tokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            BeforeReturn?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Answer;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ProductOwner : IProductPurchaseUnitConversionV1
    {
        public ProductPurchaseUnitConversionResultV1 Answer { get; set; } = null!;
        public Action? BeforeReturn { get; set; }
        public List<ProductPurchaseUnitConversionRequestV1> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public async Task<ProductPurchaseUnitConversionResultV1> ConvertAsync(ProductPurchaseUnitConversionRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request); Tokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            BeforeReturn?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Answer;
        }
        public Task<ProductPurchaseUnitConversionReadV1> ReadAsync(Guid productId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("SENTETIK unexpected Product read.");
        public Task<ProductPurchaseWeighbridgeResultV1> CheckWeighbridgeAsync(ProductPurchaseWeighbridgeRequestV1 request,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("SENTETIK unexpected weighbridge read.");
    }

    private sealed class RateOwner : ISettingsExchangeRateResolverV1
    {
        public decimal SellingRate { get; set; }
        public Exception? Failure { get; set; }
        public Action? BeforeReturn { get; set; }
        public List<(DateOnly Day, Guid Source, Guid Target)> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public async Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly rateDay, Guid sourceCurrencyId, Guid targetCurrencyId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((rateDay, sourceCurrencyId, targetCurrencyId)); Tokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            BeforeReturn?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            return new(rateDay, sourceCurrencyId, targetCurrencyId, "USD", ExchangeRateResolutionStatesV1.Resolved,
                ExchangeRateSourcesV1.Tcmb, false, null, rateDay.AddDays(-1), SellingRate.ToString(CultureInfo.InvariantCulture),
                ExchangeRateProvenancesV1.Official, RateRecordId, "4", null, null);
        }
    }
}
