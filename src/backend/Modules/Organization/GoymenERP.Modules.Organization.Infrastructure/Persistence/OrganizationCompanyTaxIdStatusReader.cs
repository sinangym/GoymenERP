using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// The module read of <see cref="IOrganizationCompanyTaxIdStatusReaderV1"/> (KIM-D35): one no-tracking read of the
/// resolved company's own row. The company is the context's company and nothing else; there is no permission scope, by
/// design. A stored VKN means <see cref="OrganizationCompanyTaxIdStatusV1.Entered"/>; no VKN means "VKN girilmemiş".
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
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || companyId != companyContext.CompanyId)
        {
            throw new OrganizationCompanyContextMismatchException();
        }

        var company = await dbContext.Companies
            .AsNoTracking()
            .Where(candidate => candidate.Id == companyId)
            .Select(candidate => new { candidate.Id, candidate.Vkn })
            .SingleOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(company.Vkn)
            ? new OrganizationCompanyTaxIdStateV1(company.Id, OrganizationCompanyTaxIdStatusV1.NotEntered, null)
            : new OrganizationCompanyTaxIdStateV1(company.Id, OrganizationCompanyTaxIdStatusV1.Entered, company.Vkn);
    }
}
