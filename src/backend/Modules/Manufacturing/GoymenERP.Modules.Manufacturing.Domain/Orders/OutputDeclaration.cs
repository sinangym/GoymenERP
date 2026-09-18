using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// One declared output of a Dilme run. It always names the parent roll it came from (plan v2
/// §7.3: every slitting output is bound to its master roll). The output id is the physical
/// label's identity in Inventory; creating that label is not this module's work.
/// </summary>
public sealed record OutputDeclaration(
    Guid OutputId,
    Guid ParentRollId,
    OutputClass Class,
    Kilograms Mass,
    CostResult Cost);
