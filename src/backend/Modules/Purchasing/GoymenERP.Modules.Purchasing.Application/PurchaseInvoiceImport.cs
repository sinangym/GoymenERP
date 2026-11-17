using GoymenERP.BuildingBlocks.Multitenancy;
using System.Globalization;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed class PurchaseInvoiceImport(ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation, IPurchaseInvoiceImportReservationStoreV1 reservations,
    IPurchaseInvoiceImportDocumentReadV1 documents, IPurchaseInvoiceFileReaderV1 reader) : IPurchaseInvoiceImportV1
{
    public async Task<PurchaseInvoiceImportReservationPageV1> ListAsync(int pageIndex, CancellationToken token = default)
    {
        var scope = await RequireAsync(token);
        if (pageIndex < 0 || pageIndex > int.MaxValue / 25) throw Invalid();
        var page = await GuardAsync(scope, () => reservations.ListAsync(pageIndex, token), token);
        if (page is null || page.CompanyId != scope.Company || page.PageIndex != pageIndex || page.Items is null
            || page.Items.Count > 25 || page.Items.Select(x => x.Id).Distinct().Count() != page.Items.Count) throw Unavailable();
        foreach (var item in page.Items) Validate(item, scope.Company, item.Id);
        return page;
    }

    public async Task<PurchaseInvoiceImportReservationResultV1> ReserveAsync(PurchasingCommandMetadata metadata, CancellationToken token = default)
    {
        var scope = await RequireAsync(token);
        if (metadata is null || !Identity(metadata.IdempotencyKey, 200) || !Identity(metadata.CorrelationId, 128)
            || metadata.CausationId is { } cause && !Identity(cause, 128)) throw Invalid();
        var envelope = new PurchasingWriteEnvelope(scope.Actor, PurchaseIncomingInvoicePermissionNamesV1.Import, scope.Roles,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent);
        var result = await GuardAsync(scope, () => reservations.ReserveAsync(envelope, ct => FenceAsync(scope, ct), token), token);
        if (result is null || result.Reservation is null) throw Unavailable();
        Validate(result.Reservation, scope.Company, result.Reservation.Id);
        return result;
    }

    public async Task<PurchaseInvoiceImportReservationV1> ReadAsync(Guid importId, CancellationToken token = default)
    {
        var scope = await RequireAsync(token);
        return await FindAsync(scope, importId, token);
    }

    public async Task<PurchaseInvoiceImportPreviewV1> PreviewAsync(Guid importId, PurchaseInvoiceImportPreviewRequestV1 request, CancellationToken token = default)
    {
        var scope = await RequireAsync(token);
        if (request is null || request.Options is null || request.DocumentId == Guid.Empty || request.DocumentVersion < 1
            || request.DocumentSha256 is not { Length: 64 } || !request.DocumentSha256.All(char.IsAsciiHexDigit)
            || !long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected < 1 || request.ExpectedVersion != expected.ToString(CultureInfo.InvariantCulture)) throw Invalid();
        var reservation = await FindAsync(scope, importId, token);
        if (request.ExpectedVersion != reservation.Version) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
        var content = await GuardAsync(scope, () => documents.ReadAsync(importId, request.DocumentId, request.DocumentVersion, token), token);
        if (content is null || content.CompanyId != scope.Company || content.DocumentId != request.DocumentId
            || content.Version != request.DocumentVersion || content.Content is null) throw Unavailable();
        if (!string.Equals(content.Sha256, request.DocumentSha256, StringComparison.OrdinalIgnoreCase))
            throw new PurchasingServiceException(409, "purchasing_invoice_source_changed");
        if (content.Format != request.Options.Format) throw Invalid();
        if (content.Content.Length is 0 or > PurchaseInvoiceFileBoundsV1.MaximumBytes) throw Invalid();
        var result = await GuardAsync(scope, () => Task.FromResult(reader.Read(content.Content, request.Options)), token);
        if (result is null) throw Unavailable();
        if (!string.Equals(result.Sha256, content.Sha256, StringComparison.OrdinalIgnoreCase)) throw Unavailable();
        await FenceAsync(scope, token);
        return new(importId, scope.Company, reservation.Version, content.DocumentId, content.Version, result.Sha256, result.Invoice, result.Issues);
    }

    private async Task<PurchaseInvoiceImportReservationV1> FindAsync(Scope scope, Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        var reservation = await GuardAsync(scope, () => reservations.FindAsync(id, token), token)
            ?? throw new PurchasingServiceException(404, "not_found");
        Validate(reservation, scope.Company, id);
        return reservation;
    }
    private async Task<Scope> RequireAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw new PurchasingServiceException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Forbidden();
        var companyId = company.CompanyId; var actor = user.UserId;
        var permission = await authority.RevalidatePermissionScopeAsync(PurchaseIncomingInvoicePermissionNamesV1.Import, companyId, token);
        if (!permission.IsGranted || !permission.IsCompanyWide || !Same(companyId, actor)) throw Forbidden();
        var price = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersPriceRead, companyId, token);
        if (!price.IsGranted || !price.IsCompanyWide || !Same(companyId, actor)) throw Forbidden();
        return new(companyId, actor, permission.RoleCodes.Order(StringComparer.Ordinal).ToArray());
    }
    private bool Same(Guid companyId, string actor) => company.IsResolved && company.CompanyId == companyId
        && user.IsAuthenticated && user.UserId == actor && user.CanAccessCompany(companyId);
    private async Task FenceAsync(Scope scope, CancellationToken token)
    {
        var current = await RequireAsync(token);
        if (current.Company != scope.Company || current.Actor != scope.Actor) throw Forbidden();
    }
    private async Task<T> GuardAsync<T>(Scope scope, Func<Task<T>> action, CancellationToken token)
    {
        try { var answer = await action(); await FenceAsync(scope, token); return answer; }
        catch (OperationCanceledException) { throw; }
        catch (PurchasingServiceException) { await FenceAsync(scope, token); throw; }
        catch (PurchasingStoreException exception)
        {
            await FenceAsync(scope, token);
            throw exception.Code switch
            {
                PurchasingStoreCodes.SameKeyPayloadMismatch => new PurchasingServiceException(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch),
                PurchasingStoreCodes.ConcurrentChange => new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange),
                PurchasingStoreCodes.CompanyContextMismatch => Forbidden(),
                _ => Unavailable(),
            };
        }
        catch { await FenceAsync(scope, token); throw Unavailable(); }
    }
    private static void Validate(PurchaseInvoiceImportReservationV1 reservation, Guid companyId, Guid id)
    { if (reservation.CompanyId != companyId || reservation.Id != id || id == Guid.Empty || reservation.Version != "1") throw Unavailable(); }
    private static bool Identity(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim()
        && value.Length <= maximum && !value.Any(char.IsControl);
    private static PurchasingServiceException Invalid() => new(400, "validation_failed");
    private static PurchasingServiceException Forbidden() => new(403, "forbidden");
    private static PurchasingServiceException Unavailable() => new(503, "service_unavailable");
    private sealed record Scope(Guid Company, string Actor, IReadOnlyCollection<string> Roles);
}
