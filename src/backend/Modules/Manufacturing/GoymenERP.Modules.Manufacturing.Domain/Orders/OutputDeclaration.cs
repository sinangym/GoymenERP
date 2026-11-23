using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// One declared output of a Dilme run. It always names the parent roll it came from (plan v2
/// §7.3: every slitting output is bound to its master roll) and when it was declared (R6). The
/// output id is the physical label's identity in Inventory; creating that label is not this
/// module's work.
/// <para>
/// F-009 D2 (URT-D30): <see cref="OutputLine"/> names the plan output line the declaration counts towards. A
/// declaration stored before D2 has none ("satırsız"): it still counts in every balance, but it cannot be corrected
/// (fail-closed).
/// </para>
/// <para>
/// AK-7 (URT-D112): <see cref="Pieces"/> is the piece count of an output whose product is counted in pieces, with the
/// theoretical kg per piece captured at declaration; null for an output that is not counted (and every output stored
/// before AK-7). The weighed <see cref="Mass"/> stays the output's quantity for balance and cost (cost per kg).
/// </para>
/// </summary>
public sealed record OutputDeclaration(
    Guid OutputId,
    Guid ParentRollId,
    OutputClass Class,
    Kilograms Mass,
    DateTimeOffset DeclaredAtUtc,
    CostResult Cost,
    PlanOutputLineReference? OutputLine = null,
    OutputPieces? Pieces = null);
