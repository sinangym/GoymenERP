using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryPhysicalHoldTests
{
    [Fact]
    public void Placement_contract_binds_real_label_and_inspection_without_quantity_input()
    {
        var request = new PlaceInventoryPhysicalHoldV1(Hold, Label, "7", Inspection,
            "SENTETIK kalite inceleme blokajı", ExpiresAtUtc: null);

        Assert.Equal((Hold, Label, "7", Inspection),
            (request.HoldId, request.LabelId, request.ExpectedLabelVersion, request.InspectionId));
        Assert.DoesNotContain(request.GetType().GetProperties(), property =>
            property.Name.Contains("Quantity", StringComparison.OrdinalIgnoreCase)
            || property.Name.EndsWith("Kg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Release_contract_binds_exact_hold_label_and_quality_disposition_versions()
    {
        var request = new ReleaseInventoryPhysicalHoldV1(Hold, "2", Label, "8", Disposition,
            "SENTETIK bağımsız kalite kararı");

        Assert.Equal((Hold, "2", Label, "8", Disposition),
            (request.HoldId, request.ExpectedHoldVersion, request.LabelId,
                request.ExpectedLabelVersion, request.DispositionId));
    }

    [Fact]
    public void Evidence_and_transfer_tokens_are_not_hold_state_or_owner_tokens()
    {
        var tokens = typeof(InventoryPhysicalHoldTokensV1).GetFields()
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .ToArray();

        Assert.DoesNotContain(tokens, token => token.Contains("Mtc", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tokens, token => token.Contains("Transfer", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["Quality", "QualityInspection", "Active", "Released"], tokens);
    }

    [Fact]
    public void Stable_fail_closed_codes_are_distinct()
    {
        var codes = typeof(InventoryPhysicalHoldCodesV1).GetFields()
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(InventoryPhysicalHoldCodesV1.LegacyBlockedAmbiguous, codes);
        Assert.Contains(InventoryPhysicalHoldCodesV1.SelfApproval, codes);
        Assert.DoesNotContain(codes, code => code.Contains("allocation", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly Guid Hold = Guid.Parse("82000000-0000-4000-8000-000000000001");
    private static readonly Guid Label = Guid.Parse("82000000-0000-4000-8000-000000000002");
    private static readonly Guid Inspection = Guid.Parse("82000000-0000-4000-8000-000000000003");
    private static readonly Guid Disposition = Guid.Parse("82000000-0000-4000-8000-000000000004");
}
