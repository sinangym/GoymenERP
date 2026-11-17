using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

public enum ManufacturingPaintSelectionOutcomeV1 { Available = 1, Ineligible = 2, VersionConflict = 3, Unavailable = 4 }
public sealed record ManufacturingPaintSelectionAnswerV1(ManufacturingPaintSelectionOutcomeV1 Outcome,
    Guid CompanyId, PlanPaintSelectionV1? Selection);
public sealed record ManufacturingPaintProductV1(Guid ProductId, string ProductVersion, string MembershipVersion,
    string SourceVersion, string Code, string Name);
public sealed record ManufacturingPaintSupplierV1(Guid PartyId, Guid CompanyAccountId,
    string PartyVersion, string AccountVersion, string Code, string Name);
public sealed record ManufacturingPaintColorV1(Guid Id, string Version, string Code, string Name);
public sealed record ManufacturingPaintCatalogV1(Guid CompanyId, IReadOnlyList<ManufacturingPaintProductV1> Products,
    IReadOnlyList<ManufacturingPaintSupplierV1> Suppliers, IReadOnlyList<ManufacturingPaintColorV1> Colors,
    bool SupplierSearchWindowFull);

/// <summary>The host composes actual read-only Product, Cari and Settings contracts. The Manufacturing caller owns
/// its current permission/company fence; every new choice carries the exact owner versions the user saw.</summary>
public interface IManufacturingPaintSelectionSourceV1
{
    Task<ManufacturingPaintSelectionAnswerV1> ValidateAsync(Guid companyId, PlanPaintSelectionV1 selection, CancellationToken cancellationToken);
    Task<ManufacturingPaintCatalogV1> ListAsync(Guid companyId, string? supplierQuery, CancellationToken cancellationToken);
}

public static class ManufacturingPaintCodesV1
{
    public const string Required = "manufacturing_paint_selection_required";
    public const string Ineligible = "manufacturing_paint_selection_ineligible";
    public const string VersionConflict = "manufacturing_paint_selection_version_conflict";
    public const string Unavailable = "manufacturing_paint_selection_unavailable";
}

public sealed partial class ManufacturingCommandService
{
    private async Task<ManufacturingApiResult<object>?> VerifyPaintSelectionAsync(PlanProcessSpec? spec,
        ManufacturingProcess process, string permission, Guid companyId, Guid actorId, CancellationToken cancellationToken,
        bool approval = false)
    {
        if (process != ManufacturingProcess.Paint) return null;
        // Existing drafts may be incomplete. A missing process spec remains incomplete, never a usable paint source;
        // approval always requires the actual choice. Supplying a new Paint spec must carry owner references.
        if (spec is null && !approval) return null;
        if (spec?.PaintSelection is not { } selection)
            return new(ManufacturingApiStatus.Unprocessable, null, false, ManufacturingPaintCodesV1.Required, ProcessSpecField);
        if (paintSelections is null)
            return new(ManufacturingApiStatus.Unavailable, null, false, ManufacturingPaintCodesV1.Unavailable);
        ManufacturingPaintSelectionAnswerV1 answer;
        try { answer = await paintSelections.ValidateAsync(companyId, selection, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return new(ManufacturingApiStatus.Denied, null, false, ManufacturingApiCodes.AuthorityDenied); }
        catch (Exception) { return new(ManufacturingApiStatus.Unavailable, null, false, ManufacturingPaintCodesV1.Unavailable); }
        if (await FenceCapturedAsync<object>(permission, companyId, actorId, cancellationToken) is { } fenced) return fenced;
        if (answer is null || answer.CompanyId != companyId) return new(ManufacturingApiStatus.Unavailable, null, false, ManufacturingPaintCodesV1.Unavailable);
        return answer.Outcome switch
        {
            ManufacturingPaintSelectionOutcomeV1.Available when answer.Selection == selection => null,
            ManufacturingPaintSelectionOutcomeV1.VersionConflict => new(ManufacturingApiStatus.Conflict, null, false, ManufacturingPaintCodesV1.VersionConflict, ProcessSpecField),
            ManufacturingPaintSelectionOutcomeV1.Ineligible => new(ManufacturingApiStatus.Unprocessable, null, false, ManufacturingPaintCodesV1.Ineligible, ProcessSpecField),
            _ => new(ManufacturingApiStatus.Unavailable, null, false, ManufacturingPaintCodesV1.Unavailable),
        };
    }
}
