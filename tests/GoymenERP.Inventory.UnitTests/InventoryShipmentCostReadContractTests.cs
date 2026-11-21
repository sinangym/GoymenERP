using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Inventory.UnitTests;

// Internal consumer contract, company admission and immutable stamp projection only.
// These tests provide neither HTTP/user authorization nor real SQL acceptance evidence.
public sealed class InventoryShipmentCostReadContractTests
{
    private static readonly Guid CompanyA = Guid.Parse("82600000-0000-4000-8000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("82600000-0000-4000-8000-000000000002");
    private static readonly Guid Shipment = Guid.Parse("82600000-0000-4000-8000-000000000003");
    private static readonly Guid Movement = Guid.Parse("82600000-0000-4000-8000-000000000004");
    private static readonly Guid Label = Guid.Parse("82600000-0000-4000-8000-000000000005");
    private static readonly Guid Product = Guid.Parse("82600000-0000-4000-8000-000000000006");
    private static readonly Guid Location = Guid.Parse("82600000-0000-4000-8000-000000000007");
    private static readonly Guid Tl = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void An_available_shipment_read_can_preserve_explicitly_unavailable_TL_without_claiming_a_price_or_accounting_admission()
    {
        var unavailable = InventoryMovementCostStampProjection.Read(null, 2m, Tl);
        var line = new InventoryShipmentCostLineV1(1,
            Guid.Parse("82600000-0000-4000-8000-000000000008"),
            Guid.Parse("82600000-0000-4000-8000-000000000009"),
            Guid.Parse("82600000-0000-4000-8000-000000000010"), Label, "SYNTHETIC-SHIPMENT-LABEL", Product,
            "synthetic-product-version", StockPartyKind.Company, CompanyA, StockPartyKind.Company, CompanyA,
            Location, BlockState.Free, 2m, unavailable);
        var value = new InventoryShipmentCostReadV1(InventoryShipmentCostReadOutcomeV1.Available,
            CompanyA, Shipment, Movement, [line], "synthetic-original-shipment-source");

        var body = JsonSerializer.SerializeToElement(value, Json);
        Assert.Equal(["companyId", "lines", "movementId", "outcome", "reason", "shipmentId", "sourceVersion"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(1, body.GetProperty("outcome").GetInt32());
        Assert.Equal(CompanyA, body.GetProperty("companyId").GetGuid());
        Assert.Equal(Shipment, body.GetProperty("shipmentId").GetGuid());
        Assert.Equal(Movement, body.GetProperty("movementId").GetGuid());
        var item = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(["allocationId", "block", "custodianId", "custodianKind", "labelCode", "labelId",
                "loadingInstructionId", "loadingInstructionLineId", "locationId", "movementLineNumber", "ownerId",
                "ownerKind", "productId", "productVersion", "quantityKg", "tl"],
            item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2m, item.GetProperty("quantityKg").GetDecimal());
        Assert.Equal((int)InventoryMovementCostStampStatusV1.Unavailable, item.GetProperty("tl").GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("tl").GetProperty("unitCostPerKg").ValueKind);
        var restored = JsonSerializer.Deserialize<InventoryShipmentCostReadV1>(body.GetRawText(), Json)!;
        Assert.Equal(value.SourceVersion, restored.SourceVersion);
        Assert.Equal("synthetic-product-version", Assert.Single(restored.Lines).ProductVersion);
        Assert.Equal(InventoryMovementCostStampStatusV1.Unavailable, Assert.Single(restored.Lines).Tl.Status);
        Assert.Equal(2, (int)InventoryShipmentCostReadOutcomeV1.NotFound);
        Assert.Equal(3, (int)InventoryShipmentCostReadOutcomeV1.Unreadable);
    }

    [Theory]
    [InlineData("unresolved")]
    [InlineData("foreign")]
    [InlineData("empty-company")]
    [InlineData("empty-shipment")]
    [InlineData("empty-movement")]
    public async Task Missing_or_mismatched_owner_company_and_empty_source_ids_refuse_before_any_connection(string fault)
    {
        var company = new CompanyContext();
        if (fault != "unresolved") company.Set(fault == "foreign" ? CompanyB : CompanyA);
        var tripwire = new NoConnectionOpen();
        await using var context = Context(company, tripwire);
        IInventoryShipmentCostReadPortV1 reader = new InventoryShipmentCostReadPort(context, company);
        var requestedCompany = fault == "empty-company" ? Guid.Empty : CompanyA;
        var requestedShipment = fault == "empty-shipment" ? Guid.Empty : Shipment;
        var requestedMovement = fault == "empty-movement" ? Guid.Empty : Movement;

        var answer = await reader.GetAsync(requestedCompany, requestedShipment, requestedMovement);

        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, answer.Outcome);
        Assert.Equal((requestedCompany, requestedShipment, requestedMovement), (answer.CompanyId, answer.ShipmentId, answer.MovementId));
        Assert.Empty(answer.Lines);
        Assert.Equal("shipment_cost_provenance_unreadable", answer.Reason);
        Assert.Equal("inventory.shipment-cost.v1/unreadable", answer.SourceVersion);
        Assert.Equal(0, tripwire.Calls);
    }

    [Theory]
    [InlineData("unresolved")]
    [InlineData("foreign")]
    [InlineData("empty-company")]
    [InlineData("empty-shipment")]
    public async Task Shipment_only_resolution_keeps_the_same_owner_company_fence_without_opening_a_connection(string fault)
    {
        var company = new CompanyContext();
        if (fault != "unresolved") company.Set(fault == "foreign" ? CompanyB : CompanyA);
        var tripwire = new NoConnectionOpen();
        await using var context = Context(company, tripwire);
        IInventoryShipmentCostReadPortV1 reader = new InventoryShipmentCostReadPort(context, company);
        var answer = await reader.GetByShipmentAsync(fault == "empty-company" ? Guid.Empty : CompanyA,
            fault == "empty-shipment" ? Guid.Empty : Shipment);
        Assert.Equal(InventoryShipmentCostReadOutcomeV1.Unreadable, answer.Outcome);
        Assert.Equal(Guid.Empty, answer.MovementId);
        Assert.Empty(answer.Lines);
        Assert.Equal("inventory.shipment-cost.v1/unreadable", answer.SourceVersion);
        Assert.Equal(0, tripwire.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_precedes_both_company_admission_and_any_connection(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved) company.Set(CompanyA);
        var tripwire = new NoConnectionOpen();
        await using var context = Context(company, tripwire);
        var reader = new InventoryShipmentCostReadPort(context, company);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.GetAsync(CompanyA, Shipment, Movement, canceled.Token));
        Assert.Equal(canceled.Token, error.CancellationToken);
        var resolutionCanceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.GetByShipmentAsync(CompanyA, Shipment, canceled.Token));
        Assert.Equal(canceled.Token, resolutionCanceled.CancellationToken);
        Assert.Equal(0, tripwire.Calls);
    }

    [Fact]
    public void A_missing_original_stamp_remains_legacy_unavailable_without_an_average_or_other_currency_substitution()
    {
        var stamp = InventoryMovementCostStampProjection.Read(null, 3m, Tl);
        Assert.Equal(InventoryMovementCostStampStatusV1.Unavailable, stamp.Status);
        Assert.Equal(Tl, stamp.CurrencyId);
        Assert.Null(stamp.UnitCostPerKg);
        Assert.Null(stamp.Amount);
        Assert.Null(stamp.UnitCostRatio);
        Assert.Null(stamp.LedgerVersion);
        Assert.Equal("legacy-not-recorded", stamp.StampVersion);
        Assert.Equal("movement_cost_stamp_not_recorded", stamp.Reason);
    }

    [Fact]
    public void An_actual_USD_stamp_cannot_be_projected_as_TL()
    {
        var row = Known();
        row.CurrencyId = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
        Assert.Throws<InvalidOperationException>(() => InventoryMovementCostStampProjection.Read(row, 2m, Tl));
    }

    [Theory]
    [InlineData("0", "0", "0", "1")]
    [InlineData("2.5", "5", "5", "2")]
    public void Finite_original_cost_including_known_zero_keeps_its_amount_and_source_versions(
        string unitText, string amountText, string numerator, string denominator)
    {
        var row = Known();
        row.UnitCostPerKg = decimal.Parse(unitText, System.Globalization.CultureInfo.InvariantCulture);
        row.Amount = decimal.Parse(amountText, System.Globalization.CultureInfo.InvariantCulture);
        var stamp = InventoryMovementCostStampProjection.Read(row, 2m, Tl);
        Assert.Equal(InventoryMovementCostStampStatusV1.Known, stamp.Status);
        Assert.Equal(row.UnitCostPerKg, stamp.UnitCostPerKg);
        Assert.Equal(row.Amount, stamp.Amount);
        Assert.Equal(7L, stamp.LedgerVersion);
        Assert.Equal("11", stamp.StampVersion);
        Assert.Equal(new InventoryExactCostRatioV1(numerator, denominator), stamp.UnitCostRatio);
        Assert.Null(stamp.Reason);
    }

    [Fact]
    public void A_one_third_original_unit_cost_stays_exact_without_a_rounded_scalar_or_amount()
    {
        var row = Known();
        row.UnitCostPerKg = null;
        row.Amount = null;
        row.UnitCostRatioJson = JsonSerializer.Serialize(new InventoryExactCostRatioV1("1", "3"), Json);
        var stamp = InventoryMovementCostStampProjection.Read(row, 1m, Tl);
        Assert.Equal(InventoryMovementCostStampStatusV1.Known, stamp.Status);
        Assert.Null(stamp.UnitCostPerKg);
        Assert.Null(stamp.Amount);
        Assert.Equal(new InventoryExactCostRatioV1("1", "3"), stamp.UnitCostRatio);
        Assert.False(stamp.UnitCostRatio!.Value().TryToDecimal(out _));
        var restored = JsonSerializer.Deserialize<InventoryMovementCostStampV1>(JsonSerializer.Serialize(stamp, Json), Json)!;
        Assert.Null(restored.UnitCostPerKg);
        Assert.Null(restored.Amount);
        Assert.Equal(stamp.UnitCostRatio, restored.UnitCostRatio);
        Assert.Equal(7L, restored.LedgerVersion);
    }

    [Fact]
    public void A_rational_unit_can_keep_an_exact_recorded_amount_when_the_actual_quantity_makes_it_finite()
    {
        var row = Known();
        row.UnitCostPerKg = null;
        row.Amount = 1m;
        row.UnitCostRatioJson = JsonSerializer.Serialize(new InventoryExactCostRatioV1("1", "3"), Json);
        var stamp = InventoryMovementCostStampProjection.Read(row, 3m, Tl);
        Assert.Null(stamp.UnitCostPerKg);
        Assert.Equal(1m, stamp.Amount);
        Assert.Equal(new InventoryExactCostRatioV1("1", "3"), stamp.UnitCostRatio);
    }

    [Theory]
    [InlineData("rounded-unit")]
    [InlineData("wrong-finite-unit")]
    [InlineData("rounded-amount")]
    [InlineData("wrong-finite-amount")]
    public void Contradictory_scalar_or_amount_evidence_is_refused_instead_of_rounded_into_agreement(string fault)
    {
        var row = Known();
        var kg = 1m;
        if (fault == "rounded-unit")
        {
            row.UnitCostPerKg = 0.3333m; row.Amount = null;
            row.UnitCostRatioJson = JsonSerializer.Serialize(new InventoryExactCostRatioV1("1", "3"), Json);
        }
        else if (fault == "wrong-finite-unit")
        {
            row.UnitCostPerKg = 2.5m; row.Amount = null;
            row.UnitCostRatioJson = JsonSerializer.Serialize(new InventoryExactCostRatioV1("2", "1"), Json);
        }
        else if (fault == "rounded-amount")
        {
            row.UnitCostPerKg = null; row.Amount = 0.3333m;
            row.UnitCostRatioJson = JsonSerializer.Serialize(new InventoryExactCostRatioV1("1", "3"), Json);
        }
        else { row.Amount = 4.99m; kg = 2m; }
        Assert.Throws<InvalidOperationException>(() => InventoryMovementCostStampProjection.Read(row, kg, Tl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Known_cost_requires_the_actual_positive_ledger_version(long? version)
    {
        var row = Known(); row.LedgerVersion = version;
        Assert.Throws<InvalidOperationException>(() => InventoryMovementCostStampProjection.Read(row, 2m, Tl));
    }

    [Theory]
    [InlineData("2", "6")]
    [InlineData("01", "3")]
    [InlineData("1", "0")]
    public void A_noncanonical_or_invalid_exact_ratio_is_not_treated_as_an_original_price(string numerator, string denominator)
    {
        var row = Known(); row.UnitCostPerKg = null; row.Amount = null;
        row.UnitCostRatioJson = JsonSerializer.Serialize(new InventoryExactCostRatioV1(numerator, denominator), Json);
        Assert.Throws<InvalidOperationException>(() => InventoryMovementCostStampProjection.Read(row, 1m, Tl));
    }

    private static MovementCostStampRow Known() => new()
    {
        CompanyId = CompanyA, MovementId = Movement, LineNumber = 1, CurrencyId = Tl,
        Status = (int)InventoryMovementCostStampStatusV1.Known, UnitCostPerKg = 2.5m, Amount = 5m,
        LedgerVersion = 7, StampVersion = 11, Reason = null, CorrelationId = "synthetic-original-shipment",
    };

    private static InventoryDbContext Context(CompanyContext company, NoConnectionOpen tripwire) => new(
        new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .AddInterceptors(tripwire).Options, company);

    private sealed class UnexpectedConnectionException : Exception;
    private sealed class NoConnectionOpen : DbConnectionInterceptor
    {
        public int Calls { get; private set; }
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            Calls++;
            throw new UnexpectedConnectionException();
        }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new UnexpectedConnectionException();
        }
    }
}
