namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// Whether a locked process (DEC-PRDCT-001) consumes a main parent roll, the scope URT-D49 measures the
/// "output above input" comparison on and URT-D68 measures the shortfall tolerance and the fire percent on.
/// <list type="bullet">
/// <item>Slitting, cold rolling, tube/profile and cut-to-length run a coil or strip (master v3 §28.3–§28.6): their
/// comparison, tolerance and fire stay per parent roll, rolls never offsetting each other (URT-D42 (1), URT-D49 (2),
/// URT-D69).</item>
/// <item>Paint runs finished profiles, counted as packages × pieces per package (master v3 §28.7, PNT-001): it has no
/// main roll input, so the order's total net issued kg is compared with its total accounted kg (URT-D49 (1)), and the
/// shortfall tolerance and fire percent are the order total's (URT-D68).</item>
/// </list>
/// URT-D69: paint is the only process that runs without a coil. A new process without a coil needs its own decision
/// first; until then an undefined process is refused here rather than given a scope.
/// The mapping is the process's own input shape; it chooses no tolerance, threshold or approval.
/// </summary>
public static class ManufacturingProcessMainInputV1
{
    public static bool HasMainRollInput(ManufacturingProcess process) => process switch
    {
        ManufacturingProcess.Slitting => true,
        ManufacturingProcess.ColdRolling => true,
        ManufacturingProcess.TubeProfile => true,
        ManufacturingProcess.CutToLength => true,
        ManufacturingProcess.Paint => false,
        _ => throw new ArgumentOutOfRangeException(nameof(process), process, null),
    };
}
