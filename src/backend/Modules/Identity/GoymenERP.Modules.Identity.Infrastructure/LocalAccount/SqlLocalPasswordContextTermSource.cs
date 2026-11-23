using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// KIM-D4 blocklist context: the code and legal name of every defined company (active or passive), read-only from
/// Organization's table. The password policy compares a password against these terms after normalisation.
/// </summary>
internal sealed class SqlLocalPasswordContextTermSource(IdentityDbContext dbContext) : ILocalPasswordContextTermSource
{
    public async Task<IReadOnlyList<string>> GetCompanyTermsAsync(CancellationToken cancellationToken)
    {
        var companies = await dbContext.Companies
            .AsNoTracking()
            .Select(company => new { company.Code, company.LegalName })
            .ToListAsync(cancellationToken);
        return companies
            .SelectMany(company => new[] { company.Code, company.LegalName })
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
