using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class PlanPaintSelectionTests
{
    private static PlanPaintSelectionV1 Selection() => new(Guid.NewGuid(), "12", "3", "paint-import:3",
        Guid.NewGuid(), Guid.NewGuid(), "4", "7");

    [Fact]
    public void Typed_paint_identity_is_plan_content_and_does_not_require_a_color_or_free_text_name()
    {
        var choice = Selection();
        var spec = new PlanProcessSpec(paintSelection: choice);
        spec.RequireAllowedFor(ManufacturingProcess.Paint, "processSpec");
        Assert.Equal(choice, spec.PaintSelection); Assert.Null(spec.PaintColorCode);
        Assert.Empty(spec.EngineValues(ManufacturingProcess.Paint));
        Assert.NotEqual(spec, new PlanProcessSpec(paintSelection: choice with { MembershipVersion = "4" }));
        Assert.Throws<ArgumentException>(() => spec.RequireAllowedFor(ManufacturingProcess.ColdRolling, "processSpec"));
        Assert.Throws<ArgumentException>(() => spec.RequireAllowedFor(ManufacturingProcess.TubeProfile, "processSpec"));
    }

    [Fact]
    public void A_color_and_version_are_whole_and_owner_versions_are_nonempty_immutable_references()
    {
        var choice = Selection();
        Assert.Throws<ArgumentException>(() => new PlanProcessSpec(paintSelection: choice with { ColorId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => new PlanProcessSpec(paintSelection: choice with { ColorVersion = "1" }));
        Assert.Throws<ArgumentException>(() => new PlanProcessSpec(paintSelection: choice with { SupplierCompanyAccountId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => new PlanProcessSpec(paintSelection: choice with { MembershipVersion = " padded " }));
        Assert.NotNull(new PlanProcessSpec(paintSelection: choice with { ColorId = Guid.NewGuid(), ColorVersion = "2" }));
        // Old content remains readable; a free description is not upgraded to a verified Product membership.
        Assert.Null(new PlanProcessSpec(paintDescription: "Eski boya notu").PaintSelection);
    }
}
