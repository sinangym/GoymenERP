using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// STK-S41 (KLN-D65 / KLN-D66, ASSUMPTION-STK-REASON-1) without a database: the Settings-backed kilogram conversion that never
/// guesses, the terminal refusals the future supply allocation writer records (and the transient ones it never records), the
/// refusal audit event types and the format-only release reason rule. The cap itself is exercised against a real SQL Server in
/// the relational acceptance tests. Every identity is synthetic test data. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryAllocationDemandCapContractTests
{
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid UnknownUnitId = Guid.NewGuid();
    private static readonly Guid OtherUnknownUnitId = Guid.NewGuid();

    [Fact]
    public async Task Settings_unit_converter_converts_only_the_same_unit_and_the_settings_kilogram()
    {
        var converter = new InventorySettingsFutureUnitConverterV1(new ApprovedSettingsUnitIdentityCatalog());
        var kilogram = SettingsUnitIdentitiesV1.Kilogram;

        var sameUnknown = await converter.ConvertAsync(ProductId, "1", 7.25m, UnknownUnitId, UnknownUnitId);
        var kilogramToKilogram = await converter.ConvertAsync(ProductId, "1", 7.25m, kilogram, kilogram);
        var unknownToKilogram = await converter.ConvertAsync(ProductId, "1", 7.25m, UnknownUnitId, kilogram);
        var kilogramToUnknown = await converter.ConvertAsync(ProductId, "1", 7.25m, kilogram, UnknownUnitId);
        var unknownPair = await converter.ConvertAsync(ProductId, "1", 7.25m, UnknownUnitId, OtherUnknownUnitId);
        var emptyPair = await converter.ConvertAsync(ProductId, "1", 7.25m, Guid.Empty, Guid.Empty);

        Assert.Equal((InventoryFutureUnitConversionOutcomeV1.Converted, (decimal?)7.25m), (sameUnknown.Outcome, sameUnknown.Quantity));
        Assert.Equal((InventoryFutureUnitConversionOutcomeV1.Converted, (decimal?)7.25m),
            (kilogramToKilogram.Outcome, kilogramToKilogram.Quantity));
        Assert.All(new[] { unknownToKilogram, kilogramToUnknown, unknownPair, emptyPair }, answer =>
            Assert.Equal((InventoryFutureUnitConversionOutcomeV1.NoSource, (decimal?)null), (answer.Outcome, answer.Quantity)));
    }

    [Fact]
    public async Task Settings_unit_converter_brings_only_the_settings_kilogram_to_kilograms()
    {
        var converter = new InventorySettingsFutureUnitConverterV1(new ApprovedSettingsUnitIdentityCatalog());

        var kilogram = await converter.ConvertToKilogramsAsync(ProductId, "1", 12.5m, SettingsUnitIdentitiesV1.Kilogram);
        var unknown = await converter.ConvertToKilogramsAsync(ProductId, "1", 12.5m, UnknownUnitId);
        var empty = await converter.ConvertToKilogramsAsync(ProductId, "1", 12.5m, Guid.Empty);

        Assert.Equal((InventoryFutureUnitConversionOutcomeV1.Converted, (decimal?)12.5m), (kilogram.Outcome, kilogram.Quantity));
        Assert.Equal((InventoryFutureUnitConversionOutcomeV1.NoSource, InventoryFutureUnitConversionOutcomeV1.NoSource),
            (unknown.Outcome, empty.Outcome));
    }

    [Fact]
    public async Task Settings_unit_converter_reads_a_failing_or_mismatched_identity_source_as_no_conversion_source()
    {
        var failing = new InventorySettingsFutureUnitConverterV1(new ThrowingUnitCatalog());
        var mismatched = new InventorySettingsFutureUnitConverterV1(new MismatchedKilogramCatalog());

        Assert.Equal(InventoryFutureUnitConversionOutcomeV1.NoSource,
            (await failing.ConvertToKilogramsAsync(ProductId, "1", 1m, SettingsUnitIdentitiesV1.Kilogram)).Outcome);
        Assert.Equal(InventoryFutureUnitConversionOutcomeV1.NoSource,
            (await mismatched.ConvertToKilogramsAsync(ProductId, "1", 1m, UnknownUnitId)).Outcome);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InventorySettingsFutureUnitConverterV1(
            new CancelledUnitCatalog()).ConvertToKilogramsAsync(ProductId, "1", 1m, SettingsUnitIdentitiesV1.Kilogram));
    }

    [Fact]
    public void Future_allocation_writer_records_only_rule_refusals_decided_under_the_locks()
    {
        Assert.Contains((409, InventoryFutureAllocationCodesV1.ExceedsDemandOpenQuantity),
            InventoryFutureAllocationWriter.RecordableCreateRefusals);
        Assert.Contains((422, InventoryFutureAllocationCodesV1.UnitConversionUnavailable),
            InventoryFutureAllocationWriter.RecordableCreateRefusals);
        Assert.Contains((422, InventoryFutureAllocationCodesV1.InvalidReference),
            InventoryFutureAllocationWriter.RecordableCreateRefusals);
        Assert.Contains((409, InventoryFutureAllocationCodesV1.InsufficientFutureSupply),
            InventoryFutureAllocationWriter.RecordableCreateRefusals);
        Assert.Contains((409, InventoryFutureAllocationCodesV1.VersionConflict),
            InventoryFutureAllocationWriter.RecordableReleaseRefusals);
        Assert.Contains((422, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid),
            InventoryFutureAllocationWriter.RecordableReleaseRefusals);

        // A busy lock, a lost race, a key conflict, a malformed request, a missing row and an unavailable source may pass on retry.
        var transient = new (int Status, string Code)[]
        {
            (409, SalesOrderLineSerializationLockContractV1.BusyCode),
            (409, InventoryFutureAllocationCodesV1.ConcurrentChange),
            (409, InventoryFutureAllocationCodesV1.IdempotencyConflict),
            (400, InventoryFutureAllocationCodesV1.ValidationFailed),
            (404, InventoryFutureAllocationCodesV1.InvalidReference),
            (503, InventoryFutureAllocationCodesV1.ServiceUnavailable),
            (503, InventoryFutureAllocationCodesV1.InvalidReference),
        };
        Assert.All(transient, refusal =>
        {
            Assert.DoesNotContain(refusal, InventoryFutureAllocationWriter.RecordableCreateRefusals);
            Assert.DoesNotContain(refusal, InventoryFutureAllocationWriter.RecordableReleaseRefusals);
        });
    }

    [Fact]
    public void Future_allocation_refusal_audit_event_types_are_versioned_and_distinct()
    {
        Assert.Equal("inventory.future-allocation.create-refused.v1", InventoryFutureAllocationWriter.CreateRefusedEventType);
        Assert.Equal("inventory.future-allocation.release-refused.v1", InventoryFutureAllocationWriter.ReleaseRefusedEventType);
        Assert.NotEqual(InventoryFutureAllocationCreatedOutboxV1.EventType, InventoryFutureAllocationWriter.CreateRefusedEventType);
    }

    [Fact]
    public void Physical_allocation_cap_codes_are_distinct_from_the_future_allocation_codes()
    {
        Assert.Equal("inventory_allocation_exceeds_demand_open_quantity", InventoryAllocationCodesV1.ExceedsDemandOpenQuantity);
        Assert.Equal("inventory_allocation_unit_conversion_unavailable", InventoryAllocationCodesV1.UnitConversionUnavailable);
        Assert.NotEqual(InventoryAllocationCodesV1.ExceedsDemandOpenQuantity,
            InventoryFutureAllocationCodesV1.ExceedsDemandOpenQuantity);
        Assert.NotEqual(InventoryAllocationCodesV1.UnitConversionUnavailable,
            InventoryFutureAllocationCodesV1.UnitConversionUnavailable);
    }

    [Theory]
    [InlineData(InventoryFutureAllocationReleaseReasonsV1.Manual)]
    [InlineData(InventoryFutureAllocationReleaseReasonsV1.SalesLineReduced)]
    [InlineData(InventoryFutureAllocationReleaseReasonsV1.AcceptanceReversedNoSupply)]
    [InlineData("a-later-slice-reason")]
    [InlineData("x")]
    public void Release_reason_code_with_a_well_formed_kebab_case_shape_is_accepted_without_a_closed_catalog(string code) =>
        Assert.True(InventoryFutureAllocationReleaseReasonsV1.IsWellFormed(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-manual")]
    [InlineData("manual-")]
    [InlineData("manual--release")]
    [InlineData("Manual-Release")]
    [InlineData("manual release")]
    [InlineData("manual_release")]
    [InlineData("manual-release!")]
    public void Release_reason_code_with_a_malformed_shape_is_refused(string? code) =>
        Assert.False(InventoryFutureAllocationReleaseReasonsV1.IsWellFormed(code));

    [Fact]
    public void Release_reason_code_longer_than_the_maximum_length_is_refused()
    {
        var longest = new string('a', InventoryFutureAllocationReleaseReasonsV1.MaximumLength);

        Assert.True(InventoryFutureAllocationReleaseReasonsV1.IsWellFormed(longest));
        Assert.False(InventoryFutureAllocationReleaseReasonsV1.IsWellFormed(longest + "a"));
    }

    private sealed class ThrowingUnitCatalog : ISettingsUnitIdentityCatalogV1
    {
        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SENTETIK settings source failure");

        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SENTETIK settings source failure");
    }

    /// <summary>Answers a kilogram identity for any id: the converter must still require the answered id to be the asked one.</summary>
    private sealed class MismatchedKilogramCatalog : ISettingsUnitIdentityCatalogV1
    {
        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UnitIdentityV1?>(new UnitIdentityV1(SettingsUnitIdentitiesV1.Kilogram, "kg", "weight", true));

        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnitIdentityV1>>([]);
    }

    private sealed class CancelledUnitCatalog : ISettingsUnitIdentityCatalogV1
    {
        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();

        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }
}
