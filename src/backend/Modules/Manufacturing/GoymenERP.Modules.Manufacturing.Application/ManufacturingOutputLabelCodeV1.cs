using GoymenERP.Modules.Manufacturing.Domain.Lineage;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// URT-S21 (URT-D62): the Ayarlar value the output declaration reads to give an output label its own number.
/// <see cref="GeneratedLabelCodeFormat"/> is the company's output label number format with exactly one <c>{n}</c>
/// (<see cref="ProductionOutputLabelCodes"/>); null means "not entered in Ayarlar", and then an output needs an entered
/// code. Manufacturing never picks a default format.
/// </summary>
public sealed record ManufacturingOutputLabelCodePolicyV1(string? GeneratedLabelCodeFormat)
{
    /// <summary>Nothing entered in Ayarlar.</summary>
    public static ManufacturingOutputLabelCodePolicyV1 NotSet { get; } = new((string?)null);
}

/// <summary>
/// URT-S21 (URT-D62): the Ayarlar owner's read of the output label number format for one company (draft port; the
/// Settings provider is requested from the Settings owner). Until it is registered the output declaration behaves as
/// <see cref="ManufacturingOutputLabelCodePolicyV1.NotSet"/>: an output without an entered code is refused with
/// <see cref="ManufacturingStoreCodes.OutputLabelCodeRequired"/>.
/// </summary>
public interface IManufacturingOutputLabelCodePolicyReadPortV1
{
    Task<ManufacturingOutputLabelCodePolicyV1> ReadAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>
/// URT-S21 (URT-D62): optional Inventory read — which of these label codes are already registered as stock labels in the
/// company (draft port; requested from the Inventory owner). Without it only Manufacturing's own output label numbers are
/// checked and Inventory's output staging stays the final guard (a taken code is its 409, both owners roll back).
/// </summary>
public interface IManufacturingLabelCodeRegisterReadPortV1
{
    Task<IReadOnlySet<string>> RegisteredAsync(
        Guid companyId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken);
}

/// <summary>
/// URT-S21 (URT-D62): the output label code a declaration was given — the code itself, where it came from, and for a
/// generated code its number and the Ayarlar format it was made with.
/// </summary>
public sealed record ManufacturingOutputLabelCodeV1(
    Guid OutputId,
    Guid LabelId,
    string LabelCode,
    ProductionOutputLabelCodeSource Source,
    long? GeneratedNumber,
    string? Format);
