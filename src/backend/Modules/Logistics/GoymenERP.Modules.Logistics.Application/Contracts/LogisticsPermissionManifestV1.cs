namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// The Logistics permission names of LOG-4 (D6). Company-wide, not critical; the central catalogue
/// (<c>ErpPermissions</c>) registers them with the central writer's step, and any grant is a human
/// gate. The service measures them afresh at every fence; an unregistered name is a 503 setup problem.
/// </summary>
public static class LogisticsPermissionManifestV1
{
    /// <summary>Open a loading instruction (Draft with lines) and issue it.</summary>
    public const string InstructionsCreate = "logistics.loading-instructions.create";

    /// <summary>List and read loading instructions of the context company.</summary>
    public const string InstructionsRead = "logistics.loading-instructions.read";

    /// <summary>Record a loaded share against an issued instruction's line.</summary>
    public const string LoadsRecord = "logistics.loads.record";

    public static readonly IReadOnlyList<string> Names = [InstructionsCreate, InstructionsRead, LoadsRecord];
}
