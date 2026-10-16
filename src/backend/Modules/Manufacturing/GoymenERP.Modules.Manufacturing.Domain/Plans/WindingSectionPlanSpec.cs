namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// One stored winding-plan section. The identity belongs to the plan snapshot; it is not an
/// Inventory label or a claim that a physical coil already exists.
/// </summary>
public sealed record WindingSectionPlanLineV1
{
    public WindingSectionPlanLineV1(Guid planWindingSectionId, decimal plannedKg)
    {
        if (planWindingSectionId == Guid.Empty)
        {
            throw new ArgumentException("Sarım plan bölümü kimliği boş olamaz.", nameof(planWindingSectionId));
        }

        if (plannedKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedKg), plannedKg,
                "Sarım plan bölümü miktarı sıfırdan büyük olmalıdır.");
        }

        PlanWindingSectionId = planWindingSectionId;
        PlannedKg = plannedKg;
    }

    public Guid PlanWindingSectionId { get; }

    public decimal PlannedKg { get; }
}

/// <summary>
/// The ordered, identity-bearing result stored with a plan revision. Source order is content and is
/// kept separately from any future machine execution order. Physical output identities are assigned
/// later by Inventory and must not reuse <see cref="WindingSectionPlanLineV1.PlanWindingSectionId"/>.
/// </summary>
public sealed class WindingSectionPlanSpecV1
{
    private readonly IReadOnlyList<WindingSectionPlanLineV1> _sections;

    public WindingSectionPlanSpecV1(IEnumerable<WindingSectionPlanLineV1> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var materialized = sections.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("Sarım planı en az bir bölüm taşımalıdır.", nameof(sections));
        }

        if (materialized.Any(section => section is null))
        {
            throw new ArgumentException("Sarım plan bölümü boş olamaz.", nameof(sections));
        }

        if (materialized.Select(section => section.PlanWindingSectionId).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Sarım plan bölümü kimliği plan içinde tekil olmalıdır.", nameof(sections));
        }

        _sections = Array.AsReadOnly(materialized);
    }

    public IReadOnlyList<WindingSectionPlanLineV1> Sections => _sections;
}
