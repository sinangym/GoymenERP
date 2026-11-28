using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed partial class OrganizationMasterDataService
{
    internal async Task<OrganizationCompanyTaxIdDecisionReadResultV1> ReadTaxIdDecisionAsync(Guid id,
        Func<CancellationToken, Task> consumer, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        token.ThrowIfCancellationRequested();
        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        var actor = currentUser.UserId;
        try
        {
            await TaxIdPurposeFence(companyId, actor, consumer, token);
            if (id == Guid.Empty) return new(OrganizationCompanyTaxIdDecisionReadStatusV1.NotFound, null);
            var row = await dbContext.CompanyTaxIdChangeRequests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, token);
            await TaxIdPurposeFence(companyId, actor, consumer, token);
            if (row is null) return new(OrganizationCompanyTaxIdDecisionReadStatusV1.NotFound, null);
            var company = await dbContext.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId, token);
            await TaxIdPurposeFence(companyId, actor, consumer, token);
            if (company is null || !ValidTaxIdPurposeRow(row) || company.Version < 1)
                return new(OrganizationCompanyTaxIdDecisionReadStatusV1.Unavailable, null);
            if (row.IsRequestedBy(actor!)) return new(OrganizationCompanyTaxIdDecisionReadStatusV1.Denied, null);
            var view = ToChangeView(row);
            var source = TaxIdSourceDigest(row);
            var detail = new OrganizationCompanyTaxIdDecisionDetailV1(companyId, company.Version, company.Vkn, view,
                row.CompanyVersionAtRequest, row.IsCurrentFor(company), source,
                TaxIdPurposeDigest(new { company.Id, company.Version, company.Vkn, Request = view, row.CompanyVersionAtRequest }));
            return new(OrganizationCompanyTaxIdDecisionReadStatusV1.Found, detail);
        }
        catch (TaxIdPurposeDenied) { return new(OrganizationCompanyTaxIdDecisionReadStatusV1.Denied, null); }
    }

    private async Task TaxIdPurposeFence(Guid companyId, string? actor, Func<CancellationToken, Task> consumer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Same()) throw new TaxIdPurposeDenied();
        await consumer(token);
        token.ThrowIfCancellationRequested();
        if (!Same()) throw new TaxIdPurposeDenied();
        var scope = await authorityRevalidationService.RevalidatePermissionScopeAsync(
            OrganizationCompanyTaxIdPermissionsV1.Approve, companyId, token);
        token.ThrowIfCancellationRequested();
        if (scope is null || !IsCompanyWideGrant(scope) || !Same()) throw new TaxIdPurposeDenied();
        bool Same() => companyId != Guid.Empty && currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(actor)
            && currentUser.UserId == actor && companyContext.IsResolved && companyContext.CompanyId == companyId
            && currentUser.CanAccessCompany(companyId);
    }
    private sealed class TaxIdPurposeDenied : UnauthorizedAccessException;
    private static string TaxIdPurposeDigest<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string TaxIdSourceDigest(CompanyTaxIdChangeRequest row) => TaxIdPurposeDigest(new
    { row.CompanyId, row.Id, row.CurrentVkn, row.ProposedVkn, row.CompanyVersionAtRequest, row.Reason, row.RequestedBy, row.RequestedAtUtc });
    private static bool ValidTaxIdPurposeRow(CompanyTaxIdChangeRequest row) => row.Id != Guid.Empty && row.CompanyId != Guid.Empty
        && row.CompanyVersionAtRequest > 0 && Company.IsCanonicalVkn(row.ProposedVkn) && OrganizationCompanyTaxIdRules.HasValidCheckDigit(row.ProposedVkn)
        && (row.CurrentVkn is null || Company.IsCanonicalVkn(row.CurrentVkn))
        && OrganizationCompanyTaxIdRules.IsValidReason(row.Reason) && row.Reason == row.Reason.Trim()
        && !string.IsNullOrWhiteSpace(row.RequestedBy) && row.RequestedBy == row.RequestedBy.Trim()
        && (row.Status switch
        {
            CompanyTaxIdChangeStatus.Pending => row.Version == 1 && row.DecidedBy is null && row.DecidedAtUtc is null && row.DecisionNote is null,
            CompanyTaxIdChangeStatus.Approved or CompanyTaxIdChangeStatus.Rejected => row.Version == 2
                && !string.IsNullOrWhiteSpace(row.DecidedBy) && row.DecidedBy != row.RequestedBy && row.DecidedAtUtc is not null
                && OrganizationCompanyTaxIdRules.IsValidNote(row.DecisionNote),
            _ => false,
        });
}
