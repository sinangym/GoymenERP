using System.Text.Json;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK Settings and authority adapters exercise the live FNS-D103 helper only.
/// These facts provide no SQL, real TCMB/provider, permission-grant or activation evidence.</summary>
public sealed class InventoryFirstEntryRateSourceTests
{
    private static readonly Guid CompanyId = Guid.Parse("8c220000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompanyId = Guid.Parse("8c220000-0000-4000-8000-000000000002");
    private static readonly Guid OrderId = Guid.Parse("8c220000-0000-4000-8000-000000000003");
    private static readonly Guid OtherOrderId = Guid.Parse("8c220000-0000-4000-8000-000000000004");
    private static readonly Guid LineId = Guid.Parse("8c220000-0000-4000-8000-000000000005");
    private static readonly Guid AcceptanceId = Guid.Parse("8c220000-0000-4000-8000-000000000006");
    private static readonly Guid ProductId = Guid.Parse("8c220000-0000-4000-8000-000000000007");
    private static readonly Guid UnitId = Guid.Parse("8c220000-0000-4000-8000-000000000008");
    private static readonly Guid RateRecordId = Guid.Parse("8c220000-0000-4000-8000-000000000009");
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Usd = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD");
    private static readonly Guid Eur = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("EUR");
    private static readonly DateTimeOffset ReceivedAt = new(2026, 11, 17, 22, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly EntryDay = new(2026, 11, 18);

    [Fact]
    public async Task An_approved_foreign_order_rate_wins_without_reading_Settings_or_borrowing_the_invoice_rate()
    {
        var world = new World();
        world.Rates.Failure = new InvalidOperationException("SENTETIK Settings must not be called.");

        var result = await world.ReadAsync();

        AssertRated(result, 31.25m, new("125", "4"), InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder);
        Assert.Empty(world.Rates.Requests);
        Assert.Equal(9L, result.PurchaseOrderVersion);
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        var order = evidence.RootElement.GetProperty("order");
        Assert.Equal(CompanyId, order.GetProperty("companyId").GetGuid());
        Assert.Equal(OrderId, order.GetProperty("purchaseOrderId").GetGuid());
        Assert.Equal(9L, order.GetProperty("orderVersion").GetInt64());
        Assert.Equal(31.25m, order.GetProperty("rate").GetProperty("rate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("dayRate").ValueKind);
        Assert.False(evidence.RootElement.TryGetProperty("invoiceRate", out _));
    }

    [Fact]
    public async Task Only_an_explicitly_unspecified_order_rate_reads_the_received_business_day_selling_rate()
    {
        var world = new World { Proof = Proof(RateNotSpecified(Usd)) };

        var result = await world.ReadAsync();

        AssertRated(result, 32.5m, new("65", "2"), InventoryFirstEntryRateSourcesV1.EntryDayTcmb);
        var request = Assert.Single(world.Rates.Requests);
        Assert.Equal((EntryDay, Usd, Lira), request);
        Assert.Equal(9L, result.PurchaseOrderVersion);
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        var dayRate = evidence.RootElement.GetProperty("dayRate");
        Assert.Equal(EntryDay.ToString("yyyy-MM-dd"), dayRate.GetProperty("rateDay").GetString());
        Assert.Equal(RateRecordId, dayRate.GetProperty("recordId").GetGuid());
        Assert.Equal("4", dayRate.GetProperty("recordVersion").GetString());
        Assert.Equal("32.5", dayRate.GetProperty("sellingRate").GetString());
        Assert.Equal(PurchaseOrderExchangeRateReasonsV1.RateNotSpecified,
            evidence.RootElement.GetProperty("order").GetProperty("unavailableReason").GetString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("not_found")]
    [InlineData("not_approved")]
    [InlineData("unreadable")]
    public async Task A_missing_or_unavailable_order_owner_does_not_enable_a_day_rate_fallback(string fault)
    {
        var order = fault switch
        {
            "missing" => null,
            "not_found" => PurchaseOrderExchangeRateAnswerV1.NotFound(CompanyId, OrderId),
            "not_approved" => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyId, OrderId, 9, Usd, "USD",
                PurchaseOrderExchangeRateReasonsV1.OrderNotApproved),
            "unreadable" => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyId, OrderId, null, null, null,
                PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var world = new World { Proof = Proof(order) };

        var result = await world.ReadAsync();

        AssertUnavailable(result, fault == "missing" ? "first_entry_approved_order_rate_source_missing"
            : "first_entry_approved_order_rate_source_unreadable");
        Assert.Empty(world.Rates.Requests);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    [InlineData("version")]
    [InlineData("currency")]
    public async Task Mismatched_owner_identity_version_or_currency_neither_falls_back_nor_enters_the_evidence(string fault)
    {
        var order = fault switch
        {
            "company" => RatedOrder(companyId: OtherCompanyId),
            "order" => RatedOrder(orderId: OtherOrderId),
            "version" => RatedOrder(version: 8),
            "currency" => RatedOrder(currency: Eur),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var world = new World { Proof = Proof(order) };

        var result = await world.ReadAsync();

        AssertUnavailable(result, "first_entry_approved_order_rate_source_unreadable");
        Assert.Empty(world.Rates.Requests);
        Assert.Null(result.PurchaseOrderVersion);
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("order").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("dayRate").ValueKind);
        Assert.DoesNotContain(OtherCompanyId.ToString("D"), result.EvidenceJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OtherOrderId.ToString("D"), result.EvidenceJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_order_rate_to_a_foreign_target_is_refused_without_an_inverse_or_cross_rate_fallback()
    {
        var order = PurchaseOrderExchangeRateAnswerV1.Rated(CompanyId, OrderId, 9, Usd, "USD", ExchangeRate.Of(Usd, Eur, 0.9m));
        var world = new World { Proof = Proof(order) };

        var result = await world.ReadAsync();

        AssertUnavailable(result, "first_entry_approved_order_rate_source_unreadable");
        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task A_functional_currency_order_needs_no_rate_and_never_invents_an_FX_of_one()
    {
        var world = new World { Proof = Proof(RateNotSpecified(Lira), Lira) };

        var result = await world.ReadAsync();

        AssertEnvelope(result);
        Assert.Equal(InventoryFirstEntryRateStateV1.NotRequired, result.State);
        Assert.Equal(InventoryFirstEntryRateSourcesV1.FunctionalCurrency, result.SourceKind);
        Assert.Equal(Lira, result.SourceCurrencyId);
        Assert.Equal(Lira, result.TargetCurrencyId);
        Assert.Null(result.Rate);
        Assert.Null(result.RateRatio);
        Assert.Null(result.Reason);
        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task A_functional_currency_order_with_a_foreign_conversion_is_not_treated_as_no_rate_required()
    {
        var order = PurchaseOrderExchangeRateAnswerV1.Rated(CompanyId, OrderId, 9, Lira, "TRY", ExchangeRate.Of(Lira, Usd, 0.1m));
        var world = new World { Proof = Proof(order, Lira) };

        var result = await world.ReadAsync();

        AssertUnavailable(result, "first_entry_approved_order_rate_source_unreadable");
        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task An_unrecorded_currency_never_turns_into_a_functional_currency_or_default_USD_rate()
    {
        var proof = Proof(PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyId, OrderId, 9, null, null,
            PurchaseOrderExchangeRateReasonsV1.RateNotSpecified));
        proof = proof with { Price = PurchaseOrderLineCostAnswerV1.Unreadable(CompanyId, OrderId, LineId, 9,
            PurchaseOrderLineCostReasonsV1.PricePending) };
        var world = new World { Proof = proof };

        var result = await world.ReadAsync();

        AssertUnavailable(result, "first_entry_order_currency_not_recorded");
        Assert.Null(result.SourceCurrencyId);
        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task A_price_pending_receipt_can_preserve_its_recorded_approved_order_rate_without_inventing_a_stock_cost()
    {
        var proof = Proof(RatedOrder());
        proof = proof with { Price = PurchaseOrderLineCostAnswerV1.Unreadable(CompanyId, OrderId, LineId, 9,
            PurchaseOrderLineCostReasonsV1.PricePending) };
        var world = new World { Proof = proof };

        var result = await world.ReadAsync();

        AssertRated(result, 31.25m, new("125", "4"), InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder);
        Assert.Empty(world.Rates.Requests);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("source")]
    [InlineData("day")]
    [InlineData("source_currency")]
    [InlineData("target_currency")]
    [InlineData("record_missing")]
    [InlineData("record_empty")]
    [InlineData("version_missing")]
    [InlineData("version_blank")]
    [InlineData("bulletin_missing")]
    [InlineData("bulletin_future")]
    [InlineData("rate_missing")]
    [InlineData("rate_zero")]
    [InlineData("rate_negative")]
    [InlineData("rate_malformed")]
    [InlineData("rate_local_separator")]
    public async Task A_wrong_or_incomplete_Settings_answer_is_unavailable_without_another_rate_attempt(string fault)
    {
        var world = new World { Proof = Proof(RateNotSpecified(Usd)) };
        var answer = world.Rates.Answer;
        world.Rates.Answer = fault switch
        {
            "state" => answer with { State = ExchangeRateResolutionStatesV1.Unavailable },
            "source" => answer with { Source = "SENTETIK-other-source" },
            "day" => answer with { RateDay = EntryDay.AddDays(-1) },
            "source_currency" => answer with { SourceCurrencyId = Eur },
            "target_currency" => answer with { TargetCurrencyId = Eur },
            "record_missing" => answer with { RecordId = null },
            "record_empty" => answer with { RecordId = Guid.Empty },
            "version_missing" => answer with { RecordVersion = null },
            "version_blank" => answer with { RecordVersion = " " },
            "bulletin_missing" => answer with { BulletinDate = null },
            "bulletin_future" => answer with { BulletinDate = EntryDay.AddDays(1) },
            "rate_missing" => answer with { SellingRate = null },
            "rate_zero" => answer with { SellingRate = "0" },
            "rate_negative" => answer with { SellingRate = "-1" },
            "rate_malformed" => answer with { SellingRate = "SENTETIK-rate" },
            "rate_local_separator" => answer with { SellingRate = "32,5" },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        var result = await world.ReadAsync();

        AssertUnavailable(result, "first_entry_day_rate_source_unreadable");
        Assert.Single(world.Rates.Requests);
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("dayRate").ValueKind);
    }

    [Fact]
    public async Task A_Settings_transport_failure_stays_unavailable_without_exposing_exception_details_as_evidence()
    {
        var world = new World { Proof = Proof(RateNotSpecified(Usd)) };
        world.Rates.Failure = new InvalidOperationException("SENTETIK-private-provider-detail");

        var result = await world.ReadAsync();

        AssertUnavailable(result, "first_entry_rate_source_unreadable");
        Assert.Single(world.Rates.Requests);
        Assert.DoesNotContain("SENTETIK-private-provider-detail", result.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_initial_authority_never_reads_Settings_or_returns_a_rate()
    {
        var world = new World { HasCompanyAccess = false, Proof = Proof(RateNotSpecified(Usd)) };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.ReadAsync());

        Assert.Empty(world.Rates.Requests);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("membership")]
    [InlineData("actor")]
    public async Task Authority_drift_during_a_Settings_read_is_refreshed_before_any_rate_answer(string change)
    {
        var world = new World { Proof = Proof(RateNotSpecified(Usd)) };
        world.Rates.BeforeReturn = () => world.ChangeAuthority(change);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.ReadAsync());

        Assert.Single(world.Rates.Requests);
    }

    [Fact]
    public async Task Authority_is_refreshed_before_a_synchronous_approved_order_answer_is_emitted()
    {
        var world = new World();
        world.BeforeAuthorityCheck = count => { if (count > 1) world.HasCompanyAccess = false; };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.ReadAsync());

        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task Authority_loss_during_a_failed_Settings_read_is_not_hidden_by_an_unavailable_answer()
    {
        var world = new World { Proof = Proof(RateNotSpecified(Usd)) };
        world.Rates.BeforeReturn = () => world.HasCompanyAccess = false;
        world.Rates.Failure = new InvalidOperationException("SENTETIK-provider-failure");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.ReadAsync());

        Assert.Single(world.Rates.Requests);
    }

    [Fact]
    public async Task Cancellation_before_the_read_propagates_without_reading_Settings()
    {
        var world = new World { Proof = Proof(RateNotSpecified(Usd)) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.ReadAsync(cancellation.Token));

        Assert.Empty(world.Rates.Requests);
    }

    [Fact]
    public async Task Cancellation_while_Settings_returns_is_checked_by_the_helper_before_it_admits_the_rate()
    {
        var world = new World { Proof = Proof(RateNotSpecified(Usd)) };
        using var cancellation = new CancellationTokenSource();
        world.Rates.BeforeReturn = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.ReadAsync(cancellation.Token));

        Assert.Single(world.Rates.Requests);
        Assert.Equal(cancellation.Token, Assert.Single(world.Rates.Tokens));
    }

    private static void AssertRated(InventoryFirstEntryRateV1 result, decimal rate,
        InventoryExactCostRatioV1 ratio, string source)
    {
        AssertEnvelope(result);
        Assert.Equal(InventoryFirstEntryRateStateV1.Rated, result.State);
        Assert.Equal(Usd, result.SourceCurrencyId);
        Assert.Equal(Lira, result.TargetCurrencyId);
        Assert.Equal(rate, result.Rate);
        Assert.Equal(ratio, result.RateRatio);
        Assert.Equal(source, result.SourceKind);
        Assert.Null(result.Reason);
        Assert.StartsWith(source + "/", result.SourceVersion, StringComparison.Ordinal);
    }

    private static void AssertUnavailable(InventoryFirstEntryRateV1 result, string reason)
    {
        AssertEnvelope(result);
        Assert.Equal(InventoryFirstEntryRateStateV1.Unavailable, result.State);
        Assert.Equal(InventoryFirstEntryRateSourcesV1.Unavailable, result.SourceKind);
        Assert.Null(result.Rate);
        Assert.Null(result.RateRatio);
        Assert.Equal(reason, result.Reason);
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        Assert.Equal(reason, evidence.RootElement.GetProperty("reason").GetString());
    }

    private static void AssertEnvelope(InventoryFirstEntryRateV1 result)
    {
        Assert.Equal((CompanyId, AcceptanceId, OrderId, EntryDay),
            (result.CompanyId, result.AcceptanceId, result.PurchaseOrderId, result.EntryDay));
        Assert.False(string.IsNullOrWhiteSpace(result.SourceVersion));
        using var evidence = JsonDocument.Parse(result.EvidenceJson);
        Assert.Equal(CompanyId, evidence.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal(AcceptanceId, evidence.RootElement.GetProperty("acceptanceId").GetGuid());
        Assert.Equal(OrderId, evidence.RootElement.GetProperty("purchaseOrderId").GetGuid());
        Assert.Equal(EntryDay.ToString("yyyy-MM-dd"), evidence.RootElement.GetProperty("entryDay").GetString());
    }

    private static PurchaseReceiptCostProofV1 Proof(PurchaseOrderExchangeRateAnswerV1? order, Guid? currencyId = null)
    {
        var currency = currencyId ?? Usd;
        var code = currency == Lira ? "TRY" : currency == Eur ? "EUR" : "USD";
        var price = PurchaseOrderLineCostPriceV1.Of(LineId, ProductId, UnitId, UnitId, true,
            Money.Of(6m, currency, 4), code, null, null, null, null);
        var invoice = currency == Lira
            ? PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyId, AcceptanceId, OrderId, LineId, currency, code,
                PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded)
            : PurchaseInvoiceCostRateAnswerV1.Rated(CompanyId, AcceptanceId, OrderId, LineId, currency, code,
                ExchangeRate.Of(currency, Lira, 35m));
        return new(CompanyId, OrderId, LineId, AcceptanceId, ProductId, "7", "10", "9", ReceivedAt,
            PurchaseOrderLineCostAnswerV1.Known(CompanyId, OrderId, LineId, 9, price), invoice, order);
    }

    private static PurchaseOrderExchangeRateAnswerV1 RatedOrder(Guid? companyId = null, Guid? orderId = null,
        long version = 9, Guid? currency = null)
    {
        var source = currency ?? Usd;
        return PurchaseOrderExchangeRateAnswerV1.Rated(companyId ?? CompanyId, orderId ?? OrderId, version, source,
            source == Eur ? "EUR" : "USD", ExchangeRate.Of(source, Lira, 31.25m));
    }

    private static PurchaseOrderExchangeRateAnswerV1 RateNotSpecified(Guid currency) =>
        PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyId, OrderId, 9, currency, currency == Lira ? "TRY" : "USD",
            PurchaseOrderExchangeRateReasonsV1.RateNotSpecified);

    private sealed class World
    {
        private const string InitialActor = "SENTETIK-first-entry-actor";
        private int authorityChecks;

        public PurchaseReceiptCostProofV1 Proof { get; set; } = InventoryFirstEntryRateSourceTests.Proof(RatedOrder());
        public RateOwner Rates { get; } = new();
        public Guid CurrentCompanyId { get; set; } = CompanyId;
        public bool HasCompanyAccess { get; set; } = true;
        public string Actor { get; set; } = InitialActor;
        public Action<int>? BeforeAuthorityCheck { get; set; }

        public Task<InventoryFirstEntryRateV1> ReadAsync(CancellationToken cancellationToken = default) =>
            InventoryFirstEntryRateSource.ReadAsync(Proof, Rates, FreshAuthority, cancellationToken);

        public void ChangeAuthority(string change)
        {
            switch (change)
            {
                case "company": CurrentCompanyId = OtherCompanyId; break;
                case "membership": HasCompanyAccess = false; break;
                case "actor": Actor = "SENTETIK-changed-actor"; break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        private void FreshAuthority()
        {
            authorityChecks++;
            BeforeAuthorityCheck?.Invoke(authorityChecks);
            if (CurrentCompanyId != CompanyId || !HasCompanyAccess || Actor != InitialActor)
                throw new UnauthorizedAccessException("SENTETIK fresh authority refused.");
        }
    }

    private sealed class RateOwner : ISettingsExchangeRateResolverV1
    {
        public ExchangeRateResolutionV1 Answer { get; set; } = new(EntryDay, Usd, Lira, "USD",
            ExchangeRateResolutionStatesV1.Resolved, ExchangeRateSourcesV1.Tcmb, false, null, EntryDay.AddDays(-1),
            "32.5", ExchangeRateProvenancesV1.Official, RateRecordId, "4", null, null);
        public Action? BeforeReturn { get; set; }
        public Exception? Failure { get; set; }
        public List<(DateOnly Day, Guid Source, Guid Target)> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public async Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly rateDay, Guid sourceCurrencyId,
            Guid targetCurrencyId, CancellationToken cancellationToken = default)
        {
            Requests.Add((rateDay, sourceCurrencyId, targetCurrencyId));
            Tokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            BeforeReturn?.Invoke();
            if (Failure is not null) throw Failure;
            // Deliberately return even if BeforeReturn cancels: the live helper must recheck after awaiting its owner.
            return Answer;
        }
    }
}
