using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryManufacturingRealizationContractTests
{
    private static readonly Guid Company = Guid.Parse("92000000-0000-4000-8000-000000000001");

    [Fact]
    public void Exact_source_and_command_tokens_are_stable_and_distinct()
    {
        Assert.Equal("manufacturing.input-issue", InventoryManufacturingRealizationContractV1.InputIssueSourceKind);
        Assert.Equal("manufacturing.input-return", InventoryManufacturingRealizationContractV1.InputReturnSourceKind);
        Assert.Equal("manufacturing.output-declaration", InventoryManufacturingRealizationContractV1.OutputSourceKind);
        Assert.Equal(3, new[]
        {
            InventoryManufacturingRealizationContractV1.InputIssueCommandName,
            InventoryManufacturingRealizationContractV1.InputReturnCommandName,
            InventoryManufacturingRealizationContractV1.OutputCommandName,
        }.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Participant_is_single_use_async_disposable_and_exposes_only_three_stages()
    {
        var type = typeof(IInventoryManufacturingRealizationParticipantV1);
        Assert.Contains(typeof(IAsyncDisposable), type.GetInterfaces());
        Assert.Equal(
            [nameof(IInventoryManufacturingRealizationParticipantV1.StageInputIssueAsync),
                nameof(IInventoryManufacturingRealizationParticipantV1.StageInputReturnAsync),
                nameof(IInventoryManufacturingRealizationParticipantV1.StageOutputAsync)],
            type.GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Input_issue_preserves_exact_post_stage_order_and_parent_label_versions()
    {
        var quantity = InventoryManufacturingRealizationContractV1.Validate(Issue(), Metadata(), Company);
        Assert.Equal(12.345m, quantity.Value);
    }

    [Fact]
    public void Parent_position_snapshot_keeps_owner_custodian_and_location_as_separate_facts()
    {
        var snapshot = new InventoryManufacturingPositionSnapshotV1(
            StockPartyKind.Counterparty, Id(20), StockPartyKind.Company, Company, Id(21));

        Assert.Equal((StockPartyKind.Counterparty, Id(20), StockPartyKind.Company, Company, Id(21)),
            (snapshot.OwnerKind, snapshot.OwnerId, snapshot.CustodianKind, snapshot.CustodianId,
                snapshot.LocationId));
    }

    [Fact]
    public void Input_return_preserves_exact_post_stage_order_and_parent_label_versions()
    {
        var quantity = InventoryManufacturingRealizationContractV1.Validate(Return(), Metadata(), Company);
        Assert.Equal(2.125m, quantity.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    public void Both_versions_are_positive_canonical_in_every_operation(string version)
    {
        AssertVersionFailure(Issue() with { ManufacturingOrderVersionAfter = version });
        AssertVersionFailure(Issue() with { ExpectedParentLabelVersion = version });
        AssertVersionFailure(Return() with { ManufacturingOrderVersionAfter = version });
        AssertVersionFailure(Return() with { ExpectedParentLabelVersion = version });
        AssertVersionFailure(Output() with { ManufacturingOrderVersionAfter = version });
        AssertVersionFailure(Output() with { ExpectedParentLabelVersion = version });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("12.3450")]
    [InlineData("12,345")]
    [InlineData("12.3456")]
    public void Kilograms_are_positive_canonical_and_never_rounded(string quantity)
    {
        AssertQuantityFailure(Issue() with { QuantityKg = quantity });
        AssertQuantityFailure(Return() with { QuantityKg = quantity });
        AssertQuantityFailure(Output() with { QuantityKg = quantity });
    }

    [Fact]
    public void Another_company_is_the_neutral_invalid_reference_for_every_operation()
    {
        var other = Guid.NewGuid();
        AssertReferenceFailure(Issue() with { CompanyId = other });
        AssertReferenceFailure(Return() with { CompanyId = other });
        AssertReferenceFailure(Output() with { CompanyId = other });
    }

    [Fact]
    public void Return_and_output_require_a_movement_identity_distinct_from_the_fact()
    {
        var returned = Return();
        var returnError = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(
                returned with { MovementId = returned.ReturnId }, Metadata(), Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidRequest),
            (returnError.Status, returnError.Code));

        var output = Output();
        var outputError = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(
                output with { MovementId = output.OutputId }, Metadata(), Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidRequest),
            (outputError.Status, outputError.Code));
    }

    [Fact]
    public void Output_requires_explicit_identity_position_block_documents_and_product_version()
    {
        InventoryManufacturingOutputRequestV1[] examples =
        [
            Output() with { ProductId = Guid.Empty },
            Output() with { ExpectedProductVersion = " product-v1" },
            Output() with { LotId = Guid.Empty },
            Output() with { LabelId = Guid.Empty },
            Output() with { LabelCode = "" },
            Output() with { RequiredDocuments = null! },
            Output() with { RequiredDocuments = [" MTC"] },
            Output() with { OwnerId = Guid.Empty },
            Output() with { CustodianId = Guid.Empty },
            Output() with { LocationId = Guid.Empty },
            Output() with { InitialBlock = (BlockState)0 },
        ];

        foreach (var request in examples)
        {
            var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
                InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company));
            Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidRequest),
                (error.Status, error.Code));
        }
    }

    [Fact]
    public void Output_class_is_an_explicit_opaque_token_not_a_physical_default()
    {
        var request = Output() with { OutputClass = "GALVANIZED-COIL", InitialBlock = BlockState.Blocked };
        var quantity = InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company);
        Assert.Equal(8.5m, quantity.Value);
        Assert.Equal(BlockState.Blocked, request.InitialBlock);
    }

    [Fact]
    public void Metadata_is_server_canonical()
    {
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(Issue(),
                Metadata() with { IdempotencyKey = " key" }, Company));
        Assert.Equal(InventoryManufacturingRealizationCodesV1.InvalidRequest, error.Code);
    }

    [Fact]
    public void Refusal_contract_is_closed_and_separates_stale_stock_and_profile_failures()
    {
        Assert.True(InventoryManufacturingRealizationCodesV1.IsDefined(409,
            InventoryManufacturingRealizationCodesV1.LabelVersionConflict));
        Assert.True(InventoryManufacturingRealizationCodesV1.IsDefined(409,
            InventoryManufacturingRealizationCodesV1.ReturnExceedsIssued));
        Assert.True(InventoryManufacturingRealizationCodesV1.IsDefined(409,
            InventoryManufacturingRealizationCodesV1.ParentInputNotIssued));
        Assert.True(InventoryManufacturingRealizationCodesV1.IsDefined(503,
            InventoryManufacturingRealizationCodesV1.QuantityProfileUnavailable));
        Assert.False(InventoryManufacturingRealizationCodesV1.IsDefined(422, "invented"));
        Assert.Throws<InvalidOperationException>(() =>
            InventoryManufacturingRealizationException.Refused(409, "invented"));
    }

    private static void AssertVersionFailure(InventoryManufacturingInputIssueRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidVersion,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static void AssertVersionFailure(InventoryManufacturingInputReturnRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidVersion,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static void AssertVersionFailure(InventoryManufacturingOutputRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidVersion,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static void AssertQuantityFailure(InventoryManufacturingInputIssueRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidQuantity,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static void AssertQuantityFailure(InventoryManufacturingInputReturnRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidQuantity,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static void AssertQuantityFailure(InventoryManufacturingOutputRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidQuantity,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static void AssertReferenceFailure(InventoryManufacturingInputIssueRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidReference,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static void AssertReferenceFailure(InventoryManufacturingInputReturnRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidReference,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static void AssertReferenceFailure(InventoryManufacturingOutputRequestV1 request) => Assert.Equal(
        InventoryManufacturingRealizationCodesV1.InvalidReference,
        Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(request, Metadata(), Company)).Code);

    private static InventoryManufacturingInputIssueRequestV1 Issue() => new(
        Company, Id(2), Id(3), "7", Id(4), "11", Id(5), "12.345", At);

    private static InventoryManufacturingInputReturnRequestV1 Return() => new(
        Company, Id(2), Id(6), "8", Id(4), "12", Id(7), "2.125", At.AddMinutes(1));

    private static InventoryManufacturingOutputRequestV1 Output() => new(
        Company, Id(2), Id(8), "9", Id(4), "13", "COIL", Id(9), "product-v1",
        Id(10), Id(11), "MFG-OUTPUT-001", "HEAT-1", "COIL-1", ["MTC"],
        StockPartyKind.Company, Company, StockPartyKind.Company, Company, Id(12), BlockState.Free,
        Id(13), "8.5", At.AddMinutes(2));

    private static InventoryCommandMetadata Metadata() => new(
        "manufacturing-user", "manufacturing.realization.write", ["MANUFACTURING-USER"],
        "corr-manufacturing-1", "cause-manufacturing-1", "manufacturing-key-1", "api");

    private static readonly DateTimeOffset At =
        new(2026, 9, 24, 15, 30, 0, TimeSpan.Zero);

    private static Guid Id(int suffix) => Guid.Parse($"92000000-0000-4000-8000-{suffix:000000000000}");
}
