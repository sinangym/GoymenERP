namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>
/// The business effect an owner command has, declared by the owner module that implements an
/// <see cref="IAiOwnerCommandToolV1"/>. The AI command pipeline (WP-C1) refuses every effect except
/// <see cref="RecordChange"/>: AI-D25 closes approval/rejection, payment, period close, role/permission
/// assignment and policy publication to the AI path under every condition. Adding a value is a contract change.
/// </summary>
public enum AiCommandEffectV1
{
    /// <summary>Creates or changes an owner record without deciding an approval, money, period, role or policy.</summary>
    RecordChange = 1,
    Approval = 2,
    Rejection = 3,
    Payment = 4,
    PeriodClose = 5,
    RoleOrPermissionAssignment = 6,
    PolicyPublication = 7,
}

/// <summary>
/// Required beside <see cref="IAiOwnerCommandToolV1"/>: a command tool that does not declare its effects is never
/// admitted (fail-closed). The declaration is owner-supplied metadata, not authority; the pipeline still applies its
/// own name and permission checks so a missing or wrong declaration cannot open a closed command.
/// </summary>
public interface IAiOwnerCommandEffectsV1
{
    IReadOnlyCollection<AiCommandEffectV1> Effects { get; }
}
