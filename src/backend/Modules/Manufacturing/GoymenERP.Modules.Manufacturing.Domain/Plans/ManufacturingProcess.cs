namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// The production engines of plan v2 §7.3. MFG-1 carries only Dilme/Sarım; the other engines
/// are added by their own slices with their own rules, not reserved here.
/// </summary>
public enum ManufacturingProcess
{
    Slitting = 1,
}
