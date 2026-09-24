namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// The five locked production processes of DEC-PRDCT-001, in their technical delivery order.
/// Process-specific parameters and engine rules remain in their own slices.
/// </summary>
public enum ManufacturingProcess
{
    Slitting = 1,
    ColdRolling = 2,
    TubeProfile = 3,
    Paint = 4,
    CutToLength = 5,
}
