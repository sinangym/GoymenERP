using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// The module read of <see cref="IOrganizationCompanyTaxIdStatusReaderV1"/> (KIM-D35): one no-tracking read of the
/// resolved company's own row. The company is the context's company and nothing else; there is no permission scope, by
/// design. A pending KIM-D48 clear that still names the current VKN is an unapproved old value, not an entered fiscal
/// identity. A stale or applied correction never hides the company's subsequently approved VKN.
/// </summary>
internal sealed class OrganizationCompanyTaxIdStatusReader(
    OrganizationDbContext dbContext,
    ICompanyContext companyContext)
    : IOrganizationCompanyTaxIdStatusReaderV1
{
    public async Task<OrganizationCompanyTaxIdStateV1?> ReadAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(companyId);

        var company = await dbContext.Companies
            .AsNoTracking()
            .Where(candidate => candidate.Id == companyId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Vkn,
                HasPendingCurrentClear = dbContext.CompanyTaxIdCorrections.AsNoTracking().Any(correction =>
                    correction.CompanyId == candidate.Id
                    && correction.Status == CompanyTaxIdCorrectionStatus.Pending
                    && correction.CorrectedVkn == null
                    && correction.StagedVkn == candidate.Vkn),
            })
            .SingleOrDefaultAsync(cancellationToken);
        RequireCompany(companyId);
        if (company is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(company.Vkn) || company.HasPendingCurrentClear
            ? new OrganizationCompanyTaxIdStateV1(company.Id, OrganizationCompanyTaxIdStatusV1.NotEntered, null)
            : new OrganizationCompanyTaxIdStateV1(company.Id, OrganizationCompanyTaxIdStatusV1.Entered, company.Vkn);
    }

    private void RequireCompany(Guid companyId)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyId != companyContext.CompanyId)
            throw new OrganizationCompanyContextMismatchException();
    }
}
