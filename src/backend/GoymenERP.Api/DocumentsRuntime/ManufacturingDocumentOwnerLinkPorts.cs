using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>What a Manufacturing read answered for one owner id: the status and, when Ok, the record's identity.</summary>
public readonly record struct ManufacturingOwnerReadV1(ManufacturingApiStatus Status, Guid? Id, Guid? CompanyId);

/// <summary>
/// B-26: adapts a Manufacturing read (the plan or the order read the module already serves, with its own read permission)
/// to the Document Vault owner-link contract. The vault never reads Manufacturing persistence. Ok is readable only for the
/// very record of the context company; NotFound and Denied stay non-disclosing; Conflict stays Conflict (the vault answers
/// it 409); an Ok for another record or company, a provider failure and every other answer is Unavailable. A plan or an
/// order accepts an existing document (a technical drawing, a customer specification) besides an upload.
/// </summary>
public abstract class ManufacturingRecordDocumentOwnerLinkPort(ICompanyContext companyContext, ICurrentUser currentUser)
    : IDocumentOwnerLinkPortV1
{
    public abstract string Kind { get; }

    public bool AcceptsExistingDocumentLinks => true;

    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ownerId == Guid.Empty || !IsCurrent())
        {
            return DocumentOwnerAccessV1.Denied;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            var read = await ReadAsync(ownerId, cancellationToken);
            if (!IsCurrent() || companyContext.CompanyId != companyId
                || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
            {
                return DocumentOwnerAccessV1.Denied;
            }

            return read.Status switch
            {
                ManufacturingApiStatus.Ok when read.Id == ownerId && read.CompanyId == companyId => DocumentOwnerAccessV1.Readable,
                ManufacturingApiStatus.NotFound => DocumentOwnerAccessV1.NotFound,
                ManufacturingApiStatus.Denied => DocumentOwnerAccessV1.Denied,
                ManufacturingApiStatus.Conflict => DocumentOwnerAccessV1.Conflict,
                _ => DocumentOwnerAccessV1.Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return DocumentOwnerAccessV1.Unavailable;
        }
    }

    /// <summary>The Manufacturing read of one record in the current company and actor context.</summary>
    protected abstract Task<ManufacturingOwnerReadV1> ReadAsync(Guid ownerId, CancellationToken cancellationToken);

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}

/// <summary>B-26: <c>manufacturing.plan</c> through <see cref="ManufacturingCommandService.GetPlanAsync"/>.</summary>
public sealed class ManufacturingPlanDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ManufacturingCommandService manufacturing) : ManufacturingRecordDocumentOwnerLinkPort(companyContext, currentUser)
{
    public override string Kind => DocumentOwnerLinkKindsV1.ManufacturingPlan;

    protected override async Task<ManufacturingOwnerReadV1> ReadAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var result = await manufacturing.GetPlanAsync(ownerId, cancellationToken);
        return new ManufacturingOwnerReadV1(result.Status, result.Resource?.PlanId, result.Resource?.CompanyId);
    }
}

/// <summary>B-26: <c>manufacturing.order</c> through <see cref="ManufacturingCommandService.GetOrderAsync"/>.</summary>
public sealed class ManufacturingOrderDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ManufacturingCommandService manufacturing) : ManufacturingRecordDocumentOwnerLinkPort(companyContext, currentUser)
{
    public override string Kind => DocumentOwnerLinkKindsV1.ManufacturingOrder;

    protected override async Task<ManufacturingOwnerReadV1> ReadAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var result = await manufacturing.GetOrderAsync(ownerId, cancellationToken);
        return new ManufacturingOwnerReadV1(result.Status, result.Resource?.OrderId, result.Resource?.CompanyId);
    }
}
