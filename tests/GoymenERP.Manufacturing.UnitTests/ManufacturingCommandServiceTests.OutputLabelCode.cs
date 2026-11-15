using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S21 (URT-D62): the output declaration's label code is the output label's own code. Sent, it is trimmed and
/// forwarded as entered; omitted, nothing is forwarded and the host gives the next number of the company's Ayarlar
/// format. A sent code that is not a usable code once trimmed is refused before any owner call or write. The code joins
/// the request hash, so the same key with and without a code is an idempotency conflict.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task An_output_without_a_label_code_forwards_none_so_the_host_numbers_it()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            OutputLabelRequest(order.Version, "GoodProduct", null) with { LabelCode = null }, "output-code-none",
            Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Null(Assert.Single(fixture.ReturnOutputs.Outputs).LabelCode);
    }

    [Fact]
    public async Task An_entered_output_label_code_is_forwarded_trimmed()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            OutputLabelRequest(order.Version, "GoodProduct", null) with { LabelCode = "  SENTETIK-CIKTI-7 " },
            "output-code-entered", Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal("SENTETIK-CIKTI-7", Assert.Single(fixture.ReturnOutputs.Outputs).LabelCode);
    }

    // Indexed so the theory's display names stay printable ASCII; the control characters live only in the array.
    private static readonly string[] UnusableOutputLabelCodes = ["", "   ", "SENTETIK\u0007KOD", "SENTETIK\nKOD"];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task An_unusable_entered_output_label_code_is_invalid_before_any_owner_call(int unusable)
    {
        var labelCode = UnusableOutputLabelCodes[unusable];
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            OutputLabelRequest(order.Version, "GoodProduct", null) with { LabelCode = labelCode },
            "output-code-invalid", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "labelCode"),
            (result.Status, result.Code, result.Field));
        Assert.Empty(fixture.ReturnOutputs.Outputs);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task An_entered_output_label_code_longer_than_the_stored_bound_is_invalid_and_never_cut()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var tooLong = new string('K', ProductionOutputLabelCodes.MaximumCodeLength + 1);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            OutputLabelRequest(order.Version, "GoodProduct", null) with { LabelCode = tooLong },
            "output-code-long", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "labelCode"), (result.Status, result.Field));
        Assert.Empty(fixture.ReturnOutputs.Outputs);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task The_same_key_with_and_without_an_output_label_code_is_an_idempotency_conflict()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        var entered = OutputLabelRequest(order.Version, "GoodProduct", null);

        var recorded = await fixture.Service.DeclareInventoryOutputAsync(order.Id, entered, "output-code-key",
            Permission, CancellationToken.None);
        var omitted = await fixture.Service.DeclareInventoryOutputAsync(order.Id, entered with { LabelCode = null },
            "output-code-key", Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, recorded.Status);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict),
            (omitted.Status, omitted.Code));
        Assert.Single(fixture.ReturnOutputs.Outputs);
    }

    [Theory]
    [InlineData(ManufacturingApiStatus.Unprocessable, ManufacturingStoreCodes.OutputLabelCodeRequired, "labelCode")]
    [InlineData(ManufacturingApiStatus.Unprocessable, ManufacturingStoreCodes.OutputLabelCodeFormatInvalid, null)]
    [InlineData(ManufacturingApiStatus.Conflict, ManufacturingStoreCodes.OutputLabelCodeConflict, "labelCode")]
    public async Task A_host_output_label_code_refusal_keeps_its_status_code_and_field(
        ManufacturingApiStatus status, string code, string? field)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        fixture.ReturnOutputs.Exception = new ManufacturingInventoryRealizationPortException(status, code, field);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            OutputLabelRequest(order.Version, "GoodProduct", null) with { LabelCode = null }, "output-code-refused",
            Permission, CancellationToken.None);

        Assert.Equal((status, code, field), (result.Status, result.Code, result.Field));
    }
}
